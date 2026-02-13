using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using YandexSpeech.models.DB;
using YandexSpeech.models.DTO;
using YandexSpeech.services.Interface;
using YandexSpeech.services.Options;

namespace YandexSpeech.Controllers
{
    [ApiController]
    [Route("api/admin/users")]
    [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme, Roles = "Admin")]
    public class AdminUsersController : ControllerBase
    {
        private readonly MyDbContext _dbContext;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly RoleManager<IdentityRole> _roleManager;
        private readonly ISubscriptionService _subscriptionService;
        private readonly SubscriptionLimitsOptions _subscriptionLimits;

        public AdminUsersController(
            MyDbContext dbContext,
            UserManager<ApplicationUser> userManager,
            RoleManager<IdentityRole> roleManager,
            ISubscriptionService subscriptionService,
            IOptions<SubscriptionLimitsOptions> subscriptionLimits)
        {
            _dbContext = dbContext;
            _userManager = userManager;
            _roleManager = roleManager;
            _subscriptionService = subscriptionService ?? throw new ArgumentNullException(nameof(subscriptionService));
            _subscriptionLimits = subscriptionLimits?.Value ?? new SubscriptionLimitsOptions();
        }

        [HttpGet]
        public async Task<ActionResult<AdminUsersPageDto>> GetUsers(
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 20,
            [FromQuery] string? filter = null,
            [FromQuery] string? sortBy = null,
            [FromQuery] string? sortOrder = null)
        {
            const int maxPageSize = 100;
            page = Math.Max(page, 1);
            pageSize = Math.Min(Math.Max(pageSize, 1), maxPageSize);

            var usersQuery = _userManager.Users.AsNoTracking();

            if (!string.IsNullOrWhiteSpace(filter))
            {
                var normalized = filter.Trim().ToLower();
                usersQuery = usersQuery.Where(u => u.Email != null && u.Email.ToLower().Contains(normalized));
            }

            var captionCountsQuery = _dbContext.YoutubeCaptionTasks
                .Where(t => t.UserId != null && t.Done)
                .GroupBy(t => t.UserId!)
                .Select(g => new
                {
                    UserId = g.Key,
                    Count = g.Count()
                });

            var baseQuery = usersQuery
                .GroupJoin(
                    captionCountsQuery,
                    user => user.Id,
                    count => count.UserId,
                    (user, counts) => new
                    {
                        User = user,
                        Recognized = counts.Sum(c => (int?)c.Count) ?? 0
                    })
                .Select(x => new AdminUserQueryItem
                {
                    User = x.User,
                    Recognized = x.Recognized
                });

            var totalCount = await baseQuery.CountAsync();

            var normalizedSortBy = (sortBy ?? string.Empty).Trim().ToLowerInvariant();
            var normalizedSortOrder = (sortOrder ?? string.Empty).Trim().ToLowerInvariant();

            var validSortFields = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "email",
                "registeredat",
                "recognizedvideos"
            };

            if (!validSortFields.Contains(normalizedSortBy))
            {
                normalizedSortBy = "recognizedvideos";
            }

            var descending = normalizedSortOrder switch
            {
                "asc" => false,
                "desc" => true,
                _ => normalizedSortBy == "recognizedvideos"
            };

            IOrderedQueryable<AdminUserQueryItem> orderedQuery = normalizedSortBy switch
            {
                "email" => descending
                    ? baseQuery.OrderByDescending(x => x.User.Email)
                    : baseQuery.OrderBy(x => x.User.Email),
                "registeredat" => descending
                    ? baseQuery.OrderByDescending(x => x.User.CreatedAt)
                    : baseQuery.OrderBy(x => x.User.CreatedAt),
                _ => descending
                    ? baseQuery.OrderByDescending(x => x.Recognized)
                    : baseQuery.OrderBy(x => x.Recognized)
            };

            if (!string.Equals(normalizedSortBy, "recognizedvideos", StringComparison.OrdinalIgnoreCase))
            {
                orderedQuery = orderedQuery.ThenByDescending(x => x.Recognized);
            }

            orderedQuery = orderedQuery
                .ThenBy(x => x.User.Email ?? string.Empty)
                .ThenBy(x => x.User.Id);

            var paged = await orderedQuery
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(x => new
                {
                    x.User.Id,
                    x.User.Email,
                    x.User.DisplayName,
                    x.User.CreatedAt,
                    Recognized = x.Recognized
                })
                .ToListAsync();

            var userIds = paged.Select(p => p.Id).ToList();

            var rolesLookup = await _dbContext.UserRoles
                .Where(ur => userIds.Contains(ur.UserId))
                .Join(
                    _dbContext.Roles,
                    ur => ur.RoleId,
                    r => r.Id,
                    (ur, role) => new { ur.UserId, role.Name })
                .GroupBy(x => x.UserId)
                .ToDictionaryAsync(
                    g => g.Key,
                    g => (IReadOnlyCollection<string>)g
                        .Where(x => x.Name != null)
                        .Select(x => x.Name!)
                        .ToList());

            var captionIps = await _dbContext.YoutubeCaptionTasks
                .Where(task =>
                    task.UserId != null &&
                    userIds.Contains(task.UserId) &&
                    task.IP != null &&
                    task.IP != string.Empty)
                .Select(task => new { task.UserId, task.IP })
                .ToListAsync();

            var ipLookup = captionIps
                .GroupBy(x => x.UserId!)
                .ToDictionary(
                    g => g.Key!,
                    g => (IReadOnlyCollection<string>)g
                        .Select(x => x.IP!)
                        .Where(ip => !string.IsNullOrWhiteSpace(ip))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .OrderBy(ip => ip)
                        .ToList());

            var items = paged.Select(p => new AdminUserListItemDto
            {
                Id = p.Id,
                Email = p.Email ?? string.Empty,
                DisplayName = p.DisplayName ?? string.Empty,
                RecognizedVideos = p.Recognized,
                RegisteredAt = p.CreatedAt,
                Roles = rolesLookup.TryGetValue(p.Id, out var r) ? r : Array.Empty<string>(),
                YoutubeCaptionIps = ipLookup.TryGetValue(p.Id, out var ips) ? ips : Array.Empty<string>()
            }).ToList();

            return Ok(new AdminUsersPageDto
            {
                Items = items,
                TotalCount = totalCount
            });
        }

        [HttpGet("{userId}/subscription")]
        public async Task<ActionResult<SubscriptionSummaryDto>> GetUserSubscription(
            string userId,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(userId))
            {
                return BadRequest("Требуется идентификатор пользователя.");
            }

            var user = await _dbContext.Users
                .AsNoTracking()
                .FirstOrDefaultAsync(u => u.Id == userId, cancellationToken)
                .ConfigureAwait(false);

            if (user == null)
            {
                return NotFound();
            }

            var subscription = await _subscriptionService
                .GetActiveSubscriptionAsync(userId, cancellationToken)
                .ConfigureAwait(false);

            var balance = await _subscriptionService
                .GetQuotaBalanceAsync(userId, cancellationToken)
                .ConfigureAwait(false);

            var freePlan = await _dbContext.SubscriptionPlans
                .AsNoTracking()
                .Where(p => p.IsActive && p.Price == 0m)
                .OrderBy(p => p.Priority)
                .ThenBy(p => p.CreatedAt)
                .FirstOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false);

            var payments = await _dbContext.SubscriptionInvoices
                .AsNoTracking()
                .Include(i => i.UserSubscription)
                    .ThenInclude(s => s.Plan)
                .Where(i => i.UserSubscription != null && i.UserSubscription.UserId == userId)
                .OrderByDescending(i => i.IssuedAt)
                .Take(50)
                .Select(i => new SubscriptionPaymentHistoryItemDto
                {
                    InvoiceId = i.Id,
                    PlanName = i.UserSubscription.Plan.Name,
                    Amount = i.Amount,
                    Currency = i.Currency,
                    Status = i.Status,
                    IssuedAt = i.IssuedAt,
                    PaidAt = i.PaidAt,
                    PaymentProvider = i.PaymentProvider,
                    ExternalInvoiceId = i.ExternalInvoiceId,
                    Comment = i.Payload
                })
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            var plan = subscription?.Plan;

            var summary = new SubscriptionSummaryDto
            {
                HasActiveSubscription = subscription != null && subscription.Status == SubscriptionStatus.Active,
                HasLifetimeAccess = user.HasLifetimeAccess,
                PlanCode = plan?.Code,
                PlanName = plan?.Name,
                Status = subscription?.Status,
                EndsAt = subscription?.EndDate,
                IsLifetime = subscription?.IsLifetime ?? user.HasLifetimeAccess,
                FreeRecognitionsPerDay = 0,
                FreeTranscriptionsPerMonth = 0,
                FreeTranscriptionMinutes = freePlan?.IncludedTranscriptionMinutes ?? 0,
                FreeVideos = freePlan?.IncludedVideos ?? 0,
                RemainingTranscriptionMinutes = balance.RemainingTranscriptionMinutes == int.MaxValue ? int.MaxValue : Math.Max(0, balance.RemainingTranscriptionMinutes),
                RemainingVideos = balance.RemainingVideos == int.MaxValue ? int.MaxValue : Math.Max(0, balance.RemainingVideos),
                TotalTranscriptionMinutes = Math.Max(0, balance.TotalTranscriptionMinutes),
                TotalVideos = Math.Max(0, balance.TotalVideos),
                BillingUrl = _subscriptionLimits.GetBillingUrlOrDefault(),
                Payments = payments
            };

            return Ok(summary);
        }

        [HttpGet("roles")]
        public async Task<ActionResult<IEnumerable<string>>> GetRoles()
        {
            var roles = await _roleManager.Roles
                .Select(r => r.Name)
                .Where(name => name != null)
                .OrderBy(name => name)
                .Select(name => name!)
                .ToListAsync();

            return Ok(roles);
        }

        [HttpPut("{userId}/roles")]
        public async Task<ActionResult<IEnumerable<string>>> UpdateRoles(string userId, [FromBody] UpdateUserRolesRequest request)
        {
            if (request == null)
            {
                return BadRequest("Request body is required");
            }

            var user = await _userManager.FindByIdAsync(userId);
            if (user == null)
            {
                return NotFound();
            }

            var availableRoles = await _roleManager.Roles
                .Select(r => r.Name)
                .Where(name => name != null)
                .Select(name => name!)
                .ToListAsync();

            var normalizedRoles = (request.Roles ?? new List<string>())
                .Where(r => !string.IsNullOrWhiteSpace(r))
                .Select(r => r.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var invalidRoles = normalizedRoles
                .Where(role => !availableRoles.Any(ar => string.Equals(ar, role, StringComparison.OrdinalIgnoreCase)))
                .ToList();

            if (invalidRoles.Any())
            {
                return BadRequest(new { message = $"Unknown roles: {string.Join(", ", invalidRoles)}" });
            }

            var resolvedRoles = normalizedRoles
                .Select(role => availableRoles.First(ar => string.Equals(ar, role, StringComparison.OrdinalIgnoreCase)))
                .ToList();

            var currentRoles = await _userManager.GetRolesAsync(user);

            var rolesToAdd = resolvedRoles
                .Except(currentRoles, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var rolesToRemove = currentRoles
                .Except(resolvedRoles, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (rolesToAdd.Any())
            {
                var addResult = await _userManager.AddToRolesAsync(user, rolesToAdd);
                if (!addResult.Succeeded)
                {
                    return BadRequest(addResult.Errors);
                }
            }

            if (rolesToRemove.Any())
            {
                var removeResult = await _userManager.RemoveFromRolesAsync(user, rolesToRemove);
                if (!removeResult.Succeeded)
                {
                    return BadRequest(removeResult.Errors);
                }
            }

            var updatedRoles = await _userManager.GetRolesAsync(user);
            return Ok(updatedRoles);
        }

        private sealed class AdminUserQueryItem
        {
            public ApplicationUser User { get; init; } = default!;
            public int Recognized { get; init; }
        }

    }
}
