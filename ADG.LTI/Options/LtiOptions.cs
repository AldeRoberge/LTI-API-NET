namespace ADG.LTI.Options;

// this is set in appsettings.json
public class LtiOptions
{
    public const string ConfigurationSection = "LTI";

    public required string ClientId { get; init; }
    public required string DeploymentId { get; init; }
    public required string MoodleAuthorizationEndpoint { get; init; }
    public required string RedirectionUrl { get; init; }
    public required string Issuer { get; init; }
    public required string JwksUrl { get; init; }
    public required bool ValidateIssuer { get; init; }
    public required bool ValidateAudience { get; init; }
    public required bool ValidateLifetime { get; init; }
    public required bool ValidateSignature { get; init; }
}