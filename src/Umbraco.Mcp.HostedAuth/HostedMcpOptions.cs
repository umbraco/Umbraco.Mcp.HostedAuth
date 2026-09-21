namespace Umbraco.Mcp.HostedAuth;

/// <summary>
/// Configuration for the hosted MCP worker auth glue. Bound from the
/// <c>HostedMcp</c> configuration section.
/// </summary>
/// <remarks>
/// In <see cref="HostedMcpMode.Cloud"/>, which clients get registered is driven
/// by <b>installed-package detection</b> (see <see cref="ProductCatalog"/>): the
/// CMS baseline is always registered, and Commerce/Engage/Workflow are
/// registered when their packages are present. The <see cref="Products"/> map is
/// for <b>overrides only</b> — forcing a product on/off or customising a
/// specific client. In <see cref="HostedMcpMode.SelfHosted"/>, <see cref="Clients"/>
/// is the explicit list of clients to register instead.
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

    /// <summary>
    /// Which deployment this app is. <c>Auto</c> (the default) resolves to
    /// <see cref="HostedMcpMode.Cloud"/> when <c>umbraco-cloud.json</c> is
    /// present in the content root, otherwise <see cref="HostedMcpMode.SelfHosted"/>.
    /// </summary>
    public HostedMcpMode Mode { get; set; } = HostedMcpMode.Auto;

    /// <summary>
    /// Explicit clients to register in <see cref="HostedMcpMode.SelfHosted"/>
    /// mode. Ignored in <see cref="HostedMcpMode.Cloud"/> mode, where clients
    /// come from <see cref="ProductCatalog"/> instead.
    /// </summary>
    public List<SelfHostedClientOptions> Clients { get; set; } = new();

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

/// <summary>Which deployment an app is, for the purpose of client discovery.</summary>
public enum HostedMcpMode
{
    /// <summary>Resolve to <see cref="Cloud"/> or <see cref="SelfHosted"/> based on whether <c>umbraco-cloud.json</c> is present.</summary>
    Auto,

    /// <summary>Umbraco Cloud: clients come from <see cref="ProductCatalog"/>, with alias narrowing and the SSO short-circuit.</summary>
    Cloud,

    /// <summary>Any other deployment: clients come from <see cref="HostedMcpOptions.Clients"/>, with no alias segment and no SSO short-circuit.</summary>
    SelfHosted,
}

/// <summary>An explicit hosted MCP client to register in self-hosted mode.</summary>
public sealed class SelfHostedClientOptions
{
    /// <summary>The OpenIddict client id, as configured on the deployed MCP worker.</summary>
    public string ClientId { get; set; } = string.Empty;

    /// <summary>OpenIddict display name. Defaults to <see cref="ClientId"/> when unset.</summary>
    public string? DisplayName { get; set; }

    /// <summary>The worker's origin(s), e.g. <c>https://mcp.example.com</c>.</summary>
    public string[] Origins { get; set; } = [];
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
