using AGX.Common.Options.Validation;
using FluentValidation;

namespace ADG.LTI.Options;

public static class LtiOptionsExtensions
{
    public static LtiOptions AddLtiOptions(this IServiceCollection services, IConfiguration configuration)
    {
        // Register the validator for LtiConfiguration
        services.AddScoped<IValidator<LtiOptions>, LtiOptionsValidator>();

        // Bind settings sections to LtiConfiguration class
        services.Configure<LtiOptions>(configuration.GetSection(LtiOptions.ConfigurationSection));

        // Use the convenience extension for validation
        services.AddValidatedOptions<LtiOptions>(LtiOptions.ConfigurationSection);

        // Create an instance with the bound configuration
        LtiOptions ltiConfig = new()
        {
            ClientId = null!,
            DeploymentId = null!,
            MoodleAuthorizationEndpoint = null!,
            RedirectionUrl = null!,
            Issuer = null!,
            JwksUrl = null!,
            ValidateIssuer = false,
            ValidateAudience = false,
            ValidateLifetime = false,
            ValidateSignature = false
        };

        configuration
            .GetRequiredSection(LtiOptions.ConfigurationSection)
            .Bind(ltiConfig);

        // Return the strongly-typed configuration
        return ltiConfig;
    }
}