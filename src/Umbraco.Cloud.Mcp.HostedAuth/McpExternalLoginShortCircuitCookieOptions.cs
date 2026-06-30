using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Umbraco.Cms.Core;

namespace Umbraco.Cloud.Mcp.HostedAuth;

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
internal sealed class McpExternalLoginShortCircuitCookieOptions
    : IPostConfigureOptions<CookieAuthenticationOptions>
{
    private const string OAuthAuthorizePath =
        "/umbraco/management/api/v1/security/back-office/authorize";

    private const string IdentityProviderParam = "identity_provider";

    // Registered by Umbraco.Cloud.Cms via AddUmbracoId →
    // AddMicrosoftIdentityWebApp(scheme: "Umbraco.UmbracoId").
    private const string ExternalLoginScheme = "Umbraco.UmbracoId";

    private readonly ILogger<McpExternalLoginShortCircuitCookieOptions> _logger;

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

        options.Events.OnRedirectToLogin = ctx =>
        {
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
                return previousLogin(ctx);
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
            return Task.CompletedTask;
        };
    }
}
