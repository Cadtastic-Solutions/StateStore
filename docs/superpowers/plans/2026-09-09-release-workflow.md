# Release Workflow Implementation Plan

> **For agentic workers:** REQUIRED: Use superpowers:subagent-driven-development (if subagents available) or superpowers:executing-plans to implement this plan. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a GitHub Actions workflow that turns a pushed `vX.Y.Z` tag into a GitHub Release carrying the NuGet packages built from that commit.

**Architecture:** One workflow file, `.github/workflows/release.yml`, with two jobs. A `contents: read` job validates the tag, derives the package version from it, then builds, tests, packs every project directly under `src/`, and uploads the packages as a workflow artifact. A dependent `contents: write` job downloads that artifact and creates or updates the GitHub Release for the tag using the runner's preinstalled `gh` CLI. No secrets and no third-party actions are involved.

**Tech Stack:** GitHub Actions, bash, .NET 10 SDK (`dotnet restore/build/test/pack`), `gh` CLI, `actionlint` for static checking.

**Spec:** `docs/superpowers/specs/2026-09-09-release-workflow-design.md`

**Working directory:** Every command in this plan runs from the repository root, `C:/Users/AddamBoord/source/repos/StateStore`, in Git Bash. Note that some tooling sessions open in `src/` instead, so `cd` to the root first and verify with `ls StateStore.sln`.

**Scratchpad:** Local test scripts and probe output go in `$SCRATCH`, defined once per chunk as shown. Nothing in the scratchpad gets committed.

**Deliberately not in this plan:** the spec's Follow-ups section lists a PR-triggered `ci.yml` and a nuget.org publish job. Both are out of scope. Chunk 5 Task 5.3 is the one exception: it records three corrections the restructure plan needs, because that plan is still unexecuted and would otherwise reintroduce failures this workflow depends on not happening.

---

## File structure

| File | Responsibility | Status |
|---|---|---|
| `.github/workflows/release.yml` | The entire deliverable. Tag validation, version derivation, build, test, pack, artifact upload, release creation. | Create |
| `docs/superpowers/plans/2026-05-22-repo-restructure.md` | Unrelated pending plan. Gains a short "corrections required before execution" note in Task 5.3. | Modify (3 added lines) |

The workflow is a single file because its two jobs share one trigger, one version derivation, and one artifact. Splitting it would mean duplicating the tag contract. Reusable-workflow extraction is not warranted for one caller.

The tag pattern is defined once, as a `PATTERN=` assignment inside the validation step. The local test harness in Chunk 1 extracts that exact line out of the YAML rather than restating the regex, so the test can never drift from the shipped pattern.

---

## Chunk 1: Tag validation

The riskiest logic in the workflow is the regex, and it is the only part testable without pushing a tag. Build it test-first against the spec's accept and reject tables.

### Task 1.1: Write the failing tag-pattern test harness

**Files:**
- Create: `$SCRATCH/test-tag-pattern.sh` (scratchpad, not committed)

- [ ] **Step 1: Set up the scratchpad variable**

```bash
cd /c/Users/AddamBoord/source/repos/StateStore
export SCRATCH="/c/Users/ADDAMB~1/AppData/Local/Temp/claude/C--Users-AddamBoord-source-repos-StateStore-src/c815e48b-30c7-403b-a842-3369c744fc0c/scratchpad"
mkdir -p "$SCRATCH"
ls StateStore.sln
```

Expected: `StateStore.sln` is listed. If it is not, you are in the wrong directory.

- [ ] **Step 2: Write the test harness**

This script extracts the pattern from the workflow file and runs it against every tag in the spec's tables. Extracting rather than duplicating is the point: a test holding its own copy of the regex would pass while the shipped workflow was broken.

