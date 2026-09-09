# Release Workflow: NuGet Package on Tag

**Date:** 2026-09-09
**Branch:** `refactor/repo-organization`
**Status:** Implemented — verified end-to-end on 2026-09-09 with tags `v0.0.1-ci.1` and `v0.0.1.1`, both since deleted

## Goal

Add a GitHub Actions workflow that, when a release tag of the form `vMAJOR.MINOR.PATCH[-prerelease]` is pushed, builds and tests the solution, packs every library project under `src/` with the version taken from the tag, and publishes the resulting packages as assets on a GitHub Release for that tag. Today that means one `.nupkg`; symbol packages are attached automatically once the restructure enables `IncludeSymbols`.

## Context

- Repository `Cadtastic-Solutions/StateStore` is public; default branch is `master`. `.github/workflows/` is empty. No tags exist yet. The repo has no Actions secrets and no Environments.
- Today there is one packable project, `src/StateStore/StateStore.csproj`. The restructure design (`2026-05-22-repo-restructure-design.md`) adds `src/StateStore.MongoDb`, `src/StateStore.Sqlite`, and a `global.json`. This workflow is designed to work before and after that restructure without edits: it packs whatever csproj files exist directly under `src/`, and it selects the SDK from `global.json` when that file is present.
- The package version is hard-coded as `1.0.0` in the csproj (Release-only `PropertyGroup`). The restructure hoists it into `Directory.Build.props`. In both cases an MSBuild global property passed on the command line (`-p:Version=`) overrides the project value, so the workflow does not depend on where the property lives. It also flows into `AssemblyVersion` and `FileVersion`, so the tag governs assembly identity too, not just the package filename.
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

### Workflow-level permissions, shell, and environment

```yaml
permissions: {}

defaults:
  run:
    shell: bash

env:
  DOTNET_NOLOGO: true
  DOTNET_CLI_TELEMETRY_OPTOUT: true
```

The empty `permissions` block sets a floor of no grants, so each job's own `permissions` reads as an explicit grant rather than a narrowing of whatever the repository or organization default happens to be. Any job added later without its own block gets nothing. The `shell: bash` default documents that the scripts use bash-only constructs such as `[[ =~ ]]`; GitHub's implicit shell on Linux runners is already bash, but making it explicit protects against a later `defaults` edit switching it. Both jobs also carry a `timeout-minutes` bound, because `cancel-in-progress: false` would otherwise let a wedged run hold the concurrency group for the six-hour default.

### Release-tag pattern

A tag is accepted when it matches this extended regular expression, used with bash `[[ =~ ]]`:

```
^v(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)(-(0|[1-9][0-9]*|[0-9]*[A-Za-z-][0-9A-Za-z-]*)(\.(0|[1-9][0-9]*|[0-9]*[A-Za-z-][0-9A-Za-z-]*))*)?$
```

That is `vMAJOR.MINOR.PATCH`, optionally followed by a hyphen and one or more dot-separated pre-release identifiers. Numeric parts and numeric pre-release identifiers may not carry leading zeros, matching SemVer 2 and, more importantly, matching NuGet's own strict version parser: a tag such as `v1.0.0-rc.01` would otherwise pass validation and then fail mid-build with `NETSDK1018: Invalid NuGet version string`, an error that names neither the tag nor the fix.

SemVer build metadata is deliberately rejected, because NuGet strips a `+suffix` from the package filename, which would break the version check in `build` step 9.

One acknowledged gap: the three numeric components are unbounded, while NuGet parses them as 32-bit integers. A tag such as `v2147483648.0.0` passes validation and then fails mid-build with `NETSDK1018`. The likelihood is negligible, and bounding the digits would obscure the pattern's otherwise direct derivation from the semver.org grammar, so the gap is documented rather than enforced.

The rejection message names the two rules people trip on most: no leading zeros in numeric parts (`-rc.1`, not `-rc.01`) and no `+build` metadata. Without that, the message describes `v1.0.0-rc.01` as valid while rejecting it.

Accepted: `v1.0.0`, `v1.2.3-alpha.1`, `v2.0.0-rc.2`, `v1.0.0-alpha-1`, `v1.0.0-0a`. Rejected: `v1.0`, `v01.0.0`, `v1.0.0.1`, `v1.0.0-`, `v1.0.0-rc.01`, `v1.0.0-alpha..1`, `v1.0.0+build.5`, `1.0.0`.

### Job `build`

