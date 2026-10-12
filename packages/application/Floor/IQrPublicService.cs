namespace RestaurantOrder.Application.Floor;

/// <summary>
/// Public-facing service for resolving and exchanging signed table QR tokens.
/// </summary>
public interface IQrPublicService
{
    Task<QrResolveResponse> ResolveQrAsync(string token, string clientIp, CancellationToken ct = default);

    Task<QrExchangeResult> ExchangeQrAsync(string token, string clientIp, CancellationToken ct = default);
}
