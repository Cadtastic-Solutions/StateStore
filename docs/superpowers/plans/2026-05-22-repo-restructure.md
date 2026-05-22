# Repo Restructure & Provider Split Implementation Plan

> **For agentic workers:** REQUIRED: Use superpowers:subagent-driven-development (if subagents available) or superpowers:executing-plans to implement this plan. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Split StateStore's bundled providers into separate NuGet packages (`StateStore.MongoDb`, `StateStore.Sqlite`), promote docs to repo root, hoist shared NuGet metadata, add SourceLink + symbol packages, replace the closed `StorageProviderKind` enum with an open-extensible factory, convert the static examples file into runnable sample projects, and ship a proper root README.

**Architecture:** The core `StateStore` package retains InMemory + FileSystem providers and the abstractions/builder/serialization/middleware/AutoSave subsystems. Two new packages (`StateStore.MongoDb`, `StateStore.Sqlite`) live under `src/`, each adding their own provider, options, and `UseXxx(...)` extension methods on both `StateStoreBuilder` (standalone) and `StateStoreOptions` (DI). The DI surface is opened via a public `ConfigureProvider(Action<IServiceCollection>)` method on `StateStoreOptions` — replacing the closed enum so external assemblies can plug in.

**Tech Stack:** .NET 8/9/10, xUnit v3, MSBuild SDK-style projects, `Microsoft.SourceLink.GitHub`, `MongoDB.Driver 3.8.1`, `Microsoft.Data.Sqlite 10.0.8`, `Microsoft.Extensions.DependencyInjection.Abstractions 10.0.8`.

**Spec:** [docs/superpowers/specs/2026-05-22-repo-restructure-design.md](../specs/2026-05-22-repo-restructure-design.md)

---

## File map

**Created:**
- `README.md` (repo root)
- `global.json` (repo root)
- `src/StateStore.MongoDb/StateStore.MongoDb.csproj`
- `src/StateStore.MongoDb/PACKAGE.md`
- `src/StateStore.MongoDb/MongoStorageProvider.cs` (moved)
- `src/StateStore.MongoDb/MongoStorageOptions.cs` (moved)
- `src/StateStore.MongoDb/StateStoreOptionsMongoExtensions.cs`
- `src/StateStore.Sqlite/StateStore.Sqlite.csproj`
- `src/StateStore.Sqlite/PACKAGE.md`
- `src/StateStore.Sqlite/SqliteStorageProvider.cs` (moved)
- `src/StateStore.Sqlite/SqliteStorageOptions.cs` (moved)
- `src/StateStore.Sqlite/StateStoreOptionsSqliteExtensions.cs`
- `samples/StateStore.Samples.QuickStart/StateStore.Samples.QuickStart.csproj`
- `samples/StateStore.Samples.QuickStart/Program.cs`
- `samples/StateStore.Samples.AspNetCoreDI/StateStore.Samples.AspNetCoreDI.csproj`
- `samples/StateStore.Samples.AspNetCoreDI/Program.cs`
- `samples/StateStore.Samples.MongoDb/StateStore.Samples.MongoDb.csproj`
- `samples/StateStore.Samples.MongoDb/Program.cs`
- `samples/StateStore.Samples.Sqlite/StateStore.Samples.Sqlite.csproj`
- `samples/StateStore.Samples.Sqlite/Program.cs`
- `docs/01-Getting-Started.md` (moved from `src/StateStore/.Docs/`)
- `docs/02-Core-Concepts.md` (moved)
- `docs/03-Basic-Usage.md` (moved)
- `docs/04-Typed-State-Store.md` (moved)
- `docs/05-Storage-Providers.md` (moved)
- `docs/06-Serialization.md` (moved)
- `docs/07-Middleware.md` (moved)
- `docs/08-Concurrency.md` (moved)
- `docs/09-Auto-Save.md` (moved)
- `docs/10-Dependency-Injection.md` (moved)
- `docs/11-Standalone-Usage.md` (moved)
- `docs/12-Error-Handling.md` (moved)
- `docs/13-Testing.md` (moved)
- `docs/14-Extensibility.md` (moved)

**Modified:**
- `Directory.Build.props` — add shared NuGet metadata + SourceLink config; remove `NuGetAuditSuppress` (moves to MongoDb csproj)
- `src/StateStore/StateStore.csproj` — strip duplicated metadata, drop `MongoDB.Driver`, update doc-pack glob; keep `InternalsVisibleTo`
- `src/StateStore/Options/StateStoreOptions.cs` — replace enum with `ConfigureProvider(...)` public method; update `UseInMemory`/`UseFileSystem` to use it; delete `StorageProviderKind` enum
- `src/StateStore/Extensions/ServiceCollectionExtensions.cs` — replace switch with delegate invocation + FileSystem fallback
- `src/StateStore/Builder/StateStoreBuilder.cs` (moved from `src/StateStore/`) — no code changes other than location
- `src/StateStore/Builder/StateStoreImplementation.cs` (moved) — fix duplicate `<summary>` block
- `src/StateStore/Builder/TypedStateStore.cs` (moved)
- `src/StateStore/PACKAGE.md` (renamed from `readme.md`)
- `tests/StateStore.Tests/StateStore.Tests.csproj` — add references to new provider packages
- `tests/StateStore.Tests/NullReferenceTests.cs` — ctor-based setup
- `tests/StateStore.Tests/EdgeCaseTests.cs` — ctor-based setup
- `StateStore.sln` — add new projects + `samples` solution folder
- `.gitignore` — add rules for `TestResults/`, `.vs/`, `*.csproj.user`, `*.diagsession`

**Deleted:**
- `src/StateStore/StateStoreBuilderProviderExtensions.cs`
- `src/StateStore/Providers/Mongo/` (empty after Mongo moves out)
- `src/StateStore/Providers/Sqlite/` (empty after Sqlite moves out)
- `src/StateStore/.Docs/` (empty after docs move out)
- `examples/ProviderUsageExamples.cs`
- `examples/` (empty after deletion)
- `StateStore.csproj.user` (if tracked; otherwise just ignored)
- `tests/StateStore.Benchmarks/Report20260227-1103.diagsession` (if tracked)
- Anything else under `TestResults/` and `.vs/` that's currently tracked

---

## Chunk 1: Foundations & housekeeping

This chunk establishes the repository foundations: pin SDK, hoist shared NuGet metadata, add SourceLink, audit/clean tracked artifacts, and move docs to the repo root. **No source code or public API changes.** Build must be green after every commit.

### Task 1.1: Pin the SDK with `global.json`

**Files:**
- Create: `global.json` (repo root)

**Context:** The build currently picks `10.0.300-preview` because it's the highest installed SDK, which fires `NETSDK1057: preview version of .NET` on every build. Pinning to a stable patch silences the warning and makes builds reproducible.

- [ ] **Step 1: Verify the stable SDK is installed**

Run: `dotnet --list-sdks`
Expected: output includes a line starting with `10.0.204` (or another stable 10.0.x patch). If only `10.0.300-preview` or `10.0.103` is installed, install `10.0.204` first or pick the latest stable patch shown.

- [ ] **Step 2: Create `global.json` at repo root**

```json
{
  "sdk": {
    "version": "10.0.204",
    "rollForward": "latestPatch",
    "allowPrerelease": false
  }
}
```

- [ ] **Step 3: Confirm the SDK pin takes effect**

Run: `dotnet --version`
Expected: `10.0.204` (or the patch you pinned). NOT a `-preview` version.

- [ ] **Step 4: Verify build is still clean**

Run: `dotnet build -c Debug --nologo 2>&1 | tail -5`
Expected: `Build succeeded.  0 Warning(s)  0 Error(s)`. The `NETSDK1057` preview-version notices are gone.

- [ ] **Step 5: Commit**

```bash
git add global.json
git commit -m "Pin SDK to 10.0.204 via global.json"
```

### Task 1.2: Audit and clean tracked build/IDE artifacts

**Files:**
- Modify: `.gitignore`
- Delete (from git): various tracked artifacts

**Context:** Several artifacts that should never be in version control are currently tracked: `tests/StateStore.Benchmarks/Report20260227-1103.diagsession`, plus likely some `TestResults/` contents, possibly `.vs/`, possibly `*.csproj.user`. Enumerate first, then add to `.gitignore`, then remove from the index.

- [ ] **Step 1: Enumerate currently-tracked stray artifacts**

Run: `git ls-files | rg "(TestResults|^\.vs/|csproj\.user|\.diagsession)"`
Expected: lists each tracked artifact. Note the output — those are the paths to `git rm --cached` in step 3.

- [ ] **Step 2: Add rules to `.gitignore`**

Open `.gitignore` and confirm these patterns exist (add any that are missing, near the existing `.vs/` block or appended at the bottom):

```gitignore
# Build/test outputs
TestResults/

# IDE
.vs/
*.csproj.user
*.user
*.suo

# VS diagnostic / profiling sessions
*.diagsession
*.psess
*.vsp
*.vspx
```

- [ ] **Step 3: Remove tracked artifacts from the index**

For each path enumerated in Step 1, run `git rm --cached <path>`. Example:
```bash
git rm --cached tests/StateStore.Benchmarks/Report20260227-1103.diagsession
git rm --cached src/StateStore/StateStore.csproj.user   # if tracked
# ...repeat for every path printed in Step 1
```
If Step 1 printed nothing, skip this step.

- [ ] **Step 4: Verify the working tree status**

Run: `git status --short`
Expected: each removed file appears as `D  <path>` (staged deletion). `.gitignore` appears as `M`.

- [ ] **Step 5: Verify build still passes**

Run: `dotnet build -c Debug --nologo 2>&1 | tail -3`
Expected: `Build succeeded.  0 Warning(s)  0 Error(s)`.

- [ ] **Step 6: Commit**

```bash
git add .gitignore
git commit -m "Untrack and ignore build/IDE artifacts

Removes TestResults/, .vs/, *.csproj.user, and *.diagsession files from
the index and adds matching .gitignore rules so they stay out going
forward."
```

### Task 1.3: Move docs out of the package folder

**Files:**
- Move: `src/StateStore/.Docs/*.md` → `docs/*.md` (14 files)
- Modify: `src/StateStore/StateStore.csproj` (update doc-pack glob)

**Context:** Docs currently live at `src/StateStore/.Docs/` — dot-prefix is hidden on Linux/Mac, oddly nested inside the package folder, and the NuGet pack glob has to use a relative path. Promote to repo-level `docs/`. The new pack glob is recursive with an explicit exclude for `docs/superpowers/` so design docs don't ship in the nupkg.

- [ ] **Step 1: Move docs with `git mv` (preserves blame)**

```bash
mkdir -p docs
git mv src/StateStore/.Docs/01-Getting-Started.md docs/01-Getting-Started.md
git mv src/StateStore/.Docs/02-Core-Concepts.md docs/02-Core-Concepts.md
git mv src/StateStore/.Docs/03-Basic-Usage.md docs/03-Basic-Usage.md
git mv src/StateStore/.Docs/04-Typed-State-Store.md docs/04-Typed-State-Store.md
git mv src/StateStore/.Docs/05-Storage-Providers.md docs/05-Storage-Providers.md
git mv src/StateStore/.Docs/06-Serialization.md docs/06-Serialization.md
git mv src/StateStore/.Docs/07-Middleware.md docs/07-Middleware.md
git mv src/StateStore/.Docs/08-Concurrency.md docs/08-Concurrency.md
git mv src/StateStore/.Docs/09-Auto-Save.md docs/09-Auto-Save.md
git mv src/StateStore/.Docs/10-Dependency-Injection.md docs/10-Dependency-Injection.md
git mv src/StateStore/.Docs/11-Standalone-Usage.md docs/11-Standalone-Usage.md
git mv src/StateStore/.Docs/12-Error-Handling.md docs/12-Error-Handling.md
git mv src/StateStore/.Docs/13-Testing.md docs/13-Testing.md
git mv src/StateStore/.Docs/14-Extensibility.md docs/14-Extensibility.md
```

- [ ] **Step 2: Verify the source `.Docs` folder is empty and remove it**

