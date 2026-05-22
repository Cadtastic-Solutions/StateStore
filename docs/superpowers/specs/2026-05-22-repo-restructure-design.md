# Repo Restructure & Provider Package Split

**Date:** 2026-05-22
**Branch:** `refactor/repo-organization`
**Status:** Design — pending implementation plan

## Goal

Reorganize the StateStore repository to follow standard .NET library conventions, split storage providers into their own NuGet packages so consumers only pay for the dependencies they use, give the project a proper public-facing README, and clean up several adjacent housekeeping items while we are already touching every csproj.

## Why now

1. The bundled-providers structure caused the SharpCompress transitive vulnerability (CVE-2026-44788) to brick the entire library and every consumer, including those who only use `InMemoryStorageProvider`. Splitting providers permanently fixes the class of problem.
2. The project ships NuGet metadata (`PackageId`, MIT license, GitHub repo URL) and a v1.0.0-alpha version — clearly intended for public distribution — but has no root `README.md`, so `github.com/Cadtastic-Solutions/StateStore` shows nothing.
3. The DI surface (`AddStateStore(opts => ...)`) silently does not support MongoDB or SQLite today (the `StorageProviderKind` enum only enumerates `InMemory` and `FileSystem`). The provider split forces us to surface and fix this gap.
4. v1.0.0-alpha is the right moment to make breaking changes. Adopters are few; semver permits it.

## Non-goals (explicitly out of scope)

- GitHub Actions CI workflows. `.github/workflows/` is empty and will stay that way.
- `CONTRIBUTING.md`, `CHANGELOG.md`, `CODE_OF_CONDUCT.md`. The repo is not actively soliciting outside contributions.
- SLNX (XML solution file) migration. Mature .NET 9 feature but adds churn with no benefit at this scale.
- Stricter analyzers (`AnalysisLevel`, `EnableNETAnalyzers`, etc.). Would flood the build with warnings to triage; better as a separate effort.
- Strong-name signing. Not requested; project is permissively licensed and signs are pure overhead at this stage.
- Per-provider test projects. One consolidated `StateStore.Tests` is sufficient for library of this size.

## Current state analysis

```
StateStore/
├── Directory.Build.props                          # global props; carries NuGetAuditSuppress
├── LICENSE.txt
├── StateStore.sln
├── .editorconfig / .gitattributes / .gitignore
├── .github/workflows/                             # empty
├── .vs/ TestResults/                              # may be tracked accidentally — verify in plan
│
├── examples/
│   └── ProviderUsageExamples.cs                   # single file, no csproj, not in sln
│
├── src/
│   └── StateStore/                                # one project, bundles every provider
│       ├── StateStore.csproj                      # has all NuGet metadata inline
│       ├── readme.md                              # NuGet package readme
│       ├── .Docs/01-Getting-Started.md … 14-Extensibility.md
│       ├── Abstractions/ AutoSave/ Concurrency/
│       ├── Exceptions/ Extensions/ Internal/
│       ├── Middleware/ Options/ Serialization/
│       ├── Providers/{FileSystem,InMemory,Mongo,Sqlite}/
│       ├── StateStoreBuilder.cs                   # loose at project root
│       ├── StateStoreBuilderProviderExtensions.cs # UseMongo + UseSqlite
│       ├── StateStoreImplementation.cs
│       └── TypedStateStore.cs
│
└── tests/
    ├── StateStore.Tests/
    └── StateStore.Benchmarks/
```

### Problems observed