```bash
cat > "$SCRATCH/test-tag-pattern.sh" <<'SH'
#!/usr/bin/env bash
# Verifies the release-tag pattern shipped in .github/workflows/release.yml.
set -uo pipefail

WORKFLOW=".github/workflows/release.yml"

if [[ ! -f "$WORKFLOW" ]]; then
  echo "FAIL: $WORKFLOW does not exist"
  exit 1
fi

# Pull the single-quoted PATTERN= assignment out of the workflow's bash block.
PATTERN="$(sed -n "s/^[[:space:]]*PATTERN='\(.*\)'[[:space:]]*$/\1/p" "$WORKFLOW")"

if [[ -z "$PATTERN" ]]; then
  echo "FAIL: no PATTERN='...' assignment found in $WORKFLOW"
  exit 1
fi

echo "Pattern under test:"
echo "  $PATTERN"
echo

# Tags that MUST be accepted. Sourced from the spec's accepted list plus the
# happy-path verification tag.
ACCEPT=(
  v1.0.0
  v1.2.3-alpha.1
  v2.0.0-rc.2
  v1.0.0-alpha-1
  v1.0.0-0a
  v0.0.1-ci.1
  v10.20.30
)

# Tags that MUST be rejected. Sourced from the spec's rejected list plus the
# negative-path verification tag.
REJECT=(
  v1.0
  v01.0.0
  v1.0.0.1
  v1.0.0-
  v1.0.0-rc.01
  v1.0.0-alpha..1
  v1.0.0+build.5
  1.0.0
  v0.0.1.1
  v1.0.0-00
  ""
)

failures=0

for tag in "${ACCEPT[@]}"; do
  if [[ "$tag" =~ $PATTERN ]]; then
    echo "  ok       accept  $tag"
  else
    echo "  NOT OK   accept  $tag  (was rejected)"
    failures=$((failures + 1))
  fi
done

for tag in "${REJECT[@]}"; do
  if [[ "$tag" =~ $PATTERN ]]; then
    echo "  NOT OK   reject  ${tag:-<empty>}  (was accepted)"
    failures=$((failures + 1))
  else
    echo "  ok       reject  ${tag:-<empty>}"
  fi
done

echo
if [[ $failures -gt 0 ]]; then
  echo "FAIL: $failures case(s) behaved incorrectly"
  exit 1
fi
echo "PASS: all $(( ${#ACCEPT[@]} + ${#REJECT[@]} )) cases behaved as specified"
SH
chmod +x "$SCRATCH/test-tag-pattern.sh"
```

- [ ] **Step 3: Run the harness to verify it fails**

```bash
bash "$SCRATCH/test-tag-pattern.sh"
```

Expected: exit code 1 with `FAIL: .github/workflows/release.yml does not exist`. This confirms the harness actually checks the real file rather than passing vacuously.

### Task 1.2: Create the workflow with the validation step

**Files:**
- Create: `.github/workflows/release.yml`

- [ ] **Step 1: Write the workflow trigger and validation step**

The `run:` block reads the tag from `$GITHUB_REF_NAME`, an environment variable the runner sets. It deliberately does not interpolate `${{ github.ref_name }}` into the script body, which would allow a crafted ref name to inject shell.

```bash
cat > .github/workflows/release.yml <<'YML'
name: Release

on:
  push:
    tags:
      - 'v[0-9]+.[0-9]+.[0-9]+*'

concurrency:
  group: release-${{ github.ref }}
  cancel-in-progress: false

env:
  DOTNET_NOLOGO: true
  DOTNET_CLI_TELEMETRY_OPTOUT: true

jobs:
  build:
    runs-on: ubuntu-latest
    permissions:
      contents: read
    outputs:
      prerelease: ${{ steps.version.outputs.prerelease }}
    steps:
      - name: Resolve version from tag
        id: version
        run: |
          set -euo pipefail
          TAG="$GITHUB_REF_NAME"
          PATTERN='^v(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)(-(0|[1-9][0-9]*|[0-9]*[A-Za-z-][0-9A-Za-z-]*)(\.(0|[1-9][0-9]*|[0-9]*[A-Za-z-][0-9A-Za-z-]*))*)?$'
          if [[ ! "$TAG" =~ $PATTERN ]]; then
            echo "::error::Tag '$TAG' is not a release tag. Expected vMAJOR.MINOR.PATCH or vMAJOR.MINOR.PATCH-prerelease."
            exit 1
          fi
          VERSION="${TAG#v}"
          echo "VERSION=$VERSION" >> "$GITHUB_ENV"
          if [[ "$VERSION" == *-* ]]; then
            echo "prerelease=true" >> "$GITHUB_OUTPUT"
          else
            echo "prerelease=false" >> "$GITHUB_OUTPUT"
          fi
          echo "Releasing version $VERSION from tag $TAG"
YML
```

