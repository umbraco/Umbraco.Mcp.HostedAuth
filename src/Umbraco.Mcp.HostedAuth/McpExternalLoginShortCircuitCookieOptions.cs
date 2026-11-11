using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Umbraco.Cms.Core;

namespace Umbraco.Mcp.HostedAuth;

// When an unauthenticated browser hits the management-API OAuth authorize
// endpoint, the back-office cookie scheme by default redirects to
// /umbraco/login. That URL is served by the standalone Umbraco Login app,
// which has no rendering path for external auth providers — so the user
// lands on a local username/password form and the flow dead-ends.
//
// This intercepts the redirect and instead bounces the user back to the same
// authorize URL with identity_provider=Umbraco.UmbracoId appended. That second
// hit is handled by BackOfficeController.AuthorizeExternal, which converts the
// external claims into a back-office cookie sign-in and then completes the OAuth
// flow. See docs/plans/management-api-sso.md for the full diagnosis.
//
// Only intercepts browser GETs (Accept: text/html) for the authorize path, and
// only when identity_provider isn't already in the query (so failure modes
// inside AuthorizeExternal fall back to the default redirect without looping).
//
// Only intercepts authorize requests from the hosted MCP clients this package
// registers. Every other client — above all the backoffice's own
// umbraco-back-office client — keeps the default redirect to /umbraco/login,
// so this can never change how a normal backoffice sign-in works.
//
// Cloud-only: relies on the Umbraco.UmbracoId scheme registered by
// Umbraco.Cloud.Cms. Gated at request time (via HostedMcpModeResolver, pulled
// from the request's own service provider) rather than at registration time,
// since resolving HostedMcpMode.Auto needs IHostEnvironment, which isn't
// reliably available while composers are still running. A Cloud site that
// doesn't reference Umbraco.Cloud.Cms has no such scheme, and redirecting to it
// would make the authorize endpoint return 500 — so the scheme's presence is
// checked too, and its absence falls back to the default redirect.
internal sealed class McpExternalLoginShortCircuitCookieOptions
    : IPostConfigureOptions<CookieAuthenticationOptions>
{
    private const string OAuthAuthorizePath =
        "/umbraco/management/api/v1/security/back-office/authorize";

    private const string IdentityProviderParam = "identity_provider";

    private const string ClientIdParam = "client_id";

    // Registered by Umbraco.Cloud.Cms via AddUmbracoId →
    // AddMicrosoftIdentityWebApp(scheme: "Umbraco.UmbracoId").
    private const string ExternalLoginScheme = "Umbraco.UmbracoId";

    private readonly ILogger<McpExternalLoginShortCircuitCookieOptions> _logger;

    // The missing-scheme warning fires on every cold-start MCP authorize, so
    // it's logged once per process rather than per request.
    private int _missingSchemeWarned;

    public McpExternalLoginShortCircuitCookieOptions(
        ILogger<McpExternalLoginShortCircuitCookieOptions> logger)
    {
        _logger = logger;
    }

    public void PostConfigure(string? name, CookieAuthenticationOptions options)
    {
        if (name != Constants.Security.BackOfficeAuthenticationType)
        {
            return;
        }

        Func<RedirectContext<CookieAuthenticationOptions>, Task> previousLogin =
            options.Events.OnRedirectToLogin;

        options.Events.OnRedirectToLogin = async ctx =>
        {
            IServiceProvider services = ctx.HttpContext.RequestServices;

            HostedMcpModeResolver modeResolver =
                services.GetRequiredService<HostedMcpModeResolver>();
            if (modeResolver.Resolve() != HostedMcpMode.Cloud)
            {
                await previousLogin(ctx);
                return;
            }

            string path = ctx.Request.Path.Value ?? string.Empty;
            bool isOAuthAuthorize = path.StartsWith(
                OAuthAuthorizePath,
                StringComparison.OrdinalIgnoreCase);
            bool isHtmlGet = HttpMethods.IsGet(ctx.Request.Method)
                && ctx.Request.Headers.Accept.ToString().Contains(
                    "text/html",
                    StringComparison.OrdinalIgnoreCase);
            bool alreadyHasIdentityProvider =
                ctx.Request.Query.ContainsKey(IdentityProviderParam);

            if (!isOAuthAuthorize || !isHtmlGet || alreadyHasIdentityProvider)
            {
                await previousLogin(ctx);
                return;
            }

            HostedMcpOptions hostedOptions =
                services.GetRequiredService<IOptions<HostedMcpOptions>>().Value;
            if (!IsHostedMcpClient(ctx.Request.Query[ClientIdParam].ToString(), hostedOptions))
            {
                await previousLogin(ctx);
                return;
            }

            IAuthenticationSchemeProvider schemeProvider =
                services.GetRequiredService<IAuthenticationSchemeProvider>();
            if (await schemeProvider.GetSchemeAsync(ExternalLoginScheme) is null)
            {
                if (Interlocked.Exchange(ref _missingSchemeWarned, 1) == 0)
                {
                    _logger.LogWarning(
                        "[HostedMcp] Skipping the cold-start SSO redirect: the '{Scheme}' authentication "
                        + "scheme is not registered (it is added by Umbraco.Cloud.Cms). Hosted MCP "
                        + "sign-ins will use the standard backoffice login instead.",
                        ExternalLoginScheme);
                }

                await previousLogin(ctx);
                return;
            }

            string pathAndQuery = ctx.Request.Path + ctx.Request.QueryString;
            string redirectUrl = QueryHelpers.AddQueryString(
                pathAndQuery,
                IdentityProviderParam,
                ExternalLoginScheme);

            _logger.LogInformation(
                "[HostedMcp] Adding identity_provider to OAuth authorize. Redirect={RedirectUrl}",
                redirectUrl);

            ctx.Response.Redirect(redirectUrl);
        };
    }

    // Matches the client ids this package registers in Cloud mode, including
    // any configured ClientId overrides. Ordinal, like OpenIddict's own lookup.
    private static bool IsHostedMcpClient(string clientId, HostedMcpOptions options)
        => !string.IsNullOrEmpty(clientId)
           && HostedMcpClientResolver.Resolve(options, HostedMcpMode.Cloud)
               .Any(client => string.Equals(client.ClientId, clientId, StringComparison.Ordinal));
}
