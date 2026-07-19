using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using XIV.fm.Server.Api.Http;
using XIV.fm.Server.Api.Telemetry;
using XIV.fm.Server.Application.Abstractions;

namespace XIV.fm.Server.Api.Authentication;

public sealed class InstallationAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = "InstallationBearer";
    public const string InstallationIdClaim = "xivfm:installation_id";

    private readonly IInstallationCredentialStore credentialStore;
    private readonly XivFmTelemetry telemetry;

    public InstallationAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        IInstallationCredentialStore credentialStore,
        XivFmTelemetry telemetry)
        : base(options, logger, encoder)
    {
        this.credentialStore = credentialStore;
        this.telemetry = telemetry;
    }

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var authorizationValues = this.Request.Headers.Authorization;
        if (authorizationValues.Count == 0)
            return AuthenticateResult.NoResult();
        if (authorizationValues.Count != 1)
            return AuthenticateResult.Fail("Exactly one Authorization header is required.");

        var authorization = authorizationValues.ToString();
        const string prefix = "Bearer ";
        if (!authorization.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return AuthenticateResult.Fail("A Bearer credential is required.");

        var credential = authorization[prefix.Length..];
        if (credential.Length == 0 || !string.Equals(credential, credential.Trim(), StringComparison.Ordinal))
            return AuthenticateResult.Fail("The Bearer credential is malformed.");

        var installationId = await this.credentialStore.AuthenticateAsync(
            credential,
            this.Context.RequestAborted).ConfigureAwait(false);
        if (installationId is null)
            return AuthenticateResult.Fail("The installation credential is invalid or revoked.");

        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, installationId.Value.ToString()),
            new Claim(InstallationIdClaim, installationId.Value.Value.ToString("D")),
        };
        var identity = new ClaimsIdentity(claims, SchemeName);
        var principal = new ClaimsPrincipal(identity);
        return AuthenticateResult.Success(new AuthenticationTicket(principal, SchemeName));
    }

    protected override async Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        this.telemetry.RecordAuthenticationFailure();
        await ApiErrorWriter.WriteAsync(
            this.Context,
            StatusCodes.Status401Unauthorized,
            "installation_credential_required",
            "A valid installation credential is required.",
            cancellationToken: this.Context.RequestAborted).ConfigureAwait(false);
    }

    protected override async Task HandleForbiddenAsync(AuthenticationProperties properties)
    {
        await ApiErrorWriter.WriteAsync(
            this.Context,
            StatusCodes.Status403Forbidden,
            "installation_forbidden",
            "The installation is not authorized for this operation.",
            cancellationToken: this.Context.RequestAborted).ConfigureAwait(false);
    }
}
