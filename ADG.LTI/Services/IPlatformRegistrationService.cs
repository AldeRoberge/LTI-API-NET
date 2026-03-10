using ADG.LTI.Controllers;

namespace ADG.LTI.Services;

/// <summary>
/// Service for managing platform registrations
/// </summary>
public interface IPlatformRegistrationService
{
    /// <summary>
    /// Store a new platform registration
    /// </summary>
    Task<PlatformRegistration> StorePlatformRegistrationAsync(PlatformRegistration registration);

    /// <summary>
    /// Get platform registration by issuer and client ID
    /// </summary>
    Task<PlatformRegistration?> GetPlatformRegistrationAsync(string issuer, string clientId);

    /// <summary>
    /// Get platform registration by issuer only
    /// </summary>
    Task<PlatformRegistration?> GetPlatformRegistrationByIssuerAsync(string issuer);

    /// <summary>
    /// Get all platform registrations
    /// </summary>
    Task<IEnumerable<PlatformRegistration>> GetAllPlatformRegistrationsAsync();

    /// <summary>
    /// Update an existing platform registration
    /// </summary>
    Task<PlatformRegistration?> UpdatePlatformRegistrationAsync(PlatformRegistration registration);

    /// <summary>
    /// Delete a platform registration
    /// </summary>
    Task<bool> DeletePlatformRegistrationAsync(string id);

    /// <summary>
    /// Check if a platform is already registered
    /// </summary>
    Task<bool> IsPlatformRegisteredAsync(string issuer);
}