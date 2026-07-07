namespace Umbraco.Mcp.Cloud.HostedAuth;

/// <summary>
/// Configuration for the hosted MCP worker auth glue. Bound from the
/// <c>HostedMcp</c> configuration section.
/// </summary>
/// <remarks>
/// Which clients get registered is driven by <b>installed-package detection</b>
/// (see <see cref="ProductCatalog"/>): the CMS baseline is always registered,
/// and Commerce/Engage/Workflow are registered when their packages are present.
/// The <see cref="Products"/> map is for <b>overrides only</b> — forcing a
/// product on/off or customising a specific client.
/// </remarks>
public sealed class HostedMcpOptions
{
    /// <summary>The configuration section this binds to.</summary>
    public const string SectionName = "HostedMcp";

    /// <summary>
    /// Master switch. When <c>false</c> the composer is a no-op: no clients are
    /// registered, the SSO short-circuit is not installed and the built-in
    /// concurrent-login revoke handler is left untouched.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Per-client access-token lifetime override for the server-wide default.</summary>
    public TimeSpan AccessTokenLifetime { get; set; } = TimeSpan.FromHours(1);

    /// <summary>Per-client refresh-token lifetime override for the server-wide default.</summary>
    public TimeSpan RefreshTokenLifetime { get; set; } = TimeSpan.FromHours(8);

    /// <summary>Whether to register the local-dev wrangler callback.</summary>
    public bool IncludeLocalhostCallback { get; set; } = true;

    /// <summary>Origin used for the local-dev wrangler callback.</summary>
    public string LocalhostCallback { get; set; } = "http://127.0.0.1:8787";

    /// <summary>
    /// Optional per-product overrides, keyed by product key (<c>cms</c>,
    /// <c>commerce</c>, …). Absent products fall back to auto-detection and the
    /// naming convention.
    /// </summary>
    public Dictionary<string, ProductOverrideOptions> Products { get; set; }
        = new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>Overrides for a single product.</summary>
public sealed class ProductOverrideOptions
{
    /// <summary>
    /// Force the product on (<c>true</c>) or off (<c>false</c>), bypassing
    /// installed-package detection. <c>null</c> keeps auto-detection.
    /// </summary>
    public bool? Enabled { get; set; }

    /// <summary>Per-variant client overrides, keyed by variant (<c>editor</c>, <c>developer</c>).</summary>
    public Dictionary<string, ClientOverrideOptions> Clients { get; set; }
        = new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>Overrides for a single (product, variant) client.</summary>
public sealed class ClientOverrideOptions
{
    /// <summary>
    /// Client id override. Defaults to <c>umbraco-{product}-{variant}-mcp-hosted</c>.
    /// Needed where the deployed worker's id does not match the convention
    /// (e.g. CMS <c>developer</c> uses <c>umbraco-cms-dev-mcp-hosted</c>).
    /// </summary>
    public string? ClientId { get; set; }

    /// <summary>OpenIddict display name override.</summary>
    public string? DisplayName { get; set; }

    /// <summary>
    /// Explicit worker origins. When set, replaces the convention-derived list
    /// (<c>https://{product}.{variant}.{major}.[dev.]mcp.umbraco.ai</c>).
    /// </summary>
    public string[]? Origins { get; set; }
}
