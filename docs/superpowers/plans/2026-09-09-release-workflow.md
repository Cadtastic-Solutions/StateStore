# Release Workflow Implementation Plan

> **For agentic workers:** REQUIRED: Use superpowers:subagent-driven-development (if subagents available) or superpowers:executing-plans to implement this plan. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a GitHub Actions workflow that turns a pushed `vX.Y.Z` tag into a GitHub Release carrying the NuGet packages built from that commit.

**Architecture:** One workflow file, `.github/workflows/release.yml`, with two jobs. A `contents: read` job validates the tag, derives the package version from it, then builds, tests, packs every project directly under `src/`, and uploads the packages as a workflow artifact. A dependent `contents: write` job downloads that artifact and creates or updates the GitHub Release for the tag using the runner's preinstalled `gh` CLI. No secrets and no third-party actions are involved.

**Tech Stack:** GitHub Actions, bash, .NET 10 SDK (`dotnet restore/build/test/pack`), `gh` CLI, `actionlint` for static checking.

**Spec:** [docs/superpowers/specs/2026-09-09-release-workflow-design.md](../specs/2026-09-09-release-workflow-design.md)

---

## Before you start

**Working directory.** Every command block in this plan opens with the same three-line preamble:

```bash
cd /c/Users/AddamBoord/source/repos/StateStore
export SCRATCH="/c/Users/AddamBoord/AppData/Local/Temp/statestore-release-plan"
mkdir -p "$SCRATCH"
```

Do not skip it or assume it carries over. Each command block runs in a fresh shell, so neither the working directory nor `$SCRATCH` persists between steps. Some sessions open in `src/` rather than the repository root, which is why the `cd` is absolute. Nothing under `$SCRATCH` is ever committed.

**Two expected warnings.** Neither is a failure:

1. `git add` on any file prints `warning: in the working copy of '<path>', LF will be replaced by CRLF the next time Git touches it`. Git's system config sets `core.autocrlf=true`, and `.gitattributes` sets `* text=auto`, so files are stored with LF and checked out with CRLF. That is the desired arrangement: the Linux runner reads the LF copy from the repository.
2. `git status --short` shows pre-existing noise unrelated to this work, currently ` M .claude/settings.local.json` and `?? src/.claude/`. The second is a nested git worktree. Leave both alone.

**Already verified during planning.** These do not need re-checking, and a failure in any of them means a transcription error rather than a design problem:

| Fact | Evidence |
|---|---|
| The complete workflow this plan produces is 181 lines and passes actionlint 1.7.12 clean, shellcheck included | Assembled and linted during planning |
| The version step handles all 18 tag cases correctly, both accept and reject, including `prerelease=false` | Harness in Task 1.1 run against the assembled file |
| The Verify packages step behaves correctly across five cases including version mismatch and nested projects | Task 2.2 Step 4 run against a synthetic tree |
| `dotnet pack --no-build` works despite `GeneratePackageOnBuild=true` for Release | Local probe produced `StateStore.0.0.1-probe.nupkg` |
| A command-line `-p:Version=` overrides the csproj's `1.0.0` and flows into `AssemblyVersion` | Same probe |
| `actions/upload-artifact@v7` pairs with `actions/download-artifact@v8` | download-artifact v8's release notes describe supporting upload-artifact v7's direct uploads; the majors are the intended pairing, not a skew |
| `gh run watch` requires a positional run ID | `gh run watch --help` shows `USAGE: gh run watch <run-id> [flags]` |

**Out of scope.** The spec's Follow-ups list a PR-triggered `ci.yml` and a nuget.org publish job. Neither is in this plan. Task 5.3 is the one deliberate exception to that boundary, for the reason given there.

---

## File structure

| File | Responsibility | Status |
|---|---|---|
| `.github/workflows/release.yml` | The entire deliverable. Tag validation, version derivation, build, test, pack, artifact upload, release creation. 181 lines when complete. | Create (Chunks 1 to 3) |
| `docs/superpowers/specs/2026-09-09-release-workflow-design.md` | The spec. Its status line and Verification section are updated once the workflow is proven. | Modify (Task 5.1) |
| `docs/superpowers/plans/2026-09-09-release-workflow.md` | This plan. Checkboxes ticked as work completes. | Modify (Task 5.2) |
| `docs/superpowers/plans/2026-05-22-repo-restructure.md` | Unrelated pending plan. Gains a warning note plus three corrected version literals. | Modify (Task 5.3) |

The workflow is a single file because its two jobs share one trigger, one version derivation, and one artifact. Splitting it would duplicate the tag contract. Reusable-workflow extraction is not warranted for one caller.

The tag pattern is defined once, as a `PATTERN=` assignment inside the validation step. The test harness in Chunk 1 extracts and executes that step out of the YAML rather than restating any of its logic, so the test cannot drift from what ships.

---

## Chunk 1: Tag validation

The riskiest logic in the workflow is the tag contract, and it is the only part testable without pushing a tag. Build it test-first.

### Task 1.1: Write the failing version-step harness

**Files:**
- Create: `$SCRATCH/test-version-step.sh` (scratchpad, not committed)

- [ ] **Step 1: Write the harness**

This script extracts the first `run: |` block out of the workflow, which is the version step, and executes it against every tag case with `GITHUB_REF_NAME`, `GITHUB_ENV`, and `GITHUB_OUTPUT` pointed at temporary files. It asserts three things per accepted tag (exit status, derived `VERSION`, `prerelease` output) and two per rejected tag (non-zero exit, the expected error text).

Executing the real step rather than a copy of its regex is the point. A harness holding its own copy of the pattern would pass while the shipped workflow was broken, and it would leave the version derivation and the `prerelease` flag untested entirely.

