using System;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.AspNetCore.Http;
using YandexSpeech.models.DB;
using YandexSpeech.models.DTO;
using YandexSpeech.services;
using YandexSpeech.services.Interface;
using System.Security.Claims;

namespace YandexSpeech.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class PaymentsController : ControllerBase
    {
        private const string SubscriptionPayloadType = "subscription";
        private const string WalletPayloadType = "wallet";

        private readonly MyDbContext _dbContext;
        private readonly IPaymentGatewayService _paymentGatewayService;
        private readonly ISubscriptionService _subscriptionService;
        private readonly IWalletService _walletService;
        private readonly IOptions<YooMoneyOptions> _yooMoneyOptions;
        private readonly ILogger<PaymentsController> _logger;

        public PaymentsController(
            MyDbContext dbContext,
            IPaymentGatewayService paymentGatewayService,
            ISubscriptionService subscriptionService,
            IWalletService walletService,
            IOptions<YooMoneyOptions> yooMoneyOptions,
            ILogger<PaymentsController> logger)
        {
            _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
            _paymentGatewayService = paymentGatewayService ?? throw new ArgumentNullException(nameof(paymentGatewayService));
            _subscriptionService = subscriptionService ?? throw new ArgumentNullException(nameof(subscriptionService));
            _walletService = walletService ?? throw new ArgumentNullException(nameof(walletService));
            _yooMoneyOptions = yooMoneyOptions ?? throw new ArgumentNullException(nameof(yooMoneyOptions));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        [HttpGet("plans")]
        public async Task<ActionResult<IReadOnlyList<SubscriptionPlanDto>>> GetPlans(CancellationToken cancellationToken)
        {
            var plans = await _subscriptionService.GetPlansAsync(includeInactive: false, cancellationToken)
                .ConfigureAwait(false);

            var response = new List<SubscriptionPlanDto>(plans.Count);
            foreach (var plan in plans)
            {
                response.Add(new SubscriptionPlanDto
                {
                    Id = plan.Id,
                    Code = plan.Code,
                    Name = plan.Name,
                    Description = plan.Description,
                    BillingPeriod = plan.BillingPeriod,
                    Price = plan.Price,
                    Currency = plan.Currency,
                    MaxRecognitionsPerDay = plan.MaxRecognitionsPerDay,
                    CanHideCaptions = plan.CanHideCaptions,
                    IsUnlimitedRecognitions = plan.IsUnlimitedRecognitions,
                    IsLifetime = plan.BillingPeriod == SubscriptionBillingPeriod.Lifetime
                });
            }

            return Ok(response);
        }

        [HttpGet("wallet")]
        public async Task<ActionResult<WalletBalanceDto>> GetWallet(CancellationToken cancellationToken)
        {
            var userId = GetUserId();
            var wallet = await _walletService.GetOrCreateWalletAsync(userId, cancellationToken).ConfigureAwait(false);

            return Ok(new WalletBalanceDto
            {
                Balance = wallet.Balance,
                Currency = wallet.Currency
            });
        }

        [HttpPost("subscriptions")]
        public async Task<ActionResult<PaymentInitResponse>> CreateSubscriptionPayment(
            [FromBody] CreateSubscriptionPaymentRequest request,
            CancellationToken cancellationToken)
        {
            if (!ModelState.IsValid)
            {
                return ValidationProblem(ModelState);
            }

            var plan = await _dbContext.SubscriptionPlans
                .AsNoTracking()
                .FirstOrDefaultAsync(p => p.Code == request.PlanCode && p.IsActive, cancellationToken)
                .ConfigureAwait(false);

            if (plan == null)
            {
                return NotFound($"Subscription plan '{request.PlanCode}' was not found.");
            }

            if (plan.Price <= 0)
            {
                return BadRequest("Подписка с нулевой стоимостью не требует оплаты.");
            }

            var userId = GetUserId();

            var payload = SerializePayload(new PaymentPayload
            {
                Type = SubscriptionPayloadType,
                PlanId = plan.Id
            });

            var operation = await _paymentGatewayService
                .RegisterOperationAsync(userId, plan.Price, plan.Currency, PaymentProvider.YooMoney, payload, cancellationToken)
                .ConfigureAwait(false);

            var paymentUrl = BuildQuickpayUrl(operation.Id, plan.Price, plan.Currency, plan.Name);

            return Ok(new PaymentInitResponse
            {
                OperationId = operation.Id,
                PaymentUrl = paymentUrl
            });
        }

        [HttpPost("wallet/deposit")]
        public async Task<ActionResult<PaymentInitResponse>> CreateWalletDeposit(
            [FromBody] CreateWalletDepositRequest request,
            CancellationToken cancellationToken)
        {
            if (!ModelState.IsValid)
            {
                return ValidationProblem(ModelState);
            }

            if (request.Amount <= 0)
            {
                return BadRequest("Сумма пополнения должна быть больше нуля.");
            }

            var userId = GetUserId();

            var payload = SerializePayload(new PaymentPayload
            {
                Type = WalletPayloadType,
                Comment = string.IsNullOrWhiteSpace(request.Comment)
                    ? "Пополнение счёта для распознавания митингов"
                    : request.Comment
            });

            var operation = await _paymentGatewayService
                .RegisterOperationAsync(userId, request.Amount, "RUB", PaymentProvider.YooMoney, payload, cancellationToken)
                .ConfigureAwait(false);

            var paymentUrl = BuildQuickpayUrl(operation.Id, request.Amount, "RUB", "Пополнение счёта");

            return Ok(new PaymentInitResponse
            {
                OperationId = operation.Id,
                PaymentUrl = paymentUrl
            });
        }

        [AllowAnonymous]
        [HttpPost("notifications/yoomoney")]
        [Consumes("application/x-www-form-urlencoded")]
        public async Task<IActionResult> HandleYooMoneyNotification([FromForm] YooMoneyNotification notification, CancellationToken cancellationToken)
        {
            if (notification == null)
            {
                return BadRequest("Пустое уведомление");
            }

            if (!Guid.TryParse(notification.Label, out var operationId))
            {
                _logger.LogWarning("Получено уведомление YooMoney без корректного идентификатора операции: {Label}", notification.Label);
                return BadRequest("Некорректный идентификатор операции");
            }

            if (!ValidateSignature(notification))
            {
                _logger.LogWarning("Получено уведомление YooMoney с некорректной подписью: {OperationId}", operationId);
                return BadRequest("Некорректная подпись уведомления");
            }

            var operation = await _paymentGatewayService.GetOperationAsync(operationId, cancellationToken).ConfigureAwait(false);
            if (operation == null)
            {
                _logger.LogWarning("Операция {OperationId} не найдена при обработке уведомления YooMoney", operationId);
                return NotFound();
            }

            if (operation.Status == PaymentOperationStatus.Succeeded)
            {
                return Ok();
            }

            var payload = DeserializePayload(operation.Payload);
            if (payload == null)
            {
                _logger.LogError("Не удалось определить тип операции {OperationId}", operationId);
                return BadRequest("Не удалось определить тип операции");
            }

            var amount = notification.Amount ?? 0m;
            if (amount <= 0m)
            {
                _logger.LogWarning("Уведомление YooMoney {OperationId} содержит некорректную сумму {Amount}", operationId, amount);
                return BadRequest("Некорректная сумма");
            }

            if (operation.Amount > 0 && Math.Abs(operation.Amount - amount) > 0.01m)
            {
                _logger.LogWarning("Сумма уведомления YooMoney {Amount} не совпадает с ожидаемой {Expected} для операции {OperationId}", amount, operation.Amount, operationId);
                return BadRequest("Некорректная сумма операции");
            }

            try
            {
                switch (payload.Type)
                {
                    case SubscriptionPayloadType:
                        await HandleSubscriptionPaymentAsync(operation, payload, notification, cancellationToken).ConfigureAwait(false);
                        break;
                    case WalletPayloadType:
                        await HandleWalletDepositAsync(operation, payload, notification, cancellationToken).ConfigureAwait(false);
                        break;
                    default:
                        _logger.LogError("Неизвестный тип операции {Type} для {OperationId}", payload.Type, operationId);
                        return BadRequest("Неизвестный тип операции");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при обработке уведомления YooMoney для операции {OperationId}", operationId);
                return StatusCode(StatusCodes.Status500InternalServerError, "Ошибка обработки уведомления");
            }

            return Ok();
        }

        private async Task HandleSubscriptionPaymentAsync(
            PaymentOperation operation,
            PaymentPayload payload,
            YooMoneyNotification notification,
            CancellationToken cancellationToken)
        {
            if (payload.PlanId == null)
            {
                throw new InvalidOperationException("Отсутствует идентификатор плана подписки.");
            }

            await _subscriptionService
                .ActivateSubscriptionAsync(operation.UserId, payload.PlanId.Value, externalPaymentId: notification.OperationId, cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            await _paymentGatewayService
                .MarkSucceededAsync(operation.Id, notification.OperationId ?? string.Empty, cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            _logger.LogInformation("Активирована подписка {PlanId} для пользователя {UserId} по операции {OperationId}", payload.PlanId, operation.UserId, operation.Id);
        }

        private async Task HandleWalletDepositAsync(
            PaymentOperation operation,
            PaymentPayload payload,
            YooMoneyNotification notification,
            CancellationToken cancellationToken)
        {
            var comment = payload.Comment ?? "Пополнение счёта";
            var transaction = await _walletService
                .DepositAsync(operation.UserId, notification.Amount ?? operation.Amount, notification.Currency ?? "RUB", relatedEntityId: operation.Id, reference: notification.OperationId, comment: comment, cancellationToken)
                .ConfigureAwait(false);

            await _paymentGatewayService
                .MarkSucceededAsync(operation.Id, notification.OperationId ?? string.Empty, transaction.Id, cancellationToken)
                .ConfigureAwait(false);

            _logger.LogInformation("Пополнен кошелёк пользователя {UserId} на сумму {Amount} по операции {OperationId}", operation.UserId, notification.Amount, operation.Id);
        }

        private string BuildQuickpayUrl(Guid operationId, decimal amount, string currency, string title)
        {
            var options = _yooMoneyOptions.Value;
            if (string.IsNullOrWhiteSpace(options.Receiver))
            {
                throw new InvalidOperationException("В конфигурации не указан кошелёк YooMoney для приема платежей.");
            }

            var baseUrl = "https://yoomoney.ru/quickpay/confirm";
            var amountText = amount.ToString("F2", CultureInfo.InvariantCulture);

            var successUrl = AppendOperation(options.SuccessUrl, operationId.ToString());
            var failUrl = AppendOperation(options.FailUrl, operationId.ToString());

            var query = new Dictionary<string, string?>
            {
                ["receiver"] = options.Receiver,
                ["quickpay-form"] = options.QuickpayForm,
                ["paymentType"] = options.PaymentType,
                ["sum"] = amountText,
                ["label"] = operationId.ToString(),
                ["targets"] = title,
                ["comment"] = title,
                ["successURL"] = successUrl,
                ["failURL"] = failUrl
            };

            var filteredQuery = new Dictionary<string, string?>();
            foreach (var (key, value) in query)
            {
                if (!string.IsNullOrWhiteSpace(value))
                {
                    filteredQuery[key] = value;
                }
            }

            return QueryHelpers.AddQueryString(baseUrl, filteredQuery);
        }

        private static string? AppendOperation(string? url, string operationId)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                return null;
            }

            return QueryHelpers.AddQueryString(url, "operation", operationId);
        }

        private string GetUserId()
        {
            var userId = User.FindFirstValue(System.Security.Claims.ClaimTypes.NameIdentifier);
            return userId ?? throw new InvalidOperationException("Не удалось определить пользователя.");
        }

        private bool ValidateSignature(YooMoneyNotification notification)
        {
            var secret = _yooMoneyOptions.Value.NotificationSecret;
            if (string.IsNullOrWhiteSpace(secret))
            {
                return true; // подпись не настроена
            }

            var builder = new StringBuilder();
            builder.Append(notification.NotificationType ?? string.Empty).Append('&');
            builder.Append(notification.OperationId ?? string.Empty).Append('&');
            builder.Append(notification.Amount?.ToString("F2", CultureInfo.InvariantCulture) ?? string.Empty).Append('&');
            builder.Append(notification.Currency ?? string.Empty).Append('&');
            builder.Append(notification.DateTime ?? string.Empty).Append('&');
            builder.Append(notification.Sender ?? string.Empty).Append('&');
            builder.Append(notification.CodePro ? "true" : "false").Append('&');
            builder.Append(secret).Append('&');
            builder.Append(notification.Label ?? string.Empty);

            using var sha1 = SHA1.Create();
            var data = Encoding.UTF8.GetBytes(builder.ToString());
            var hash = sha1.ComputeHash(data);
            var computed = BitConverter.ToString(hash).Replace("-", string.Empty, StringComparison.Ordinal).ToLowerInvariant();

            return string.Equals(computed, notification.Sha1Hash, StringComparison.OrdinalIgnoreCase);
        }

        private static string SerializePayload(PaymentPayload payload)
        {
            return JsonSerializer.Serialize(payload, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
            });
        }

        private static PaymentPayload? DeserializePayload(string? payload)
        {
            if (string.IsNullOrWhiteSpace(payload))
            {
                return null;
            }

            try
            {
                return JsonSerializer.Deserialize<PaymentPayload>(payload, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });
            }
            catch
            {
                return null;
            }
        }

        private sealed class PaymentPayload
        {
            public string Type { get; set; } = string.Empty;

            public Guid? PlanId { get; set; }

            public string? Comment { get; set; }
        }

        public sealed class YooMoneyNotification
        {
            [FromForm(Name = "notification_type")]
            public string? NotificationType { get; set; }

            [FromForm(Name = "operation_id")]
            public string? OperationId { get; set; }

            [FromForm(Name = "amount")]
            public decimal? Amount { get; set; }

            [FromForm(Name = "currency")]
            public string? Currency { get; set; }

            [FromForm(Name = "datetime")]
            public string? DateTime { get; set; }

            [FromForm(Name = "sender")]
            public string? Sender { get; set; }

            [FromForm(Name = "codepro")]
            public bool CodePro { get; set; }

            [FromForm(Name = "label")]
            public string? Label { get; set; }

            [FromForm(Name = "sha1_hash")]
            public string? Sha1Hash { get; set; }
        }
    }
}
