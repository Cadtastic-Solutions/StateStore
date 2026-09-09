# Release Workflow Implementation Plan

> **For agentic workers:** REQUIRED: Use superpowers:subagent-driven-development (if subagents available) or superpowers:executing-plans to implement this plan. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a GitHub Actions workflow that turns a pushed `vX.Y.Z` tag into a GitHub Release carrying the NuGet packages built from that commit.

**Architecture:** One workflow file, `.github/workflows/release.yml`, with two jobs. A `contents: read` job validates the tag, derives the package version from it, then builds, tests, packs every project directly under `src/`, and uploads the packages as a workflow artifact. A dependent `contents: write` job downloads that artifact and creates or updates the GitHub Release for the tag using the runner's preinstalled `gh` CLI. No secrets and no third-party actions are involved.

**Tech Stack:** GitHub Actions, bash, .NET 10 SDK (`dotnet restore/build/test/pack`), `gh` CLI, `actionlint` for static checking.

**Spec:** [docs/superpowers/specs/2026-09-09-release-workflow-design.md](../specs/2026-09-09-release-workflow-design.md)

---

## Before you start

**Working directory.** Each command block runs in a fresh shell. Neither the working directory nor any exported variable survives from the previous step, so **every block re-establishes exactly the context it needs.** Run each block as written and do not add or remove setup lines. Most blocks begin with:

```bash
cd /c/Users/AddamBoord/source/repos/StateStore
export SCRATCH="/c/Users/AddamBoord/AppData/Local/Temp/statestore-release-plan"
mkdir -p "$SCRATCH"
```

Some sessions open in `src/` rather than the repository root, which is why the `cd` is absolute. Nothing under `$SCRATCH` is ever committed.

One block deliberately does not `cd` to the repository: Task 2.2 Step 4 runs inside a synthetic tree under `$SCRATCH`, and adding a `cd` to the repository root there would make its test cases create directories inside the real checkout. It is labelled where it appears.

**Two expected warnings.** Neither is a failure:

1. `git add` prints `warning: in the working copy of '<path>', LF will be replaced by CRLF the next time Git touches it` whenever it actually updates the index entry. Git's system config sets `core.autocrlf=true`, and `.gitattributes` sets `* text=auto`, so files are stored with LF and checked out with CRLF. That is the desired arrangement: the Linux runner reads the LF copy from the repository. Re-staging an already-staged identical file prints nothing, so the warning's absence is not a signal either way.
2. `git status --short` shows pre-existing noise unrelated to this work, currently ` M .claude/settings.local.json` and `?? src/.claude/`. The second is a nested git worktree. Leave both alone.

**Linting needs shellcheck on the PATH.** actionlint runs shellcheck over every `run:` block, but only if it can find the binary; otherwise it silently disables that rule and still exits 0. Every actionlint command in this plan therefore prepends the winget install location of shellcheck 0.11.0 to `PATH`. If you see `Rule "shellcheck" was disabled` in `actionlint -verbose` output, the bash is not being checked. Install with `winget install koalaman.shellcheck` if the path does not exist.

**No unbalanced apostrophes inside heredocs.** A lone apostrophe in prose inside a `cat <<'YML'` or `cat <<'SH'` block, such as a contraction or a possessive, breaks the command transport of the tooling that runs these blocks, even though bash itself would accept it. Balanced single quotes used as shell quoting, such as `'$TAG'` or `'*.csproj'`, are fine and appear throughout. The workflow comments and harness comments avoid contractions on purpose. Keep it that way when editing.

**Already verified during planning.** These do not need re-checking, and a failure in any of them means a transcription error rather than a design problem:

| Fact | Evidence |
|---|---|
| The complete workflow this plan produces is 237 lines and passes actionlint 1.7.12 with shellcheck 0.11.0 active, zero findings | Assembled and linted during planning, re-measured after each review amendment |
| The version step handles all 18 tag cases correctly, both accept and reject, including `prerelease=false` | Harness in Task 1.1 run against the assembled file |
| The Verify packages step behaves correctly across six cases including version mismatch, nested projects, and a project that produced no package | Task 2.2 Step 4 run against a synthetic tree |
| `dotnet pack --no-build` works despite `GeneratePackageOnBuild=true` for Release | Local probe produced `StateStore.0.0.1-probe.nupkg` |
| A command-line `-p:Version=` overrides the csproj's `1.0.0` and flows into `AssemblyVersion` | Same probe |
| `actions/upload-artifact@v7` pairs with `actions/download-artifact@v8` | download-artifact v8's release notes describe supporting upload-artifact v7's direct uploads; the majors are the intended pairing, not a skew |
| `gh run watch` requires a positional run ID | `gh run watch --help` shows `USAGE: gh run watch <run-id> [flags]` |

**Out of scope.** The spec's Follow-ups list a PR-triggered `ci.yml` and a nuget.org publish job. Neither is in this plan. Task 5.2, which corrects a separate pending plan, is the one deliberate exception to that boundary, for the reason given there.

---

## File structure

| File | Responsibility | Status |
|---|---|---|
| `.github/workflows/release.yml` | The entire deliverable. Tag validation, version derivation, build, test, pack, artifact upload, release creation. 237 lines when complete. | Create (Chunks 1 to 3) |
| `docs/superpowers/specs/2026-09-09-release-workflow-design.md` | The spec for this work. Its status line, Verification section, and one stale command are updated once the workflow is proven. | Modify (Task 5.1) |
| `docs/superpowers/plans/2026-05-22-repo-restructure.md` | Unrelated pending plan. Gains a warning note plus four corrected version literals. | Modify (Task 5.2) |
| `docs/superpowers/specs/2026-05-22-repo-restructure-design.md` | That plan's spec. One stale version literal corrected. | Modify (Task 5.2) |
| `docs/superpowers/plans/2026-09-09-release-workflow.md` | This plan. Checkboxes ticked as work completes. | Modify (Task 5.3) |

The workflow is a single file because its two jobs share one trigger, one version derivation, and one artifact. Splitting it would duplicate the tag contract. Reusable-workflow extraction is not warranted for one caller.

The tag pattern is defined once, as a `PATTERN=` assignment inside the validation step. The test harness in Chunk 1 extracts and executes that step out of the YAML rather than restating any of its logic, so the test cannot drift from what ships.

---

## Chunk 1: Tag validation

The riskiest logic in the workflow is the tag contract, and it is the only part testable without pushing a tag. Build it test-first.

### Task 1.1: Write the failing version-step harness

**Files:**
- Create: `$SCRATCH/test-version-step.sh` (scratchpad, not committed)

- [x] **Step 1: Write the harness**

This script extracts the first `run: |` block out of the workflow, which is the version step, and executes it against every tag case with `GITHUB_REF_NAME`, `GITHUB_ENV`, and `GITHUB_OUTPUT` pointed at temporary files. It asserts three things per accepted tag (exit status, derived `RELEASE_VERSION`, `prerelease` output) and two per rejected tag (non-zero exit, the expected error text).

Executing the real step rather than a copy of its regex is the point. A harness holding its own copy of the pattern would pass while the shipped workflow was broken, and it would leave the version derivation and the `prerelease` flag untested entirely.

