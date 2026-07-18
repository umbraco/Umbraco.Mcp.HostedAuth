using Microsoft.Extensions.DependencyModel;

namespace Umbraco.Mcp.Cloud.HostedAuth;

/// <summary>
/// A product that can expose hosted MCP workers.
/// </summary>
/// <param name="Key">
/// Stable product key, used as the client-id and host-name segment
/// (e.g. <c>cms</c> in <c>umbraco-cms-editor-mcp-hosted</c> /
/// <c>cms.editor.18.mcp.umbraco.ai</c>).
/// </param>
/// <param name="LibraryPrefix">
/// NuGet/runtime-library name prefix used to detect whether the product is
/// installed (e.g. <c>Umbraco.Commerce</c>). <c>null</c> means the product is
/// always considered installed (the CMS baseline).
/// </param>
/// <param name="Variants">The MCP worker variants this product exposes.</param>
public sealed record ProductDefinition(
    string Key,
    string? LibraryPrefix,
    IReadOnlyList<string> Variants);

/// <summary>
/// The built-in catalog of Umbraco products that expose hosted MCP workers.
/// Detection is by installed NuGet package; registration for a detected product
/// can still be forced on/off or its clients overridden via configuration.
/// </summary>
public static class ProductCatalog
{
    // Start simple: every product exposes the same editor + developer variants.
    private static readonly string[] DefaultVariants = ["editor", "developer"];

    public static readonly IReadOnlyList<ProductDefinition> Products =
    [
        new ProductDefinition("cms", LibraryPrefix: null, DefaultVariants),        // always on
        new ProductDefinition("commerce", "Umbraco.Commerce", DefaultVariants),
        new ProductDefinition("engage", "Umbraco.Engage", DefaultVariants),
        new ProductDefinition("workflow", "Umbraco.Workflow", DefaultVariants),
    ];
}

/// <summary>
/// Detects whether a product's NuGet package is present in the running app by
/// inspecting the dependency context (the app's <c>.deps.json</c>), which lists
/// every referenced package regardless of assembly load state.
/// </summary>
public static class InstalledProductDetector
{
    public static bool IsInstalled(ProductDefinition product)
    {
        // No prefix => baseline product (CMS), always present.
        if (product.LibraryPrefix is null)
        {
            return true;
        }

        DependencyContext? context = DependencyContext.Default;
        if (context is null)
        {
            return false;
        }

        return context.RuntimeLibraries.Any(library =>
            library.Name.StartsWith(product.LibraryPrefix, StringComparison.OrdinalIgnoreCase));
    }
}
