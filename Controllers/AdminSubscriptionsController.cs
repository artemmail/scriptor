using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using YandexSpeech.models.DB;
using YandexSpeech.models.DTO;
using YandexSpeech.services.Interface;

namespace YandexSpeech.Controllers
{
    [ApiController]
    [Route("api/admin/subscriptions")]
    [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme, Roles = "Admin")]
    public class AdminSubscriptionsController : ControllerBase
    {
        private readonly MyDbContext _dbContext;
        private readonly ISubscriptionService _subscriptionService;

        public AdminSubscriptionsController(MyDbContext dbContext, ISubscriptionService subscriptionService)
        {
            _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
            _subscriptionService = subscriptionService ?? throw new ArgumentNullException(nameof(subscriptionService));
        }

        [HttpGet]
        public async Task<ActionResult<IReadOnlyList<AdminSubscriptionDto>>> GetSubscriptions(
            [FromQuery] SubscriptionStatus? status = null,
            [FromQuery] int expiringInDays = 0,
            CancellationToken cancellationToken = default)
        {
            var query = _dbContext.UserSubscriptions
                .AsNoTracking()
                .Include(s => s.Plan)
                .Include(s => s.User)
                .AsQueryable();

            if (status.HasValue)
            {
                query = query.Where(s => s.Status == status.Value);
            }

            if (expiringInDays > 0)
            {
                var threshold = DateTime.UtcNow.AddDays(expiringInDays);
                query = query.Where(s => s.EndDate != null && s.EndDate <= threshold && s.Status == SubscriptionStatus.Active);
            }

            var subscriptions = await query
                .OrderByDescending(s => s.StartDate)
                .Take(200)
                .Select(s => new AdminSubscriptionDto
                {
                    Id = s.Id,
                    UserId = s.UserId,
                    UserEmail = s.User != null ? s.User.Email : null,
                    PlanCode = s.Plan != null ? s.Plan.Code : string.Empty,
                    PlanName = s.Plan != null ? s.Plan.Name : string.Empty,
                    Status = s.Status,
                    StartDate = s.StartDate,
                    EndDate = s.EndDate,
                    IsLifetime = s.IsLifetime,
                    AutoRenew = s.AutoRenew,
                    ExternalPaymentId = s.ExternalPaymentId
                })
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            return Ok(subscriptions);
        }

        [HttpPost("manual")]
        public async Task<ActionResult<AdminSubscriptionDto>> CreateManualSubscription(
            [FromBody] ManualSubscriptionPaymentRequest request,
            CancellationToken cancellationToken)
        {
            if (!ModelState.IsValid)
            {
                return ValidationProblem(ModelState);
            }

            var user = await _dbContext.Users
                .FirstOrDefaultAsync(u => u.Id == request.UserId, cancellationToken)
                .ConfigureAwait(false);

            if (user == null)
            {
                return NotFound($"User '{request.UserId}' was not found.");
            }

            var plan = await _dbContext.SubscriptionPlans
                .FirstOrDefaultAsync(p => p.Code == request.PlanCode, cancellationToken)
                .ConfigureAwait(false);

            if (plan == null)
            {
                return NotFound($"Subscription plan '{request.PlanCode}' was not found.");
            }

            var subscription = await _subscriptionService
                .ActivateSubscriptionAsync(user.Id, plan.Id, externalPaymentId: request.Reference, cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            if (request.EndDate.HasValue)
            {
                var endDate = request.EndDate.Value.Kind == DateTimeKind.Unspecified
                    ? DateTime.SpecifyKind(request.EndDate.Value, DateTimeKind.Utc)
                    : request.EndDate.Value.ToUniversalTime();

                subscription.EndDate = endDate;
                subscription.IsLifetime = false;
                subscription.Status = SubscriptionStatus.Active;
                await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            }

            var amount = request.Amount ?? plan.Price;
            var currency = string.IsNullOrWhiteSpace(request.Currency) ? plan.Currency : request.Currency!;
            var paidAt = request.PaidAt?.ToUniversalTime() ?? DateTime.UtcNow;

            var invoice = new SubscriptionInvoice
            {
                Id = Guid.NewGuid(),
                UserSubscriptionId = subscription.Id,
                Amount = amount,
                Currency = currency,
                Status = SubscriptionInvoiceStatus.Paid,
                IssuedAt = paidAt,
                PaidAt = paidAt,
                PaymentProvider = PaymentProvider.Manual.ToString(),
                ExternalInvoiceId = request.Reference,
                Payload = request.Comment
            };

            _dbContext.SubscriptionInvoices.Add(invoice);
            await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            var dto = new AdminSubscriptionDto
            {
                Id = subscription.Id,
                UserId = subscription.UserId,
                UserEmail = user.Email,
                PlanCode = plan.Code,
                PlanName = plan.Name,
                Status = subscription.Status,
                StartDate = subscription.StartDate,
                EndDate = subscription.EndDate,
                IsLifetime = subscription.IsLifetime,
                AutoRenew = subscription.AutoRenew,
                ExternalPaymentId = subscription.ExternalPaymentId
            };

            return CreatedAtAction(nameof(GetSubscriptions), null, dto);
        }

        [HttpPost("{subscriptionId:guid}/cancel")]
        public async Task<IActionResult> CancelSubscription(Guid subscriptionId, CancellationToken cancellationToken)
        {
            await _subscriptionService.CancelSubscriptionAsync(subscriptionId, cancellationToken).ConfigureAwait(false);
            return NoContent();
        }

        [HttpPost("{subscriptionId:guid}/extend")]
        public async Task<ActionResult<AdminSubscriptionDto>> ExtendSubscription(
            Guid subscriptionId,
            [FromBody] ExtendSubscriptionRequest request,
            CancellationToken cancellationToken)
        {
            if (request?.EndDate == null)
            {
                return BadRequest("Не указана новая дата окончания подписки.");
            }

            var subscription = await _dbContext.UserSubscriptions
                .Include(s => s.Plan)
                .Include(s => s.User)
                .FirstOrDefaultAsync(s => s.Id == subscriptionId, cancellationToken)
                .ConfigureAwait(false);

            if (subscription == null)
            {
                return NotFound();
            }

            var newEndDate = request.EndDate.Value.Kind == DateTimeKind.Unspecified
                ? DateTime.SpecifyKind(request.EndDate.Value, DateTimeKind.Utc)
                : request.EndDate.Value.ToUniversalTime();

            subscription.EndDate = newEndDate;
            subscription.IsLifetime = false;
            subscription.Status = SubscriptionStatus.Active;
            subscription.CancelledAt = null;

            await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await _subscriptionService.RefreshUserCapabilitiesAsync(subscription.UserId, cancellationToken).ConfigureAwait(false);

            var dto = new AdminSubscriptionDto
            {
                Id = subscription.Id,
                UserId = subscription.UserId,
                UserEmail = subscription.User?.Email,
                PlanCode = subscription.Plan?.Code ?? string.Empty,
                PlanName = subscription.Plan?.Name ?? string.Empty,
                Status = subscription.Status,
                StartDate = subscription.StartDate,
                EndDate = subscription.EndDate,
                IsLifetime = subscription.IsLifetime,
                AutoRenew = subscription.AutoRenew,
                ExternalPaymentId = subscription.ExternalPaymentId
            };

            return Ok(dto);
        }
    }
}
