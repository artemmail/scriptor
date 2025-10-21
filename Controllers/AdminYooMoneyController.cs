using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
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

        public AdminYooMoneyController(IYooMoneyRepository yooMoneyRepository, MyDbContext dbContext)
        {
            _yooMoneyRepository = yooMoneyRepository ?? throw new ArgumentNullException(nameof(yooMoneyRepository));
            _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
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
    }
}
