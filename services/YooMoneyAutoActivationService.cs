using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using YandexSpeech.models.DB;
using YandexSpeech.services.Interface;

namespace YandexSpeech.services
{
    public sealed class YooMoneyAutoActivationService : IYooMoneyAutoActivationService
    {
        private const int HistoryBatchSize = 100;

        private readonly IYooMoneyRepository _yooMoneyRepository;
        private readonly MyDbContext _dbContext;
        private readonly IPaymentOperationApplicationService _paymentOperationApplicationService;
        private readonly ILogger<YooMoneyAutoActivationService> _logger;

        public YooMoneyAutoActivationService(
            IYooMoneyRepository yooMoneyRepository,
            MyDbContext dbContext,
            IPaymentOperationApplicationService paymentOperationApplicationService,
            ILogger<YooMoneyAutoActivationService> logger)
        {
            _yooMoneyRepository = yooMoneyRepository ?? throw new ArgumentNullException(nameof(yooMoneyRepository));
            _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
            _paymentOperationApplicationService = paymentOperationApplicationService ?? throw new ArgumentNullException(nameof(paymentOperationApplicationService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<int> ProcessAsync(CancellationToken cancellationToken = default)
        {
            var operationHistory = await _yooMoneyRepository
                .GetOperationHistoryAsync(0, HistoryBatchSize, cancellationToken)
                .ConfigureAwait(false);

            if (operationHistory == null || operationHistory.Count == 0)
            {
                return 0;
            }

            var successfulOperations = operationHistory
                .Where(IsSuccessfulIncomingOperation)
                .Where(o => !string.IsNullOrWhiteSpace(o.OperationId))
                .OrderBy(o => o.DateTime ?? DateTime.MinValue)
                .ToList();

            if (successfulOperations.Count == 0)
            {
                return 0;
            }

            var externalOperationIds = successfulOperations
                .Select(o => o.OperationId!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var alreadyAppliedIds = await _dbContext.PaymentOperations
                .AsNoTracking()
                .Where(p => p.Provider == PaymentProvider.YooMoney
                    && p.ExternalOperationId != null
                    && externalOperationIds.Contains(p.ExternalOperationId))
                .Select(p => p.ExternalOperationId!)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            var appliedSet = new HashSet<string>(alreadyAppliedIds, StringComparer.OrdinalIgnoreCase);
            var appliedCount = 0;

            foreach (var externalOperation in successfulOperations)
            {
                var externalOperationId = externalOperation.OperationId!;
                if (appliedSet.Contains(externalOperationId))
                {
                    continue;
                }

                try
                {
                    var operationDetails = await _yooMoneyRepository
                        .GetOperationDetailsAsync(externalOperationId, cancellationToken)
                        .ConfigureAwait(false);

                    var label = ExtractLabel(operationDetails?.AdditionalData)
                        ?? ExtractLabel(externalOperation.AdditionalData);

                    if (!Guid.TryParse(label, out var localOperationId))
                    {
                        _logger.LogDebug(
                            "Skipping YooMoney operation {ExternalOperationId} because its label is not a local GUID.",
                            externalOperationId);
                        continue;
                    }

                    var localOperation = await _dbContext.PaymentOperations
                        .AsNoTracking()
                        .FirstOrDefaultAsync(
                            p => p.Id == localOperationId && p.Provider == PaymentProvider.YooMoney,
                            cancellationToken)
                        .ConfigureAwait(false);

                    if (localOperation == null)
                    {
                        _logger.LogDebug(
                            "Skipping YooMoney operation {ExternalOperationId} because local operation {LocalOperationId} was not found.",
                            externalOperationId,
                            localOperationId);
                        continue;
                    }

                    if (localOperation.Status == PaymentOperationStatus.Succeeded)
                    {
                        appliedSet.Add(externalOperationId);
                        continue;
                    }

                    if (localOperation.Status == PaymentOperationStatus.Failed
                        || localOperation.Status == PaymentOperationStatus.Cancelled)
                    {
                        _logger.LogDebug(
                            "Skipping YooMoney operation {ExternalOperationId} because local operation {LocalOperationId} has status {Status}.",
                            externalOperationId,
                            localOperationId,
                            localOperation.Status);
                        continue;
                    }

                    var paymentPayload = PaymentOperationPayloadSerializer.Deserialize(localOperation.Payload);
                    if (!string.Equals(paymentPayload?.Type, PaymentOperationPayloadTypes.Subscription, StringComparison.OrdinalIgnoreCase))
                    {
                        _logger.LogDebug(
                            "Skipping YooMoney operation {ExternalOperationId} because local operation {LocalOperationId} is not a subscription payment.",
                            externalOperationId,
                            localOperationId);
                        continue;
                    }

                    if (externalOperation.Amount.HasValue
                        && localOperation.Amount > 0m
                        && Math.Abs(localOperation.Amount - externalOperation.Amount.Value) > 0.01m)
                    {
                        _logger.LogWarning(
                            "Skipping YooMoney operation {ExternalOperationId} because amount {ActualAmount} does not match expected amount {ExpectedAmount} for local operation {LocalOperationId}.",
                            externalOperationId,
                            externalOperation.Amount,
                            localOperation.Amount,
                            localOperationId);
                        continue;
                    }

                    var externalPayloadSource = (object?)operationDetails ?? externalOperation;
                    var externalPayload = JsonConvert.SerializeObject(externalPayloadSource);
                    var updatedOperation = await _paymentOperationApplicationService
                        .ApplyAsync(localOperationId, externalOperationId, externalPayload, cancellationToken)
                        .ConfigureAwait(false);

                    if (updatedOperation?.Status == PaymentOperationStatus.Succeeded)
                    {
                        appliedCount++;
                        appliedSet.Add(externalOperationId);
                    }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (InvalidOperationException ex)
                {
                    _logger.LogWarning(
                        ex,
                        "Failed to auto-apply YooMoney operation {ExternalOperationId}.",
                        externalOperationId);
                }
                catch (Exception ex)
                {
                    _logger.LogError(
                        ex,
                        "Unexpected error while processing YooMoney operation {ExternalOperationId}.",
                        externalOperationId);
                }
            }

            if (appliedCount > 0)
            {
                _logger.LogInformation("Auto-applied {Count} YooMoney subscription payment(s).", appliedCount);
            }

            return appliedCount;
        }

        private static bool IsSuccessfulIncomingOperation(OperationHistory operation)
        {
            if (operation == null)
            {
                return false;
            }

            if (!string.Equals(operation.Status, "success", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (!operation.Amount.HasValue || operation.Amount.Value <= 0m)
            {
                return false;
            }

            var direction = ExtractAdditionalDataString(operation.AdditionalData, "direction");
            if (!string.IsNullOrWhiteSpace(direction)
                && !string.Equals(direction, "in", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return true;
        }

        private static string? ExtractLabel(IDictionary<string, JToken>? additionalData)
        {
            return ExtractAdditionalDataString(additionalData, "label");
        }

        private static string? ExtractAdditionalDataString(IDictionary<string, JToken>? additionalData, string key)
        {
            if (additionalData == null || additionalData.Count == 0)
            {
                return null;
            }

            foreach (var pair in additionalData)
            {
                if (!string.Equals(pair.Key, key, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                return pair.Value.Type == JTokenType.Null || pair.Value.Type == JTokenType.Undefined
                    ? null
                    : pair.Value.ToString();
            }

            return null;
        }
    }
}