```bash
cd /c/Users/AddamBoord/source/repos/StateStore
export SCRATCH="/c/Users/AddamBoord/AppData/Local/Temp/statestore-release-plan"
mkdir -p "$SCRATCH"

cat > "$SCRATCH/test-version-step.sh" <<'SH'
#!/usr/bin/env bash
# Executes the real "Resolve version from tag" step out of the shipped workflow
# against every tag case, asserting exit status, derived VERSION, and the
# prerelease output. Extracting the step means this test cannot drift from it.
set -uo pipefail

WORKFLOW=".github/workflows/release.yml"
WORK="$(mktemp -d)"
trap 'rm -rf "$WORK"' EXIT

if [[ ! -f "$WORKFLOW" ]]; then
  echo "FAIL: $WORKFLOW does not exist"
  exit 1
fi

STEP="$WORK/version-step.sh"
awk '
  /^        run: \|$/ { if (!seen) { seen=1; capture=1; next } }
  capture {
    if ($0 ~ /^          / || $0 ~ /^[[:space:]]*$/) { sub(/^          /, ""); print; next }
    capture=0
  }
' "$WORKFLOW" > "$STEP"

if ! grep -q "PATTERN=" "$STEP"; then
  echo "FAIL: the first 'run: |' block in $WORKFLOW is not the version step"
  echo "      (extraction found no PATTERN= assignment)"
  exit 1
fi

# tag|expected VERSION|expected prerelease
ACCEPT=(
  "v1.0.0|1.0.0|false"
  "v10.20.30|10.20.30|false"
  "v1.2.3-alpha.1|1.2.3-alpha.1|true"
  "v2.0.0-rc.2|2.0.0-rc.2|true"
  "v1.0.0-alpha-1|1.0.0-alpha-1|true"
  "v1.0.0-0a|1.0.0-0a|true"
  "v0.0.1-ci.1|0.0.1-ci.1|true"
)

REJECT=(
  "v1.0" "v01.0.0" "v1.0.0.1" "v0.0.1.1" "v1.0.0-"
  "v1.0.0-rc.01" "v1.0.0-00" "v1.0.0-alpha..1" "v1.0.0+build.5" "1.0.0" ""
)

failures=0

run_step() {
  : > "$WORK/env" ; : > "$WORK/out"
  GITHUB_REF_NAME="$1" GITHUB_ENV="$WORK/env" GITHUB_OUTPUT="$WORK/out" \
    bash "$STEP" > "$WORK/log" 2>&1
}

for case in "${ACCEPT[@]}"; do
  IFS='|' read -r tag want_ver want_pre <<< "$case"
  run_step "$tag"; rc=$?
  got_ver="$(sed -n 's/^VERSION=//p' "$WORK/env")"
  got_pre="$(sed -n 's/^prerelease=//p' "$WORK/out")"
  if [[ $rc -eq 0 && "$got_ver" == "$want_ver" && "$got_pre" == "$want_pre" ]]; then
    printf '  ok       accept  %-18s version=%-16s prerelease=%s\n' "$tag" "$got_ver" "$got_pre"
  else
    printf '  NOT OK   accept  %-18s rc=%s version=%s (want %s) prerelease=%s (want %s)\n' \
      "$tag" "$rc" "${got_ver:-<none>}" "$want_ver" "${got_pre:-<none>}" "$want_pre"
    failures=$((failures + 1))
  fi
done

for tag in "${REJECT[@]}"; do
  run_step "$tag"; rc=$?
  if [[ $rc -ne 0 ]] && grep -q "is not a release tag" "$WORK/log"; then
    printf '  ok       reject  %-18s exit=%s with the expected error\n' "${tag:-<empty>}" "$rc"
  else
    printf '  NOT OK   reject  %-18s exit=%s (expected non-zero + "is not a release tag")\n' \
      "${tag:-<empty>}" "$rc"
    failures=$((failures + 1))
  fi
done

echo
if [[ $failures -gt 0 ]]; then
  echo "FAIL: $failures case(s) behaved incorrectly"
  exit 1
fi
echo "PASS: all $(( ${#ACCEPT[@]} + ${#REJECT[@]} )) cases behaved as specified"
SH

chmod +x "$SCRATCH/test-version-step.sh"
wc -l "$SCRATCH/test-version-step.sh"
```

Expected: `85 /c/Users/AddamBoord/AppData/Local/Temp/statestore-release-plan/test-version-step.sh`. A different count means the heredoc was truncated.

- [ ] **Step 2: Run the harness to verify it fails**

```bash
cd /c/Users/AddamBoord/source/repos/StateStore
export SCRATCH="/c/Users/AddamBoord/AppData/Local/Temp/statestore-release-plan"
bash "$SCRATCH/test-version-step.sh"; echo "exit: $?"
```

Expected, exactly:

```
FAIL: .github/workflows/release.yml does not exist
exit: 1
```

This confirms the harness checks the real file rather than passing vacuously. If you instead see `No such file or directory` for the script itself, the preamble was skipped and `$SCRATCH` is unset.

### Task 1.2: Create the workflow with the validation step

**Files:**
- Create: `.github/workflows/release.yml`

- [ ] **Step 1: Write the workflow trigger and validation step**

The `run:` block reads the tag from `$GITHUB_REF_NAME`, an environment variable the runner sets. It deliberately does not interpolate `${{ github.ref_name }}` into the script body, which would let a crafted ref name inject shell.

`.github/workflows/` exists but is empty, and Git does not track empty directories, so `mkdir -p` is required for a fresh clone.

```bash
cd /c/Users/AddamBoord/source/repos/StateStore
mkdir -p .github/workflows

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

wc -l .github/workflows/release.yml
```

Expected: `41 .github/workflows/release.yml`.

- [ ] **Step 2: Run the harness to verify it passes**

```bash
cd /c/Users/AddamBoord/source/repos/StateStore
export SCRATCH="/c/Users/AddamBoord/AppData/Local/Temp/statestore-release-plan"
bash "$SCRATCH/test-version-step.sh"; echo "exit: $?"
```

