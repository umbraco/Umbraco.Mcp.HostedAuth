using System.Globalization;
using OpenIddict.Abstractions;

namespace Umbraco.Mcp.HostedAuth;

/// <summary>
/// Builds the OpenIddict application descriptor for a hosted MCP client. Shared
/// by startup registration and the runtime alias reconciler so both produce an
/// identical shape, differing only in which environment alias(es) they register.
/// </summary>
public static class HostedMcpDescriptorFactory
{
    public static OpenIddictApplicationDescriptor Build(
        ResolvedMcpClient client, IReadOnlyList<string> aliases, HostedMcpOptions options)
    {
        var descriptor = new OpenIddictApplicationDescriptor
        {
            ClientId = client.ClientId,
            ClientType = OpenIddictConstants.ClientTypes.Public,
            DisplayName = client.DisplayName,
            Permissions =
            {
                OpenIddictConstants.Permissions.Endpoints.Authorization,
                OpenIddictConstants.Permissions.Endpoints.Token,
                OpenIddictConstants.Permissions.Endpoints.Revocation,
                OpenIddictConstants.Permissions.Endpoints.EndSession,
                OpenIddictConstants.Permissions.GrantTypes.AuthorizationCode,
                OpenIddictConstants.Permissions.GrantTypes.RefreshToken,
                OpenIddictConstants.Permissions.ResponseTypes.Code,
            },
            // Per-client token lifetimes override the server-wide defaults
            // (derived from Umbraco:CMS:Global:TimeOut). Hosted MCP sessions
            // sit idle between tool calls, so we extend them here.
            Settings =
            {
                [OpenIddictConstants.Settings.TokenLifetimes.AccessToken]
                    = options.AccessTokenLifetime.ToString("c", CultureInfo.InvariantCulture),
                [OpenIddictConstants.Settings.TokenLifetimes.RefreshToken]
                    = options.RefreshTokenLifetime.ToString("c", CultureInfo.InvariantCulture),
            }
        };

        foreach (string origin in client.Origins)
        {
            // Single-tenant fallback (legacy callback path).
            descriptor.RedirectUris.Add(new Uri($"{origin}/callback"));
            descriptor.PostLogoutRedirectUris.Add(new Uri($"{origin}/logout-callback"));

            // Multi-tenant tenant-prefixed callback used by the Cloud preset's
            // site router — one per environment alias.
            foreach (string alias in aliases)
            {
                descriptor.RedirectUris.Add(new Uri($"{origin}/callback/{alias}"));
                descriptor.PostLogoutRedirectUris.Add(new Uri($"{origin}/logout-callback/{alias}"));
            }
        }

        if (options.IncludeLocalhostCallback)
        {
            foreach (string origin in LocalhostCallbackOrigins(options.LocalhostCallback))
            {
                if (aliases.Count == 0)
                {
                    descriptor.RedirectUris.Add(new Uri($"{origin}/callback"));
                }
                else
                {
                    foreach (string alias in aliases)
                    {
                        descriptor.RedirectUris.Add(new Uri($"{origin}/callback/{alias}"));
                    }
                }
            }
        }

        return descriptor;
    }

    /// <summary>
    /// Origins to register for the local-dev callback. A loopback origin
    /// (<c>127.0.0.0/8</c>, <c>localhost</c>, <c>::1</c>) is registered over
    /// both <c>http</c> and <c>https</c> on the same host and port, so
    /// <c>wrangler dev</c> works whether or not it runs with
    /// <c>--local-protocol https</c>. Any other origin is registered as given.
    /// </summary>
    public static IReadOnlyList<string> LocalhostCallbackOrigins(string localhostCallback)
    {
        string origin = localhostCallback.TrimEnd('/');

        if (!Uri.TryCreate(origin, UriKind.Absolute, out Uri? uri)
            || !uri.IsLoopback
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            return [origin];
        }

        string hostAndPort = uri.GetComponents(UriComponents.HostAndPort, UriFormat.UriEscaped);
        return [$"http://{hostAndPort}", $"https://{hostAndPort}"];
    }
}
