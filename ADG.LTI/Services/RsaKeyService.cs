using System.Security.Cryptography;
using System.Text.Json;
using ADG.LTI.Controllers;
using Microsoft.IdentityModel.Tokens;

namespace ADG.LTI.Services;

public interface IRsaKeyService
{
    Task<JwksResponse> GetPublicKeysAsync();
    Task<RsaSecurityKey> GetSigningKeyAsync();
    Task EnsureKeysExistAsync();
}

public class RsaKeyService : IRsaKeyService
{
    private readonly ILogger<RsaKeyService> _logger;
    private readonly string _keyStorePath;
    private RSA? _signingKey;
    private JwksResponse? _cachedJwks;

    public RsaKeyService(ILogger<RsaKeyService> logger, IWebHostEnvironment environment)
    {
        _logger = logger;
        _keyStorePath = Path.Combine(environment.ContentRootPath, "Keys");
        Directory.CreateDirectory(_keyStorePath);
    }

    public async Task<JwksResponse> GetPublicKeysAsync()
    {
        if (_cachedJwks != null)
            return _cachedJwks;

        await EnsureKeysExistAsync();

        if (_signingKey == null)
            throw new InvalidOperationException("Signing key not available");

        var parameters = _signingKey.ExportParameters(false);

        var jwkKey = new JwkKey
        {
            KeyType = "RSA",
            KeyId = "key1",
            Use = "sig",
            Algorithm = "RS256",
            Modulus = Base64UrlEncoder.Encode(parameters.Modulus!),
            Exponent = Base64UrlEncoder.Encode(parameters.Exponent!)
        };

        _cachedJwks = new JwksResponse
        {
            Keys = [jwkKey]
        };

        return _cachedJwks;
    }

    public async Task<RsaSecurityKey> GetSigningKeyAsync()
    {
        await EnsureKeysExistAsync();

        if (_signingKey == null)
            throw new InvalidOperationException("Signing key not available");

        return new RsaSecurityKey(_signingKey) { KeyId = "key1" };
    }

    public async Task EnsureKeysExistAsync()
    {
        if (_signingKey != null)
            return;

        var privateKeyPath = Path.Combine(_keyStorePath, "private_key.json");

        if (File.Exists(privateKeyPath))
        {
            // Load existing key
            try
            {
                var keyJson = await File.ReadAllTextAsync(privateKeyPath);
                var keyData = JsonSerializer.Deserialize<RsaKeyData>(keyJson);

                if (keyData != null)
                {
                    _signingKey = RSA.Create();
                    var parameters = new RSAParameters
                    {
                        Modulus = Convert.FromBase64String(keyData.Modulus),
                        Exponent = Convert.FromBase64String(keyData.Exponent),
                        D = Convert.FromBase64String(keyData.D),
                        P = Convert.FromBase64String(keyData.P),
                        Q = Convert.FromBase64String(keyData.Q),
                        DP = Convert.FromBase64String(keyData.DP),
                        DQ = Convert.FromBase64String(keyData.DQ),
                        InverseQ = Convert.FromBase64String(keyData.InverseQ)
                    };
                    _signingKey.ImportParameters(parameters);

                    _logger.LogInformation("Loaded existing RSA signing key");
                    return;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to load existing key, generating new one");
            }
        }

        // Generate new private key
        _signingKey = RSA.Create(2048);
        await SavePrivateKeyAsync(_signingKey);

        _logger.LogInformation("Generated new RSA signing key");
    }

    private async Task SavePrivateKeyAsync(RSA rsa)
    {
        var parameters = rsa.ExportParameters(true);

        var keyData = new RsaKeyData
        {
            Modulus = Convert.ToBase64String(parameters.Modulus!),
            Exponent = Convert.ToBase64String(parameters.Exponent!),
            D = Convert.ToBase64String(parameters.D!),
            P = Convert.ToBase64String(parameters.P!),
            Q = Convert.ToBase64String(parameters.Q!),
            DP = Convert.ToBase64String(parameters.DP!),
            DQ = Convert.ToBase64String(parameters.DQ!),
            InverseQ = Convert.ToBase64String(parameters.InverseQ!)
        };

        var keyJson = JsonSerializer.Serialize(keyData, new JsonSerializerOptions { WriteIndented = true });
        var privateKeyPath = Path.Combine(_keyStorePath, "private_key.json");

        await File.WriteAllTextAsync(privateKeyPath, keyJson);

        // Set restrictive permissions on Unix systems
        if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
        {
            File.SetUnixFileMode(privateKeyPath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
    }

    private class RsaKeyData
    {
        public string Modulus { get; init; } = string.Empty;
        public string Exponent { get; init; } = string.Empty;
        public string D { get; init; } = string.Empty;
        public string P { get; init; } = string.Empty;
        public string Q { get; init; } = string.Empty;
        public string DP { get; init; } = string.Empty;
        public string DQ { get; init; } = string.Empty;
        public string InverseQ { get; init; } = string.Empty;
    }
}