Expected: 18 lines each beginning `  ok`, then `PASS: all 18 cases behaved as specified` and `exit: 0`.

If an accept case fails, the regex was transcribed with an error. Compare it character by character against the spec's Release-tag pattern section. Do not edit the harness to match a broken pattern.

- [ ] **Step 3: Stage the file and confirm it is stored with LF**

The working copy may hold CRLF, which is fine. What matters is the form stored in the repository, because that is what the Linux runner reads.

```bash
cd /c/Users/AddamBoord/source/repos/StateStore
git add .github/workflows/release.yml
git show :.github/workflows/release.yml | file -
```

Expected: `/dev/stdin: ASCII text`. The `git add` warning about LF being replaced by CRLF is expected and benign, as described in Before you start.

A result reading `ASCII text, with CRLF line terminators` is the failure case and would break the bash `run:` blocks on the runner. Both the harness and actionlint tolerate CRLF in the working copy, so neither will catch this for you.

### Task 1.3: Lint and commit

- [ ] **Step 1: Run actionlint**

```bash
cd /c/Users/AddamBoord/source/repos/StateStore
"/c/Users/AddamBoord/AppData/Local/Microsoft/WinGet/Packages/rhysd.actionlint_Microsoft.Winget.Source_8wekyb3d8bbwe/actionlint" .github/workflows/release.yml
echo "exit: $?"
```

Expected: no output before `exit: 0`. actionlint 1.7.12 checks YAML validity, expression syntax, context availability, and runs shellcheck over `run:` blocks.

actionlint does not validate that referenced actions or their versions exist. Those were confirmed current during planning.

- [ ] **Step 2: Commit**

```bash
cd /c/Users/AddamBoord/source/repos/StateStore
git add .github/workflows/release.yml
git commit -m "Add release workflow with tag validation

Validates a pushed v* tag against the release-tag pattern and derives
the package version from it. Build, pack, and release steps follow."
```

---

## Chunk 2: Build, test, and pack

Add the steps that produce the packages. The two riskiest ones, pack and verify, are proven locally before they are written into CI.

### Task 2.1: Add checkout and SDK setup

**Files:**
- Modify: `.github/workflows/release.yml`

- [ ] **Step 1: Append the checkout and setup steps**

Two mutually exclusive setup steps. The repository has no `global.json` today, so the second runs. When the pending restructure adds a `global.json` pinned to `10.0.204` with `rollForward: latestPatch`, the first takes over. A bare `dotnet-version: 10.0.x` installs the newest 10.0.x, currently in the 10.0.4xx band, which that pin refuses.

`hashFiles` resolves paths against the workspace, so both conditions are only meaningful after checkout. Keep this order.

```bash
cd /c/Users/AddamBoord/source/repos/StateStore

cat >> .github/workflows/release.yml <<'YML'

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
YML

grep -n '^      - name:' .github/workflows/release.yml
```

Expected, in this order:

```
      - name: Resolve version from tag
      - name: Checkout
      - name: Set up .NET from global.json
      - name: Set up .NET 10
```

Order matters and actionlint will not check it: a correctly indented step appended in the wrong place lints clean.

- [ ] **Step 2: Lint**

```bash
cd /c/Users/AddamBoord/source/repos/StateStore
"/c/Users/AddamBoord/AppData/Local/Microsoft/WinGet/Packages/rhysd.actionlint_Microsoft.Winget.Source_8wekyb3d8bbwe/actionlint" .github/workflows/release.yml
echo "exit: $?"
```

Expected: no output before `exit: 0`.

This was verified during planning, so actionlint should accept `hashFiles` in a step-level `if`. In the unlikely event it objects, insert this step immediately after Checkout and before the two setup steps:

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

then change the two setup conditions to `steps.sdkpin.outputs.pinned == 'true'` and `== 'false'`.

- [ ] **Step 3: Commit**

```bash
cd /c/Users/AddamBoord/source/repos/StateStore
git add .github/workflows/release.yml
git commit -m "Add checkout and SDK setup to release workflow

Selects the SDK from global.json when present so the restructure's
rollForward: latestPatch pin resolves, otherwise installs .NET 10."
```

### Task 2.2: Prove the pack and verify mechanics locally

Before writing the CI steps, confirm the command sequence works against this solution and that the verify logic catches what it claims to.

- [ ] **Step 1: Run the pack sequence with a probe version**

```bash
cd /c/Users/AddamBoord/source/repos/StateStore
export SCRATCH="/c/Users/AddamBoord/AppData/Local/Temp/statestore-release-plan"
mkdir -p "$SCRATCH"
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

Expected: exactly one file, `StateStore.0.0.1-probe.nupkg`. No `.snupkg` yet; symbol packages arrive with the restructure, and every check in this plan treats them as optional.

The filename carrying `0.0.1-probe` rather than `1.0.0` is the proof that a command-line `Version` overrides the csproj.

- [ ] **Step 2: Confirm the glob's depth assumption**

```bash
cd /c/Users/AddamBoord/source/repos/StateStore
find src -mindepth 3 -name '*.csproj' -not -path '*/bin/*' -not -path '*/obj/*' -not -path '*/.*/*'
echo "exit: $?"
```

Expected: no output before `exit: 0`. Every packable project sits directly under `src/`.

The `-not -path '*/.*/*'` clause is required, not cosmetic. Without it this command reports three csproj files inside the nested git worktree at `src/.claude/worktrees/`, which are not part of the build. Note that `-mindepth` is a global option in `find`, so the `-prune` idiom does not work here; path exclusion does.

- [ ] **Step 3: Write the verify logic to a script for testing**

The same text goes into the workflow in Task 2.3. Testing it here first means the most intricate bash in the chunk is not first executed against a public tag.

```bash
cd /c/Users/AddamBoord/source/repos/StateStore
export SCRATCH="/c/Users/AddamBoord/AppData/Local/Temp/statestore-release-plan"
mkdir -p "$SCRATCH"