`runs-on: ubuntu-latest`, `timeout-minutes: 15`, `permissions: contents: read`. Exposes one output, `prerelease`, valued `true` or `false`, taken from step 1 and consumed by the `release` job, which checks that it is exactly one of those two values before acting on it. The version is not a job output; `release` derives the tag from `github.ref_name` and needs nothing else.

Labels below are workflow steps in order; 3a and 3b are one logical step split across two mutually exclusive `uses` entries, so the job has eleven steps carrying ten labels. Cross-references elsewhere in this spec use the labels. Command strings quote the expansion as `-p:Version="$RELEASE_VERSION"`.

| # | Step | Detail |
|---|---|---|
| 1 | Resolve version from tag | Bash, `id: version`. Runs before checkout so a malformed tag fails in seconds. Reads `TAG="$GITHUB_REF_NAME"` and tests it against the release-tag pattern above. On mismatch, emits `::error::Tag '<tag>' is not a release tag. Expected vMAJOR.MINOR.PATCH or vMAJOR.MINOR.PATCH-prerelease, with no leading zeros in numeric parts (use -rc.1, not -rc.01) and no +build metadata.` and exits 1. On match, writes `RELEASE_VERSION=${TAG#v}` to `$GITHUB_ENV` so every later step in this job can read `$RELEASE_VERSION`. The name is deliberate: MSBuild promotes environment variables to properties, so a variable called `VERSION` would silently become the `Version` property for every later `dotnet` command, with behavior that differs between Debug and Release. It also writes `prerelease=true` or `prerelease=false` to `$GITHUB_OUTPUT`, true when the version contains a hyphen. |
| 2 | Checkout | `actions/checkout@v7`, default depth, with `persist-credentials: false`. Nothing after this step talks to git, and `dotnet pack` executes MSBuild from every csproj under `src/`, so the token is not left on disk for that code to read. The .NET 8+ SDK bundles SourceLink for GitHub; it needs only the commit SHA and the origin URL, both present in a shallow clone. |
| 3a | Set up .NET from `global.json` | `actions/setup-dotnet@v6`, `if: hashFiles('global.json') != ''`, with `global-json-file: global.json`. |
| 3b | Set up .NET 10 | `actions/setup-dotnet@v6`, `if: hashFiles('global.json') == ''`, with `dotnet-version: 10.0.x`. Steps 3a and 3b are mutually exclusive. Splitting them keeps the workflow correct when the restructure adds a `global.json` pinned with `rollForward: latestPatch`, which a plain `10.0.x` install would not satisfy. `hashFiles` resolves against the workspace, so both conditions evaluate correctly only because checkout precedes them. |
| 4 | Restore | `dotnet restore StateStore.sln`. Restore is the one step that takes no `-p:Version=`; NuGet resolution does not depend on the package version being produced. Because the tag version lives in `RELEASE_VERSION` rather than `VERSION`, MSBuild does not pick it up from the environment here either, so restore genuinely runs with the project's own version. |
| 5 | Build | `dotnet build StateStore.sln -c Release --no-restore -p:Version="$RELEASE_VERSION" -p:ContinuousIntegrationBuild=true`. `ContinuousIntegrationBuild` normalizes PDB source paths for deterministic builds. Because the project sets `GeneratePackageOnBuild` for Release, this step also drops a `.nupkg` into `bin/Release/`. That copy is ignored; `release` reads only `artifacts/packages`. |
| 6 | Test | `dotnet test StateStore.sln -c Release --no-build -p:Version="$RELEASE_VERSION" -p:ContinuousIntegrationBuild=true --logger trx --results-directory artifacts/test-results`. Running against the solution executes only projects with `IsTestProject=true`, so the benchmarks project and any future samples are no-ops. Every step after restore passes the same `-p:` values so MSBuild evaluates identical properties across build, test, and pack. |
| 7 | Upload test results | `actions/upload-artifact@v7`, `if: failure() && steps.test.outcome == 'failure'`, name `test-results`, path `artifacts/test-results`, `if-no-files-found: ignore`. The test step carries `id: test` for this. A bare `failure()` would also fire on a restore or build failure and show the upload as having run, which misleads on a red run. |
| 8 | Pack | Bash loop with `shopt -s nullglob` over `src/*/*.csproj`. Fails with `::error::` if the glob matches nothing. For each project: `dotnet pack "$proj" -c Release --no-build -o artifacts/packages -p:Version="$RELEASE_VERSION" -p:ContinuousIntegrationBuild=true`. Packing per project rather than the solution keeps release contents governed by what lives under `src/`, and it picks up the MongoDb and Sqlite packages automatically once the split lands. On its own that is not enough: `dotnet pack` on a project whose `IsPackable` evaluates to `false` exits 0 and writes nothing, so step 9 enforces one package per project. The invariant it depends on: every project intended for release lives directly under `src/`, and nothing else does. Tests, benchmarks, and the planned `samples/` tree are excluded because they sit outside `src/`, not because of their `IsPackable` value. One consequence worth naming: `Version` is an MSBuild global property, so it propagates across `ProjectReference`. Once the provider split lands, the MongoDb and Sqlite packages each declare a dependency on `StateStore` at exactly the tag version rather than at the csproj default. |
| 9 | Verify packages | Fails if `artifacts/packages` contains no `.nupkg`. Fails if the number of `.nupkg` files differs from the number of `src/*/*.csproj` projects, which is how a project that produced no package becomes a red run rather than a silent omission. For every package file present, fails unless the filename ends in `.$RELEASE_VERSION.nupkg` or `.$RELEASE_VERSION.snupkg`, which proves the version override took effect. Fails if `find src -mindepth 3 -name '*.csproj' -not -path '*/bin/*' -not -path '*/obj/*' -not -path '*/.*/*'` returns anything, which means a project was nested too deep for step 8's glob and would otherwise be dropped from the release on a green run. All three exclusions are load-bearing: without the hidden-path one, a nested git worktree under `src/.claude/` produces false positives, and the usual `-prune` idiom cannot be substituted because `-mindepth` is a global option in `find` rather than a test. Writes the resulting filenames to `$GITHUB_STEP_SUMMARY`; the packed-project list is written by step 8 as each pack succeeds. |
| 10 | Upload packages | `actions/upload-artifact@v7`, name `packages`, path `artifacts/packages/*`, `if-no-files-found: error`. |

