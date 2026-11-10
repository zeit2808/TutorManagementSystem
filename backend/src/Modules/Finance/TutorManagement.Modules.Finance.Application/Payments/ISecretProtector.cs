namespace TutorManagement.Modules.Finance.Application.Payments;

/// <summary>
/// Encrypts provider secrets at rest (AES-256-GCM). The API and the Worker share the keys through configuration.
/// The purpose is bound into the ciphertext, so a value copied to another row or field no longer decrypts.
/// </summary>
public interface ISecretProtector
{
    /// <summary>The key new values are encrypted with; stored next to them as <c>key_id</c>.</summary>
    string ActiveKeyId { get; }

    byte[] Protect(string plaintext, string purpose);

    /// <summary>Throws <see cref="System.Security.Cryptography.CryptographicException"/> when the value was changed or moved.</summary>
    string Unprotect(byte[] protectedValue, string keyId, string purpose);

    /// <summary>Keyed hash that stays the same when encryption keys rotate; used to compare secrets without decrypting.</summary>
    string Fingerprint(string value, string purpose);
}

/// <summary>Purposes bound into each secret, one per center, provider and field.</summary>
public static class SecretPurposes
{
    public static string WebhookSecret(Guid centerId, string provider) => $"payment-config:{centerId:N}:{provider}:webhook-secret";

    public static string ApiToken(Guid centerId, string provider) => $"payment-config:{centerId:N}:{provider}:api-token";

    public static string ApiTokenFingerprint(string provider) => $"payment-config:{provider}:api-token-fingerprint";
}
