using Umbraco.Cms.Core.Composing;

namespace Umbraco.Cloud.Mcp.HostedAuth;

/// <summary>A hosted MCP client with all convention-derived values resolved.</summary>
public sealed record ResolvedMcpClient(string ClientId, string DisplayName, IReadOnlyList<string> Origins);

/// <summary>
/// Turns <see cref="HostedMcpClientOptions"/> into <see cref="ResolvedMcpClient"/>s,
/// applying the host-name convention and any per-client overrides.
/// </summary>
public static class HostedMcpClientResolver
{
    // Environment labels woven into the worker origin: prod is transparent, dev
    // carries the ".dev" label (e.g. cms.editor.17.mcp.umbraco.ai and
    // cms.editor.17.dev.mcp.umbraco.ai).
    private static readonly string[] EnvironmentLabels = ["", "dev."];

    /// <summary>
    /// Resolves the Umbraco major version: explicit config wins, otherwise the
    /// loaded Umbraco core assembly version.
    /// </summary>
    public static int ResolveMajorVersion(HostedMcpOptions options)
        => options.MajorVersion
           ?? typeof(IComposer).Assembly.GetName().Version?.Major
           ?? throw new InvalidOperationException(
               "Could not determine the Umbraco major version; set HostedMcp:MajorVersion explicitly.");

    public static IReadOnlyList<ResolvedMcpClient> Resolve(HostedMcpOptions options)
    {
        int major = ResolveMajorVersion(options);
        return options.Clients.Select(client => Resolve(options, client, major)).ToList();
    }

    private static ResolvedMcpClient Resolve(HostedMcpOptions options, HostedMcpClientOptions client, int major)
    {
        if (string.IsNullOrWhiteSpace(client.Type))
        {
            throw new InvalidOperationException("Each HostedMcp:Clients entry must specify a Type.");
        }

        string clientId = client.ClientId
            ?? $"umbraco-{options.Product}-{client.Type}-mcp-hosted";

        string displayName = client.DisplayName
            ?? $"Umbraco {options.Product.ToUpperInvariant()} {Capitalize(client.Type)} MCP Worker";

        IReadOnlyList<string> origins = client.Origins
            ?? EnvironmentLabels
                .Select(label => $"https://{options.Product}.{client.Type}.{major}.{label}{options.Zone}")
                .ToArray();

        return new ResolvedMcpClient(clientId, displayName, origins);
    }

    private static string Capitalize(string value)
        => value.Length == 0 ? value : char.ToUpperInvariant(value[0]) + value[1..];
}
