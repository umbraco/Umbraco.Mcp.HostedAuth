using System.Globalization;
using Microsoft.Extensions.DependencyModel;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenIddict.Abstractions;
using Umbraco.Cms.Core.Events;
using Umbraco.Cms.Core.Notifications;

namespace Umbraco.Cloud.Mcp.HostedAuth;

/// <summary>
/// Registers each configured hosted MCP worker as an OpenIddict
/// authorization_code public client so the worker can authenticate users via
/// the Umbraco backoffice.
/// </summary>
/// <remarks>
/// Registration is <b>idempotent</b>: existing clients are updated in place
/// (preserving the OpenIddict application id, and therefore any live refresh
/// tokens) rather than deleted and recreated, so warm restarts don't sever
/// active MCP sessions.
/// </remarks>
public sealed class RegisterHostedMcpClientsHandler
    : INotificationAsyncHandler<UmbracoApplicationStartingNotification>
{
    private readonly IOpenIddictApplicationManager _applicationManager;
    private readonly CloudAliasProvider _aliasProvider;
    private readonly HostedMcpOptions _options;
    private readonly ILogger<RegisterHostedMcpClientsHandler> _logger;

    public RegisterHostedMcpClientsHandler(
        IOpenIddictApplicationManager applicationManager,
        CloudAliasProvider aliasProvider,
        IOptions<HostedMcpOptions> options,
        ILogger<RegisterHostedMcpClientsHandler> logger)
    {
        _applicationManager = applicationManager;
        _aliasProvider = aliasProvider;
        _options = options.Value;
        _logger = logger;
    }

    public async Task HandleAsync(
        UmbracoApplicationStartingNotification notification,
        CancellationToken cancellationToken)
    {
        // Product auto-detection needs the dependency context. When it is
        // unavailable, detection is indeterminate rather than "not installed":
        // warn so the operator knows only the CMS baseline and explicitly
        // enabled products (HostedMcp:Products:{key}:Enabled=true) will register.
        if (DependencyContext.Default is null)
        {
            _logger.LogWarning(
                "[HostedMcp] Dependency context unavailable; product auto-detection is disabled. "
                + "Only the CMS baseline and explicitly enabled products will be registered.");
        }

        // The tenant-prefixed callback the hosted worker actually sends is
        // /callback/{siteId}, and the siteId differs per environment (live vs
        // dev use different subdomains). We register a callback for every
        // environment alias so any environment accepts the right one. A missing
        // alias set would leave only the legacy non-aliased callback, failing
        // later with a cryptic invalid_redirect_uri — so fail closed: skip
        // registration entirely (mutating nothing) rather than register unusable
        // clients.
        List<string> aliases = _aliasProvider.ResolveAliases().Where(IsValidAlias).ToList();
        if (aliases.Count == 0)
        {
            _logger.LogError(
                "[HostedMcp] No valid Cloud environment alias could be resolved; skipping MCP client "
                + "registration to avoid creating clients with unusable callback URIs. Ensure "
                + "umbraco-cloud.json (Deploy:Project:Alias / Workspaces) is present and valid.");
            return;
        }

        foreach (ResolvedMcpClient client in HostedMcpClientResolver.Resolve(_options))
        {
            OpenIddictApplicationDescriptor descriptor = BuildDescriptor(client, aliases);

            object? existing = await _applicationManager.FindByClientIdAsync(client.ClientId, cancellationToken);
            if (existing is not null)
            {
                // Update in place — keeps the application id (and its tokens).
                await _applicationManager.UpdateAsync(existing, descriptor, cancellationToken);
                _logger.LogInformation(
                    "[HostedMcp] Updated MCP OAuth client {ClientId} ({RedirectCount} redirect URI(s)).",
                    client.ClientId, descriptor.RedirectUris.Count);
            }
            else
            {
                await _applicationManager.CreateAsync(descriptor, cancellationToken);
                _logger.LogInformation(
                    "[HostedMcp] Created MCP OAuth client {ClientId} ({RedirectCount} redirect URI(s)).",
                    client.ClientId, descriptor.RedirectUris.Count);
            }
        }
    }

    private OpenIddictApplicationDescriptor BuildDescriptor(ResolvedMcpClient client, IReadOnlyList<string> aliases)
    {
        var descriptor = new OpenIddictApplicationDescriptor
        {
            ClientId = client.ClientId,
            ClientType = OpenIddictConstants.ClientTypes.Public,
            DisplayName = client.DisplayName,
            Permissions =
            {
                OpenIddictConstants.Permissions.Endpoints.Authorization,
                OpenIddictConstants.Permissions.Endpoints.Token,
                OpenIddictConstants.Permissions.Endpoints.Revocation,
                OpenIddictConstants.Permissions.Endpoints.EndSession,
                OpenIddictConstants.Permissions.GrantTypes.AuthorizationCode,
                OpenIddictConstants.Permissions.GrantTypes.RefreshToken,
                OpenIddictConstants.Permissions.ResponseTypes.Code,
            },
            // Per-client token lifetimes override the server-wide defaults
            // (derived from Umbraco:CMS:Global:TimeOut). Hosted MCP sessions
            // sit idle between tool calls, so we extend them here.
            Settings =
            {
                [OpenIddictConstants.Settings.TokenLifetimes.AccessToken]
                    = _options.AccessTokenLifetime.ToString("c", CultureInfo.InvariantCulture),
                [OpenIddictConstants.Settings.TokenLifetimes.RefreshToken]
                    = _options.RefreshTokenLifetime.ToString("c", CultureInfo.InvariantCulture),
            }
        };

        foreach (string origin in client.Origins)
        {
            // Single-tenant fallback (legacy callback path).
            descriptor.RedirectUris.Add(new Uri($"{origin}/callback"));
            descriptor.PostLogoutRedirectUris.Add(new Uri($"{origin}/logout-callback"));

            // Multi-tenant tenant-prefixed callback used by the Cloud preset's
            // site router — one per environment alias, since the siteId in the
            // path the worker sends is the environment's own subdomain.
            foreach (string alias in aliases)
            {
                descriptor.RedirectUris.Add(new Uri($"{origin}/callback/{alias}"));
                descriptor.PostLogoutRedirectUris.Add(new Uri($"{origin}/logout-callback/{alias}"));
            }
        }

        if (_options.IncludeLocalhostCallback)
        {
            foreach (string alias in aliases)
            {
                descriptor.RedirectUris.Add(new Uri($"{_options.LocalhostCallback}/callback/{alias}"));
            }
        }

        return descriptor;
    }

    // A project alias becomes a single URL path segment, so it must be non-empty
    // and contain no slashes or whitespace.
    private static bool IsValidAlias(string? alias)
        => !string.IsNullOrWhiteSpace(alias)
           && !alias.Contains('/')
           && !alias.Any(char.IsWhiteSpace);
}
