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
            var pendingOperations = await _dbContext.PaymentOperations
                .AsNoTracking()
                .Where(p => p.Provider == PaymentProvider.YooMoney && p.Status == PaymentOperationStatus.Pending)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
            if (pendingOperations.Count == 0)
                return 0;

            var pendingById = pendingOperations.ToDictionary(p => p.Id);
            var recentHistory = await _yooMoneyRepository
                .GetOperationHistoryAsync(0, HistoryBatchSize, cancellationToken)
                .ConfigureAwait(false);
            var candidates = (recentHistory ?? Array.Empty<OperationHistory>())
                .Where(IsSuccessfulIncomingOperation)
                .Where(o => HasPendingLabel(o, pendingById))
                .ToList();

            var foundLabels = candidates
                .Select(o => Guid.Parse(ExtractLabel(o.AdditionalData)!))
                .ToHashSet();
            foreach (var pending in pendingOperations.Where(p => !foundLabels.Contains(p.Id)))
            {
                try
                {
                    var labeledHistory = await _yooMoneyRepository
                        .GetOperationHistoryByLabelAsync(pending.Id.ToString("D"), cancellationToken)
                        .ConfigureAwait(false);
                    candidates.AddRange((labeledHistory ?? Array.Empty<OperationHistory>())
                        .Where(IsSuccessfulIncomingOperation)
                        .Where(o => HasPendingLabel(o, pendingById)));
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to check YooMoney label for payment {OperationId}.", pending.Id);
                }
            }

            var successfulOperations = candidates
                .Where(o => !string.IsNullOrWhiteSpace(o.OperationId))
                .GroupBy(o => o.OperationId!, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .OrderBy(o => o.DateTime ?? DateTime.MinValue)
                .ToList();
            if (successfulOperations.Count == 0)
                return 0;

            var externalOperationIds = successfulOperations.Select(o => o.OperationId!).ToList();
            var alreadyAppliedIds = await _dbContext.PaymentOperations.AsNoTracking()
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

                    var historyLabel = ExtractLabel(externalOperation.AdditionalData);
                    if (!Guid.TryParse(historyLabel, out var localOperationId)
                        || !pendingById.TryGetValue(localOperationId, out var localOperation))
                        continue;

                    if (operationDetails == null
                        || !string.Equals(operationDetails.OperationId, externalOperationId, StringComparison.OrdinalIgnoreCase)
                        || !string.Equals(operationDetails.Status, "success", StringComparison.OrdinalIgnoreCase)
                        || !string.Equals(ExtractAdditionalDataString(operationDetails.AdditionalData, "direction"), "in", StringComparison.OrdinalIgnoreCase)
                        || !Guid.TryParse(ExtractLabel(operationDetails.AdditionalData), out var verifiedLabel)
                        || verifiedLabel != localOperationId)
                    {
                        _logger.LogWarning("YooMoney details did not verify payment {ExternalOperationId}.", externalOperationId);
                        continue;
                    }

                    var paymentPayload = PaymentOperationPayloadSerializer.Deserialize(localOperation.Payload);
                    if (paymentPayload?.Type is not (PaymentOperationPayloadTypes.Subscription or PaymentOperationPayloadTypes.Wallet)
                        || !string.Equals(localOperation.Currency, "RUB", StringComparison.OrdinalIgnoreCase))
                    {
                        _logger.LogWarning("Skipping YooMoney payment {OperationId} with unsupported payload or currency.", localOperationId);
                        continue;
                    }

                    if (localOperation.Amount <= 0m
                        || !operationDetails.Amount.HasValue
                        || Math.Abs(localOperation.Amount - operationDetails.Amount.Value) > 0.01m)
                    {
                        _logger.LogWarning(
                            "Skipping YooMoney operation {ExternalOperationId} because amount {ActualAmount} does not match expected amount {ExpectedAmount} for local operation {LocalOperationId}.",
                            externalOperationId,
                            operationDetails.Amount,
                            localOperation.Amount,
                            localOperationId);
                        continue;
                    }

                    var externalPayload = JsonConvert.SerializeObject(operationDetails);
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
                _logger.LogInformation("Auto-applied {Count} YooMoney payment(s).", appliedCount);
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
            if (!string.Equals(direction, "in", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return true;
        }

        private static bool HasPendingLabel(
            OperationHistory operation,
            IReadOnlyDictionary<Guid, PaymentOperation> pendingById)
        {
            return Guid.TryParse(ExtractLabel(operation.AdditionalData), out var id)
                && pendingById.ContainsKey(id);
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