### Job `release`

`needs: build`, `runs-on: ubuntu-latest`, `timeout-minutes: 10`, `permissions: contents: write`. Job-level `env` carries only `TAG: ${{ github.ref_name }}` and `PRERELEASE: ${{ needs.build.outputs.prerelease }}`. The `gh` credentials, `GH_TOKEN: ${{ github.token }}` and `GH_REPO: ${{ github.repository }}`, sit on the step that calls `gh`, so the one write token in the workflow is not in the environment of the artifact download or of any step added to this job later. No checkout is needed; `gh` resolves the repository from `GH_REPO`. The tag reaches bash only through the `env` mapping, never through inline `${{ }}` interpolation in a script body, so there is no script-injection surface.

| # | Step | Detail |
|---|---|---|
| 1 | Download packages | `actions/download-artifact@v8`, name `packages`, path `artifacts/packages`. |
| 2 | Create or update release | Bash. First fails with `::error::` unless `$PRERELEASE` is exactly `true` or `false`. A stale output reference yields an empty string rather than an error, and an empty value would otherwise read as "not a pre-release" and publish an `-rc` tag as the latest stable release, with nothing red anywhere in the run. Then collects `artifacts/packages/*.nupkg` and `*.snupkg` into an array with `nullglob` and fails with `::error::` if empty. Then probes with `gh release view "$TAG" --json isDraft --jq .isDraft`. If that succeeds the release exists, so it runs `gh release upload "$TAG" --clobber <assets>` and then fails with `::error::` if the release is a draft. Otherwise it runs `gh release create "$TAG" --title "$TAG" --generate-notes --verify-tag <assets>`, adding `--prerelease` when `PRERELEASE` is `true`. Finally appends the release URL to `$GITHUB_STEP_SUMMARY`. |

Behavioral consequences of create-or-update:

- **Re-running a failed `release` job** uploads over any partially attached assets, as long as the `packages` artifact is still within its retention period, 90 days by default. If the earlier failure left a draft, the re-run attaches the assets and then fails, rather than reporting success over an unpublished release.
- **A draft release counts as existing, and is treated as an error.** `gh release view` succeeds for a draft. This matters beyond hand-created drafts: `gh release create` works in three API calls, creating the release as a draft, uploading the assets, then publishing, so a failure partway through leaves a draft behind. The probe captures `isDraft` and the job fails after attaching assets, because a silent success over an unpublished release is the worst available outcome. Publishing or deleting the draft is a deliberate manual step.
- **Publishing a release from the GitHub UI first**, which creates the tag and fires this workflow, results in the workflow attaching packages to that release and leaving the hand-written notes untouched.
- **Drafting a release in the UI first and then pushing the tag now ends in a red run**, by design rather than regression. The workflow attaches the packages to the draft and then fails, because it cannot tell a deliberate draft from one left behind by a create that died partway. Publish the draft to finish.
- **The update path changes nothing but assets.** If an existing release was hand-created with the wrong pre-release flag or title, the workflow does not correct it.
- `--verify-tag` does more than its name suggests. Without it, `gh release create` creates a missing tag from the default branch HEAD and publishes a release whose contents, built from the tagged commit, disagree with the tag it names. With it, this job cannot write a ref at all, only releases.

