# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

English is the official project language. Keep documentation, user-facing text,
logs, examples, issues, and contribution content in English.

## Project Overview

Two products sharing one RFC-extraction core:

1. **Visual Studio 2022+ extension (VSIX)** that adds a `.sapproj` project type for browsing/editing SAP ABAP source extracted over RFC. Each repository object becomes a `.abap` file in the project — searchable, diffable, version-controllable.
2. **MCP server** (`SapAbapProject.McpServer`) that exposes SAP repository discovery, ABAP/DDIC definitions, table reads, live RFC metadata, and generic RFC execution as MCP tools for LLM agents (Claude Desktop, VS Code Copilot). Both rendered ABAP source and structured JSON metadata are returned per tool call.

## Common Commands

Build, test, and packaging are PowerShell-friendly (Windows-only — the VSIX targets `net472` and the Extension project requires the VS SDK + MSBuild from a Visual Studio install).

```powershell
# Restore + build entire solution (VSIX is produced under src/SapAbapProject.Extension/bin/Debug/)
dotnet build SapAbapProject.slnx

# Build only the cross-platform pieces (Core, RfcExtractor, Tests run on net8.0)
dotnet build src/SapAbapProject.Core/SapAbapProject.Core.csproj
dotnet build src/SapAbapProject.RfcExtractor/SapAbapProject.RfcExtractor.csproj

# Run all integration tests (REQUIRES live SAP + RFC SDK — see Test setup below)
dotnet test tests/SapAbapProject.RfcExtractor.Tests/SapAbapProject.RfcExtractor.Tests.csproj

# Run a single test
dotnet test tests/SapAbapProject.RfcExtractor.Tests --filter "FullyQualifiedName~TestConnection_ShouldSucceed"

# Build VSIX (requires `devenv` or MSBuild from VS install on PATH)
msbuild src/SapAbapProject.Extension/SapAbapProject.Extension.csproj /t:Rebuild /p:Configuration=Debug

# Debug the extension: F5 in Visual Studio launches devenv.exe /rootsuffix Exp
# (configured via StartAction/StartProgram/StartArguments in the Extension csproj)

# Run the MCP server (stdio transport — host wires the env vars, see McpServer README)
dotnet run --project src/SapAbapProject.McpServer
```

`TreatWarningsAsErrors=true` is set globally in `Directory.Build.props` — warnings will break the build.

## Architecture

Four projects + one test project, wired via central package management (`Directory.Packages.props`):

- **`SapAbapProject.Core`** (`net472;net8.0`) — Pure DTOs, enums, and interfaces. No dependencies. Defines `AbapObject` (with optional structured `Metadata` of type `AbapObjectMetadata`), per-type metadata records (`FunctionModuleMetadata`, `TableMetadata`, `StructureMetadata`, `DataElementMetadata`, `DomainMetadata`, `TableTypeMetadata`), `AbapObjectSummary`, `TableReadResult`, plus `ImportOptions`, `ImportProgress`, `SapConnectionSettings`, and the `IAbapExtractor` / `IObjectExtractor` / `IScriptWriter` contracts. Multi-targets so the same model types work in both the VSIX (`net472`) and modern host code/tests (`net8.0`). No JSON polymorphism attributes here — the metadata records are plain so Core stays serialization-agnostic.

- **`SapAbapProject.RfcExtractor`** (`net472;net8.0`) — All SAP RFC logic. Depends on `SapNwRfc` (managed wrapper around the native `sapnwrfc.dll`). Key components:
  - `AbapObjectExtractor` — Top-level `IAbapExtractor` implementation; lazily opens a single `SapConnection`, dispatches to per-type extractors. Serializes ALL operations through a `SemaphoreSlim(1,1)` so the single SAP connection is never accessed concurrently. Exposes single-object lookups, TADIR discovery, ABAP source reads, live RFC metadata, generic RFC execution, and a paginated `ReadTableAsync` escape hatch.
  - `RfcRuntimeInvoker` — Builds runtime CLR input types from SAP RFC metadata so arbitrary JSON can be mapped to scalar, structure, and TABLE parameters without compile-time DTOs. It converts dynamic RFC output back to JSON-safe dictionaries and bounds output table rows.
  - `Extractors/BaseExtractor` — Shared `RFC_READ_TABLE` plumbing (also `ReadTableWithMetadata` which returns the field schema alongside rows — used by the MCP `sap_read_table` tool). Crucially, the SAP `RFC_READ_TABLE` `OPTIONS` parameter has a 72-character-per-row limit; `SplitWhereClause` chunks WHERE clauses to fit. Field values come back pipe-delimited (`|`) in `Wa` and must be split using the `Fields` metadata.
  - One concrete extractor per object kind: `FunctionModuleExtractor`, `DataElementExtractor`, `DomainExtractor`, `TableDefinitionExtractor` (used for both transparent tables and structures via static factories `ForTables`/`ForStructures`), `TableTypeExtractor`. Each one (a) populates the structured `Metadata` field on the returned `AbapObject` so MCP tools can return JSON, (b) exposes an `internal Task<AbapObject?> ExtractByNameAsync(string name)` for single-object lookups, and where applicable an `internal Task<IReadOnlyList<AbapObjectSummary>> ListAsync(...)` for discovery. The bulk `ExtractAsync(ImportOptions)` is what the VS extension's import wizard still calls; the new methods are what the MCP server calls.
  - `SapRfcSdkManager` — Static helper that locates the SAP NetWeaver RFC SDK on disk (the user installs it separately; we cannot redistribute it), validates the required DLLs (`sapnwrfc.dll`, `icudt50.dll`, `icuin50.dll`, `icuuc50.dll`, `libsapucum.dll`), and prepends the SDK directory to `PATH` + calls `SetDllDirectory` so `SapNwRfc` can `LoadLibrary` the native DLL. Configured path is persisted to `%AppData%\SapAbapProject\sdk-path.txt`.
  - `ScriptFileWriter` — Writes each `AbapObject` to `<projectRoot>/<FolderName>/<PackageName>/<Name>.abap`. The folder taxonomy is owned by `AbapObject.FolderName`/`RelativePath` in Core.
  - `Net472Polyfills.cs` — Small polyfills (`Dictionary.GetValueOrDefault`, `string.Contains(string, StringComparison)`) needed because `net472` lacks them; also wraps `File.WriteAllText` in `Task.Run` because there is no `WriteAllTextAsync` on `net472`.