Run: `ls src/StateStore/.Docs 2>/dev/null`
Expected: empty (no files printed).
Run: `rmdir src/StateStore/.Docs` (rmdir refuses to delete non-empty dirs, so it's a safety check).

- [ ] **Step 3: Update the doc-pack glob in `src/StateStore/StateStore.csproj`**

Find this block (currently around lines 31-34):
```xml
<ItemGroup>
  <None Include="readme.md" Pack="true" PackagePath="docs/readme.md" />
  <None Include=".Docs\*.md" Pack="true" PackagePath="docs/" />
</ItemGroup>
```

Replace with:
```xml
<ItemGroup>
  <None Include="..\..\docs\**\*.md" Exclude="..\..\docs\superpowers\**\*.md" Pack="true" PackagePath="docs/" />
</ItemGroup>
```

(The `readme.md` entry is removed because Task 1.4 below renames `readme.md` to `PACKAGE.md` and the new `Directory.Build.props` will handle PACKAGE.md packing automatically via `PackageReadmeFile`. Task 1.4 runs before any pack, so this is safe.)

- [ ] **Step 4: Confirm the spec doc would not be packed (it lives under `docs/superpowers/`)**

Run: `ls docs/superpowers/specs/`
Expected: `2026-05-22-repo-restructure-design.md` is there. Good — that's the path the Exclude pattern targets.

- [ ] **Step 5: Verify build still passes**

Run: `dotnet build -c Debug --nologo 2>&1 | tail -3`
Expected: `Build succeeded.  0 Warning(s)  0 Error(s)`.

- [ ] **Step 6: Commit**

```bash
git add src/StateStore/StateStore.csproj
git commit -m "Move docs from src/StateStore/.Docs to repo-level docs/

Promotes the 14 numbered guides out of the package folder. Updates
the doc-pack glob to a recursive include with explicit exclude of
docs/superpowers/ so internal design specs never ship in the nupkg."
```

### Task 1.4: Rename package readme

**Files:**
- Move: `src/StateStore/readme.md` → `src/StateStore/PACKAGE.md`
- Modify: `src/StateStore/StateStore.csproj` (drop the explicit readme `<None>` entry; the shared props will handle it in Task 1.5)

**Context:** Disambiguates the package-specific readme from the new repo-root `README.md` we'll add in a later chunk. Convention used by AWS SDK, Polly, Refit, and others.

- [ ] **Step 1: Rename via git mv**

```bash
git mv src/StateStore/readme.md src/StateStore/PACKAGE.md
```

- [ ] **Step 2: Verify build still passes**

Run: `dotnet build -c Debug --nologo 2>&1 | tail -3`
Expected: `Build succeeded.  0 Warning(s)  0 Error(s)`. (The csproj's `<None Include="readme.md" ... />` reference was already removed in Task 1.3 step 3.)

- [ ] **Step 3: Commit**

```bash
git commit -m "Rename src/StateStore/readme.md to PACKAGE.md

Disambiguates the package-specific NuGet readme from the repo-root
README.md we will add later. Matches the convention used by Polly,
Refit, AWS SDK, and similar libraries."
```

### Task 1.5: Hoist shared NuGet metadata and add SourceLink config

**Files:**
- Modify: `Directory.Build.props` (add shared metadata + SourceLink)
- Modify: `src/StateStore/StateStore.csproj` (remove duplicated metadata)

**Context:** Today every NuGet-pack-able project would duplicate Authors/Company/License/RepositoryUrl. Hoisting them keeps each csproj minimal. Also adds SourceLink for stepable-into-our-source debugging, deterministic builds, and `.snupkg` symbol packages — standard for public NuGet packages.

- [ ] **Step 1: Update `Directory.Build.props`**

Replace its current contents with:

```xml
<Project>

  <PropertyGroup>
    <LangVersion>latest</LangVersion>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <EnforceCodeStyleInBuild>true</EnforceCodeStyleInBuild>
  </PropertyGroup>

  <!-- Shared NuGet metadata for all packable projects in this repo. -->
  <PropertyGroup>
    <Authors>Cadtastic Solutions</Authors>
    <Company>Cadtastic Solutions</Company>
    <Version>1.0.0</Version>
    <PackageLicenseExpression>MIT</PackageLicenseExpression>
    <RepositoryUrl>https://github.com/Cadtastic-Solutions/StateStore</RepositoryUrl>
    <RepositoryType>git</RepositoryType>
    <PackageProjectUrl>https://github.com/Cadtastic-Solutions/StateStore</PackageProjectUrl>
    <PackageReadmeFile>PACKAGE.md</PackageReadmeFile>
  </PropertyGroup>

  <!-- SourceLink + deterministic + symbols for Release builds. -->
  <PropertyGroup Condition="'$(Configuration)' == 'Release'">
    <GeneratePackageOnBuild>true</GeneratePackageOnBuild>
    <PublishRepositoryUrl>true</PublishRepositoryUrl>
    <EmbedUntrackedSources>true</EmbedUntrackedSources>
    <IncludeSymbols>true</IncludeSymbols>
    <SymbolPackageFormat>snupkg</SymbolPackageFormat>
    <DebugType>portable</DebugType>
    <ContinuousIntegrationBuild Condition="'$(GITHUB_ACTIONS)' == 'true'">true</ContinuousIntegrationBuild>
  </PropertyGroup>

  <ItemGroup Condition="'$(IsPackable)' != 'false' AND '$(Configuration)' == 'Release'">
    <PackageReference Include="Microsoft.SourceLink.GitHub" Version="8.0.0" PrivateAssets="all" />
    <None Include="$(MSBuildProjectDirectory)\PACKAGE.md" Pack="true" PackagePath="\" Condition="Exists('$(MSBuildProjectDirectory)\PACKAGE.md')" />
  </ItemGroup>

</Project>
```

Note: the `NuGetAuditSuppress` for GHSA-6c8g-7p36-r338 is intentionally **removed** here — it will be re-added with narrower scope in `StateStore.MongoDb.csproj` in Chunk 3. Until then, restore will fail. That's expected; Task 1.5 must be paired with the eventual MongoDB extraction to be on a green branch tip. Acceptable mid-chunk red — we'll restore green at the end of this chunk via a workaround (Step 3 below).

- [ ] **Step 2: Trim `src/StateStore/StateStore.csproj`**

Replace its current `<Project>` content with:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFrameworks>net8.0;net9.0;net10.0</TargetFrameworks>
    <GenerateDocumentationFile>true</GenerateDocumentationFile>
    <RootNamespace>StateStore</RootNamespace>
    <AssemblyName>StateStore</AssemblyName>
    <IsAotCompatible>true</IsAotCompatible>
    <PackageId>StateStore</PackageId>
    <Description>Flexible, testable state storage library for .NET supporting multiple providers and middleware.</Description>
    <PackageTags>state;storage;middleware;dotnet;testable</PackageTags>
  </PropertyGroup>

  <ItemGroup>
    <InternalsVisibleTo Include="StateStore.Tests" />
  </ItemGroup>

  <ItemGroup>
    <None Include="..\..\docs\**\*.md" Exclude="..\..\docs\superpowers\**\*.md" Pack="true" PackagePath="docs/" />
  </ItemGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.Extensions.DependencyInjection.Abstractions" Version="10.0.8" />
    <PackageReference Include="Microsoft.Extensions.Hosting.Abstractions" Version="10.0.8" />
    <PackageReference Include="Microsoft.Extensions.Logging.Abstractions" Version="10.0.8" />
    <PackageReference Include="Microsoft.Extensions.Options" Version="10.0.8" />
    <PackageReference Include="Microsoft.Data.Sqlite" Version="10.0.8" />
    <PackageReference Include="MongoDB.Driver" Version="3.8.1" />
  </ItemGroup>

</Project>
```

Note: `MongoDB.Driver` and `Microsoft.Data.Sqlite` PackageReferences **remain for now** — they'll be removed when the provider packages extract them in Chunks 3 and 4. Removing them now would break the build (the provider classes are still in `src/StateStore/Providers/`).

The `InternalsVisibleTo` item stays — required for `StateStore.Tests` to instantiate `internal sealed StateStoreImplementation` and `internal sealed MiddlewarePipeline`.

The package-level NuGet properties (`Authors`, `Company`, `Version`, `PackageLicenseExpression`, `RepositoryUrl`, `RepositoryType`, `PackageProjectUrl`, `PackageReadmeFile`) and the `GeneratePackageOnBuild`-on-Release toggle are now inherited from `Directory.Build.props`.

- [ ] **Step 3: Restore the SharpCompress suppress temporarily**

Without `NuGetAuditSuppress`, the `Release` config (which enables `GeneratePackageOnBuild`) would still try to restore and choke on the MongoDB.Driver SharpCompress transitive vuln. Add it back **to `src/StateStore/StateStore.csproj`** temporarily — Chunk 3 will move it to `StateStore.MongoDb.csproj`:

Insert just before `</Project>`:
```xml
  <ItemGroup>
    <!--
      Temporary: pinned here until Chunk 3 extracts MongoDB.Driver into
      StateStore.MongoDb. Suppresses GHSA-6c8g-7p36-r338 (SharpCompress
      path traversal in WriteToDirectory()) — vulnerable code path is
      unreachable through MongoDB.Driver's compression usage.
    -->
    <NuGetAuditSuppress Include="https://github.com/advisories/GHSA-6c8g-7p36-r338" />
  </ItemGroup>
```

- [ ] **Step 4: Verify Debug build is clean**

Run: `dotnet build -c Debug --nologo 2>&1 | tail -5`
Expected: `Build succeeded.  0 Warning(s)  0 Error(s)`.

- [ ] **Step 5: Verify Release pack works**

Run: `dotnet pack -c Release src/StateStore/StateStore.csproj --nologo 2>&1 | tail -10`
Expected: pack succeeds; produces `src/StateStore/bin/Release/StateStore.1.0.0.nupkg` and `src/StateStore/bin/Release/StateStore.1.0.0.snupkg`.

- [ ] **Step 6: Inspect the produced nupkg to confirm PACKAGE.md and docs are included**

Run: `unzip -l src/StateStore/bin/Release/StateStore.1.0.0.nupkg | rg -e "PACKAGE.md" -e "docs/"`
Expected: `PACKAGE.md` is at the package root; `docs/01-Getting-Started.md` through `docs/14-Extensibility.md` are listed; **no `docs/superpowers/` entries appear**.

(If `unzip` isn't available on Windows, use PowerShell: `Expand-Archive -Path src/StateStore/bin/Release/StateStore.1.0.0.nupkg -DestinationPath /tmp/pkg-inspect/ -Force; ls -R /tmp/pkg-inspect/` and inspect the result.)

- [ ] **Step 7: Run tests to confirm nothing is broken**

Run: `dotnet test --nologo 2>&1 | tail -5`
Expected: all tests pass (72 on top of the prior keyhelper branch).

- [ ] **Step 8: Commit**

```bash
git add Directory.Build.props src/StateStore/StateStore.csproj
git commit -m "Hoist shared NuGet metadata, add SourceLink and symbol packages

Authors/Company/License/RepositoryUrl/Version/PackageReadmeFile move to
Directory.Build.props so the three eventual package csprojs only set
PackageId/Description/PackageTags. Adds Microsoft.SourceLink.GitHub +
deterministic build + .snupkg symbol package generation for Release
builds. Preserves InternalsVisibleTo to StateStore.Tests in core csproj.

The SharpCompress NuGetAuditSuppress is temporarily restored to
StateStore.csproj; Chunk 3 will move it to StateStore.MongoDb.csproj
where it permanently belongs."
```

### Task 1.6: Verify Chunk 1 end-state

**Files:** none (verification only)

- [ ] **Step 1: Full Debug build clean**

Run: `dotnet build -c Debug --nologo 2>&1 | tail -3`
Expected: `Build succeeded.  0 Warning(s)  0 Error(s)`.

- [ ] **Step 2: Full Release build clean (this is the first time we've exercised the Release path with the new props)**

Run: `dotnet build -c Release --nologo 2>&1 | tail -3`
Expected: `Build succeeded.  0 Warning(s)  0 Error(s)`.

- [ ] **Step 2a: Confirm Benchmarks/Tests do not produce nupkgs**

Run: `find . -name "*.nupkg" -path "*/bin/Release/*" 2>/dev/null`
Expected: only `src/StateStore/bin/Release/StateStore.1.0.0.nupkg` (and matching `.snupkg`). If `StateStore.Benchmarks.1.0.0.nupkg` or `StateStore.Tests.1.0.0.nupkg` appears, the SDK default `<IsPackable>` is not auto-falsifying for `OutputType=Exe` or for the test SDK. Fix:

Add `<IsPackable>false</IsPackable>` to the `<PropertyGroup>` in:
- `tests/StateStore.Benchmarks/StateStore.Benchmarks.csproj`
- `tests/StateStore.Tests/StateStore.Tests.csproj`

Then re-run Step 2 + Step 2a; commit the csproj fix as a separate commit with message "Mark test and benchmark projects as non-packable" before continuing. If Step 2a is clean on the first run, skip the fix entirely.

- [ ] **Step 3: Tests pass**

Run: `dotnet test --nologo 2>&1 | tail -3`
Expected: `Passed!  - Failed: 0, Passed: 72, Skipped: 0, Total: 72`.

- [ ] **Step 4: Verify `docs/` is populated and `src/StateStore/.Docs` is gone**

Run: `ls docs | head` and `ls src/StateStore/.Docs 2>&1`
Expected: 14 numbered markdown files in `docs/`; the `.Docs` listing prints "No such file or directory".

- [ ] **Step 5: Verify `git log --follow` shows pre-move history for a moved file**

Run: `git log --oneline --follow -- docs/01-Getting-Started.md | head -5`
Expected: at least one commit, showing the file existed under its previous path. This confirms `git mv` was used (not delete + create).

---

## Chunk 2: Open the DI surface — `StateStoreOptions` refactor

This chunk replaces the closed `StorageProviderKind` enum with a public `ConfigureProvider(Action<IServiceCollection>)` method on `StateStoreOptions`, so external provider packages (added in Chunks 3 and 4) can plug in without needing internals access. The default-FileSystem-fallback behavior of `AddStateStore()` is preserved exactly. **This is a breaking API change**: `StorageProviderKind` and `StateStoreOptions.Provider` disappear. No external file moves yet.

### Task 2.1: Write failing tests for the new `ConfigureProvider` mechanism

**Files:**
- Modify: `tests/StateStore.Tests/StateStoreBuilderTests.cs` — verify current usages are unaffected
- Create: `tests/StateStore.Tests/StateStoreOptionsTests.cs`

**Context:** Three new test cases cover the contract:
1. `ConfigureProvider` is the seam — calling it sets the delegate that `AddStateStore` invokes.
2. `UseInMemory()` and `UseFileSystem()` (the built-in extension methods on `StateStoreOptions`) both go through `ConfigureProvider` and produce a working `IStorageProvider` registration.
3. `AddStateStore` with no body falls back to FileSystem with empty options — matching today's behavior.

- [ ] **Step 1: Look for any existing references to `StorageProviderKind` or `StateStoreOptions.Provider`**

Run: `git grep -nE "StorageProviderKind|options\.Provider" src/ tests/ samples/ 2>&1 || true`
Expected: the only matches should be inside `src/StateStore/Options/StateStoreOptions.cs` and `src/StateStore/Extensions/ServiceCollectionExtensions.cs`. If anything else appears (any test file, builder, etc.), note the path and line — those callers will need updates in Step 5 of Task 2.4 below.

- [ ] **Step 2: Create the new test file**

Create `tests/StateStore.Tests/StateStoreOptionsTests.cs` with:

```csharp
using Microsoft.Extensions.DependencyInjection;
using StateStore.Abstractions;
using StateStore.Extensions;
using StateStore.Options;
using StateStore.Providers.FileSystem;
using StateStore.Providers.InMemory;

namespace StateStore.Tests;

public sealed class StateStoreOptionsTests
{
    [Fact]
    public void ConfigureProvider_StoresRegistrationDelegate_AndAddStateStoreInvokesIt()
    {
        var services = new ServiceCollection();
        var sentinelInvoked = false;

        services.AddStateStore(o => o.ConfigureProvider(_ =>
        {
            sentinelInvoked = true;
        }));

        Assert.True(sentinelInvoked, "AddStateStore must invoke the delegate set by ConfigureProvider.");
    }

    [Fact]
    public void ConfigureProvider_ThrowsArgumentNullException_ForNullDelegate()
    {
        var options = new StateStoreOptions();
        Assert.Throws<ArgumentNullException>(() => options.ConfigureProvider(null!));
    }

    [Fact]
    public void UseInMemory_RegistersInMemoryStorageProvider()
    {
        var services = new ServiceCollection();
        services.AddStateStore(o => o.UseInMemory());

        var sp = services.BuildServiceProvider();
        var provider = sp.GetRequiredService<IStorageProvider>();

        Assert.IsType<InMemoryStorageProvider>(provider);
    }

    [Fact]
    public void UseFileSystem_RegistersFileSystemStorageProvider_AndAppliesOptions()
    {
        var services = new ServiceCollection();
        services.AddStateStore(o => o.UseFileSystem(fs =>
        {
            fs.BasePath = "/tmp/state-store-test";
            fs.FileExtension = ".dat";
        }));

        var sp = services.BuildServiceProvider();
        var provider = sp.GetRequiredService<IStorageProvider>();

        Assert.IsType<FileSystemStorageProvider>(provider);
    }

    [Fact]
    public void AddStateStore_WithoutAnyConfiguration_FallsBackToFileSystemWithEmptyOptions()
    {
        var services = new ServiceCollection();
        services.AddStateStore();

        var sp = services.BuildServiceProvider();
        var provider = sp.GetRequiredService<IStorageProvider>();

        // Falls back to FileSystem with default options — same as pre-refactor behavior.
        Assert.IsType<FileSystemStorageProvider>(provider);
    }

    [Fact]
    public void ConfigureProvider_LastCallWins()
    {
        var services = new ServiceCollection();
        services.AddStateStore(o => o
            .UseInMemory()
            .UseFileSystem());

        var sp = services.BuildServiceProvider();
        var provider = sp.GetRequiredService<IStorageProvider>();

        Assert.IsType<FileSystemStorageProvider>(provider);
    }
}
```

- [ ] **Step 3: Run the new tests to confirm they fail correctly**

Run: `dotnet test --filter "FullyQualifiedName~StateStoreOptionsTests" --nologo 2>&1 | tail -20`
Expected: all six tests fail. The exact failure message varies:
- `ConfigureProvider_StoresRegistrationDelegate_*`: `'StateStoreOptions' does not contain a definition for 'ConfigureProvider'` (compile error).
- The build will fail at compile-time on the missing method, so the test runner won't get to run individual cases — that's a valid RED signal. Confirm the compile error mentions `ConfigureProvider`.

### Task 2.2: Implement the `ConfigureProvider` method on `StateStoreOptions`

**Files:**
- Modify: `src/StateStore/Options/StateStoreOptions.cs`

**Context:** Add the public `ConfigureProvider` method and the internal `ProviderRegistration` accessor. Keep the existing `UseInMemory()` and `UseFileSystem()` methods structurally similar — they'll be rewritten in Task 2.3 to delegate through the new mechanism. Don't delete the enum yet — Task 2.5 does that.

- [ ] **Step 1: Add the field, public method, and internal accessor to `StateStoreOptions`**

Open `src/StateStore/Options/StateStoreOptions.cs` and add (just after the existing `_autoSaveConfigurations` field declaration):

```csharp
    private Action<IServiceCollection>? _configureProviderRegistration;
```

Add this method block before the closing brace of the `StateStoreOptions` class (immediately after `UseAutoSave`):

```csharp
    /// <summary>
    /// Registers the provider's services. Called by built-in and provider-package
    /// extension methods (UseInMemory, UseFileSystem, UseMongoDb, UseSqlite, ...).
    /// Each call replaces the previous registration — last call wins, matching the
    /// "single provider per state store" model.
    /// </summary>
    /// <param name="register">The action that registers the chosen <see cref="IStorageProvider"/> implementation.</param>
    /// <returns>This instance for chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="register"/> is <see langword="null"/>.</exception>
    public StateStoreOptions ConfigureProvider(Action<IServiceCollection> register)
    {
        ArgumentNullException.ThrowIfNull(register);
        _configureProviderRegistration = register;
        return this;
    }

    /// <summary>
    /// Gets the most-recently-configured provider registration delegate, if any.
    /// </summary>
    internal Action<IServiceCollection>? ProviderRegistration => _configureProviderRegistration;
```

Add this `using` near the top of the file (if not already present):
```csharp
using Microsoft.Extensions.DependencyInjection;
```

- [ ] **Step 2: Build and confirm only the runtime tests fail (compile passes)**

Run: `dotnet build --nologo 2>&1 | tail -5`
Expected: `Build succeeded.  0 Warning(s)  0 Error(s)`. The compile error from Task 2.1 is gone.

- [ ] **Step 3: Run the new tests — `ConfigureProvider_ThrowsArgumentNullException` and `ConfigureProvider_StoresRegistrationDelegate` should now pass; the others will still fail because `UseInMemory`/`UseFileSystem` haven't been rewired yet**

Run: `dotnet test --filter "FullyQualifiedName~StateStoreOptionsTests" --nologo 2>&1 | tail -25`
Expected:
- ✅ `ConfigureProvider_StoresRegistrationDelegate_AndAddStateStoreInvokesIt` — passes only AFTER Task 2.4 updates `AddStateStore` to invoke the delegate. May still fail here. Acceptable.
- ✅ `ConfigureProvider_ThrowsArgumentNullException_ForNullDelegate` — passes now.
- The remaining four tests likely still fail. Acceptable at this midpoint.

### Task 2.3: Rewire `UseInMemory()` and `UseFileSystem()` to call `ConfigureProvider`

**Files:**
- Modify: `src/StateStore/Options/StateStoreOptions.cs`

**Context:** Replace the body of the two existing methods. Capture `BasePath` / `FileExtension` into locals so the closure snapshots config at call-time, not at DI-build time (deliberate semantic tightening per spec).

- [ ] **Step 1: Add the `using` for the DI extensions (if not added in Task 2.2 Step 1)**

Open `src/StateStore/Options/StateStoreOptions.cs` and ensure these `using`s are at the top:
```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using StateStore.Abstractions;
using StateStore.Providers.FileSystem;
using StateStore.Providers.InMemory;
using StateStore.Serialization;
```

- [ ] **Step 2: Replace the body of `UseFileSystem`**

Find:
```csharp
    public StateStoreOptions UseFileSystem(Action<FileSystemStorageOptions>? configure = null)
    {
        Provider = StorageProviderKind.FileSystem;
        FileSystem ??= new FileSystemStorageOptions();
        configure?.Invoke(FileSystem);
        return this;
    }
```

Replace with:
```csharp
    public StateStoreOptions UseFileSystem(Action<FileSystemStorageOptions>? configure = null)
    {
        FileSystem ??= new FileSystemStorageOptions();
        configure?.Invoke(FileSystem);

        // Snapshot configured values into locals so the closure doesn't
        // see later mutations of the FileSystem options.
        var basePath = FileSystem.BasePath;
        var fileExtension = FileSystem.FileExtension;

        return ConfigureProvider(services =>
        {
            services.Configure<FileSystemStorageOptions>(o =>
            {
                o.BasePath = basePath;
                o.FileExtension = fileExtension;
            });
            services.TryAddSingleton<IStorageProvider, FileSystemStorageProvider>();
        });
    }
```

- [ ] **Step 3: Replace the body of `UseInMemory`**

Find:
```csharp
    public StateStoreOptions UseInMemory()
    {
        Provider = StorageProviderKind.InMemory;
        return this;
    }
```

Replace with:
```csharp
    public StateStoreOptions UseInMemory()
    {
        return ConfigureProvider(services =>
            services.TryAddSingleton<IStorageProvider, InMemoryStorageProvider>());
    }
```

- [ ] **Step 4: Build**

Run: `dotnet build --nologo 2>&1 | tail -5`
Expected: build succeeds. `StateStoreOptions.Provider` (the property) and `StorageProviderKind` still exist but are now unused — the compiler may emit no warning (they're public). That's expected; Task 2.5 removes them.

### Task 2.4: Rewire `AddStateStore` to use the new delegate

**Files:**
- Modify: `src/StateStore/Extensions/ServiceCollectionExtensions.cs`

**Context:** Replace the `switch (options.Provider)` block (lines ~57-71) with a single invocation of `options.ProviderRegistration` and a FileSystem fallback that preserves today's "AddStateStore() with no body works" behavior.

- [ ] **Step 1: Locate and replace the switch block**

Find this block in `src/StateStore/Extensions/ServiceCollectionExtensions.cs`:

```csharp
        // Register storage provider.
        switch (options.Provider)
        {
            case StorageProviderKind.InMemory:
                services.TryAddSingleton<IStorageProvider, InMemoryStorageProvider>();
                break;
            case StorageProviderKind.FileSystem:
            default:
                if (options.FileSystem is null)
                {
                    services.Configure<FileSystemStorageOptions>(_ => { });
                }

                services.TryAddSingleton<IStorageProvider, FileSystemStorageProvider>();
                break;
        }
```

Replace with:

```csharp
        // Register the configured storage provider, or fall back to FileSystem
        // with empty options to preserve "AddStateStore() with no body works".
        if (options.ProviderRegistration is not null)
        {
            options.ProviderRegistration(services);
        }
        else
        {
            services.Configure<FileSystemStorageOptions>(_ => { });
            services.TryAddSingleton<IStorageProvider, FileSystemStorageProvider>();
        }
```

- [ ] **Step 2: Remove the now-redundant `FileSystem` options `if` block above (the one at lines ~44-51)**

Find:
```csharp
        if (options.FileSystem is not null)
        {
            services.Configure<FileSystemStorageOptions>(o =>
            {
                o.BasePath = options.FileSystem.BasePath;
                o.FileExtension = options.FileSystem.FileExtension;
            });
        }
```

Delete it entirely. The new `UseFileSystem` (Task 2.3) does this work inside its registration delegate; running it again in `AddStateStore` is dead code.

- [ ] **Step 3: Build**

Run: `dotnet build --nologo 2>&1 | tail -5`
Expected: build succeeds.

- [ ] **Step 4: Run the new options tests — all six should pass now**

Run: `dotnet test --filter "FullyQualifiedName~StateStoreOptionsTests" --nologo 2>&1 | tail -10`
Expected: `Passed!  - Failed: 0, Passed: 6, Skipped: 0, Total: 6`.

- [ ] **Step 5: Run the full test suite to confirm no regressions**

Run: `dotnet test --nologo 2>&1 | tail -5`
Expected: `Passed!  - Failed: 0, Passed: 78, Skipped: 0, Total: 78` (72 prior + 6 new).

### Task 2.5: Delete the `StorageProviderKind` enum and the `Provider` property

**Files:**
- Modify: `src/StateStore/Options/StateStoreOptions.cs`

**Context:** Final breaking change. Now safe because `UseInMemory`/`UseFileSystem` no longer write to `Provider`, and `AddStateStore` no longer reads from it.

- [ ] **Step 1: Delete the `Provider` property**

Open `src/StateStore/Options/StateStoreOptions.cs` and remove:
```csharp
    /// <summary>
    /// Gets or sets the storage provider type to use. Defaults to <see cref="StorageProviderKind.FileSystem"/>.
    /// </summary>
    public StorageProviderKind Provider { get; set; } = StorageProviderKind.FileSystem;
```

- [ ] **Step 2: Delete the `StorageProviderKind` enum**

In the same file, remove the entire enum block:
```csharp
/// <summary>
/// Specifies the built-in storage provider to use.
/// </summary>
public enum StorageProviderKind
{
    /// <summary>
    /// File system-based storage provider.
    /// </summary>
    FileSystem,

    /// <summary>
    /// In-memory storage provider.
    /// </summary>
    InMemory
}
```

- [ ] **Step 3: Sweep for any stale references**

Run: `git grep -nE "StorageProviderKind|\.Provider\s*=" src/ tests/ samples/ 2>&1 || true`
Expected: zero matches. If Task 2.1 Step 1 flagged extra callers, they were updated by Task 2.3/2.4 already; this sweep is a safety check.

- [ ] **Step 4: Build**

Run: `dotnet build --nologo 2>&1 | tail -5`
Expected: build succeeds. If a reference was missed, the compile will fail with `'StorageProviderKind' could not be found` or `'StateStoreOptions' does not contain a definition for 'Provider'` — fix the offending caller and retry.

- [ ] **Step 5: Run all tests**

Run: `dotnet test --nologo 2>&1 | tail -5`
Expected: `Passed!  - Failed: 0, Passed: 78, Skipped: 0, Total: 78`.

- [ ] **Step 6: Commit (single commit for all of Chunk 2)**

```bash
git add src/StateStore/Options/StateStoreOptions.cs \
        src/StateStore/Extensions/ServiceCollectionExtensions.cs \
        tests/StateStore.Tests/StateStoreOptionsTests.cs
git commit -m "Open the DI surface with StateStoreOptions.ConfigureProvider

Replaces the closed StorageProviderKind enum with a public
ConfigureProvider(Action<IServiceCollection>) method on
StateStoreOptions, so external provider packages can plug into the
AddStateStore() DI registration without needing InternalsVisibleTo.

UseInMemory() and UseFileSystem() on StateStoreOptions now go through
ConfigureProvider; they snapshot their configuration into the closure
at call time so later mutations of options.FileSystem are not observed
(a deliberate semantic tightening).

AddStateStore preserves the prior 'no body needed' default by falling
back to FileSystem with empty options when no provider was configured.

Breaking changes (1.0.0-alpha is the right moment):
- StorageProviderKind enum removed.
- StateStoreOptions.Provider property removed.

Six new tests in StateStoreOptionsTests cover the contract, the
fallback, and last-call-wins semantics. All 78 tests pass."
```

### Task 2.6: Verify Chunk 2 end-state

- [ ] **Step 1: Debug build clean**

Run: `dotnet build -c Debug --nologo 2>&1 | tail -3`
Expected: `Build succeeded.  0 Warning(s)  0 Error(s)`.

- [ ] **Step 2: Release build clean**

Run: `dotnet build -c Release --nologo 2>&1 | tail -3`
Expected: `Build succeeded.  0 Warning(s)  0 Error(s)`.

- [ ] **Step 3: All tests pass**

Run: `dotnet test --nologo 2>&1 | tail -3`
Expected: `Passed!  - Failed: 0, Passed: 78, Skipped: 0, Total: 78`.

- [ ] **Step 4: `StorageProviderKind` is fully gone**

Run: `git grep -n "StorageProviderKind" 2>&1 || true`
Expected: zero matches in src/, tests/, samples/. (Confirmed absent from docs/ guides as of this chunk; if a future commit adds a reference, Chunk 8 picks it up.)

---

## Chunk 3: Extract `StateStore.MongoDb` package

Splits the MongoDB provider into its own NuGet package. After this chunk, `MongoDB.Driver` and the `SharpCompress` audit suppress live only in `StateStore.MongoDb.csproj`; the core `StateStore` package no longer pulls them.

The chunk is structured as a sequence of small atomic steps. The build goes red between Task 3.2 and Task 3.3 (during the file moves), and recovers at the end of Task 3.3. The single chunk-level commit happens after Task 3.7.

### Task 3.1: Scaffold `StateStore.MongoDb` (empty project)

**Files:**
- Create: `src/StateStore.MongoDb/StateStore.MongoDb.csproj`
- Create: `src/StateStore.MongoDb/PACKAGE.md`
- Modify: `StateStore.sln` (add the new project)

**Context:** The empty project compiles to an empty assembly. The Mongo source files arrive in Task 3.2.

- [ ] **Step 1: Create the csproj**

Create `src/StateStore.MongoDb/StateStore.MongoDb.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFrameworks>net8.0;net9.0;net10.0</TargetFrameworks>
    <GenerateDocumentationFile>true</GenerateDocumentationFile>
    <RootNamespace>StateStore.MongoDb</RootNamespace>
    <AssemblyName>StateStore.MongoDb</AssemblyName>
    <IsAotCompatible>true</IsAotCompatible>
    <PackageId>StateStore.MongoDb</PackageId>
    <Description>MongoDB storage provider for StateStore.</Description>
    <PackageTags>state;storage;mongodb;dotnet;testable</PackageTags>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="..\StateStore\StateStore.csproj" />
  </ItemGroup>

  <ItemGroup>
    <PackageReference Include="MongoDB.Driver" Version="3.8.1" />
  </ItemGroup>

  <ItemGroup>
    <!--
      CVE-2026-44788 / GHSA-6c8g-7p36-r338: path traversal in
      SharpCompress.WriteToDirectory(). Pulled transitively by
      MongoDB.Driver for wire-protocol compression. We never call
      WriteToDirectory(), so the vulnerable code path is unreachable.
      No patched SharpCompress version is available (all versions
      <= 0.47.4 affected). Re-evaluate when SharpCompress ships a fix.
    -->
    <NuGetAuditSuppress Include="https://github.com/advisories/GHSA-6c8g-7p36-r338" />
  </ItemGroup>

</Project>
```

- [ ] **Step 2: Create `PACKAGE.md`**

Create `src/StateStore.MongoDb/PACKAGE.md`:

```markdown
# StateStore.MongoDb

MongoDB-backed storage provider for [StateStore](https://github.com/Cadtastic-Solutions/StateStore).

## Install

    dotnet add package StateStore
    dotnet add package StateStore.MongoDb

## Quick start

```csharp
using StateStore;

builder.Services.AddStateStore(o => o
    .UseMongoDb(m =>
    {
        m.ConnectionString = "mongodb://localhost:27017";
        m.DatabaseName = "myapp";
    })
    .UseJsonSerializer());
```

Full documentation: <https://github.com/Cadtastic-Solutions/StateStore/tree/main/docs>.
```

- [ ] **Step 3: Register in the solution**

Run: `dotnet sln StateStore.sln add src/StateStore.MongoDb/StateStore.MongoDb.csproj --solution-folder src`
Expected: `Project ... added to the solution.`

- [ ] **Step 4: Verify build**

Run: `dotnet build --nologo 2>&1 | tail -5`
Expected: `Build succeeded.  0 Warning(s)  0 Error(s)`. The new project compiles to an empty assembly (no `.cs` files yet); the solution now contains four packable projects + two test/benchmark projects.

### Task 3.2: Move Mongo source files into the new package

**Files:**
- Move: `src/StateStore/Providers/Mongo/MongoStorageProvider.cs` → `src/StateStore.MongoDb/MongoStorageProvider.cs`
- Move: `src/StateStore/Options/MongoStorageOptions.cs` → `src/StateStore.MongoDb/MongoStorageOptions.cs`
- Modify both files: namespace updates + duplicate `<summary>` fix

**Context:** After this task, core's `StateStoreBuilderProviderExtensions.cs` will fail to compile (it still imports `StateStore.Providers.Mongo`). That's expected — Task 3.3 fixes it. Do not run `dotnet build` between Task 3.2 and Task 3.3.

- [ ] **Step 1: Move the provider file**

Run: `git mv src/StateStore/Providers/Mongo/MongoStorageProvider.cs src/StateStore.MongoDb/MongoStorageProvider.cs`

- [ ] **Step 2: Move the options file**

Run: `git mv src/StateStore/Options/MongoStorageOptions.cs src/StateStore.MongoDb/MongoStorageOptions.cs`

- [ ] **Step 3: Remove the now-empty `Providers/Mongo` directory**

Run: `rmdir src/StateStore/Providers/Mongo` (will fail if non-empty — that's a safety check).

- [ ] **Step 4: Update namespace and fix duplicate `<summary>` in `MongoStorageProvider.cs`**

Open `src/StateStore.MongoDb/MongoStorageProvider.cs`.

Replace the `using` block at the top and the namespace line. Find:
```csharp
using MongoDB.Bson;
using MongoDB.Driver;
using StateStore.Options;
using StateStore.Abstractions;

namespace StateStore.Providers.Mongo;
```

Replace with:
```csharp
using MongoDB.Bson;
using MongoDB.Driver;
using StateStore.Abstractions;

namespace StateStore.MongoDb;
```

(The `using StateStore.Options;` import goes away because `MongoStorageOptions` is now in the same `StateStore.MongoDb` namespace and resolves automatically.)

Then fix the duplicate `<summary>` block on the class. Find:
```csharp
/// <summary>
/// MongoDB-based storage provider for StateStore.
/// </summary>
/// <summary>
/// Provides a MongoDB-backed implementation of <see cref="IStorageProvider"/> for StateStore.
/// </summary>
public sealed class MongoStorageProvider : IStorageProvider
```

Replace with:
```csharp
/// <summary>
/// MongoDB-backed implementation of <see cref="IStorageProvider"/> for StateStore.
/// </summary>
public sealed class MongoStorageProvider : IStorageProvider
```

- [ ] **Step 5: Update namespace in `MongoStorageOptions.cs`**

Open `src/StateStore.MongoDb/MongoStorageOptions.cs`. Find:
```csharp
namespace StateStore.Options;
```

Replace with:
```csharp
namespace StateStore.MongoDb;
```

- [ ] **Step 6: Confirm the build is currently red (this is the expected mid-chunk state)**

Run: `dotnet build --nologo 2>&1 | tail -10`
Expected: build FAILS in `StateStore` core with errors like `The type or namespace name 'Mongo' does not exist in the namespace 'StateStore.Providers'` (from `StateStoreBuilderProviderExtensions.cs`). This is the planned transient state — Task 3.3 fixes it. Do not attempt to fix the errors here.

### Task 3.3: Remove Mongo references from core

**Files:**
- Modify: `src/StateStore/StateStoreBuilderProviderExtensions.cs`

**Context:** Strip the `UseMongo` methods and the `using StateStore.Providers.Mongo` line so core compiles again. SQLite stays in this file for now — Chunk 4 handles it.

- [ ] **Step 1: Trim `StateStoreBuilderProviderExtensions.cs`**

Replace the entire contents of `src/StateStore/StateStoreBuilderProviderExtensions.cs` with:

```csharp
using StateStore.Providers.Sqlite;
using StateStore.Options;

namespace StateStore;

/// <summary>
/// Extension methods for configuring StateStoreBuilder with additional storage providers.
/// </summary>
public static class StateStoreBuilderProviderExtensions
{
    /// <summary>
    /// Configures the builder to use a SQLite storage provider with the specified options.
    /// </summary>
    /// <param name="builder">The StateStoreBuilder instance.</param>
    /// <param name="options">The SQLite storage options.</param>
    /// <returns>The builder for chaining.</returns>
    public static StateStoreBuilder UseSqlite(this StateStoreBuilder builder, SqliteStorageOptions options)
    {
        return builder.UseProvider(new SqliteStorageProvider(options));
    }

    /// <summary>
    /// Configures the builder to use a SQLite storage provider with the specified connection string.
    /// </summary>
    /// <param name="builder">The StateStoreBuilder instance.</param>
    /// <param name="connectionString">The SQLite connection string.</param>
    /// <returns>The builder for chaining.</returns>
    public static StateStoreBuilder UseSqlite(this StateStoreBuilder builder, string connectionString)
    {
        return builder.UseProvider(new SqliteStorageProvider(connectionString));
    }
}
```

- [ ] **Step 2: Verify build is green again**

Run: `dotnet build --nologo 2>&1 | tail -5`
Expected: `Build succeeded.  0 Warning(s)  0 Error(s)`. `StateStore.MongoDb` builds with just the two source files; core no longer references Mongo types.

### Task 3.4: Remove MongoDB.Driver from core and drop the temporary suppress

**Files:**
- Modify: `src/StateStore/StateStore.csproj`

**Context:** Core no longer compiles any Mongo-related code, so it no longer needs `MongoDB.Driver` or the SharpCompress audit exception. Both move out — the `MongoDB.Driver` reference is simply removed (it now lives only in `StateStore.MongoDb.csproj`, which Task 3.1 set up), and the `NuGetAuditSuppress` placed temporarily in Chunk 1 Task 1.5 Step 3 is removed since the only project that needs it is `StateStore.MongoDb`.

- [ ] **Step 1: Remove `MongoDB.Driver` from `src/StateStore/StateStore.csproj`**

Find:
```xml
    <PackageReference Include="MongoDB.Driver" Version="3.8.1" />
```
Delete this line.

- [ ] **Step 2: Remove the temporary suppress `<ItemGroup>` from `src/StateStore/StateStore.csproj`**

Find and delete this entire block (added in Chunk 1 Task 1.5 Step 3):
```xml
  <ItemGroup>
    <!--
      Temporary: pinned here until Chunk 3 extracts MongoDB.Driver into
      StateStore.MongoDb. Suppresses GHSA-6c8g-7p36-r338 (SharpCompress
      path traversal in WriteToDirectory()) — vulnerable code path is
      unreachable through MongoDB.Driver's compression usage.
    -->
    <NuGetAuditSuppress Include="https://github.com/advisories/GHSA-6c8g-7p36-r338" />
  </ItemGroup>
```

- [ ] **Step 3: Verify restore and build**

Run: `dotnet restore --nologo 2>&1 | tail -10`
Expected: restore succeeds. The `NU1902` warning fires for `SharpCompress` in `StateStore.MongoDb` but is suppressed by that project's `NuGetAuditSuppress`. Core and Sqlite (still in core for now) restore cleanly because they don't pull SharpCompress.

Run: `dotnet build --nologo 2>&1 | tail -5`
Expected: `Build succeeded.  0 Warning(s)  0 Error(s)`.

### Task 3.5: Reference `StateStore.MongoDb` from the test project

**Files:**
- Modify: `tests/StateStore.Tests/StateStore.Tests.csproj`

**Context:** Tests need to instantiate `MongoStorageProvider` (already used implicitly via the move). Add a project reference.

- [ ] **Step 1: Add the project reference**

Run: `dotnet add tests/StateStore.Tests/StateStore.Tests.csproj reference src/StateStore.MongoDb/StateStore.MongoDb.csproj`
Expected: `Reference ... added to the project.`

- [ ] **Step 2: Verify build**

Run: `dotnet build --nologo 2>&1 | tail -5`
Expected: `Build succeeded.  0 Warning(s)  0 Error(s)`.

### Task 3.6: Write failing tests for `UseMongoDb` extension methods

**Files:**
- Create: `tests/StateStore.Tests/MongoDbExtensionsTests.cs`

**Context:** Two tests — the standalone builder path and the DI options path — exercise the new extension methods that Task 3.7 will create. Tests don't connect to a real Mongo instance; they just verify the provider type is wired up correctly (consistent with how the InMemory tests work).

- [ ] **Step 1: Create the test file**

Create `tests/StateStore.Tests/MongoDbExtensionsTests.cs`:

```csharp
using Microsoft.Extensions.DependencyInjection;
using StateStore.Abstractions;
using StateStore.Extensions;
using StateStore.MongoDb;

namespace StateStore.Tests;

public sealed class MongoDbExtensionsTests
{
    [Fact]
    public void UseMongoDb_OnStateStoreBuilder_WithOptions_RegistersMongoProvider()
    {
        var options = new MongoStorageOptions
        {
            ConnectionString = "mongodb://localhost:27017",
            DatabaseName = "test",
            CollectionName = "test",
        };

        var store = new StateStoreBuilder()
            .UseMongoDb(options)
            .UseJsonSerializer()
            .Build();

        Assert.NotNull(store);
    }

    [Fact]
    public void UseMongoDb_OnStateStoreBuilder_WithConnectionString_RegistersMongoProvider()
    {
        var store = new StateStoreBuilder()
            .UseMongoDb("mongodb://localhost:27017")
            .UseJsonSerializer()
            .Build();

        Assert.NotNull(store);
    }

    [Fact]
    public void UseMongoDb_OnStateStoreOptions_WithConfigureAction_RegistersMongoProvider()
    {
        var services = new ServiceCollection();
        services.AddStateStore(o => o
            .UseMongoDb(m =>
            {
                m.ConnectionString = "mongodb://localhost:27017";
                m.DatabaseName = "test";
                m.CollectionName = "test";
            })
            .UseJsonSerializer());

        var sp = services.BuildServiceProvider();
        var provider = sp.GetRequiredService<IStorageProvider>();

        Assert.IsType<MongoStorageProvider>(provider);
    }

    [Fact]
    public void UseMongoDb_OnStateStoreOptions_WithOptionsInstance_RegistersMongoProvider()
    {
        var options = new MongoStorageOptions
        {
            ConnectionString = "mongodb://localhost:27017",
            DatabaseName = "test",
            CollectionName = "test",
        };

        var services = new ServiceCollection();
        services.AddStateStore(o => o
            .UseMongoDb(options)
            .UseJsonSerializer());

        var sp = services.BuildServiceProvider();
        var provider = sp.GetRequiredService<IStorageProvider>();

        Assert.IsType<MongoStorageProvider>(provider);
    }
}
```

- [ ] **Step 2: Run the new tests to confirm they fail correctly**

Run: `dotnet test --filter "FullyQualifiedName~MongoDbExtensionsTests" --nologo 2>&1 | tail -10`
Expected: build fails with `'StateStoreBuilder' does not contain a definition for 'UseMongoDb'` and `'StateStoreOptions' does not contain a definition for 'UseMongoDb'`. Valid RED state.

### Task 3.7: Implement `UseMongoDb` extension methods

**Files:**
- Create: `src/StateStore.MongoDb/StateStoreOptionsMongoExtensions.cs`

**Context:** A single file in `StateStore.MongoDb` houses both extension method targets (`StateStoreBuilder` and `StateStoreOptions`). The namespace is `StateStore` (NOT `StateStore.MongoDb`) so consumers don't need a second `using` to see the extensions — a single `using StateStore;` already in their code finds them.

- [ ] **Step 1: Create the extensions file**

Create `src/StateStore.MongoDb/StateStoreOptionsMongoExtensions.cs`:

```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using StateStore.Abstractions;
using StateStore.MongoDb;
using StateStore.Options;

namespace StateStore;

/// <summary>
/// Extension methods that configure the MongoDB storage provider on
/// <see cref="StateStoreBuilder"/> (standalone) and <see cref="StateStoreOptions"/> (DI).
/// </summary>
public static class StateStoreOptionsMongoExtensions
{
    /// <summary>
    /// Configures the builder to use a MongoDB storage provider with the specified options.
    /// </summary>
    /// <param name="builder">The StateStoreBuilder instance.</param>
    /// <param name="options">The MongoDB storage options.</param>
    /// <returns>The builder for chaining.</returns>
    public static StateStoreBuilder UseMongoDb(this StateStoreBuilder builder, MongoStorageOptions options)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(options);
        return builder.UseProvider(new MongoStorageProvider(options));
    }

    /// <summary>
    /// Configures the builder to use a MongoDB storage provider with the specified connection string, database, and collection.
    /// </summary>
    /// <param name="builder">The StateStoreBuilder instance.</param>
    /// <param name="connectionString">The MongoDB connection string.</param>
    /// <param name="databaseName">The MongoDB database name. Defaults to "StateStore".</param>
    /// <param name="collectionName">The MongoDB collection name. Defaults to "StateStore".</param>
    /// <returns>The builder for chaining.</returns>
    public static StateStoreBuilder UseMongoDb(
        this StateStoreBuilder builder,
        string connectionString,
        string databaseName = "StateStore",
        string collectionName = "StateStore")
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.UseProvider(new MongoStorageProvider(connectionString, databaseName, collectionName));
    }

    /// <summary>
    /// Configures the DI registration to use a MongoDB storage provider with the specified options.
    /// </summary>
    /// <param name="options">The StateStoreOptions instance.</param>
    /// <param name="mongoOptions">The MongoDB storage options.</param>
    /// <returns>The options for chaining.</returns>
    public static StateStoreOptions UseMongoDb(this StateStoreOptions options, MongoStorageOptions mongoOptions)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(mongoOptions);

        // Snapshot config at call time so later mutations of mongoOptions aren't observed.
        var connectionString = mongoOptions.ConnectionString;
        var databaseName = mongoOptions.DatabaseName;
        var collectionName = mongoOptions.CollectionName;

        return options.ConfigureProvider(services =>
            services.TryAddSingleton<IStorageProvider>(_ =>
                new MongoStorageProvider(connectionString, databaseName, collectionName)));
    }

    /// <summary>
    /// Configures the DI registration to use a MongoDB storage provider, configured via an action.
    /// </summary>
    /// <param name="options">The StateStoreOptions instance.</param>
    /// <param name="configure">An action to configure <see cref="MongoStorageOptions"/>.</param>
    /// <returns>The options for chaining.</returns>
    public static StateStoreOptions UseMongoDb(this StateStoreOptions options, Action<MongoStorageOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(configure);

        var mongoOptions = new MongoStorageOptions();
        configure(mongoOptions);

        return options.UseMongoDb(mongoOptions);
    }
}
```

- [ ] **Step 2: Build**

Run: `dotnet build --nologo 2>&1 | tail -5`
Expected: `Build succeeded.  0 Warning(s)  0 Error(s)`.

- [ ] **Step 3: Run the new tests**

Run: `dotnet test --filter "FullyQualifiedName~MongoDbExtensionsTests" --nologo 2>&1 | tail -10`
Expected: `Passed!  - Failed: 0, Passed: 4, Skipped: 0, Total: 4`.

- [ ] **Step 4: Run full test suite to confirm nothing regressed**

Run: `dotnet test --nologo 2>&1 | tail -3`
Expected: `Passed!  - Failed: 0, Passed: 82, Skipped: 0, Total: 82` (78 prior + 4 new).

- [ ] **Step 5: Commit the entire chunk**

```bash
git add src/StateStore.MongoDb \
        src/StateStore/StateStore.csproj \
        src/StateStore/StateStoreBuilderProviderExtensions.cs \
        tests/StateStore.Tests/StateStore.Tests.csproj \
        tests/StateStore.Tests/MongoDbExtensionsTests.cs \
        StateStore.sln