cat > "$SCRATCH/verify.sh" <<'SH'
set -euo pipefail
shopt -s nullglob

nested=$(find src -mindepth 3 -name '*.csproj' -not -path '*/bin/*' -not -path '*/obj/*' -not -path '*/.*/*')
if [[ -n "$nested" ]]; then
  echo "::error::Projects nested too deep for the src/*/*.csproj glob and excluded from this release: $(echo "$nested" | tr '\n' ' ')"
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
SH

wc -l "$SCRATCH/verify.sh"
```

Expected: `33 /c/Users/AddamBoord/AppData/Local/Temp/statestore-release-plan/verify.sh`.

- [ ] **Step 4: Test the verify logic against five cases**

This runs in a synthetic tree under `$SCRATCH`, never against the real repository, so the nested-project cases cannot disturb your checkout.

```bash
export SCRATCH="/c/Users/AddamBoord/AppData/Local/Temp/statestore-release-plan"
T="$SCRATCH/verifytest"
rm -rf "$T" && mkdir -p "$T/artifacts/packages" "$T/src/StateStore"
cd "$T"
touch src/StateStore/StateStore.csproj artifacts/packages/StateStore.0.0.1-probe.nupkg

echo "=== case 1: matching version, no .snupkg (today's state) ==="
: > summary.md
VERSION=0.0.1-probe GITHUB_STEP_SUMMARY="$T/summary.md" bash "$SCRATCH/verify.sh"; echo "exit: $?"
cat summary.md

echo "=== case 2: version mismatch ==="
VERSION=9.9.9 GITHUB_STEP_SUMMARY="$T/summary.md" bash "$SCRATCH/verify.sh"; echo "exit: $?"

echo "=== case 3: no packages ==="
rm artifacts/packages/*.nupkg
VERSION=0.0.1-probe GITHUB_STEP_SUMMARY="$T/summary.md" bash "$SCRATCH/verify.sh"; echo "exit: $?"

echo "=== case 4: nested projects ==="
touch artifacts/packages/StateStore.0.0.1-probe.nupkg
mkdir -p src/Providers/Foo src/Providers/Bar
touch src/Providers/Foo/Foo.csproj src/Providers/Bar/Bar.csproj
VERSION=0.0.1-probe GITHUB_STEP_SUMMARY="$T/summary.md" bash "$SCRATCH/verify.sh"; echo "exit: $?"

echo "=== case 5: hidden dir must not trip the nested check ==="
rm -rf src/Providers
mkdir -p src/.claude/worktrees/wt/src/StateStore
touch src/.claude/worktrees/wt/src/StateStore/StateStore.csproj
VERSION=0.0.1-probe GITHUB_STEP_SUMMARY="$T/summary.md" bash "$SCRATCH/verify.sh"; echo "exit: $?"
```

Expected:

| Case | Expected |
|---|---|
| 1 | `exit: 0`, and the summary contains `` - `StateStore.0.0.1-probe.nupkg` `` |
| 2 | `::error::Package 'StateStore.0.0.1-probe.nupkg' does not carry version 9.9.9.` then `exit: 1` |
| 3 | `::error::No .nupkg was produced.` then `exit: 1` |
| 4 | One `::error::` naming both `Bar.csproj` and `Foo.csproj` on a single line, then `exit: 1` |
| 5 | `exit: 0` |

Case 4 proving the message is one line matters: GitHub annotations do not accept embedded newlines, so a multi-line value would surface only the first path.

- [ ] **Step 5: Clean up the probe output**

`GeneratePackageOnBuild` also left a probe-versioned package in the project's own output directory.

```bash
cd /c/Users/AddamBoord/source/repos/StateStore
export SCRATCH="/c/Users/AddamBoord/AppData/Local/Temp/statestore-release-plan"
rm -rf "$SCRATCH/packages" "$SCRATCH/verifytest"
rm -f src/StateStore/bin/Release/*.nupkg src/StateStore/bin/Release/*.snupkg
git status --short
```

Expected: only the pre-existing noise described in Before you start. Nothing new, and nothing under `src/StateStore` or `artifacts/`. Both `bin/` and `artifacts/` are gitignored.

### Task 2.3: Add the build, test, and pack steps

**Files:**
- Modify: `.github/workflows/release.yml`

- [ ] **Step 1: Append the steps**

`$VERSION` comes from `$GITHUB_ENV`, written in Task 1.2. Restore is the one step with no `-p:Version=`, because NuGet resolution does not depend on the version being produced.

The verify block is the text you tested in Task 2.2, unchanged.

```bash
cd /c/Users/AddamBoord/source/repos/StateStore

cat >> .github/workflows/release.yml <<'YML'

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
            dotnet pack "$proj" -c Release --no-build -o artifacts/packages \
              -p:Version="$VERSION" -p:ContinuousIntegrationBuild=true
            echo "- \`$proj\`" >> "$GITHUB_STEP_SUMMARY"
          done

      - name: Verify packages
        run: |
          set -euo pipefail
          shopt -s nullglob

          nested=$(find src -mindepth 3 -name '*.csproj' -not -path '*/bin/*' -not -path '*/obj/*' -not -path '*/.*/*')
          if [[ -n "$nested" ]]; then
            echo "::error::Projects nested too deep for the src/*/*.csproj glob and excluded from this release: $(echo "$nested" | tr '\n' ' ')"
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
YML

grep -n '^      - name:' .github/workflows/release.yml
```

Expected, in this order:

```
      - name: Resolve version from tag
      - name: Checkout
      - name: Set up .NET from global.json
      - name: Set up .NET 10
      - name: Restore
      - name: Build
      - name: Test
      - name: Upload test results
      - name: Pack
      - name: Verify packages
      - name: Upload packages
