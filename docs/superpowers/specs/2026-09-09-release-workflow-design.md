# Release Workflow: NuGet Package on Tag

**Date:** 2026-09-09
**Branch:** `refactor/repo-organization`
**Status:** Design — approved in brainstorming, pending implementation plan

## Goal

Add a GitHub Actions workflow that, when a release tag of the form `vMAJOR.MINOR.PATCH[-prerelease]` is pushed, builds and tests the solution, packs every library project under `src/` with the version taken from the tag, and publishes the resulting `.nupkg` and `.snupkg` files as assets on a GitHub Release for that tag.

## Context

- Repository `Cadtastic-Solutions/StateStore` is public; default branch is `master`. `.github/workflows/` is empty. No tags exist yet. The repo has no Actions secrets and no Environments.
- Today there is one packable project, `src/StateStore/StateStore.csproj`. The restructure design (`2026-05-22-repo-restructure-design.md`) adds `src/StateStore.MongoDb`, `src/StateStore.Sqlite`, and a `global.json`. This workflow is designed to work before and after that restructure without edits: it packs whatever csproj files exist under `src/`, and it selects the SDK from `global.json` when that file is present.
- The package version is hard-coded as `1.0.0` in the csproj (Release-only `PropertyGroup`). The restructure hoists it into `Directory.Build.props`. In both cases an MSBuild global property passed on the command line (`-p:Version=`) overrides the project value, so the workflow does not depend on where the property lives.
- Tests are xunit v3 targeting `net10.0` only and need no external services. The library multi-targets `net8.0;net9.0;net10.0`; those targets compile with the .NET 10 SDK alone because targeting packs are restored from NuGet.
- The restructure design listed "GitHub Actions CI workflows" as a non-goal and "publish packages on tag" as a follow-up. This spec is that follow-up and supersedes the non-goal line.

## Decisions

| Decision | Choice |
|---|---|
| Trigger | Push of a tag matching `v*`, then strict release-tag validation in the first step |
| Version source | The tag. Strip the leading `v` and pass it as `-p:Version=` |
| Destination | GitHub Release only. No nuget.org, no GitHub Packages, no secrets |
| Shape | Two jobs: `build` (`contents: read`) then `release` (`contents: write`) |
| Release tooling | Runner-preinstalled `gh` CLI with the built-in `GITHUB_TOKEN`; no third-party release action |
| Pre-release flag | Set when the version contains a hyphen (e.g. `1.0.0-alpha.1`) |
| Release notes | `gh release create --generate-notes` |
| Re-run behavior | Create-or-update: an existing release gets assets uploaded with `--clobber` |
| Runner | `ubuntu-latest` for both jobs |
| SDK setup | `actions/setup-dotnet@v6`. If `global.json` exists at the tagged commit, use `global-json-file`; otherwise `dotnet-version: 10.0.x` |
| Projects packed | Every `src/*/*.csproj`, individually. Not the solution |
| OS test matrix | No |
| PR-triggered CI | No (follow-up) |

## Non-goals

- A build-and-test workflow for pull requests or pushes to `master`.
- Publishing to nuget.org or GitHub Packages.
- Package or assembly signing.
- Windows or macOS test coverage.

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

The tag glob is a coarse filter using GitHub's filter-pattern syntax, where `+` means one or more of the preceding character and `.` is literal. Strict validation happens in the first step of the `build` job. The concurrency group keys on the tag ref so two pushes of the same tag cannot race. Setting `cancel-in-progress: false` lets an in-flight release finish rather than being killed partway through uploading assets.

### Workflow-level environment

```yaml
env:
  DOTNET_NOLOGO: true
  DOTNET_CLI_TELEMETRY_OPTOUT: true
```

### Release-tag pattern

A tag is accepted when it matches this extended regular expression, used with bash `[[ =~ ]]`:

```
^v(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)(-[0-9A-Za-z-]+(\.[0-9A-Za-z-]+)*)?$
```

That is `vMAJOR.MINOR.PATCH` with no leading zeros in the numeric parts, optionally followed by a hyphen and one or more dot-separated alphanumeric pre-release identifiers. SemVer build metadata is deliberately rejected, because NuGet strips a `+suffix` from the package filename, which would break the version check in `build` step 9. Leading zeros inside pre-release identifiers are not policed.

Accepted: `v1.0.0`, `v1.2.3-alpha.1`, `v2.0.0-rc.2`. Rejected: `v1.0`, `v01.0.0`, `v1.0.0.1`, `v1.0.0-`, `v1.0.0+build.5`.