git commit -m "Extract StateStore.MongoDb provider package

Splits the MongoDB provider into a standalone NuGet package. Consumers
who don't need MongoDB no longer transitively pull MongoDB.Driver or
SharpCompress.

- Creates src/StateStore.MongoDb/ with its own csproj, PACKAGE.md,
  MongoStorageProvider, MongoStorageOptions, and the
  StateStoreOptionsMongoExtensions class.
- Moves MongoDB.Driver PackageReference from core to the new package.
- Moves the GHSA-6c8g-7p36-r338 NuGetAuditSuppress from core to the
  new package (only project that pulls SharpCompress).
- Renames UseMongo(...) to UseMongoDb(...) matching the package name
  and MongoDB.Driver's product casing.
- Adds new DI-path overloads: UseMongoDb on StateStoreOptions for
  AddStateStore(o => o.UseMongoDb(...)) — previously not possible
  with the closed StorageProviderKind enum.
- Strips the Mongo-related methods from
  StateStoreBuilderProviderExtensions.cs (Sqlite still lives there;
  Chunk 4 splits it out next).
- Test project picks up a ProjectReference to the new package; four
  new tests in MongoDbExtensionsTests cover both extension surfaces."
```

### Task 3.8: Verify Chunk 3 end-state

- [ ] **Step 1: Debug build clean**

Run: `dotnet build -c Debug --nologo 2>&1 | tail -3`
Expected: `Build succeeded.  0 Warning(s)  0 Error(s)`.

- [ ] **Step 2: Release pack produces two packages so far**

Run: `dotnet pack -c Release --nologo 2>&1 | tail -5` (clears prior `bin/Release` first if needed: `find . -path "*/bin/Release/*.nupkg" -delete 2>/dev/null`)

Then: `find . -name "*.nupkg" -path "*/bin/Release/*" 2>/dev/null | sort`
Expected: only `src/StateStore/bin/Release/StateStore.1.0.0.nupkg` and `src/StateStore.MongoDb/bin/Release/StateStore.MongoDb.1.0.0.nupkg` (plus matching `.snupkg` files). Confirm `StateStore.Tests.nupkg`, `StateStore.Benchmarks.nupkg`, and (Sqlite stays bundled in core until Chunk 4) `StateStore.Sqlite.nupkg` are NOT produced.

- [ ] **Step 3: All tests pass**

Run: `dotnet test --nologo 2>&1 | tail -3`
Expected: `Passed!  - Failed: 0, Passed: 82, Skipped: 0, Total: 82`.

- [ ] **Step 4: `git log --follow` shows pre-move history for the moved Mongo files**

Run: `git log --oneline --follow -- src/StateStore.MongoDb/MongoStorageProvider.cs | head -5`
Expected: at least one commit pre-dating the move. Confirms `git mv` was used.

- [ ] **Step 5: Confirm `MongoDB.Driver` no longer appears as a dep in the core nupkg**

Run: `unzip -p src/StateStore/bin/Release/StateStore.1.0.0.nupkg StateStore.nuspec | rg -e "MongoDB" -e "SharpCompress" || echo "clean"`
Expected: prints `clean` (no MongoDB or SharpCompress references in the core nuspec).

(PowerShell fallback if `unzip` isn't available: `Expand-Archive -Path src/StateStore/bin/Release/StateStore.1.0.0.nupkg -DestinationPath /tmp/pkg-inspect/ -Force; Select-String -Path /tmp/pkg-inspect/StateStore.nuspec -Pattern "MongoDB|SharpCompress"`.)

---

## Chunk 4: Extract `StateStore.Sqlite` package

Mirror of Chunk 3, structured the same way. After this chunk, `StateStoreBuilderProviderExtensions.cs` is empty and gets deleted; `Microsoft.Data.Sqlite` moves out of core.

### Task 4.1: Scaffold `StateStore.Sqlite` (empty project)

**Files:**
- Create: `src/StateStore.Sqlite/StateStore.Sqlite.csproj`
- Create: `src/StateStore.Sqlite/PACKAGE.md`
- Modify: `StateStore.sln`

- [ ] **Step 1: Create the csproj**

Create `src/StateStore.Sqlite/StateStore.Sqlite.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFrameworks>net8.0;net9.0;net10.0</TargetFrameworks>
    <GenerateDocumentationFile>true</GenerateDocumentationFile>
    <RootNamespace>StateStore.Sqlite</RootNamespace>
    <AssemblyName>StateStore.Sqlite</AssemblyName>
    <IsAotCompatible>true</IsAotCompatible>
    <PackageId>StateStore.Sqlite</PackageId>
    <Description>SQLite storage provider for StateStore.</Description>
    <PackageTags>state;storage;sqlite;dotnet;testable</PackageTags>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="..\StateStore\StateStore.csproj" />
  </ItemGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.Data.Sqlite" Version="10.0.8" />
  </ItemGroup>

