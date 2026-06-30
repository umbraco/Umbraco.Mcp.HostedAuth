using Microsoft.Extensions.Options;
using OpenIddict.Abstractions;

namespace Umbraco.Cloud.Mcp.HostedAuth;

/// <summary>
/// Caches the OpenIddict application ids of the configured MCP clients so the
/// concurrent-login carve-out can spare their tokens. Resolved lazily on first
/// use (after startup, when the clients have been registered) and cached for
/// the application lifetime.
/// </summary>
public sealed class ProtectedMcpApplicationStore
{
    private readonly HostedMcpOptions _options;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private volatile IReadOnlySet<string>? _cache;

    public ProtectedMcpApplicationStore(IOptions<HostedMcpOptions> options)
        => _options = options.Value;

    /// <summary>
    /// Returns the set of OpenIddict application ids whose tokens must survive a
    /// backoffice login.
    /// </summary>
    public async ValueTask<IReadOnlySet<string>> GetProtectedApplicationIdsAsync(
        IOpenIddictApplicationManager applicationManager,
        CancellationToken cancellationToken)
    {
        if (_cache is not null)
        {
            return _cache;
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_cache is not null)
            {
                return _cache;
            }

            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (ResolvedMcpClient client in HostedMcpClientResolver.Resolve(_options))
            {
                var application = await applicationManager.FindByClientIdAsync(client.ClientId, cancellationToken);
                if (application is null)
                {
                    continue;
                }

                string? id = await applicationManager.GetIdAsync(application, cancellationToken);
                if (id is not null)
                {
                    ids.Add(id);
                }
            }

            _cache = ids;
            return _cache;
        }
        finally
        {
            _gate.Release();
        }
    }
}
