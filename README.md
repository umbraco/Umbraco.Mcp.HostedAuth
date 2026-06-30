# Umbraco.Cloud.Mcp.HostedAuth

Auth glue that wires hosted Umbraco **MCP Cloudflare Workers** into the Umbraco
backoffice OAuth flow on Umbraco Cloud. Install the package, add a small
`HostedMcp` configuration block, and the workers can authenticate users through
the backoffice without any hand-written composers.

It does three things:

1. **Registers each MCP worker as an OpenIddict `authorization_code` client** on
   startup (redirect/logout URIs, extended token lifetimes), so the worker can
   drive the backoffice OAuth flow.
2. **Fixes cold-start SSO** on the management-API authorize endpoint by bouncing
   unauthenticated browser hits through Umbraco ID (`identity_provider=Umbraco.UmbracoId`)
   instead of dead-ending on the standalone login form.
3. **Keeps MCP sessions alive across backoffice logins** without weakening
   security globally — see [Concurrent logins](#concurrent-logins).

## Requirements

- Umbraco CMS **17.3.0+** (first version exposing
  `SecuritySettings.GetUserAllowConcurrentLogins`).
- An Umbraco **Cloud** project — the cold-start SSO fix relies on the
  `Umbraco.UmbracoId` external login scheme registered by `Umbraco.Cloud.Cms`,
  and the project alias is read from `umbraco-cloud.json`.

## Install

```bash
dotnet add package Umbraco.Cloud.Mcp.HostedAuth
```

The `HostedMcpComposer` is discovered automatically — no `Program.cs` changes.

## Configuration

Add a `HostedMcp` section. Only `Type` per client is required; everything else
is derived by convention and can be overridden.

```jsonc
{
  "HostedMcp": {
    "Clients": [
      { "Type": "editor" },
      { "Type": "developer", "ClientId": "umbraco-cms-dev-mcp-hosted" }
    ]
  }
}
```

### Full options

| Key | Default | Notes |
|---|---|---|
| `Enabled` | `true` | Master switch; `false` makes the package a no-op. |
| `Product` | `cms` | Host-name prefix, e.g. `cms` in `cms.editor.17.mcp.umbraco.ai`. |
| `Zone` | `mcp.umbraco.ai` | Base zone for worker origins. |
| `MajorVersion` | *(derived)* | Umbraco major version in the origin; defaults to the loaded Umbraco assembly version. |
| `CloudAlias` | *(derived)* | Tenant segment in `/callback/{alias}`; defaults to `Deploy:Project:Alias` from `umbraco-cloud.json`. |
| `AccessTokenLifetime` | `01:00:00` | Per-client access-token lifetime. |
| `RefreshTokenLifetime` | `08:00:00` | Per-client refresh-token lifetime. |
| `IncludeLocalhostCallback` | `true` | Register the local wrangler dev callback. |
| `LocalhostCallback` | `http://127.0.0.1:8787` | Origin for the local dev callback. |
| `Clients[].Type` | — | **Required.** Worker type label (`editor`, `developer`, …). |
| `Clients[].ClientId` | `umbraco-{Product}-{Type}-mcp-hosted` | Override when the deployed worker's id differs from the type label. |
| `Clients[].DisplayName` | *(derived)* | OpenIddict display name. |
| `Clients[].Origins` | *(derived)* | Replaces the convention-derived origin list. |

### Derived URLs

For each client, origins are `https://{Product}.{Type}.{Major}.[dev.]{Zone}`
(prod + dev), and per origin the redirect/logout URIs are:

- `{origin}/callback` and `{origin}/callback/{alias}`
- `{origin}/logout-callback` and `{origin}/logout-callback/{alias}`
- plus `{LocalhostCallback}/callback/{alias}` when enabled.

## Concurrent logins

By default Umbraco revokes **all** of a user's OpenIddict tokens on every
backoffice login (single-session enforcement), which would kill an active MCP
session. Rather than flip `UserAllowConcurrentLogins` on globally, this package
replaces only the `UserLoginSuccess` revoke handler with one that revokes
everything **except** tokens issued to the configured MCP clients. The
`UserSaved` / `UserDeleted` revocation is left intact, so disabling or deleting
a user still kills their MCP session immediately.

## Releasing

Versioned via `Directory.Build.props`. Pushing a `v*` tag runs the
[release workflow](.github/workflows/release.yml), which packs and pushes to
NuGet.org using the `NUGET_API_KEY` repository secret.

```bash
git tag v0.1.0
git push origin v0.1.0
```
