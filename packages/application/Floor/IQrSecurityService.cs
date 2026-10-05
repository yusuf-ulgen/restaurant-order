using RestaurantOrder.Domain.Floor;

namespace RestaurantOrder.Application.Floor;

/// <summary>
/// Contract for cryptographic generation, canonical signing, verification, and SVG encoding of table QR codes.
/// </summary>
public interface IQrSecurityService
{
    string CurrentKeyId { get; }

    string GenerateToken(QrPayload payload);

    QrPayload VerifyAndDecodeToken(string token, DateTimeOffset nowUtc);

    string GenerateSvg(string content);

    string GetFingerprint(string token);
}
