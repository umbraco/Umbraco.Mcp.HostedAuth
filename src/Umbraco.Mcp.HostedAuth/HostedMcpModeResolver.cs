using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Umbraco.Mcp.HostedAuth;

/// <summary>
/// Resolves <see cref="HostedMcpOptions.Mode"/>, deciding <see cref="HostedMcpMode.Auto"/>
/// by whether <c>umbraco-cloud.json</c> is present. Resolved once and cached —
/// the content root doesn't change at runtime.
/// </summary>
public sealed class HostedMcpModeResolver
{
    private readonly IHostEnvironment _environment;
    private readonly HostedMcpOptions _options;
    private HostedMcpMode? _resolved;

    public HostedMcpModeResolver(IHostEnvironment environment, IOptions<HostedMcpOptions> options)
    {
        _environment = environment;
        _options = options.Value;
    }

    public HostedMcpMode Resolve() => _resolved ??= _options.Mode switch
    {
        HostedMcpMode.Cloud => HostedMcpMode.Cloud,
        HostedMcpMode.SelfHosted => HostedMcpMode.SelfHosted,
        _ => CloudAliasProvider.HasCloudConfig(_environment) ? HostedMcpMode.Cloud : HostedMcpMode.SelfHosted,
    };
}
