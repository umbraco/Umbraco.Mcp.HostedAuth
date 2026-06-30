using System.Globalization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenIddict.Abstractions;
using Umbraco.Cms.Core.Events;
using Umbraco.Cms.Core.Notifications;

namespace Umbraco.Cloud.Mcp.HostedAuth;

/// <summary>
/// Registers each configured hosted MCP worker as an OpenIddict
/// authorization_code public client so the worker can authenticate users via
/// the Umbraco backoffice. Re-registers on every startup so redirect-URI
/// changes take effect without manual cleanup.
/// </summary>
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
        string? alias = _aliasProvider.Resolve(_options);

        foreach (ResolvedMcpClient client in HostedMcpClientResolver.Resolve(_options))
        {
            var existing = await _applicationManager.FindByClientIdAsync(client.ClientId, cancellationToken);
            if (existing is not null)
            {
                await _applicationManager.DeleteAsync(existing, cancellationToken);
            }

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
                // site router. This is the form the hosted worker actually sends.
                if (alias is not null)
                {
                    descriptor.RedirectUris.Add(new Uri($"{origin}/callback/{alias}"));
                    descriptor.PostLogoutRedirectUris.Add(new Uri($"{origin}/logout-callback/{alias}"));
                }
            }

            if (_options.IncludeLocalhostCallback && alias is not null)
            {
                descriptor.RedirectUris.Add(new Uri($"{_options.LocalhostCallback}/callback/{alias}"));
            }

            await _applicationManager.CreateAsync(descriptor, cancellationToken);

            _logger.LogInformation(
                "[HostedMcp] Registered MCP OAuth client {ClientId} with {RedirectCount} redirect URI(s).",
                client.ClientId, descriptor.RedirectUris.Count);
        }
    }
}
