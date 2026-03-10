using System.Collections.Concurrent;
using ADG.LTI.Controllers;

namespace ADG.LTI.Services;

/// <summary>
/// In-memory implementation of platform registration service
/// </summary>
public class PlatformRegistrationService(
    ILogger<PlatformRegistrationService> logger
) : IPlatformRegistrationService
{
    private readonly ConcurrentDictionary<string, PlatformRegistration> _registrations = new();

    public Task<PlatformRegistration> StorePlatformRegistrationAsync(PlatformRegistration registration)
    {
        registration.UpdatedAt = DateTime.UtcNow;
        _registrations.AddOrUpdate(registration.Id, registration, (_, _) => registration);

        logger.LogInformation("Stored platform registration: {Issuer} with ClientId: {ClientId}",
            registration.Issuer, registration.ClientId);

        return Task.FromResult(registration);
    }

    public Task<PlatformRegistration?> GetPlatformRegistrationAsync(string issuer, string clientId)
    {
        var registration = _registrations.Values
            .FirstOrDefault(r => r.Issuer == issuer && r.ClientId == clientId && r.IsActive);

        return Task.FromResult(registration);
    }

    public Task<PlatformRegistration?> GetPlatformRegistrationByIssuerAsync(string issuer)
    {
        var registration = _registrations.Values
            .FirstOrDefault(r => r.Issuer == issuer && r.IsActive);

        return Task.FromResult(registration);
    }

    public Task<IEnumerable<PlatformRegistration>> GetAllPlatformRegistrationsAsync()
    {
        var activeRegistrations = _registrations.Values
            .Where(r => r.IsActive)
            .OrderByDescending(r => r.CreatedAt)
            .AsEnumerable();

        return Task.FromResult(activeRegistrations);
    }

    public Task<PlatformRegistration?> UpdatePlatformRegistrationAsync(PlatformRegistration registration)
    {
        if (!_registrations.ContainsKey(registration.Id))
        {
            return Task.FromResult<PlatformRegistration?>(null);
        }

        registration.UpdatedAt = DateTime.UtcNow;
        _registrations.AddOrUpdate(registration.Id, registration, (_, _) => registration);

        logger.LogInformation("Updated platform registration: {Issuer}", registration.Issuer);

        return Task.FromResult<PlatformRegistration?>(registration);
    }

    public Task<bool> DeletePlatformRegistrationAsync(string id)
    {
        if (_registrations.TryGetValue(id, out var registration))
        {
            registration.IsActive = false;
            registration.UpdatedAt = DateTime.UtcNow;

            logger.LogInformation("Deactivated platform registration: {Issuer}", registration.Issuer);
            return Task.FromResult(true);
        }

        return Task.FromResult(false);
    }

    public Task<bool> IsPlatformRegisteredAsync(string issuer)
    {
        var isRegistered = _registrations.Values
            .Any(r => r.Issuer == issuer && r.IsActive);

        return Task.FromResult(isRegistered);
    }
}