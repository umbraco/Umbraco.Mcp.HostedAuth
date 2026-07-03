using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Umbraco.Cloud.Mcp.HostedAuth;

/// <summary>
/// Resolves the Cloud environment alias(es) (siteIds) used in the tenant-prefixed
/// callback path, read from <c>umbraco-cloud.json</c> in the content root.
/// </summary>
/// <remarks>
/// The hosted Worker builds its callback as <c>/callback/{siteId}</c>, where the
/// siteId is the environment's own subdomain — so live and dev use *different*
/// aliases (<c>hosted-mcp-worker-test</c> vs <c>dev-hosted-mcp-worker-test</c>).
/// <para>
/// When the current environment can be identified — via <c>DOTNET_ENVIRONMENT</c>,
/// which Umbraco Cloud sets per environment to the workspace name (e.g. "Live",
/// "Dev") — only that environment's alias is registered. Otherwise (local dev, or
/// an unrecognised value) it falls back to registering every environment's alias,
/// so registration is always correct even when the environment can't be pinned
/// down. The committed <c>umbraco-cloud.json</c> is identical across environments,
/// so this env var is the only reliable per-environment signal.
/// </para>
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

    private sealed record Workspace(string? Name, string? Type, string SiteId);

    /// <summary>
    /// Returns the callback alias(es) to register: just the current environment's
    /// when it can be identified, otherwise all known environment aliases. Empty
    /// when none are discoverable.
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

        string? projectAlias = null;
        var workspaces = new List<Workspace>();

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
                    projectAlias = alias.GetString();
                }

                if (project.TryGetProperty("Workspaces", out JsonElement workspacesElement)
                    && workspacesElement.ValueKind == JsonValueKind.Array)
                {
                    foreach (JsonElement workspace in workspacesElement.EnumerateArray())
                    {
                        string? siteId = workspace.TryGetProperty("Url", out JsonElement url)
                            && url.ValueKind == JsonValueKind.String
                                ? ExtractSiteId(url.GetString())
                                : null;

                        if (string.IsNullOrWhiteSpace(siteId))
                        {
                            continue;
                        }

                        workspaces.Add(new Workspace(
                            GetString(workspace, "Name"),
                            GetString(workspace, "Type"),
                            siteId));
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

        // Prefer the single current-environment alias when we can identify it.
        string? currentEnvironment = CurrentEnvironmentName();
        Workspace? current = workspaces.FirstOrDefault(w =>
            Matches(w.Name, currentEnvironment) || Matches(w.Type, currentEnvironment));

        if (current is not null)
        {
            _logger.LogInformation(
                "[HostedMcp] Environment '{Environment}' matched workspace '{Workspace}'; "
                + "registering only its callback alias '{Alias}'.",
                currentEnvironment, current.Name ?? current.Type, current.SiteId);
            return [current.SiteId];
        }

        // Fallback: register every environment's alias (safe when the current
        // environment can't be pinned down, e.g. local dev).
        var all = new List<string>();
        Add(all, projectAlias);
        foreach (Workspace workspace in workspaces)
        {
            Add(all, workspace.SiteId);
        }

        _logger.LogInformation(
            "[HostedMcp] Could not match environment '{Environment}' to a workspace; "
            + "registering all {Count} known environment alias(es).",
            currentEnvironment ?? "(unknown)", all.Count);
        return all;
    }

    // Umbraco Cloud sets DOTNET_ENVIRONMENT per environment to the workspace name
    // (e.g. "Live", "Dev"). Fall back to the host environment name otherwise.
    private string? CurrentEnvironmentName()
    {
        string? value = Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT");
        return string.IsNullOrWhiteSpace(value) ? _environment.EnvironmentName : value;
    }

    private static bool Matches(string? candidate, string? environment)
        => !string.IsNullOrWhiteSpace(candidate)
           && string.Equals(candidate, environment, StringComparison.OrdinalIgnoreCase);

    private static void Add(List<string> aliases, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value)
            && !aliases.Contains(value, StringComparer.OrdinalIgnoreCase))
        {
            aliases.Add(value);
        }
    }

    private static string? GetString(JsonElement element, string property)
        => element.TryGetProperty(property, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    // The siteId is the first host label of the workspace URL, e.g.
    // https://dev-hosted-mcp-worker-test.euwest01.umbraco.io -> dev-hosted-mcp-worker-test
    private static string? ExtractSiteId(string? url)
        => Uri.TryCreate(url, UriKind.Absolute, out Uri? uri)
            ? uri.Host.Split('.', 2)[0]
            : null;
}