```

The summary line for each project is written after its `dotnet pack` succeeds, so a failed pack cannot leave a summary claiming the project was packed.

- [ ] **Step 2: Lint**

```bash
cd /c/Users/AddamBoord/source/repos/StateStore
"/c/Users/AddamBoord/AppData/Local/Microsoft/WinGet/Packages/rhysd.actionlint_Microsoft.Winget.Source_8wekyb3d8bbwe/actionlint" .github/workflows/release.yml
echo "exit: $?"
```

Expected: no output before `exit: 0`. shellcheck runs over every `run:` block here, so quoting mistakes surface now.

- [ ] **Step 3: Re-run the version-step harness**

The file changed, so confirm the extraction still lands on the version step. The harness fails loudly if the first `run: |` block is no longer the one holding `PATTERN=`.

```bash
cd /c/Users/AddamBoord/source/repos/StateStore
export SCRATCH="/c/Users/AddamBoord/AppData/Local/Temp/statestore-release-plan"
bash "$SCRATCH/test-version-step.sh"; echo "exit: $?"
```

Expected: `PASS: all 18 cases behaved as specified` and `exit: 0`.

- [ ] **Step 4: Commit**

```bash
cd /c/Users/AddamBoord/source/repos/StateStore
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

```bash
cd /c/Users/AddamBoord/source/repos/StateStore

cat >> .github/workflows/release.yml <<'YML'

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
YML

wc -l .github/workflows/release.yml
```

Expected: `181 .github/workflows/release.yml`. This number is the whole-file checksum for this plan: a different count means a transcription error somewhere in Chunks 1 to 3, not a design problem.

- [ ] **Step 2: Lint**

```bash
cd /c/Users/AddamBoord/source/repos/StateStore
"/c/Users/AddamBoord/AppData/Local/Microsoft/WinGet/Packages/rhysd.actionlint_Microsoft.Winget.Source_8wekyb3d8bbwe/actionlint" .github/workflows/release.yml
echo "exit: $?"
```

Expected: no output before `exit: 0`.

- [ ] **Step 3: Confirm the job graph and permissions**

```bash
cd /c/Users/AddamBoord/source/repos/StateStore
grep -n -E "^  (build|release):|^    (needs|runs-on|outputs|permissions):|^      contents: (read|write)" .github/workflows/release.yml
```

Expected, in this order:

```
  build:
    runs-on: ubuntu-latest
    permissions:
      contents: read
    outputs:
  release:
    needs: build
    runs-on: ubuntu-latest
    permissions:
      contents: write
```

Two things to confirm by eye: `build` is read-only, and `release` is the only writer.

- [ ] **Step 4: Re-confirm the stored line endings**

The file has been appended to three times since Task 1.2 Step 3. Check the whole assembled file once.

```bash
cd /c/Users/AddamBoord/source/repos/StateStore
git add .github/workflows/release.yml
git show :.github/workflows/release.yml | file -
```

Expected: `/dev/stdin: ASCII text`, with no CRLF qualifier.

- [ ] **Step 5: Commit**

```bash
cd /c/Users/AddamBoord/source/repos/StateStore
git add .github/workflows/release.yml
git commit -m "Add release job to release workflow

Creates the GitHub Release for the tag with the packages attached, or
uploads over the assets of a release that already exists so a failed
run can be retried. Marks a hyphenated version as a pre-release."
```

---

## Chunk 4: End-to-end verification

**This chunk is gated. Get explicit user confirmation before running any step in it, and do not push a branch or a tag on your own initiative.**

Every step here touches the public repository `Cadtastic-Solutions/StateStore`. Task 4.0 publishes a branch for the first time. Task 4.1 creates a public pre-release, briefly. Task 4.2 creates a failing workflow run. A pushed tag is visible the moment it lands.

**If execution stops for any reason after a tag is pushed, run that task's cleanup step before finishing.** Cleanup is the last step of each task, but it is not optional and it is not only for the success path.

**What cleanup does not remove.** Both workflow runs stay in the repository's Actions tab, and the successful run's `packages` artifact persists for the default 90-day retention. Neither is a release and neither is misleading, so this plan leaves them. Add `gh run delete <run-id>` to the cleanup steps if you want the tab clean.

**If the user declines this chunk,** skip to Chunk 5 and use the alternate wording in Task 5.1 Step 1. Do not claim end-to-end verification that did not happen.

### Task 4.0: Preconditions

- [ ] **Step 1: Confirm the gh CLI is authenticated as the right account**

Two accounts may be logged in. Every step in this chunk depends on the active one having write access to the org repository.

```bash
cd /c/Users/AddamBoord/source/repos/StateStore
gh auth status
git remote -v
```

Expected: `Active account: true` under `github.com` for an account with push rights to `Cadtastic-Solutions/StateStore`, and `origin` pointing at `https://github.com/Cadtastic-Solutions/StateStore.git`.

- [ ] **Step 2: Confirm immutable releases are not enabled**

If the repository or organization enforces immutable releases, a published release cannot be deleted. That would leave the throwaway `v0.0.1-ci.1` release on a public repository permanently, turning this chunk from briefly-public into irreversible. It would also break the re-run path the workflow's create-or-update design depends on.

```bash
export MSYS_NO_PATHCONV=1
gh api repos/Cadtastic-Solutions/StateStore --jq '{immutable_releases: .immutable_releases}'
```

Expected: `false`, or the field absent, which means the setting is not in force.

If it reports `true`, **stop and tell the user.** Do not proceed with Task 4.1; there is no cleanup path.

- [ ] **Step 3: Confirm the workflow is in the commit that will be tagged**

A tag-push workflow runs from the tagged commit, so the file has to already be there.

