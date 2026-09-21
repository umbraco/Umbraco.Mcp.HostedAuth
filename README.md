# Umbraco.Mcp.HostedAuth

Auth glue that wires hosted Umbraco **MCP Cloudflare Workers** into the Umbraco
backoffice OAuth flow — on Umbraco Cloud *and* self-hosted. Install the
package, optionally add a small `HostedMcp` configuration block, and the
workers can authenticate users through the backoffice without any
hand-written composers.

It does three things:

1. **Registers each MCP worker as an OpenIddict `authorization_code` client** on
   startup (redirect/logout URIs, extended token lifetimes), so the worker can
   drive the backoffice OAuth flow.
2. **Fixes cold-start SSO** on the management-API authorize endpoint by bouncing
   unauthenticated browser hits through Umbraco ID (`identity_provider=Umbraco.UmbracoId`)
   instead of dead-ending on the standalone login form. **Cloud only.**
3. **Keeps MCP sessions alive across backoffice logins** without weakening
   security globally — see [Concurrent logins](#concurrent-logins). Both Cloud
   and self-hosted.

Client discovery, alias narrowing, and the SSO short-circuit are Cloud-only;
see [Self-hosted](#self-hosted) for what changes when there's no
`umbraco-cloud.json`.

## Requirements

- Umbraco CMS **17.3.0+** (this is the 17 line, on the `v17/main` branch; for
  CMS 18 use `main`). Requires `SecuritySettings.GetUserAllowConcurrentLogins`,
  first exposed in 17.3.0.
- On Umbraco **Cloud**, the cold-start SSO fix relies on the
  `Umbraco.UmbracoId` external login scheme registered by `Umbraco.Cloud.Cms`,
  and the project alias is read from `umbraco-cloud.json`. Self-hosted
  installs don't need either.

## Install

```bash
dotnet add package Umbraco.Mcp.HostedAuth
```

The `HostedMcpComposer` is discovered automatically — no `Program.cs` changes.

## Mode

`HostedMcp:Mode` picks which of the two setups below applies:

| Mode | Resolves to | Client discovery | Alias narrowing + SSO short-circuit |
|---|---|---|---|
| `Auto` (default) | `Cloud` if `umbraco-cloud.json` is present, else `SelfHosted` | — | — |
| `Cloud` | itself | installed-package detection (below) | on |
| `SelfHosted` | itself | explicit `HostedMcp:Clients` (below) | off |

Existing Cloud projects need no config change — `Auto` already resolves to
`Cloud` for them.

## What gets registered (Cloud)

Registration is **driven by installed packages** — you don't list clients. On
startup the package walks a built-in catalog and registers a client per variant
(`editor` + `developer`) for every product present:

| Product | Registered when | Clients |
|---|---|---|
| CMS | always | `umbraco-cms-editor-mcp-hosted`, `umbraco-cms-developer-mcp-hosted` |
| Commerce | `Umbraco.Commerce*` package installed | `umbraco-commerce-editor-mcp-hosted`, `umbraco-commerce-developer-mcp-hosted` |
| Engage | `Umbraco.Engage*` package installed | `umbraco-engage-editor-mcp-hosted`, `umbraco-engage-developer-mcp-hosted` |
| Workflow | `Umbraco.Workflow*` package installed | `umbraco-workflow-editor-mcp-hosted`, `umbraco-workflow-developer-mcp-hosted` |

Detection reads the app's dependency context (`.deps.json`), so it reflects what
is actually installed regardless of assembly load order.

## Configuration (Cloud)

**None is required** — install the package and matching clients register
themselves. The `HostedMcp` section exists for **overrides only**.

```jsonc
{
  "HostedMcp": {
    "Products": {
      // CMS 'developer' worker's deployed id doesn't follow the convention yet:
      "cms": {
        "Clients": {
          "developer": { "ClientId": "umbraco-cms-dev-mcp-hosted" }
        }
      },
      // Force a product off even though its package is installed:
      "engage": { "Enabled": false }
    }
  }
}
```

## Self-hosted

With no `umbraco-cloud.json`, `Mode` resolves to `SelfHosted` automatically —
set it explicitly only if you want to force one mode regardless of that file.
List the clients you want registered under `Clients`; there's no
product/variant convention and no per-environment alias, so each client is
just an id and its worker's origin(s):

```jsonc
{
  "HostedMcp": {
    "Mode": "SelfHosted", // optional — Auto already resolves here without umbraco-cloud.json
    "Clients": [
      {
        "ClientId": "umbraco-cms-editor-mcp-hosted",
        "Origins": [ "https://mcp.example.com" ]
      }
    ]
  }
}
```

Redirect/logout URIs for a self-hosted client are just `{origin}/callback` and
`{origin}/logout-callback` — no `/{alias}` segment. Everything else (per-client
token lifetimes, idempotent update-in-place registration, the concurrent-login
carve-out) works exactly as it does on Cloud.

## Options

| Key | Default | Notes |
|---|---|---|
| `Enabled` | `true` | Master switch; `false` makes the package a no-op. |
| `Mode` | `Auto` | `Auto` \| `Cloud` \| `SelfHosted`. See [Mode](#mode). |
| `AccessTokenLifetime` | `01:00:00` | Per-client access-token lifetime. |
| `RefreshTokenLifetime` | `08:00:00` | Per-client refresh-token lifetime. |
| `IncludeLocalhostCallback` | `true` | Register the local wrangler dev callback. |
| `LocalhostCallback` | `http://127.0.0.1:8787` | Origin for the local dev callback. |
| `Clients[]` | *(empty)* | SelfHosted only. Explicit clients: `ClientId`, optional `DisplayName`, `Origins`. |
| `Products.{key}.Enabled` | *(auto-detect)* | Cloud only. Force a product on/off, bypassing detection. |
| `Products.{key}.Clients.{variant}.ClientId` | `umbraco-{key}-{variant}-mcp-hosted` | Cloud only. Override when the deployed worker's id differs. |
| `Products.{key}.Clients.{variant}.DisplayName` | *(derived)* | Cloud only. OpenIddict display name. |
| `Products.{key}.Clients.{variant}.Origins` | *(derived)* | Cloud only. Replaces the convention-derived origin list. |

The zone (`mcp.umbraco.ai`), the Umbraco major version, and the Cloud alias are
not configurable — the first two are fixed and the alias is read from
`umbraco-cloud.json` (`Deploy:Project:Alias`).

## Derived URLs (Cloud)

For each client, origins are `https://{key}.{variant}.{major}.[dev.]mcp.umbraco.ai`
(prod + dev — both are always registered), and per origin the redirect/logout
URIs are:

- `{origin}/callback` and `{origin}/callback/{alias}`
- `{origin}/logout-callback` and `{origin}/logout-callback/{alias}`
- plus `{LocalhostCallback}/callback/{alias}` when enabled.

`{alias}` is the environment's siteId. The hosted Worker builds its callback as
`/callback/{siteId}`, and the siteId is the environment's own subdomain — so live
and dev use different aliases (e.g. `hosted-mcp-worker-test` vs
`dev-hosted-mcp-worker-test`), read from `umbraco-cloud.json`
(`Deploy:Project:Workspaces[].Url`).

To keep the redirect-URI allowlist minimal, the callbacks are **narrowed to the
current environment** at runtime. Startup registers all known aliases as a safe
baseline; then on the first request served on a recognised
`{siteId}.{region}.umbraco.io` host, the clients are rewritten to that single
siteId's callbacks. The host is ground truth — immune to how `DOTNET_ENVIRONMENT`
is configured — and narrowing only ever acts on siteIds listed in
`umbraco-cloud.json`, so an unexpected host can't wipe the allowlist. Locally (no
`*.umbraco.io` host) it simply stays on the all-aliases baseline.

## Concurrent logins

By default Umbraco revokes **all** of a user's OpenIddict tokens on every
backoffice login (single-session enforcement), which would kill an active MCP
session. Rather than flip `UserAllowConcurrentLogins` on globally, this package
replaces only the `UserLoginSuccess` revoke handler with one that revokes
everything **except** tokens issued to the configured MCP clients. The
`UserSaved` / `UserDeleted` revocation is left intact, so disabling or deleting
a user still kills their MCP session immediately.

This carve-out **fails closed**: if the built-in revoke handler can't be found
and removed (e.g. an unsupported CMS version changed it), the app throws at
startup rather than silently letting both handlers run and revoke live MCP
tokens.

Client registration is **idempotent** — existing OpenIddict clients are updated
in place (preserving the application id and any live refresh tokens), so warm
restarts don't sever active MCP sessions.

## Releasing

Versioned via `Directory.Build.props`. The [Azure Pipeline](azure-pipelines.yml)
builds and packs on every push to `v17/main` under `src/**` or
`Directory.Build.props`, then — still on `v17/main` — pushes the `.nupkg` to
the `umbracoprereleases` MyGet feed. There's no tag step; bump the version in
`Directory.Build.props` and merge to `v17/main` to release.
