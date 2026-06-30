namespace Umbraco.Cloud.Mcp.HostedAuth;

/// <summary>
/// Configuration for the hosted MCP worker auth glue. Bound from the
/// <c>HostedMcp</c> configuration section.
/// </summary>
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

    /// <summary>Host-name product prefix, e.g. <c>cms</c> in <c>cms.editor.17.mcp.umbraco.ai</c>.</summary>
    public string Product { get; set; } = "cms";

    /// <summary>Base zone the worker origins live on.</summary>
    public string Zone { get; set; } = "mcp.umbraco.ai";

    /// <summary>
    /// Umbraco major version baked into the worker origins. When null it is
    /// derived from the loaded Umbraco assembly version.
    /// </summary>
    public int? MajorVersion { get; set; }

    /// <summary>
    /// Cloud project alias used in the tenant-prefixed callback path
    /// (<c>/callback/{alias}</c>). When null it is read from
    /// <c>umbraco-cloud.json</c> (<c>Deploy:Project:Alias</c>).
    /// </summary>
    public string? CloudAlias { get; set; }

    /// <summary>Per-client access-token lifetime override for the server-wide default.</summary>
    public TimeSpan AccessTokenLifetime { get; set; } = TimeSpan.FromHours(1);

    /// <summary>Per-client refresh-token lifetime override for the server-wide default.</summary>
    public TimeSpan RefreshTokenLifetime { get; set; } = TimeSpan.FromHours(8);

    /// <summary>Whether to register the local-dev wrangler callback.</summary>
    public bool IncludeLocalhostCallback { get; set; } = true;

    /// <summary>Origin used for the local-dev wrangler callback.</summary>
    public string LocalhostCallback { get; set; } = "http://127.0.0.1:8787";

    /// <summary>The MCP worker clients to register.</summary>
    public List<HostedMcpClientOptions> Clients { get; set; } = [];
}

/// <summary>
/// A single hosted MCP worker client. Only <see cref="Type"/> is required; all
/// other values are derived by convention unless overridden.
/// </summary>
public sealed class HostedMcpClientOptions
{
    /// <summary>
    /// Worker type label, e.g. <c>editor</c> or <c>developer</c>. Used as the
    /// host-name segment and (by default) in the client id.
    /// </summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>
    /// Client id override. Defaults to <c>umbraco-{Product}-{Type}-mcp-hosted</c>.
    /// Override is needed where the deployed worker's client id does not match
    /// the type label (e.g. type <c>developer</c> with id <c>umbraco-cms-dev-mcp-hosted</c>).
    /// </summary>
    public string? ClientId { get; set; }

    /// <summary>OpenIddict display name override.</summary>
    public string? DisplayName { get; set; }

    /// <summary>
    /// Explicit worker origins. When set, replaces the convention-derived list
    /// (<c>https://{Product}.{Type}.{major}.[dev.]{Zone}</c>).
    /// </summary>
    public string[]? Origins { get; set; }
}
