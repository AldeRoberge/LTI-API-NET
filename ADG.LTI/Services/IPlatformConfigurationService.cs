using ADG.LTI.Controllers;
using FluentResults;

namespace ADG.LTI.Services;

/// <summary>
/// Service for fetching and validating platform configurations
/// </summary>
public interface IPlatformConfigurationService
{
    /// <summary>
    /// Fetch platform OpenID configuration from the provided URL
    /// </summary>
    Task<Result<OpenIdConfiguration>> FetchPlatformConfigurationAsync(string openIdConfigurationUrl);

    /// <summary>
    /// Validate that the platform configuration contains required endpoints
    /// </summary>
    Result ValidatePlatformConfiguration(OpenIdConfiguration configuration);
}