- [ ] **Step 2: Run the harness to verify it passes**

```bash
bash "$SCRATCH/test-tag-pattern.sh"
```

Expected: `PASS: all 18 cases behaved as specified`, exit code 0.

If any accept case fails, the regex was transcribed with an error. Compare it character by character against the spec's Release-tag pattern section. Do not "fix" the harness to match a broken pattern.

- [ ] **Step 3: Verify the file was committed with LF endings**

`.gitattributes` sets `* text=auto`, so Git normalizes the file to LF in the repository even though the working copy may show CRLF on Windows. Confirm the stored form:

```bash
git add .github/workflows/release.yml
git show :.github/workflows/release.yml | file -
```

Expected: output mentions `ASCII text` with no `CRLF` qualifier. CRLF in the stored file would break bash `run:` blocks on the Linux runner.

### Task 1.3: Lint the workflow

- [ ] **Step 1: Run actionlint**

```bash
"/c/Users/AddamBoord/AppData/Local/Microsoft/WinGet/Packages/rhysd.actionlint_Microsoft.Winget.Source_8wekyb3d8bbwe/actionlint" .github/workflows/release.yml
```

Expected: no output, exit code 0. actionlint 1.7.12 checks YAML validity, expression syntax, and context availability.

Note: actionlint does not validate action versions or that referenced actions exist. Those were confirmed current at design time.

- [ ] **Step 2: Commit**

```bash
git add .github/workflows/release.yml
git commit -m "Add release workflow with tag validation

Validates a pushed v* tag against the release-tag pattern and derives
the package version from it. Build, pack, and release steps follow."
```

---

## Chunk 2: Build, test, and pack

Add the steps that produce the packages. Each is verified locally against the real solution before being trusted in CI.

### Task 2.1: Add checkout and SDK setup

**Files:**
- Modify: `.github/workflows/release.yml`

- [ ] **Step 1: Append the checkout and setup steps**

Two mutually exclusive setup steps. The repository has no `global.json` today, so the second one runs. When the pending restructure adds a `global.json` pinned to `10.0.204` with `rollForward: latestPatch`, the first takes over. A bare `dotnet-version: 10.0.x` would install the newest 10.0.x, currently in the 10.0.4xx band, which that pin would refuse.

`hashFiles` resolves paths against the workspace, so both conditions are only meaningful after checkout has run. Keep them in this order.

Append to the end of `.github/workflows/release.yml`, indented to match the existing `steps:` entries (6 spaces before the dash):

```yaml
      - name: Checkout
        uses: actions/checkout@v7

      - name: Set up .NET from global.json
        if: hashFiles('global.json') != ''
        uses: actions/setup-dotnet@v6
        with:
          global-json-file: global.json

      - name: Set up .NET 10
        if: hashFiles('global.json') == ''
        uses: actions/setup-dotnet@v6
        with:
          dotnet-version: 10.0.x
```

- [ ] **Step 2: Lint**

```bash
"/c/Users/AddamBoord/AppData/Local/Microsoft/WinGet/Packages/rhysd.actionlint_Microsoft.Winget.Source_8wekyb3d8bbwe/actionlint" .github/workflows/release.yml
```

Expected: no output, exit code 0.

If actionlint rejects `hashFiles` in a step-level `if`, replace both conditions with a preceding step that sets an output:

```yaml
      - name: Detect global.json
        id: sdkpin
        run: |
          set -euo pipefail
          if [[ -f global.json ]]; then
            echo "pinned=true" >> "$GITHUB_OUTPUT"
          else
            echo "pinned=false" >> "$GITHUB_OUTPUT"
          fi
```

then gate the two setup steps on `steps.sdkpin.outputs.pinned == 'true'` and `== 'false'`.

- [ ] **Step 3: Commit**

```bash
git add .github/workflows/release.yml
git commit -m "Add checkout and SDK setup to release workflow

Selects the SDK from global.json when present so the restructure's
rollForward: latestPatch pin resolves, otherwise installs .NET 10."
```

### Task 2.2: Verify the pack mechanics locally

Before writing the CI pack step, confirm the command sequence works against this solution. The mechanical risk is `dotnet pack --no-build` combined with `GeneratePackageOnBuild=true`, which the project sets for Release.

- [ ] **Step 1: Run the sequence with a probe version**

