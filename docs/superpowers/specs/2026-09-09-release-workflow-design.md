# Release Workflow: NuGet Package on Tag

**Date:** 2026-09-09
**Branch:** `refactor/repo-organization`
**Status:** Design — approved in brainstorming, pending implementation plan

## Goal

Add a GitHub Actions workflow that, when a release tag of the form `vMAJOR.MINOR.PATCH[-prerelease]` is pushed, builds and tests the solution, packs every library project under `src/` with the version taken from the tag, and publishes the resulting `.nupkg` and `.snupkg` files as assets on a GitHub Release for that tag.

## Context

- Repository `Cadtastic-Solutions/StateStore` is public; default branch is `master`. `.github/workflows/` is empty. No tags exist yet. The repo has no Actions secrets and no Environments.
- Today there is one packable project, `src/StateStore/StateStore.csproj`. The restructure design (`2026-05-22-repo-restructure-design.md`) adds `src/StateStore.MongoDb` and `src/StateStore.Sqlite`. The workflow must work in both states without edits.
- The package version is hard-coded as `1.0.0` in the csproj (Release-only `PropertyGroup`). The restructure hoists it into `Directory.Build.props`. In both cases an MSBuild global property passed on the command line (`-p:Version=`) overrides the project value, so the workflow does not depend on where the property lives.
- Tests are xunit v3 targeting `net10.0` only and need no external services. The library multi-targets `net8.0;net9.0;net10.0`; those targets compile with the .NET 10 SDK alone because targeting packs are restored from NuGet.
- The restructure design listed "GitHub Actions CI workflows" as a non-goal and "publish packages on tag" as a follow-up. This spec is that follow-up and supersedes the non-goal line.

## Decisions

| Decision | Choice |
|---|---|
| Trigger | Push of a tag matching `v*`, then strict SemVer 2 validation in the first step |
| Version source | The tag. Strip the leading `v` and pass it as `-p:Version=` |
| Destination | GitHub Release only. No nuget.org, no GitHub Packages, no secrets |
| Shape | Two jobs: `build` (`contents: read`) then `release` (`contents: write`) |
| Release tooling | Runner-preinstalled `gh` CLI with the built-in `GITHUB_TOKEN`; no third-party release action |
| Pre-release flag | Set when the version contains a hyphen (e.g. `1.0.0-alpha.1`) |
| Release notes | `gh release create --generate-notes` |
| Re-run behavior | Create-or-update: an existing release gets assets uploaded with `--clobber` |
| Runner | `ubuntu-latest` for both jobs |
| SDK setup | `actions/setup-dotnet` with `dotnet-version: 10.0.x`; switch to `global-json-file` once `global.json` exists |
| Projects packed | Every `src/*/*.csproj`, individually. Not the solution |
| OS test matrix | No |
| PR-triggered CI | No (follow-up) |

## Non-goals

- A build-and-test workflow for pull requests or pushes to `master`.
- Publishing to nuget.org or GitHub Packages.
- Package or assembly signing.
- Windows or macOS test coverage.
- Fixing the NuGet audit failure described under Preconditions. That is tracked as a separate task.

## Workflow design

### File

`.github/workflows/release.yml`, `name: Release`.

### Trigger and concurrency

```yaml
on:
  push:
    tags:
      - 'v[0-9]+.[0-9]+.[0-9]+*'

concurrency:
  group: release-${{ github.ref }}
  cancel-in-progress: false
```

The tag glob is a coarse filter using GitHub's filter-pattern syntax. Strict validation happens in the first step of the `build` job. The concurrency group keys on the tag ref so two pushes of the same tag cannot race; `cancel-in-progress: false` lets an in-flight release finish rather than being killed halfway through uploading assets.

### Workflow-level environment

```yaml
env:
  DOTNET_NOLOGO: true
  DOTNET_CLI_TELEMETRY_OPTOUT: true
  DOTNET_SKIP_FIRST_TIME_EXPERIENCE: true
```

### Job `build`

`runs-on: ubuntu-latest`, `permissions: contents: read`. Exposes two outputs, `version` and `prerelease`, consumed by the `release` job.

| # | Step | Detail |
|---|---|---|
| 1 | Resolve version from tag | Bash, `id: version`. Runs before checkout so a malformed tag fails in seconds. `TAG="$GITHUB_REF_NAME"`. Must match `^v(0\|[1-9][0-9]*)\.(0\|[1-9][0-9]*)\.(0\|[1-9][0-9]*)(-[0-9A-Za-z-]+(\.[0-9A-Za-z-]+)*)?$`. On mismatch, emit `::error::Tag '<tag>' is not a release tag. Expected vMAJOR.MINOR.PATCH or vMAJOR.MINOR.PATCH-prerelease.` and `exit 1`. On match, write `version=${TAG#v}` and `prerelease=true` or `false` (true when the version contains `-`) to `$GITHUB_OUTPUT`. |
| 2 | Checkout | `actions/checkout@v7`, default depth. SourceLink (planned) only needs the commit SHA and remote URL, both present in a shallow clone. |
| 3 | Set up .NET | `actions/setup-dotnet@v6` with `dotnet-version: 10.0.x`. |
| 4 | Restore | `dotnet restore StateStore.sln`. |
| 5 | Build | `dotnet build StateStore.sln -c Release --no-restore -p:Version=$VERSION -p:ContinuousIntegrationBuild=true`. `ContinuousIntegrationBuild` normalizes PDB source paths, which the planned SourceLink configuration relies on and which is harmless without it. |
| 6 | Test | `dotnet test StateStore.sln -c Release --no-build -p:Version=$VERSION -p:ContinuousIntegrationBuild=true --logger trx --results-directory artifacts/test-results`. `dotnet test` on the solution runs only projects with `IsTestProject=true`; the benchmarks project is skipped. The same `-p:` values are passed to every `--no-build` step so MSBuild evaluates identical properties across build, test, and pack. |
| 7 | Upload test results | `actions/upload-artifact@v7`, `if: failure()`, name `test-results`, path `artifacts/test-results`, `if-no-files-found: ignore`. |
| 8 | Pack | Bash loop with `shopt -s nullglob` over `src/*/*.csproj`. Fail with `::error::` if the glob matches nothing. For each project: `dotnet pack "$proj" -c Release --no-build -o artifacts/packages -p:Version=$VERSION -p:ContinuousIntegrationBuild=true`. Packing per project rather than the solution guarantees that tests, benchmarks, and future samples can never appear in a release regardless of their `IsPackable` value, and it picks up the MongoDb and Sqlite packages automatically once the split lands. |
| 9 | Verify packages | Fail if `artifacts/packages` contains no `.nupkg`. Fail if any `.nupkg` filename does not end in `.$VERSION.nupkg`, which proves the version override took effect. Print `ls -l artifacts/packages`. |
| 10 | Upload packages | `actions/upload-artifact@v7`, name `packages`, path `artifacts/packages/*`, `if-no-files-found: error`. |

