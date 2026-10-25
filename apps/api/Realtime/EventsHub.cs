using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using RestaurantOrder.Application.Auth;
using RestaurantOrder.Application.Floor;

namespace RestaurantOrder.Api.Realtime;

/// <summary>
/// Authoritative real-time event hub.
/// Enforces continuous authorization, server-determined group memberships,
/// strict tenant/branch isolation, and prohibits client-requested scope mutations.
/// </summary>
[Authorize]
public sealed class EventsHub : Hub
{
    private readonly ICustomerSessionValidator _sessionValidator;
    private readonly ITokenRevocationValidator _tokenValidator;
    private readonly IBranchScopeValidator _branchScopeValidator;
    private readonly IRealtimeConnectionTracker _tracker;
    private readonly ILogger<EventsHub> _logger;

    public EventsHub(
        ICustomerSessionValidator sessionValidator,
        ITokenRevocationValidator tokenValidator,
        IBranchScopeValidator branchScopeValidator,
        IRealtimeConnectionTracker tracker,
        ILogger<EventsHub> logger)
    {
        _sessionValidator = sessionValidator ?? throw new ArgumentNullException(nameof(sessionValidator));
        _tokenValidator = tokenValidator ?? throw new ArgumentNullException(nameof(tokenValidator));
        _branchScopeValidator = branchScopeValidator ?? throw new ArgumentNullException(nameof(branchScopeValidator));
        _tracker = tracker ?? throw new ArgumentNullException(nameof(tracker));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public override async Task OnConnectedAsync()
    {
        var principal = Context.User;
        if (principal == null)
        {
            _logger.LogWarning("Rejecting unauthenticated connection '{ConnectionId}'.", Context.ConnectionId);
            Context.Abort();
            return;
        }

        var tenantClaim = principal.FindFirst(JwtClaimNames.TenantId)?.Value ?? principal.FindFirst("tenant_id")?.Value;
        var branchClaim = principal.FindFirst(JwtClaimNames.BranchId)?.Value ?? principal.FindFirst("branch_id")?.Value;
        var roleClaim = principal.FindFirst(ClaimTypes.Role)?.Value ?? principal.FindFirst("role")?.Value;
        var principalType = principal.FindFirst(JwtClaimNames.PrincipalType)?.Value ?? principal.FindFirst("principal_type")?.Value;

        if (!Guid.TryParse(tenantClaim, out var tenantId) || tenantId == Guid.Empty || string.IsNullOrWhiteSpace(roleClaim))
        {
            _logger.LogWarning("Rejecting connection '{ConnectionId}' with missing tenant or role claims.", Context.ConnectionId);
            Context.Abort();
            return;
        }

        var normalizedRole = roleClaim.Trim().ToLowerInvariant();
        Guid branchId;

        // RestaurantAdmin has tenant scope; resolves branch through verified query parameter or header
        if (normalizedRole is "restaurantadmin" or "restaurant_admin" or "superadmin" or "super_admin")
        {
            var httpContext = Context.GetHttpContext();
            var branchQuery = httpContext?.Request.Query["branchId"].ToString()
                ?? httpContext?.Request.Query["branch_id"].ToString()
                ?? httpContext?.Request.Headers["X-Branch-Id"].ToString();

            if (!Guid.TryParse(branchQuery, out branchId) || branchId == Guid.Empty)
            {
                _logger.LogWarning("Rejecting '{Role}' connection '{ConnectionId}' without valid branch parameter.", roleClaim, Context.ConnectionId);
                Context.Abort();
                return;
            }

            var isBranchValid = await _branchScopeValidator.ValidateBranchBelongsToTenantAsync(tenantId, branchId, Context.ConnectionAborted);
            if (!isBranchValid)
            {
                _logger.LogWarning("Rejecting '{Role}' connection '{ConnectionId}' for unverified branch '{BranchId}' in tenant '{TenantId}'.",
                    roleClaim, Context.ConnectionId, branchId, tenantId);
                Context.Abort();
                return;
            }
        }
        else
        {
            if (!Guid.TryParse(branchClaim, out branchId) || branchId == Guid.Empty)
            {
                _logger.LogWarning("Rejecting connection '{ConnectionId}' with missing branch claim for role '{Role}'.", Context.ConnectionId, roleClaim);
                Context.Abort();
                return;
            }
        }

        // Parse token expiration for server-side connection eviction
        var expClaim = principal.FindFirst("exp")?.Value;
        var tokenExpiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(15);
        if (long.TryParse(expClaim, out var expSeconds))
        {
            tokenExpiresAtUtc = DateTimeOffset.FromUnixTimeSeconds(expSeconds);
        }

        var assignedGroups = new List<string>();

        if (string.Equals(principalType, "customer", StringComparison.OrdinalIgnoreCase) || normalizedRole == "customer")
        {
            var tableSessionClaim = principal.FindFirst(JwtClaimNames.TableSessionId)?.Value ?? principal.FindFirst("table_session_id")?.Value;
            if (!Guid.TryParse(tableSessionClaim, out var sessionId) || sessionId == Guid.Empty)
            {
                _logger.LogWarning("Rejecting customer connection '{ConnectionId}' without valid table session claim.", Context.ConnectionId);
                Context.Abort();
                return;
            }

            var isSessionActive = await _sessionValidator.ValidateSessionActiveAsync(tenantId, branchId, sessionId, Context.ConnectionAborted);
            if (!isSessionActive)
            {
                _logger.LogWarning("Rejecting customer connection '{ConnectionId}' for closed or inactive session '{SessionId}'.",
                    Context.ConnectionId, sessionId);
                Context.Abort();
                return;
            }

            // Assign server-determined customer groups: own session and catalog invalidations ONLY
            var sessionGroup = $"tenant:{tenantId}:branch:{branchId}:session:{sessionId}";
            var catalogGroup = $"tenant:{tenantId}:branch:{branchId}:catalog";

            assignedGroups.Add(sessionGroup);
            assignedGroups.Add(catalogGroup);

            foreach (var group in assignedGroups)
            {
                await Groups.AddToGroupAsync(Context.ConnectionId, group);
            }

            _tracker.Track(new TrackedConnection(
                ConnectionId: Context.ConnectionId,
                TenantId: tenantId,
                BranchId: branchId,
                Role: "Customer",
                DiningSessionId: sessionId,
                UserId: null,
                StaffSessionId: null,
                SecurityVersion: null,
                TokenExpiresAtUtc: tokenExpiresAtUtc,
                SubscribedGroups: assignedGroups,
                Abort: () => Context.Abort()));

            _logger.LogInformation("Customer connected '{ConnectionId}' bound to session '{SessionId}'.", Context.ConnectionId, sessionId);
        }
        else
        {
            // Staff connection: verify token active status
            var sidClaim = principal.FindFirst(JwtClaimNames.SessionId)?.Value ?? principal.FindFirst("sid")?.Value;
            var subClaim = principal.FindFirst(JwtClaimNames.Subject)?.Value ?? principal.FindFirst("sub")?.Value;
            var secVerClaim = principal.FindFirst(JwtClaimNames.SecurityVersion)?.Value ?? principal.FindFirst("security_version")?.Value;

            if (Guid.TryParse(sidClaim, out var staffSessionId) &&
                Guid.TryParse(subClaim, out var staffUserId) &&
                int.TryParse(secVerClaim, out var secVer))
            {
                var isTokenActive = await _tokenValidator.ValidateTokenActiveAsync(staffSessionId, staffUserId, secVer, Context.ConnectionAborted);
                if (!isTokenActive)
                {
                    _logger.LogWarning("Rejecting staff connection '{ConnectionId}' with revoked token.", Context.ConnectionId);
                    Context.Abort();
                    return;
                }

                var catalogGroup = $"tenant:{tenantId}:branch:{branchId}:catalog";
                var floorGroup = $"tenant:{tenantId}:branch:{branchId}:floor";
                var kitchenGroup = $"tenant:{tenantId}:branch:{branchId}:station:kitchen";
                var barGroup = $"tenant:{tenantId}:branch:{branchId}:station:bar";

                assignedGroups.Add(catalogGroup);

                switch (normalizedRole)
                {
                    case "kitchen":
                        // Kitchen: preparation queue only, no floor stream
                        assignedGroups.Add(kitchenGroup);
                        break;

                    case "bar":
                        // Bar: drink preparation queue only, no floor stream
                        assignedGroups.Add(barGroup);
                        break;

                    case "waiter":
                    case "cashier":
                        // Floor and catalog
                        assignedGroups.Add(floorGroup);
                        break;

                    case "branchmanager":
                    case "branch_manager":
                    case "restaurantadmin":
                    case "restaurant_admin":
                    case "superadmin":
                    case "super_admin":
                        // Managers and Admins: floor, stations, and catalog
                        assignedGroups.Add(floorGroup);
                        assignedGroups.Add(kitchenGroup);
                        assignedGroups.Add(barGroup);
                        break;

                    default:
                        assignedGroups.Add(floorGroup);
                        break;
                }

                foreach (var group in assignedGroups)
                {
                    await Groups.AddToGroupAsync(Context.ConnectionId, group);
                }

                _tracker.Track(new TrackedConnection(
                    ConnectionId: Context.ConnectionId,
                    TenantId: tenantId,
                    BranchId: branchId,
                    Role: roleClaim,
                    DiningSessionId: null,
                    UserId: staffUserId,
                    StaffSessionId: staffSessionId,
                    SecurityVersion: secVer,
                    TokenExpiresAtUtc: tokenExpiresAtUtc,
                    SubscribedGroups: assignedGroups,
                    Abort: () => Context.Abort()));

                _logger.LogInformation("Staff '{Role}' connected '{ConnectionId}' for branch '{BranchId}' with {GroupCount} groups.",
                    roleClaim, Context.ConnectionId, branchId, assignedGroups.Count);
            }
            else
            {
                _logger.LogWarning("Rejecting staff connection '{ConnectionId}' missing required token security claims.", Context.ConnectionId);
                Context.Abort();
                return;
            }
        }

        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        _tracker.Untrack(Context.ConnectionId);
        _logger.LogInformation("Connection '{ConnectionId}' disconnected. Exception: {Error}", Context.ConnectionId, exception?.Message);
        await base.OnDisconnectedAsync(exception);
    }
}
