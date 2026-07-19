using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.RateLimiting;
using XIV.fm.Server.Api.Authentication;
using XIV.fm.Server.Api.Endpoints;
using XIV.fm.Server.Api.Health;
using XIV.fm.Server.Api.Http;
using XIV.fm.Server.Api.Telemetry;
using XIV.fm.Server.Application.Abstractions;
using XIV.fm.Server.Application.Sync;
using XIV.fm.Server.Domain.Installations;
using XIV.fm.Server.Infrastructure.Authentication;
using XIV.fm.Server.Infrastructure.Presence;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = 16 * 1024);
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow;
});
builder.Services.Configure<RouteHandlerOptions>(options => options.ThrowOnBadRequest = true);

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<InMemoryInstallationCredentialStore>();
builder.Services.AddSingleton<IInstallationCredentialStore>(
    services => services.GetRequiredService<InMemoryInstallationCredentialStore>());
builder.Services.AddSingleton<InMemoryPresenceStore>();
builder.Services.AddSingleton<IPresenceStore>(services => services.GetRequiredService<InMemoryPresenceStore>());
builder.Services.AddSingleton<SyncApplicationService>();
builder.Services.AddSingleton<XivFmTelemetry>();
builder.Services.AddHealthChecks()
    .AddCheck<InMemoryStoresHealthCheck>("in_memory_stores", tags: ["ready"]);
builder.Services.AddAuthentication(InstallationAuthenticationHandler.SchemeName)
    .AddScheme<AuthenticationSchemeOptions, InstallationAuthenticationHandler>(
        InstallationAuthenticationHandler.SchemeName,
        _ => { });
builder.Services.AddAuthorization();
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.OnRejected = async (context, cancellationToken) =>
    {
        context.HttpContext.RequestServices.GetRequiredService<XivFmTelemetry>().RecordRateLimitedRequest();
        if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
        {
            context.HttpContext.Response.Headers.RetryAfter =
                Math.Max(1, (int)Math.Ceiling(retryAfter.TotalSeconds)).ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        await ApiErrorWriter.WriteAsync(
            context.HttpContext,
            StatusCodes.Status429TooManyRequests,
            "sync_rate_limit_exceeded",
            "The sync rate limit was exceeded.",
            cancellationToken: cancellationToken).ConfigureAwait(false);
    };
    options.AddPolicy("sync", httpContext =>
    {
        var installationId = httpContext.User.FindFirst(InstallationAuthenticationHandler.InstallationIdClaim)?.Value;
        var partitionKey = installationId ?? httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        return RateLimitPartition.GetFixedWindowLimiter(
            partitionKey,
            _ => new FixedWindowRateLimiterOptions
            {
                AutoReplenishment = true,
                PermitLimit = 120,
                QueueLimit = 0,
                Window = TimeSpan.FromMinutes(1),
            });
    });
});

var app = builder.Build();
var developmentCredential = app.Configuration["XIVFM_DEV_INSTALLATION_CREDENTIAL"];
if (!string.IsNullOrEmpty(developmentCredential))
{
    if (!app.Environment.IsDevelopment())
        throw new InvalidOperationException("The development installation credential is allowed only in Development.");

    var installationId = new InstallationId(Guid.NewGuid());
    await app.Services.GetRequiredService<IInstallationCredentialStore>().RegisterAsync(
        installationId,
        developmentCredential,
        CancellationToken.None);
}

app.UseMiddleware<RequestIdMiddleware>();
app.UseMiddleware<ApiExceptionMiddleware>();
app.UseStatusCodePages(async statusContext =>
{
    var response = statusContext.HttpContext.Response;
    if (response.StatusCode == StatusCodes.Status404NotFound)
    {
        await ApiErrorWriter.WriteAsync(
            statusContext.HttpContext,
            StatusCodes.Status404NotFound,
            "route_not_found",
            "The requested route does not exist.",
            cancellationToken: statusContext.HttpContext.RequestAborted).ConfigureAwait(false);
    }
});
app.UseAuthentication();
app.UseRateLimiter();
app.UseAuthorization();

app.MapHealthChecks(
    "/health/live",
    new HealthCheckOptions
    {
        Predicate = _ => false,
        ResponseWriter = HealthResponseWriter.WriteAsync,
    });
app.MapHealthChecks(
    "/health/ready",
    new HealthCheckOptions
    {
        Predicate = registration => registration.Tags.Contains("ready"),
        ResponseWriter = HealthResponseWriter.WriteAsync,
    });
app.MapSyncEndpoint();

await app.RunAsync();

public partial class Program;
