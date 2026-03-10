using System.Text.Json;
using ADG.LTI.Controllers;
using FluentResults;

namespace ADG.LTI.Services;

/// <summary>
/// Service for fetching and validating platform configurations
/// </summary>
public class PlatformConfigurationService(
    ILogger<PlatformConfigurationService> logger,
    IHttpClientFactory httpClientFactory
) : IPlatformConfigurationService
{
    public async Task<Result<OpenIdConfiguration>> FetchPlatformConfigurationAsync(string openIdConfigurationUrl)
    {
        try
        {
            if (!Uri.TryCreate(openIdConfigurationUrl, UriKind.Absolute, out var uri))
            {
                return Result.Fail($"Invalid OpenID configuration URL: {openIdConfigurationUrl}");
            }

            // Ensure HTTPS for security
            if (uri.Scheme != "https")
            {
                logger.LogWarning("OpenID configuration URL is not HTTPS: {Url}", openIdConfigurationUrl);
                // In production, this should be an error, but allow HTTP for development
            }

            using var httpClient = httpClientFactory.CreateClient();
            httpClient.Timeout = TimeSpan.FromSeconds(30);

            logger.LogInformation("Fetching platform configuration from: {Url}", openIdConfigurationUrl);

            var response = await httpClient.GetAsync(openIdConfigurationUrl);

            if (!response.IsSuccessStatusCode)
            {
                return Result.Fail($"Failed to fetch platform configuration. Status: {response.StatusCode}");
            }

            var content = await response.Content.ReadAsStringAsync();

            if (string.IsNullOrWhiteSpace(content))
            {
                return Result.Fail("Platform configuration response is empty");
            }

            var configuration = JsonSerializer.Deserialize<OpenIdConfiguration>(content);

            if (configuration == null)
            {
                return Result.Fail("Failed to deserialize platform configuration");
            }

            logger.LogInformation("Successfully fetched platform configuration for issuer: {Issuer}", configuration.Issuer);

            var validationResult = ValidatePlatformConfiguration(configuration);
            if (validationResult.IsFailed)
            {
                return Result.Fail($"Platform configuration validation failed: {string.Join(", ", validationResult.Errors.Select(e => e.Message))}");
            }

            return Result.Ok(configuration);
        }
        catch (HttpRequestException ex)
        {
            logger.LogError(ex, "HTTP error while fetching platform configuration from {Url}", openIdConfigurationUrl);
            return Result.Fail($"Network error: {ex.Message}");
        }
        catch (TaskCanceledException ex)
        {
            logger.LogError(ex, "Timeout while fetching platform configuration from {Url}", openIdConfigurationUrl);
            return Result.Fail("Request timeout while fetching platform configuration");
        }
        catch (JsonException ex)
        {
            logger.LogError(ex, "JSON parsing error for platform configuration from {Url}", openIdConfigurationUrl);
            return Result.Fail($"Invalid JSON in platform configuration: {ex.Message}");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unexpected error while fetching platform configuration from {Url}", openIdConfigurationUrl);
            return Result.Fail($"Unexpected error: {ex.Message}");
        }
    }

    public Result ValidatePlatformConfiguration(OpenIdConfiguration configuration)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(configuration.Issuer))
        {
            errors.Add("Missing issuer");
        }

        if (string.IsNullOrWhiteSpace(configuration.AuthorizationEndpoint))
        {
            errors.Add("Missing authorization_endpoint");
        }

        if (string.IsNullOrWhiteSpace(configuration.TokenEndpoint))
        {
            errors.Add("Missing token_endpoint");
        }

        if (string.IsNullOrWhiteSpace(configuration.JwksUri))
        {
            errors.Add("Missing jwks_uri");
        }

        /*
        // Check for LTI-specific endpoints - registration_endpoint should be present for dynamic registration
        var hasRegistrationEndpoint = false;
        if (configuration.AdditionalData?.ContainsKey("registration_endpoint") == true)
        {
            var registrationEndpoint = configuration.AdditionalData["registration_endpoint"];
            if (registrationEndpoint is JsonElement jsonElement)
            {
                hasRegistrationEndpoint = !string.IsNullOrWhiteSpace(jsonElement.GetString());
            }
            else if (registrationEndpoint is string stringValue)
            {
                hasRegistrationEndpoint = !string.IsNullOrWhiteSpace(stringValue);
            }
        }

        if (!hasRegistrationEndpoint)
        {
            errors.Add("Missing registration_endpoint required for LTI dynamic registration");
        }

        // Validate that required response types are supported
        if (!configuration.ResponseTypesSupported.Contains("id_token"))
        {
            errors.Add("Platform must support 'id_token' response type");
        }

        // Validate that RS256 is supported for signing
        if (!configuration.IdTokenSigningAlgValuesSupported.Contains("RS256"))
        {
            errors.Add("Platform must support 'RS256' for ID token signing");
        }*/

        if (errors.Any())
        {
            return Result.Fail(string.Join("; ", errors));
        }

        return Result.Ok();
    }
}