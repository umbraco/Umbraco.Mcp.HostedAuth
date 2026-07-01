using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Umbraco.Cloud.Mcp.HostedAuth;

/// <summary>
/// Resolves the Cloud project alias used in the tenant-prefixed callback path,
/// read from <c>umbraco-cloud.json</c> (<c>Deploy:Project:Alias</c>) in the
/// content root. This is always available on Umbraco Cloud, so it is derived
/// rather than configured.
/// </summary>
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
    /// Returns the Cloud project alias, or <c>null</c> when it is not
    /// discoverable (in which case tenant-prefixed callbacks are skipped).
    /// </summary>
    public string? Resolve()
    {
        string path = Path.Combine(_environment.ContentRootPath, "umbraco-cloud.json");
        if (!File.Exists(path))
        {
            _logger.LogWarning(
                "[HostedMcp] {Path} not found; tenant-prefixed callback URIs will be skipped.", path);
            return null;
        }

        try
        {
            using FileStream stream = File.OpenRead(path);
            using JsonDocument doc = JsonDocument.Parse(stream);
            if (doc.RootElement.TryGetProperty("Deploy", out JsonElement deploy)
                && deploy.TryGetProperty("Project", out JsonElement project)
                && project.TryGetProperty("Alias", out JsonElement alias)
                && alias.ValueKind == JsonValueKind.String)
            {
                return alias.GetString();
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "[HostedMcp] Failed to read Deploy:Project:Alias from {Path}; " +
                "tenant-prefixed callback URIs will be skipped.", path);
        }

        return null;
    }
}
