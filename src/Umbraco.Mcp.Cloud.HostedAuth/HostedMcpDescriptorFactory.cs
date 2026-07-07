using System.Globalization;
using OpenIddict.Abstractions;

namespace Umbraco.Mcp.Cloud.HostedAuth;

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
            foreach (string alias in aliases)
            {
                descriptor.RedirectUris.Add(new Uri($"{options.LocalhostCallback}/callback/{alias}"));
            }
        }

        return descriptor;
    }
}