</Project>
```

- [ ] **Step 2: Create `PACKAGE.md`**

Create `src/StateStore.Sqlite/PACKAGE.md`:

```markdown
# StateStore.Sqlite

SQLite-backed storage provider for [StateStore](https://github.com/Cadtastic-Solutions/StateStore).

## Install

    dotnet add package StateStore
    dotnet add package StateStore.Sqlite

## Quick start

```csharp
using StateStore;

builder.Services.AddStateStore(o => o
    .UseSqlite(s => s.ConnectionString = "Data Source=state.db")
    .UseJsonSerializer());
```

Full documentation: <https://github.com/Cadtastic-Solutions/StateStore/tree/main/docs>.
```

- [ ] **Step 3: Register in the solution**

Run: `dotnet sln StateStore.sln add src/StateStore.Sqlite/StateStore.Sqlite.csproj --solution-folder src`
Expected: `Project ... added to the solution.`

- [ ] **Step 4: Verify build**

Run: `dotnet build --nologo 2>&1 | tail -5`
Expected: `Build succeeded.  0 Warning(s)  0 Error(s)`.

### Task 4.2: Move SQLite source files into the new package

**Files:**
- Move: `src/StateStore/Providers/Sqlite/SqliteStorageProvider.cs` → `src/StateStore.Sqlite/SqliteStorageProvider.cs`
- Move: `src/StateStore/Options/SqliteStorageOptions.cs` → `src/StateStore.Sqlite/SqliteStorageOptions.cs`

**Context:** Same red-build window as Chunk 3 Task 3.2. Task 4.3 fixes it. Do not build between Task 4.2 and Task 4.3.

- [ ] **Step 1: Move the provider file**

Run: `git mv src/StateStore/Providers/Sqlite/SqliteStorageProvider.cs src/StateStore.Sqlite/SqliteStorageProvider.cs`

- [ ] **Step 2: Move the options file**

Run: `git mv src/StateStore/Options/SqliteStorageOptions.cs src/StateStore.Sqlite/SqliteStorageOptions.cs`

- [ ] **Step 3: Remove the now-empty `Providers/Sqlite` directory**

Run: `rmdir src/StateStore/Providers/Sqlite` (safety check — refuses if non-empty).

- [ ] **Step 4: Update namespace in `SqliteStorageProvider.cs`**

Open `src/StateStore.Sqlite/SqliteStorageProvider.cs`. Find:
```csharp
namespace StateStore.Providers.Sqlite;
```

Replace with:
```csharp
namespace StateStore.Sqlite;
```

Then check the `using` block at the top of the file — if `using StateStore.Options;` is present (the file may import `SqliteStorageOptions` that way), delete it. The options class is now in the same `StateStore.Sqlite` namespace and resolves automatically.

Then fix the duplicate `<summary>` block on the class (same pattern as Mongo in Chunk 3 Task 3.2 Step 4). Find:
```csharp
/// <summary>
/// SQLite-based storage provider for StateStore.
/// </summary>
/// <summary>
/// Provides a SQLite-backed implementation of <see cref="IStorageProvider"/> for StateStore.
/// </summary>
public sealed class SqliteStorageProvider : IStorageProvider
```

Replace with:
```csharp
/// <summary>
/// SQLite-backed implementation of <see cref="IStorageProvider"/> for StateStore.
/// </summary>
public sealed class SqliteStorageProvider : IStorageProvider
```

- [ ] **Step 5: Update namespace in `SqliteStorageOptions.cs`**

Open `src/StateStore.Sqlite/SqliteStorageOptions.cs`. Find:
```csharp
namespace StateStore.Options;
```

Replace with:
```csharp
namespace StateStore.Sqlite;
```

- [ ] **Step 6: Confirm the build is currently red (expected transient state)**

Run: `dotnet build --nologo 2>&1 | tail -10`
Expected: build FAILS in `StateStore` core because `StateStoreBuilderProviderExtensions.cs` still references `StateStore.Providers.Sqlite`. Acceptable — Task 4.3 fixes.

### Task 4.3: Delete `StateStoreBuilderProviderExtensions.cs` from core

**Files:**
- Delete: `src/StateStore/StateStoreBuilderProviderExtensions.cs`

**Context:** After Chunk 3 removed the Mongo methods, this file only contained SQLite extensions. With SQLite extracted, the file is empty/redundant and goes away entirely. The `UseSqlite` extensions move into `StateStore.Sqlite` in Task 4.7.

- [ ] **Step 1: Delete the file**

Run: `git rm src/StateStore/StateStoreBuilderProviderExtensions.cs`

- [ ] **Step 2: Verify core builds again**

Run: `dotnet build --nologo 2>&1 | tail -5`
Expected: `Build succeeded.  0 Warning(s)  0 Error(s)`. Core no longer references SQLite types or `StateStore.Providers.Sqlite`. `StateStore.Sqlite` package compiles with its two source files.

### Task 4.4: Remove `Microsoft.Data.Sqlite` from core

**Files:**
- Modify: `src/StateStore/StateStore.csproj`

- [ ] **Step 1: Remove the package reference**

Find:
```xml
    <PackageReference Include="Microsoft.Data.Sqlite" Version="10.0.8" />
```
Delete this line.

- [ ] **Step 2: Verify restore and build**

Run: `dotnet restore --nologo 2>&1 | tail -5`
Expected: restore succeeds with no warnings on the core project.

Run: `dotnet build --nologo 2>&1 | tail -5`
Expected: `Build succeeded.  0 Warning(s)  0 Error(s)`.

### Task 4.5: Reference `StateStore.Sqlite` from the test project

**Files:**
- Modify: `tests/StateStore.Tests/StateStore.Tests.csproj`

- [ ] **Step 1: Add the project reference**

Run: `dotnet add tests/StateStore.Tests/StateStore.Tests.csproj reference src/StateStore.Sqlite/StateStore.Sqlite.csproj`

- [ ] **Step 2: Verify build**

Run: `dotnet build --nologo 2>&1 | tail -5`
Expected: `Build succeeded.  0 Warning(s)  0 Error(s)`.

### Task 4.6: Write failing tests for `UseSqlite` extension methods

**Files:**
- Create: `tests/StateStore.Tests/SqliteExtensionsTests.cs`

- [ ] **Step 1: Create the test file**

Create `tests/StateStore.Tests/SqliteExtensionsTests.cs`:

```csharp
using Microsoft.Extensions.DependencyInjection;
using StateStore.Abstractions;
using StateStore.Extensions;
using StateStore.Sqlite;

namespace StateStore.Tests;

public sealed class SqliteExtensionsTests
{
    [Fact]
    public void UseSqlite_OnStateStoreBuilder_WithOptions_RegistersSqliteProvider()
    {
        var options = new SqliteStorageOptions
        {
            ConnectionString = "Data Source=:memory:",
        };

        var store = new StateStoreBuilder()
            .UseSqlite(options)
            .UseJsonSerializer()
            .Build();

        Assert.NotNull(store);
    }

    [Fact]
    public void UseSqlite_OnStateStoreBuilder_WithConnectionString_RegistersSqliteProvider()
    {
        var store = new StateStoreBuilder()
            .UseSqlite("Data Source=:memory:")
            .UseJsonSerializer()
            .Build();

        Assert.NotNull(store);
    }

    [Fact]
    public void UseSqlite_OnStateStoreOptions_WithConfigureAction_RegistersSqliteProvider()
    {
        var services = new ServiceCollection();
        services.AddStateStore(o => o
            .UseSqlite(s => s.ConnectionString = "Data Source=:memory:")
            .UseJsonSerializer());

        var sp = services.BuildServiceProvider();
        var provider = sp.GetRequiredService<IStorageProvider>();

        Assert.IsType<SqliteStorageProvider>(provider);
    }

    [Fact]
    public void UseSqlite_OnStateStoreOptions_WithOptionsInstance_RegistersSqliteProvider()
    {
        var options = new SqliteStorageOptions
        {
            ConnectionString = "Data Source=:memory:",
        };

        var services = new ServiceCollection();
        services.AddStateStore(o => o
            .UseSqlite(options)
            .UseJsonSerializer());

        var sp = services.BuildServiceProvider();
        var provider = sp.GetRequiredService<IStorageProvider>();

        Assert.IsType<SqliteStorageProvider>(provider);
    }
}
```

- [ ] **Step 2: Run the new tests to confirm they fail correctly**

Run: `dotnet test --filter "FullyQualifiedName~SqliteExtensionsTests" --nologo 2>&1 | tail -10`
Expected: build fails with `'StateStoreBuilder' does not contain a definition for 'UseSqlite'` and `'StateStoreOptions' does not contain a definition for 'UseSqlite'`. Valid RED state.

### Task 4.7: Implement `UseSqlite` extension methods

**Files:**
- Create: `src/StateStore.Sqlite/StateStoreOptionsSqliteExtensions.cs`

- [ ] **Step 1: Create the extensions file**

Create `src/StateStore.Sqlite/StateStoreOptionsSqliteExtensions.cs`:

```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using StateStore.Abstractions;
using StateStore.Options;
using StateStore.Sqlite;

namespace StateStore;

/// <summary>
/// Extension methods that configure the SQLite storage provider on
/// <see cref="StateStoreBuilder"/> (standalone) and <see cref="StateStoreOptions"/> (DI).
/// </summary>
public static class StateStoreOptionsSqliteExtensions
{
    /// <summary>
    /// Configures the builder to use a SQLite storage provider with the specified options.
    /// </summary>
    /// <param name="builder">The StateStoreBuilder instance.</param>
    /// <param name="options">The SQLite storage options.</param>
    /// <returns>The builder for chaining.</returns>
    public static StateStoreBuilder UseSqlite(this StateStoreBuilder builder, SqliteStorageOptions options)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(options);
        return builder.UseProvider(new SqliteStorageProvider(options));
    }

    /// <summary>
    /// Configures the builder to use a SQLite storage provider with the specified connection string.
    /// </summary>
    /// <param name="builder">The StateStoreBuilder instance.</param>
    /// <param name="connectionString">The SQLite connection string.</param>
    /// <returns>The builder for chaining.</returns>
    public static StateStoreBuilder UseSqlite(this StateStoreBuilder builder, string connectionString)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.UseProvider(new SqliteStorageProvider(connectionString));
    }

    /// <summary>
    /// Configures the DI registration to use a SQLite storage provider with the specified options.
    /// </summary>
    /// <param name="options">The StateStoreOptions instance.</param>
    /// <param name="sqliteOptions">The SQLite storage options.</param>
    /// <returns>The options for chaining.</returns>
    public static StateStoreOptions UseSqlite(this StateStoreOptions options, SqliteStorageOptions sqliteOptions)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(sqliteOptions);

        // Snapshot config at call time so later mutations of sqliteOptions aren't observed.
        var connectionString = sqliteOptions.ConnectionString;

        return options.ConfigureProvider(services =>
            services.TryAddSingleton<IStorageProvider>(_ =>
                new SqliteStorageProvider(connectionString)));
    }

    /// <summary>
    /// Configures the DI registration to use a SQLite storage provider, configured via an action.
    /// </summary>
    /// <param name="options">The StateStoreOptions instance.</param>
    /// <param name="configure">An action to configure <see cref="SqliteStorageOptions"/>.</param>
    /// <returns>The options for chaining.</returns>
    public static StateStoreOptions UseSqlite(this StateStoreOptions options, Action<SqliteStorageOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(configure);

        var sqliteOptions = new SqliteStorageOptions();
        configure(sqliteOptions);

        return options.UseSqlite(sqliteOptions);
    }
}
```

- [ ] **Step 2: Build**

Run: `dotnet build --nologo 2>&1 | tail -5`
Expected: `Build succeeded.  0 Warning(s)  0 Error(s)`.

- [ ] **Step 3: Run the new Sqlite tests**

Run: `dotnet test --filter "FullyQualifiedName~SqliteExtensionsTests" --nologo 2>&1 | tail -10`
Expected: `Passed!  - Failed: 0, Passed: 4, Skipped: 0, Total: 4`.

- [ ] **Step 4: Run full suite**

Run: `dotnet test --nologo 2>&1 | tail -3`
Expected: `Passed!  - Failed: 0, Passed: 86, Skipped: 0, Total: 86` (82 prior + 4 new).

- [ ] **Step 5: Commit the chunk**

```bash
git add src/StateStore.Sqlite \
        src/StateStore/StateStore.csproj \
        src/StateStore/StateStoreBuilderProviderExtensions.cs \
        tests/StateStore.Tests/StateStore.Tests.csproj \
        tests/StateStore.Tests/SqliteExtensionsTests.cs \
        StateStore.sln
