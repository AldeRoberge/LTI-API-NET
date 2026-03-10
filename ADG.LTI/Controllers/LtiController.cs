using System.Net;
using ADG.LTI.Options;
using ADG.LTI.Services;
using ADG.Server.Shared.Infrastructure.Options;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace ADG.LTI.Controllers;

/// <summary>
/// The main LTI 1.3 controller handling core LTI operations.
/// Provides endpoints for OIDC login, launch handling, OpenID configuration, and JWKS.
/// </summary>
[ApiController]
[Route("lti")]
public class LtiController(
    ILogger<LtiController> logger,
    IOptionsMonitor<LtiOptions> ltiConfig,
    ILtiJwtValidationService jwtValidationService,
    IRsaKeyService rsaKeyService,
    IOptionsMonitor<InfrastructureOptions> infrastructureOptions,
    IPlatformRegistrationService platformRegistrationService)
    : ControllerBase
{
    private LtiOptions LtiConfig => ltiConfig.CurrentValue;


    /// <summary>
    /// OIDC login initiation endpoint for LTI 1.3.
    /// Handles the initial authentication request from an LTI platform (e.g., Moodle).
    /// </summary>
    [AllowAnonymous]
    [HttpPost("oidc/login")]
    [EndpointSummary("OIDC login initiation")]
    [EndpointDescription("Initiates the OpenID Connect login flow for LTI 1.3. Receives login parameters from the LTI platform and redirects to the platform's authorization endpoint.")]
    [ProducesResponseType(StatusCodes.Status302Found)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public IActionResult Login([FromForm] LtiLoginRequest request)
    {
        logger.LogInformation("LTI login request received from issuer: {Issuer}", request.Issuer);

        // Basic validation
        if (string.IsNullOrEmpty(request.LoginHint))
        {
            logger.LogWarning("Missing login_hint in LTI login request");
            return BadRequest("Missing login_hint");
        }

        if (string.IsNullOrEmpty(request.TargetLinkUri))
        {
            logger.LogWarning("Missing target_link_uri in LTI login request");
            return BadRequest("Missing target_link_uri");
        }

        // Generate state and nonce for security
        var state = Guid.NewGuid().ToString("N");
        var nonce = Guid.NewGuid().ToString("N");

        // Store state and nonce for validation (in production, use distributed cache or database)
        HttpContext.Session.SetString("lti_state", state);
        HttpContext.Session.SetString("lti_nonce", nonce);

        // Build the OIDC authorization request URL
        var redirectUrl = $"{LtiConfig.MoodleAuthorizationEndpoint}?" +
                          $"scope=openid" + // <-- REQUIRED
                          $"&response_type=id_token" +
                          $"&response_mode=form_post" +
                          $"&prompt=none" +
                          $"&client_id={Uri.EscapeDataString(LtiConfig.ClientId)}" +
                          $"&redirect_uri={Uri.EscapeDataString(LtiConfig.RedirectionUrl)}" +
                          $"&login_hint={Uri.EscapeDataString(request.LoginHint)}" +
                          $"&lti_message_hint={Uri.EscapeDataString(request.LtiMessageHint)}" +
                          $"&state={state}" +
                          $"&nonce={nonce}";


        logger.LogInformation("Redirecting to Moodle Authorization Endpoint: {RedirectUrl}", redirectUrl);

        return Redirect(redirectUrl);
    }

    /// <summary>
    /// LTI launch endpoint that handles the JWT token from the LTI platform.
    /// Validates the JWT token and extracts user information for authentication.
    /// </summary>
    [AllowAnonymous]
    [HttpPost("launch")]
    [EndpointSummary("LTI launch handler")]
    [EndpointDescription("Handles the LTI launch request from the platform. Validates the JWT token containing user information and platform details.")]
    [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> Launch()
    {
        logger.LogInformation("LTI launch received at {Time}", DateTime.UtcNow);


        try
        {
            // Ensure that we don't receive a 'error' parameter in the form data
            if (!string.IsNullOrEmpty(Request.Form["error"]))
            {
                var error = Request.Form["error"].ToString();
                var errorDescription = Request.Form["state"].ToString();
                logger.LogWarning("LTI launch error received: {Error}, Description: {Description}", error, errorDescription);

                // if the error is "unauthorized_client", it usually means that the client_id is not recognized by Moodle
                if (error == "unauthorized_client")
                {
                    logger.LogError("The client_id is not recognized by Moodle. " +
                                    "Please ensure that the client_id is correctly configured in Moodle and in the appsettings.json.");

                    return BadRequest($"Received error from Moodle: {error}. Make sure the client_id provided from Moodle was added in the appsettings.json.");
                }

                return BadRequest($"Received error from Moodle: {error} - {errorDescription}");
            }

            // Extract the id_token from the form data
            var idToken = Request.Form["id_token"].ToString();
            var state = Request.Form["state"].ToString();

            if (string.IsNullOrEmpty(idToken))
            {
                // print the content of the form for debugging
                foreach (var key in Request.Form.Keys)
                {
                    logger.LogInformation("Form key: {Key}, Value: {Value}", key, Request.Form[key]);
                }

                logger.LogWarning("Missing id_token in LTI launch request");
                return BadRequest("Missing id_token");
            }

            // Validate state (in production, implement proper state validation)
            var storedState = HttpContext.Session.GetString("lti_state");
            if (string.IsNullOrEmpty(storedState) || storedState != state)
            {
                logger.LogWarning("Invalid state parameter in LTI launch");
                return BadRequest("Invalid state parameter");
            }

            logger.LogInformation("id_token received: {TokenLength} characters", idToken.Length);

            // Validate JWT and extract user information
            var userInfoResult = await jwtValidationService.ValidateAndExtractClaimsAsync(idToken);

            if (userInfoResult.IsFailed)
            {
                logger.LogError("JWT validation failed: {Errors}", string.Join(", ", userInfoResult.Errors.Select(e => e.Message)));
                return BadRequest($"JWT validation failed: {string.Join(", ", userInfoResult.Errors.Select(e => e.Message))}");
            }

            var userInfo = userInfoResult.Value;

            // Validate deployment ID and issuer
            var payload = await jwtValidationService.ValidateJwtAsync(idToken);
            var deploymentClaim = payload.RootElement.GetProperty("https://purl.imsglobal.org/spec/lti/claim/deployment_id").GetString();
            var issuerClaim = payload.RootElement.GetProperty("iss").GetString();
            var audienceClaim = payload.RootElement.GetProperty("aud").GetString();

            // Check if this is a dynamically registered platform
            var dynamicRegistration = await platformRegistrationService.GetPlatformRegistrationByIssuerAsync(issuerClaim!);

            if (dynamicRegistration != null)
            {
                // Validate against dynamic registration
                if (!string.IsNullOrEmpty(dynamicRegistration.DeploymentId) &&
                    deploymentClaim != dynamicRegistration.DeploymentId)
                {
                    logger.LogWarning("Invalid deployment ID for dynamic registration: {DeploymentId}, expected: {ExpectedDeploymentId}",
                        deploymentClaim, dynamicRegistration.DeploymentId);
                    return BadRequest("Invalid deployment for registered platform");
                }

                logger.LogInformation("Using dynamic registration for platform: {Issuer}, ClientId: {ClientId}",
                    dynamicRegistration.Issuer, dynamicRegistration.ClientId);
            }
            else
            {
                // Fall back to static configuration validation
                if (deploymentClaim != LtiConfig.DeploymentId)
                {
                    logger.LogWarning("Invalid deployment ID: {DeploymentId}", deploymentClaim);
                    return BadRequest("Invalid deployment");
                }

                logger.LogInformation("Using static configuration for platform validation");
            }

            logger.LogInformation("LTI user authenticated: UserId={UserId}, Name={UserName}, Email={Email}",
                userInfo.UserId, userInfo.UserName, userInfo.UserEmail);

            // Clear session data
            HttpContext.Session.Remove("lti_state");
            HttpContext.Session.Remove("lti_nonce");

            // In a real application, you would:
            // 1. Create/update user in your system
            // 2. Create authentication cookie/session
            // 3. Redirect to your application

            /*return Ok(new
            {
                message = "LTI launch successful",
                user = new
                {
                    userId = userInfo.UserId,
                    userName = userInfo.UserName,
                    userEmail = userInfo.UserEmail,
                    givenName = userInfo.GivenName,
                    familyName = userInfo.FamilyName
                },
                timestamp = DateTime.UtcNow
            });*/


            // Create a simple cookie that will be


            // store id_token in secure cookie
            Response.Cookies.Append("lti_id_token", idToken, new CookieOptions
            {
                HttpOnly = true,
                Secure = Request.IsHttps,
                SameSite = SameSiteMode.Lax,
                Expires = DateTimeOffset.UtcNow.AddHours(1) // match token lifetime
            });

            // Return a small HTML page that greets the user and redirects in 3 seconds ---
            var safeName = WebUtility.HtmlEncode(userInfo.UserName);

            var playPath = GetPlayPath();

            logger.LogInformation("Redirecting user {UserName} to play page at {PlayPath}", userInfo.UserName, playPath);

            var html =
                $$"""
                  <!doctype html>
                              <html lang='en'>
                              <head>
                                <meta charset='utf-8'>
                                <meta name='viewport' content='width=device-width,initial-scale=1'>
                                <title>Welcome back</title>
                                <meta http-equiv='refresh' content='3;url={{WebUtility.HtmlEncode(playPath)}}'>
                                <style>
                                  body{font-family:system-ui,-apple-system,Segoe UI,Roboto,'Helvetica Neue',Arial; background:#f6f8fb; margin:0;}
                                  .card{max-width:640px;margin:8vh auto;padding:2.5rem;border-radius:12px;background:#fff;box-shadow:0 8px 30px rgba(20,24,31,0.08);text-align:center}
                                  h1{margin:0 0 0.5rem;font-size:1.8rem}
                                  p.sub{color:#6b7280;margin:0 0 1rem}
                                  a.cta{display:inline-block;margin-top:1rem;padding:0.6rem 1rem;border-radius:8px;text-decoration:none;border:1px solid #e5e7eb}
                                  .card-logo{height:64px;}
                                </style>
                              </head>
                              <body>
                                <div class='card'>
                                  <img class='card-logo' src='/images/logo/logo.png' alt='The Clinique Ludique Logo'/>
                                  <h1>Welcome back, {{safeName}}!</h1>
                                  <p class='sub'>You are signed in. We'll take you to the play page in a short moment.</p>
                                  <p><a class='cta' href='{{WebUtility.HtmlEncode(playPath)}}'>Go to play page now</a></p>
                                </div>
                                <script>
                                  // fallback JS redirect after 500ms
                                  setTimeout(function() { window.location.href = '{{WebUtility.HtmlEncode(playPath)}}'; }, 500);
                                </script>
                              </body>
                              </html>
                  """;

            return Content(html, "text/html");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error processing LTI launch");
            return StatusCode(500, new { error = "Internal server error", message = ex.Message });
        }
    }

    private string GetPlayPath()
    {
        var playPath = $"{infrastructureOptions.CurrentValue.HostUrl}play-from-moodle"; // change to your play page path or make configurable

        // Ensure that the url contains HTTPS at the start
        playPath = playPath.Trim();

        // If URL starts with http://, replace it with https://
        if (playPath.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
        {
            playPath = "https://" + playPath[7..]; // remove "http://"
        }
        // If URL has no scheme, prepend https://
        else if (!playPath.Contains("://"))
        {
            playPath = "https://" + playPath;
        }

        if (string.IsNullOrEmpty(infrastructureOptions.CurrentValue.HostUrl))
        {
            logger.LogError("HostUrl is not configured in InfrastructureOptions. Please set the HostUrl in the configuration. This will result in an incorrect redirect URL.");
        }

        return playPath;
    }

    /// <summary>
    /// OpenID Connect Discovery document endpoint.
    /// Provides metadata about the OpenID Connect configuration for LTI platforms.
    /// </summary>
    [AllowAnonymous]
    [HttpGet(".well-known/openid-configuration")]
    [EndpointSummary("OpenID Connect Discovery")]
    [EndpointDescription("Returns the OpenID Connect Discovery document containing configuration metadata for LTI platforms to discover endpoints and capabilities.")]
    [ProducesResponseType(typeof(OpenIdConfiguration), StatusCodes.Status200OK)]
    public IActionResult OpenIdConfiguration()
    {
        var baseUrl = $"{Request.Scheme}://{Request.Host}";

        logger.LogInformation("Serving OpenID Configuration from base URL: {BaseUrl}", baseUrl);

        var config = new OpenIdConfiguration
        {
            Issuer = LtiConfig.Issuer,
            AuthorizationEndpoint = $"{baseUrl}/api/lti/login",
            TokenEndpoint = $"{baseUrl}/api/lti/token",
            UserinfoEndpoint = $"{baseUrl}/api/lti/userinfo",
            JwksUri = $"{baseUrl}/api/.well-known/jwks.json",
            ResponseTypesSupported = ["id_token"],
            SubjectTypesSupported = ["public"],
            IdTokenSigningAlgValuesSupported = ["RS256"],
            ScopesSupported = ["openid"],
            ClaimsSupported =
            [
                "sub", "name", "email", "given_name", "family_name",
                "https://purl.imsglobal.org/spec/lti/claim/deployment_id",
                "https://purl.imsglobal.org/spec/lti/claim/message_type",
                "https://purl.imsglobal.org/spec/lti/claim/version"
            ]
        };

        return Ok(config);
    }

    /// <summary>
    /// JSON Web Key Set (JWKS) endpoint for JWT verification.
    /// Provides public keys used by LTI platforms to verify JWT tokens.
    /// </summary>
    [AllowAnonymous]
    [HttpGet(".well-known/jwks.json")]
    [EndpointSummary("JSON Web Key Set")]
    [EndpointDescription("Returns the JSON Web Key Set containing public keys for JWT token verification by LTI platforms.")]
    [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> Jwks()
    {
        try
        {
            var jwks = await rsaKeyService.GetPublicKeysAsync();
            return Ok(jwks);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error serving JWKS");
            return StatusCode(500, new { error = "Failed to retrieve public keys" });
        }
    }
}