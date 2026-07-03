using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Umbraco.Cloud.Mcp.HostedAuth;

/// <summary>
/// Resolves the known Cloud environment aliases (siteIds) from
/// <c>umbraco-cloud.json</c> in the content root: the project alias plus the
/// subdomain of every <c>Deploy:Project:Workspaces[].Url</c>.
/// </summary>
/// <remarks>
/// The hosted Worker builds its callback as <c>/callback/{siteId}</c>, where the
/// siteId is the environment's own subdomain (live and dev differ). This set is
/// used two ways: as the safe startup baseline (register all, so every
/// environment works immediately), and as the allow-list the runtime reconciler
/// validates a request host against before narrowing (see
/// <see cref="HostedMcpAliasReconciler"/>).
/// </remarks>
public sealed class CloudAliasProvider
{
    private readonly IHostEnvironment _environment;
    private readonly ILogger<CloudAliasProvider> _logger;

    public CloudAliasProvider(IHostEnvironment environment, ILogger<CloudAliasProvider> logger)
    {
        _environment = environment;
        _logger = logger;
    }

    /// <summary>
    /// Returns the distinct known environment aliases, or an empty list when none
    /// are discoverable.
    /// </summary>
    public IReadOnlyList<string> ResolveAliases()
    {
        string path = Path.Combine(_environment.ContentRootPath, "umbraco-cloud.json");
        if (!File.Exists(path))
        {
            _logger.LogWarning(
                "[HostedMcp] {Path} not found; tenant-prefixed callback URIs cannot be registered.", path);
            return [];
        }

        var aliases = new List<string>();

        try
        {
            using FileStream stream = File.OpenRead(path);
            using JsonDocument doc = JsonDocument.Parse(stream);

            if (doc.RootElement.TryGetProperty("Deploy", out JsonElement deploy)
                && deploy.TryGetProperty("Project", out JsonElement project))
            {
                if (project.TryGetProperty("Alias", out JsonElement alias)
                    && alias.ValueKind == JsonValueKind.String)
                {
                    Add(aliases, alias.GetString());
                }

                if (project.TryGetProperty("Workspaces", out JsonElement workspaces)
                    && workspaces.ValueKind == JsonValueKind.Array)
                {
                    foreach (JsonElement workspace in workspaces.EnumerateArray())
                    {
                        if (workspace.TryGetProperty("Url", out JsonElement url)
                            && url.ValueKind == JsonValueKind.String)
                        {
                            Add(aliases, ExtractSiteId(url.GetString()));
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "[HostedMcp] Failed to read environment aliases from {Path}; " +
                "tenant-prefixed callback URIs cannot be registered.", path);
            return [];
        }

        return aliases;
    }

    private static void Add(List<string> aliases, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value)
            && !aliases.Contains(value, StringComparer.OrdinalIgnoreCase))
        {
            aliases.Add(value);
        }
    }

    // The siteId is the first host label of the workspace URL, e.g.
    // https://dev-hosted-mcp-worker-test.euwest01.umbraco.io -> dev-hosted-mcp-worker-test
    private static string? ExtractSiteId(string? url)
        => Uri.TryCreate(url, UriKind.Absolute, out Uri? uri)
            ? uri.Host.Split('.', 2)[0]
            : null;
}
