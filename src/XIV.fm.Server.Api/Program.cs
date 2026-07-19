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
using XIV.fm.Server.Api.Listening;
using XIV.fm.Server.Api.Telemetry;
using XIV.fm.Server.Application.Abstractions;
using XIV.fm.Server.Application.AccountLinks;
using XIV.fm.Server.Application.Listening;
using XIV.fm.Server.Application.Presence;
using XIV.fm.Server.Application.Relays;
using XIV.fm.Server.Application.Sync;
using XIV.fm.Server.Domain.Installations;
using XIV.fm.Server.Infrastructure.AccountLinks;
using XIV.fm.Server.Infrastructure.Authentication;
using XIV.fm.Server.Infrastructure.LastFm;
using XIV.fm.Server.Infrastructure.Listening;
using XIV.fm.Server.Infrastructure.Persistence;
using XIV.fm.Server.Infrastructure.Presence;
using XIV.fm.Server.Infrastructure.Relays;

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
    builder.Services.AddSingleton<InMemoryAccountLinkStore>();
    builder.Services.AddSingleton<IAccountLinkStore>(
        services => services.GetRequiredService<InMemoryAccountLinkStore>());
    builder.Services.AddSingleton<ILinkedAccountResolver>(
        services => services.GetRequiredService<InMemoryAccountLinkStore>());
    builder.Services.AddSingleton<InMemoryListeningStateStore>();
    builder.Services.AddSingleton<IListeningStateStore>(
        services => services.GetRequiredService<InMemoryListeningStateStore>());
    builder.Services.AddSingleton<IListeningPollLeaseStore, InMemoryListeningPollLeaseStore>();
    builder.Services.AddSingleton<IPublicPresenceSnapshotCache, InMemoryPublicPresenceSnapshotCache>();
    builder.Services.AddSingleton<IRelayPresenceSnapshotCache, InMemoryRelayPresenceSnapshotCache>();
    builder.Services.AddSingleton<InMemoryRelayStore>();
    builder.Services.AddSingleton<IRelayStore>(services => services.GetRequiredService<InMemoryRelayStore>());
    builder.Services.AddSingleton<LastFmRequestBudget>();
    builder.Services.AddSingleton<ILastFmRequestBudget>(
        services => services.GetRequiredService<LastFmRequestBudget>());
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
    builder.Services.AddSingleton<PostgresAccountLinkStore>();
    builder.Services.AddSingleton<IAccountLinkStore>(
        services => services.GetRequiredService<PostgresAccountLinkStore>());
    builder.Services.AddSingleton<ILinkedAccountResolver>(
        services => services.GetRequiredService<PostgresAccountLinkStore>());
    builder.Services.AddSingleton<IListeningStateStore, RedisListeningStateStore>();
    builder.Services.AddSingleton<IListeningPollLeaseStore, RedisListeningPollLeaseStore>();
    builder.Services.AddSingleton<IPublicPresenceSnapshotCache, RedisPublicPresenceSnapshotCache>();
    builder.Services.AddSingleton<IRelayPresenceSnapshotCache, RedisRelayPresenceSnapshotCache>();
    builder.Services.AddSingleton<IRelayStore, PostgresRelayStore>();
    builder.Services.AddSingleton<ILastFmRequestBudget, RedisLastFmRequestBudget>();
    healthChecks.AddCheck<DurableStoresHealthCheck>("durable_stores", tags: ["ready"]);
}

