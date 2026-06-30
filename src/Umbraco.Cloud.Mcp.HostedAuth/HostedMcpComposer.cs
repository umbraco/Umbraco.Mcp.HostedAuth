using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Umbraco.Cms.Core.Composing;
using Umbraco.Cms.Core.DependencyInjection;
using Umbraco.Cms.Core.Events;
using Umbraco.Cms.Core.Notifications;

namespace Umbraco.Cloud.Mcp.HostedAuth;

/// <summary>
/// Wires the hosted MCP worker auth glue into Umbraco: registers the configured
/// workers as OpenIddict clients, installs the cold-start SSO short-circuit, and
/// swaps the built-in login-token-revocation for an MCP-aware one. Driven by the
/// <c>HostedMcp</c> configuration section; a no-op when disabled or unconfigured.
/// </summary>
public sealed class HostedMcpComposer : IComposer
{
    // The internal built-in handler we replace on the UserLoginSuccess path.
    private const string BuiltInRevokeHandlerTypeName =
        "Umbraco.Cms.Api.Management.Handlers.RevokeUserAuthenticationTokensNotificationHandler";

    public void Compose(IUmbracoBuilder builder)
    {
        ILogger logger = builder.BuilderLoggerFactory.CreateLogger<HostedMcpComposer>();

        HostedMcpOptions options =
            builder.Config.GetSection(HostedMcpOptions.SectionName).Get<HostedMcpOptions>()
            ?? new HostedMcpOptions();

        if (!options.Enabled)
        {
            logger.LogInformation("[HostedMcp] Disabled via configuration; skipping registration.");
            return;
        }

        if (options.Clients.Count == 0)
        {
            logger.LogWarning("[HostedMcp] Enabled but no HostedMcp:Clients configured.");
        }

        builder.Services.Configure<HostedMcpOptions>(
            builder.Config.GetSection(HostedMcpOptions.SectionName));

        builder.Services.AddSingleton<CloudAliasProvider>();
        builder.Services.AddSingleton<ProtectedMcpApplicationStore>();

        // Register the OAuth clients on startup.
        builder.AddNotificationAsyncHandler<UmbracoApplicationStartingNotification,
            RegisterHostedMcpClientsHandler>();

        // Cold-start SSO short-circuit on the back-office cookie scheme.
        builder.Services.AddSingleton<IPostConfigureOptions<CookieAuthenticationOptions>,
            McpExternalLoginShortCircuitCookieOptions>();

        // Surgical concurrent-login carve-out: drop the built-in
        // UserLoginSuccess revoke registration and add our MCP-aware one.
        // The UserSaved / UserDeleted registrations of the built-in handler are
        // deliberately left intact (disable/delete still revokes MCP tokens).
        RemoveBuiltInLoginRevokeHandler(builder, logger);
        builder.AddNotificationAsyncHandler<UserLoginSuccessNotification,
            McpAwareRevokeOnLoginHandler>();
    }

    private static void RemoveBuiltInLoginRevokeHandler(IUmbracoBuilder builder, ILogger logger)
    {
        List<ServiceDescriptor> toRemove = builder.Services
            .Where(d => d.ServiceType == typeof(INotificationAsyncHandler<UserLoginSuccessNotification>)
                && d.ImplementationType?.FullName == BuiltInRevokeHandlerTypeName)
            .ToList();

        foreach (ServiceDescriptor descriptor in toRemove)
        {
            builder.Services.Remove(descriptor);
        }

        if (toRemove.Count == 0)
        {
            logger.LogWarning(
                "[HostedMcp] Built-in '{Handler}' registration for UserLoginSuccess was not found; " +
                "the MCP-aware revoke handler may run alongside it. This usually means the CMS " +
                "internals changed — verify MCP tokens survive a backoffice login.",
                BuiltInRevokeHandlerTypeName);
        }
        else
        {
            logger.LogInformation(
                "[HostedMcp] Replaced built-in UserLoginSuccess token-revocation with MCP-aware handler.");
        }
    }
}