```bash
cd /c/Users/AddamBoord/source/repos/StateStore
git log --oneline -1
git ls-tree HEAD .github/workflows/release.yml
```

Expected: `git ls-tree` prints a blob line ending in `.github/workflows/release.yml`. Empty output means Chunks 1 to 3 are not committed.

- [ ] **Step 4: Publish the branch**

The branch has no upstream today, so this is its first publication to the public repository. It carries the unrelated restructure design work as well as this workflow.

Pushing the tag in Task 4.1 would carry the commit to the remote regardless, and a tag push triggers the workflow whether or not the tag is reachable from a branch. The branch is pushed anyway for two reasons: after `--cleanup-tag` the commits would otherwise become unreferenced on the remote and eligible for garbage collection, which breaks the run's commit links, and `--generate-notes` produces more predictable output for a tag reachable from a branch.

```bash
cd /c/Users/AddamBoord/source/repos/StateStore
git push -u origin refactor/repo-organization
```

Expected: a `new branch` line and `branch 'refactor/repo-organization' set up to track 'origin/refactor/repo-organization'`.

If the push is rejected, stop and report why. Do not force.

### Task 4.1: Happy path with a throwaway pre-release tag

- [ ] **Step 1: Push the throwaway tag**

```bash
cd /c/Users/AddamBoord/source/repos/StateStore
git tag v0.0.1-ci.1
git push origin v0.0.1-ci.1
```

Expected: a line reading `* [new tag]         v0.0.1-ci.1 -> v0.0.1-ci.1`.

If the push is rejected, stop and report why rather than retrying with force.

- [ ] **Step 2: Watch the run**

`gh run watch` takes the run ID as a required positional argument, so capture it first. There is a short registration delay after a tag push, and the list can briefly return an older run, so check that the captured run is for this tag.

```bash
cd /c/Users/AddamBoord/source/repos/StateStore
export MSYS_NO_PATHCONV=1
REPO=Cadtastic-Solutions/StateStore
gh run list --repo "$REPO" --limit 3 --json databaseId,headBranch,workflowName,status
RUN_ID=$(gh run list --repo "$REPO" --limit 1 --json databaseId --jq '.[0].databaseId')
echo "RUN_ID=$RUN_ID"
gh run watch "$RUN_ID" --repo "$REPO" --exit-status; echo "exit: $?"
```

Expected: the list shows a run with `headBranch` of `v0.0.1-ci.1`, and the watch ends with `exit: 0` after both jobs succeed.

If the list does not yet show the tag's run, wait a few seconds and re-run the two `gh run list` commands. If `RUN_ID` picked up an older run, take the correct `databaseId` from the list output and set it by hand.

On failure, read the logs and fix the workflow before retrying:

```bash
gh run view "$RUN_ID" --repo "$REPO" --log-failed
```

Three retry options, cheapest first. If only the `release` job failed, `gh run rerun "$RUN_ID" --repo "$REPO" --failed` reruns it against the stored artifact and exercises the create-or-update path. If the workflow file itself needs a fix, commit it and push a fresh tag such as `v0.0.1-ci.2`, which the pattern accepts, avoiding any delete-and-re-tag dance. Only if you want to reuse the same tag, run Step 5 first, then start again from Step 1.

- [ ] **Step 3: Verify the release**

```bash
export MSYS_NO_PATHCONV=1
gh release view v0.0.1-ci.1 --repo Cadtastic-Solutions/StateStore
```

Expected, all four:
- The release is marked **Pre-release**, because the version carries `-ci.1`.
- `StateStore.0.0.1-ci.1.nupkg` is attached.
- Release notes were generated.
- The title is `v0.0.1-ci.1`.

The version in the asset filename is the key assertion: it proves the tag drove the package version rather than the csproj's `1.0.0`.

- [ ] **Step 4: Verify the run summary content**

The step summary is not exposed by the API, so confirm the same facts from the logs. Opening the run's summary page in a browser with `gh run view "$RUN_ID" --repo "$REPO" --web` shows the rendered version if you prefer.

```bash
export MSYS_NO_PATHCONV=1
REPO=Cadtastic-Solutions/StateStore
RUN_ID=$(gh run list --repo "$REPO" --limit 1 --json databaseId --jq '.[0].databaseId')
gh run view "$RUN_ID" --repo "$REPO" --log | grep -E "Packing src/|StateStore\.0\.0\.1-ci\.1\.nupkg"
```

Expected: at least a line containing `Packing src/StateStore/StateStore.csproj` and a line naming `StateStore.0.0.1-ci.1.nupkg`.

- [ ] **Step 5: Delete the release and the tag**

The delete path depends on whether a release exists. If the run failed in `build`, it does not, and `gh release delete` would exit non-zero on a release that was never created.

```bash
cd /c/Users/AddamBoord/source/repos/StateStore
export MSYS_NO_PATHCONV=1
REPO=Cadtastic-Solutions/StateStore
TAG=v0.0.1-ci.1
if gh release view "$TAG" --repo "$REPO" >/dev/null 2>&1; then
  gh release delete "$TAG" --repo "$REPO" --yes --cleanup-tag
else
  echo "No release for $TAG; deleting the remote tag directly."
  git push --delete origin "$TAG"
fi
git tag -d "$TAG" 2>/dev/null || echo "no local tag to delete"
git fetch --prune --prune-tags origin
git tag -l
echo "--- remote tags ---"
git ls-remote --tags origin
```

Expected: `git tag -l` prints nothing, and `git ls-remote --tags origin` prints nothing.

Order matters. The remote delete comes first, then the local delete, then the prune. Pruning before the local delete would let the fetch re-create the local tag. Note that `git tag -l` printing nothing is a valid assertion only because this repository has no other tags, and `--prune-tags` would delete any local-only tag if it ever did.

### Task 4.2: Negative path with a malformed tag

