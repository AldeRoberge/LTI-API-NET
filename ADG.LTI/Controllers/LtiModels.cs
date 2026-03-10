using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;

namespace ADG.LTI.Controllers;

public class LtiLoginRequest
{
    [FromForm(Name = "iss")]
    [JsonPropertyName("iss")]
    public string Issuer { get; set; } = string.Empty;

    [FromForm(Name = "login_hint")]
    [JsonPropertyName("login_hint")]
    public string LoginHint { get; set; } = string.Empty;

    [FromForm(Name = "target_link_uri")]
    [JsonPropertyName("target_link_uri")]
    public string TargetLinkUri { get; set; } = string.Empty;

    [FromForm(Name = "lti_message_hint")]
    [JsonPropertyName("lti_message_hint")]
    public string LtiMessageHint { get; set; } = string.Empty;

    [FromForm(Name = "client_id")]
    [JsonPropertyName("client_id")]
    public string ClientId { get; set; } = string.Empty;

    [FromForm(Name = "lti_deployment_id")]
    [JsonPropertyName("lti_deployment_id")]
    public string LtiDeploymentId { get; set; } = string.Empty;
}

public class LtiLaunchRequest
{
    [JsonPropertyName("id_token")] public string IdToken { get; set; } = string.Empty;

    [JsonPropertyName("state")] public string State { get; set; } = string.Empty;
}

public class LtiUserInfo
{
    public string UserId { get; set; } = string.Empty;
    public string UserName { get; set; } = string.Empty;
    public string? UserEmail { get; set; }
    public string? GivenName { get; set; }
    public string? FamilyName { get; set; }
    public Dictionary<string, object> AdditionalClaims { get; set; } = new();
}

public class OpenIdConfiguration
{
    [JsonPropertyName("issuer")] public string Issuer { get; set; } = string.Empty;

    [JsonPropertyName("authorization_endpoint")]
    public string AuthorizationEndpoint { get; set; } = string.Empty;

    [JsonPropertyName("token_endpoint")] public string TokenEndpoint { get; set; } = string.Empty;

    [JsonPropertyName("userinfo_endpoint")]
    public string UserinfoEndpoint { get; set; } = string.Empty;

    [JsonPropertyName("jwks_uri")] public string JwksUri { get; set; } = string.Empty;

    [JsonPropertyName("response_types_supported")]
    public string[] ResponseTypesSupported { get; set; } = [];

    [JsonPropertyName("subject_types_supported")]
    public string[] SubjectTypesSupported { get; set; } = [];

    [JsonPropertyName("id_token_signing_alg_values_supported")]
    public string[] IdTokenSigningAlgValuesSupported { get; set; } = [];

    [JsonPropertyName("scopes_supported")] public string[] ScopesSupported { get; set; } = [];

    [JsonPropertyName("claims_supported")] public string[] ClaimsSupported { get; set; } = [];

    [JsonPropertyName("registration_endpoint")]
    public string RegistrationEndpoint { get; set; } = string.Empty;

    [JsonExtensionData] public Dictionary<string, object>? AdditionalData { get; set; }
}

public class JwksResponse
{
    [JsonPropertyName("keys")] public JwkKey[] Keys { get; set; } = [];
}

public class JwkKey
{
    [JsonPropertyName("kty")] public string KeyType { get; set; } = string.Empty;

    [JsonPropertyName("kid")] public string KeyId { get; set; } = string.Empty;

    [JsonPropertyName("use")] public string Use { get; set; } = string.Empty;

    [JsonPropertyName("alg")] public string Algorithm { get; set; } = string.Empty;

    [JsonPropertyName("n")] public string Modulus { get; set; } = string.Empty;

    [JsonPropertyName("e")] public string Exponent { get; set; } = string.Empty;
}