- **`SapAbapProject.Extension`** (`net472`) — The VSIX. Imports `Microsoft.NET.Sdk` *manually* (explicit `Sdk.props`/`Sdk.targets` `Import`s) so the VSSDK targets can be imported AFTER SDK targets, which is required for `PrepareForRunDependsOn` ordering. Key pieces:
  - `SapAbapPackage` — `AsyncPackage` entry point. Registers commands, prompts for SDK path on first load via `SdkPathDialog`, and tracks the active document to set a custom UI context GUID when an `.abap` file is focused (drives context-sensitive menu visibility from `SapAbapPackage.vsct`).
  - `SapAbapProject.pkgdef` — Registers `.sapproj` with the **CPS (Common Project System) SDK-style project factory** GUID `{9A19103F-16F7-4668-BE54-9A1E7A4F7556}`. There is NO custom project factory — `.sapproj` is an SDK-style MSBuild project and CPS handles the project system automatically.
  - `BuildSystem/SapAbapProject.props` + `.targets` — Imported by every `.sapproj`. Define `AbapScript` item type, glob `.abap` files from the canonical folder layout (`FunctionGroups/`, `FunctionModules/`, `DataElements/`, `Domains/`, `Structures/`, `Tables/`, `TableTypes/`, `Programs/`, `Includes/`), add the `SapAbapSourceProject` project capability, and suppress all .NET build output (`EnableDefaultItems=false`, `ProduceReferenceAssembly=false`). These files are shipped inside the VSIX and imported by generated `.sapproj` files via a relative path (see `ProjectTemplate/Template.sapproj`).
  - `ProjectTemplate/` and `ItemTemplate/` — `.vstemplate` source. They are zipped at build time by custom MSBuild targets (`CreateTemplateZip`, `CreateItemTemplateZip` in the Extension csproj) because VSSDK's default `GetVSTemplateItems` doesn't handle `.sapproj`. The zips are injected into the VSIX via dynamic `VSIXSourceItem` items.
  - `Grammars/abap.tmLanguage.json` — TextMate grammar registered via the custom `[ProvideTextMateGrammarDirectory]` attribute (defined in `ProvideTextMateGrammarDirectoryAttribute.cs`) for syntax highlighting of `.abap` files.
  - `Dialogs/` — WPF dialogs (`ImportWizardDialog`, `ConnectDialog`, `SdkPathDialog`) with MVVM via `ImportWizardViewModel` + `RelayCommand`. The wizard is the user-facing entry point for an end-to-end import.
  - `Commands/` — `ImportFromSapCommand` and `ConnectSapCommand`, wired through `SapAbapPackage.vsct`.
  - `Services/ConnectionStore` — Persists recent connection profiles to `%AppData%\SapAbapProject\connections.xml`.

