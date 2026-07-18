using Umbraco.Cms.Core.Composing;

namespace Umbraco.Mcp.Cloud.HostedAuth;

/// <summary>A hosted MCP client with all convention-derived values resolved.</summary>
public sealed record ResolvedMcpClient(string ClientId, string DisplayName, IReadOnlyList<string> Origins);

/// <summary>
/// Produces the set of MCP clients to register by walking the
/// <see cref="ProductCatalog"/>: a product is included when installed (or forced
/// on via config), and each of its variants becomes a client whose id and
/// origins follow the naming convention unless overridden.
/// </summary>
public static class HostedMcpClientResolver
{
    // The zone is fixed for all hosted MCP workers.
    private const string Zone = "mcp.umbraco.ai";

    // Environment labels woven into the worker origin: prod is transparent, dev
    // carries the ".dev" label (e.g. cms.editor.18.mcp.umbraco.ai and
    // cms.editor.18.dev.mcp.umbraco.ai). Both are always registered so either
    // worker can complete the flow.
    private static readonly string[] EnvironmentLabels = ["", "dev."];

    /// <summary>The Umbraco major version, derived from the loaded core assembly.</summary>
    public static int MajorVersion
        => typeof(IComposer).Assembly.GetName().Version?.Major
           ?? throw new InvalidOperationException("Could not determine the Umbraco major version.");

    public static IReadOnlyList<ResolvedMcpClient> Resolve(HostedMcpOptions options)
    {
        int major = MajorVersion;
        var clients = new List<ResolvedMcpClient>();

        foreach (ProductDefinition product in ProductCatalog.Products)
        {
            options.Products.TryGetValue(product.Key, out ProductOverrideOptions? productOverride);

            bool enabled = productOverride?.Enabled ?? InstalledProductDetector.IsInstalled(product);
            if (!enabled)
            {
                continue;
            }

            foreach (string variant in product.Variants)
            {
                ClientOverrideOptions? clientOverride = null;
                productOverride?.Clients.TryGetValue(variant, out clientOverride);

                clients.Add(ResolveClient(product, variant, major, clientOverride));
            }
        }

        return clients;
    }

    private static ResolvedMcpClient ResolveClient(
        ProductDefinition product, string variant, int major, ClientOverrideOptions? over)
    {
        string clientId = over?.ClientId
            ?? $"umbraco-{product.Key}-{variant}-mcp-hosted";

        string displayName = over?.DisplayName
            ?? $"Umbraco {Capitalize(product.Key)} {Capitalize(variant)} MCP Worker";

        IReadOnlyList<string> origins = over?.Origins
            ?? EnvironmentLabels
                .Select(label => $"https://{product.Key}.{variant}.{major}.{label}{Zone}")
                .ToArray();

        return new ResolvedMcpClient(clientId, displayName, origins);
    }

    private static string Capitalize(string value)
        => value.Length == 0 ? value : char.ToUpperInvariant(value[0]) + value[1..];
}