| # | Issue | Severity |
|---|-------|----------|
| 1 | All four providers bundled in one assembly — every consumer transitively gets MongoDB.Driver + Microsoft.Data.Sqlite. | High |
| 2 | No root `README.md`. GitHub project page is blank. | High |
| 3 | `StateStoreOptions` (DI path) closed-enum `StorageProviderKind` excludes Mongo/Sqlite from `AddStateStore`. | Medium |
| 4 | Docs live in `src/StateStore/.Docs/` — dot-prefix hides them on Linux/Mac; oddly nested inside the package folder. | Medium |
| 5 | `examples/ProviderUsageExamples.cs` is a single floating file. Not in the solution, no compile guard against bit-rot. | Medium |
| 6 | NuGet metadata duplicated per project (after split there'd be three copies). | Low |
| 7 | Loose top-level files in `src/StateStore/` (`StateStoreBuilder.cs`, `StateStoreImplementation.cs`, `TypedStateStore.cs`, `StateStoreBuilderProviderExtensions.cs`). | Low |
| 8 | Duplicate `<summary>` XML-doc blocks on classes in `MongoStorageProvider.cs` and `StateStoreImplementation.cs`. | Low |
| 9 | Build emits `NETSDK1057: preview version of .NET` because the highest installed SDK is `10.0.300-preview`. No `global.json` pin. | Low |
| 10 | Test files `NullReferenceTests.cs` and `EdgeCaseTests.cs` duplicate the same 5-line setup block in each test method. | Low |

## Decisions

| Decision | Choice |
|---|---|
| Provider packaging | Split: core + per-provider packages |
| Which providers stay in core | InMemory + FileSystem |
| Separate packages | `StateStore.MongoDb`, `StateStore.Sqlite` |
| README polish | Full OSS-grade |
| Examples | Convert to runnable sample projects under `samples/` |
| Test layout | One consolidated `StateStore.Tests` |
| CONTRIBUTING / CHANGELOG / CI | Skipped |
| Hoist shared NuGet metadata | Yes |
| Re-scope SharpCompress suppress | Yes — move into `StateStore.MongoDb.csproj` |
| SourceLink + symbols + deterministic builds | Yes |
| Pin SDK via `global.json` | Yes |
| Per-package `PACKAGE.md` for MongoDb and Sqlite | Yes |
| Fix duplicate `<summary>` XML doc tags | Yes |
| Refactor test setup duplication | Yes |

## Target layout

```
StateStore/
├── README.md                              # NEW — full OSS-grade
├── LICENSE.txt
├── Directory.Build.props                  # carries shared NuGet metadata + SourceLink config
├── StateStore.sln                         # updated with new projects + samples folder
├── global.json                            # NEW — pin to stable SDK
├── .editorconfig
├── .gitattributes
├── .gitignore                             # verify TestResults/, .vs/, *.csproj.user excluded
│
├── docs/                                  # ← moved from src/StateStore/.Docs/
│   ├── 01-Getting-Started.md
│   ├── 02-Core-Concepts.md
│   ├── 03-Basic-Usage.md
│   ├── 04-Typed-State-Store.md
│   ├── 05-Storage-Providers.md
│   ├── 06-Serialization.md
│   ├── 07-Middleware.md
│   ├── 08-Concurrency.md
│   ├── 09-Auto-Save.md
│   ├── 10-Dependency-Injection.md
│   ├── 11-Standalone-Usage.md
│   ├── 12-Error-Handling.md
│   ├── 13-Testing.md
│   ├── 14-Extensibility.md
│   └── superpowers/specs/                 # design docs (this file lives here)
│
├── src/
│   ├── StateStore/                        # core package
│   │   ├── StateStore.csproj              # MongoDB.Driver and Microsoft.Data.Sqlite removed
│   │   ├── PACKAGE.md                     # renamed from readme.md
│   │   ├── Abstractions/
│   │   ├── AutoSave/
│   │   ├── Builder/                       # NEW folder for builder + impl + typed
│   │   │   ├── StateStoreBuilder.cs
│   │   │   ├── StateStoreImplementation.cs
│   │   │   └── TypedStateStore.cs
│   │   ├── Concurrency/
│   │   ├── Exceptions/
│   │   ├── Extensions/
│   │   │   └── ServiceCollectionExtensions.cs
│   │   ├── Internal/
│   │   ├── Middleware/
│   │   ├── Options/                       # FileSystemStorageOptions, StateStoreOptions
│   │   ├── Providers/
│   │   │   ├── FileSystem/
│   │   │   └── InMemory/
│   │   └── Serialization/
│   │
│   ├── StateStore.MongoDb/                # NEW provider package
│   │   ├── StateStore.MongoDb.csproj      # carries the SharpCompress NuGetAuditSuppress
│   │   ├── PACKAGE.md                     # NEW
│   │   ├── MongoStorageProvider.cs
│   │   ├── MongoStorageOptions.cs
│   │   └── StateStoreOptionsMongoExtensions.cs   # UseMongoDb() for builder + DI
│   │
│   └── StateStore.Sqlite/                 # NEW provider package
│       ├── StateStore.Sqlite.csproj
│       ├── PACKAGE.md                     # NEW
│       ├── SqliteStorageProvider.cs
│       ├── SqliteStorageOptions.cs
│       └── StateStoreOptionsSqliteExtensions.cs  # UseSqlite() for builder + DI
│
├── samples/                               # ← renamed from examples/
│   ├── StateStore.Samples.QuickStart/
│   ├── StateStore.Samples.AspNetCoreDI/
│   ├── StateStore.Samples.MongoDb/
│   └── StateStore.Samples.Sqlite/
│
└── tests/
    ├── StateStore.Tests/                  # refs all three packages
    └── StateStore.Benchmarks/
```

## File migrations

All moves use `git mv` so history follows.

| Source | Destination | Note |
|---|---|---|
| `src/StateStore/.Docs/*.md` | `docs/*.md` | Drop dot prefix; promote to repo root. Update the `<None Include=".Docs\*.md" Pack="true" PackagePath="docs/" />` glob in core csproj. |
| `src/StateStore/readme.md` | `src/StateStore/PACKAGE.md` | Rename to disambiguate from new root README. |
| `src/StateStore/StateStoreBuilder.cs` | `src/StateStore/Builder/StateStoreBuilder.cs` | |
| `src/StateStore/StateStoreImplementation.cs` | `src/StateStore/Builder/StateStoreImplementation.cs` | |
| `src/StateStore/TypedStateStore.cs` | `src/StateStore/Builder/TypedStateStore.cs` | |
| `src/StateStore/StateStoreBuilderProviderExtensions.cs` | **deleted** | Methods split between the two new provider packages. |
| `src/StateStore/Providers/Mongo/MongoStorageProvider.cs` | `src/StateStore.MongoDb/MongoStorageProvider.cs` | Namespace `StateStore.Providers.Mongo` → `StateStore.MongoDb`. |
| `src/StateStore/Options/MongoStorageOptions.cs` | `src/StateStore.MongoDb/MongoStorageOptions.cs` | Namespace `StateStore.Options` → `StateStore.MongoDb`. |
| `src/StateStore/Providers/Sqlite/SqliteStorageProvider.cs` | `src/StateStore.Sqlite/SqliteStorageProvider.cs` | Namespace `StateStore.Providers.Sqlite` → `StateStore.Sqlite`. |
| `src/StateStore/Options/SqliteStorageOptions.cs` | `src/StateStore.Sqlite/SqliteStorageOptions.cs` | Namespace `StateStore.Options` → `StateStore.Sqlite`. |
| `examples/ProviderUsageExamples.cs` | **deleted** | Content salvaged into `samples/StateStore.Samples.QuickStart`. |
| `examples/` (directory) | **deleted** | Replaced by `samples/`. |

## Files created

| Path | Purpose |
|---|---|
| `README.md` (root) | Full OSS-grade — see README outline below. |
| `global.json` | Pins SDK to `10.0.204` with `rollForward: latestPatch`. |
| `src/StateStore.MongoDb/StateStore.MongoDb.csproj` | Multi-targets net8.0/net9.0/net10.0. References `StateStore` core via `<ProjectReference>`. Depends on `MongoDB.Driver 3.8.1`. Carries the `NuGetAuditSuppress` for GHSA-6c8g-7p36-r338. |
| `src/StateStore.MongoDb/PACKAGE.md` | ~30-line NuGet readme: install + one quick-start snippet + repo link. |
| `src/StateStore.MongoDb/StateStoreOptionsMongoExtensions.cs` | Houses both `UseMongoDb(this StateStoreBuilder, ...)` (standalone) and `UseMongoDb(this StateStoreOptions, ...)` (DI). Namespace `StateStore` for discovery. |
| `src/StateStore.Sqlite/StateStore.Sqlite.csproj` | Mirrors MongoDb package. Depends on `Microsoft.Data.Sqlite 10.0.8`. |
| `src/StateStore.Sqlite/PACKAGE.md` | Mirror of MongoDb. |
| `src/StateStore.Sqlite/StateStoreOptionsSqliteExtensions.cs` | Mirror of MongoDb extensions. |
| `samples/StateStore.Samples.QuickStart/Program.cs` + `.csproj` | Minimal `StateStoreBuilder` + InMemory roundtrip. ~30 lines. |
| `samples/StateStore.Samples.AspNetCoreDI/Program.cs` + `.csproj` | `Host.CreateApplicationBuilder` + `AddStateStore` + `BackgroundService` reading/writing typed state. |
| `samples/StateStore.Samples.MongoDb/Program.cs` + `.csproj` | DI registration + injected `IStateStore`. Top-of-file comment: `docker run -d -p 27017:27017 mongo:7`. |
| `samples/StateStore.Samples.Sqlite/Program.cs` + `.csproj` | File-based SQLite, demonstrates multi-process durability. |

## `StateStoreOptions` refactor (open-extensible)

Today (closed enum, blocks external providers):

```csharp
public StorageProviderKind Provider { get; set; } = StorageProviderKind.FileSystem;
public enum StorageProviderKind { FileSystem, InMemory }
```

`AddStateStore` (`ServiceCollectionExtensions.cs:57-71`):
```csharp
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

After (factory delegate, open). The registration delegate is exposed via a **public method** — provider packages live in separate assemblies and cannot touch internals, so a public surface is required. We deliberately do not expose it as a public mutable property (that would invite callers to set raw delegates).

```csharp
public sealed class StateStoreOptions
{
    private Action<IServiceCollection>? _configureProviderRegistration;

    /// <summary>
    /// Registers the provider's services. Called by built-in and provider-package
    /// extension methods (UseInMemory, UseFileSystem, UseMongoDb, UseSqlite, ...).
    /// Each call replaces the previous registration — last call wins, matching the
    /// "single provider per state store" model.
    /// </summary>
    public StateStoreOptions ConfigureProvider(Action<IServiceCollection> register)
    {
        ArgumentNullException.ThrowIfNull(register);
        _configureProviderRegistration = register;
        return this;
    }

    internal Action<IServiceCollection>? ProviderRegistration => _configureProviderRegistration;

    // ... existing properties (Serializer, FileSystem, MiddlewareTypes, etc.) unchanged ...
}
```

Built-in extension methods in core (replaces the enum branches, lives in `Options/StateStoreOptions.cs` since they're tightly coupled to it):

```csharp
public StateStoreOptions UseInMemory()
{
    return ConfigureProvider(services =>
        services.TryAddSingleton<IStorageProvider, InMemoryStorageProvider>());
}

public StateStoreOptions UseFileSystem(Action<FileSystemStorageOptions>? configure = null)
{
    FileSystem ??= new FileSystemStorageOptions();
    configure?.Invoke(FileSystem);

    // Capture snapshot of the configured options so the closure doesn't mutate later.
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

`AddStateStore` in `ServiceCollectionExtensions` becomes:
```csharp
// Apply the configured provider, or fall back to FileSystem with empty options
// to preserve today's "AddStateStore() with no body works" default.
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

Provider packages (`StateStore.MongoDb`, `StateStore.Sqlite`) extend `StateStoreOptions` via extension methods in namespace `StateStore` that internally call the **public** `ConfigureProvider(...)` method — no internals access required (see the `InternalsVisibleTo` section below). The enum is removed. The `StorageProviderKind` symbol is gone from the public surface.

**Subtle behavior change worth noting:** the new `UseFileSystem` snapshots `BasePath` / `FileExtension` into local variables before passing them to the registration closure. Today's code uses `services.Configure<FileSystemStorageOptions>(o => { o.BasePath = options.FileSystem.BasePath; ... })`, where the lambda body executes at DI-build time and reads whatever's on `options.FileSystem` at that moment. After the refactor, values are captured at the `UseFileSystem(...)` call site. This is a deliberate tightening — configuration-at-config-time matches user mental model — but it does change behavior if a consumer mutates `options.FileSystem` AFTER calling `UseFileSystem(...)` but before `AddStateStore` returns. The current single-line `services.AddStateStore(o => o.UseFileSystem(...))` idiom is unaffected.

**StateStoreBuilder gets a parallel update:** The standalone path has its own `_provider` field today. Provider extension methods on the builder continue to call `UseProvider(IStorageProvider)` directly (public; no change). The two extension methods per provider package (`UseMongoDb(this StateStoreBuilder, ...)` and `UseMongoDb(this StateStoreOptions, ...)`) ship in the same file inside the provider package.

## Public API breaking changes

| Change | Consumer migration |
|---|---|
| Namespace `StateStore.Providers.Mongo` → `StateStore.MongoDb` | New package install + `using` change. |
| Namespace `StateStore.Providers.Sqlite` → `StateStore.Sqlite` | New package install + `using` change. |
| `MongoStorageOptions` / `SqliteStorageOptions` namespace → matches provider | `using` change. |
| Method `UseMongo(...)` renamed to `UseMongoDb(...)` | One-line rename. Matches package name and `MongoDB.Driver` casing. |
| `StorageProviderKind` enum removed | Replace `options.Provider = StorageProviderKind.X` with `options.UseX(...)` (which most consumers already call). |
| `StateStoreBuilderProviderExtensions` class removed | Provider extension methods now live in their respective packages; new `using StateStore;` already covers them via extension method discovery. |
| `AddStateStore(o => o.UseMongoDb(...))` newly works | Net-new capability — not a break. |

## What stays unchanged

- `IStateStore`, `ITypedStateStore<>`, `IStorageProvider`, `IStateSerializer`, `IStateStoreMiddleware`, `IAutoSaveStrategy`.
- `StateStoreBuilder.UseInMemory()` / `.UseFileSystem()` / `.UseJsonSerializer()` / `.Build()`.
- All exception types and their hierarchy.
- All InMemory and FileSystem provider behavior.
- The 14 documentation guides (content unchanged; only their location).

## `InternalsVisibleTo` preservation

The current core csproj has `<InternalsVisibleTo Include="StateStore.Tests" />` (`src/StateStore/StateStore.csproj:28`). This must survive the refactor — the consolidated test project still directly instantiates `StateStoreImplementation` and `MiddlewarePipeline`, both of which are `internal sealed` (see `src/StateStore/Builder/StateStoreImplementation.cs` after move and `src/StateStore/Middleware/MiddlewarePipeline.cs`).

**Action:** the `InternalsVisibleTo` `<ItemGroup>` stays in `src/StateStore/StateStore.csproj` — it does NOT get hoisted to `Directory.Build.props` (only the *shared* metadata moves). Neither provider package needs internals access; they only consume public `IStorageProvider` and the public `ConfigureProvider` method.

## Doc-pack glob update

Current core csproj packs the `.Docs/*.md` files into the nupkg at `docs/` (`StateStore.csproj:33`):
```xml
<None Include=".Docs\*.md" Pack="true" PackagePath="docs/" />
```

After docs move to the repo root `docs/` folder, we use a **recursive** glob so future subfolders under `docs/` are picked up automatically, and explicitly exclude the internal design-docs tree (`docs/superpowers/`) so it never ships to NuGet:

```xml
<None Include="..\..\docs\**\*.md" Exclude="..\..\docs\superpowers\**\*.md" Pack="true" PackagePath="docs/" />
```

The `Exclude=` is **load-bearing** here: with the recursive `**` include, anything under `docs/superpowers/specs/` (this spec file, future design docs) would otherwise leak into the nupkg. If a planner switches to a single-level `..\..\docs\*.md` include, the `Exclude` becomes a no-op safety net — also acceptable, since the top-level `docs/` is intentionally flat (01–14 numbered guides) and a single-level glob already misses subfolders. Pick one and stay consistent; the recursive form is recommended for forward-compatibility.

## Benchmarks project

`tests/StateStore.Benchmarks/StateStore.Benchmarks.csproj` is **unchanged structurally** — it stays at its current path, keeps multi-targeting net8/9/10, keeps referencing `src/StateStore/StateStore.csproj`. It inherits the new `Directory.Build.props` automatically, but because `OutputType=Exe` makes it non-packable by default, the SourceLink and `PACKAGE.md` injection in the shared props are guarded by `<IsPackable>` and won't apply. Benchmark sources don't reference Mongo, Sqlite, or `StorageProviderKind`, so no code changes needed.

Verification step: the post-refactor `dotnet pack -c Release` must NOT emit a `StateStore.Benchmarks.nupkg`.

## Shared NuGet metadata in `Directory.Build.props`

After the hoist:

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

  <!-- SourceLink + deterministic + symbols for all packable projects. -->
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

Each package csproj then only sets:
```xml
<PropertyGroup>
  <PackageId>StateStore.MongoDb</PackageId>
  <Description>MongoDB storage provider for StateStore.</Description>
  <PackageTags>state;storage;mongodb;dotnet</PackageTags>
</PropertyGroup>
```

## NuGet audit suppression scope change

`Directory.Build.props` currently carries:
```xml
<ItemGroup>
  <NuGetAuditSuppress Include="https://github.com/advisories/GHSA-6c8g-7p36-r338" />
</ItemGroup>
```

After the split, only `StateStore.MongoDb` pulls SharpCompress (via MongoDB.Driver). The suppress moves into `src/StateStore.MongoDb/StateStore.MongoDb.csproj` with the same comment explaining the rationale. Core and the SQLite package no longer carry the exception — if they ever pick up a transitive vulnerability, the audit will catch it without manual review.

## `global.json` (new)

```json
{
  "sdk": {
    "version": "10.0.204",
    "rollForward": "latestPatch",
    "allowPrerelease": false
  }
}
```

Silences `NETSDK1057` and makes builds reproducible. `latestPatch` lets contributors pick up CVE fixes within the same minor.

## Per-package `PACKAGE.md` template

`src/StateStore.MongoDb/PACKAGE.md` (Sqlite mirrors this):

```markdown
# StateStore.MongoDb

MongoDB-backed storage provider for [StateStore](https://github.com/Cadtastic-Solutions/StateStore).

## Install

    dotnet add package StateStore
    dotnet add package StateStore.MongoDb

## Quick start

```csharp
builder.Services.AddStateStore(o => o
    .UseMongoDb(m => m.ConnectionString = "mongodb://localhost:27017")
    .UseJsonSerializer());
```

Full documentation: <https://github.com/Cadtastic-Solutions/StateStore/tree/main/docs>.
```

## Root README outline (full OSS-grade)

```
# StateStore

[NuGet badge: StateStore] [NuGet badge: StateStore.MongoDb] [NuGet badge: StateStore.Sqlite]
[License: MIT]  [.NET 8 / 9 / 10]

> Flexible, testable state storage for .NET — pluggable providers, middleware
> pipeline, zero ceremony to wire up.

## Why StateStore                       (3–4 bullets)
## Install                              (package table + dotnet add commands)
## Quick start                          (standalone, DI, typed, MongoDB, SQLite)
## Features                             (compact bullet grid)
## Documentation                        (table linking to /docs guides 01–14)
## Samples                              (links to /samples/*)
## License                              (MIT)
```

Target length: 150–200 rendered lines.

## Sample projects

Four runnable console apps under `samples/`. Each targets `net8.0` only (samples don't need to multi-target), is added to `StateStore.sln` under a new `samples` solution folder so `dotnet build` from the root builds them all, and is NOT packaged.

| Sample | Project refs | Demonstrates |
|---|---|---|
| `StateStore.Samples.QuickStart` | `StateStore` | `StateStoreBuilder` + InMemory + JSON serializer. ~30 lines. Salvages the InMemory snippet from the existing `ProviderUsageExamples.cs`. |
| `StateStore.Samples.AspNetCoreDI` | `StateStore` | `Host.CreateApplicationBuilder`, `AddStateStore`, `BackgroundService` consuming `IStateStore` and `ITypedStateStore<T>`. FileSystem backend. |
| `StateStore.Samples.MongoDb` | `StateStore`, `StateStore.MongoDb` | `AddStateStore(o => o.UseMongoDb(...))`. Top-of-file comment: `docker run -d -p 27017:27017 mongo:7`. |
| `StateStore.Samples.Sqlite` | `StateStore`, `StateStore.Sqlite` | File-based SQLite, multi-process durability across two store instances over the same file. |

## Code-quality cleanups

### Duplicate `<summary>` XML doc blocks

`src/StateStore/StateStoreImplementation.cs` lines 10-13 and 14-17 contain two consecutive `<summary>` blocks on the class. Remove the duplicate; merge the `<remarks>` into the single remaining summary.

`src/StateStore/Providers/Mongo/MongoStorageProvider.cs` (post-move: `src/StateStore.MongoDb/MongoStorageProvider.cs`) lines 8-10 and 11-13 contain the same defect. Same fix.

A grep pass across the moved files will catch any others.

### Test setup duplication

`tests/StateStore.Tests/NullReferenceTests.cs` and `tests/StateStore.Tests/EdgeCaseTests.cs` each construct the same `InMemoryStorageProvider` + `JsonStateSerializer` + `MiddlewarePipeline` + `StateStoreImplementation` graph inside every test method. `StateStoreTests.cs` already uses ctor-based shared setup; the two outliers will be brought in line:

```csharp
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
    public async Task SetAsync_NullKey_ThrowsAsync() =>
        await Assert.ThrowsAsync<ArgumentNullException>(...);

    // ... other tests use _store directly
}
```

## Tracked-artifact cleanup

The repo currently has accidentally-tracked build outputs and IDE artifacts that should be excluded going forward. Plan must audit `.gitignore` and either confirm exclusion or add rules:

- `TestResults/` (verified present at repo root)
- `.vs/` (verified present at repo root)
- `**/*.csproj.user` (present at `src/StateStore/StateStore.csproj.user`)
- `**/*.diagsession` (`tests/StateStore.Benchmarks/Report20260227-1103.diagsession` is currently tracked — confirm by `git ls-files`, then `git rm --cached` if needed)

The plan should run `git ls-files | rg '(TestResults|\.vs|csproj\.user|diagsession)'` to enumerate currently-tracked artifacts and `git rm --cached <path>` each one before pushing.

## Samples target framework

Samples target `net8.0` only — they don't need to multi-target since they're not packaged. They inherit `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>` from `Directory.Build.props` so language-version regressions still break the build. Verification step 2 below explicitly tests only net8.0 for samples; the multi-TFM coverage comes from the library and test projects.

## Verification

The plan must produce a final state where:

1. `dotnet restore` succeeds with zero NU1902/NU1903 warnings outside the MongoDB.Driver SharpCompress suppression in `StateStore.MongoDb.csproj`.
2. `dotnet build -c Release` succeeds with zero warnings across all configured TFM combinations: net8.0 + net9.0 + net10.0 for `StateStore`, `StateStore.MongoDb`, `StateStore.Sqlite`, `StateStore.Tests`, `StateStore.Benchmarks`; net8.0 only for each `StateStore.Samples.*`.
3. `dotnet pack -c Release` produces exactly three `.nupkg` files (`StateStore.1.0.0.nupkg`, `StateStore.MongoDb.1.0.0.nupkg`, `StateStore.Sqlite.1.0.0.nupkg`) plus three matching `.snupkg` symbol packages, and **does not** emit a `StateStore.Benchmarks.nupkg` or `StateStore.Samples.*.nupkg`.
4. `dotnet test` — test count unchanged (currently 72 on the parent branch). Setup refactor merges duplicated `_store` construction without removing any `[Fact]`.
5. `dotnet build` of the solution from the root compiles every sample project — guards against snippet bit-rot.
6. `git log --follow` on a moved provider file (e.g. `src/StateStore.MongoDb/MongoStorageProvider.cs`) shows pre-move history.
7. The new `README.md` renders cleanly on github.com (no broken links, badges resolve).
8. `rg -U '/// <summary>[^<]*</summary>\s*/// <summary>' src/` returns zero matches — confirms duplicate `<summary>` cleanup is complete.
9. `git ls-files` shows no `*.csproj.user`, no `*.diagsession`, no `TestResults/**`, no `.vs/**`.

### Assumptions / preconditions

- The contributor has .NET SDK `10.0.204` installed (matches the proposed `global.json` pin). If `10.0.204` is not installed locally, `dotnet --list-sdks` will not show it and the plan must adjust to whatever latest stable patch is available on the user's machine before pushing.
- `Microsoft.SourceLink.GitHub 8.0.0` is compatible with .NET 10 SDK — verified by Microsoft, but confirm during the plan's restore step.

## Out-of-scope follow-ups (capture for later)

- Wire up GitHub Actions CI to run the verification steps above on every PR and publish packages on tag.
- Add `CONTRIBUTING.md` and `CHANGELOG.md` if the project starts taking outside contributions.
- Strict analyzers pass once the structural churn settles.
- Strong-name signing if/when a corporate consumer requires it.
