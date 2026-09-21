# Umbraco.Mcp.HostedAuth

Repo/process notes for anyone (human or agent) working on this repo, as
opposed to using the package — package docs live in [README.md](README.md).

## Repo structure

One package, one NuGet `PackageId` (`Umbraco.Mcp.HostedAuth`), shipped from
**two independent version lines** in the same repo:

- `main` / `dev` — CMS 18 (`Umbraco.Cms.Api.Management [18.0.0, 19.0.0)`)
- `v17/main` / `v17/dev` — CMS 17 (`Umbraco.Cms.Api.Management [17.3.0, 18.0.0)`)

Both lines publish under the same `PackageId`, so they're versions of **one**
NuGet listing, not two — that's why `umbraco-marketplace.json` only exists
on `main` (the repo's default branch): the Marketplace only needs it once.

Everything else — source (`src/Umbraco.Mcp.HostedAuth/`), README, pipeline —
exists on both lines, independently, and *does* differ between them
(dependency bound, pipeline trigger branch, a few "17"-vs-"18" comments).
Never assume a change made on one line applies unmodified to the other.

## Build & pack

```bash
dotnet build src/Umbraco.Mcp.HostedAuth/Umbraco.Mcp.HostedAuth.csproj -c Release
dotnet pack src/Umbraco.Mcp.HostedAuth/Umbraco.Mcp.HostedAuth.csproj -c Release --no-build -o /tmp/nupkg
```

No test project exists yet. After any change to `Directory.Build.props`
(icon, README packaging, etc.), pack once and inspect the `.nupkg` — those
`Exists()`-conditioned `<ItemGroup>` blocks are easy to get wrong silently.

## Branching & release (two-branch gitflow)

Full mechanics: the `release-flow` skill / `references/gitflow.md`. Repo
specifics:

- Day-to-day work branches off `dev` (or `v17/dev`), PRs back into it,
  **squash merge**.
- A release branches off `dev` into `release/<version>`. **The only file that
  needs a version bump is `Directory.Build.props`'s `<Version>`** — nothing
  else hardcodes it. PR into `main` with a **merge commit, not squash** — the
  tag automation and the main→dev sync both key off that commit.
- Landing on `main` (or `v17/main`) triggers, in order:
  1. `build/azure-pipelines.yml`'s `Publish` stage → the `umbracoprereleases`
     MyGet feed. Fires on every push to `main`/`v17/main`, which under
     gitflow means every merged release, not every commit.
  2. `.github/workflows/release-tag.yml` → tags `v<version>` + creates a
     GitHub Release. Idempotent — a no-op if `<Version>` didn't change.
  3. That tag push triggers `build/azure-pipelines.yml`'s `PublishNuGetOrg`
     stage → nuget.org. Needs the `NuGetOrgApiKey` secret pipeline variable
     in Azure Pipelines (Pipeline → Edit → Variables) — doesn't exist yet as
     of this writing.
  4. `.github/workflows/sync-main-to-dev.yml` → opens a PR merging `main`
     back into `dev` (or `v17/main` into `v17/dev`), so the version bump
     doesn't strand there.
- Publishing to nuget.org (the package is tagged `umbraco-marketplace`) is
  also what gets it listed on the [Umbraco Marketplace](https://marketplace.umbraco.com/) —
  no separate submission step, synced nightly from NuGet.

## Gotchas

- **`build/azure-pipelines.yml`'s path is not yet live.** The Azure DevOps
  pipeline definition's own "YAML file path" setting is a single value
  shared across every branch it builds, and it still points at the pre-move
  root path (`azure-pipelines.yml`). Don't flip it
  (`az pipelines update --yaml-path build/azure-pipelines.yml`, or the
  portal equivalent) until the move has actually landed on **both** `main`
  and `v17/main` via a real release — flip it too early and the next
  push-triggered run on whichever line hasn't released yet finds nothing.
- **Merging across the two lines silently picks the wrong side.** A
  criss-cross history merge (e.g. merging one line's branch into the
  other's) can resolve version-specific files — the `PackageReference`
  bound, "17"-vs-"18" comments, the pipeline's trigger branch name — to the
  *wrong* line's content without flagging a conflict. This has actually
  happened once, during the repo's migration to this org. Always diff the
  result against the pre-merge branch tip for `src/**`, the `.csproj`, and
  `build/azure-pipelines.yml` before pushing.
- Only `main` carries `umbraco-marketplace.json` — don't add a copy on
  `v17/main`.
