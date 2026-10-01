namespace RestaurantOrder.Infrastructure.Auth;

/// <summary>
/// Cryptographic protector for outbox notification payloads.
/// Prevents plaintext persistence of sensitive invitation or password reset tokens.
/// </summary>
public interface IIdentityOutboxPayloadProtector
{
    string Protect(string plaintextPayload);
    string Unprotect(string cipherTextPayload);
}
