using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using StackExchange.Redis;
using XIV.fm.Server.Api.Authentication;
using XIV.fm.Server.Api.Endpoints;
using XIV.fm.Server.Api.Health;
using XIV.fm.Server.Api.Http;
using XIV.fm.Server.Api.Telemetry;
using XIV.fm.Server.Application.Abstractions;
using XIV.fm.Server.Application.Sync;
using XIV.fm.Server.Domain.Installations;
using XIV.fm.Server.Infrastructure.Authentication;
using XIV.fm.Server.Infrastructure.Persistence;
using XIV.fm.Server.Infrastructure.Presence;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = 16 * 1024);
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow;
});
builder.Services.Configure<RouteHandlerOptions>(options => options.ThrowOnBadRequest = true);

builder.Services.AddSingleton(TimeProvider.System);
var storageMode = builder.Configuration["XIVFM_STORAGE_MODE"];
var useInMemoryStorage = string.IsNullOrWhiteSpace(storageMode)
    ? builder.Environment.IsDevelopment() || builder.Environment.IsEnvironment("Testing")
    : string.Equals(storageMode, "memory", StringComparison.OrdinalIgnoreCase);
if (!useInMemoryStorage && !string.IsNullOrWhiteSpace(storageMode) &&
    !string.Equals(storageMode, "durable", StringComparison.OrdinalIgnoreCase))
{
    throw new InvalidOperationException("XIVFM_STORAGE_MODE must be 'memory' or 'durable'.");
}

var healthChecks = builder.Services.AddHealthChecks();
if (useInMemoryStorage)
{
    if (!builder.Environment.IsDevelopment() && !builder.Environment.IsEnvironment("Testing"))
        throw new InvalidOperationException("In-memory storage is allowed only in Development or Testing.");

    builder.Services.AddSingleton<InMemoryInstallationCredentialStore>();
    builder.Services.AddSingleton<IInstallationCredentialStore>(
        services => services.GetRequiredService<InMemoryInstallationCredentialStore>());
    builder.Services.AddSingleton<InMemoryPresenceStore>();
    builder.Services.AddSingleton<IPresenceStore>(services => services.GetRequiredService<InMemoryPresenceStore>());
    healthChecks.AddCheck<InMemoryStoresHealthCheck>("in_memory_stores", tags: ["ready"]);
}
else
{
    var postgresConnection = builder.Configuration.GetConnectionString("Postgres")
        ?? throw new InvalidOperationException("ConnectionStrings:Postgres is required for durable storage.");
    var redisConnection = builder.Configuration.GetConnectionString("Redis")
        ?? throw new InvalidOperationException("ConnectionStrings:Redis is required for durable storage.");
    builder.Services.AddPooledDbContextFactory<XivFmDbContext>(
        options => options.UseNpgsql(postgresConnection));
    builder.Services.AddSingleton<IConnectionMultiplexer>(_ =>
    {
        var options = ConfigurationOptions.Parse(redisConnection);
        options.AbortOnConnectFail = false;
        return ConnectionMultiplexer.Connect(options);
    });
    builder.Services.AddSingleton<IInstallationCredentialStore, PostgresInstallationCredentialStore>();
    builder.Services.AddSingleton<IPresenceStore, RedisPresenceStore>();
    healthChecks.AddCheck<DurableStoresHealthCheck>("durable_stores", tags: ["ready"]);
}

builder.Services.AddSingleton<SyncApplicationService>();
builder.Services.AddSingleton<XivFmTelemetry>();
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
            "route_rate_limit_exceeded",
            "The route rate limit was exceeded.",
            cancellationToken: cancellationToken).ConfigureAwait(false);
    };
    options.AddPolicy("sync", httpContext => CreateRateLimiterPartition(httpContext, 120));
    options.AddPolicy("installation-lifecycle", httpContext => CreateRateLimiterPartition(httpContext, 10));
});

var app = builder.Build();
if (!useInMemoryStorage && app.Configuration.GetValue<bool>("XIVFM_APPLY_MIGRATIONS"))
{
    await using var database = await app.Services
        .GetRequiredService<IDbContextFactory<XivFmDbContext>>()
        .CreateDbContextAsync(CancellationToken.None);
    await database.Database.MigrateAsync(CancellationToken.None);
}
var developmentCredential = app.Configuration["XIVFM_DEV_INSTALLATION_CREDENTIAL"];
if (!string.IsNullOrEmpty(developmentCredential))
{
    if (!app.Environment.IsDevelopment())
        throw new InvalidOperationException("The development installation credential is allowed only in Development.");

    var credentialStore = app.Services.GetRequiredService<IInstallationCredentialStore>();
    if (await credentialStore.AuthenticateAsync(developmentCredential, CancellationToken.None) is null)
    {
        await credentialStore.RegisterAsync(
            new InstallationId(Guid.NewGuid()),
            developmentCredential,
            CancellationToken.None);
    }
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
app.MapInstallationEndpoints();

await app.RunAsync();

static RateLimitPartition<string> CreateRateLimiterPartition(HttpContext context, int permitsPerMinute)
{
    var installationId = context.User.FindFirst(InstallationAuthenticationHandler.InstallationIdClaim)?.Value;
    var partitionKey = installationId ?? context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    return RateLimitPartition.GetFixedWindowLimiter(
        partitionKey,
        _ => new FixedWindowRateLimiterOptions
        {
            AutoReplenishment = true,
            PermitLimit = permitsPerMinute,
            QueueLimit = 0,
            Window = TimeSpan.FromMinutes(1),
        });
}

public partial class Program;
