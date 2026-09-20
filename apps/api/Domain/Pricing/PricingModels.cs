namespace RestaurantOrder.Api.Domain.Pricing;

/// <summary>
/// Result of platform commission calculation for digital payment transactions.
/// Dual-entry ledger records gross transaction, platform commission fee, and net tenant payable.
/// </summary>
public sealed record PlatformCommissionResult(
    decimal GrossAmount,
    decimal CommissionFee,
    decimal NetAmount
);
