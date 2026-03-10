using System.Net.Http.Headers;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using ADG.LTI.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ADG.LTI.Controllers;

/// <summary>
/// LTI 1.3 Dynamic Registration controller (spec-compliant).
/// Handles automatic registration with LTI platforms like Moodle.
/// </summary>
[ApiController]
[Route("lti")]
public class DynamicRegistrationController(
    ILogger<DynamicRegistrationController> logger,
    IPlatformConfigurationService platformConfigService,
    IPlatformRegistrationService platformRegistrationService,
    IHttpClientFactory httpClientFactory
)
    : ControllerBase
{
    /// <summary>
    /// LTI 1.3 Dynamic Registration endpoint for automatic registration with LTI platforms.
    /// This endpoint is called by LTI platforms (like Moodle) to automatically register this tool.
    /// </summary>
    /// <param name="openIdConfiguration">URL to the platform's OpenID configuration document (required)</param>
    /// <param name="registrationToken">Optional registration token provided by the platform</param>
    /// <returns>HTML page with registration results</returns>
    /// <example>
    /// GET /lti/dynamicregistration?openid_configuration=https://moodle.example.com/mod/lti/openid-configuration.php&registration_token=abc
    /// </example>
    [AllowAnonymous]
    [HttpGet("dynamicregistration")]
    [EndpointSummary("LTI Dynamic Registration")]
    [EndpointDescription("Handles automatic registration with LTI platforms. The platform provides its OpenID configuration URL and this endpoint completes the registration process.")]
    [ProducesResponseType(typeof(string), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(string), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(string), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> DynamicRegistration(
        [FromQuery(Name = "openid_configuration")]
        string openIdConfiguration,
        [FromQuery(Name = "registration_token")]
        string? registrationToken)
    {
        logger.LogInformation("LTI dynamic registration called. openid_configuration={OpenIdConfig} tokenProvided={HasToken}",
            openIdConfiguration, !string.IsNullOrEmpty(registrationToken));

        if (string.IsNullOrWhiteSpace(openIdConfiguration))
        {
            logger.LogWarning("Missing openid_configuration parameter");
            return Content(BuildHtmlPage(false, "Missing openid_configuration parameter"), "text/html");
        }

        try
        {
            // 1) Fetch the platform OpenID configuration (use service to cache/validate)
            var platformConfigResult = await platformConfigService.FetchPlatformConfigurationAsync(openIdConfiguration);
            if (platformConfigResult.IsFailed)
            {
                logger.LogError("Failed to fetch platform configuration: {Error}", platformConfigResult.Errors.FirstOrDefault()?.Message);
                return Content(BuildHtmlPage(false, "Failed to fetch platform configuration"), "text/html");
            }

            var platformConfig = platformConfigResult.Value;

            // 2) Validate issuer: basic check that openid_configuration origin matches issuer
            if (!IsIssuerValid(openIdConfiguration, platformConfig.Issuer))
            {
                logger.LogWarning("Issuer validation failed. OpenIdConfig={OpenIdConfig} Issuer={Issuer}", openIdConfiguration, platformConfig.Issuer);
                return Content(BuildHtmlPage(false, "Platform issuer validation failed"), "text/html");
            }

            // 3) If already registered, return success page (avoid duplicate registration)
            var existing = await platformRegistrationService.GetPlatformRegistrationByIssuerAsync(platformConfig.Issuer);
            if (existing != null)
            {
                logger.LogInformation("Platform already registered: issuer={Issuer} client_id={ClientId}", platformConfig.Issuer, existing.ClientId);
                return Content(BuildHtmlPage(true, "Platform already registered", existing.ClientId), "text/html");
            }

            // 4) Ensure platform supports dynamic registration
            var registrationEndpoint = platformConfig.RegistrationEndpoint;
            if (string.IsNullOrWhiteSpace(registrationEndpoint) && platformConfig.AdditionalData != null)
            {
                if (platformConfig.AdditionalData.TryGetValue("registration_endpoint", out var regObj))
                {
                    registrationEndpoint = regObj switch
                    {
                        JsonElement jsonElement => jsonElement.GetString(),
                        string str => str,
                        _ => registrationEndpoint
                    };
                }
            }

            if (string.IsNullOrWhiteSpace(registrationEndpoint))
            {
                logger.LogError("Platform missing registration_endpoint");
                return Content(BuildHtmlPage(false, "Platform does not support dynamic registration (missing registration_endpoint)"), "text/html");
            }

            // 5) Compose registration payload (IMS/OIDC dynamic registration + LTI tool configuration extension)
            var baseUrl = BuildBaseUrl(Request); // e.g. https://yourtool.example.com
            var registrationPayload = BuildToolRegistrationPayload(baseUrl);

            var jsonOptions = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
            };
            var payloadJson = JsonSerializer.Serialize(registrationPayload, jsonOptions);

            // 6) POST to platform registration endpoint
            using var httpClient = httpClientFactory.CreateClient();
            var request = new HttpRequestMessage(HttpMethod.Post, (string?)registrationEndpoint)
            {
                Content = new StringContent(payloadJson, Encoding.UTF8, "application/json")
            };

            if (!string.IsNullOrEmpty(registrationToken))
            {
                // registration_token is optional; include if present
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", registrationToken);
            }

            logger.LogInformation("Posting registration payload to {RegistrationEndpoint}", registrationEndpoint);
            var response = await httpClient.SendAsync(request);
            var responseContent = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                logger.LogError("Dynamic registration failed. Status={Status} Body={Body}", response.StatusCode, responseContent);
                return Content(BuildHtmlPage(false, $"Registration failed: {response.StatusCode}"), "text/html");
            }

            // 7) Parse registration response; must contain client_id
            var regResponse = JsonSerializer.Deserialize<ToolRegistrationResponse>(responseContent, jsonOptions);
            if (regResponse == null || string.IsNullOrWhiteSpace(regResponse.ClientId))
            {
                logger.LogError("Invalid registration response: {Body}", responseContent);
                return Content(BuildHtmlPage(false, "Invalid registration response - missing client_id"), "text/html");
            }

            // 8) Persist platform registration (client id, deployment id if present, token, endpoints, etc.)
            var platformRegistration = new PlatformRegistration
            {
                Issuer = platformConfig.Issuer,
                ClientId = regResponse.ClientId,
                DeploymentId = regResponse.DeploymentId,
                AuthorizationEndpoint = platformConfig.AuthorizationEndpoint,
                TokenEndpoint = platformConfig.TokenEndpoint,
                JwksUri = platformConfig.JwksUri,
                RegistrationEndpoint = registrationEndpoint,
                RegistrationAccessToken = regResponse.RegistrationAccessToken
            };

            await platformRegistrationService.StorePlatformRegistrationAsync(platformRegistration);

            logger.LogInformation("Registration successful: issuer={Issuer} client_id={ClientId}", platformConfig.Issuer, regResponse.ClientId);
            return Content(BuildHtmlPage(true, "Registration completed successfully", regResponse.ClientId), "text/html");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unexpected error during dynamic registration");
            return Content(BuildHtmlPage(false, "Unexpected error during registration"), "text/html");
        }
    }

    // ---------- Helper Methods ----------

    private static bool IsIssuerValid(string openidConfigurationUrl, string issuer)
    {
        try
        {
            var openid = new Uri(openidConfigurationUrl);
            var iss = new Uri(issuer);

            // minimal but effective check: scheme/host/port must match
            return string.Equals(openid.Scheme, iss.Scheme, StringComparison.OrdinalIgnoreCase)
                   && string.Equals(openid.Host, iss.Host, StringComparison.OrdinalIgnoreCase)
                   && openid.Port == iss.Port;
        }
        catch
        {
            return false;
        }
    }

    private static string BuildBaseUrl(HttpRequest request)
    {
        // Builds a canonical public URL using the incoming request's scheme and host.
        // If you have a proxy/ingress, ensure ForwardedHeaders are configured so this returns the external host.
        var scheme = request.Scheme;
        var host = request.Host.Value; // includes port when non-default
        return $"{scheme}://{host}";
    }

    private ToolRegistrationRequest BuildToolRegistrationPayload(string baseUrl)
    {
        var uri = new Uri(baseUrl);
        var host = uri.Host;
        var scheme = uri.Scheme;
        var port = uri.Port;

        var domain = (port == 80 || port == 443) ? host : $"{host}:{port}";
        var fullBaseUrl = (port == 80 || port == 443) ? $"{scheme}://{host}" : $"{scheme}://{host}:{port}";

        var launchUrl = $"{fullBaseUrl}/lti/launch";
        var loginUrl = $"{fullBaseUrl}/lti/oidc/login";
        var jwksUrl = $"{fullBaseUrl}/.well-known/jwks.json";

        var ret = new ToolRegistrationRequest
        {
            ApplicationType = "web",
            ClientName = "Clinique Ludique",
            RedirectUris = [launchUrl],
            InitiateLoginUri = loginUrl,
            JwksUri = jwksUrl,
            TokenEndpointAuthMethod = "private_key_jwt",
            GrantTypes = ["client_credentials", "implicit"],
            ResponseTypes = ["id_token"],
            Contacts = ["admin@yourtool.example.com"],
            Scope = string.Join(' ', new[]
            {
                "openid",
                "https://purl.imsglobal.org/spec/lti-ags/scope/score",
                "https://purl.imsglobal.org/spec/lti-nrps/scope/contextmembership.readonly"
            }),
            LtiToolConfiguration = new LtiToolConfiguration
            {
                Domain = domain, // Use host:port if non-standard port
                TargetLinkUri = launchUrl,
                Messages =
                [
                    new LtiMessageConfiguration
                    {
                        Type = "LtiResourceLinkRequest",
                        TargetLinkUri = launchUrl,
                        Label = "Clinique Ludique"
                    },
                    new LtiMessageConfiguration
                    {
                        Type = "LtiDeepLinkingRequest",
                        TargetLinkUri = launchUrl,
                        Label = "Clinique Ludique"
                    }
                ],
                Claims = ["iss", "sub", "name", "given_name", "family_name", "email"]
            }
        };

        logger.LogDebug("Built registration payload: {Payload}", JsonSerializer.Serialize(ret));

        return ret;
    }


    private static string BuildHtmlPage(bool success, string message, string? clientId = null)
    {
        // Return a simple HTML page that posts the IMS-standard close message,
        // which Moodle listens to: { subject: 'org.imsglobal.lti.close' }
        var safeMessage = HtmlEncoder.Default.Encode(message);
        var safeClientId = HtmlEncoder.Default.Encode(clientId ?? "");

        return $@"<!doctype html>
<html>
<head>
  <meta charset=""utf-8"">
  <meta name=""viewport"" content=""width=device-width,initial-scale=1"">
  <title>LTI Dynamic Registration</title>
  <style>
    body {{ font-family: -apple-system, Roboto, 'Segoe UI', Arial; background:#f6f7fb; padding:24px; }}
    .card {{ max-width:720px; margin:0 auto; background:#fff; padding:20px; border-radius:8px; box-shadow:0 6px 18px rgba(0,0,0,0.08); }}
    .ok {{ color:#2e7d32; }} .err {{ color:#c62828; }}
  </style>
</head>
<body>
  <div class=""card"">
    <h2>LTI Dynamic Registration</h2>
    <p class=""{(success ? "ok" : "err")}"">{safeMessage}</p>
    {(string.IsNullOrEmpty(clientId) ? "" : $"<p><strong>Client ID:</strong> {safeClientId}</p>")}
    <p>You can close this window.</p>
  </div>

  <script>
    (function() {{
      var payload = {{
        subject: 'org.imsglobal.lti.close',
        success: {(success ? "true" : "false")},
        message: '{JavaScriptEncode(message)}',
        client_id: '{JavaScriptEncode(clientId ?? "")}'
      }};
      try {{
        var target = window.opener || window.parent;
        if (target && target.postMessage) {{
          target.postMessage(payload, '*');
        }}
      }} catch (e) {{
        // ignore errors
      }}
    }})();
  </script>
</body>
</html>";
    }

    // small helper to encode JS string literals
    private static string JavaScriptEncode(string input)
    {
        if (input == null) return "";
        return JavaScriptEncoder.Default.Encode(input).Replace("'", "\\'");
    }
}