```bash
cd /c/Users/AddamBoord/source/repos/StateStore
export SCRATCH="/c/Users/ADDAMB~1/AppData/Local/Temp/claude/C--Users-AddamBoord-source-repos-StateStore-src/c815e48b-30c7-403b-a842-3369c744fc0c/scratchpad"
rm -rf "$SCRATCH/packages" && mkdir -p "$SCRATCH/packages"
VERSION=0.0.1-probe
dotnet restore StateStore.sln
dotnet build StateStore.sln -c Release --no-restore -p:Version="$VERSION" -p:ContinuousIntegrationBuild=true
shopt -s nullglob
for proj in src/*/*.csproj; do
  dotnet pack "$proj" -c Release --no-build -o "$SCRATCH/packages" -p:Version="$VERSION" -p:ContinuousIntegrationBuild=true
done
ls -l "$SCRATCH/packages"
```

Expected: exactly `StateStore.0.0.1-probe.nupkg`. No `.snupkg` yet; symbol packages arrive with the restructure, and the workflow's checks treat them as optional.

The filename carrying `0.0.1-probe` rather than `1.0.0` is the proof that a command-line `Version` overrides the value in the csproj.

- [ ] **Step 2: Confirm the glob's depth assumption**

```bash
find src -mindepth 3 -name '*.csproj' -not -path '*/bin/*' -not -path '*/obj/*'
```

Expected: no output. Every packable project sits directly under `src/`. A non-empty result means a project is nested deeper than `src/*/*.csproj` reaches and would be silently dropped from a release, which is why the CI verify step asserts this too.

- [ ] **Step 3: Clean up the probe output**

```bash
rm -rf "$SCRATCH/packages"
git status --short
```

Expected: no changes to tracked files from the probe. The build wrote into `bin/` and `obj/`, both ignored.

### Task 2.3: Add the build, test, and pack steps

**Files:**
- Modify: `.github/workflows/release.yml`

- [ ] **Step 1: Append the steps**

`$VERSION` comes from `$GITHUB_ENV`, written in Task 1.2. Restore is the one step with no `-p:Version=`, because NuGet resolution does not depend on the version being produced.

```yaml
      - name: Restore
        run: dotnet restore StateStore.sln

      - name: Build
        run: |
          set -euo pipefail
          dotnet build StateStore.sln -c Release --no-restore \
            -p:Version="$VERSION" -p:ContinuousIntegrationBuild=true

      - name: Test
        run: |
          set -euo pipefail
          dotnet test StateStore.sln -c Release --no-build \
            -p:Version="$VERSION" -p:ContinuousIntegrationBuild=true \
            --logger trx --results-directory artifacts/test-results

      - name: Upload test results
        if: failure()
        uses: actions/upload-artifact@v7
        with:
          name: test-results
          path: artifacts/test-results
          if-no-files-found: ignore

      - name: Pack
        run: |
          set -euo pipefail
          shopt -s nullglob
          projects=(src/*/*.csproj)
          if [[ ${#projects[@]} -eq 0 ]]; then
            echo "::error::No projects found under src/. Nothing to pack."
            exit 1
          fi
          {
            echo "### Packed projects"
            echo
          } >> "$GITHUB_STEP_SUMMARY"
          for proj in "${projects[@]}"; do
            echo "Packing $proj"
            echo "- \`$proj\`" >> "$GITHUB_STEP_SUMMARY"
            dotnet pack "$proj" -c Release --no-build -o artifacts/packages \
              -p:Version="$VERSION" -p:ContinuousIntegrationBuild=true
          done

      - name: Verify packages
        run: |
          set -euo pipefail
          shopt -s nullglob

          nested=$(find src -mindepth 3 -name '*.csproj' -not -path '*/bin/*' -not -path '*/obj/*')
          if [[ -n "$nested" ]]; then
            echo "::error::Project nested too deep for the src/*/*.csproj glob and excluded from this release: $nested"
            exit 1
          fi

          packages=(artifacts/packages/*.nupkg)
          if [[ ${#packages[@]} -eq 0 ]]; then
            echo "::error::No .nupkg was produced."
            exit 1
          fi

          {
            echo
            echo "### Packages"
            echo
          } >> "$GITHUB_STEP_SUMMARY"

          for pkg in artifacts/packages/*.nupkg artifacts/packages/*.snupkg; do
            case "$pkg" in
              *".$VERSION.nupkg"|*".$VERSION.snupkg") ;;
              *)
                echo "::error::Package '$(basename "$pkg")' does not carry version $VERSION."
                exit 1
                ;;
            esac
            echo "- \`$(basename "$pkg")\`" >> "$GITHUB_STEP_SUMMARY"
          done

          ls -l artifacts/packages

      - name: Upload packages
        uses: actions/upload-artifact@v7
        with:
          name: packages
          path: artifacts/packages/*
          if-no-files-found: error
```