### Failure handling

| Condition | Where it fails | Outcome |
|---|---|---|
| Tag does not match the release-tag pattern | `build` step 1 | Run fails in seconds with a message naming the expected format. Nothing is built. |
| Restore or build error | `build` steps 4 to 5 | Run fails. Nothing packed or released. |
| A newly published advisory on a transitive dependency | `build` step 4 | Restore fails with an `NU19xx` error because `Directory.Build.props` sets `TreatWarningsAsErrors=true`. This can turn a green tag run red with no code change. The fix is a dependency bump or a scoped `NuGetAuditSuppress`, then a new tag. |
| Test failure | `build` step 6 | Run fails. TRX results attached to the run as `test-results`. Nothing packed or released. |
| No `.nupkg` produced, or a package name lacks the tag version | `build` step 9 | Explicit failure rather than an empty or misversioned release. |
| Two runs for the same tag | Concurrency group | The second waits for the first. |
| `gh release create` fails partway through | `release` step 2 | A draft release is left behind. The run is red. A re-run attaches assets to the draft and then fails explicitly, so the draft never passes for a published release. Resolve by publishing the draft or deleting it, then re-running. |
| `gh release view` fails for a reason other than not-found, such as a rate limit or an API outage | `release` step 2 | Falls through to the create path, which cannot succeed against an existing release: the API returns 422 `already_exists`. The run is red and nothing is mispublished, though the log names the create step rather than the probe as the cause. Re-run the `release` job. |
| Fetching the release URL for the run summary fails | `release` step 2 | Deliberately does not fail the run. The release is already created and correct, so the summary line is left with an empty URL rather than turning a good release red. A blank URL in the summary means this, not a failed release. |
| `release` job fails after `build` succeeded | `release` | Re-run the `release` job alone from the Actions UI; the `packages` artifact is already stored. |

### Preconditions

1. **The Release build compiles.** Satisfied on this branch as of commits `33b441f` and `836643f`, which bumped `Microsoft.Data.Sqlite` to `10.0.12`, pulling `SQLitePCLRaw 2.1.12`, to clear `NU1903` for advisory GHSA-2m69-gcr7-jv3q. Residual risks introduced by the pending restructure are listed under Follow-ups.
2. **The tag points at a commit that contains the workflow file.** Tag-push workflows run from the tagged commit, so the workflow has to be committed before the first release tag is created.
3. **Immutable releases must be off, or the re-run path narrows.** If the repository or organization has GitHub's immutable-releases setting enabled, `gh release upload --clobber` against an already-published release fails, which breaks the re-run behavior the create-or-update design depends on. Confirm the setting before relying on re-runs.

## Verification

Carried out by `docs/superpowers/plans/2026-09-09-release-workflow.md`. All five checks passed; results recorded below.

