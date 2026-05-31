using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json.Linq;
using YandexSpeech.models.DB;
using YandexSpeech.models.DTO;
using YandexSpeech.services;
using YandexSpeech.services.Interface;

namespace YandexSpeech.Controllers
{
    [ApiController]
    [Route("api/admin/yoomoney")]
    [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme, Roles = "Admin")]
    public class AdminYooMoneyController : ControllerBase
    {
        private readonly IYooMoneyRepository _yooMoneyRepository;
        private readonly MyDbContext _dbContext;
        private readonly IPaymentOperationApplicationService _paymentOperationApplicationService;

        public AdminYooMoneyController(
            IYooMoneyRepository yooMoneyRepository,
            MyDbContext dbContext,
            IPaymentOperationApplicationService paymentOperationApplicationService)
        {
            _yooMoneyRepository = yooMoneyRepository ?? throw new ArgumentNullException(nameof(yooMoneyRepository));
            _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
            _paymentOperationApplicationService = paymentOperationApplicationService ?? throw new ArgumentNullException(nameof(paymentOperationApplicationService));
        }

        [HttpGet("oauth/authorize")]
        public async Task<IActionResult> BeginAuthorization(CancellationToken cancellationToken = default)
        {
            try
            {
                var response = await _yooMoneyRepository
                    .AuthorizeAsync(cancellationToken)
                    .ConfigureAwait(false);

                return FormatResponse(response);
            }
            catch (Exception ex)
            {
                Response.StatusCode = StatusCodes.Status502BadGateway;
                return Content(
                    $"Не удалось отправить запрос авторизации в YooMoney: {ex.Message}",
                    "text/plain",
                    Encoding.UTF8);
            }
        }

        [AllowAnonymous]
        [HttpGet("~/admin/yandexget")]
        public async Task<IActionResult> CompleteAuthorization(
            [FromQuery(Name = "code")] string? authorizationCode,
            [FromQuery(Name = "error")] string? error,
            [FromQuery(Name = "error_description")] string? errorDescription,
            CancellationToken cancellationToken = default)
        {
            if (!string.IsNullOrWhiteSpace(error) || !string.IsNullOrWhiteSpace(errorDescription))
            {
                Response.StatusCode = StatusCodes.Status400BadRequest;
                return Content(
                    $"Авторизация YooMoney завершилась ошибкой: {errorDescription ?? error}",
                    "text/plain",
                    Encoding.UTF8);
            }

            if (string.IsNullOrWhiteSpace(authorizationCode))
            {
                Response.StatusCode = StatusCodes.Status400BadRequest;
                return Content(
                    "Отсутствует параметр 'code' в строке запроса.",
                    "text/plain",
                    Encoding.UTF8);
            }

            try
            {
                var response = await _yooMoneyRepository
                    .ExchangeTokenAsync(authorizationCode, cancellationToken)
                    .ConfigureAwait(false);

                return FormatResponse(response);
            }
            catch (Exception ex)
            {
                Response.StatusCode = StatusCodes.Status502BadGateway;
                return Content(
                    $"Не удалось обменять код авторизации на токен YooMoney: {ex.Message}",
                    "text/plain",
                    Encoding.UTF8);
            }
        }

        [HttpGet("operation-history")]
        public async Task<ActionResult<IReadOnlyList<AdminYooMoneyOperationDto>>> GetOperationHistory(
            [FromQuery(Name = "start_record")] int startRecord = 0,
            [FromQuery(Name = "records")] int records = 30,
            CancellationToken cancellationToken = default)
        {
            if (startRecord < 0)
            {
                return BadRequest("start_record must be greater than or equal to zero.");
            }

            if (records < 0)
            {
                return BadRequest("records must be greater than or equal to zero.");
            }

            var operations = await _yooMoneyRepository
                .GetOperationHistoryAsync(startRecord, records, cancellationToken)
                .ConfigureAwait(false);

            if (operations == null || operations.Count == 0)
            {
                return Ok(Array.Empty<AdminYooMoneyOperationDto>());
            }

            var dtos = operations
                .Select(MapOperation)
                .ToList();

            return Ok(dtos);
        }

        [HttpGet("operation-details/{operationId}")]
        public async Task<ActionResult<AdminYooMoneyOperationDetailsDto>> GetOperationDetails(
            string operationId,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(operationId))
            {
                return BadRequest("operationId is required.");
            }

            var operationDetails = await _yooMoneyRepository
                .GetOperationDetailsAsync(operationId, cancellationToken)
                .ConfigureAwait(false);

            if (operationDetails == null)
            {
                return NotFound();
            }

            var dto = MapOperationDetails(operationDetails);
            return Ok(dto);
        }

        [HttpGet("payment-operations/{operationId}")]
        public async Task<ActionResult<AdminPaymentOperationDetailsDto>> GetPaymentOperation(
            string operationId,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(operationId))
            {
                return BadRequest("operationId is required.");
            }

            if (!Guid.TryParse(operationId, out var parsedOperationId))
            {
                return BadRequest("operationId must be a valid GUID.");
            }

            var operation = await _dbContext.PaymentOperations
                .AsNoTracking()
                .Include(o => o.User)
                .FirstOrDefaultAsync(o => o.Id == parsedOperationId, cancellationToken)
                .ConfigureAwait(false);

            if (operation == null)
            {
                return NotFound();
            }

            var dto = MapPaymentOperation(operation);
            return Ok(dto);
        }

        [HttpPost("payment-operations/{operationId}/apply")]
        public async Task<ActionResult<AdminPaymentOperationDetailsDto>> ApplyPaymentOperation(
            string operationId,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(operationId))
            {
                return BadRequest("operationId is required.");
            }

            if (!Guid.TryParse(operationId, out var parsedOperationId))
            {
                return BadRequest("operationId must be a valid GUID.");
            }

            try
            {
                var operation = await _paymentOperationApplicationService
                    .ApplyAsync(parsedOperationId, cancellationToken: cancellationToken)
                    .ConfigureAwait(false);

                if (operation == null)
                {
                    return NotFound();
                }

                var dto = MapPaymentOperation(operation);
                return Ok(dto);
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(ex.Message);
            }
        }

        private static AdminYooMoneyOperationDto MapOperation(OperationHistory operation)
        {
            if (operation == null)
            {
                throw new ArgumentNullException(nameof(operation));
            }

            return new AdminYooMoneyOperationDto
            {
                OperationId = operation.OperationId,
                Title = operation.Title,
                Amount = operation.Amount,
                DateTime = operation.DateTime,
                Status = operation.Status,
                AdditionalData = ConvertAdditionalData(operation.AdditionalData)
            };
        }

        private static AdminYooMoneyOperationDetailsDto MapOperationDetails(OperationDetails details)
        {
            if (details == null)
            {
                throw new ArgumentNullException(nameof(details));
            }

            return new AdminYooMoneyOperationDetailsDto
            {
                OperationId = details.OperationId,
                Title = details.Title,
                Amount = details.Amount,
                DateTime = details.DateTime,
                Status = details.Status,
                AdditionalData = ConvertAdditionalData(details.AdditionalData)
            };
        }

        private static AdminPaymentOperationDetailsDto MapPaymentOperation(PaymentOperation operation)
        {
            if (operation == null)
            {
                throw new ArgumentNullException(nameof(operation));
            }

            return new AdminPaymentOperationDetailsDto
            {
                Id = operation.Id,
                UserId = operation.UserId,
                UserEmail = operation.User?.Email,
                UserDisplayName = operation.User?.DisplayName,
                Provider = operation.Provider.ToString(),
                Status = operation.Status.ToString(),
                Applied = operation.Status == PaymentOperationStatus.Succeeded,
                Amount = operation.Amount,
                Currency = operation.Currency,
                RequestedAt = operation.RequestedAt,
                CompletedAt = operation.CompletedAt,
                Payload = operation.Payload,
                ExternalOperationId = operation.ExternalOperationId,
                WalletTransactionId = operation.WalletTransactionId
            };
        }

        private static IDictionary<string, object?>? ConvertAdditionalData(IDictionary<string, JToken>? additionalData)
        {
            if (additionalData == null || additionalData.Count == 0)
            {
                return null;
            }

            return additionalData.ToDictionary(
                pair => pair.Key,
                pair => ConvertJToken(pair.Value));
        }

        private static object? ConvertJToken(JToken token)
        {
            return token.Type switch
            {
                JTokenType.Object => token.Children<JProperty>()
                    .ToDictionary(p => p.Name, p => ConvertJToken(p.Value)),
                JTokenType.Array => token.Children()
                    .Select(ConvertJToken)
                    .ToList(),
                JTokenType.Null => null,
                JTokenType.Undefined => null,
                _ => (token as JValue)?.Value ?? token.ToString()
            };
        }

        private ContentResult FormatResponse(string payload)
        {
            if (string.IsNullOrEmpty(payload))
            {
                return Content(string.Empty, "text/plain", Encoding.UTF8);
            }

            if (TryFormatJson(payload, out var formattedJson))
            {
                return Content(formattedJson, "application/json", Encoding.UTF8);
            }

            return Content(payload, "text/plain", Encoding.UTF8);
        }

        private static bool TryFormatJson(string payload, out string formattedJson)
        {
            try
            {
                using var document = JsonDocument.Parse(payload);
                formattedJson = JsonSerializer.Serialize(
                    document.RootElement,
                    new JsonSerializerOptions { WriteIndented = true });
                return true;
            }
            catch (JsonException)
            {
                formattedJson = payload;
                return false;
            }
        }

    }
}