- **`SapAbapProject.McpServer`** (`net8.0`, console app, output: `sap-mcp.exe`) — MCP server over stdio transport. Built on the official `ModelContextProtocol` SDK (decorator-based: `[McpServerToolType]` + `[McpServerTool]`) and `Microsoft.Extensions.Hosting`. Three layers:
  - `Program.cs` — Loads native RFC SDK via `SapRfcSdkManager.EnsureSdkLoaded(SAP_RFC_SDK_PATH)` BEFORE wiring DI (fails fast with a stderr message if missing). Registers `IAbapExtractor` as a singleton (single connection per process — required because MCP tools may run interleaved). Calls `AddMcpServer().WithStdioServerTransport().WithToolsFromAssembly()` to auto-register all `[McpServerToolType]` classes.
  - `SapMcpConfiguration` — Builds `SapConnectionSettings` from `SAP_*` env vars (host, user, password mandatory; the rest have sane defaults). The agent never sees credentials. `SAP_DESCRIPTION_LANGUAGES` (CSV, e.g. `ES,EN`) overrides the description-language cascade; if absent the cascade is derived from `SAP_LANGUAGE` with `E` as the final fallback.
  - `SapTools` — All MCP tools live here. Tools use explicit stable names and MCP 2.1 annotations (`ReadOnly`, `Destructive`, `Idempotent`, `OpenWorld`, structured content). `sap_read_table` exposes unrestricted reads permitted by the SAP user; `sap_execute_rfc` may mutate SAP and is deliberately marked destructive.
  - `nlog.config` — NLog wired via `Microsoft.Extensions.Logging`. Two targets: rolling file at `${SAP_MCP_LOG_DIR}` (default `%APPDATA%\SapAbapProject\logs\sap-mcp.log`) and stderr. **Stdout is never written to by anything other than the MCP framework** — this is a hard rule of the stdio transport (stdout carries the JSON-RPC frames).

- **`SapAbapProject.RfcExtractor.Tests`** (`net8.0`) — xUnit integration tests. Uses `SapTestFixture` (xUnit `ICollectionFixture`, collection name `"SAP"`) which loads `testsettings.json` + `testsettings.local.json` (gitignored, holds real credentials) + `SAP_*` environment variables, then loads the RFC SDK once for the whole collection.

### Cross-cutting design notes

- The dependency direction is one-way: `Core` ← `RfcExtractor` ← (`Extension` | `McpServer`). Core has no `SapNwRfc` reference; all RFC types live in `RfcExtractor` and are hidden behind the `IAbapExtractor` interface. `Extension` and `McpServer` do not depend on each other.
- The Core/RfcExtractor projects multi-target `net472;net8.0` so they can be consumed by both the VSIX (which is locked to `net472` because Visual Studio still loads extensions in the .NET Framework process) and the test project (which uses `net8.0` for modern tooling). `PolySharp` is referenced privately to provide `required`/`init`/etc. on `net472`.
- `SapNwRfc` is a P/Invoke wrapper. The native `sapnwrfc.dll` is **not** redistributed with this repo or the VSIX — users must install the SAP NetWeaver RFC SDK separately and configure its path via `SdkPathDialog` on first run.
- `BuildSystem/SapAbapProject.props` and `.targets` ship as content inside the VSIX. When a generated `.sapproj` imports them, the relative path resolves into the deployed VSIX install directory — keep that pathing in mind when editing template files.

## Test Setup

Integration tests need a real SAP system AND the RFC SDK on disk. To run locally:

1. Install the SAP NetWeaver RFC SDK (binary download from SAP) somewhere local.
2. Copy `tests/SapAbapProject.RfcExtractor.Tests/testsettings.json` to `testsettings.local.json` (this file is gitignored) and fill in:
   - `Sap:SdkPath` — directory containing `sapnwrfc.dll` and the ICU DLLs.
   - `Sap:AppServerHost`, `User`, `Password`, optionally `SapRouter`, `SystemNumber`, `Client`, `Language`.
   - `Import:Packages`, `ObjectTypes`, etc., to scope what the extraction tests touch.
3. Alternatively, set `SAP_*` environment variables (e.g. `SAP_Sap__Password`) — they override the JSON.

Tests will skip the SDK load and throw at fixture construction time if the SDK isn't reachable. There are no unit tests; everything is integration-level against a live SAP.

## Things to Watch Out For

- `RFC_READ_TABLE` returns at most 512 bytes per row in `Wa` and limits `OPTIONS` rows to 72 chars — both already handled in `BaseExtractor`. If you add a new extractor, route through `BaseExtractor.ReadTable` rather than calling `RFC_READ_TABLE` directly.
- The MCP server stdio transport **owns stdout**. Anything you `Console.Out.Write` from any code path will corrupt the JSON-RPC stream and crash the host's parser. Always go through `ILogger`, which routes to stderr + the rolling file. The one place we deliberately use `Console.Error.Write` is the pre-DI startup failure path in `Program.cs`, before logging is wired up.
- `IAbapExtractor` is registered as a singleton in the MCP server because there's a single underlying `SapConnection`. The implementation serializes calls via `SemaphoreSlim` — don't relax this without first replacing the connection with a pool.
- Anything that touches Visual Studio APIs must run on the UI thread: use `await JoinableTaskFactory.SwitchToMainThreadAsync(...)` and `ThreadHelper.ThrowIfNotOnUIThread()`. The package holds references to COM event sinks (`_dteEvents`, `_windowEvents`) explicitly to prevent GC.