```bash
cd /c/Users/AddamBoord/source/repos/StateStore
export SCRATCH="/c/Users/AddamBoord/AppData/Local/Temp/statestore-release-plan"
mkdir -p "$SCRATCH"

cat > "$SCRATCH/test-version-step.sh" <<'SH'
#!/usr/bin/env bash
# Executes the real "Resolve version from tag" step out of the shipped workflow
# against every tag case, asserting exit status, derived RELEASE_VERSION, and the
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

# tag|expected RELEASE_VERSION|expected prerelease
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
  got_ver="$(sed -n 's/^RELEASE_VERSION=//p' "$WORK/env")"
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

- [x] **Step 2: Run the harness to verify it fails**

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

- [x] **Step 1: Write the workflow trigger and validation step**

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

# No default grants. Each job declares exactly the permissions it needs.
permissions: {}

concurrency:
  group: release-${{ github.ref }}
  # Never kill an in-flight release partway through uploading assets.
  cancel-in-progress: false

defaults:
  run:
    shell: bash

env:
  DOTNET_NOLOGO: true
  DOTNET_CLI_TELEMETRY_OPTOUT: true

jobs:
  build:
    runs-on: ubuntu-latest
    timeout-minutes: 15
    permissions:
      contents: read
    outputs:
      prerelease: ${{ steps.version.outputs.prerelease }}
    steps:
      - name: Resolve version from tag
        id: version
        run: |
          set -euo pipefail
          # Read the tag from the runner environment rather than interpolating the
          # github.ref_name expression into this script, so a crafted ref cannot inject shell.
          TAG="$GITHUB_REF_NAME"
          # SemVer 2 without build metadata. NuGet strips a +suffix from the package
          # filename, which would break the version check in the Verify step, and its
          # strict parser rejects leading zeros in numeric pre-release identifiers mid-build.
          PATTERN='^v(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)(-(0|[1-9][0-9]*|[0-9]*[A-Za-z-][0-9A-Za-z-]*)(\.(0|[1-9][0-9]*|[0-9]*[A-Za-z-][0-9A-Za-z-]*))*)?$'
          if [[ ! "$TAG" =~ $PATTERN ]]; then
            echo "::error::Tag '$TAG' is not a release tag. Expected vMAJOR.MINOR.PATCH or vMAJOR.MINOR.PATCH-prerelease, with no leading zeros in numeric parts (use -rc.1, not -rc.01) and no +build metadata."
            exit 1
          fi
          # Named RELEASE_VERSION rather than VERSION on purpose: MSBuild promotes
          # environment variables to properties, so a VERSION variable would silently
          # become the Version property for every dotnet command in this job.
          RELEASE_VERSION="${TAG#v}"
          echo "RELEASE_VERSION=$RELEASE_VERSION" >> "$GITHUB_ENV"
          if [[ "$RELEASE_VERSION" == *-* ]]; then
            echo "prerelease=true" >> "$GITHUB_OUTPUT"
          else
            echo "prerelease=false" >> "$GITHUB_OUTPUT"
          fi
          echo "Releasing version $RELEASE_VERSION from tag $TAG"
YML

wc -l .github/workflows/release.yml
```

Expected: `58 .github/workflows/release.yml`.

Four choices in this file are deliberate and are commented in place, because each is a plausible "fix" for a later editor to undo. The workflow-level `permissions: {}` makes every job-level grant explicit rather than a narrowing of an unknown default. `defaults.run.shell: bash` documents that the scripts use bash-only constructs. `timeout-minutes` bounds a wedged run, which matters because `cancel-in-progress: false` would otherwise let it hold the concurrency group for six hours. And the variable is `RELEASE_VERSION`, never `VERSION`: MSBuild promotes environment variables to properties, so a variable named `VERSION` in `$GITHUB_ENV` would silently become the `Version` property for every later `dotnet` command, including restore, with behavior that differs between Debug and Release.

The comments contain no contractions or possessives. That is not style: a lone apostrophe in prose inside a heredoc breaks the command transport of the tooling that runs these blocks. Balanced shell quoting like `'$TAG'` is unaffected.

- [x] **Step 2: Run the harness to verify it passes**

```bash
cd /c/Users/AddamBoord/source/repos/StateStore
export SCRATCH="/c/Users/AddamBoord/AppData/Local/Temp/statestore-release-plan"
bash "$SCRATCH/test-version-step.sh"; echo "exit: $?"
```

Expected: 18 lines each beginning `  ok`, then a blank line, then `PASS: all 18 cases behaved as specified` and `exit: 0`.

If an **accept** case fails, the regex is too strict or the version derivation is wrong. If a **reject** case fails, the regex is too permissive, which is the direction that ships a bad package version. Either way, compare the pattern character by character against the spec's Release-tag pattern section, whose accept and reject lists are the source of the harness tables. Do not edit the harness to match a broken pattern.

- [x] **Step 3: Stage the file and confirm it is stored with LF**

The working copy may hold CRLF, which is fine. What matters is the form stored in the repository, because that is what the Linux runner reads.

```bash
cd /c/Users/AddamBoord/source/repos/StateStore
git add .github/workflows/release.yml
git show :.github/workflows/release.yml | file -
```

Expected: `/dev/stdin: ASCII text`. The `git add` warning about LF being replaced by CRLF is expected and benign, as described in Before you start.

A result reading `ASCII text, with CRLF line terminators` is the failure case and would break the bash `run:` blocks on the runner. Both the harness and actionlint tolerate CRLF in the working copy, so neither will catch this for you.

### Task 1.3: Lint and commit

**Files:**
- Modify: `.github/workflows/release.yml` (staged and committed; no content change)

- [x] **Step 1: Run actionlint**

```bash
cd /c/Users/AddamBoord/source/repos/StateStore
PATH="/c/Users/AddamBoord/AppData/Local/Microsoft/WinGet/Packages/koalaman.shellcheck_Microsoft.Winget.Source_8wekyb3d8bbwe:$PATH" "/c/Users/AddamBoord/AppData/Local/Microsoft/WinGet/Packages/rhysd.actionlint_Microsoft.Winget.Source_8wekyb3d8bbwe/actionlint" .github/workflows/release.yml
echo "exit: $?"
```

Expected: no output before `exit: 0`. actionlint 1.7.12 checks YAML validity, expression syntax, and context availability, and with shellcheck on the PATH (which the command above arranges) it also lints every `run:` block. To confirm shellcheck is genuinely active, add `-verbose` and check that no line says the shellcheck rule was disabled.

actionlint does not validate that referenced actions or their versions exist. Those were confirmed current during planning.

If the binary is missing on the machine you are working from, install it with `winget install rhysd.actionlint` and re-run. The hardcoded path above is where winget places it.

- [x] **Step 2: Commit**

```bash
cd /c/Users/AddamBoord/source/repos/StateStore
git add .github/workflows/release.yml
git commit -m "Add release workflow with tag validation

Validates a pushed v* tag against the release-tag pattern and derives
the package version from it. Build, pack, and release steps follow."
```

Expected: `1 file changed, 58 insertions(+)` and a line creating `.github/workflows/release.yml`.

If the commit reports more than one file, something else was staged. The working tree carries an unstaged modification to `.claude/settings.local.json` that must not be swept in. Check with `git show --stat HEAD` and reset if needed.

### Task 1.4: Apply the quality-review amendments

**Files:**
- Modify: `.github/workflows/release.yml`
- Modify: `$SCRATCH/test-version-step.sh` (scratchpad)

**Why this task exists.** Tasks 1.1 to 1.3 were first executed with an earlier 41-line version of the workflow, committed as `1b8c467`. The code quality review of that commit found four things worth fixing before Chunk 2 multiplied the cost: the version variable was named `VERSION`, which MSBuild promotes to the `Version` property for every later `dotnet` command; there was no workflow-level `permissions: {}` floor; no `timeout-minutes` bounded a wedged run despite `cancel-in-progress: false`; and the rejection message did not name the two rules people trip on. It also found that actionlint had been running without shellcheck. All of those are now folded into the heredocs in Tasks 1.1 and 1.2 above, so a fresh executor gets the final form directly and this task is a no-op for them. If your checkout is at `1b8c467`, do the following.

- [x] **Step 1: Rewrite both files to the current Task 1.1 and Task 1.2 content**

Re-run Task 1.1 Step 1 and Task 1.2 Step 1 exactly as written above. Both are whole-file overwrites, so re-running them is safe.

Expected: `85` for the harness and `58 .github/workflows/release.yml` for the workflow.

- [x] **Step 2: Re-verify**

Re-run Task 1.2 Step 2 (harness), Task 1.2 Step 3 (staged form is LF), and Task 1.3 Step 1 (actionlint with shellcheck).

Expected: `PASS: all 18 cases behaved as specified`; `/dev/stdin: ASCII text`; actionlint prints nothing and exits 0.

- [x] **Step 3: Confirm the diff is only the intended amendments**

```bash
cd /c/Users/AddamBoord/source/repos/StateStore
git diff --cached --stat .github/workflows/release.yml
git diff --cached .github/workflows/release.yml | grep -E '^[-+]' | grep -v -E '^(\+\+\+|---)' | grep -c -E 'RELEASE_VERSION|permissions: \{\}|timeout-minutes|shell: bash|^\+\s*#|rc\.01'
```

