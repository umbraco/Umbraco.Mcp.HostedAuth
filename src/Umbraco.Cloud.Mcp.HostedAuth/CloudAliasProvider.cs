using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Umbraco.Cloud.Mcp.HostedAuth;

/// <summary>
/// Resolves the Cloud environment aliases (siteIds) used in the tenant-prefixed
/// callback path, read from <c>umbraco-cloud.json</c> in the content root.
/// </summary>
/// <remarks>
/// The hosted Worker builds its callback as <c>/callback/{siteId}</c>, where the
/// siteId is the environment's own subdomain — so live and dev use *different*
/// aliases (<c>hosted-mcp-worker-test</c> vs <c>dev-hosted-mcp-worker-test</c>).
/// The same <c>umbraco-cloud.json</c> is deployed to every environment and lists
/// them all under <c>Deploy:Project:Workspaces[].Url</c>, so we register a
/// callback for each — meaning any environment accepts the callback for whichever
/// environment the Worker is routing.
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
    /// Returns the distinct Cloud environment aliases (the project alias plus the
    /// subdomain of every workspace URL), or an empty list when none are
    /// discoverable.
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

        // Preserve insertion order (project alias first) while de-duplicating.
        var aliases = new List<string>();
        void Add(string? value)
        {
            if (!string.IsNullOrWhiteSpace(value)
                && !aliases.Contains(value, StringComparer.OrdinalIgnoreCase))
            {
                aliases.Add(value);
            }
        }

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
                    Add(alias.GetString());
                }

                if (project.TryGetProperty("Workspaces", out JsonElement workspaces)
                    && workspaces.ValueKind == JsonValueKind.Array)
                {
                    foreach (JsonElement workspace in workspaces.EnumerateArray())
                    {
                        if (workspace.TryGetProperty("Url", out JsonElement url)
                            && url.ValueKind == JsonValueKind.String)
                        {
                            Add(ExtractSiteId(url.GetString()));
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

    // The siteId is the first host label of the workspace URL, e.g.
    // https://dev-hosted-mcp-worker-test.euwest01.umbraco.io -> dev-hosted-mcp-worker-test
    private static string? ExtractSiteId(string? url)
        => Uri.TryCreate(url, UriKind.Absolute, out Uri? uri)
            ? uri.Host.Split('.', 2)[0]
            : null;
}