### Job `release`

`needs: build`, `runs-on: ubuntu-latest`, `permissions: contents: write`. Job-level `env`: `GH_TOKEN: ${{ github.token }}`, `GH_REPO: ${{ github.repository }}`, `TAG: ${{ github.ref_name }}`, `PRERELEASE: ${{ needs.build.outputs.prerelease }}`. No checkout is needed; `gh` resolves the repository from `GH_REPO`.

| # | Step | Detail |
|---|---|---|
| 1 | Download packages | `actions/download-artifact@v8`, name `packages`, path `artifacts/packages`. |
| 2 | Create or update release | Bash. Collect `artifacts/packages/*.nupkg` and `*.snupkg` into an array with `nullglob`; fail with `::error::` if empty. If `gh release view "$TAG"` succeeds, run `gh release upload "$TAG" <assets> --clobber`. Otherwise run `gh release create "$TAG" <assets> --title "$TAG" --generate-notes --verify-tag`, adding `--prerelease` when `PRERELEASE` is `true`. |

Behavioral consequences of create-or-update:

- **Re-running a failed `release` job** uploads over any partially attached assets and succeeds.
- **Publishing a release from the GitHub UI first** (which creates the tag and fires this workflow) results in the workflow attaching packages to that release and leaving the hand-written notes untouched.
- `--verify-tag` refuses to create a release for a tag that does not exist on the remote, so the workflow can never fabricate a release.

### Failure handling

| Condition | Where it fails | Outcome |
|---|---|---|
| Tag does not match the SemVer pattern | `build` step 1 | Run fails in seconds with a message naming the expected format. Nothing is built. |
| Restore or build error | `build` steps 4 to 5 | Run fails. Nothing packed or released. |
| Test failure | `build` step 6 | Run fails. TRX results attached to the run as `test-results`. Nothing packed or released. |
| No `.nupkg` produced, or a package name lacks the tag version | `build` step 9 | Explicit failure rather than an empty or misversioned release. |
| Two runs for the same tag | Concurrency group | The second waits for the first. |
| `release` job fails after `build` succeeded | `release` | Re-run the `release` job alone from the Actions UI; the `packages` artifact is already stored. |

### Preconditions

1. **The Release build must compile.** As of this writing, `dotnet build StateStore.sln -c Release` fails at restore with `NU1903` because `SQLitePCLRaw.lib.e_sqlite3 2.1.11` (transitive via `Microsoft.Data.Sqlite 10.0.8`) has advisory GHSA-2m69-gcr7-jv3q and `Directory.Build.props` sets `TreatWarningsAsErrors=true`. Patched versions `2.1.12` and `2.1.13` exist on nuget.org. Fixing this is a separate task and is not part of this spec; the workflow cannot go green until it lands.
2. **The tag must point at a commit that contains the workflow file.** Tag-push workflows run from the tagged commit, so the workflow has to be committed before the first release tag is created.

## Verification

To be carried out by the implementation plan.

1. **Static check:** `actionlint .github/workflows/release.yml` passes. `actionlint` is installed locally via winget.
2. **Local dry run of the packaging mechanics**, independent of the audit fix, by disabling audit for that one local invocation only (never in the repo config): from the repo root, run restore/build/pack with `-p:Version=0.0.1-probe -p:NuGetAudit=false` and confirm the output directory contains exactly `StateStore.0.0.1-probe.nupkg` (plus `.snupkg` once the restructure enables symbols). Confirm the same loop-over-`src/*/*.csproj` command from step 8 is what is used.
3. **End-to-end**, after the workflow is committed and the audit fix is on the same commit: push a throwaway tag `v0.0.1-ci.1` on this branch, watch the run with `gh run watch`, confirm `gh release view v0.0.1-ci.1` shows a pre-release with the `.nupkg` attached, then clean up with `gh release delete v0.0.1-ci.1 --yes --cleanup-tag`.
4. **Negative path:** push a tag `v0.0.1-ci.1x` (or similar) that matches the coarse glob but fails strict validation, and confirm the run fails at step 1 with no release created. Delete the tag afterwards.

## Follow-ups (out of scope)

- A `ci.yml` that builds and tests on pull requests and pushes to `master`.
- A third `publish-nuget` job gated by a GitHub Environment holding `NUGET_API_KEY`, pushing the same `packages` artifact to nuget.org.
- Switch `actions/setup-dotnet` to `global-json-file: global.json` when the restructure plan adds `global.json`.
- Amend the non-goals section of `2026-05-22-repo-restructure-design.md` to point here.