Expected: the stat line shows `1 file changed, 22 insertions(+), 5 deletions(-)`, and the count is at least 12. The count confirms the changed lines are dominated by the intended amendments. It is not exhaustive: the two blank lines, the `defaults:` and `run:` structural keys, and the five removed lines fall outside the grep pattern and are expected. Read the full `git diff --cached` once; anything that is not the rename, the permissions floor, the timeout, the shell default, a comment, or the message is a transcription error.

- [x] **Step 4: Commit**

```bash
cd /c/Users/AddamBoord/source/repos/StateStore
git add .github/workflows/release.yml
git commit -m "Harden release workflow after review

Rename the derived version to RELEASE_VERSION: MSBuild promotes
environment variables to properties, so VERSION would silently become
the Version property for every dotnet command in the job. Add a
workflow-level permissions floor, bound the build job with a timeout so
a wedged run cannot hold the concurrency group, name the two rules the
tag message previously omitted, and comment the deliberate choices."
```

Expected: `1 file changed` and a `wc -l .github/workflows/release.yml` of 58.

---

## Chunk 2: Build, test, and pack

Add the steps that produce the packages. The two riskiest ones, pack and verify, are proven locally before they are written into CI.

### Task 2.1: Add checkout and SDK setup

**Files:**
- Modify: `.github/workflows/release.yml`

- [x] **Step 1: Append the checkout and setup steps**

Two mutually exclusive setup steps. The repository has no `global.json` today, so the second runs. When the pending restructure adds a `global.json` pinned to `10.0.204` with `rollForward: latestPatch`, the first takes over. A bare `dotnet-version: 10.0.x` installs the newest 10.0.x, currently in the 10.0.4xx band, which that pin refuses.

`hashFiles` resolves paths against the workspace, so both conditions are only meaningful after checkout. Keep this order.

```bash
cd /c/Users/AddamBoord/source/repos/StateStore

cat >> .github/workflows/release.yml <<'YML'

      - name: Checkout
        uses: actions/checkout@v7
        with:
          # Nothing after this step talks to git, and dotnet pack executes MSBuild from
          # every csproj under src/, so do not leave the token on disk for that code.
          persist-credentials: false

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

grep '^      - name:' .github/workflows/release.yml
```

Expected, in this order:

```
      - name: Resolve version from tag
      - name: Checkout
      - name: Set up .NET from global.json
      - name: Set up .NET 10
```

Order matters and actionlint will not check it: a correctly indented step appended in the wrong place lints clean.

- [x] **Step 2: Lint**

```bash
cd /c/Users/AddamBoord/source/repos/StateStore
PATH="/c/Users/AddamBoord/AppData/Local/Microsoft/WinGet/Packages/koalaman.shellcheck_Microsoft.Winget.Source_8wekyb3d8bbwe:$PATH" "/c/Users/AddamBoord/AppData/Local/Microsoft/WinGet/Packages/rhysd.actionlint_Microsoft.Winget.Source_8wekyb3d8bbwe/actionlint" .github/workflows/release.yml
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

Taking that fallback changes two later expectations, and both are stated as checksums, so note them now: Task 2.3 Step 1's step list becomes twelve names with `Detect global.json` third, and Chunk 3 Step 1's `wc -l` reports 247 rather than 237. Neither is then a transcription error.

- [x] **Step 3: Commit**

```bash
cd /c/Users/AddamBoord/source/repos/StateStore
git add .github/workflows/release.yml
git commit -m "Add checkout and SDK setup to release workflow