### Job `build`

`runs-on: ubuntu-latest`, `permissions: contents: read`. Exposes one output, `prerelease`, valued `true` or `false`, taken from step 1 and consumed by the `release` job. The version is not a job output; `release` derives the tag from `github.ref_name` and needs nothing else.

| # | Step | Detail |
|---|---|---|
| 1 | Resolve version from tag | Bash, `id: version`. Runs before checkout so a malformed tag fails in seconds. Reads `TAG="$GITHUB_REF_NAME"` and tests it against the release-tag pattern above. On mismatch, emits `::error::Tag '<tag>' is not a release tag. Expected vMAJOR.MINOR.PATCH or vMAJOR.MINOR.PATCH-prerelease.` and exits 1. On match, writes `VERSION=${TAG#v}` to `$GITHUB_ENV` so every later step in this job can read `$VERSION`, and writes `prerelease=true` or `prerelease=false` to `$GITHUB_OUTPUT`, true when the version contains a hyphen. |
| 2 | Checkout | `actions/checkout@v7`, default depth. The .NET 8+ SDK bundles SourceLink for GitHub; it needs only the commit SHA and the origin URL, both present in a shallow clone. |
| 3 | Set up .NET | Two mutually exclusive `actions/setup-dotnet@v6` steps. The first runs `if: hashFiles('global.json') != ''` with `global-json-file: global.json`. The second runs `if: hashFiles('global.json') == ''` with `dotnet-version: 10.0.x`. This keeps the workflow correct when the restructure adds a `global.json` pinned with `rollForward: latestPatch`, which a plain `10.0.x` install would not satisfy. |
| 4 | Restore | `dotnet restore StateStore.sln`. |
| 5 | Build | `dotnet build StateStore.sln -c Release --no-restore -p:Version=$VERSION -p:ContinuousIntegrationBuild=true`. `ContinuousIntegrationBuild` normalizes PDB source paths for deterministic builds. Because the project sets `GeneratePackageOnBuild` for Release, this step also drops a `.nupkg` into `bin/Release/`. That copy is ignored; `release` reads only `artifacts/packages`. |
| 6 | Test | `dotnet test StateStore.sln -c Release --no-build -p:Version=$VERSION -p:ContinuousIntegrationBuild=true --logger trx --results-directory artifacts/test-results`. Running against the solution executes only projects with `IsTestProject=true`, so the benchmarks project and any future samples are no-ops. The same `-p:` values are passed to every `--no-build` step so MSBuild evaluates identical properties across build, test, and pack. |
| 7 | Upload test results | `actions/upload-artifact@v7`, `if: failure()`, name `test-results`, path `artifacts/test-results`, `if-no-files-found: ignore`. |
| 8 | Pack | Bash loop with `shopt -s nullglob` over `src/*/*.csproj`. Fails with `::error::` if the glob matches nothing. For each project: `dotnet pack "$proj" -c Release --no-build -o artifacts/packages -p:Version=$VERSION -p:ContinuousIntegrationBuild=true`. Packing per project rather than the solution guarantees that tests, benchmarks, and future samples can never appear in a release regardless of their `IsPackable` value, and it picks up the MongoDb and Sqlite packages automatically once the split lands. |
| 9 | Verify packages | Fails if `artifacts/packages` contains no `.nupkg`. For every `.nupkg` and `.snupkg` present, fails unless the filename ends in `.$VERSION.nupkg` or `.$VERSION.snupkg`, which proves the version override took effect. Prints `ls -l artifacts/packages`. |
| 10 | Upload packages | `actions/upload-artifact@v7`, name `packages`, path `artifacts/packages/*`, `if-no-files-found: error`. |

### Job `release`

`needs: build`, `runs-on: ubuntu-latest`, `permissions: contents: write`. Job-level `env`: `GH_TOKEN: ${{ github.token }}`, `GH_REPO: ${{ github.repository }}`, `TAG: ${{ github.ref_name }}`, `PRERELEASE: ${{ needs.build.outputs.prerelease }}`. No checkout is needed; `gh` resolves the repository from `GH_REPO`.