1. **Static check:** `actionlint .github/workflows/release.yml` passes with zero findings. Note two corrections to the original plan here. actionlint silently disables its shellcheck rule when the binary is absent and still exits 0, so shellcheck 0.11.0 was installed and every lint command now prepends its directory to `PATH`; the checks above were re-run with it active. actionlint also does not validate action versions; the majors `checkout@v7`, `setup-dotnet@v6`, `upload-artifact@v7`, `download-artifact@v8` were each confirmed as the current release, and `download-artifact@v8` is documented as the counterpart to `upload-artifact@v7`.
2. **Tag pattern:** a harness extracts the shipped version step out of the YAML and executes it against 18 cases, asserting exit status, derived `RELEASE_VERSION`, and the `prerelease` output for each. All 18 behave as documented. Extracting rather than restating the regex means the test cannot pass while the workflow is broken; a mutation test confirmed it fails on an over-permissive pattern and on a swapped `prerelease` flag.
3. **Local dry run of the packaging mechanics** produced exactly `StateStore.0.0.1-probe.nupkg`, proving a command-line `Version` overrides the csproj. The step-9 verify logic was additionally executed against six synthetic cases: matching version, version mismatch, no packages, two nested projects, a hidden worktree directory, and two projects yielding one package.
4. **End-to-end.** Tag `v0.0.1-ci.1` produced run 34357667993: `build` green in 34s, `release` green in 11s. The release was a pre-release, not a draft, titled `v0.0.1-ci.1`, with `StateStore.0.0.1-ci.1.nupkg` attached at 154544 bytes and generated notes. The tag resolved to the intended commit. The published package was downloaded and inspected: nuspec version `0.0.1-ci.1`, all three target frameworks present, and `ProductVersion` inside the assembly reading `0.0.1-ci.1+5551043fbe3b5aeea3bc07920e3468615113e864`, confirming the tag governs assembly identity and the build commit is traceable from a shipped DLL. The `global.json` branch was skipped and `Set up .NET 10` ran, as intended with no `global.json` present. Release and tag were deleted; `gh release view` reported `isImmutable: false`, so cleanup was unobstructed.
5. **Negative path.** Tag `v0.0.1.1`, which the coarse glob accepts and the pattern rejects, produced run 34358149426: `build` failed, `release` skipped, no release created. Only `Set up job`, `Resolve version from tag`, and `Complete job` ran, so nothing was checked out or restored. The error read `Tag 'v0.0.1.1' is not a release tag.` with the expected-format guidance. Tag deleted.

After both runs, `git tag -l`, `git ls-remote --tags origin`, and `gh release list` were all empty. What remains is the published branch, two workflow runs in the Actions tab, and the successful run's `packages` artifact for its 90-day retention.

## Follow-ups (out of scope)

- A `ci.yml` that builds and tests on pull requests and pushes to `master`.
- A third `publish-nuget` job gated by a GitHub Environment holding `NUGET_API_KEY`, pushing the same `packages` artifact to nuget.org.
- ~~**Amend the restructure documents on three points**~~ **Done on 2026-09-09**, across two commits. `5551043` corrected four `Microsoft.Data.Sqlite` literals in `2026-05-22-repo-restructure.md` and one in its design doc, and added a warning note at the top of that plan, which is the artifact an executor actually follows. A follow-up commit then corrected the three stale statements left in `2026-05-22-repo-restructure-design.md` itself: the CI non-goal, the future-work line, and the false claim that `OutputType=Exe` makes a project non-packable. The three points, of which the second and third would otherwise have turned every tag run red once the restructure lands:
  - Point its CI non-goal here.
  - Raise the `Microsoft.Data.Sqlite` version specified for `StateStore.Sqlite.csproj` from `10.0.8` to `10.0.12` or later, so the provider split does not reintroduce `NU1903`.
  - Set `IsPackable=false` explicitly on `StateStore.Benchmarks` and on the four planned `samples/` projects. That design claims `OutputType=Exe` makes a project non-packable by default, which is false: `IsPackable` evaluates to `true` for `StateStore.Benchmarks` today, verified with `dotnet msbuild -getProperty:IsPackable -p:Configuration=Release`. (`StateStore.Tests` is `false` only because its csproj sets the property explicitly.) Its proposed `Directory.Build.props` sets `GeneratePackageOnBuild` and `PackageReadmeFile=PACKAGE.md` for every Release project, and a project with no `PACKAGE.md` then fails the build with `NU5039`, breaking `build` step 5. The `IsPackable != 'false'` guard on that file's `ItemGroup` does not save it: `Directory.Build.props` is imported before the SDK and before the project body, so the condition evaluates against an empty value and is always true.

## Amendments

**2026-09-09, after the Chunk 1 code quality review.** The first committed form of the validation step (`1b8c467`) was reviewed and the following changes were folded into this spec and into the plan before Chunk 2 was written, because each one gets more expensive with every step that references it.