- [ ] **Step 2: Lint**

```bash
"/c/Users/AddamBoord/AppData/Local/Microsoft/WinGet/Packages/rhysd.actionlint_Microsoft.Winget.Source_8wekyb3d8bbwe/actionlint" .github/workflows/release.yml
```

Expected: no output, exit code 0. actionlint also runs shellcheck over `run:` blocks, so quoting mistakes surface here.

- [ ] **Step 3: Re-run the tag-pattern harness**

The workflow file changed, so confirm the pattern extraction still finds exactly one `PATTERN=` line.

```bash
bash "$SCRATCH/test-tag-pattern.sh"
```

Expected: `PASS: all 18 cases behaved as specified`.

- [ ] **Step 4: Commit**

```bash
git add .github/workflows/release.yml
git commit -m "Add build, test, and pack steps to release workflow

Packs each project directly under src/ with the tag version, asserts
every package filename carries that version, and fails on a project
nested too deep for the glob rather than dropping it silently."
```

---

## Chunk 3: The release job

### Task 3.1: Add the release job

**Files:**
- Modify: `.github/workflows/release.yml`

- [ ] **Step 1: Append the job**

This is the only job with write permission. The tag reaches bash through the job's `env` mapping rather than inline interpolation, so a crafted ref name cannot inject shell. `--verify-tag` makes `gh` refuse to create a release for a tag absent from the remote.

Append at the same indentation as `build:` (2 spaces), after the `build` job's final step:

```yaml
  release:
    needs: build
    runs-on: ubuntu-latest
    permissions:
      contents: write
    env:
      GH_TOKEN: ${{ github.token }}
      GH_REPO: ${{ github.repository }}
      TAG: ${{ github.ref_name }}
      PRERELEASE: ${{ needs.build.outputs.prerelease }}
    steps:
      - name: Download packages
        uses: actions/download-artifact@v8
        with:
          name: packages
          path: artifacts/packages

      - name: Create or update release
        run: |
          set -euo pipefail
          shopt -s nullglob
          assets=(artifacts/packages/*.nupkg artifacts/packages/*.snupkg)
          if [[ ${#assets[@]} -eq 0 ]]; then
            echo "::error::No package assets to attach to release $TAG."
            exit 1
          fi
          if gh release view "$TAG" >/dev/null 2>&1; then
            echo "Release $TAG already exists. Uploading assets."
            gh release upload "$TAG" "${assets[@]}" --clobber
          else
            echo "Creating release $TAG."
            args=(--title "$TAG" --generate-notes --verify-tag)
            if [[ "$PRERELEASE" == "true" ]]; then
              args+=(--prerelease)
            fi
            gh release create "$TAG" "${assets[@]}" "${args[@]}"
          fi
```

- [ ] **Step 2: Lint**

```bash
"/c/Users/AddamBoord/AppData/Local/Microsoft/WinGet/Packages/rhysd.actionlint_Microsoft.Winget.Source_8wekyb3d8bbwe/actionlint" .github/workflows/release.yml
```

Expected: no output, exit code 0.

- [ ] **Step 3: Confirm the job graph and permissions by inspection**

```bash
grep -n -E "^  [a-z]+:|^    (needs|runs-on|permissions):|^      contents:|^    outputs:|^      prerelease:" .github/workflows/release.yml
```

Expected, in order: `build:`, its `runs-on`, `permissions`, `contents: read`, `outputs`, `prerelease`, then `release:`, `needs`, `runs-on`, `permissions`, `contents: write`.

Two things to confirm by eye: `build` is read-only, and `release` is the only writer.

- [ ] **Step 4: Commit**