| # | Step | Detail |
|---|---|---|
| 1 | Download packages | `actions/download-artifact@v8`, name `packages`, path `artifacts/packages`. |
| 2 | Create or update release | Bash. Collects `artifacts/packages/*.nupkg` and `*.snupkg` into an array with `nullglob` and fails with `::error::` if empty. If `gh release view "$TAG"` succeeds, runs `gh release upload "$TAG" <assets> --clobber`. Otherwise runs `gh release create "$TAG" <assets> --title "$TAG" --generate-notes --verify-tag`, adding `--prerelease` when `PRERELEASE` is `true`. |

Behavioral consequences of create-or-update:

- **Re-running a failed `release` job** uploads over any partially attached assets and succeeds, as long as the `packages` artifact is still within its retention period, 90 days by default.
- **Publishing a release from the GitHub UI first**, which creates the tag and fires this workflow, results in the workflow attaching packages to that release and leaving the hand-written notes untouched.
- **The update path changes nothing but assets.** If an existing release was hand-created with the wrong pre-release flag or title, the workflow does not correct it.
- `--verify-tag` refuses to create a release for a tag that does not exist on the remote, so the workflow can never fabricate a release.

### Failure handling

| Condition | Where it fails | Outcome |
|---|---|---|
| Tag does not match the release-tag pattern | `build` step 1 | Run fails in seconds with a message naming the expected format. Nothing is built. |
| Restore or build error | `build` steps 4 to 5 | Run fails. Nothing packed or released. |
| A newly published advisory on a transitive dependency | `build` step 4 | Restore fails with an `NU19xx` error because `Directory.Build.props` sets `TreatWarningsAsErrors=true`. This can turn a green tag run red with no code change. The fix is a dependency bump or a scoped `NuGetAuditSuppress`, then a new tag. |
| Test failure | `build` step 6 | Run fails. TRX results attached to the run as `test-results`. Nothing packed or released. |
| No `.nupkg` produced, or a package name lacks the tag version | `build` step 9 | Explicit failure rather than an empty or misversioned release. |
| Two runs for the same tag | Concurrency group | The second waits for the first. |
| `gh release view` fails for a reason other than not-found, such as an API outage | `release` step 2 | Falls through to the create path, which fails with "already exists" if a release does exist. Re-run the `release` job. |
| `release` job fails after `build` succeeded | `release` | Re-run the `release` job alone from the Actions UI; the `packages` artifact is already stored. |

### Preconditions

1. **The Release build compiles.** Satisfied on this branch as of commits `33b441f` and `836643f`, which bumped `Microsoft.Data.Sqlite` to `10.0.12`, pulling `SQLitePCLRaw 2.1.12`, to clear `NU1903` for advisory GHSA-2m69-gcr7-jv3q. Residual risk: the restructure design still specifies `Microsoft.Data.Sqlite 10.0.8` for the new `StateStore.Sqlite.csproj`, which would reintroduce the failure if implemented as written. See Follow-ups.
2. **The tag points at a commit that contains the workflow file.** Tag-push workflows run from the tagged commit, so the workflow has to be committed before the first release tag is created.

## Verification

To be carried out by the implementation plan.

1. **Static check:** `actionlint .github/workflows/release.yml` passes. `actionlint` is installed locally via winget.
2. **Local dry run of the packaging mechanics** from the repo root, using the exact commands from `build` steps 4, 5, 8, and 9 with `VERSION=0.0.1-probe` and an output directory in the scratchpad. Expect exactly `StateStore.0.0.1-probe.nupkg`, plus a matching `.snupkg` once the restructure enables symbols, and the step-9 name check to pass.
3. **End-to-end**, after the workflow is committed on this branch: push a throwaway tag `v0.0.1-ci.1`, watch the run with `gh run watch`, confirm `gh release view v0.0.1-ci.1` shows a pre-release with the `.nupkg` attached, then clean up with `gh release delete v0.0.1-ci.1 --yes --cleanup-tag`.
4. **Negative path:** push the tag `v0.0.1.1`, which the coarse glob accepts but the release-tag pattern rejects, and confirm the run fails at `build` step 1 with no release created. Delete the tag afterwards with `git push --delete origin v0.0.1.1`.

## Follow-ups (out of scope)

- A `ci.yml` that builds and tests on pull requests and pushes to `master`.
- A third `publish-nuget` job gated by a GitHub Environment holding `NUGET_API_KEY`, pushing the same `packages` artifact to nuget.org.
- Amend `2026-05-22-repo-restructure-design.md`: point its CI non-goal here, and raise the `Microsoft.Data.Sqlite` version specified for `StateStore.Sqlite.csproj` from `10.0.8` to `10.0.12` or later so the provider split does not reintroduce `NU1903`.