git commit -m "Extract StateStore.Sqlite provider package

Splits the SQLite provider into a standalone NuGet package. Consumers
who don't need SQLite no longer transitively pull Microsoft.Data.Sqlite.

- Creates src/StateStore.Sqlite/ with its own csproj, PACKAGE.md,
  SqliteStorageProvider, SqliteStorageOptions, and the
  StateStoreOptionsSqliteExtensions class.
- Moves Microsoft.Data.Sqlite PackageReference from core to the new
  package.
- Adds new DI-path overloads: UseSqlite on StateStoreOptions for
  AddStateStore(o => o.UseSqlite(...)) — previously not possible.
- Deletes src/StateStore/StateStoreBuilderProviderExtensions.cs (now
  empty after Chunks 3 and 4 extracted both providers).
- Test project picks up a ProjectReference to the new package; four
  new tests in SqliteExtensionsTests cover both extension surfaces."
```

### Task 4.8: Verify Chunk 4 end-state

- [ ] **Step 1: Debug build clean**

Run: `dotnet build -c Debug --nologo 2>&1 | tail -3`
Expected: `Build succeeded.  0 Warning(s)  0 Error(s)`.

- [ ] **Step 2: Release pack produces exactly three packages**

Run: `find . -name "*.nupkg" -path "*/bin/Release/*" -delete 2>/dev/null; dotnet pack -c Release --nologo 2>&1 | tail -3 && find . -name "*.nupkg" -path "*/bin/Release/*" 2>/dev/null | sort`
Expected:
```
./src/StateStore.MongoDb/bin/Release/StateStore.MongoDb.1.0.0.nupkg
./src/StateStore.Sqlite/bin/Release/StateStore.Sqlite.1.0.0.nupkg
./src/StateStore/bin/Release/StateStore.1.0.0.nupkg
```
plus three matching `.snupkg` files. Confirm no `Benchmarks` or `Tests` nupkgs.

- [ ] **Step 3: All tests pass**

Run: `dotnet test --nologo 2>&1 | tail -3`
Expected: `Passed!  - Failed: 0, Passed: 86, Skipped: 0, Total: 86`.

- [ ] **Step 4: Confirm core nupkg has no Mongo or Sqlite deps**

Run: `unzip -p src/StateStore/bin/Release/StateStore.1.0.0.nupkg StateStore.nuspec | rg -e "MongoDB" -e "Microsoft.Data.Sqlite" -e "SharpCompress" || echo "clean"`
Expected: prints `clean`.

(PowerShell fallback: `Expand-Archive -Path src/StateStore/bin/Release/StateStore.1.0.0.nupkg -DestinationPath /tmp/pkg-inspect2/ -Force; Select-String -Path /tmp/pkg-inspect2/StateStore.nuspec -Pattern "MongoDB|Microsoft.Data.Sqlite|SharpCompress"`.)

- [ ] **Step 5: Confirm `StateStoreBuilderProviderExtensions` is gone**

Run: `git ls-files | rg "StateStoreBuilderProviderExtensions"`
Expected: zero matches.

- [ ] **Step 6: Confirm empty provider directories are gone**

Run: `ls src/StateStore/Providers/ 2>&1`
Expected: only `FileSystem` and `InMemory` subdirectories remain.

---

## Chunk 5: Core file reorganization

Move the loose top-level files in `src/StateStore/` into a new `Builder/` folder, and fix the remaining duplicate `<summary>` block in `StateStoreImplementation.cs`. All three files keep `namespace StateStore;` so no consumer-facing changes — purely a folder rearrangement to give the project root a cleaner shape.

### Task 5.1: Move loose files into `Builder/`

**Files:**
- Move: `src/StateStore/StateStoreBuilder.cs` → `src/StateStore/Builder/StateStoreBuilder.cs`
- Move: `src/StateStore/StateStoreImplementation.cs` → `src/StateStore/Builder/StateStoreImplementation.cs`
- Move: `src/StateStore/TypedStateStore.cs` → `src/StateStore/Builder/TypedStateStore.cs`

**Context:** SDK-style csproj auto-includes all `.cs` files under the project root, so the moves are transparent to the build. Namespaces are unchanged. The `Builder/` folder convention matches how the spec layout shows it.

- [ ] **Step 1: Create the folder and move the three files**

```bash
mkdir -p src/StateStore/Builder
git mv src/StateStore/StateStoreBuilder.cs src/StateStore/Builder/StateStoreBuilder.cs
git mv src/StateStore/StateStoreImplementation.cs src/StateStore/Builder/StateStoreImplementation.cs
git mv src/StateStore/TypedStateStore.cs src/StateStore/Builder/TypedStateStore.cs
```

- [ ] **Step 2: Verify build**

Run: `dotnet build --nologo 2>&1 | tail -5`
Expected: `Build succeeded.  0 Warning(s)  0 Error(s)`. (Namespaces unchanged; SDK glob picks the files up at their new path.)

### Task 5.2: Fix duplicate `<summary>` block on `StateStoreImplementation`

**Files:**
- Modify: `src/StateStore/Builder/StateStoreImplementation.cs`

**Context:** The class has two `<summary>` blocks with identical content at lines 10-13 and 14-17, plus a `<remarks>` block at 18-20. Merge to a single `<summary>` and keep the `<remarks>` intact.

- [ ] **Step 1: Replace the doc-comment block**

Open `src/StateStore/Builder/StateStoreImplementation.cs`. Find:

```csharp
/// <summary>
/// Core implementation of <see cref="IStateStore"/> that coordinates serialization,
/// middleware pipeline, concurrency control, and storage provider access.
/// </summary>
/// <summary>
/// Core implementation of <see cref="IStateStore"/> that coordinates serialization,
/// middleware pipeline, concurrency control, and storage provider access.
/// </summary>
/// <remarks>
/// <b>Thread Safety:</b> All public methods are thread-safe. Compound operations (such as upsert) are atomic per key.
/// </remarks>
internal sealed class StateStoreImplementation : IStateStore
```

Replace with:

```csharp
/// <summary>
/// Core implementation of <see cref="IStateStore"/> that coordinates serialization,
/// middleware pipeline, concurrency control, and storage provider access.
/// </summary>
/// <remarks>
/// <b>Thread Safety:</b> All public methods are thread-safe. Compound operations (such as upsert) are atomic per key.
/// </remarks>
internal sealed class StateStoreImplementation : IStateStore
```

- [ ] **Step 2: Confirm there are no other duplicate `<summary>` blocks in src/**

Run: `rg -U '/// <summary>[^<]*</summary>\s*/// <summary>' src/ 2>&1 || echo "clean"`
Expected: prints `clean` (no matches). If matches surface, merge them by the same rule: pick the better-worded summary, drop the duplicate.

### Task 5.3: Verify Chunk 5 end-state and commit

- [ ] **Step 1: Debug + Release build clean**

Run: `dotnet build -c Debug --nologo 2>&1 | tail -3 && dotnet build -c Release --nologo 2>&1 | tail -3`
Expected: both report `Build succeeded.  0 Warning(s)  0 Error(s)`.

- [ ] **Step 2: All tests pass**

Run: `dotnet test --nologo 2>&1 | tail -3`
Expected: `Passed!  - Failed: 0, Passed: 86, Skipped: 0, Total: 86`.

- [ ] **Step 3: `git log --follow` works on a moved file**

Run: `git log --oneline --follow -- src/StateStore/Builder/StateStoreBuilder.cs | head -3`
Expected: at least one pre-move commit, confirming `git mv` preserved history.

- [ ] **Step 4: Confirm `src/StateStore/` root contains only the csproj and PACKAGE.md**

Run: `ls src/StateStore/*.cs 2>&1`
Expected: no matches (`No such file`). All `.cs` files now live in subfolders.

- [ ] **Step 5: Commit**

```bash
git add src/StateStore/Builder
git commit -m "Reorganize core: move loose files into Builder/ folder

The three top-level files (StateStoreBuilder.cs, StateStoreImplementation.cs,
TypedStateStore.cs) move into src/StateStore/Builder/. Namespaces are
unchanged so no consumer-facing impact. Also merges the duplicate <summary>
XML doc block on StateStoreImplementation into a single block, preserving
the <remarks> section."
```

---

## Chunk 6: Sample projects

Replaces the static `examples/ProviderUsageExamples.cs` with four runnable console sample projects under `samples/`. Each builds as part of `dotnet build` from the root, so snippet bit-rot is impossible.

### Task 6.1: Delete the legacy `examples/` folder

**Files:**
- Delete: `examples/ProviderUsageExamples.cs`
- Delete: `examples/` directory

**Context:** Content is salvaged into `samples/StateStore.Samples.QuickStart` in Task 6.2.

- [ ] **Step 1: Remove the file and folder**

```bash
git rm examples/ProviderUsageExamples.cs
rmdir examples
```

- [ ] **Step 2: Confirm**

Run: `ls examples 2>&1`
Expected: `No such file or directory`.

### Task 6.2: Create `StateStore.Samples.QuickStart`

**Files:**
- Create: `samples/StateStore.Samples.QuickStart/StateStore.Samples.QuickStart.csproj`
- Create: `samples/StateStore.Samples.QuickStart/Program.cs`

**Context:** Minimal demonstration of `StateStoreBuilder` + `InMemory` + `UseJsonSerializer`. Targets `net8.0` only (samples don't multi-target). Marked `IsPackable=false`.

- [ ] **Step 1: Create the csproj**

Create `samples/StateStore.Samples.QuickStart/StateStore.Samples.QuickStart.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net8.0</TargetFramework>
    <RootNamespace>StateStore.Samples.QuickStart</RootNamespace>
    <AssemblyName>StateStore.Samples.QuickStart</AssemblyName>
    <IsPackable>false</IsPackable>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\src\StateStore\StateStore.csproj" />
  </ItemGroup>

</Project>
```

- [ ] **Step 2: Create `Program.cs`**

Create `samples/StateStore.Samples.QuickStart/Program.cs`:

```csharp
// Quick-start sample for StateStore.
//
// Demonstrates the standalone StateStoreBuilder path with the in-memory
// provider — the smallest possible setup. Run with:
//
//     dotnet run --project samples/StateStore.Samples.QuickStart

using StateStore;

var store = new StateStoreBuilder()
    .UseInMemory()
    .UseJsonSerializer()
    .Build();

await store.SetAsync("greeting", "hello from StateStore");
var greeting = await store.GetAsync<string>("greeting");

Console.WriteLine($"Round-tripped value: {greeting}");
```

### Task 6.3: Create `StateStore.Samples.AspNetCoreDI`

**Files:**
- Create: `samples/StateStore.Samples.AspNetCoreDI/StateStore.Samples.AspNetCoreDI.csproj`
- Create: `samples/StateStore.Samples.AspNetCoreDI/Program.cs`

**Context:** `Host.CreateApplicationBuilder` + `AddStateStore` + a `BackgroundService` consuming `ITypedStateStore<T>`. FileSystem-backed; default options place state under `./state` in the current working directory.

- [ ] **Step 1: Create the csproj**

Create `samples/StateStore.Samples.AspNetCoreDI/StateStore.Samples.AspNetCoreDI.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net8.0</TargetFramework>
    <RootNamespace>StateStore.Samples.AspNetCoreDI</RootNamespace>
    <AssemblyName>StateStore.Samples.AspNetCoreDI</AssemblyName>
    <IsPackable>false</IsPackable>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.Extensions.Hosting" Version="10.0.8" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\src\StateStore\StateStore.csproj" />
  </ItemGroup>

</Project>
```

- [ ] **Step 2: Create `Program.cs`**

Create `samples/StateStore.Samples.AspNetCoreDI/Program.cs`:

```csharp
// ASP.NET Core / Worker Service DI sample for StateStore.
//
// Demonstrates AddStateStore(...) registration and ITypedStateStore<T>
// consumption inside a BackgroundService. The FileSystem provider writes
// to ./state/ relative to the current working directory.
//
// Run with:
//
//     dotnet run --project samples/StateStore.Samples.AspNetCoreDI

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using StateStore;
using StateStore.Abstractions;
using StateStore.Extensions;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddStateStore(o => o
    .UseFileSystem(fs => fs.BasePath = "./state")
    .UseJsonSerializer());

builder.Services.AddHostedService<CounterService>();

var host = builder.Build();
await host.RunAsync();

internal sealed record Counter(int Hits);

internal sealed class CounterService(ITypedStateStore<Counter> store, IHostApplicationLifetime lifetime) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var current = await store.GetAsync(stoppingToken) ?? new Counter(0);
        var next = current with { Hits = current.Hits + 1 };
        await store.SetAsync(next, stoppingToken);

        Console.WriteLine($"Run #{next.Hits} — state persists in ./state/ across restarts.");
        lifetime.StopApplication();
    }
}
```

### Task 6.4: Create `StateStore.Samples.MongoDb`

**Files:**
- Create: `samples/StateStore.Samples.MongoDb/StateStore.Samples.MongoDb.csproj`
- Create: `samples/StateStore.Samples.MongoDb/Program.cs`

- [ ] **Step 1: Create the csproj**

Create `samples/StateStore.Samples.MongoDb/StateStore.Samples.MongoDb.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net8.0</TargetFramework>
    <RootNamespace>StateStore.Samples.MongoDb</RootNamespace>
    <AssemblyName>StateStore.Samples.MongoDb</AssemblyName>
    <IsPackable>false</IsPackable>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.Extensions.Hosting" Version="10.0.8" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\src\StateStore\StateStore.csproj" />
    <ProjectReference Include="..\..\src\StateStore.MongoDb\StateStore.MongoDb.csproj" />
  </ItemGroup>

</Project>
```

- [ ] **Step 2: Create `Program.cs`**

Create `samples/StateStore.Samples.MongoDb/Program.cs`:

```csharp
// MongoDB sample for StateStore.
//
// Requires a running MongoDB instance. Start one locally with:
//
//     docker run -d -p 27017:27017 --name statestore-sample mongo:7
//
// Then run:
//
//     dotnet run --project samples/StateStore.Samples.MongoDb

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using StateStore;
using StateStore.Abstractions;
using StateStore.Extensions;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddStateStore(o => o
    .UseMongoDb(m =>
    {
        m.ConnectionString = "mongodb://localhost:27017";
        m.DatabaseName = "StateStoreSample";
        m.CollectionName = "State";
    })
    .UseJsonSerializer());

builder.Services.AddHostedService<MongoSampleService>();

var host = builder.Build();
await host.RunAsync();

internal sealed record Visit(DateTimeOffset At);

internal sealed class MongoSampleService(IStateStore store, IHostApplicationLifetime lifetime) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var visit = new Visit(DateTimeOffset.UtcNow);
        await store.SetAsync("last-visit", visit, stoppingToken);
        var roundtrip = await store.GetAsync<Visit>("last-visit", stoppingToken);

        Console.WriteLine($"Stored visit at {visit.At:o}; read back {roundtrip?.At:o}.");
        lifetime.StopApplication();
    }
}
```

### Task 6.5: Create `StateStore.Samples.Sqlite`

**Files:**
- Create: `samples/StateStore.Samples.Sqlite/StateStore.Samples.Sqlite.csproj`
- Create: `samples/StateStore.Samples.Sqlite/Program.cs`

- [ ] **Step 1: Create the csproj**

Create `samples/StateStore.Samples.Sqlite/StateStore.Samples.Sqlite.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net8.0</TargetFramework>
    <RootNamespace>StateStore.Samples.Sqlite</RootNamespace>
    <AssemblyName>StateStore.Samples.Sqlite</AssemblyName>
    <IsPackable>false</IsPackable>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.Extensions.Hosting" Version="10.0.8" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\src\StateStore\StateStore.csproj" />
    <ProjectReference Include="..\..\src\StateStore.Sqlite\StateStore.Sqlite.csproj" />
  </ItemGroup>

</Project>
```

- [ ] **Step 2: Create `Program.cs`**

Create `samples/StateStore.Samples.Sqlite/Program.cs`:

```csharp
// SQLite sample for StateStore.
//
// Writes to a local SQLite file ./statestore-sample.db. Two state stores
// open the same file to demonstrate durability across instances. Run with:
//
//     dotnet run --project samples/StateStore.Samples.Sqlite

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using StateStore;
using StateStore.Abstractions;
using StateStore.Extensions;

const string connectionString = "Data Source=statestore-sample.db";

// Producer scope.
using (var producer = BuildHost())
{
    var store = producer.Services.GetRequiredService<IStateStore>();
    await store.SetAsync("note", "written from the first store", CancellationToken.None);
    Console.WriteLine("Wrote 'note' from the producer scope.");
}

// Consumer scope — separate IServiceProvider, same SQLite file.
using (var consumer = BuildHost())
{
    var store = consumer.Services.GetRequiredService<IStateStore>();
    var value = await store.GetAsync<string>("note", CancellationToken.None);
    Console.WriteLine($"Read '{value}' from the consumer scope.");
}

static IHost BuildHost()
{
    var builder = Host.CreateApplicationBuilder();
    builder.Services.AddStateStore(o => o
        .UseSqlite(s => s.ConnectionString = connectionString)
        .UseJsonSerializer());
    return builder.Build();
}
```

### Task 6.6: Add samples to the solution

**Files:**
- Modify: `StateStore.sln`

- [ ] **Step 1: Register all four sample projects under a `samples` solution folder**

```bash
dotnet sln StateStore.sln add samples/StateStore.Samples.QuickStart/StateStore.Samples.QuickStart.csproj --solution-folder samples
dotnet sln StateStore.sln add samples/StateStore.Samples.AspNetCoreDI/StateStore.Samples.AspNetCoreDI.csproj --solution-folder samples
dotnet sln StateStore.sln add samples/StateStore.Samples.MongoDb/StateStore.Samples.MongoDb.csproj --solution-folder samples
dotnet sln StateStore.sln add samples/StateStore.Samples.Sqlite/StateStore.Samples.Sqlite.csproj --solution-folder samples
```

- [ ] **Step 2: Confirm all four projects are in the sln**

Run: `dotnet sln StateStore.sln list | rg Samples`
Expected: four lines, one per sample project.

### Task 6.7: Verify Chunk 6 end-state and commit

- [ ] **Step 1: Build entire solution from root**

Run: `dotnet build --nologo 2>&1 | tail -5`
Expected: `Build succeeded.  0 Warning(s)  0 Error(s)`. All four samples compile as part of the solution build.

- [ ] **Step 2: Confirm no samples produced nupkgs**

Run: `find . -name "StateStore.Samples.*.nupkg" 2>/dev/null || echo "none"`
Expected: prints `none`.

- [ ] **Step 3: Smoke-run the QuickStart sample**

Run: `dotnet run --project samples/StateStore.Samples.QuickStart --no-build 2>&1 | tail -3`
Expected: prints `Round-tripped value: hello from StateStore`.

- [ ] **Step 4: All tests still pass**

Run: `dotnet test --nologo 2>&1 | tail -3`
Expected: `Passed!  - Failed: 0, Passed: 86, Skipped: 0, Total: 86`.

- [ ] **Step 5: Commit**

```bash
git add samples StateStore.sln
git commit -m "Replace examples/ with runnable sample projects under samples/

Four console apps under samples/, each referencing the appropriate
StateStore packages:

- QuickStart: standalone StateStoreBuilder + InMemory.
- AspNetCoreDI: Host.CreateApplicationBuilder + AddStateStore +
  ITypedStateStore<T> consumed by a BackgroundService.
- MongoDb: DI registration against a local MongoDB instance.
- Sqlite: two-scope durability roundtrip against a SQLite file.

All targets net8.0 only, all marked IsPackable=false. Added to the
solution under a samples folder so dotnet build from the root compiles
them — guards against snippet bit-rot. The legacy examples/ folder is
deleted; its InMemory snippet is salvaged into QuickStart."
```

---

## Chunk 7: Refactor test setup duplication

`NullReferenceTests.cs` and `EdgeCaseTests.cs` each construct the same `InMemoryStorageProvider` + `JsonStateSerializer` + `MiddlewarePipeline` + `StateStoreImplementation` graph inside every test method. `StateStoreTests.cs` already uses ctor-based shared setup; the two outliers should match.

### Task 7.1: Refactor `NullReferenceTests`

**Files:**
- Modify: `tests/StateStore.Tests/NullReferenceTests.cs`

- [ ] **Step 1: Replace the entire file content**

Replace the entire contents of `tests/StateStore.Tests/NullReferenceTests.cs` with:

```csharp
using StateStore.Abstractions;
using StateStore.Middleware;
using StateStore.Providers.InMemory;
using StateStore.Serialization;

namespace StateStore.Tests;

public sealed class NullReferenceTests
{
    private readonly IStateStore _store;

    public NullReferenceTests()
    {
        var provider = new InMemoryStorageProvider();
        var serializer = new JsonStateSerializer();
        var pipeline = new MiddlewarePipeline([], provider);
        _store = new StateStoreImplementation(serializer, pipeline);
    }

    [Fact]
    public async Task SetAsync_NullKey_ThrowsAsync()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(async () =>
            await _store.SetAsync(null!, 123, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task GetAsync_NullKey_ThrowsAsync()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(async () =>
            await _store.GetAsync<int>(null!, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task UpsertAsync_NullKey_ThrowsAsync()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(async () =>
            await _store.UpsertAsync<int>(null!, 1, x => x + 1, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task UpsertAsync_NullUpdater_ThrowsAsync()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(async () =>
            await _store.UpsertAsync("key", 1, null!, TestContext.Current.CancellationToken));
    }
}
```

- [ ] **Step 2: Run NullReferenceTests to confirm they all still pass**

Run: `dotnet test --filter "FullyQualifiedName~NullReferenceTests" --nologo 2>&1 | tail -10`
Expected: `Passed!  - Failed: 0, Passed: 4, Skipped: 0, Total: 4`. Test count is unchanged; only the setup is consolidated.

### Task 7.2: Refactor `EdgeCaseTests`

**Files:**
- Modify: `tests/StateStore.Tests/EdgeCaseTests.cs`

- [ ] **Step 1: Replace the entire file content**

Replace the entire contents of `tests/StateStore.Tests/EdgeCaseTests.cs` with:

```csharp
using StateStore.Abstractions;
using StateStore.Middleware;
using StateStore.Providers.InMemory;
using StateStore.Serialization;

namespace StateStore.Tests;

public sealed class EdgeCaseTests
{
    private readonly IStateStore _store;

    public EdgeCaseTests()
    {
        var provider = new InMemoryStorageProvider();
        var serializer = new JsonStateSerializer();
        var pipeline = new MiddlewarePipeline([], provider);
        _store = new StateStoreImplementation(serializer, pipeline);
    }

    [Fact]
    public async Task SetAndGet_LargeObject_SucceedsAsync()
    {
        var largeObject = new string('x', 100_000);
        await _store.SetAsync("large", largeObject, TestContext.Current.CancellationToken);
        var result = await _store.GetAsync<string>("large", TestContext.Current.CancellationToken);
        Assert.Equal(largeObject, result);
    }

    [Fact]
    public async Task SetAndGet_EmptyString_SucceedsAsync()
    {
        await _store.SetAsync("empty", "", TestContext.Current.CancellationToken);
        var result = await _store.GetAsync<string>("empty", TestContext.Current.CancellationToken);
        Assert.Equal("", result);
    }

    [Fact]
    public async Task SetAndGet_EmptyArray_SucceedsAsync()
    {
        var emptyArray = Array.Empty<int>();
        await _store.SetAsync("emptyArray", emptyArray, TestContext.Current.CancellationToken);
        var result = await _store.GetAsync<int[]>("emptyArray", TestContext.Current.CancellationToken) ?? [];
        Assert.Empty(result);
    }

    [Fact]
    public async Task Get_NonExistentKey_ReturnsDefault_Async()
    {
        var result = await _store.GetAsync<int>("does_not_exist", TestContext.Current.CancellationToken);
        Assert.Equal(0, result);
    }
}
```

- [ ] **Step 2: Run EdgeCaseTests to confirm they all still pass**

Run: `dotnet test --filter "FullyQualifiedName~EdgeCaseTests" --nologo 2>&1 | tail -10`
Expected: `Passed!  - Failed: 0, Passed: 4, Skipped: 0, Total: 4`.

### Task 7.3: Verify Chunk 7 end-state and commit

- [ ] **Step 1: Full test suite passes**

Run: `dotnet test --nologo 2>&1 | tail -3`
Expected: `Passed!  - Failed: 0, Passed: 86, Skipped: 0, Total: 86` (count unchanged).

- [ ] **Step 2: Build clean**

Run: `dotnet build --nologo 2>&1 | tail -3`
Expected: `Build succeeded.  0 Warning(s)  0 Error(s)`.

- [ ] **Step 3: Commit**

```bash
git add tests/StateStore.Tests/NullReferenceTests.cs tests/StateStore.Tests/EdgeCaseTests.cs
git commit -m "Consolidate NullReferenceTests/EdgeCaseTests setup into ctors

Replaces the per-method InMemoryStorageProvider + JsonStateSerializer +
MiddlewarePipeline + StateStoreImplementation construction with a
ctor-based _store field, matching StateStoreTests. No behavioral changes
to any test; all 86 tests pass."
```

---

## Chunk 8: Root README

The final chunk: writes the OSS-grade `README.md` at the repo root. References packages, docs, samples, and provides quick-start snippets for each provider.

### Task 8.1: Create the root README

**Files:**
- Create: `README.md` (repo root)

- [ ] **Step 1: Create `README.md`**

Create `README.md` at the repo root with:

```markdown
# StateStore

[![NuGet StateStore](https://img.shields.io/nuget/v/StateStore?label=StateStore&logo=nuget)](https://www.nuget.org/packages/StateStore)
[![NuGet StateStore.MongoDb](https://img.shields.io/nuget/v/StateStore.MongoDb?label=StateStore.MongoDb&logo=nuget)](https://www.nuget.org/packages/StateStore.MongoDb)
[![NuGet StateStore.Sqlite](https://img.shields.io/nuget/v/StateStore.Sqlite?label=StateStore.Sqlite&logo=nuget)](https://www.nuget.org/packages/StateStore.Sqlite)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE.txt)
[![.NET 8/9/10](https://img.shields.io/badge/.NET-8%20%7C%209%20%7C%2010-512BD4)](https://dotnet.microsoft.com)

> Flexible, testable state storage for .NET — pluggable providers, middleware pipeline, zero ceremony to wire up.

## Why StateStore

- **Dictionary-style API** over any storage backend. `GetAsync`, `SetAsync`, `UpsertAsync`, `DeleteAsync`, `ExistsAsync`.
- **Swappable providers**: in-memory and file-system come bundled in the core package; MongoDB and SQLite ship as opt-in packages so consumers only pull the deps they use.
- **Per-key concurrency** baked in — `UpsertAsync` is atomic per key, no manual locking.
- **DI-first**: `AddStateStore(...)` integrates with ASP.NET Core, worker services, or any `IServiceCollection`. A standalone `StateStoreBuilder` works without a DI container.
- **Middleware pipeline** for cross-cutting concerns (logging, validation, encryption, etc.).
- **Designed for tests**: the in-memory provider is a drop-in fake; per-key locking lets concurrency scenarios run in unit tests.

## Install

| Package | When to install |
|---|---|
| [`StateStore`](https://www.nuget.org/packages/StateStore) | Always. Bundles the in-memory and file-system providers. |
| [`StateStore.MongoDb`](https://www.nuget.org/packages/StateStore.MongoDb) | When you want a MongoDB-backed store. |
| [`StateStore.Sqlite`](https://www.nuget.org/packages/StateStore.Sqlite) | When you want a SQLite-backed store. |

```bash
dotnet add package StateStore
dotnet add package StateStore.MongoDb     # optional
dotnet add package StateStore.Sqlite      # optional
```

## Quick start

### Standalone (no DI container)

```csharp
using StateStore;

var store = new StateStoreBuilder()
    .UseInMemory()
    .UseJsonSerializer()
    .Build();

await store.SetAsync("greeting", "hello");
var greeting = await store.GetAsync<string>("greeting");
```

### ASP.NET Core / worker service

```csharp
using StateStore;

builder.Services.AddStateStore(o => o
    .UseFileSystem(fs => fs.BasePath = "./state")
    .UseJsonSerializer());
```

Then inject `IStateStore` or `ITypedStateStore<TState>` anywhere it's needed.

### Typed store

```csharp
using StateStore;

public sealed record UserPreferences(string Theme, bool Notifications);

var prefs = new StateStoreBuilder()
    .UseFileSystem(fs => fs.BasePath = "./state")
    .UseJsonSerializer()
    .Build<UserPreferences>();

await prefs.SetAsync(new UserPreferences("dark", Notifications: true));
var current = await prefs.GetAsync();
```

### MongoDB

```csharp
using StateStore;

builder.Services.AddStateStore(o => o
    .UseMongoDb(m =>
    {
        m.ConnectionString = "mongodb://localhost:27017";
        m.DatabaseName = "myapp";
    })
    .UseJsonSerializer());
```

### SQLite

```csharp
using StateStore;

builder.Services.AddStateStore(o => o
    .UseSqlite(s => s.ConnectionString = "Data Source=state.db")
    .UseJsonSerializer());
```

## Documentation

| # | Guide |
|---|-------|
| 01 | [Getting Started](docs/01-Getting-Started.md) |
| 02 | [Core Concepts](docs/02-Core-Concepts.md) |
| 03 | [Basic Usage](docs/03-Basic-Usage.md) |
| 04 | [Typed State Store](docs/04-Typed-State-Store.md) |
| 05 | [Storage Providers](docs/05-Storage-Providers.md) |
| 06 | [Serialization](docs/06-Serialization.md) |
| 07 | [Middleware](docs/07-Middleware.md) |
| 08 | [Concurrency](docs/08-Concurrency.md) |
| 09 | [Auto-Save](docs/09-Auto-Save.md) |
| 10 | [Dependency Injection](docs/10-Dependency-Injection.md) |
| 11 | [Standalone Usage](docs/11-Standalone-Usage.md) |
| 12 | [Error Handling](docs/12-Error-Handling.md) |
| 13 | [Testing](docs/13-Testing.md) |
| 14 | [Extensibility](docs/14-Extensibility.md) |

## Samples

Runnable console projects under [`samples/`](samples/):

- [`StateStore.Samples.QuickStart`](samples/StateStore.Samples.QuickStart) — in-memory standalone roundtrip.
- [`StateStore.Samples.AspNetCoreDI`](samples/StateStore.Samples.AspNetCoreDI) — `AddStateStore` + `BackgroundService`.
- [`StateStore.Samples.MongoDb`](samples/StateStore.Samples.MongoDb) — MongoDB DI registration.
- [`StateStore.Samples.Sqlite`](samples/StateStore.Samples.Sqlite) — two-scope durability roundtrip against a SQLite file.

Each sample compiles as part of `dotnet build` from the repo root, so the snippets in this README don't bit-rot.

## License

[MIT](LICENSE.txt). © Cadtastic Solutions.
```

### Task 8.2: Verify Chunk 8 end-state and commit

- [ ] **Step 1: Confirm `README.md` renders sensibly (preview locally if you can; otherwise spot-check)**

Run: `head -30 README.md`
Expected: heading, badges, tagline, "Why StateStore" intro.

- [ ] **Step 2: Confirm all linked paths exist**

Run: `for path in docs/01-Getting-Started.md docs/02-Core-Concepts.md docs/03-Basic-Usage.md docs/04-Typed-State-Store.md docs/05-Storage-Providers.md docs/06-Serialization.md docs/07-Middleware.md docs/08-Concurrency.md docs/09-Auto-Save.md docs/10-Dependency-Injection.md docs/11-Standalone-Usage.md docs/12-Error-Handling.md docs/13-Testing.md docs/14-Extensibility.md samples/StateStore.Samples.QuickStart samples/StateStore.Samples.AspNetCoreDI samples/StateStore.Samples.MongoDb samples/StateStore.Samples.Sqlite LICENSE.txt; do [ -e "$path" ] && echo "ok: $path" || echo "MISSING: $path"; done`
Expected: every line starts with `ok:`. Any `MISSING:` line means the README link is broken and must be fixed.

- [ ] **Step 3: Build clean**

Run: `dotnet build --nologo 2>&1 | tail -3`
Expected: `Build succeeded.  0 Warning(s)  0 Error(s)`.

- [ ] **Step 4: Commit**

```bash
git add README.md
git commit -m "Add root README

Full OSS-grade README with NuGet badges, install table, quick-start
snippets per provider, documentation index linking to docs/ guides
01-14, samples links, and license. Renders the project page on
GitHub."
```

---

## Final verification (all chunks complete)

After Chunk 8 is committed, run the full verification suite from the spec's "Verification" section:

- [ ] **Step 1: `dotnet restore` is clean**

Run: `dotnet restore --nologo 2>&1 | grep -E "(NU1902|NU1903|NU1904|error)" || echo "clean"`
Expected: prints `clean`. The SharpCompress advisory is suppressed inside `StateStore.MongoDb.csproj` only.

- [ ] **Step 2: `dotnet build -c Release` is clean across all TFM combinations**

Run: `dotnet build -c Release --nologo 2>&1 | tail -3`
Expected: `Build succeeded.  0 Warning(s)  0 Error(s)`.

- [ ] **Step 3: `dotnet pack -c Release` produces exactly three nupkgs and three snupkgs**

Run: `find . -name "*.nupkg" -path "*/bin/Release/*" -delete; find . -name "*.snupkg" -path "*/bin/Release/*" -delete 2>/dev/null; dotnet pack -c Release --nologo 2>&1 | tail -3 && find . \( -name "*.nupkg" -o -name "*.snupkg" \) -path "*/bin/Release/*" 2>/dev/null | sort`
Expected:
```
./src/StateStore.MongoDb/bin/Release/StateStore.MongoDb.1.0.0.nupkg
./src/StateStore.MongoDb/bin/Release/StateStore.MongoDb.1.0.0.snupkg
./src/StateStore.Sqlite/bin/Release/StateStore.Sqlite.1.0.0.nupkg
./src/StateStore.Sqlite/bin/Release/StateStore.Sqlite.1.0.0.snupkg
./src/StateStore/bin/Release/StateStore.1.0.0.nupkg
./src/StateStore/bin/Release/StateStore.1.0.0.snupkg
```

- [ ] **Step 4: All 86 tests pass**

Run: `dotnet test --nologo 2>&1 | tail -3`
Expected: `Passed!  - Failed: 0, Passed: 86, Skipped: 0, Total: 86`.

- [ ] **Step 5: Samples build as part of the solution**

Already covered by Step 2 (sln-level Release build), but spot-check by running one sample:

Run: `dotnet run --project samples/StateStore.Samples.QuickStart --no-build -c Release 2>&1 | tail -3`
Expected: prints `Round-tripped value: hello from StateStore`.

- [ ] **Step 6: `git log --follow` history survives across moves**

Run: `git log --oneline --follow -- src/StateStore.MongoDb/MongoStorageProvider.cs | wc -l`
Expected: ≥ 2 (initial commit + the Chunk 3 move + anything since).

- [ ] **Step 7: Tracked-artifact hygiene**

Run: `git ls-files | rg "(TestResults|^\.vs/|csproj\.user|\.diagsession)" || echo "clean"`
Expected: prints `clean`.

- [ ] **Step 8: No duplicate `<summary>` doc-comments anywhere in `src/`**

Run: `rg -U '/// <summary>[^<]*</summary>\s*/// <summary>' src/ 2>&1 || echo "clean"`
Expected: prints `clean`.

- [ ] **Step 9: No stale `StorageProviderKind` or `options.Provider` references**

Run: `git grep -nE "StorageProviderKind|options\.Provider" src/ tests/ samples/ 2>&1 || echo "clean"`
Expected: prints `clean`.

- [ ] **Step 10: README links all resolve**

(Same recipe as Chunk 8 Task 8.2 Step 2; included here as a final pre-PR gate.)