```bash
git add .github/workflows/release.yml
git commit -m "Add release job to release workflow

Creates the GitHub Release for the tag with the packages attached, or
uploads over the assets of a release that already exists so a failed
run can be retried. Marks a hyphenated version as a pre-release."
```

---

## Chunk 4: End-to-end verification

These tasks push tags to the public repository `Cadtastic-Solutions/StateStore`, which briefly creates a public release. They are outward-facing and irreversible in the sense that a pushed tag is immediately visible.

**Get explicit confirmation from the user before running any task in this chunk.** Do not push a tag on your own initiative.

Both tasks end by deleting what they created.

### Task 4.1: Happy path with a throwaway pre-release tag

**Preconditions:**
- Chunks 1 through 3 are committed.
- The branch is pushed, so the tagged commit exists on the remote: `git push -u origin refactor/repo-organization`.
- GitHub Actions is enabled for the repository.

- [ ] **Step 1: Confirm the tag will point at a commit containing the workflow**

A tag-push workflow runs from the tagged commit, so the workflow file has to already be there.

```bash
cd /c/Users/AddamBoord/source/repos/StateStore
git log --oneline -1
git show --stat HEAD -- .github/workflows/release.yml | head -5
git ls-tree HEAD .github/workflows/release.yml
```

Expected: `git ls-tree` prints the blob, confirming the file is in the commit HEAD points at.

- [ ] **Step 2: Push the throwaway tag**

```bash
git tag v0.0.1-ci.1
git push origin v0.0.1-ci.1
```

- [ ] **Step 3: Watch the run**

```bash
gh run watch --repo Cadtastic-Solutions/StateStore --exit-status
```

Expected: both jobs succeed. If `gh run watch` cannot find the run, list them with `gh run list --repo Cadtastic-Solutions/StateStore --limit 5` and watch by ID.

On failure, read the logs with `gh run view <id> --log-failed` and fix the workflow before re-tagging. Delete the tag first (Step 6) so the retry starts clean.

- [ ] **Step 4: Verify the release**

```bash
gh release view v0.0.1-ci.1 --repo Cadtastic-Solutions/StateStore
```

Expected, all four:
- The release is marked **Pre-release**, because the version carries `-ci.1`.
- `StateStore.0.0.1-ci.1.nupkg` is attached.
- Release notes were generated.
- The title is `v0.0.1-ci.1`.

The version in the asset filename is the key assertion: it proves the tag drove the package version rather than the csproj's `1.0.0`.

- [ ] **Step 5: Check the run summary**

Open the run's summary page, or:

```bash
gh run view --repo Cadtastic-Solutions/StateStore --json jobs --jq '.jobs[].name'
```

Expected: the summary lists the packed project `src/StateStore/StateStore.csproj` and the package `StateStore.0.0.1-ci.1.nupkg`.

- [ ] **Step 6: Delete the release and the tag**

```bash
gh release delete v0.0.1-ci.1 --repo Cadtastic-Solutions/StateStore --yes --cleanup-tag
git tag -d v0.0.1-ci.1
git fetch --prune --prune-tags origin
git tag -l
```

Expected: `git tag -l` prints nothing. `--cleanup-tag` removes the remote tag; `git tag -d` removes the local one.

### Task 4.2: Negative path with a malformed tag

- [ ] **Step 1: Push a tag the glob accepts but the pattern rejects**

`v0.0.1.1` matches the coarse trigger glob `v[0-9]+.[0-9]+.[0-9]+*`, so the workflow starts, and then fails validation. That is exactly the case worth testing: a tag the trigger cannot filter out.

```bash
git tag v0.0.1.1
git push origin v0.0.1.1
```

- [ ] **Step 2: Confirm the run fails at validation**

```bash
gh run watch --repo Cadtastic-Solutions/StateStore --exit-status
```

Expected: the run fails. `gh run watch --exit-status` returns non-zero, which is the desired outcome here.

```bash
gh run view --repo Cadtastic-Solutions/StateStore --log-failed | grep -i "not a release tag"
```

Expected: the error message naming the tag and the expected format.

Also confirm the failure was fast and cheap: the `build` job should have failed on its first step, with `release` skipped, having never checked out or restored.

- [ ] **Step 3: Confirm no release was created**

```bash
gh release view v0.0.1.1 --repo Cadtastic-Solutions/StateStore
```

Expected: `release not found`.

- [ ] **Step 4: Delete the tag**

