using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;
using TutorManagement.Modules.Finance.Application.Payments;

namespace TutorManagement.Modules.Finance.Infrastructure.Security;

/// <summary>
/// Keys for <see cref="AesGcmSecretProtector"/>:
/// <c>Payments:EncryptionKeys:{id}</c> (Base64, 32 bytes, keep old ones for decrypting),
/// <c>Payments:ActiveKeyId</c> and <c>Payments:FingerprintKey</c> (Base64, at least 32 bytes, never rotated).
/// </summary>
public sealed class PaymentSecretOptions
{
    public const string Section = "Payments";

    public Dictionary<string, string> EncryptionKeys { get; init; } = new(StringComparer.Ordinal);
    public string? ActiveKeyId { get; init; }
    public string? FingerprintKey { get; init; }

    public static PaymentSecretOptions From(IConfiguration configuration)
    {
        var section = configuration.GetSection(Section);
        return new PaymentSecretOptions
        {
            EncryptionKeys = section.GetSection(nameof(EncryptionKeys)).GetChildren()
                .Where(x => x.Value is not null).ToDictionary(x => x.Key, x => x.Value!, StringComparer.Ordinal),
            ActiveKeyId = section[nameof(ActiveKeyId)],
            FingerprintKey = section[nameof(FingerprintKey)]
        };
    }
}

/// <summary>
/// Format: version (1 byte) | nonce (12) | tag (16) | ciphertext. A fresh random nonce per value; the purpose is the
/// associated data. Built on first use, so hosts that never touch payment secrets need no keys.
/// </summary>
public sealed class AesGcmSecretProtector : ISecretProtector
{
    private const byte FormatVersion = 1;
    private const int KeySize = 32;
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private const int HeaderSize = 1 + NonceSize + TagSize;

    private readonly Dictionary<string, byte[]> _keys;
    private readonly byte[] _fingerprintKey;

    public AesGcmSecretProtector(PaymentSecretOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _keys = options.EncryptionKeys.ToDictionary(x => x.Key, x => Decode(x.Value, $"Payments:EncryptionKeys:{x.Key}", exact: true),
            StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(options.ActiveKeyId) || !_keys.ContainsKey(options.ActiveKeyId))
            throw new InvalidOperationException("Payments:ActiveKeyId must name one of Payments:EncryptionKeys.");
        ActiveKeyId = options.ActiveKeyId;
        _fingerprintKey = Decode(options.FingerprintKey, "Payments:FingerprintKey", exact: false);
    }

    public string ActiveKeyId { get; }

    public byte[] Protect(string plaintext, string purpose)
    {
        ArgumentNullException.ThrowIfNull(plaintext);
        var data = Encoding.UTF8.GetBytes(plaintext);
        var output = new byte[HeaderSize + data.Length];
        output[0] = FormatVersion;
        var nonce = output.AsSpan(1, NonceSize);
        RandomNumberGenerator.Fill(nonce);
        using var aes = new AesGcm(_keys[ActiveKeyId], TagSize);
        aes.Encrypt(nonce, data, output.AsSpan(HeaderSize), output.AsSpan(1 + NonceSize, TagSize), Associated(purpose));
        CryptographicOperations.ZeroMemory(data);
        return output;
    }

    public string Unprotect(byte[] protectedValue, string keyId, string purpose)
    {
        ArgumentNullException.ThrowIfNull(protectedValue);
        if (protectedValue.Length < HeaderSize || protectedValue[0] != FormatVersion)
            throw new CryptographicException("The protected value has an unknown format.");
        if (!_keys.TryGetValue(keyId, out var key))
            throw new CryptographicException($"Encryption key '{keyId}' is not configured.");
        var plain = new byte[protectedValue.Length - HeaderSize];
        using var aes = new AesGcm(key, TagSize);
        aes.Decrypt(protectedValue.AsSpan(1, NonceSize), protectedValue.AsSpan(HeaderSize),
            protectedValue.AsSpan(1 + NonceSize, TagSize), plain, Associated(purpose));
        try { return Encoding.UTF8.GetString(plain); }
        finally { CryptographicOperations.ZeroMemory(plain); }
    }

    public string Fingerprint(string value, string purpose)
    {
        ArgumentNullException.ThrowIfNull(value);
        var key = HKDF.DeriveKey(HashAlgorithmName.SHA256, _fingerprintKey, KeySize, info: Associated(purpose));
        try { return Convert.ToHexStringLower(HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(value))); }
        finally { CryptographicOperations.ZeroMemory(key); }
    }

    private static byte[] Associated(string purpose) =>
        string.IsNullOrWhiteSpace(purpose)
            ? throw new ArgumentException("A purpose is required.", nameof(purpose))
            : Encoding.UTF8.GetBytes(purpose);

    private static byte[] Decode(string? value, string name, bool exact)
    {
        byte[] bytes;
        try { bytes = Convert.FromBase64String(value ?? string.Empty); }
        catch (FormatException) { throw new InvalidOperationException($"{name} must be Base64."); }
        if (exact ? bytes.Length != KeySize : bytes.Length < KeySize)
            throw new InvalidOperationException($"{name} must be {(exact ? "exactly" : "at least")} {KeySize} bytes.");
        return bytes;
    }
}
