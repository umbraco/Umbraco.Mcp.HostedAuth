using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenIddict.Abstractions;

namespace Umbraco.Mcp.HostedAuth;

/// <summary>
/// Narrows the registered callback URIs to the environment the app is actually
/// served on, using the request host as ground truth — immune to how
/// <c>DOTNET_ENVIRONMENT</c> happens to be set.
/// </summary>
/// <remarks>
/// Startup registers the safe baseline (every known alias). On the first request
/// whose host is a recognised <c>{siteId}.{region}.umbraco.io</c> environment,
/// this rewrites each MCP client to that single siteId's callbacks and never
/// touches them again. It only acts on siteIds that appear in
/// <c>umbraco-cloud.json</c>, so an unexpected host can't wipe the allow-list.
/// </remarks>
public sealed class HostedMcpAliasReconciler
{
    private readonly CloudAliasProvider _aliasProvider;
    private readonly HostedMcpModeResolver _modeResolver;
    private readonly HostedMcpOptions _options;
    private readonly ILogger<HostedMcpAliasReconciler> _logger;

    private readonly SemaphoreSlim _gate = new(1, 1);
    private volatile string? _narrowedTo;
    private IReadOnlyList<string>? _knownAliases;

    public HostedMcpAliasReconciler(
        CloudAliasProvider aliasProvider,
        HostedMcpModeResolver modeResolver,
        IOptions<HostedMcpOptions> options,
        ILogger<HostedMcpAliasReconciler> logger)
    {
        _aliasProvider = aliasProvider;
        _modeResolver = modeResolver;
        _options = options.Value;
        _logger = logger;
    }

    /// <summary>
    /// Extracts the siteId from an Umbraco Cloud host
    /// (<c>{siteId}.{region}.umbraco.io</c>), or <c>null</c> for any other host.
    /// </summary>
    public static string? ExtractSiteId(string? host)
    {
        if (string.IsNullOrEmpty(host))
        {
            return null;
        }

        string[] labels = host.Split('.');
        // {siteId}.{region}.umbraco.io — at least 4 labels ending in umbraco.io.
        if (labels.Length >= 4
            && labels[^1].Equals("io", StringComparison.OrdinalIgnoreCase)
            && labels[^2].Equals("umbraco", StringComparison.OrdinalIgnoreCase)
            && labels[0].Length > 0)
        {
            return labels[0];
        }

        return null;
    }

    /// <summary>
    /// Narrows all MCP clients to <paramref name="siteId"/> the first time a
    /// recognised environment host is seen. No-op afterwards, for unknown
    /// siteIds, or when narrowing is disabled.
    /// </summary>
    public async ValueTask EnsureNarrowedAsync(
        string siteId, IOpenIddictApplicationManager applicationManager, CancellationToken cancellationToken)
    {
        if (_narrowedTo is not null)
        {
            return;
        }

        // Only ever narrow to an environment we actually know about, so a stray
        // *.umbraco.io host can never wipe the allow-list.
        IReadOnlyList<string> known = _knownAliases ??= _aliasProvider.ResolveAliases();
        if (!known.Contains(siteId, StringComparer.OrdinalIgnoreCase))
        {
            return;
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_narrowedTo is not null)
            {
                return;
            }

            string[] aliases = [siteId];
            foreach (ResolvedMcpClient client in HostedMcpClientResolver.Resolve(_options, _modeResolver.Resolve()))
            {
                OpenIddictApplicationDescriptor descriptor =
                    HostedMcpDescriptorFactory.Build(client, aliases, _options);

                object? existing = await applicationManager.FindByClientIdAsync(client.ClientId, cancellationToken);
                if (existing is not null)
                {
                    await applicationManager.UpdateAsync(existing, descriptor, cancellationToken);
                }
                else
                {
                    await applicationManager.CreateAsync(descriptor, cancellationToken);
                }
            }

            _narrowedTo = siteId;
            _logger.LogInformation(
                "[HostedMcp] Narrowed MCP callback URIs to the current environment '{SiteId}'.", siteId);
        }
        finally
        {
            _gate.Release();
        }
    }
}
