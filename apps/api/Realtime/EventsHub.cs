using System;
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
    private readonly IRealtimeConnectionTracker _tracker;
    private readonly ILogger<EventsHub> _logger;

    public EventsHub(
        ICustomerSessionValidator sessionValidator,
        ITokenRevocationValidator tokenValidator,
        IRealtimeConnectionTracker tracker,
        ILogger<EventsHub> logger)
    {
        _sessionValidator = sessionValidator ?? throw new ArgumentNullException(nameof(sessionValidator));
        _tokenValidator = tokenValidator ?? throw new ArgumentNullException(nameof(tokenValidator));
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

        if (!Guid.TryParse(tenantClaim, out var tenantId) || tenantId == Guid.Empty ||
            !Guid.TryParse(branchClaim, out var branchId) || branchId == Guid.Empty ||
            string.IsNullOrWhiteSpace(roleClaim))
        {
            _logger.LogWarning("Rejecting connection '{ConnectionId}' with missing scope claims.", Context.ConnectionId);
            Context.Abort();
            return;
        }

        var normalizedRole = roleClaim.Trim().ToLowerInvariant();

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

            // Assign server-determined customer groups: own session and catalog invalidations
            var sessionGroup = $"tenant:{tenantId}:branch:{branchId}:session:{sessionId}";
            var catalogGroup = $"tenant:{tenantId}:branch:{branchId}:catalog";

            await Groups.AddToGroupAsync(Context.ConnectionId, sessionGroup);
            await Groups.AddToGroupAsync(Context.ConnectionId, catalogGroup);

            _tracker.Track(new TrackedConnection(
                Context.ConnectionId,
                tenantId,
                branchId,
                "Customer",
                sessionId,
                UserId: null));

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

                // Assign server-determined staff groups
                var floorGroup = $"tenant:{tenantId}:branch:{branchId}:floor";
                var catalogGroup = $"tenant:{tenantId}:branch:{branchId}:catalog";

                await Groups.AddToGroupAsync(Context.ConnectionId, floorGroup);
                await Groups.AddToGroupAsync(Context.ConnectionId, catalogGroup);

                if (normalizedRole == "kitchen")
                {
                    await Groups.AddToGroupAsync(Context.ConnectionId, $"tenant:{tenantId}:branch:{branchId}:station:kitchen");
                }
                else if (normalizedRole == "bar")
                {
                    await Groups.AddToGroupAsync(Context.ConnectionId, $"tenant:{tenantId}:branch:{branchId}:station:bar");
                }

                _tracker.Track(new TrackedConnection(
                    Context.ConnectionId,
                    tenantId,
                    branchId,
                    roleClaim,
                    DiningSessionId: null,
                    UserId: staffUserId));

                _logger.LogInformation("Staff '{Role}' connected '{ConnectionId}' for branch '{BranchId}'.", roleClaim, Context.ConnectionId, branchId);
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
