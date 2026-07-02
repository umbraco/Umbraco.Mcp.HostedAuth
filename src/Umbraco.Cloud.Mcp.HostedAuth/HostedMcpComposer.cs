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
    // Matched by simple name (resilient to namespace changes); full name is kept
    // for diagnostics.
    private const string BuiltInRevokeHandlerTypeName =
        "RevokeUserAuthenticationTokensNotificationHandler";
    private const string BuiltInRevokeHandlerFullName =
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
        // Match by simple type name (not full name) so a namespace change in the
        // CMS doesn't silently defeat removal.
        List<ServiceDescriptor> toRemove = builder.Services
            .Where(d => d.ServiceType == typeof(INotificationAsyncHandler<UserLoginSuccessNotification>)
                && d.ImplementationType?.Name == BuiltInRevokeHandlerTypeName)
            .ToList();

        // Fail closed: if we can't remove the built-in handler, it would run
        // alongside ours and revoke the very MCP tokens we spare — silently
        // breaking sessions AND bypassing the safeguard. Better to stop startup
        // with a clear message than ship a false sense of protection.
        if (toRemove.Count == 0)
        {
            throw new InvalidOperationException(
                $"[HostedMcp] Could not find the built-in '{BuiltInRevokeHandlerFullName}' registration "
                + "for UserLoginSuccess to replace. This Umbraco version is not compatible with "
                + "Umbraco.Cloud.Mcp.HostedAuth: without removing it, backoffice logins would revoke live "
                + "MCP sessions. Upgrade the package or pin a supported CMS version.");
        }

        foreach (ServiceDescriptor descriptor in toRemove)
        {
            builder.Services.Remove(descriptor);
        }

        logger.LogInformation(
            "[HostedMcp] Replaced built-in UserLoginSuccess token-revocation with MCP-aware handler.");
    }
}