var publicBaseUrl = builder.Configuration["XIVFM_PUBLIC_BASE_URL"];
if (string.IsNullOrWhiteSpace(publicBaseUrl) &&
    (builder.Environment.IsDevelopment() || builder.Environment.IsEnvironment("Testing")))
{
    publicBaseUrl = "http://127.0.0.1:5080";
}
if (!Uri.TryCreate(publicBaseUrl, UriKind.Absolute, out var publicBaseUri) ||
    (publicBaseUri.Scheme != Uri.UriSchemeHttp && publicBaseUri.Scheme != Uri.UriSchemeHttps) ||
    publicBaseUri.AbsolutePath != "/" ||
    !string.IsNullOrEmpty(publicBaseUri.Query) ||
    !string.IsNullOrEmpty(publicBaseUri.Fragment) ||
    (publicBaseUri.Scheme != Uri.UriSchemeHttps && !builder.Environment.IsDevelopment() &&
        !builder.Environment.IsEnvironment("Testing")))
{
    throw new InvalidOperationException("XIVFM_PUBLIC_BASE_URL must be an HTTPS origin without a path.");
}

builder.Services.AddSingleton(new AccountLinkOptions(publicBaseUri, AccountLinkOptions.DefaultLifetime));
builder.Services.AddSingleton(new LastFmAuthorizationOptions(
    builder.Configuration["XIVFM_LASTFM_API_KEY"],
    builder.Configuration["XIVFM_LASTFM_SHARED_SECRET"],
    LastFmAuthorizationOptions.DefaultApiBaseUri,
    LastFmAuthorizationOptions.DefaultBrowserBaseUri));
builder.Services.AddHttpClient<LastFmAuthorizationClient>(ConfigureLastFmHttpClient);
builder.Services.AddSingleton<ILastFmAuthorizationClient>(services =>
    services.GetRequiredService<LastFmAuthorizationClient>());
builder.Services.AddHttpClient<LastFmRecentTracksClient>(ConfigureLastFmHttpClient);
builder.Services.AddSingleton<ILastFmRecentTracksClient>(services =>
    services.GetRequiredService<LastFmRecentTracksClient>());
builder.Services.AddSingleton(ListeningPollingOptions.Default);
builder.Services.AddSingleton<ListeningFreshnessPolicy>();
builder.Services.AddSingleton<ListeningPollingCoordinator>();
builder.Services.AddSingleton<IListeningPollingCoordinator>(services =>
    services.GetRequiredService<ListeningPollingCoordinator>());
builder.Services.AddHostedService<ListeningPollingHostedService>();
builder.Services.AddSingleton<AccountLinkApplicationService>();
builder.Services.AddSingleton(CreateRelayOptions(builder.Configuration));
builder.Services.AddSingleton<RelayApplicationService>();
builder.Services.AddSingleton<PublicPresenceSnapshotService>();
builder.Services.AddSingleton<RelayPresenceSnapshotService>();
builder.Services.AddSingleton<SyncApplicationService>();
builder.Services.AddSingleton<XivFmTelemetry>();
builder.Services.AddSingleton<IListeningPollingTelemetry>(
    services => services.GetRequiredService<XivFmTelemetry>());
builder.Services.AddSingleton<IPresenceSnapshotTelemetry>(
    services => services.GetRequiredService<XivFmTelemetry>());
builder.Services.AddSingleton<IRelayTelemetry>(
    services => services.GetRequiredService<XivFmTelemetry>());
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
    options.AddPolicy("account-link-start", httpContext => CreateRateLimiterPartition(httpContext, 5));
    options.AddPolicy("account-link-status", httpContext => CreateRateLimiterPartition(httpContext, 120));
    options.AddPolicy("account-link-callback", httpContext => CreateRateLimiterPartition(httpContext, 10));
    options.AddPolicy("relay-read", httpContext => CreateRateLimiterPartition(httpContext, 120));
    options.AddPolicy("relay-creation", httpContext => CreateIpRateLimiterPartition(httpContext, 10));
    options.AddPolicy("relay-mutation", httpContext => CreateRateLimiterPartition(httpContext, 30));
    options.AddPolicy("relay-invitation", httpContext => CreateRateLimiterPartition(httpContext, 30));
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
app.MapAccountLinkEndpoints();
app.MapRelayEndpoints();

await app.RunAsync();