Selects the SDK from global.json when present so the restructure's
rollForward: latestPatch pin resolves, otherwise installs .NET 10."
wc -l .github/workflows/release.yml
```

Expected: `1 file changed, 19 insertions(+)` and `77 .github/workflows/release.yml`. The heredoc is nineteen lines including its leading blank line.

### Task 2.2: Prove the pack and verify mechanics locally

Before writing the CI steps, confirm the command sequence works against this solution and that the verify logic catches what it claims to.

- [x] **Step 1: Run the pack sequence with a probe version**

```bash
cd /c/Users/AddamBoord/source/repos/StateStore
export SCRATCH="/c/Users/AddamBoord/AppData/Local/Temp/statestore-release-plan"
mkdir -p "$SCRATCH"
rm -rf "$SCRATCH/packages" && mkdir -p "$SCRATCH/packages"
RELEASE_VERSION=0.0.1-probe
dotnet restore StateStore.sln
dotnet build StateStore.sln -c Release --no-restore -p:Version="$RELEASE_VERSION" -p:ContinuousIntegrationBuild=true
dotnet test StateStore.sln -c Release --no-build -p:Version="$RELEASE_VERSION" -p:ContinuousIntegrationBuild=true --logger trx --results-directory "$SCRATCH/test-results"
shopt -s nullglob
for proj in src/*/*.csproj; do
  dotnet pack "$proj" -c Release --no-build -o "$SCRATCH/packages" -p:Version="$RELEASE_VERSION" -p:ContinuousIntegrationBuild=true
done
ls -l "$SCRATCH/packages"
```

Expected: the test run passes, and the pack output is exactly one file, `StateStore.0.0.1-probe.nupkg`. No `.snupkg` yet; symbol packages arrive with the restructure, and every check in this plan treats them as optional.

The filename carrying `0.0.1-probe` rather than `1.0.0` is the proof that a command-line `Version` overrides the csproj.

The `dotnet test` invocation is included here, with only the results directory changed, so that every command the CI job runs has been executed locally at least once. The `--logger trx` form is correct for this project because it uses `Microsoft.NET.Test.Sdk` with the VSTest runner; a project on the newer Microsoft.Testing.Platform would need `--report-trx` instead.

- [x] **Step 2: Confirm the glob's depth assumption**

```bash
cd /c/Users/AddamBoord/source/repos/StateStore
find src -mindepth 3 -name '*.csproj' -not -path '*/bin/*' -not -path '*/obj/*' -not -path '*/.*/*'
echo "exit: $?"
```

Expected: no output before `exit: 0`. Every packable project sits directly under `src/`.

The `-not -path '*/.*/*'` clause is required, not cosmetic. Without it this command reports three csproj files inside the nested git worktree at `src/.claude/worktrees/`, which are not part of the build. Note that `-mindepth` is a global option in `find`, so the `-prune` idiom does not work here; path exclusion does.

- [x] **Step 3: Write the verify logic to a script for testing**

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

projects=(src/*/*.csproj)
if [[ ${#packages[@]} -ne ${#projects[@]} ]]; then
  echo "::error::Packed ${#projects[@]} project(s) but found ${#packages[@]} .nupkg file(s). A project under src/ produced no package (IsPackable=false?)."
  exit 1
fi

{
  echo
  echo "### Packages"
  echo
} >> "$GITHUB_STEP_SUMMARY"

for pkg in artifacts/packages/*.nupkg artifacts/packages/*.snupkg; do
  case "$pkg" in
    *".$RELEASE_VERSION.nupkg"|*".$RELEASE_VERSION.snupkg") ;;
    *)
      echo "::error::Package '$(basename "$pkg")' does not carry version $RELEASE_VERSION."
      exit 1
      ;;
  esac
  echo "- \`$(basename "$pkg")\`" >> "$GITHUB_STEP_SUMMARY"
done

ls -l artifacts/packages
SH

wc -l "$SCRATCH/verify.sh"
```

Expected: `39 /c/Users/AddamBoord/AppData/Local/Temp/statestore-release-plan/verify.sh`.

The project-count check is what makes the per-project pack loop honest. `dotnet pack` on a project whose `IsPackable` evaluates to `false` exits 0 and writes nothing, so without the count a project could vanish from a release while the run stayed green. With one project today the zero-package check covers it; the count is for the restructure, which adds two more.

- [x] **Step 4: Test the verify logic against six cases**

This runs in a synthetic tree under `$SCRATCH`, never against the real repository, so the nested-project cases cannot disturb your checkout.

**This block deliberately has no `cd` to the repository root.** Do not add one. Cases 4 and 5 create directories named `src/Providers` and `src/.claude`, which must land in the synthetic tree.

```bash
export SCRATCH="/c/Users/AddamBoord/AppData/Local/Temp/statestore-release-plan"
T="$SCRATCH/verifytest"
rm -rf "$T" && mkdir -p "$T/artifacts/packages" "$T/src/StateStore"
cd "$T"
touch src/StateStore/StateStore.csproj artifacts/packages/StateStore.0.0.1-probe.nupkg

echo "=== case 1: matching version, no .snupkg (today's state) ==="
: > summary.md
RELEASE_VERSION=0.0.1-probe GITHUB_STEP_SUMMARY="$T/summary.md" bash "$SCRATCH/verify.sh"; echo "exit: $?"
cat summary.md

echo "=== case 2: version mismatch ==="
RELEASE_VERSION=9.9.9 GITHUB_STEP_SUMMARY="$T/summary.md" bash "$SCRATCH/verify.sh"; echo "exit: $?"

echo "=== case 3: no packages ==="
rm artifacts/packages/*.nupkg
RELEASE_VERSION=0.0.1-probe GITHUB_STEP_SUMMARY="$T/summary.md" bash "$SCRATCH/verify.sh"; echo "exit: $?"

echo "=== case 4: nested projects ==="
touch artifacts/packages/StateStore.0.0.1-probe.nupkg
mkdir -p src/Providers/Foo src/Providers/Bar
touch src/Providers/Foo/Foo.csproj src/Providers/Bar/Bar.csproj
RELEASE_VERSION=0.0.1-probe GITHUB_STEP_SUMMARY="$T/summary.md" bash "$SCRATCH/verify.sh"; echo "exit: $?"

echo "=== case 5: hidden dir must not trip the nested check ==="
rm -rf src/Providers
mkdir -p src/.claude/worktrees/wt/src/StateStore
touch src/.claude/worktrees/wt/src/StateStore/StateStore.csproj
RELEASE_VERSION=0.0.1-probe GITHUB_STEP_SUMMARY="$T/summary.md" bash "$SCRATCH/verify.sh"; echo "exit: $?"

echo "=== case 6: two projects, one package ==="
mkdir -p src/StateStore.Other
touch src/StateStore.Other/StateStore.Other.csproj
RELEASE_VERSION=0.0.1-probe GITHUB_STEP_SUMMARY="$T/summary.md" bash "$SCRATCH/verify.sh"; echo "exit: $?"
```

Expected:

| Case | Expected |
|---|---|
| 1 | `exit: 0`, and the summary contains `` - `StateStore.0.0.1-probe.nupkg` `` |
| 2 | `::error::Package 'StateStore.0.0.1-probe.nupkg' does not carry version 9.9.9.` then `exit: 1` |
| 3 | `::error::No .nupkg was produced.` then `exit: 1` |
| 4 | One `::error::` naming both `Bar.csproj` and `Foo.csproj` on a single line, then `exit: 1` |
| 5 | `exit: 0` |
| 6 | `::error::Packed 2 project(s) but found 1 .nupkg file(s). A project under src/ produced no package (IsPackable=false?).` then `exit: 1` |

Case 4 proving the message is one line matters: GitHub annotations do not accept embedded newlines, so a multi-line value would surface only the first path. The order of the two filenames within that line is not asserted, because `find` does not guarantee traversal order.

Note what these cases do not cover: the `.snupkg` arm of the version check is never exercised, because no symbol package exists until the restructure enables `IncludeSymbols`. The arm is present so that it works when they appear.

- [x] **Step 5: Clean up the probe output**

`GeneratePackageOnBuild` also left a probe-versioned package in the project's own output directory.

```bash
cd /c/Users/AddamBoord/source/repos/StateStore
export SCRATCH="/c/Users/AddamBoord/AppData/Local/Temp/statestore-release-plan"
rm -rf "$SCRATCH/packages" "$SCRATCH/verifytest" "$SCRATCH/test-results"
rm -f src/StateStore/bin/Release/StateStore.0.0.1-probe.nupkg
rm -f src/StateStore/bin/Release/StateStore.0.0.1-probe.snupkg
find . -path ./src/.claude -prune -o -path '*/obj/Release/*.0.0.1-probe.nuspec' -print -exec rm -f {} +
ls -1 src/StateStore/bin/Release/*.nupkg 2>/dev/null || echo "(no packages in bin/Release)"
git status --short
```

Expected: no probe-versioned package remains. A pre-existing `StateStore.1.0.0.nupkg` may still be listed; it predates this work, is gitignored, and is deliberately left alone, which is why the probe file is deleted by name rather than by glob. The `find` prints and removes the intermediate `*.0.0.1-probe.nuspec` files that `GeneratePackageOnBuild` leaves under `obj/Release/` for the library and, because it is packable, the benchmarks project. They are gitignored and would be overwritten by the next build, so this is tidiness rather than correctness.

`git status --short` should show only the pre-existing noise described in Before you start. Nothing new, and nothing under `src/StateStore` or `artifacts/`. Both `bin/` and `artifacts/` are gitignored.

### Task 2.3: Add the build, test, and pack steps

**Files:**
- Modify: `.github/workflows/release.yml`

- [x] **Step 1: Append the steps**

`$RELEASE_VERSION` comes from `$GITHUB_ENV`, written in Task 1.2. Restore is the one step with no `-p:Version=`, because NuGet resolution does not depend on the version being produced. That claim is only true because the variable is not named `VERSION`: MSBuild promotes environment variables to properties, so a `VERSION` entry in `$GITHUB_ENV` would reach restore as the `Version` property regardless of the command line.

The verify block is the text you tested in Task 2.2, unchanged.

```bash
cd /c/Users/AddamBoord/source/repos/StateStore

cat >> .github/workflows/release.yml <<'YML'

      - name: Restore
        run: dotnet restore StateStore.sln

      - name: Build
        run: |
          set -euo pipefail
          # Test and Pack run with --no-build and must pass exactly the same -p: values
          # as this step, or the assemblies and the package will disagree about the version.
          dotnet build StateStore.sln -c Release --no-restore \
            -p:Version="$RELEASE_VERSION" -p:ContinuousIntegrationBuild=true

      - name: Test
        id: test
        run: |
          set -euo pipefail
          dotnet test StateStore.sln -c Release --no-build \
            -p:Version="$RELEASE_VERSION" -p:ContinuousIntegrationBuild=true \
            --logger trx --results-directory artifacts/test-results

      - name: Upload test results
        if: failure() && steps.test.outcome == 'failure'
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
              -p:Version="$RELEASE_VERSION" -p:ContinuousIntegrationBuild=true
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

          projects=(src/*/*.csproj)
          if [[ ${#packages[@]} -ne ${#projects[@]} ]]; then
            echo "::error::Packed ${#projects[@]} project(s) but found ${#packages[@]} .nupkg file(s). A project under src/ produced no package (IsPackable=false?)."
            exit 1
          fi

          {
            echo
            echo "### Packages"
            echo
          } >> "$GITHUB_STEP_SUMMARY"

          for pkg in artifacts/packages/*.nupkg artifacts/packages/*.snupkg; do
            case "$pkg" in
              *".$RELEASE_VERSION.nupkg"|*".$RELEASE_VERSION.snupkg") ;;
              *)
                echo "::error::Package '$(basename "$pkg")' does not carry version $RELEASE_VERSION."
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

grep '^      - name:' .github/workflows/release.yml
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

- [x] **Step 2: Confirm the verify block matches the version you tested**

The verify logic now exists twice, once as `$SCRATCH/verify.sh` and once inside the YAML at ten spaces of indentation. Extract it back out and diff, so a transcription slip cannot leave CI running logic that was never tested.

```bash
cd /c/Users/AddamBoord/source/repos/StateStore
export SCRATCH="/c/Users/AddamBoord/AppData/Local/Temp/statestore-release-plan"
awk '
  /^      - name: Verify packages$/ { found=1; next }
  found && /^        run: \|$/ { capture=1; found=0; next }
  capture {
    if ($0 ~ /^          / || $0 ~ /^[[:space:]]*$/) { sub(/^          /, ""); print; next }
    capture=0
  }
' .github/workflows/release.yml > "$SCRATCH/verify-from-yaml.sh"
diff -B "$SCRATCH/verify.sh" "$SCRATCH/verify-from-yaml.sh" && echo "identical"
```

Expected: `identical`, with no diff output above it. The `-B` flag is needed because the extraction picks up the blank line that separates this step from the next, leaving one trailing newline the script file does not have.

Any other difference means the block was retyped rather than copied. Take the tested version in `$SCRATCH/verify.sh` as authoritative, correct the YAML, and re-run Task 2.2 Step 4 afterwards.

- [x] **Step 3: Lint**

```bash
cd /c/Users/AddamBoord/source/repos/StateStore
PATH="/c/Users/AddamBoord/AppData/Local/Microsoft/WinGet/Packages/koalaman.shellcheck_Microsoft.Winget.Source_8wekyb3d8bbwe:$PATH" "/c/Users/AddamBoord/AppData/Local/Microsoft/WinGet/Packages/rhysd.actionlint_Microsoft.Winget.Source_8wekyb3d8bbwe/actionlint" .github/workflows/release.yml
echo "exit: $?"
```

Expected: no output before `exit: 0`. shellcheck runs over every `run:` block here, so quoting mistakes surface now.

- [x] **Step 4: Re-run the version-step harness**

The file changed, so confirm the extraction still lands on the version step. The harness fails loudly if the first `run: |` block is no longer the one holding `PATTERN=`.

```bash
cd /c/Users/AddamBoord/source/repos/StateStore
export SCRATCH="/c/Users/AddamBoord/AppData/Local/Temp/statestore-release-plan"
bash "$SCRATCH/test-version-step.sh"; echo "exit: $?"
```

Expected: `PASS: all 18 cases behaved as specified` and `exit: 0`.

- [x] **Step 5: Commit**

```bash
cd /c/Users/AddamBoord/source/repos/StateStore
git add .github/workflows/release.yml
git commit -m "Add build, test, and pack steps to release workflow

Packs each project directly under src/ with the tag version, asserts
every package filename carries that version, and fails on a project
nested too deep for the glob rather than dropping it silently."
wc -l .github/workflows/release.yml
```

Expected: `1 file changed, 96 insertions(+)` and `173 .github/workflows/release.yml`. Chunk 3 adds the remaining 64 lines to reach the 237-line checksum.

### Task 2.4: Apply the Chunk 2 quality-review amendments

**Files:**
- Modify: `.github/workflows/release.yml`
- Modify: `$SCRATCH/verify.sh` (scratchpad)

**Why this task exists.** Tasks 2.1 to 2.3 were first executed as commits `630133d` and `c13179b`, leaving a 160-line file. The code quality review of that state found one gap that matters and three cheap hardenings: `dotnet pack` on a project whose `IsPackable` evaluates to `false` exits 0 and writes nothing, so a project could vanish from a release while the run stayed green; the `--no-build` coupling between Build, Test, and Pack was unstated at the point an editor would break it; the test-results upload fired on any earlier failure, not only a test failure; and checkout left git credentials on disk for `dotnet pack` to read. All four are now folded into the heredocs in Tasks 2.1, 2.2, and 2.3 above, so a fresh executor gets the final form directly and this task is a no-op for them. If your checkout is at `c13179b`, do the following. The appends in Tasks 2.1 and 2.3 are not idempotent, so the file is first reset to its Chunk 1 form.

- [x] **Step 1: Reset the workflow to the Chunk 1 content**

```bash
cd /c/Users/AddamBoord/source/repos/StateStore
git show e2bb913:.github/workflows/release.yml > .github/workflows/release.yml
wc -l .github/workflows/release.yml
```

Expected: `58 .github/workflows/release.yml`. Commit `e2bb913` is the amended Chunk 1 file; it is the same blob as at `1f3df1e`.

- [x] **Step 2: Re-run Task 2.1 Step 1**

Expected: the four step names in order, then `wc -l .github/workflows/release.yml` reports 77.

- [x] **Step 3: Re-run Task 2.2 Step 3 and Step 4**

Expected: `39` for `verify.sh`, and all six cases behave as tabulated.

- [x] **Step 4: Re-run Task 2.3 Steps 1 to 4**

Expected: eleven step names in order; `identical` from the verify diff; actionlint prints nothing and exits 0; the harness reports `PASS: all 18 cases behaved as specified`.

- [x] **Step 5: Confirm the diff against the previous commit is only the intended amendments**

```bash
cd /c/Users/AddamBoord/source/repos/StateStore
git add .github/workflows/release.yml
git diff --cached --stat
git diff --cached | grep '^-' | grep -v '^---'
wc -l .github/workflows/release.yml
```

Expected: `1 file changed, 14 insertions(+), 1 deletion(-)`; the only removed line is `        if: failure()`; and `173 .github/workflows/release.yml`. The fourteen added lines are the `with:` block under checkout (four lines), the two-line Build comment, `id: test`, the replacement `if:` line, and the six-line project-count block. Anything else is a transcription error.

- [x] **Step 6: Commit**

```bash
cd /c/Users/AddamBoord/source/repos/StateStore
git commit -m "Harden release workflow build job after review

Assert one package per project under src/: dotnet pack on a project
whose IsPackable is false exits 0 and writes nothing, so a project
could otherwise vanish from a release on a green run. Scope the
test-results upload to a test failure, drop git credentials after
checkout since nothing later needs them, and name the --no-build
coupling where an editor would break it."
```

Expected: `1 file changed, 14 insertions(+), 1 deletion(-)`.

---

## Chunk 3: The release job

### Task 3.1: Add the release job

**Files:**
- Modify: `.github/workflows/release.yml`

- [x] **Step 1: Append the job**

This is the only job with write permission. The tag reaches bash through the job's `env` mapping rather than inline interpolation, so a crafted ref name cannot inject shell. `--verify-tag` makes `gh` refuse to create a release for a tag absent from the remote.

```bash
cd /c/Users/AddamBoord/source/repos/StateStore

cat >> .github/workflows/release.yml <<'YML'

  release:
    needs: build
    runs-on: ubuntu-latest
    timeout-minutes: 10
    permissions:
      contents: write
    env:
      TAG: ${{ github.ref_name }}
      PRERELEASE: ${{ needs.build.outputs.prerelease }}
    steps:
      - name: Download packages
        uses: actions/download-artifact@v8
        with:
          name: packages
          path: artifacts/packages

      - name: Create or update release
        env:
          GH_TOKEN: ${{ github.token }}
          GH_REPO: ${{ github.repository }}
        run: |
          set -euo pipefail
          shopt -s nullglob
          # A stale output reference yields an empty string, not an error. Left
          # unchecked, empty reads as "not a pre-release" and a -rc tag would be
          # published as the latest stable release.
          if [[ "$PRERELEASE" != "true" && "$PRERELEASE" != "false" ]]; then
            echo "::error::prerelease flag from the build job is '$PRERELEASE'; expected true or false."
            exit 1
          fi
          assets=(artifacts/packages/*.nupkg artifacts/packages/*.snupkg)
          if [[ ${#assets[@]} -eq 0 ]]; then
            echo "::error::No package assets to attach to release $TAG."
            exit 1
          fi
          # The existence probe doubles as a draft check. gh release create works in
          # three API calls (create as draft, upload assets, publish), so a failure
          # partway through leaves a draft, and a plain retry would attach assets to
          # it and exit 0 with nothing published.
          if draft="$(gh release view "$TAG" --json isDraft --jq .isDraft 2>/dev/null)"; then
            echo "Release $TAG already exists. Uploading assets."
            gh release upload "$TAG" --clobber "${assets[@]}"
            if [[ "$draft" == "true" ]]; then
              echo "::error::Release $TAG exists as a draft. Assets are attached but nothing is published. Publish it, or delete the draft and re-run."
              exit 1
            fi
          else
            echo "Creating release $TAG."
            # --verify-tag earns its place: without it, gh creates a missing tag from
            # the default branch HEAD and publishes a release whose contents disagree
            # with the tag it names.
            args=(--title "$TAG" --generate-notes --verify-tag)
            if [[ "$PRERELEASE" == "true" ]]; then
              args+=(--prerelease)
            fi
            gh release create "$TAG" "${args[@]}" "${assets[@]}"
          fi
          {
            echo
            echo "### Release"
            echo
            echo "- $TAG: $(gh release view "$TAG" --json url --jq .url)"
          } >> "$GITHUB_STEP_SUMMARY"
YML

wc -l .github/workflows/release.yml
```

Expected: `237 .github/workflows/release.yml`. This number is the whole-file checksum for this plan: a different count means a transcription error somewhere in Chunks 1 to 3, not a design problem.

Three details in this job are load-bearing and easy to undo by accident. The probe captures `isDraft` rather than discarding output, because `gh release create` is three API calls and a failure partway leaves a draft that a plain retry would silently accept. `--verify-tag` stops `gh` from creating a missing tag at the default branch HEAD and publishing a release whose contents disagree with it. And `GH_TOKEN` sits on the step rather than the job, so the only write credential in the workflow is not in the artifact download step's environment.

- [x] **Step 2: Lint**

```bash
cd /c/Users/AddamBoord/source/repos/StateStore
PATH="/c/Users/AddamBoord/AppData/Local/Microsoft/WinGet/Packages/koalaman.shellcheck_Microsoft.Winget.Source_8wekyb3d8bbwe:$PATH" "/c/Users/AddamBoord/AppData/Local/Microsoft/WinGet/Packages/rhysd.actionlint_Microsoft.Winget.Source_8wekyb3d8bbwe/actionlint" .github/workflows/release.yml
echo "exit: $?"
```

Expected: no output before `exit: 0`.

- [x] **Step 3: Confirm the job graph and permissions**

```bash
cd /c/Users/AddamBoord/source/repos/StateStore
grep -E "^  (build|release):|^    (needs|runs-on|outputs|permissions):|^      contents: (read|write)" .github/workflows/release.yml
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

- [x] **Step 4: Re-confirm the stored line endings**

The file has been appended to three times since Task 1.2 Step 3. Check the whole assembled file once.

```bash
cd /c/Users/AddamBoord/source/repos/StateStore
git add .github/workflows/release.yml
git show :.github/workflows/release.yml | file -
```

Expected: `/dev/stdin: ASCII text`, with no CRLF qualifier.

- [x] **Step 5: Commit**

```bash
cd /c/Users/AddamBoord/source/repos/StateStore
git add .github/workflows/release.yml
git commit -m "Add release job to release workflow

Creates the GitHub Release for the tag with the packages attached, or
uploads over the assets of a release that already exists so a failed
run can be retried. Marks a hyphenated version as a pre-release."
wc -l .github/workflows/release.yml
```

Expected: `1 file changed, 64 insertions(+)` and `237 .github/workflows/release.yml`.

### Task 3.2: Apply the Chunk 3 quality-review amendments

**Files:**
- Modify: `.github/workflows/release.yml`

**Why this task exists.** Task 3.1 was first executed as commit `4d0a345`, leaving a 219-line file whose release job used a bare `gh release view` probe. The code quality review of that state found that `gh release create` performs three API calls, creating the release as a draft, uploading assets, then publishing, so a failure partway through leaves a draft. A plain retry then took the update branch, attached the assets, and exited 0 with nothing published: a green run over an unpublished release. That is now fixed, along with four cheap hardenings. All are folded into the Task 3.1 heredoc above, so a fresh executor gets the final form and this task is a no-op for them. If your checkout is at `4d0a345`, do the following.

- [x] **Step 1: Reset the file to its post-Chunk-2 state and re-append**

The append in Task 3.1 is not idempotent, so drop the old release job first.

```bash
cd /c/Users/AddamBoord/source/repos/StateStore
git show 817349e:.github/workflows/release.yml > .github/workflows/release.yml
wc -l .github/workflows/release.yml
```

Expected: `173 .github/workflows/release.yml`.

- [x] **Step 2: Re-run Task 3.1 Step 1**

Expected: `237 .github/workflows/release.yml`.

- [x] **Step 3: Re-run Task 3.1 Steps 2 to 4**

Expected: actionlint prints nothing and exits 0; the job graph grep matches; the staged form is `ASCII text`.

- [x] **Step 4: Confirm the diff is only the intended amendments**

```bash
cd /c/Users/AddamBoord/source/repos/StateStore
git add .github/workflows/release.yml
git diff --cached --stat
git diff --cached | grep '^-' | grep -v '^---'
```

Expected: `1 file changed, 23 insertions(+), 5 deletions(-)`, and these five removed lines exactly:

```
-      GH_TOKEN: ${{ github.token }}
-      GH_REPO: ${{ github.repository }}
-          if gh release view "$TAG" >/dev/null 2>&1; then
-            gh release upload "$TAG" "${assets[@]}" --clobber
-            gh release create "$TAG" "${assets[@]}" "${args[@]}"
```

The two env lines move to the step, and the three `gh` lines are replaced by their reordered forms. Everything added is the step-level `env` block, the draft probe with its four-line comment, the draft failure branch, the three-line `--verify-tag` comment, and the six-line step-summary block. Anything else is a transcription error.

- [x] **Step 5: Commit**

```bash
cd /c/Users/AddamBoord/source/repos/StateStore
git commit -m "Harden release job after review

Treat a draft release as an error rather than success. gh release
create runs as three API calls, so a failure partway leaves a draft,
and the retry path would attach assets to it and exit 0 with nothing
published. The probe now captures isDraft and fails loudly.

Also confine the write token to the step that calls gh, put flags
before positional asset paths, record the release URL in the run
summary, and comment why --verify-tag is not optional."
```

---

## Chunk 4: End-to-end verification

**This chunk is gated. Get explicit user confirmation before running any step in it, and do not push a branch or a tag on your own initiative.**

Every step here touches the public repository `Cadtastic-Solutions/StateStore`. Task 4.0 publishes a branch for the first time. Task 4.1 creates a public pre-release, briefly. Task 4.2 creates a failing workflow run. A pushed tag is visible the moment it lands.

**If execution stops for any reason after a tag is pushed, run that task's cleanup step before finishing.** Cleanup is the last step of each task, but it is not optional and it is not only for the success path.

**What cleanup does not remove.** Three things, all permanent or long-lived:

1. **The published branch.** Task 4.0 Step 4 pushes `refactor/repo-organization` to the public repository for the first time, and nothing in this plan deletes it. It carries the unrelated restructure design and plan work as well as this workflow. This is the one genuinely irreversible act in the chunk, so it has its own gated step.
2. **Both workflow runs**, which stay in the repository's Actions tab.
3. **The successful run's `packages` artifact**, for the default 90-day retention.

Only the tags and releases are cleaned up. Nothing in that list is misleading, so this plan leaves them; add `gh run delete <run-id>` to the cleanup steps if you want the Actions tab clear.

**If the user declines this chunk,** skip to Chunk 5 and use the alternate wording in Task 5.1 Step 1. Do not claim end-to-end verification that did not happen.

### Task 4.0: Preconditions

- [x] **Step 1: Confirm the gh CLI is authenticated as the right account**

Two accounts may be logged in. Every step in this chunk depends on the active one having write access to the org repository.

```bash
cd /c/Users/AddamBoord/source/repos/StateStore
gh auth status
git remote -v
```

Expected: `Active account: true` under `github.com` for an account with push rights to `Cadtastic-Solutions/StateStore`, and `origin` pointing at `https://github.com/Cadtastic-Solutions/StateStore.git`.

- [x] **Step 2: Confirm with the user that immutable releases are not enabled**

If the repository or organization enforces immutable releases, a published release cannot be deleted. That would leave the throwaway `v0.0.1-ci.1` release on a public repository permanently, turning this chunk from briefly-public into irreversible. It would also break the re-run path the workflow's create-or-update design depends on.

**This cannot be checked from the API.** `immutable_releases` is not a field on the repository response: `gh api repos/Cadtastic-Solutions/StateStore --jq 'has("immutable_releases")'` returns `false`, and querying it yields `null` whatever the real setting is. A script that tests that field always passes and gives false assurance, so do not write one.

Ask the user to confirm the setting is off, under the repository's Settings then General then Releases, and under the organization's equivalent policy page. Record their answer.

If it is enabled, or if the user cannot confirm either way, **stop and say so.** Do not proceed to Task 4.1: there is no cleanup path for a release that cannot be deleted. Task 4.2 is unaffected, because it never creates a release.

- [x] **Step 3: Confirm the workflow is in the commit that will be tagged**

A tag-push workflow runs from the tagged commit, so the file has to already be there.

```bash
cd /c/Users/AddamBoord/source/repos/StateStore
git log --oneline -1
git ls-tree HEAD .github/workflows/release.yml
```

Expected: `git ls-tree` prints a blob line ending in `.github/workflows/release.yml`. Empty output means Chunks 1 to 3 are not committed.

- [x] **Step 4: Publish the branch**

The branch has no upstream today, so this is its first publication to the public repository. It carries the unrelated restructure design work as well as this workflow.

Pushing the tag in Task 4.1 would carry the commit to the remote regardless, and a tag push triggers the workflow whether or not the tag is reachable from a branch. The branch is pushed anyway for two reasons: after `--cleanup-tag` the commits would otherwise become unreferenced on the remote and eligible for garbage collection, which breaks the run's commit links, and `--generate-notes` produces more predictable output for a tag reachable from a branch.

```bash
cd /c/Users/AddamBoord/source/repos/StateStore
git push -u origin refactor/repo-organization
```

Expected: a `new branch` line and `branch 'refactor/repo-organization' set up to track 'origin/refactor/repo-organization'`.

If the push is rejected, stop and report why. Do not force.

### Task 4.1: Happy path with a throwaway pre-release tag

- [x] **Step 1: Push the throwaway tag**

```bash
cd /c/Users/AddamBoord/source/repos/StateStore
git tag v0.0.1-ci.1
git push origin v0.0.1-ci.1
```

Expected: a line reading `* [new tag]         v0.0.1-ci.1 -> v0.0.1-ci.1`.

If the push is rejected, stop and report why rather than retrying with force.

- [x] **Step 2: Watch the run**

`gh run watch` takes the run ID as a required positional argument, so capture it first. A tag-triggered run carries the tag name in `headBranch`, so filter on that rather than taking the most recent run: there is a short registration delay after a tag push, and an unfiltered `--limit 1` can return an older run.

```bash
cd /c/Users/AddamBoord/source/repos/StateStore
export MSYS_NO_PATHCONV=1
REPO=Cadtastic-Solutions/StateStore
TAG=v0.0.1-ci.1
RUN_ID=$(gh run list --repo "$REPO" --branch "$TAG" --limit 1 --json databaseId --jq '.[0].databaseId')
echo "RUN_ID=$RUN_ID"
gh run watch "$RUN_ID" --repo "$REPO" --exit-status; echo "exit: $?"
```

Expected: `RUN_ID` holds a number, and the watch ends with `exit: 0` after both jobs succeed.

If `RUN_ID` comes back empty, the run has not registered yet. Wait a few seconds and re-run the block. An empty `RUN_ID` makes `gh run watch` fail on a missing argument rather than silently watching the wrong run, which is the point of filtering by tag.

On failure, read the logs and fix the workflow before retrying. This block re-derives `RUN_ID`, since the variable from the previous block is gone:

```bash
cd /c/Users/AddamBoord/source/repos/StateStore
export MSYS_NO_PATHCONV=1
REPO=Cadtastic-Solutions/StateStore
TAG=v0.0.1-ci.1
RUN_ID=$(gh run list --repo "$REPO" --branch "$TAG" --limit 1 --json databaseId --jq '.[0].databaseId')
gh run view "$RUN_ID" --repo "$REPO" --log-failed
```

Three retry options, cheapest first. If only the `release` job failed, `gh run rerun "$RUN_ID" --repo "$REPO" --failed` reruns it against the stored artifact and exercises the create-or-update path. If the workflow file itself needs a fix, commit it and push a fresh tag such as `v0.0.1-ci.2`, which the pattern accepts, avoiding any delete-and-re-tag dance. Only if you want to reuse the same tag, run Step 5 first, then start again from Step 1.

- [x] **Step 3: Verify the release**

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

- [x] **Step 4: Verify the run summary content**

The step summary is not exposed by the API, so confirm the same facts from the logs. Opening the run's summary page in a browser with `gh run view "$RUN_ID" --repo "$REPO" --web` shows the rendered version if you prefer.

```bash
export MSYS_NO_PATHCONV=1
REPO=Cadtastic-Solutions/StateStore
TAG=v0.0.1-ci.1
RUN_ID=$(gh run list --repo "$REPO" --branch "$TAG" --limit 1 --json databaseId --jq '.[0].databaseId')
gh run view "$RUN_ID" --repo "$REPO" --log | grep -E "Packing src/|StateStore\.0\.0\.1-ci\.1\.nupkg"
```

Expected: at least a line containing `Packing src/StateStore/StateStore.csproj` and a line naming `StateStore.0.0.1-ci.1.nupkg`. The first comes from the Pack step's own echo, the second from the pack output and the Verify step's directory listing.

- [x] **Step 5: Delete the release and the tag**

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

**Preconditions:** every step of Task 4.0 must have been completed, including the branch publication, whether or not Task 4.1 was run. Task 4.0 Step 2 is the one exception: this task never creates a release, so the immutable-releases setting does not affect it.

- [x] **Step 1: Push a tag the glob accepts but the pattern rejects**

`v0.0.1.1` matches the coarse trigger glob `v[0-9]+.[0-9]+.[0-9]+*`, so the workflow starts and then fails validation. That is exactly the case worth testing: a malformed tag the trigger cannot filter out.

```bash
cd /c/Users/AddamBoord/source/repos/StateStore
git tag v0.0.1.1
git push origin v0.0.1.1
```

Expected: `* [new tag]         v0.0.1.1 -> v0.0.1.1`.

- [x] **Step 2: Confirm the run fails at validation**

Filter by tag again. Without `--branch`, an unfiltered `--limit 1` can return Task 4.1's successful run, and then every assertion below inverts and appears to pass.

```bash
cd /c/Users/AddamBoord/source/repos/StateStore
export MSYS_NO_PATHCONV=1
REPO=Cadtastic-Solutions/StateStore
TAG=v0.0.1.1
RUN_ID=$(gh run list --repo "$REPO" --branch "$TAG" --limit 1 --json databaseId --jq '.[0].databaseId')
echo "RUN_ID=$RUN_ID"
gh run watch "$RUN_ID" --repo "$REPO" --exit-status; echo "watch exit: $? (non-zero is the expected outcome here)"
gh run view "$RUN_ID" --repo "$REPO" --log-failed | grep -i "not a release tag"
gh run view "$RUN_ID" --repo "$REPO" --json jobs --jq '.jobs[] | {name, conclusion}'
```

Expected:
- `RUN_ID` holds a number. If it is empty, the run has not registered yet; wait a few seconds and re-run the block.
- The watch exits non-zero, which is success for this test.
- The grep prints the error naming the tag and the expected format.
- The job listing shows `build` with conclusion `failure` and `release` with conclusion `skipped`.

The `release` job being skipped rather than failed is the assertion that matters: the failure was contained to validation and nothing was ever checked out, restored, or published.

- [x] **Step 3: Confirm no release was created**

```bash
export MSYS_NO_PATHCONV=1
gh release view v0.0.1.1 --repo Cadtastic-Solutions/StateStore; echo "exit: $?"
```

Expected: `release not found` and a non-zero exit.

If a release does exist, the pattern was mis-transcribed and accepted the tag. In that case use `gh release delete v0.0.1.1 --repo Cadtastic-Solutions/StateStore --yes --cleanup-tag` in place of Step 4, then fix the pattern and re-run Chunk 1's harness. Deleting only the tag would orphan the release.

- [x] **Step 4: Delete the tag**

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

- [x] **Step 1: Update the status line**

Two cases. Pick the one that matches what actually happened.

If Chunk 4 ran and passed, replace the header's `**Status:**` line with, using an ISO date:

```markdown
**Status:** Implemented — verified end-to-end on 2026-09-09 with tag `v0.0.1-ci.1`
```

If Chunk 4 was declined or skipped, use instead:

```markdown
**Status:** Implemented — statically verified (actionlint, tag-pattern harness, local pack probe). Not yet exercised by a real tag push.
```

- [x] **Step 2: Update the spec's Verification section**

Its preamble reads "To be carried out by the implementation plan," which goes stale the moment the status flips. Replace that line with a note saying the checks were carried out by `docs/superpowers/plans/2026-09-09-release-workflow.md`, and mark any step Chunk 4 did not run as not performed.

- [x] **Step 3: Correct the stale find command in the spec**

The spec's `build` step 9 documents the nested-project check without the three `-not -path` exclusions, so as written it reports a false positive in this very repository. Chunk 2 proved the exclusions are load-bearing.

```bash
cd /c/Users/AddamBoord/source/repos/StateStore
grep -n "mindepth 3" docs/superpowers/specs/2026-09-09-release-workflow-design.md
```

Update each occurrence to the form Chunk 2 uses:

```
find src -mindepth 3 -name '*.csproj' -not -path '*/bin/*' -not -path '*/obj/*' -not -path '*/.*/*'
```

Expected afterwards: every `mindepth 3` line in the spec carries all three exclusions.

- [x] **Step 4: Verify and commit**

```bash
cd /c/Users/AddamBoord/source/repos/StateStore
grep -n "^\*\*Status:\*\*" docs/superpowers/specs/2026-09-09-release-workflow-design.md
grep -c "To be carried out by the implementation plan" docs/superpowers/specs/2026-09-09-release-workflow-design.md
grep -c "mindepth 3 -name '\*\.csproj' -not -path '\*/bin/\*' -not -path '\*/obj/\*' -not -path '\*/\.\*/\*'" docs/superpowers/specs/2026-09-09-release-workflow-design.md
```

Expected: the status line shows your new wording, the second count is `0`, and the third is at least `1`.

```bash
cd /c/Users/AddamBoord/source/repos/StateStore
git add docs/superpowers/specs/2026-09-09-release-workflow-design.md
git commit -m "Record release workflow verification results in the spec"
```

### Task 5.2: Correct the restructure plan and its design doc

The pending restructure plan has 165 unexecuted steps and would, as written, reintroduce two failures this workflow depends on not happening. The spec records these under Follow-ups, but whoever executes that plan will not read this spec.

A note alone is not enough. `Microsoft.Data.Sqlite` at `10.0.8` appears as copy-paste csproj content that an executor will transcribe verbatim, so correct the literals as well as adding the pointer.

**Files:**
- Modify: `docs/superpowers/plans/2026-05-22-repo-restructure.md`

- [x] **Step 1: Correct the four Sqlite version literals**

Only the `Microsoft.Data.Sqlite` references change. The `Microsoft.Extensions.*` packages at `10.0.8` are unaffected by the advisory and stay as they are.

```bash
cd /c/Users/AddamBoord/source/repos/StateStore
grep -n 'Microsoft\.Data\.Sqlite 10\.0\.8\|Microsoft\.Data\.Sqlite" Version="10\.0\.8"' docs/superpowers/plans/2026-05-22-repo-restructure.md
```

Expected: exactly four lines, at 9 (the Tech Stack summary), 381, 1529, and 1667.

The pattern spells out both spellings on purpose. A looser `Microsoft\.Data\.Sqlite.*10\.0\.8` also matches line 9 after a correct edit, because that line carries a second, unrelated `10.0.8` for `Microsoft.Extensions.DependencyInjection.Abstractions` and `.*` reaches it. A bare `Microsoft.Data.Sqlite` grep matches eleven lines, seven of which are prose needing no change.

Edit each of those four to read `10.0.12` instead of `10.0.8`, then confirm:

```bash
cd /c/Users/AddamBoord/source/repos/StateStore
echo "stale: $(grep -c 'Microsoft\.Data\.Sqlite 10\.0\.8\|Microsoft\.Data\.Sqlite" Version="10\.0\.8"' docs/superpowers/plans/2026-05-22-repo-restructure.md)"
echo "fixed: $(grep -c 'Microsoft\.Data\.Sqlite.*10\.0\.12' docs/superpowers/plans/2026-05-22-repo-restructure.md)"
```

Expected exactly:

```
stale: 0
fixed: 4
```

- [x] **Step 2: Insert the warning note**

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

- [x] **Step 3: Correct the same literal in the restructure design doc**

The spec's Follow-up names the design doc, and its line 199 still specifies `Microsoft.Data.Sqlite 10.0.8` for the future `StateStore.Sqlite.csproj`. Fixing only the plan would leave the two documents contradicting each other.

```bash
cd /c/Users/AddamBoord/source/repos/StateStore
grep -n 'Microsoft\.Data\.Sqlite 10\.0\.8' docs/superpowers/specs/2026-05-22-repo-restructure-design.md
```

Expected: exactly one line, 199.

Change it to `10.0.12`, then confirm:

```bash
cd /c/Users/AddamBoord/source/repos/StateStore
echo "stale: $(grep -c 'Microsoft\.Data\.Sqlite 10\.0\.8' docs/superpowers/specs/2026-05-22-repo-restructure-design.md)"
echo "fixed: $(grep -c 'Microsoft\.Data\.Sqlite 10\.0\.12' docs/superpowers/specs/2026-05-22-repo-restructure-design.md)"
```

Expected: `stale: 0` and `fixed: 1`.

- [x] **Step 4: Verify the note landed and commit both files**

```bash
cd /c/Users/AddamBoord/source/repos/StateStore
sed -n '9,28p' docs/superpowers/plans/2026-05-22-repo-restructure.md
```

Expected: the note appears after the `**Spec:**` line and before the `---` rule, with the File map heading below it.

```bash
cd /c/Users/AddamBoord/source/repos/StateStore
git add docs/superpowers/plans/2026-05-22-repo-restructure.md docs/superpowers/specs/2026-05-22-repo-restructure-design.md
git commit -m "Correct Sqlite version and flag IsPackable trap in restructure work

Executing that plan as written would reintroduce NU1903 and add NU5039,
both of which break the release workflow's build step."
```

Expected: `2 files changed`.

Do not count total `10.0.8` occurrences as a check. That number drops from 11 lines to 8, not to 7, because line 9 carries two occurrences and only one of them changes. The per-pattern counts in Steps 1 and 3 are the reliable assertions.

### Task 5.3: Mark this plan complete

**Files:**
- Modify: `docs/superpowers/plans/2026-09-09-release-workflow.md`

- [x] **Step 1: Tick the completed checkboxes**

Go through this plan and change `- [ ]` to `- [x]` for every step actually performed. Leave the rest unticked and add a one-line note under each saying why it was skipped. An unticked box is information; a falsely ticked one is a lie to the next reader.

If Chunk 4 was declined, its boxes stay empty with a note pointing at Task 5.1's alternate status wording.

Task 5.4's own boxes will still be open at this point, since it runs after this task. Note that in the commit message rather than pre-ticking them.

- [x] **Step 2: Verify and commit**

```bash
cd /c/Users/AddamBoord/source/repos/StateStore
echo "unticked: $(grep -c '^- \[ \]' docs/superpowers/plans/2026-09-09-release-workflow.md)"
echo "ticked:   $(grep -c '^- \[x\]' docs/superpowers/plans/2026-09-09-release-workflow.md)"
```

Expected: the ticked count matches the number of steps you performed, and every remaining unticked step has a note explaining why.

```bash
cd /c/Users/AddamBoord/source/repos/StateStore
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

One constraint to raise when presenting the options: the repository has an active ruleset named `enforced-reviews` targeting the default branch, requiring a pull request with review and forbidding deletion and non-fast-forward pushes. A direct push to `master` is therefore not available, and a reviewed pull request is the only merge path. The ruleset targets branches, not tags, and only the default branch, so it does not affect anything in Chunk 4.

---

## Verification summary

The plan is complete when all of these hold. The three rows marked gated depend on Chunk 4 and do not apply if it was declined.

| # | Check | How | Gated |
|---|---|---|---|
| 1 | The version step accepts and rejects exactly as specified, and derives the right version and pre-release flag | `bash "$SCRATCH/test-version-step.sh"` prints PASS for all 18 cases | No |
| 2 | The workflow is valid and shellcheck-clean | actionlint exits 0 with no output | No |
| 3 | The assembled file matches this plan | `wc -l .github/workflows/release.yml` reports 237, or 247 if Task 2.1's `hashFiles` fallback was taken | No |
| 4 | The verify logic catches mismatched versions, missing packages, nested projects, and a project that produced no package | Task 2.2 Step 4's six cases behave as tabulated | No |
| 5 | A command-line version overrides the csproj value | The local probe produced `StateStore.0.0.1-probe.nupkg` | No |
| 6 | Every packable project is reachable by the glob | `find src -mindepth 3 -name '*.csproj' -not -path '*/bin/*' -not -path '*/obj/*' -not -path '*/.*/*'` prints nothing | No |
| 7 | `build` cannot write to the repository | `permissions: contents: read` on `build`, `write` only on `release` | No |
| 8 | A valid tag yields a release with the versioned package attached | `gh release view v0.0.1-ci.1` reports a pre-release with `StateStore.0.0.1-ci.1.nupkg` attached | Yes |
| 9 | An invalid tag fails fast and releases nothing | `build` fails at its first step, `release` is skipped, and `gh release view v0.0.1.1` reports not found | Yes |
| 10 | No test tags or releases remain | `git tag -l` and `git ls-remote --tags origin` both print nothing | Yes |
