using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using RestaurantOrder.Application.Auth;
using RestaurantOrder.Domain.Auth;
using RestaurantOrder.Domain.Branches;
using RestaurantOrder.Domain.Common;
using RestaurantOrder.Domain.Tenants;
using RestaurantOrder.Infrastructure.Persistence;

namespace RestaurantOrder.Infrastructure.Auth;

/// <summary>
/// Implements trusted terminal enrollment, hardware activation, constant-time validation,
/// and cascading session revocation.
/// </summary>
public sealed class TrustedTerminalService : ITrustedTerminalService
{
    private readonly RestaurantOrderDbContext _dbContext;
    private readonly ITerminalEnrollmentStore _enrollmentStore;
    private static readonly TimeSpan DefaultEnrollmentTtl = TimeSpan.FromMinutes(15);
    private const string CodeAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

    public TrustedTerminalService(
        RestaurantOrderDbContext dbContext,
        ITerminalEnrollmentStore enrollmentStore)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        _enrollmentStore = enrollmentStore ?? throw new ArgumentNullException(nameof(enrollmentStore));
    }

    public async Task<EnrollTerminalResult> CreateEnrollmentCodeAsync(
        TenantId tenantId,
        EnrollTerminalCommand command,
        UserId actorUserId,
        CancellationToken ct = default)
    {
        if (command.BranchId == Guid.Empty)
        {
            throw new ArgumentException("Branch ID is required.", nameof(command));
        }

        if (string.IsNullOrWhiteSpace(command.TerminalName))
        {
            throw new ArgumentException("Terminal name is required.", nameof(command));
        }

        var rawCode = RandomNumberGenerator.GetString(CodeAlphabet, 8);
        var codeHash = ComputeSha256(rawCode);
        var now = DateTimeOffset.UtcNow;
        var expiresAt = now.Add(DefaultEnrollmentTtl);

        var ticket = new TerminalEnrollmentTicket(
            CodeHash: codeHash,
            TenantId: tenantId.Value,
            BranchId: command.BranchId,
            TerminalName: command.TerminalName.Trim(),
            DeviceIdentifier: command.DeviceIdentifier?.Trim() ?? $"dev-{Guid.NewGuid():N}",
            CreatedAtUtc: now,
            ExpiresAtUtc: expiresAt);

        await _enrollmentStore.StoreEnrollmentTicketAsync(codeHash, ticket, DefaultEnrollmentTtl, ct);

        return new EnrollTerminalResult(
            EnrollmentCode: rawCode,
            ExpiresAtUtc: expiresAt,
            BranchId: command.BranchId,
            TerminalName: command.TerminalName.Trim());
    }

    public async Task<ActivateTerminalResult> ActivateTerminalAsync(
        ActivateTerminalCommand command,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(command.EnrollmentCode))
        {
            throw new PinAuthFailureException("Enrollment code cannot be empty.");
        }

        var normalizedCode = command.EnrollmentCode.Trim().ToUpperInvariant();
        var codeHash = ComputeSha256(normalizedCode);

        var ticket = await _enrollmentStore.ConsumeEnrollmentTicketAsync(codeHash, ct);
        if (ticket == null)
        {
            throw new PinAuthFailureException("Invalid or expired terminal enrollment code.");
        }

        var tenantId = TenantId.From(ticket.TenantId);

        var rawDeviceSecret = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
        var secretHash = ComputeSha256(rawDeviceSecret);
        var now = DateTimeOffset.UtcNow;
        var branchId = new BranchId(ticket.BranchId);

        var deviceId = string.IsNullOrWhiteSpace(command.DeviceIdentifier)
            ? ticket.DeviceIdentifier
            : command.DeviceIdentifier.Trim();

        var terminalName = string.IsNullOrWhiteSpace(command.TerminalName)
            ? ticket.TerminalName
            : command.TerminalName.Trim();

        var existingTerminal = await _dbContext.TrustedTerminals
            .FirstOrDefaultAsync(t => t.TenantId == tenantId && t.BranchId == branchId && t.DeviceIdentifier == deviceId, ct);

        TrustedTerminal terminal;
        if (existingTerminal != null)
        {
            _dbContext.TrustedTerminals.Remove(existingTerminal);
            terminal = TrustedTerminal.Enroll(tenantId, branchId, deviceId, terminalName, secretHash, now);
            _dbContext.TrustedTerminals.Add(terminal);
        }
        else
        {
            terminal = TrustedTerminal.Enroll(tenantId, branchId, deviceId, terminalName, secretHash, now);
            _dbContext.TrustedTerminals.Add(terminal);
        }

        var auditEvent = SecurityAuditEvent.Create(
            tenantId: tenantId,
            eventType: SecurityAuditEventType.TrustedTerminalEnrolled,
            nowUtc: now,
            userId: null,
            branchId: branchId,
            ipAddress: null,
            userAgent: $"terminal:{terminal.Id}",
            detailsJson: $"{{\"terminalName\":\"{terminalName}\",\"deviceIdentifier\":\"{deviceId}\",\"branchId\":\"{ticket.BranchId}\"}}");

        _dbContext.SecurityAuditEvents.Add(auditEvent);
        await _dbContext.SaveChangesAsync(ct);

        return new ActivateTerminalResult(
            TerminalId: terminal.Id,
            DeviceSecret: rawDeviceSecret,
            TenantId: tenantId.Value,
            BranchId: ticket.BranchId,
            TerminalName: terminal.TerminalName);
    }

    public async Task<TerminalContext?> AuthenticateTerminalAsync(
        Guid terminalId,
        string deviceSecret,
        CancellationToken ct = default)
    {
        if (terminalId == Guid.Empty || string.IsNullOrWhiteSpace(deviceSecret))
        {
            return null;
        }

        var terminal = await _dbContext.TrustedTerminals
            .FirstOrDefaultAsync(t => t.Id == terminalId, ct);

        if (terminal == null || !terminal.IsActive)
        {
            return null;
        }

        var presentedHash = ComputeSha256(deviceSecret.Trim());
        if (!FixedTimeEquals(presentedHash, terminal.SecretHash))
        {
            return null;
        }

        terminal.RecordHeartbeat(DateTimeOffset.UtcNow);
        await _dbContext.SaveChangesAsync(ct);

        return new TerminalContext(
            TerminalId: terminal.Id,
            TenantId: terminal.TenantId.Value,
            BranchId: terminal.BranchId.Value,
            TerminalName: terminal.TerminalName,
            DeviceIdentifier: terminal.DeviceIdentifier,
            IsActive: terminal.IsActive);
    }

    public async Task<bool> RevokeTerminalAsync(
        TenantId tenantId,
        Guid terminalId,
        UserId actorUserId,
        CancellationToken ct = default)
    {
        var terminal = await _dbContext.TrustedTerminals
            .FirstOrDefaultAsync(t => t.Id == terminalId && t.TenantId == tenantId, ct);

        if (terminal == null)
        {
            return false;
        }

        var now = DateTimeOffset.UtcNow;
        terminal.Revoke(now);

        // Terminal revocation cascade: terminate all active sessions tied to this terminal
        var terminalPrefix = $"terminal:{terminalId}";
        var activeSessions = await _dbContext.Sessions
            .Where(s => s.TenantId == tenantId && !s.IsRevoked && s.UserAgent != null && s.UserAgent.StartsWith(terminalPrefix))
            .ToListAsync(ct);

        foreach (var session in activeSessions)
        {
            session.Revoke("Cascading revocation from trusted terminal deactivation.", now);
        }

        var auditEvent = SecurityAuditEvent.Create(
            tenantId: tenantId,
            eventType: SecurityAuditEventType.TrustedTerminalRevoked,
            nowUtc: now,
            userId: actorUserId,
            branchId: terminal.BranchId,
            ipAddress: null,
            userAgent: terminalPrefix,
            detailsJson: $"{{\"terminalId\":\"{terminalId}\",\"revokedSessionsCount\":{activeSessions.Count}}}");

        _dbContext.SecurityAuditEvents.Add(auditEvent);
        await _dbContext.SaveChangesAsync(ct);

        return true;
    }

    public Task<TerminalContext?> GetCurrentTerminalAsync(
        Guid terminalId,
        string deviceSecret,
        CancellationToken ct = default) =>
        AuthenticateTerminalAsync(terminalId, deviceSecret, ct);

    private static string ComputeSha256(string input)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static bool FixedTimeEquals(string a, string b)
    {
        var bytesA = Encoding.UTF8.GetBytes(a);
        var bytesB = Encoding.UTF8.GetBytes(b);
        return CryptographicOperations.FixedTimeEquals(bytesA, bytesB);
    }
}