static RelayOptions CreateRelayOptions(IConfiguration configuration)
{
    var defaults = RelayOptions.Default;
    var options = new RelayOptions(
        configuration.GetValue<int?>("XIVFM_RELAYS:MaximumActiveOwnedRelays") ?? defaults.MaximumActiveOwnedRelays,
        configuration.GetValue<int?>("XIVFM_RELAYS:MaximumCreationsPerRollingWindow") ?? defaults.MaximumCreationsPerRollingWindow,
        TimeSpan.FromDays(configuration.GetValue<int?>("XIVFM_RELAYS:CreationRollingWindowDays") ?? (int)defaults.CreationRollingWindow.TotalDays),
        TimeSpan.FromSeconds(configuration.GetValue<int?>("XIVFM_RELAYS:CreationBurstWindowSeconds") ?? (int)defaults.CreationBurstWindow.TotalSeconds),
        configuration.GetValue<int?>("XIVFM_RELAYS:MaximumJoinedRelays") ?? defaults.MaximumJoinedRelays,
        configuration.GetValue<int?>("XIVFM_RELAYS:MaximumMembersPerRelay") ?? defaults.MaximumMembersPerRelay,
        configuration.GetValue<int?>("XIVFM_RELAYS:MaximumActiveInvitationsPerRelay") ?? defaults.MaximumActiveInvitationsPerRelay,
        TimeSpan.FromHours(configuration.GetValue<int?>("XIVFM_RELAYS:InvitationLifetimeHours") ?? (int)defaults.InvitationLifetime.TotalHours),
        TimeSpan.FromHours(configuration.GetValue<int?>("XIVFM_RELAYS:MaximumInvitationLifetimeHours") ?? (int)defaults.MaximumInvitationLifetime.TotalHours),
        configuration.GetValue<int?>("XIVFM_RELAYS:MaximumSelectedRelays") ?? defaults.MaximumSelectedRelays);
    if (options.MaximumActiveOwnedRelays <= 0 || options.MaximumCreationsPerRollingWindow <= 0 ||
        options.CreationRollingWindow <= TimeSpan.Zero || options.CreationBurstWindow < TimeSpan.Zero ||
        options.MaximumJoinedRelays <= 0 || options.MaximumMembersPerRelay <= 0 ||
        options.MaximumActiveInvitationsPerRelay <= 0 || options.InvitationLifetime <= TimeSpan.Zero ||
        options.MaximumInvitationLifetime < options.InvitationLifetime || options.MaximumSelectedRelays is < 1 or > 5)
    {
        throw new InvalidOperationException("XIVFM_RELAYS limits are invalid; selected Relays cannot exceed the frozen v1 maximum of five.");
    }
    return options;
}

static void ConfigureLastFmHttpClient(HttpClient client)
{
    client.Timeout = TimeSpan.FromSeconds(10);
    client.MaxResponseContentBufferSize = 64 * 1024;
    client.DefaultRequestHeaders.UserAgent.ParseAdd("XIV.fm/0.1");
}

static RateLimitPartition<string> CreateIpRateLimiterPartition(HttpContext context, int permitsPerMinute)
{
    var partitionKey = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    return CreateFixedWindowPartition(partitionKey, permitsPerMinute);
}

static RateLimitPartition<string> CreateRateLimiterPartition(HttpContext context, int permitsPerMinute)
{
    var installationId = context.User.FindFirst(InstallationAuthenticationHandler.InstallationIdClaim)?.Value;
    var partitionKey = installationId ?? context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    return CreateFixedWindowPartition(partitionKey, permitsPerMinute);
}

static RateLimitPartition<string> CreateFixedWindowPartition(string partitionKey, int permitsPerMinute) =>
    RateLimitPartition.GetFixedWindowLimiter(
        partitionKey,
        _ => new FixedWindowRateLimiterOptions
        {
            AutoReplenishment = true,
            PermitLimit = permitsPerMinute,
            QueueLimit = 0,
            Window = TimeSpan.FromMinutes(1),
        });

public partial class Program;
