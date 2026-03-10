using System.Text.Json.Serialization;

namespace ADG.LTI.Controllers;

/// <summary>
/// Request model for initiating dynamic registration
/// </summary>
public class DynamicRegistrationRequest
{
    [JsonPropertyName("openid_configuration")]
    public string OpenIdConfiguration { get; set; } = string.Empty;

    [JsonPropertyName("registration_token")]
    public string RegistrationToken { get; set; } = string.Empty;
}

/// <summary>
/// Model representing a dynamically registered platform
/// </summary>
public class PlatformRegistration
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Issuer { get; set; } = string.Empty;
    public string ClientId { get; set; } = string.Empty;
    public string? DeploymentId { get; set; }
    public string AuthorizationEndpoint { get; set; } = string.Empty;
    public string TokenEndpoint { get; set; } = string.Empty;
    public string JwksUri { get; set; } = string.Empty;
    public string RegistrationEndpoint { get; set; } = string.Empty;

    public string? RegistrationAccessToken { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public bool IsActive { get; set; } = true;
}

/// <summary>
/// Request model for registering with a platform using LTI 1.3 Dynamic Registration
/// </summary>
public class ToolRegistrationRequest
{
    /// <summary>
    /// The type of application. Usually "web" for LTI tools.
    /// </summary>
    [JsonPropertyName("application_type")]
    public string ApplicationType { get; set; } = "web";

    /// <summary>
    /// Display name of the tool.
    /// </summary>
    [JsonPropertyName("client_name")]
    public string ClientName { get; set; } = "Clinique Ludique";

    /// <summary>
    /// Array of redirect URIs for OIDC login/launch.
    /// </summary>
    [JsonPropertyName("redirect_uris")]
    public string[] RedirectUris { get; set; } = [];

    /// <summary>
    /// The OIDC login initiation endpoint of your tool.
    /// </summary>
    [JsonPropertyName("initiate_login_uri")]
    public string InitiateLoginUri { get; set; } = string.Empty;

    /// <summary>
    /// JWKS endpoint for public keys.
    /// </summary>
    [JsonPropertyName("jwks_uri")]
    public string JwksUri { get; set; } = string.Empty;

    /// <summary>
    /// Method used for client authentication. For LTI 1.3 tools: "private_key_jwt".
    /// </summary>
    [JsonPropertyName("token_endpoint_auth_method")]
    public string TokenEndpointAuthMethod { get; set; } = "private_key_jwt";

    /// <summary>
    /// Supported grant types (e.g., client_credentials, implicit).
    /// </summary>
    [JsonPropertyName("grant_types")]
    public string[] GrantTypes { get; set; } = [];

    /// <summary>
    /// Supported response types (LTI 1.3 requires "id_token").
    /// </summary>
    [JsonPropertyName("response_types")]
    public string[] ResponseTypes { get; set; } = ["id_token"];

    /// <summary>
    /// Contact emails for administrators or support.
    /// </summary>
    [JsonPropertyName("contacts")]
    public string[] Contacts { get; set; } = [];

    /// <summary>
    /// Requested OAuth scopes.
    /// </summary>
    [JsonPropertyName("scope")]
    public string Scope { get; set; } =
        "openid https://purl.imsglobal.org/spec/lti-ags/scope/score " +
        "https://purl.imsglobal.org/spec/lti-nrps/scope/contextmembership.readonly";

    /// <summary>
    /// LTI-specific tool configuration.
    /// </summary>
    [JsonPropertyName("https://purl.imsglobal.org/spec/lti-tool-configuration")]
    public LtiToolConfiguration LtiToolConfiguration { get; set; } = new();
}

/// <summary>
/// LTI tool configuration within the registration request
/// </summary>
public class LtiToolConfiguration
{
    [JsonPropertyName("domain")] public string Domain { get; set; } = string.Empty;

    [JsonPropertyName("description")] public string Description { get; set; } = "Clinique Ludique integration for Moodle.";

    [JsonPropertyName("target_link_uri")] public string TargetLinkUri { get; set; } = string.Empty;

    [JsonPropertyName("custom_parameters")]
    public Dictionary<string, string> CustomParameters { get; set; } = new();

    [JsonPropertyName("claims")]
    public string[] Claims { get; set; } =
    [
        "iss",
        "sub",
        "name",
        "given_name",
        "family_name",
        "email",
        "https://purl.imsglobal.org/spec/lti/claim/deployment_id",
        "https://purl.imsglobal.org/spec/lti/claim/target_link_uri",
        "https://purl.imsglobal.org/spec/lti/claim/context",
        "https://purl.imsglobal.org/spec/lti/claim/resource_link",
        "https://purl.imsglobal.org/spec/lti/claim/roles"
    ];

    [JsonPropertyName("messages")]
    public LtiMessageConfiguration[] Messages { get; set; } =
    [
        new()
        {
            Type = "LtiResourceLinkRequest",
            TargetLinkUri = string.Empty,
            Label = "Clinique Ludique"
        }
    ];
}

/// <summary>
/// LTI message configuration
/// </summary>
public class LtiMessageConfiguration
{
    [JsonPropertyName("type")] public string Type { get; set; } = string.Empty;

    [JsonPropertyName("target_link_uri")] public string TargetLinkUri { get; set; } = string.Empty;

    [JsonPropertyName("label")] public string Label { get; set; } = string.Empty;
}

/// <summary>
/// Response model from platform registration
/// </summary>
public class ToolRegistrationResponse
{
    [JsonPropertyName("client_id")] public string ClientId { get; set; } = string.Empty;

    [JsonPropertyName("deployment_id")] public string? DeploymentId { get; set; }

    [JsonPropertyName("client_secret")] public string? ClientSecret { get; set; }

    [JsonPropertyName("registration_client_uri")]
    public string? RegistrationClientUri { get; set; }

    [JsonPropertyName("client_id_issued_at")]
    public long? ClientIdIssuedAt { get; set; }

    // RegistrationAccessToken
    [JsonPropertyName("registration_access_token")]
    public string? RegistrationAccessToken { get; set; }
}