**Preconditions:** Task 4.0 must have been completed. If you are running this task without Task 4.1, its Steps 1 to 4 still apply.

- [ ] **Step 1: Push a tag the glob accepts but the pattern rejects**

`v0.0.1.1` matches the coarse trigger glob `v[0-9]+.[0-9]+.[0-9]+*`, so the workflow starts and then fails validation. That is exactly the case worth testing: a malformed tag the trigger cannot filter out.

```bash
cd /c/Users/AddamBoord/source/repos/StateStore
git tag v0.0.1.1
git push origin v0.0.1.1
```

Expected: `* [new tag]         v0.0.1.1 -> v0.0.1.1`.

- [ ] **Step 2: Confirm the run fails at validation**

```bash
cd /c/Users/AddamBoord/source/repos/StateStore
export MSYS_NO_PATHCONV=1
REPO=Cadtastic-Solutions/StateStore
RUN_ID=$(gh run list --repo "$REPO" --limit 1 --json databaseId --jq '.[0].databaseId')
echo "RUN_ID=$RUN_ID"
gh run watch "$RUN_ID" --repo "$REPO" --exit-status; echo "watch exit: $? (non-zero is the expected outcome here)"
gh run view "$RUN_ID" --repo "$REPO" --log-failed | grep -i "not a release tag"
gh run view "$RUN_ID" --repo "$REPO" --json jobs --jq '.jobs[] | {name, conclusion}'
```

Expected:
- The watch exits non-zero, which is success for this test.
- The grep prints the error naming the tag and the expected format.
- The job listing shows `build` with conclusion `failure` and `release` with conclusion `skipped`.

The `release` job being skipped rather than failed is the assertion that matters: the failure was contained to validation and nothing was ever checked out, restored, or published.

- [ ] **Step 3: Confirm no release was created**

```bash
export MSYS_NO_PATHCONV=1
gh release view v0.0.1.1 --repo Cadtastic-Solutions/StateStore; echo "exit: $?"
```

Expected: `release not found` and a non-zero exit.

If a release does exist, the pattern was mis-transcribed and accepted the tag. In that case use `gh release delete v0.0.1.1 --repo Cadtastic-Solutions/StateStore --yes --cleanup-tag` in place of Step 4, then fix the pattern and re-run Chunk 1's harness. Deleting only the tag would orphan the release.

- [ ] **Step 4: Delete the tag**

```bash
cd /c/Users/AddamBoord/source/repos/StateStore
git push --delete origin v0.0.1.1
git tag -d v0.0.1.1
git fetch --prune --prune-tags origin
git tag -l
git ls-remote --tags origin
```

Expected: both listings print nothing.

---

## Chunk 5: Documentation and handoff

### Task 5.1: Record the outcome in the spec

**Files:**
- Modify: `docs/superpowers/specs/2026-09-09-release-workflow-design.md`

- [ ] **Step 1: Update the status line**

Two cases. Pick the one that matches what actually happened.

If Chunk 4 ran and passed, replace the header's `**Status:**` line with, using an ISO date:

```markdown
**Status:** Implemented — verified end-to-end on 2026-09-09 with tag `v0.0.1-ci.1`
```

If Chunk 4 was declined or skipped, use instead:

```markdown
**Status:** Implemented — statically verified (actionlint, tag-pattern harness, local pack probe). Not yet exercised by a real tag push.
```

- [ ] **Step 2: Update the spec's Verification section**

Its preamble reads "To be carried out by the implementation plan," which goes stale the moment the status flips. Replace that line with a note saying the checks were carried out by `docs/superpowers/plans/2026-09-09-release-workflow.md`, and mark any step Chunk 4 did not run as not performed.

- [ ] **Step 3: Verify and commit**

```bash
cd /c/Users/AddamBoord/source/repos/StateStore
grep -n "^\*\*Status:\*\*" docs/superpowers/specs/2026-09-09-release-workflow-design.md
grep -n "To be carried out by the implementation plan" docs/superpowers/specs/2026-09-09-release-workflow-design.md
```

Expected: the status line shows your new wording, and the second grep prints nothing.

```bash
git add docs/superpowers/specs/2026-09-09-release-workflow-design.md
git commit -m "Record release workflow verification results in the spec"
```

### Task 5.2: Correct the restructure plan

The pending restructure plan has 165 unexecuted steps and would, as written, reintroduce two failures this workflow depends on not happening. The spec records these under Follow-ups, but whoever executes that plan will not read this spec.

A note alone is not enough. `Microsoft.Data.Sqlite` at `10.0.8` appears as copy-paste csproj content that an executor will transcribe verbatim, so correct the literals as well as adding the pointer.

**Files:**
- Modify: `docs/superpowers/plans/2026-05-22-repo-restructure.md`

- [ ] **Step 1: Correct the three Sqlite version literals**

Only the `Microsoft.Data.Sqlite` references change. The `Microsoft.Extensions.*` packages at `10.0.8` are unaffected by the advisory and stay as they are.

```bash
cd /c/Users/AddamBoord/source/repos/StateStore
grep -n 'Microsoft\.Data\.Sqlite.*10\.0\.8' docs/superpowers/plans/2026-05-22-repo-restructure.md
```

Expected: exactly four lines, at 9 (the Tech Stack summary), 381, 1529, and 1667. Note the version in the pattern: a bare `Microsoft.Data.Sqlite` grep matches eleven lines, and the other seven are prose that needs no change.

Edit each of those four to read `10.0.12` instead of `10.0.8`, then confirm:

```bash
grep -n 'Microsoft\.Data\.Sqlite.*10\.0\.\(8\|12\)' docs/superpowers/plans/2026-05-22-repo-restructure.md
```

Expected: the same four line numbers, now all showing `10.0.12`, and none showing `10.0.8`.

- [ ] **Step 2: Insert the warning note**

