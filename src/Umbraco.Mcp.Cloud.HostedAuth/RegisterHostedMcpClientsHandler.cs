using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenIddict.Abstractions;
using Umbraco.Cms.Core.Events;
using Umbraco.Cms.Core.Notifications;

namespace Umbraco.Mcp.Cloud.HostedAuth;

/// <summary>
/// Registers each configured hosted MCP worker as an OpenIddict
/// authorization_code public client on startup, using every known environment
/// alias as a safe baseline. The runtime <see cref="HostedMcpAliasReconciler"/>
/// then narrows the callbacks to the environment actually being served.
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
        // Baseline: register every known environment alias so authentication
        // works on any environment immediately, before the runtime reconciler
        // narrows to the actual one. A missing alias set would leave only the
        // legacy non-aliased callback, failing later with a cryptic
        // invalid_redirect_uri — so fail closed: skip registration (mutating
        // nothing) rather than register unusable clients.
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
            OpenIddictApplicationDescriptor descriptor =
                HostedMcpDescriptorFactory.Build(client, aliases, _options);

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

    // A project alias becomes a single URL path segment, so it must be non-empty
    // and contain no slashes or whitespace.
    private static bool IsValidAlias(string? alias)
        => !string.IsNullOrWhiteSpace(alias)
           && !alias.Contains('/')
           && !alias.Any(char.IsWhiteSpace);
}