```bash
git push --delete origin v0.0.1.1
git tag -d v0.0.1.1
git tag -l
```

Expected: `git tag -l` prints nothing.

---

## Chunk 5: Documentation and handoff

### Task 5.1: Record the verification results in the spec

**Files:**
- Modify: `docs/superpowers/specs/2026-09-09-release-workflow-design.md`

- [ ] **Step 1: Update the status line**

Change the header's `**Status:**` line from `Design — approved in brainstorming, pending implementation plan` to:

```markdown
**Status:** Implemented — verified end-to-end on <date> with tag `v0.0.1-ci.1`
```

Use the date the Chunk 4 verification actually ran.

- [ ] **Step 2: Commit**

```bash
git add docs/superpowers/specs/2026-09-09-release-workflow-design.md
git commit -m "Mark release workflow spec implemented"
```

### Task 5.2: Mark this plan complete

**Files:**
- Modify: `docs/superpowers/plans/2026-09-09-release-workflow.md`

- [ ] **Step 1: Tick every checkbox that was completed and commit**

```bash
git add docs/superpowers/plans/2026-09-09-release-workflow.md
git commit -m "Mark release workflow plan complete"
```

If any task was skipped, leave its boxes unticked and add a one-line note saying why. An unticked box is information; a falsely ticked one is a lie to the next reader.

### Task 5.3: Warn the restructure plan about three corrections

The pending restructure plan has 165 unexecuted steps and would, as written, reintroduce two failures this workflow depends on not happening. The corrections are recorded in this workflow's spec, but whoever executes the restructure plan will not read that spec. Put the pointer where they will see it.

**Files:**
- Modify: `docs/superpowers/plans/2026-05-22-repo-restructure.md`

- [ ] **Step 1: Insert a note directly after the plan's header block**

```markdown
> **Corrections required before executing this plan.** See the Follow-ups section of
> `docs/superpowers/specs/2026-09-09-release-workflow-design.md`. In short: use
> `Microsoft.Data.Sqlite` 10.0.12 or later, not 10.0.8, and set `IsPackable=false`
> explicitly on `StateStore.Benchmarks` and every `samples/` project, because
> `OutputType=Exe` does not imply it. Each omission turns every release tag run red.
```

- [ ] **Step 2: Commit**

```bash
git add docs/superpowers/plans/2026-05-22-repo-restructure.md
git commit -m "Flag corrections the restructure plan needs before execution

Executing it as written would reintroduce NU1903 and add NU5039, both
of which break the release workflow's build step."
```

### Task 5.4: Finish the branch

- [ ] **Step 1: Confirm the working tree is clean and the branch is pushed**

```bash
cd /c/Users/AddamBoord/source/repos/StateStore
git status --short
git log --oneline origin/refactor/repo-organization..HEAD
```

Expected: no unexpected modifications, and the log shows the commits ready to push.

- [ ] **Step 2: Use the finishing-a-development-branch skill**

Invoke @superpowers:finishing-a-development-branch to choose between merging, opening a pull request, or leaving the branch in place. Do not merge to `master` without asking; the branch also carries the unrelated restructure design work.

---

## Verification summary

The plan is complete when all of these hold:

| # | Check | How |
|---|---|---|
| 1 | The tag pattern accepts and rejects exactly as specified | `bash "$SCRATCH/test-tag-pattern.sh"` prints PASS for all 18 cases |
| 2 | The workflow is valid and shellcheck-clean | `actionlint .github/workflows/release.yml` exits 0 with no output |
| 3 | A command-line version overrides the csproj value | The local probe produced `StateStore.0.0.1-probe.nupkg` |
| 4 | Every packable project is reachable by the glob | `find src -mindepth 3 -name '*.csproj'` prints nothing |
| 5 | A valid tag yields a release with the versioned package attached | `gh release view v0.0.1-ci.1` showed a pre-release with `StateStore.0.0.1-ci.1.nupkg` |
| 6 | An invalid tag fails fast and releases nothing | The run failed on step 1; `gh release view v0.0.1.1` reported not found |
| 7 | `build` cannot write to the repository | `permissions: contents: read` on `build`, `write` only on `release` |
| 8 | No test tags or releases remain | `git tag -l` prints nothing; `gh release list` shows no `v0.0.1-*` |
