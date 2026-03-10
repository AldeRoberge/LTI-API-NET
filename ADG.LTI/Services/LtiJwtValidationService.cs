using System.IdentityModel.Tokens.Jwt;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using ADG.LTI.Controllers;
using ADG.LTI.Options;
using FluentResults;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace ADG.LTI.Services;

public interface ILtiJwtValidationService
{
    Task<Result<LtiUserInfo>> ValidateAndExtractClaimsAsync(string idToken);
    Task<JsonDocument> ValidateJwtAsync(string idToken);
}

public class LtiJwtValidationService(
    ILogger<LtiJwtValidationService> logger,
    IOptions<LtiOptions> ltiConfig,
    IHttpClientFactory httpClientFactory,
    IHostEnvironment hostEnvironment
) : ILtiJwtValidationService
{
    private readonly LtiOptions _ltiConfig = ltiConfig.Value;
    private readonly Dictionary<string, RSA> _cachedKeys = new();
    private readonly SemaphoreSlim _keyCacheSemaphore = new(1, 1);

    public async Task<Result<LtiUserInfo>> ValidateAndExtractClaimsAsync(string idToken)
    {
        try
        {
            var payload = await ValidateJwtAsync(idToken);

            // Extract LTI claims
            var userInfo = new LtiUserInfo();

            // Subject (user ID)
            if (payload.RootElement.TryGetProperty("sub", out var subElement))
                userInfo.UserId = subElement.GetString() ?? string.Empty;

            // Name
            if (payload.RootElement.TryGetProperty("name", out var nameElement))
                userInfo.UserName = nameElement.GetString() ?? string.Empty;

            // Email
            if (payload.RootElement.TryGetProperty("email", out var emailElement))
                userInfo.UserEmail = emailElement.GetString();

            // Given name
            if (payload.RootElement.TryGetProperty("given_name", out var givenNameElement))
                userInfo.GivenName = givenNameElement.GetString();

            // Family name
            if (payload.RootElement.TryGetProperty("family_name", out var familyNameElement))
                userInfo.FamilyName = familyNameElement.GetString();

            // Store all additional claims for potential use
            foreach (var property in payload.RootElement.EnumerateObject())
            {
                if (!IsStandardClaim(property.Name))
                {
                    userInfo.AdditionalClaims[property.Name] = property.Value.Clone();
                }
            }

            logger.LogInformation("Successfully extracted user info: UserId={UserId}, Name={UserName}, Email={Email}",
                userInfo.UserId, userInfo.UserName, userInfo.UserEmail);

            return Result.Ok(userInfo);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to validate and extract claims from JWT");
            return Result.Fail($"JWT validation failed: {ex.Message}");
        }
    }

    public async Task<JsonDocument> ValidateJwtAsync(string idToken)
    {
        var tokenHandler = new JwtSecurityTokenHandler();

        // Parse the token without validation first to get the header
        var token = tokenHandler.ReadJwtToken(idToken);

        // Get the key ID from the token header
        var keyId = token.Header.Kid;
        if (string.IsNullOrEmpty(keyId))
        {
            throw new SecurityTokenValidationException("Token missing 'kid' header");
        }

        // Get the signing key
        var signingKey = await GetSigningKeyAsync(keyId);

        var validationParameters = new TokenValidationParameters
        {
            ValidateIssuer = _ltiConfig.ValidateIssuer,
            ValidIssuer = _ltiConfig.Issuer,
            ValidateAudience = _ltiConfig.ValidateAudience,
            ValidAudience = _ltiConfig.ClientId,
            ValidateLifetime = _ltiConfig.ValidateLifetime,
            ValidateIssuerSigningKey = _ltiConfig.ValidateSignature,
            IssuerSigningKey = signingKey,
            ClockSkew = TimeSpan.FromMinutes(5) // Allow 5 minutes clock skew
        };

        // Validate the token
        tokenHandler.ValidateToken(idToken, validationParameters, out var validatedToken);

        // Parse the payload as JsonDocument for easier claim access
        var jwtToken = (JwtSecurityToken)validatedToken;
        var payloadJson = jwtToken.Payload.SerializeToJson();
        return JsonDocument.Parse(payloadJson);
    }

    private async Task<RsaSecurityKey> GetSigningKeyAsync(string keyId)
    {
        await _keyCacheSemaphore.WaitAsync();
        try
        {
            // Check if we have this key cached
            if (_cachedKeys.TryGetValue(keyId, out var cachedRsa))
            {
                return new RsaSecurityKey(cachedRsa) { KeyId = keyId };
            }

            var handler = new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback = (req, cert, _, errors) =>
                {
                    Console.WriteLine($"Requesting: {req.RequestUri}");
                    Console.WriteLine($"Subject: {cert?.Subject}");
                    Console.WriteLine($"Issuer: {cert?.Issuer}");
                    Console.WriteLine($"Errors: {errors}");

                    if (cert?.Extensions.OfType<X509Extension>()
                            .FirstOrDefault(e => e.Oid?.Value == "2.5.29.17") is { } sanExt)
                    {
                        Console.WriteLine($"SAN: {sanExt.Format(true)}");
                    }

                    // Check if we are running in Development, automatically accept
                    if (hostEnvironment.IsDevelopment())
                    {
                        logger.LogInformation("Dangerous: Allowing untrusted certificate in Development environment.");
                        return true;
                    }

                    logger.LogWarning("Rejecting untrusted certificate in non-Development environment.");

                    return false; // don’t accept, just inspect
                }
            };

            var client = new HttpClient(handler);
            var response = await client.GetStringAsync(_ltiConfig.JwksUrl);
            var jwks = JsonSerializer.Deserialize<JwksResponse>(response);

            if (jwks?.Keys == null)
            {
                throw new SecurityTokenValidationException("Invalid JWKS response");
            }

            // Find the key with matching kid
            var jwkKey = jwks.Keys.FirstOrDefault(k => k.KeyId == keyId);
            if (jwkKey == null)
            {
                throw new SecurityTokenValidationException($"Key with ID '{keyId}' not found in JWKS");
            }

            // Create RSA key from JWK
            var rsa = RSA.Create();
            var rsaParameters = new RSAParameters
            {
                Modulus = Base64UrlEncoder.DecodeBytes(jwkKey.Modulus),
                Exponent = Base64UrlEncoder.DecodeBytes(jwkKey.Exponent)
            };
            rsa.ImportParameters(rsaParameters);

            // Cache the key
            _cachedKeys[keyId] = rsa;

            return new RsaSecurityKey(rsa) { KeyId = keyId };
        }
        finally
        {
            _keyCacheSemaphore.Release();
        }
    }

    private static bool IsStandardClaim(string claimName)
    {
        return claimName is "sub" or "name" or "email" or "given_name" or "family_name" or
            "iss" or "aud" or "exp" or "iat" or "nbf" or "jti" or "azp" or "nonce";
    }
}