The header block ends with the `**Spec:**` line at line 11, followed by a blank line and a `---` rule at line 13. Insert the note between them, so it sits directly under the header and above the File map. Do this after Step 1, whose edits do not shift any line numbers.

```markdown
> **Corrections required before executing this plan.** See the Follow-ups section of
> `docs/superpowers/specs/2026-09-09-release-workflow-design.md`. Three items:
> (1) `Microsoft.Data.Sqlite` must be 10.0.12 or later, never 10.0.8, or `NU1903`
> returns and fails every build. The four literals in this plan were corrected on
> 2026-09-09; check any you add. (2) Set `IsPackable=false` explicitly on
> `StateStore.Benchmarks` and on every `samples/` project. Task 1.5's shared
> `Directory.Build.props` sets `PackageReadmeFile` for all Release projects, and one
> without a `PACKAGE.md` fails with `NU5039`. `OutputType=Exe` does not make a
> project non-packable, and the `IsPackable != 'false'` guard does not help because
> `Directory.Build.props` is imported before that property is set. (3) This plan's
> spec lists CI as a non-goal; that is superseded by the release workflow spec above.
```

- [ ] **Step 3: Verify and commit**

```bash
cd /c/Users/AddamBoord/source/repos/StateStore
sed -n '9,28p' docs/superpowers/plans/2026-05-22-repo-restructure.md
grep -c '10\.0\.8' docs/superpowers/plans/2026-05-22-repo-restructure.md
```

Expected: the note appears after the `**Spec:**` line and before the `---`, and the `10.0.8` count has dropped from 11 to 7. The seven that remain are `Microsoft.Extensions.*` references, which the advisory does not affect and which stay as they are.

```bash
git add docs/superpowers/plans/2026-05-22-repo-restructure.md
git commit -m "Correct Sqlite version and flag IsPackable trap in restructure plan

Executing it as written would reintroduce NU1903 and add NU5039, both
of which break the release workflow's build step."
```

### Task 5.3: Mark this plan complete

**Files:**
- Modify: `docs/superpowers/plans/2026-09-09-release-workflow.md`

- [ ] **Step 1: Tick the completed checkboxes**

Go through this plan and change `- [ ]` to `- [x]` for every step actually performed. Leave the rest unticked and add a one-line note under each saying why it was skipped. An unticked box is information; a falsely ticked one is a lie to the next reader.

If Chunk 4 was declined, its boxes stay empty with a note pointing at Task 5.1's alternate status wording.

Task 5.4's own boxes will still be open at this point, since it runs after this task. Note that in the commit message rather than pre-ticking them.

- [ ] **Step 2: Verify and commit**

```bash
cd /c/Users/AddamBoord/source/repos/StateStore
echo "unticked: $(grep -c '^- \[ \]' docs/superpowers/plans/2026-09-09-release-workflow.md)"
echo "ticked:   $(grep -c '^- \[x\]' docs/superpowers/plans/2026-09-09-release-workflow.md)"
```

Expected: the ticked count matches the number of steps you performed, and every remaining unticked step has a note explaining why.

```bash
git add docs/superpowers/plans/2026-09-09-release-workflow.md
git commit -m "Mark release workflow plan complete

Task 5.4's steps remain open; they are performed by the branch-finishing
skill after this commit."
```

### Task 5.4: Finish the branch

- [ ] **Step 1: Confirm the working tree is clean and see what is unpushed**

```bash
cd /c/Users/AddamBoord/source/repos/StateStore
git status --short
if git rev-parse --abbrev-ref --symbolic-full-name '@{u}' >/dev/null 2>&1; then
  git log --oneline '@{u}..HEAD'
else
  echo "no upstream configured; all commits on this branch are unpushed"
  git log --oneline master..HEAD
fi
```

Expected: only the pre-existing noise from Before you start, and a log listing the commits made by this plan. The conditional matters because Task 4.0 Step 4 sets the upstream, and it will not exist if Chunk 4 was declined.

- [ ] **Step 2: Hand off**

Invoke @superpowers:finishing-a-development-branch to choose between merging, opening a pull request, or leaving the branch in place.

Do not merge to `master` without asking. This branch also carries the unrelated restructure design and plan work.

---

## Verification summary

The plan is complete when all of these hold. Rows 5 to 7 depend on Chunk 4 and do not apply if it was declined.

| # | Check | How | Gated |
|---|---|---|---|
| 1 | The version step accepts and rejects exactly as specified, and derives the right version and pre-release flag | `bash "$SCRATCH/test-version-step.sh"` prints PASS for all 18 cases | No |
| 2 | The workflow is valid and shellcheck-clean | actionlint exits 0 with no output | No |
| 3 | The assembled file matches this plan | `wc -l .github/workflows/release.yml` reports 181 | No |
| 4 | The verify logic catches mismatched versions, missing packages, and nested projects | Task 2.2 Step 4's five cases behave as tabulated | No |
| 5 | A command-line version overrides the csproj value | The local probe produced `StateStore.0.0.1-probe.nupkg` | No |
| 6 | Every packable project is reachable by the glob | `find src -mindepth 3 -name '*.csproj' -not -path '*/bin/*' -not -path '*/obj/*' -not -path '*/.*/*'` prints nothing | No |
| 7 | `build` cannot write to the repository | `permissions: contents: read` on `build`, `write` only on `release` | No |
| 8 | A valid tag yields a release with the versioned package attached | `gh release view v0.0.1-ci.1` showed a pre-release with `StateStore.0.0.1-ci.1.nupkg` | Yes |
| 9 | An invalid tag fails fast and releases nothing | `build` failed at its first step, `release` was skipped, `gh release view v0.0.1.1` reported not found | Yes |
| 10 | No test tags or releases remain | `git tag -l` and `git ls-remote --tags origin` both print nothing | Yes |
