using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.Internal;
using Microsoft.Extensions.Logging.Abstractions;
using Umbraco.Cms.Core;

namespace Umbraco.Mcp.HostedAuth.Tests;

public class McpExternalLoginShortCircuitCookieOptionsTests
{
    private const string AuthorizePath = "/umbraco/management/api/v1/security/back-office/authorize";
    private const string DefaultLoginUrl = "/umbraco/login";
    private const string HostedEditorClient = "umbraco-cms-editor-mcp-hosted";
    private const string BackOfficeClient = "umbraco-back-office";
    private const string UmbracoIdScheme = "Umbraco.UmbracoId";

    [Fact]
    public async Task Hosted_mcp_client_is_redirected_through_Umbraco_ID_when_the_scheme_is_registered()
    {
        RedirectOutcome outcome = await RedirectToLoginAsync(
            $"?client_id={HostedEditorClient}&response_type=code",
            umbracoIdRegistered: true);

        Assert.False(outcome.FellThrough);
        Assert.Contains("identity_provider=Umbraco.UmbracoId", outcome.Location);
        Assert.StartsWith(AuthorizePath, outcome.Location);
    }

    [Fact]
    public async Task Hosted_mcp_client_falls_through_when_Umbraco_ID_is_not_registered()
    {
        // Cloud site without Umbraco.Cloud.Cms: redirecting to a missing scheme
        // makes the authorize endpoint return 500.
        RedirectOutcome outcome = await RedirectToLoginAsync(
            $"?client_id={HostedEditorClient}&response_type=code",
            umbracoIdRegistered: false);

        Assert.True(outcome.FellThrough);
        Assert.Equal(DefaultLoginUrl, outcome.Location);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Backoffice_client_is_never_redirected(bool umbracoIdRegistered)
    {
        RedirectOutcome outcome = await RedirectToLoginAsync(
            $"?client_id={BackOfficeClient}&response_type=code",
            umbracoIdRegistered);

        Assert.True(outcome.FellThrough);
        Assert.Equal(DefaultLoginUrl, outcome.Location);
    }

    [Fact]
    public async Task Request_without_a_client_id_falls_through()
    {
        RedirectOutcome outcome = await RedirectToLoginAsync("?response_type=code", umbracoIdRegistered: true);

        Assert.True(outcome.FellThrough);
    }

    [Fact]
    public async Task Overridden_client_id_is_treated_as_a_hosted_mcp_client()
    {
        RedirectOutcome outcome = await RedirectToLoginAsync(
            "?client_id=umbraco-cms-dev-mcp-hosted",
            umbracoIdRegistered: true,
            configure: options => options.Products["cms"] = new ProductOverrideOptions
            {
                Clients = { ["developer"] = new ClientOverrideOptions { ClientId = "umbraco-cms-dev-mcp-hosted" } },
            });

        Assert.False(outcome.FellThrough);
        Assert.Contains("identity_provider=Umbraco.UmbracoId", outcome.Location);
    }

    [Fact]
    public async Task Request_that_already_names_an_identity_provider_falls_through()
    {
        RedirectOutcome outcome = await RedirectToLoginAsync(
            $"?client_id={HostedEditorClient}&identity_provider={UmbracoIdScheme}",
            umbracoIdRegistered: true);

        Assert.True(outcome.FellThrough);
    }

    [Fact]
    public async Task Self_hosted_mode_falls_through()
    {
        RedirectOutcome outcome = await RedirectToLoginAsync(
            $"?client_id={HostedEditorClient}",
            umbracoIdRegistered: true,
            configure: options => options.Mode = HostedMcpMode.SelfHosted);

        Assert.True(outcome.FellThrough);
    }

    [Fact]
    public async Task Non_html_request_falls_through()
    {
        RedirectOutcome outcome = await RedirectToLoginAsync(
            $"?client_id={HostedEditorClient}",
            umbracoIdRegistered: true,
            accept: "application/json");

        Assert.True(outcome.FellThrough);
    }

    private sealed record RedirectOutcome(bool FellThrough, string Location);

    private static async Task<RedirectOutcome> RedirectToLoginAsync(
        string queryString,
        bool umbracoIdRegistered,
        Action<HostedMcpOptions>? configure = null,
        string accept = "text/html,application/xhtml+xml")
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.Configure<HostedMcpOptions>(options =>
        {
            options.Mode = HostedMcpMode.Cloud;
            configure?.Invoke(options);
        });
        services.AddSingleton<IHostEnvironment>(new HostingEnvironment { ContentRootPath = AppContext.BaseDirectory });
        services.AddSingleton<HostedMcpModeResolver>();
        services.AddAuthenticationCore(options =>
        {
            if (umbracoIdRegistered)
            {
                options.AddScheme<NoOpAuthenticationHandler>(UmbracoIdScheme, displayName: null);
            }
        });

        await using ServiceProvider provider = services.BuildServiceProvider();

        var httpContext = new DefaultHttpContext { RequestServices = provider };
        httpContext.Request.Method = HttpMethods.Get;
        httpContext.Request.Path = AuthorizePath;
        httpContext.Request.QueryString = new QueryString(queryString);
        httpContext.Request.Headers.Accept = accept;

        bool fellThrough = false;
        var cookieOptions = new CookieAuthenticationOptions();
        cookieOptions.Events.OnRedirectToLogin = ctx =>
        {
            fellThrough = true;
            ctx.Response.Redirect(DefaultLoginUrl);
            return Task.CompletedTask;
        };

        var sut = new McpExternalLoginShortCircuitCookieOptions(
            NullLogger<McpExternalLoginShortCircuitCookieOptions>.Instance);
        sut.PostConfigure(Constants.Security.BackOfficeAuthenticationType, cookieOptions);

        var scheme = new AuthenticationScheme(
            Constants.Security.BackOfficeAuthenticationType,
            displayName: null,
            typeof(CookieAuthenticationHandler));
        var redirectContext = new RedirectContext<CookieAuthenticationOptions>(
            httpContext, scheme, cookieOptions, new AuthenticationProperties(), DefaultLoginUrl);

        await cookieOptions.Events.OnRedirectToLogin(redirectContext);

        return new RedirectOutcome(fellThrough, httpContext.Response.Headers.Location.ToString());
    }

    private sealed class NoOpAuthenticationHandler : IAuthenticationHandler
    {
        public Task InitializeAsync(AuthenticationScheme scheme, HttpContext context) => Task.CompletedTask;

        public Task<AuthenticateResult> AuthenticateAsync() => Task.FromResult(AuthenticateResult.NoResult());

        public Task ChallengeAsync(AuthenticationProperties? properties) => Task.CompletedTask;

        public Task ForbidAsync(AuthenticationProperties? properties) => Task.CompletedTask;
    }
}
