using ADG.LTI.Components;
using ADG.LTI.Options;
using ADG.LTI.Services;
using ADG.Server.ServiceDefaults;
using ADG.Server.Shared.Constants;
using AGX.Common.Logging.Serilog;
using Microsoft.OpenApi;
using Scalar.AspNetCore;
using Serilog;

namespace ADG.LTI;

public static class Program
{
    public static async Task Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        LoggingConfig.ConfigureLogger();

        builder.Host.UseSerilog();

        builder.AddServiceDefaults(Project.LtiApi);

        // Add session support
        builder.Services.AddDistributedMemoryCache();
        builder.Services.AddSession(options =>
        {
            options.IdleTimeout = TimeSpan.FromMinutes(30);
            options.Cookie.HttpOnly = true;
            options.Cookie.IsEssential = true;
        });

        // Configure LTI options and services
        builder.Services.AddLtiOptions(builder.Configuration);
        builder.Services.AddHttpClient();
        builder.Services.AddScoped<ILtiJwtValidationService, LtiJwtValidationService>();
        builder.Services.AddSingleton<IRsaKeyService, RsaKeyService>();
        builder.Services.AddScoped<IPlatformRegistrationService, PlatformRegistrationService>();
        builder.Services.AddScoped<IPlatformConfigurationService, PlatformConfigurationService>();

        builder.Services.AddControllers();

        // Add OpenAPI
        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddOpenApi("v1", options =>
        {
            options.AddDocumentTransformer((document, _, _) =>
            {
                document.Info.Title = "ADG LTI API";
                document.Info.Version = "v1";
                return Task.CompletedTask;
            });
            options.OpenApiVersion = OpenApiSpecVersion.OpenApi3_0;
        });

        // CORS for development
        builder.Services.AddCors(options => options.AddDefaultPolicy(policy => policy.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader()));

        // Add Razor Components / Blazor Server
        builder.Services.AddRazorComponents().AddInteractiveServerComponents();

        // Add authentication/authorization
        builder.Services.AddAuthentication(); // configure schemes if needed
        builder.Services.AddAuthorization();

        var app = builder.Build();

        // HTTP pipeline
        if (app.Environment.IsDevelopment())
        {
            app.MapOpenApi();

            app.UseDeveloperExceptionPage();
            app.UseCors();
        }
        else
        {
            app.UseExceptionHandler("/Error");
            app.UseHsts();
        }

        // This sets the PathBase, so the app knows it is behind /lti (works with reverse proxy)
        app.UsePathBase("/lti-api");
        app.UseRouting();

        app.UseHttpsRedirection();
        app.UseStaticFiles();
        app.UseAntiforgery();

        // Allow anonymous access to /openapi/v1.json in Development mode
        if (app.Environment.IsDevelopment())
        {
            app.Map("/openapi/v1.json", async context =>
            {
                context.Response.ContentType = "application/json";
                await context.Response.SendFileAsync("openapi/v1.json");
            }).AllowAnonymous();
        }

        // Map endpoints
        app.MapRazorComponents<App>().AddInteractiveServerRenderMode();
        app.MapStaticAssets();

        app.UseSession();

        app.MapControllers();

        // Map Scalar API
        if (app.Environment.IsDevelopment())
        {
            app.MapScalarApiReference("/scalar", options =>
            {
                options.WithTitle("ADG API");
                options.WithTheme(ScalarTheme.Purple);
                options.WithDefaultHttpClient(ScalarTarget.CSharp, ScalarClient.RestSharp);
            }).AllowAnonymous();
        }
        else
        {
            app.Map("/scalar", context =>
            {
                context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
                return context.Response.WriteAsync($"Scalar API is only available in development. The current environment is {app.Environment.EnvironmentName}.");
            });
        }

        // Ensure RSA keys exist on startup
        using var scope = app.Services.CreateScope();
        var rsaKeyService = scope.ServiceProvider.GetRequiredService<IRsaKeyService>();
        await rsaKeyService.EnsureKeysExistAsync();
        
        await app.RunAsync();
    }
}