| Change | Reason |
|---|---|
| Derived version variable renamed from `VERSION` to `RELEASE_VERSION` | MSBuild promotes environment variables to properties. Verified against the .NET 10 SDK: with `VERSION=9.9.9` in the environment, `dotnet msbuild -getProperty:Version` reports `9.9.9` in Debug and `1.0.0` in Release, so the old name made restore see the tag version ambiently and configuration-dependently |
| Workflow-level `permissions: {}` | Makes each job-level grant explicit; a future job without its own block gets nothing rather than the repository default |
| `defaults.run.shell: bash` | Documents the bash-only constructs and protects against a later `defaults` edit |
| `timeout-minutes` on both jobs (15 and 10) | With `cancel-in-progress: false`, a wedged run would otherwise hold the concurrency group for six hours and block every re-push of that tag |
| The `release` job validates `PRERELEASE` is exactly `true` or `false` | A stale output reference yields an empty string, which the original test treated as "not a pre-release" and would have published an `-rc` tag as the latest stable release with nothing red in the run |
| Rejection message names the leading-zero and build-metadata rules | The original message described `v1.0.0-rc.01` as valid while rejecting it |
| Deliberate choices commented in the YAML | The build-metadata exclusion in particular looks like a bug to fix; the comment stops that |
| Int32 bound on numeric components documented as an acknowledged gap | `v2147483648.0.0` passes validation and fails mid-build; negligible likelihood, and bounding the digits would obscure the pattern's derivation from the semver.org grammar |
| Verification claim corrected: shellcheck was not installed during planning | actionlint silently disables its shellcheck rule when the binary is missing and still exits 0. shellcheck 0.11.0 is now installed and on the PATH of every lint command in the plan; the complete workflow passes with it active |

**2026-09-09, after the Chunk 2 code quality review.** Applied before Chunk 3 so the final checksum is set once. The complete workflow is 219 lines.

| Change | Reason |
|---|---|
| Verify step asserts one `.nupkg` per `src/*/*.csproj` project | Verified empirically: `dotnet pack` with `IsPackable=false` exits 0 and writes nothing, so a project could vanish from a release on a green run. The spec previously claimed per-project packing prevented this; it did not. With one project today the zero-package check covered it; the restructure adds two more |
| Comment on the Build step naming the `--no-build` coupling | Test and Pack reuse Build output, so the `-p:Version` values must match exactly. If Build drifted alone, the filename check would pass while the assemblies carried the wrong version. The comment sits where an editor would make that mistake |
| Test-results upload scoped to `steps.test.outcome == 'failure'` | A bare `failure()` fires on any earlier failure, showing the upload as run during a restore or build failure |
| `persist-credentials: false` on checkout | Nothing after checkout needs git credentials, and `dotnet pack` executes MSBuild from every csproj under `src/`. Not a privilege boundary, since anyone who can push a tag can edit the workflow, but it removes a token from disk for one line |

**2026-09-09, after the Chunk 3 code quality review.** Applied before the end-to-end verification so the tag run exercises the final code. The complete workflow is 237 lines.

| Change | Reason |
|---|---|
| The existence probe captures `isDraft`, and the update path fails if the release is a draft | `gh release create` works in three API calls (create as draft, upload, publish), confirmed in its own help text, so a failure partway leaves a draft. A plain retry took the update branch, attached assets, and exited 0 with nothing published: a green run over an unpublished release. This is the same silent-success class as the empty `PRERELEASE` and the `IsPackable=false` findings |
| `GH_TOKEN` and `GH_REPO` moved from job `env` to the step that calls `gh` | The artifact download authenticates with the runtime token and has no use for them. Confines the only write credential in the workflow to the one step that needs it, and to steps added later |
| Flags precede positional asset paths in both `gh` invocations | Reads in the conventional order, and an asset path beginning with a hyphen can no longer be parsed as a flag. Not reachable today, since NuGet package ids cannot lead with a hyphen |
| The release URL is appended to `$GITHUB_STEP_SUMMARY` | The build job already summarizes what it packed; the run summary previously never said where the packages went |
| Comment added on `--verify-tag` | Its real purpose is preventing `gh` from fabricating a tag at the default branch HEAD, which the flag name does not convey, making it a plausible thing for a later editor to drop |

Considered after the re-review and deliberately not adopted, each a small polish that would cost a further amend-and-review cycle without changing behavior on any expected path: the count-mismatch message assumes fewer packages than projects, so a legacy `.symbols.nupkg` would point the operator at the wrong cause (mitigated by the restructure specifying `SymbolPackageFormat=snupkg`); the Verify step lists `artifacts/packages` only on success, so a red run lacks that listing; and a `.csproj` placed directly under `src/` rather than in a subdirectory is invisible to the glob, the nested check, and the count check alike, which is an unconventional layout the restructure does not use. From the Chunk 3 review, the probe still discards the `gh release view` diagnostic, so a rate-limit or auth failure is reported as the wrong cause; the outcome is a red run either way, never a mispublish, and the create step surfaces the real API error text. From the Chunk 3 re-review, the draft test reads any value other than `true` as published, which is defensive only because a bad `--json` field errors rather than returning empty; and `gh release view` also exposes `isImmutable`, which would let the job name immutable releases as the cause when `upload --clobber` is refused, worth folding in if that setting is ever enabled.
