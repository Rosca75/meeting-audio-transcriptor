# Reference digest: doc-anonymiser-for-.NET

Purpose: let an engineer who never sees the reference reproduce its conventions
in the new app (meeting-audio-transcriptor: CLI plus a very basic desktop UI,
Windows x64 and Linux x64). Everything below was read from the reference at
`/home/user/doc-anonymiser-for-.NET` (HEAD `ea9c58e`). Where the reference is
Windows-only and the new app must also run on Linux, the digest says so in a
"New app note". Comments inside quoted config are shortened with `...` where
the reference's prose is long; keys and values are verbatim.

---

## 1. Solution and folder structure

### 1.1 Solution file

`DocAnonymiser.slnx` (the XML solution format, with a header comment and
folders `/src/`, `/build/`, `/tests/`):

```xml
<!-- The solution: every source project, build tool and test suite of the repository. -->
<Solution>
  <Folder Name="/src/">
    <Project Path="src/DocAnonymiser.Contracts/DocAnonymiser.Contracts.csproj" />
    <Project Path="src/DocAnonymiser.Engine/DocAnonymiser.Engine.csproj" />
    <Project Path="src/DocAnonymiser.Inference/DocAnonymiser.Inference.csproj" />
    <Project Path="src/DocAnonymiser.PdfKit/DocAnonymiser.PdfKit.csproj" />
    <Project Path="src/DocAnonymiser.App/DocAnonymiser.App.csproj" />
    <Project Path="src/DocAnonymiser.Shell/DocAnonymiser.Shell.csproj" />
  </Folder>
  <Folder Name="/build/">
    <Project Path="build/gliner-graph/DocAnonymiser.GlinerGraph.csproj" />
  </Folder>
  <Folder Name="/tests/">
    <Project Path="tests/DocAnonymiser.Engine.Tests/DocAnonymiser.Engine.Tests.csproj" />
    ... App.Tests, Corpus.Tests, Inference.Tests, Inference.Probe, Guards.Tests,
        PdfFixtures, PdfKit.Tests, PdfKit.Recorder, UiHarness
  </Folder>
</Solution>
```

### 1.2 Repository layout

```
repo/
  CLAUDE.md                  root charter (single source of truth, overrides all)
  README.md  LICENSE  THIRD-PARTY-NOTICES.md
  .editorconfig  .gitattributes  .gitignore
  global.json  Directory.Build.props  Directory.Build.targets  Directory.Packages.props
  DocAnonymiser.slnx
  src/        six projects (below), each with a purpose-header comment in its csproj
  frontend/   the page: plain ES modules, tests beside sources (*.test.js), CLAUDE.md, BRIDGE.md
  tests/      one project per suite + CLAUDE.md
  corpus/     recorded-bytes regression cases (README.md, MANIFEST.txt)
  build/      publish.ps1, release.ps1, fetch-*.mjs (+ *.test.mjs), *.lock, size-budgets.json,
              setup.iss (Inno Setup), pdfkit-linux.sh, cache/ (gitignored), tool subfolders
  docs/       TESTING.md, UITESTING.md, PATTERNS.md, SPELLINGS.md, change-template.md, change-NN*.md
  .github/    workflows/{ci,release,export-judge,spellings-refresh,patterns-refresh}.yml
              actions/{fetch-models,open-refresh-pr,require-refresh-token}/action.yml
```

### 1.3 Projects, responsibilities, reference graph

| Project | TFM | Output | Responsibility | References |
|---|---|---|---|---|
| `DocAnonymiser.Contracts` | net10.0 | lib | Wire shapes (records), `BridgeMethods`/`BridgeEvents` name constants, `BridgeJson` + `BridgeJsonContext` (source-generated System.Text.Json). `IsTrimmable=true` | nothing (every project references it, so it must stay empty of dependencies) |
| `DocAnonymiser.Engine` | net10.0 | lib | All domain logic, no window, no path the user chose; documents arrive as bytes + filename. Embedded resources (word lists, data tables). `InternalsVisibleTo` its test project | Contracts; package `System.Drawing.Common` |
| `DocAnonymiser.Inference` | net10.0 | lib | The two package-backed halves of the model boundary the engine declares as interfaces: `OnnxTensorRunner : ITensorRunner`, `SentencePieceSubwordTokenizer`, `ModelLoader`, `ModelStore`, `InferenceRuntime` (native-load check). Holds no model algorithm | Engine; packages `Microsoft.ML.OnnxRuntime`, `Microsoft.ML.Tokenizers` |
| `DocAnonymiser.PdfKit` | net10.0 | lib | The one project that names the native PDF library; unsafe code allowed here only | Engine |
| `DocAnonymiser.App` | net10.0 | lib | The "bound surface": one public instance method per bridge method (`App.*.cs` partial classes), session state, the lock, cancellation slots, `Dispatcher` (switch over method names), `Bridge` (envelope router), `IHostServices` (the window seam). Names no UI type | Engine, Contracts, Inference, PdfKit |
| `DocAnonymiser.Shell` | net10.0-windows | WinExe `doc-anonymiser` | Raw Win32 window, WebView2, injected `BridgeScript.js`, dialogs, clipboard, docs window, embedded `frontend/`. The only project that may name a UI type | App; package `Microsoft.Web.WebView2` |
| `build/gliner-graph` | net10.0 | Exe `gliner-graph` | Build-time console that rewrites a downloaded ONNX export into the shipped graph; framework only | none |

Test projects (all `OutputType=Exe` with `UseMicrosoftTestingPlatformRunner=true`, xunit.v3):
`Engine.Tests`, `App.Tests` (App driven through a fake `IHostServices`),
`Corpus.Tests` (replays `corpus/`), `Guards.Tests` (net10.0-windows; reads source
text and assemblies), `Inference.Tests` (+ `Inference.Probe`, a console run as a
child process), `PdfFixtures` (builder + recorded answers + fake), `PdfKit.Tests`,
`PdfKit.Recorder` (hand-run console), `UiHarness` (drives the real window, net10.0-windows).

Dependency direction: `Contracts <- Engine <- {Inference, PdfKit} <- App <- Shell`.
The frontend is a leaf nothing writes into.

### 1.4 Namespace rules

* Namespace equals folder path: `DocAnonymiser.Engine.Pipeline`,
  `.Pipeline.Checksums`, `.Discovery.Ner`, `.Formats.Docx`, `.Rendering.Random`, ...
  `RootNamespace` is set in every csproj. Tests mirror: `DocAnonymiser.Engine.Tests.Rendering`.
* File-scoped namespaces are enforced (`csharp_style_namespace_declarations = file_scoped:error`).
* `ImplicitUsings` is disabled; every file lists its `using`s, outside the namespace.
* One folder-per-area charter: each `src/*/CLAUDE.md` owns its project's rules.

**New app note.** Mirror as `MeetingTranscriptor.Contracts` / `.Engine` (audio
decode, VAD, chunking, transcript assembly, writers) / `.Inference` (ONNX
Runtime session + tokenizer + model store) / `.App` (bound surface + session) /
`.Cli` (console exe) / `.Shell` (desktop UI). Keep the same graph; add `.Cli`
beside `.Shell`, both referencing `.App` (or `.Engine` for the CLI if the UI
bridge surface is not needed there).

---

## 2. Shared build configuration

### 2.1 `global.json`

```json
{
  "sdk": {
    "version": "10.0.204",
    "rollForward": "latestPatch"
  },
  "test": {
    "runner": "Microsoft.Testing.Platform"
  }
}
```
`rollForward: latestPatch` pins the feature band (10.0.2xx) and accepts patches.
The `test.runner` entry is what makes `dotnet test` drive Microsoft.Testing.Platform
(the .NET 10 SDK reads it from here). CI uses `actions/setup-dotnet` with
`global-json-file: global.json`, so the SDK version lives in exactly one place.

### 2.2 `Directory.Build.props`

```xml
<Project>
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <LangVersion>latest</LangVersion>
    <Nullable>enable</Nullable>
    <ImplicitUsings>disable</ImplicitUsings>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <AnalysisLevel>latest-recommended</AnalysisLevel>
    <EnforceCodeStyleInBuild>true</EnforceCodeStyleInBuild>
    <InvariantGlobalization>true</InvariantGlobalization>
    <Deterministic>true</Deterministic>
    <GenerateDocumentationFile>false</GenerateDocumentationFile>
    <AllowedReferenceRelatedFileExtensions>none</AllowedReferenceRelatedFileExtensions>
    <PdfKitVersion>0.4.0-preview</PdfKitVersion>   <!-- one number the native binding, load check and packaging all read -->
  </PropertyGroup>

  <!-- The trim analyzer runs in CI and in a Release build; a local Debug build skips it
       (its data-flow analysis is the slowest build step). IsTrimmable in a project's own
       file switches the analyzer on; the SDK reads this property only when still unset.
       GitHub sets CI=true. Empty Configuration is Debug as well. -->
  <PropertyGroup Condition="'$(EnableTrimAnalyzer)' == '' And ('$(Configuration)' == '' Or '$(Configuration)' == 'Debug') And '$(CI)' != 'true'">
    <EnableTrimAnalyzer>false</EnableTrimAnalyzer>
  </PropertyGroup>

  <!-- Every assembly declares the platform it supports (picture codecs are the OS's),
       so the platform-compatibility analyser is answered once here. -->
  <ItemGroup>
    <AssemblyAttribute Include="System.Runtime.Versioning.SupportedOSPlatformAttribute">
      <_Parameter1>windows</_Parameter1>
    </AssemblyAttribute>
  </ItemGroup>
</Project>
```
Non-obvious properties:
* `Nullable=enable`: nullability is the contract (an optional wire field is `T?`).
* `ImplicitUsings=disable`: a reader of one file sees every dependency.
* `TreatWarningsAsErrors` + `AnalysisLevel=latest-recommended` + `EnforceCodeStyleInBuild`:
  analyzers and `.editorconfig` severity `:error` rules fail the build. CI additionally
  passes `-warnaserror`.
* `InvariantGlobalization=true`: no ICU data, culture-invariant formatting (local-only tool).
* `AllowedReferenceRelatedFileExtensions=none`: stops package `.pdb`/`.xml` docs being
  copied into output (a published folder is later refused if it holds anything unexpected).
* Trim analyzer scoping is guarded by `TrimAnalyzerScopeTests`; no workflow may pass the
  properties that decide it.
* The `SupportedOSPlatform=windows` assembly attribute is applied to **every** assembly.

**New app note (important).** The new app must run on Linux, so do NOT copy the
`SupportedOSPlatform=windows` item group repo-wide. Apply it only to the Windows
shell project (or drop it) and add `<EnableWindowsTargeting>true</EnableWindowsTargeting>`
if any `net*-windows` project must be built on Linux (see ch. 12).

### 2.3 `Directory.Build.targets`

```xml
<Project>
  <!-- Drops the two desktop-framework WebView2 controls the package adds: nothing hosts them,
       and the WPF one binds a base-assembly version the base runtime lacks. Repo-wide because a
       test project that reads the shell inherits the same references. -->
  <Target Name="DropTheDesktopWebViewControls" BeforeTargets="ResolveAssemblyReferences">
    <ItemGroup>
      <Reference Remove="@(Reference->WithMetadataValue('Filename', 'Microsoft.Web.WebView2.Wpf'))" />
      <Reference Remove="@(Reference->WithMetadataValue('Filename', 'Microsoft.Web.WebView2.WinForms'))" />
    </ItemGroup>
  </Target>
</Project>
```

### 2.4 `Directory.Packages.props` (central package management)

```xml
<Project>
  <PropertyGroup>
    <ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>
    <CentralPackageTransitivePinningEnabled>true</CentralPackageTransitivePinningEnabled>
  </PropertyGroup>
  <ItemGroup>
    <PackageVersion Include="Microsoft.Web.WebView2" Version="1.0.4191.47" />
    <PackageVersion Include="System.Drawing.Common" Version="10.0.0" />
    <PackageVersion Include="Microsoft.ML.OnnxRuntime" Version="1.30.0" />
    <PackageVersion Include="Microsoft.ML.Tokenizers" Version="2.0.0" />
  </ItemGroup>
  <ItemGroup Label="Test only">
    <PackageVersion Include="xunit.v3" Version="4.0.1" />
  </ItemGroup>
</Project>
```
Project files use `<PackageReference Include="X" />` with no `Version`. A new package is
also added to the dependency table in root `CLAUDE.md` chapter 7 in the same change.

### 2.5 `.editorconfig`

```ini
root = true

[*]
charset = utf-8
end_of_line = lf
insert_final_newline = true
indent_style = space
trim_trailing_whitespace = true

[*.{csproj,props,targets,slnx,json,yml,yaml}]
indent_size = 2

[*.cs]
indent_size = 4

csharp_style_namespace_declarations = file_scoped:error
csharp_style_var_for_built_in_types = false:suggestion
csharp_style_var_when_type_is_apparent = true:suggestion
csharp_style_var_elsewhere = false:suggestion
csharp_prefer_braces = true:error
dotnet_sort_system_directives_first = true
dotnet_separate_import_directive_groups = false
csharp_using_directive_placement = outside_namespace:error
dotnet_style_qualification_for_field = false:suggestion
dotnet_style_require_accessibility_modifiers = for_non_interface_members:error
csharp_style_prefer_primary_constructors = false:none
```
(Each rule carries a short reason comment in the original: e.g. braces always because a later
line under an unbraced `if` changes meaning silently; `var` only where the type is apparent
because diff reviewers cannot hover.)

### 2.6 `.gitattributes`

```
* text=auto eol=lf

*.docx binary
*.pptx binary
*.xlsx binary
*.png  binary
*.jpg  binary
*.jpeg binary
*.emf  binary
*.wmf  binary
*.woff2 binary
*.ico  binary
*.gz   binary

corpus/cases/** -text
corpus/contracts/** -text
corpus/MANIFEST.txt -text
```
LF everywhere; recorded fixtures are exempt from normalisation because a manifest hashes them.

### 2.7 `.gitignore`

```
bin/
obj/
publish/
*.user
.vs/
build/cache/
artifacts/

.claude/settings.local.json
build/spellings/cache/
build/patterns/cache/
build/spellings/measure/data/
build/spellings/measure/results/

.claude/worktrees/
node_modules/
generate.log
.vscode/
.idea/
__pycache__/
```

### 2.8 Per-project csproj conventions

* Every csproj opens with an XML comment explaining the project's role and constraints.
* Library csproj: `RootNamespace`, `IsTrimmable=true`, package/project references, and
  `<InternalsVisibleTo Include="X.Tests" />` (with a reason comment) instead of making types public.
* Embedded data: `<EmbeddedResource Include="dir\*.txt" LogicalName="Ns.Name.%(Filename)%(Extension)" WithCulture="false" Type="Non-Resx" />`
  (marked `WithCulture="false"`, else `iban.en.txt` reads as a satellite resource).
* App project: `<NoWarn>$(NoWarn);CA1822</NoWarn>` (bound methods are instance methods by design).
* Test csproj shape: see ch. 8.

---

## 3. Coding standards

### 3.1 File purpose headers (enforced by `PurposeHeaderGuardTests`)

Every `.cs` file under `src/` and `tests/` starts with a `//` comment block (before
usings) stating what the file is for, present tense, citing no document or change order.
A `///` summary on the type does not replace it. Real examples:

```csharp
// The interface thread's synchronisation context: the one way work from another
// thread reaches the window and the web view.
using System;
using System.Collections.Concurrent;
```

```csharp
// The binding from a method name to an App method: each positional argument is
// read as its own type through the generated context, and the result is
// serialised into the reply.
using System;
using System.Collections.Generic;
```
JS files do the same (`// state.js, the single source of truth for frontend state: ...`),
as do csproj files (leading XML comment), workflow YAML (leading `#` block) and scripts.

### 3.2 Comment style

* "Few lines go unexplained", but a comment's length is earned by the intent it explains.
  Code must be readable without language-level expertise: say what it is FOR and WHY.
* A comment describes the code as it is now: no document names, phases, change orders or
  tickets (`CommentGuardTests` fails them). A past decision is stated as its reason.
* `///` XML docs with `<summary>` and `<remarks>` on types and public members; remarks
  carry the design rationale (threading, why a switch not reflection).

### 3.3 Error messages

Actionable: what failed, what was expected, how to fix. Examples:
* `"the window could not be created: " + Marshal.GetLastPInvokeErrorMessage()`
* `"the bridge script is not embedded in the shell under X; the shell project must embed BridgeScript.js under that name"`
* `$"the {what} could not be opened ({failure.Message}), try again; if it keeps failing, restart the application"`
* `"the ONNX Runtime could not load {modelPath}: {message}. Download the pack for this version again, or reinstall the application if every model fails the same way"`
Lower-case sentence start for exception text that the UI wraps; user-visible copy is English with no em dash (U+2014).
Dispatcher rule: programming faults (NullReference, KeyNotFound, InvalidCast, IndexOutOfRange,
ArgumentNull) are replaced by `"{Method} failed unexpectedly ({TypeName}); try again, and if it keeps failing restart the application"`; every other exception's message is passed to the page verbatim.
Test failure messages must state got/wanted/input; bare `Assert.True(x)` is banned.

### 3.4 Naming and types

* PascalCase types/members, `_camelCase` private fields, `internal sealed` by default,
  `public sealed` only for the cross-project surface.
* Records for data (about 327 `sealed record`s in src): wire shapes are `public sealed record X { [JsonPropertyName("x")] public T X { get; init; } = default; }`
  with `init` setters and non-null defaults (`""`, `[]`).
* Classes for behaviour holding state (`App` is `sealed partial class`, split by area into
  `App.Import.cs`, `App.Run.cs`, ...). Static classes for pure helpers (`Dispatcher`, `Assets`).
* Name constants in static classes (`BridgeMethods`, `BridgeEvents`, `HostFailureKinds`) so a typo is a compile error.
* Guard clauses via `ArgumentNullException.ThrowIfNull`, `ArgumentException.ThrowIfNullOrWhiteSpace`.
* `Lock _appLock = new();` (the .NET 9+ `Lock` type) guards session state; nothing is held across an await or a dialog.

### 3.5 Async and cancellation

* Bridge calls run on the thread pool (`Task.Run`); the UI thread never waits on a task.
  A guard (`ShellGuardTests`) regex-scans the shell for `.Result`, `.Wait(`, `.GetAwaiter().GetResult()`.
* `ConfigureAwait(false)` throughout App/engine code; UI-thread continuations rely on the installed
  `SynchronizationContext` (`UiThread`).
* Long work takes a `CancellationToken` as the last parameter (`Pipeline.Run(input, CancellationToken)`);
  the engine's CPU work is synchronous with a token, the App wraps it in a worker.
  Cancellation slots are `CancellationTokenSource?` properties on the session read/written under the lock
  and cancelled outside it (token callbacks run synchronously).
* Native-runtime cancellation: `cancellation.Register(() => runOptions.Terminate = true)`.
* `catch (Exception e) when (e is not OutOfMemoryException)` is the standard broad catch.

### 3.6 Logging

None. No `ILogger`, no console logging in src. Failures reach the user as a rejection message
or a `host:error` event carrying only a code; a developer trace goes to `Debug.WriteLine` (the
shell's `Log`), compiled to nothing in release. Reports/audit records are explicit user-saved files.

### 3.7 Source-generated JSON

All wire shapes go through one generated context: `[JsonSerializable(typeof(X))]` per type on
`BridgeJsonContext : JsonSerializerContext` with `[JsonSourceGenerationOptions(GenerationMode = Default,
DefaultIgnoreCondition = Never, WriteIndented = false)]`. `BridgeJson.Context` wraps the generated
context with `Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping` (so output is human-readable
and byte-comparable). Rules: property order = declaration order = wire order; an optional property is
nullable and `[JsonIgnore(Condition = WhenWritingNull)]`; absent list is `null`, never `[]`.
No reflection-based serialisation anywhere (portable build is trimmed).

### 3.8 Regular expressions

Compiled once through `[GeneratedRegex(pattern, RegexOptions.CultureInvariant)]` on a `partial` class
and documented with what they match and deliberately do not. Exception: a generated matcher over
12 KiB IL is constructed once at run time (`GeneratedMatcherSizeGuardTests`). A regex from text this
repo did not write is allowed in one file only (`UserRegexGuardTests`).

### 3.9 Other recurring patterns

* Determinism: same input gives the same bytes; the corpus replays every case twice.
* Seams for tests: `internal` constructors taking `TimeProvider`, factories, backends
  (`App(IHostServices, Func<..>?, NerBackend?, ..., TimeProvider? clock = null, ...)`).
* One class per capability boundary (one class opens a connection; one project names the native lib),
  each enforced by a guard.

---

## 4. Desktop host (Shell project)

### 4.1 csproj (verbatim, comments trimmed)

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0-windows</TargetFramework>
    <OutputType>WinExe</OutputType>
    <RootNamespace>DocAnonymiser.Shell</RootNamespace>
    <AssemblyName>doc-anonymiser</AssemblyName>
    <RuntimeIdentifiers>win-x64;win-arm64</RuntimeIdentifiers>
    <UseWindowsForms>false</UseWindowsForms>
    <UseWPF>false</UseWPF>
    <AllowUnsafeBlocks>true</AllowUnsafeBlocks>
    <ApplicationIcon>icon.ico</ApplicationIcon>
    <TieredCompilationQuickJitForLoops>false</TieredCompilationQuickJitForLoops>
  </PropertyGroup>

  <!-- Bridge script embedded under a DOTTED logical name, so the asset handler (which serves
       resources by the path the page asks for) can never hand it out as a file. -->
  <ItemGroup>
    <None Remove="BridgeScript.js" />
    <EmbeddedResource Include="BridgeScript.js" LogicalName="DocAnonymiser.Shell.BridgeScript.js" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\DocAnonymiser.App\DocAnonymiser.App.csproj" />
  </ItemGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.Web.WebView2" />
  </ItemGroup>

  <!-- Trimmed portable build: root the WebView2 core assembly whole, switch the built-in COM
       interop on (the trimmed exe fails at start-up without it), silence the matching warnings. -->
  <ItemGroup Condition="'$(PublishTrimmed)' == 'true'">
    <TrimmerRootAssembly Include="Microsoft.Web.WebView2.Core" />
  </ItemGroup>
  <PropertyGroup Condition="'$(PublishTrimmed)' == 'true'">
    <BuiltInComInteropSupport>true</BuiltInComInteropSupport>
    <NoWarn>$(NoWarn);IL2104;IL2026</NoWarn>
  </PropertyGroup>

  <!-- The frontend ships INSIDE the assembly. Logical name = the path the page asks for, with
       forward slashes. Dev files (tests, helpers, package.json, *.md) are excluded. -->
  <ItemGroup>
    <EmbeddedResource Include="..\..\frontend\**" Exclude="..\..\frontend\**\*.test.js;..\..\frontend\node_modules\**;..\..\frontend\testdom.js;..\..\frontend\testhtml.js;..\..\frontend\testrail.js;..\..\frontend\testworkspace.js;..\..\frontend\package.json;..\..\frontend\*.md">
      <LogicalName>$([System.String]::Copy('%(RecursiveDir)').Replace('\','/'))%(Filename)%(Extension)</LogicalName>
    </EmbeddedResource>
  </ItemGroup>
</Project>
```
`TieredCompilationQuickJitForLoops=false` is a measured startup choice for loop-heavy methods;
omit unless measured. Neither WinForms nor WPF is enabled: a raw Win32 window keeps the app on the
base runtime (no Windows Desktop runtime prerequisite) and keeps it trimmable.

### 4.2 Files (`src/DocAnonymiser.Shell/`)

`Program.cs` (entry, exit codes), `MainWindow.cs` (Win32 window), `UiThread.cs`
(SynchronizationContext + message pump), `WebHost.cs` (WebView2 creation/settings), `Assets.cs`
(embedded asset origin), `BridgeScript.cs`/`BridgeScript.js` (injected bridge), `HostServices.cs`
(`IHostServices` implementation: dialogs, clipboard, `Emit`), `DocsWindow.cs` (second window),
`ShellOptions.cs` (start-up record + harness seam), `ShellAssembly.cs` (`AssetOrigin` constant,
`InternalsVisibleTo("...UiHarness")`), `Native/{User32,Kernel32,Ole32,FileDialogs}.cs`
(source-generated `LibraryImport` P/Invokes; dialogs via source-generated COM interop).

### 4.3 Interface-thread rules (from `src/DocAnonymiser.Shell/CLAUDE.md` chapter 1)

* One thread creates the window and owns the pump; every WebView2 call and callback is on it; it never
  waits on a task.
* `UiThread : SynchronizationContext`: `Post` enqueues into a `ConcurrentQueue` and posts a private
  `WM_APP+1` message to a hidden message-only window (`HWND_MESSAGE`); its WndProc drains the queue.
  `Send` throws `NotSupportedException`. Every continuation, reply and event takes this path.
* The WebView2 message handler only parses and dispatches work to the thread pool.
* Modal dialogs: posted to the UI thread, caller awaits a `TaskCompletionSource`.
* A reply posted after the window closed is dropped (flag set/read on the UI thread).
* An exception escaping a window procedure would cross into native code, so it `Environment.FailFast`s.
* Host objects are switched off (`AreHostObjectsAllowed=false`); the bridge is messages only.

### 4.4 Entry point and message loop (`Program.cs`, trimmed)

```csharp
[STAThread]
internal static int Main(string[] args) => Run(ShellOptions.FromCommandLine(args));

internal static int Run(ShellOptions options)
{
    User32.SetProcessDpiAwarenessContext(User32.DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2);

    if (!WebViewRuntimeIsPresent())
    {
        User32.MessageBoxW(0, "The Microsoft Edge WebView2 runtime is not installed, ... " + RuntimeDownloadPage,
            options.Title, User32.MB_OK | User32.MB_ICONERROR);
        return ExitRuntimeMissing;               // 2
    }

    UiThread ui = UiThread.Install();
    HostServices host = new(ui);
    App.App app = new(options.WrapHost is null ? host : options.WrapHost(host));

    MainWindow window = MainWindow.Create(options.Title, options.Width, options.Height, quitsOnClose: true);
    host.Window = window;

    _ = StartAsync(ui, window, host, app, options);   // continues on the pump
    window.Show();
    return UiThread.RunLoop();
}

private static bool WebViewRuntimeIsPresent()
{
    try { CoreWebView2Environment.GetAvailableBrowserVersionString(); return true; }
    catch (WebView2RuntimeNotFoundException) { return false; }
}

internal static string UserDataFolder() =>
    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                 ModelStore.AppFolderName, "WebView2");
```
Exit codes: 0 closed, 2 WebView2 runtime missing, 3 WebView2 could not be created, 4 browser process died.
`StartAsync` wraps creation in try/catch: on failure a `MessageBoxW` names the cause and the download
page, then `PostQuitMessage(ExitWebViewFailed)`.

### 4.5 Window creation and message loop (`MainWindow.cs`, `UiThread.cs`)

```csharp
nint instance = Kernel32.GetModuleHandleW(null);
RegisterClass(instance);                       // WNDCLASSEXW with lpfnWndProc = &WindowProcedure
double scale = User32.GetDpiForSystem() / 96.0;
nint handle = User32.CreateWindowExW(0, "DocAnonymiserWindow", title, User32.WS_OVERLAPPEDWINDOW,
    User32.CW_USEDEFAULT, User32.CW_USEDEFAULT, (int)(logicalWidth * scale), (int)(logicalHeight * scale),
    0, 0, instance, 0);
if (handle == 0) throw new InvalidOperationException("the window could not be created: " + Marshal.GetLastPInvokeErrorMessage());

[UnmanagedCallersOnly]
private static nint WindowProcedure(nint hwnd, uint message, nuint wParam, nint lParam)
{
    if (!OpenWindows.TryGetValue(hwnd, out MainWindow? window)) return User32.DefWindowProcW(hwnd, message, wParam, lParam);
    try { return window.HandleMessage(message, wParam, lParam); }
    catch (Exception failure) { Environment.FailFast("unhandled exception in a window procedure: " + failure.Message, failure); return 0; }
}
// HandleMessage: WM_SIZE -> Resized event; WM_SETFOCUS -> Focused; WM_DPICHANGED -> SetWindowPos(suggested rect);
// WM_CLOSE -> DestroyWindow; WM_DESTROY -> Closed event + PostQuitMessage(0) if quitsOnClose.

public static int RunLoop()
{
    User32.MSG message; int status;
    while ((status = User32.GetMessageW(&message, 0, 0, 0)) != 0)
    {
        if (status == -1) throw new InvalidOperationException("the message loop failed: " + Marshal.GetLastPInvokeErrorMessage());
        User32.TranslateMessage(&message);
        User32.DispatchMessageW(&message);
    }
    return (int)message.wParam;
}
```
`MainWindow` is `internal sealed unsafe class` holding a static `Dictionary<nint, MainWindow>` keyed by
HWND; the window class registers once, loads the exe icon (`IDI_APPLICATION` id) with stock fallback.
All P/Invokes are `internal static unsafe partial class User32` with `[LibraryImport]` (source generated,
trim-safe) and only the constants actually used.

### 4.6 WebView2 creation (`WebHost.cs`)

```csharp
CoreWebView2Environment environment = await CoreWebView2Environment.CreateAsync(null, userDataFolder, null);
CoreWebView2Controller controller = await environment.CreateCoreWebView2ControllerAsync(window.Handle);
CoreWebView2 core = controller.CoreWebView2;

CoreWebView2Settings settings = core.Settings;
settings.AreDefaultContextMenusEnabled = false;
settings.IsStatusBarEnabled = false;
settings.IsZoomControlEnabled = false;
settings.AreHostObjectsAllowed = false;
settings.IsWebMessageEnabled = true;
settings.IsGeneralAutofillEnabled = false;
settings.IsPasswordAutosaveEnabled = false;
settings.IsSwipeNavigationEnabled = false;
bool debugBuild = false;
#if DEBUG
debugBuild = true;
#endif
settings.AreDevToolsEnabled = debugBuild || devToolsProtocol;           // --harness switch
settings.AreBrowserAcceleratorKeysEnabled = debugBuild || devToolsProtocol;

core.NavigationStarting += (_, args) => { if (!Assets.MayNavigateTo(args.Uri)) args.Cancel = true; };
core.WebMessageReceived += (_, args) => { if (Assets.IsOnOrigin(args.Source)) bridge.Receive(args.WebMessageAsJson); };
core.ProcessFailed += (_, args) => host.OnProcessFailed(args.ProcessFailedKind);

Assets.Serve(environment, core);                                         // BEFORE first navigation
await core.AddScriptToExecuteOnDocumentCreatedAsync(BridgeScript.Text);  // BEFORE first navigation

host.FitToWindow();
window.Resized += (w, h) => controller.Bounds = new Rectangle(0, 0, w, h);
window.Focused += () => controller.MoveFocus(CoreWebView2MoveFocusReason.Programmatic);
window.Closed  += () => controller.Close();
// on any exception after controller creation: controller.Close(); throw;
// then: core.Navigate(ShellAssembly.AssetOrigin + "index.html");
```
User-data folder: `%LOCALAPPDATA%\doc-anonymiser\WebView2` (the one folder the app creates; uninstaller
deletes the whole `doc-anonymiser` folder). Process-failure policy: renderer exit/unresponsive -> offer
reload (Yes/No MessageBox); browser exit -> message + exit code 4; helper processes ignored.
Order guarantee: bridge attached, then script injected, then navigation.

### 4.7 Serving embedded assets (`Assets.cs`)

Virtual origin `https://doc-anonymiser.app/` (`ShellAssembly.AssetOrigin`), HTTPS because ES modules need
a secure origin and JS media type. NOT `SetVirtualHostNameToFolderMapping` (folder mapping skips the handler
and would ship loose editable files). Instead a `WebResourceRequested` handler over embedded resources:

```csharp
core.AddWebResourceRequestedFilter("http://*",  CoreWebView2WebResourceContext.All, CoreWebView2WebResourceRequestSourceKinds.All);
core.AddWebResourceRequestedFilter("https://*", CoreWebView2WebResourceContext.All, CoreWebView2WebResourceRequestSourceKinds.All);
core.WebResourceRequested += (_, args) => args.Response = Respond(environment, args.Request.Uri);

private static CoreWebView2WebResourceResponse Respond(CoreWebView2Environment env, string uri)
{
    Stream? content = Open(uri, out string mediaType);       // Assembly.GetManifestResourceStream(name)
    if (content is null) return env.CreateWebResourceResponse(null, 404, "Not Found", "Cache-Control: no-store");
    return env.CreateWebResourceResponse(content, 200, "OK",
        "Content-Type: " + mediaType + "\nCache-Control: no-store\nContent-Security-Policy: " + ContentSecurityPolicy);
}
const string ContentSecurityPolicy =
    "default-src 'self'; img-src 'self' data:; style-src 'self' 'unsafe-inline'; " +
    "script-src 'self' 'unsafe-inline'; connect-src 'none'; frame-src 'none'; object-src 'none'; base-uri 'none'";
```
Details: explicit media-type table (`.html .js .css .svg`; unknown = octet-stream, deliberately); empty path
-> `index.html`; query/fragment stripped, path unescaped; every http/https request off-origin gets 404 (local-only
guarantee at the asset layer); `data:`/`blob:` skip the filter; the bridge script resource name is excluded from
serving; `MayNavigateTo` allows only the origin and `about:blank`; messages from other origins are dropped.

### 4.8 Posting events/replies to the page

All posting goes `Bridge.Post(json)` -> `_ui.Post(...)` (UiThread) -> `Action<string> postToPage`
= `json => { if (!closed) web?.Core.PostWebMessageAsJson(json); }` (see Program.StartAsync). `IHostServices.Emit(name, payload)`
(from the App, any thread) is implemented in `HostServices` by forwarding to `Bridge.Emit`; before the page exists it is a no-op.

### 4.9 Native dialogs and clipboard

System common item dialogs through source-generated COM interop (`Native/FileDialogs.cs`, flat interface
declarations in vtable order); a cancelled dialog is `null`/empty, not an error. Clipboard: movable global block as UTF-16
handed over on the UI thread, retried a few times off-thread if another program holds it; write faults with a
sentence on failure.

**New app note.** For a Windows+Linux "very basic desktop UI" the raw-Win32 host cannot be copied as is. The
transferable parts: `IHostServices`-style seam, embedded static page, `Bridge`/`Dispatcher` (cross-platform,
window-free), `Assets` CSP + 404 policy. Options for the Linux host: a localhost-free approach is a native
webview (WebKitGTK) behind the same seam; or a minimal cross-platform UI that references `.App` through the same
`IHostServices`. Decide that explicitly in the new app's charter; keep `Shell` Windows-only if desired and add a
separate Linux host project.

---

## 5. JS-to-.NET bridge

### 5.1 Envelope shapes (documented in `src/DocAnonymiser.Shell/CLAUDE.md` chapter 2; `frontend/BRIDGE.md` ch. 13)

| Direction | Shape |
|---|---|
| Page to host, call | `{"id": n, "method": "...", "args": [ ... ]}` |
| Host to page, reply | `{"id": n, "result": ...}` or `{"id": n, "error": "..."}` |
| Host to page, event | `{"event": "...", "payload": ...}` |
| Page to host, drop | `{"drop": [{"name": "...", "bytes": "<base64>"}]}` |

JSON over `postMessage`/`PostWebMessageAsJson` only; no host objects. Positional args are bound by position to
their own type via the generated context. A void method replies `result: null`. A rejection carries a plain
message string. Events flow host-to-page only; the host accepts only a call or a drop (anything else is
reported). Unknown method: rejected with `"unknown method <Name>"` (never a silent drop). Failures with no
waiting call (unreadable message, failed drop import, docs window failure) go through one event `host:error`
whose payload is `{ "kind": "<code>" }` only (codes in `HostFailureKinds`: `message_unreadable`, `drop_failed`,
`documentation_window`); no content rides on it. Event names are `noun:verb` strings in `BridgeEvents`
(`documents:changed`, `pipeline:progress`, `pipeline:done`, `detection:progress`, `detection:done`,
`detection:error`, `judge:progress`, `judge:done`, `host:error`).

### 5.2 Contracts (`src/DocAnonymiser.Contracts/Bridge.cs`)

```csharp
public sealed record BridgeCall  { [JsonPropertyName("id")] public long Id {get;init;}
                                   [JsonPropertyName("method")] public string Method {get;init;} = "";
                                   [JsonPropertyName("args")] public IReadOnlyList<JsonElement> Args {get;init;} = []; }
public sealed record BridgeReply { [JsonPropertyName("id")] public long Id {get;init;}
                                   [JsonPropertyName("result")][JsonIgnore(Condition=WhenWritingNull)] public JsonElement? Result {get;init;}
                                   [JsonPropertyName("error")][JsonIgnore(Condition=WhenWritingNull)] public string? Error {get;init;} }
public sealed record BridgeEvent { [JsonPropertyName("event")] public string Event {get;init;} = "";
                                   [JsonPropertyName("payload")] public JsonElement Payload {get;init;} }
```
`BridgeMethods` is `public static class` of `const string` per method name (`Ping = "Ping"`, ...), and the App has
a public instance method of each name; a test cross-checks the two lists. `BridgeMethods`' header comment: a method
outside the list is not reachable from the page; lifecycle/host-called methods are deliberately not on it.

### 5.3 Router: `Bridge` (App project, window-free) essentials

```csharp
public Bridge(App app, SynchronizationContext ui, Action<string> postToPage, Action<string> log) { ... }

public void Receive(string json)            // called on the UI thread: parse + hand off only
{
    EnvelopeKind kind = Classify(json);     // "method" key => Call, "drop" key => Drop, else Unknown (reported)
    switch (kind)
    {
        case EnvelopeKind.Call:
            BridgeCall? call = JsonSerializer.Deserialize(json, BridgeJson.Context.BridgeCall);
            _ = Task.Run(() => AnswerAsync(call));  return;
        case EnvelopeKind.Drop:
            _ = Task.Run(() => ImportDrop(drop));   return;
        default: Report(HostFailureKinds.MessageUnreadable, "..."); return;
    }
    // JsonException with an id present => reject THAT call so no promise waits forever
}

public void Emit(string name, object? payload)       // any thread
{
    JsonElement element = payload is null ? Dispatcher.NullResult
        : JsonSerializer.SerializeToElement(payload, payload.GetType(), BridgeJson.Context);
    Post(JsonSerializer.Serialize(new BridgeEvent { Event = name, Payload = element }, BridgeJson.Context.BridgeEvent));
}

private async Task AnswerAsync(BridgeCall call)
{
    BridgeReply reply = await Dispatcher.InvokeAsync(_app, call).ConfigureAwait(false);
    Post(JsonSerializer.Serialize(reply, BridgeJson.Context.BridgeReply));
}
private void Post(string json) => _ui.Post(static s => ((PendingPost)s!).Deliver(), new PendingPost(this, json));
```

### 5.4 Dispatcher: a switch, not reflection (`Dispatcher.cs`)

```csharp
public static async Task<BridgeReply> InvokeAsync(App app, BridgeCall call)
{
    try { JsonElement result = await DispatchAsync(app, call).ConfigureAwait(false);
          return new BridgeReply { Id = call.Id, Result = result }; }
    catch (Exception f) when (IsProgrammingFault(f))
    { return new BridgeReply { Id = call.Id, Error = $"{call.Method} failed unexpectedly ({f.GetType().Name}); try again, and if it keeps failing restart the application" }; }
    catch (Exception f) when (f is not OutOfMemoryException)
    { return new BridgeReply { Id = call.Id, Error = f.Message }; }
}

switch (call.Method)
{
    case BridgeMethods.Ping:        return Result(app.Ping(), json.String);
    case BridgeMethods.ApplySettings: return Result(app.ApplySettings(Arg(call, 0, json.Settings)), json.OllamaState);
    case BridgeMethods.ImportFiles: return Result(await app.ImportFiles().ConfigureAwait(false), json.ImportResult);
    case BridgeMethods.ResetRun:    app.ResetRun(); return NullResult;
    ...
    default: throw new ArgumentException(UnknownMethodPrefix + call.Method);   // "unknown method "
}
private static JsonElement Result<T>(T v, JsonTypeInfo<T> info) => JsonSerializer.SerializeToElement(v, info);
private static T Arg<T>(BridgeCall call, int index, JsonTypeInfo<T> info)   // arg-count mismatch => ArgumentException naming both counts
```
`IJsonTypeInfo` properties on `BridgeJsonContext` (`json.String`, `json.Settings`, `json.IReadOnlyListString`) are generated
from `[JsonSerializable]` attributes; adding a method means: constant in `BridgeMethods`, record(s) + `[JsonSerializable]`
in the context, public method on `App`, `case` in the `Dispatcher`, wrapper in `frontend/api.js`, section in `frontend/BRIDGE.md`.

### 5.5 `IHostServices` (App's only dependency on the window)

```csharp
public interface IHostServices
{
    Task<string[]> OpenFilesAsync(FileDialogOptions options);   // empty = cancelled
    Task<string?>  OpenFileAsync(FileDialogOptions options);    // null = cancelled
    Task<string?>  SaveFileAsync(SaveDialogOptions options);
    Task<string?>  PickFolderAsync();
    Task SetClipboardText(string text);                         // faults with a sentence on failure
    void Emit(string name, object? payload);                    // no-op before the page exists
}
```
App wraps dialog calls in `HostFailures.AskHost(what, dialog)` (rethrows as one actionable sentence) and file I/O failures
are filtered by `HostFailures.IsFileSystemFailure` (`IOException`, `UnauthorizedAccessException`, `ArgumentException`, `NotSupportedException`).
Tests implement `IHostServices` with scripted answers.

### 5.6 Cancellation slots and terminal-event rule (`src/DocAnonymiser.App/CLAUDE.md` chapters 3 and 4)

* One lock (`private readonly Lock _appLock`) guards all session state; nothing held across an await or a dialog.
* Three `CancellationTokenSource?` slots (`CancelRun`, `CancelDetectionSource` shared by every discovery route,
  `CancelJudgeSource` separate), read/written under the lock, **cancelled outside it**.
* A run starts on a worker and the call returns at once; results arrive on the terminal event, progress on a progress event.
  A second run is refused while one is in flight.
* **Terminal-event rule: a run emits exactly one terminal event however it ends**, guaranteed in a `finally`
  (`pipeline:done`; detection has two, `detection:done` or `detection:error`; judge has `judge:done`). A run that fails to start emits it too, carrying
  the reason. A cancelled run reports partial findings and says it was cancelled.
* Progress fraction is computed in one place and never decreases.
* Background work is enumerated and capped (4 pieces in the reference) and its failures are swallowed where the
  foreground owns the message.

### 5.7 JS side: injected `BridgeScript.js` (trimmed, verbatim logic)

```js
(() => {
  "use strict";
  const webview = window.chrome && window.chrome.webview;
  if (!webview) { return; }                       // outside the host: globals stay undefined, api.js rejects readably

  const pending = new Map();                      // id -> {resolve, reject}
  let nextId = 1;
  const listeners = new Map();                    // event name -> Set<callback>

  function call(method, args) {
    return new Promise((resolve, reject) => {
      const id = nextId++;
      pending.set(id, { resolve, reject });
      try { webview.postMessage({ id, method, args }); }
      catch (failure) { pending.delete(id); throw failure; }
    });
  }

  function dispatchEvent(name, payload) {
    const subscribers = listeners.get(name);
    if (!subscribers) { return; }
    for (const callback of Array.from(subscribers)) {
      try { callback(payload); } catch (failure) { console.error("an event handler for " + name + " failed:", failure); }
    }
  }

  webview.addEventListener("message", (event) => {
    const message = event.data;
    if (!message || typeof message !== "object") { return; }
    if ("id" in message) {
      const settlers = pending.get(message.id);
      if (!settlers) { return; }
      pending.delete(message.id);
      if ("error" in message) { settlers.reject(message.error); } else { settlers.resolve(message.result); }
      return;
    }
    if ("event" in message) { dispatchEvent(message.event, message.payload); }
  });

  // Every property read is a method; "then" and symbols return undefined so the object is never mistaken for a promise.
  const app = new Proxy(Object.create(null), {
    get(_, member) {
      if (typeof member !== "string" || member === "then") { return undefined; }
      return (...args) => call(member, args);
    },
  });
  window.go = { backend: { App: app } };

  window.runtime = {
    EventsOn(name, callback) { /* add to listeners; return unsubscribe fn */ },
    EventsOff(name, ...more) { /* delete listeners for each name */ },
  };
  // + drag/drop shim: cancels every file drag (dropEffect "copy" over an element whose computed
  //   `--drop-target` custom property is "drop", "none" elsewhere), reads dropped files to base64 in 0x8000 chunks and
  //   posts {drop:[{name,bytes}]}; an unreadable entry posts bytes "".
})();
```
`frontend/api.js` is the only module that touches `window.go`/`window.runtime`. Every wrapper is `async` so an absent bridge
becomes a rejected promise, not a synchronous throw:

```js
function bridge() {
  const app = window.go?.backend?.App;
  if (!app) { throw new Error("The host bridge is not available. This page must run inside the doc-anonymiser application window, not a regular browser."); }
  return app;
}
export async function ping() { return bridge().Ping(); }
```
The script has its own Node test (`src/DocAnonymiser.Shell/BridgeScript.test.js`) run by `ShellGuardTests`.

---

## 6. Frontend conventions

* **No build step, no npm dependencies, no framework, no bundler**: vanilla ES2020 modules loaded by
  `<script type="module">` in `index.html`. `frontend/package.json` only marks the folder as an ES-module scope:
  `{"name":"doc-anonymiser-frontend","private":true,"type":"module"}` (with a `"//"` comment key).
* Nothing loads from the network: no CDN, no remote font; icons are vendored SVG under `frontend/assets/icons/` with a
  `LICENSE`. `index.html` marks the drop container `style="--drop-target: drop"`.
* **Module map** (flat files + `views/`): `index.html`, `main.js` (boot, menu, step bar, view switch),
  `shell.js` (pure markup builders), `nav.js`, `api.js` (ONLY bridge caller), `state.js` (ONLY state holder), `copy.js`
  (all user-visible strings; imports nothing), `html.js`, `icons.js`, `ui.js` (shared components), `modal.js`, `toast.js`,
  `splitter.js`, `scroll.js`, `highlight.js`, `*model.js` (pure logic), `views/*.js` (one module per screen), CSS:
  `tokens.css` (non-colour primitives), `brand.css` (palette + font faces), `style.css` (consumes both, declares no tokens),
  `docs/` (second-window documentation page).
* **State**: `state.js` = plain object + `subscribe/notify`, `getState()`, `setState()` (shallow merge) and exported reducers;
  pure JS with no DOM or host access, so headless-testable. Views read `getState()`, re-render on notify, dispatch actions,
  and keep no parallel state.
* **Views are logic-free**; logic lives in pure `*model.js`/`state.js` modules covered by Node tests.
* **Mirrored vocabularies** (tables in `state.js`) have parity guards in the .NET Guards suite that read the JS file and compare
  with the engine's table (no transcribed copies).
* **CSS**: tokens in `tokens.css`/`brand.css` only; `style.css` never re-declares a token; `CssTokenGuardTests` bans raw laddered
  values and colour literals outside the palette; `CascadeGuardTests` catches same-specificity ties; fixed-height layout contract
  (a card that changes height loses the reader's scroll place); no native dialogs (page opens none; `StepParityTests`).
* **Embedding + hash pinning**: the Shell csproj embeds `frontend/**` (minus tests/helpers/`package.json`/`*.md`); the
  `FrontendGuardTests` read `tests/DocAnonymiser.Guards.Tests/frontend-hashes.txt` (format `<sha256> *<relative-path>`, one line
  per file, e.g. `26b6ccc... *api.js`) and fail if any page file's bytes differ, if a file is missing from the list, or if the
  executable lacks `index.html`, `main.js`, `api.js`, `docs/index.html` or carries a test file. A page edit moves its hash
  line in the same commit. Authored-here exceptions: `CLAUDE.md`, `BRIDGE.md`. The page is "a leaf": no project writes into it.
* **Page tests (Node)**: `*.test.js` beside each module, `node:test` + `node:assert/strict`, run with
  `node --test "frontend/**/*.test.js"` (the quoted recursive glob is mandatory; a flat glob silently skips subfolders).
  Test helpers (`testdom.js`, `testhtml.js`, ...) are dependency-free DOM stubs. `PageTestsPresentTests` fails a test the build
  would never run, a silenced one, or a charter documenting another command.
  Header style: `// state.test.js: dev-time unit tests for the store, runnable with \`node --test "frontend/**/*.test.js"\` ...`
* **Copy rules**: all user-visible strings are English and live in `copy.js`; **no em dash (U+2014)** (use commas, periods,
  parentheses) - enforced by `copy.test.js` and `CopyGuardTests`. `FOOTER.version` in `copy.js` must equal the version default
  in `build/publish.ps1` and `build/setup.iss` (`FooterVersionGuardTests`). Data attributes must be lower-case
  (`DatasetParityTests`). Every icon asked for must exist and vice versa (`IconParityTests`).
* `frontend/CLAUDE.md` has numbered chapters: 1 What this folder is, 2 Seam to the backend, 3 Discipline rules, 4 Layout
  contract, 5 No native dialogs, 6 Chrome carries no status read-out, 7 Component index, 8 Settled components, 9 File map,
  10 Typography and brand, 11 Floating-surface models, 12 Comment rules, 13 Copy rules, 14 Documentation window, 15 Testing,
  16 Domain vocabulary, 17 Where to look next.
* `frontend/BRIDGE.md` chapters: 1 Connectivity, 2 Documentation window, 3 Import, 4 Settings, ... 11 Runtime events,
  12 Rules for changing the contract, 13 The envelope. Per method: arguments, resolved shape, errors.

---

## 7. ONNX Runtime usage and model handling

### 7.1 Session creation (`src/DocAnonymiser.Inference/OnnxTensorRunner.cs`)

```csharp
SessionOptions options = new()
{
    IntraOpNumThreads = intraOpThreads,                 // default Math.Max(1, Environment.ProcessorCount)
    InterOpNumThreads = 1,
    ExecutionMode = ExecutionMode.ORT_SEQUENTIAL,
    GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL,
    LogSeverityLevel = OrtLoggingLevel.ORT_LOGGING_LEVEL_ERROR,
};
if (spinningAllowed is bool s) options.AddSessionConfigEntry("session.intra_op.allow_spinning", s ? "1" : "0");  // span model only: "0"
try   { return new OnnxTensorRunner(new InferenceSession(modelPath, options), intraOpThreads, spinningAllowed); }
catch (Exception e) when (e is OnnxRuntimeException or TypeInitializationException or DllNotFoundException or EntryPointNotFoundException)
      { throw new ModelLoadException($"the ONNX Runtime could not load {modelPath}: {e.Message}. Download the pack for this version again, ...", e); }
finally { options.Dispose(); }
```
* CPU provider only; a runner is `ITensorRunner, IDisposable`, one `InferenceSession` per loaded model, kept loaded until the model
  choice changes or the app closes. `InferenceSession.Run` is documented thread-safe on CPU, so several chunks (and the judge) share one runner without a lock.
* Cancellation: `using CancellationTokenRegistration stop = cancellation.Register(() => runOptions.Terminate = true);` and an
  `OnnxRuntimeException` while cancelled is rethrown as `OperationCanceledException`.
* Tensor I/O: the engine defines its own `Tensor`/`TensorSpec`/`ITensorRunner` (named inputs in, named outputs back); the runner
  reads input element types from the graph (`_session.InputMetadata`), converts to the declared type, builds `OrtValue`s, copies out only the
  output names the caller asks for, disposes every `OrtValue` in `finally`. Mismatches throw `NerModelException` naming expected vs declared names.
* Model algorithms (prompting, decoding, chunking) are in Engine behind the interfaces; Inference only moves arrays. Floats are
  deterministic across thread counts (kernels split by output block, no reductions), held to the last bit by the models tier.
* Native-load pre-check `InferenceRuntime.Check()` (once per process, lazy): looks for `onnxruntime.dll` beside the exe,
  `NativeLibrary.TryLoad`s it, and on failure tries each C++ runtime dependency individually to name the missing file; the result becomes the
  tooltip of a disabled control rather than a failing first run.
* Packages: `Microsoft.ML.OnnxRuntime 1.30.0`, `Microsoft.ML.Tokenizers 2.0.0` (SentencePiece vocabularies).

### 7.2 Model store, packs, manifests (`ModelStore.cs`)

* Per-user folder `%LOCALAPPDATA%\doc-anonymiser\models\<model-id>\` (non-roaming), a `manifest.json` beside the files
  (`{"model": id, "comment": ..., "files":[{"path","sha256","bytes","role"}]}`); `AppFolderName = "doc-anonymiser"` is the one constant naming the app's folder.
* The application downloads nothing: a **model pack** (zip of the model files + `manifest.json`) is installed by the user via a file dialog; install verifies every
  file's hash against the shipped descriptor (copied from the lock), extracts to a temporary folder beside the target, and swaps it in only when it passed
  (a half-extracted pack never reads as installed); a manifest is only what a pack claims.
* Three questions at three costs: installed? (sizes only, cheap, startup list); bytes reviewed? (hash every file once per process before first load);
  may a pack install? (hash while extracting).
* Pack zips are written by `build/publish.ps1` entry by entry to keep paths inside the model folder.

### 7.3 `build/models.lock` (JSON; representative entry, verbatim shape)

```json
{
  "comment": "The models the application can run, pinned file by file by address, size and hash ... (long prose on roles)",
  "models": {
    "xlm-roberta-base-ner-hrl": {
      "repository": "tjruesch/xlm-roberta-base-ner-hrl-onnx",
      "files": [
        { "path": "onnx/model_quantized.onnx",
          "url": "https://huggingface.co/tjruesch/xlm-roberta-base-ner-hrl-onnx/resolve/c460d3b8fedc898fed4e6f4b879cb67ad4fb04f9/onnx/model_quantized.onnx",
          "sha256": "fa6ce51b0f642ca452372e916902124fdf3bdc48957010241fccaefb9ca5f64f",
          "bytes": 278294552, "role": "model" },
        { "path": "sentencepiece.bpe.model", "url": ".../sentencepiece.bpe.model", "sha256": "cfc8...2865", "bytes": 5069051, "role": "tokenizer" },
        { "path": "config.json", "...": "...", "role": "config" },
        { "path": "tokenizer.json", "...": "...", "role": "reference" }
      ]
    },
    "gliner_multi_pii-v1": {
      "repository": "onnx-community/gliner_multi_pii-v1",
      "files": [
        { "path": "onnx/model.onnx", "url": null, "sha256": "b203...72a3", "bytes": 1135145095, "role": "model", "derivedFrom": "source/onnx/model.onnx" },
        { "path": "source/onnx/model.onnx", "url": "https://huggingface.co/.../resolve/<commit-sha>/onnx/model.onnx", "sha256": "7704...68d2", "bytes": 1157129714, "role": "source" }
      ]
    }
  }
}
```
Rules: every entry has exactly `path, url, sha256, bytes, role`; URLs are **commit-SHA-pinned** (not `main`); roles are
`model | tokenizer | config | reference | source` (reference = only read by fixture generators, never shipped; source = export a shipped graph is
derived from, never shipped); a derived entry has `url: null` and `derivedFrom` pointing at a `source` entry of the same model. A model ships exactly one graph.
Sibling locks of the same style: `build/pdfkit.lock` (`version`, `abi`, `packages[{rid,url,bytes,sha256,files[{path,bytes,sha256}]}]`), `build/runtime.lock`
(`version`, `runtimes[{rid,url,sha256}]`), `build/size-budgets.json`, and for the data generators `sources.lock`.

### 7.4 Fetch tool: `build/fetch-models.mjs` (Node 24, ESM, zero dependencies)

CLI: `node build/fetch-models.mjs [--model <id>] [--into <dir>] [--reference] [--offline] [--lock <path>]`.
* `--offline`: download nothing, verify what is present against the lock, write each model's `manifest.json`, exit non-zero listing every missing/mismatched file
  with the expected hash. Used by developers who placed files by hand and by `publish.ps1` on a laptop.
* Without `--offline` (CI): downloads missing files, verifies each (size and SHA-256), deletes a mismatching download, skips a present file whose hash is right
  (idempotent). Default install folder `%LOCALAPPDATA%\doc-anonymiser\models` (throws if `LOCALAPPDATA` unset; `--into` overrides).
* Proxy/TLS: a downloading run re-executes itself once with `NODE_USE_ENV_PROXY=1` and `--use-system-ca` (`reexecWithNetworkOptions` via `spawnSync(process.execPath, [...process.execArgv, "--use-system-ca", file, ...args], {stdio:"inherit", env})`).
* Core pieces (verbatim):

```js
export const entryFields = ["path", "url", "sha256", "bytes", "role"];
export function readLock(path) { /* JSON.parse; require models object; each model.files array; each entry has every field;
                                    derivedFrom must name a role "source" entry and then url must be null */ }
export async function sha256Of(path) {            // streamed: model files exceed 1 GB
  const hash = createHash("sha256");
  for await (const chunk of createReadStream(path)) hash.update(chunk);
  return hash.digest("hex");
}
export async function verify(path, entry) {       // {ok} | {ok:false, reason:"missing"|"size"|"hash", actual}
  if (!existsSync(path)) return { ok: false, reason: "missing" };
  const bytes = statSync(path).size;  if (bytes !== entry.bytes) return { ok: false, reason: "size", actual: String(bytes) };
  const actual = await sha256Of(path); if (actual !== entry.sha256) return { ok: false, reason: "hash", actual };
  return { ok: true };
}
export function timeoutFor(entry) { return Math.max(10*60*1000, Math.ceil((entry.bytes / (512*1024)) * 1000)); }  // half a MB/s floor, >=10 min

export async function download(entry, target) {
  mkdirSync(dirname(target), { recursive: true });
  const part = `${target}.part`;
  const response = await fetch(entry.url, { signal: AbortSignal.timeout(timeoutFor(entry)) });
  if (!response.ok || !response.body) throw new Error(`downloading ${entry.path} from ${entry.url} answered ${response.status} ...; check the url in the lock and the proxy settings`);
  const hash = createHash("sha256"); let bytes = 0;
  const counting = Readable.fromWeb(response.body);
  counting.on("data", (c) => { hash.update(c); bytes += c.length; });
  await pipeline(counting, createWriteStream(part));
  const actual = hash.digest("hex");
  if (bytes !== entry.bytes || actual !== entry.sha256) { rmSync(part, { force: true }); return { ok: false, reason: ..., actual: ... }; }
  renameSync(part, target);                       // only a verified file is moved into place
  return { ok: true };
}
// run(): per model, per entry: verify -> skip | (offline: record problem) | else rm wrong file, download, record problem;
// write manifest.json only when every fetched file is complete; failures do not stop the other files.
// Entry guard: `if (process.argv[1] && resolve(process.argv[1]) === fileURLToPath(import.meta.url)) main()` so tests import the module.
```
`build/fetch-models.test.mjs` runs the tool against a loopback `http` server the test starts. `build/fetch-pdfkit.mjs` is the same idea for a zip package
(reads package into memory, checks size+hash before trusting a byte, inflates entries with `node:zlib` `inflateRawSync`, verifies each inner file, and writes nothing to the cache unless all pass,
so a cache is whole or absent; options `--offline --from <zip> --rid <rid> --lock <path>`; cache `$PDFKIT_CACHE/<version>/<rid>/` else `build/cache/pdfkit/...`).
CI caches the model folder keyed `models-${{ hashFiles('build/models.lock') }}` and pdfkit `pdfkit-${{ hashFiles('build/pdfkit.lock') }}`.
The judge's graph is produced by `export-judge.yml` (Python 3.12, pinned requirements) and attached to a GitHub release tagged `models`, then pinned in the lock by its release URL and fetched by `build/fetch-judge-graph.ps1` using `gh` + `GH_TOKEN` (the repo is private; the Node tool carries no credentials).

**New app note.** For Whisper-style ASR: pin each model file (encoder/decoder ONNX, tokenizer, config) in `build/models.lock` with the same five fields;
reuse `fetch-models.mjs` nearly unchanged but change the default install folder to a cross-platform per-user data dir
(`%LOCALAPPDATA%` on Windows, `${XDG_DATA_HOME:-$HOME/.local/share}` on Linux). Keep ORT thread settings configurable (CLI flag) and keep native-load pre-check
logic per platform (`onnxruntime.dll` vs `libonnxruntime.so`).

---

## 8. Tests

### 8.1 Suites (`tests/CLAUDE.md` and `docs/TESTING.md`)

* `.NET`: xunit.v3 4.0.1 on Microsoft.Testing.Platform, one Exe project per suite. Node: `node --test` for the page and for build tools
  (`build/*.test.mjs`, `build/spellings/generate.test.mjs`, `build/patterns/generate.test.mjs`), each run by file name with its own CI step
  (a guard checks that documented commands match `ci.yml`).
* Test csproj (verbatim shape, `tests/DocAnonymiser.App.Tests`):

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <RootNamespace>DocAnonymiser.App.Tests</RootNamespace>
    <OutputType>Exe</OutputType>
    <IsPackable>false</IsPackable>
    <UseMicrosoftTestingPlatformRunner>true</UseMicrosoftTestingPlatformRunner>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="xunit.v3" />
  </ItemGroup>
  <ItemGroup>
    <None Include="testdata\**" CopyToOutputDirectory="PreserveNewest" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\src\DocAnonymiser.App\DocAnonymiser.App.csproj" />
    <ProjectReference Include="..\..\src\DocAnonymiser.Contracts\DocAnonymiser.Contracts.csproj" />
    <ProjectReference Include="..\DocAnonymiser.PdfFixtures\DocAnonymiser.PdfFixtures.csproj" />   <!-- fake native lib -->
  </ItemGroup>
</Project>
```
(`Guards.Tests`, `UiHarness` use `net10.0-windows` because they read the Shell.)

### 8.2 Tiers (trait `Tier`) and categories (trait `Category`)

| Tier | Trait | Requires | Budget | Runs |
|---|---|---|---|---|
| unit | none | pure logic, small committed fixtures | whole tier < 10 s | every push |
| integration | `Tier=integration` | real file I/O, format round-trips, real native lib | < 2 min | pull requests to main |
| deep | `Tier=deep` | slow/dependency-heavy: wall-clock benchmarks, accuracy benchmark, real-window harness | none | on demand; harness on path-filtered pushes |
| live | `Tier=live` | a real Ollama | none | on demand |
| models | `Tier=models` | installed model files | none | publish job and on demand |

Rules: the trait is the only tier mechanism (no env-var gating); a missing dependency **skips by name**, never fails
(`Assert.SkipUnless(cond, "needs Windows: <what>; run it on Windows")`); a higher tier tests only what a lower one cannot.
Class-level `[Trait("Category", "<x>")]` is mandatory (`TestCategoryGuardTests`); vocabulary: `detection, redaction, extraction, roundtrip,
layout, fonts, config, errors`. Example:

```csharp
[Trait("Category", "redaction")]
public sealed class RendererTests
{
    [Theory]
    [InlineData("pseudonymisation", "Jean Dupont", "[PERSON_1]")]
    public void EachModeWritesItsSurfaceForOneOccurrence(string mode, string occurrence, string expected) { ... }
}
// integration: [Trait("Tier", "integration")] on class or method
```
Hygiene: data-driven with scenario names; failure messages state got/wanted/input; expected values written out, never computed the way the code
does; shared fixtures loaded once; no sleeps/global mutable state/ordering dependence; build cache stays on; fixtures live under the owning suite's `testdata/`.
Change discipline: a change is not finished until its tests move with it; never weaken an assertion; a failing guard names a real inconsistency, fix the
inconsistency not the guard; exemptions are named file by file with a reason and the guard asserts each exemption is still needed.

### 8.3 Commands (from `docs/TESTING.md` chapter 9)

```
dotnet test --filter "Tier!=deep&Tier!=integration&Tier!=live&Tier!=models"     # unit tier (every other tier excluded by name)
dotnet test --filter "Tier=integration"                                         # (CI adds --ignore-exit-code 8: suites with no integration test exit 8)
dotnet test --filter "Tier=deep"
dotnet test --project tests/DocAnonymiser.Engine.Tests --filter "Tier=deep"
dotnet test --filter "Tier=models"
dotnet test --filter "Area=Corpus"
dotnet test --project tests/DocAnonymiser.Guards.Tests
dotnet test --filter "Category=layout"                                          # scope to a category
node --test "frontend/**/*.test.js"
node --test build/fetch-models.test.mjs
node --test build/fetch-pdfkit.test.mjs
node --test build/spellings/generate.test.mjs
node build/patterns/generate.mjs --check ; node --test build/patterns/generate.test.mjs
```
Fallback when `dotnet test` runs nothing: run the suite's own executable with the same `--filter`
(`DocAnonymiser.Engine.Tests.exe --filter "Tier!=deep&..."`). Always read the test count: zero tests proves nothing. Do not use the
Microsoft.Testing.Platform `--filter-query` form for trait tiers (segments are assembly/namespace/class/method).
Scoping rule: name projects touched (guards touch every change), decide per tier and say why, run only those; full run is for release or sweeps.

### 8.4 Guards (`tests/DocAnonymiser.Guards.Tests`, folders `Hygiene/`, `Parity/`, `Structural/`)

Source-text / assembly / file-tree checks that run in the unit tier. "A guard that reads nothing fails rather than passing."
Helpers: `RepositoryPaths.Root`, `RepositoryFiles.Under(folder, glob)`, `RepositoryFiles.Read`, `LockFile`, `GuardCannotRunException`.
* Hygiene: `PurposeHeaderGuardTests` (every `.cs` under src/tests opens with `//`), `CommentGuardTests` (no doc/phase/ticket citations),
  `CopyGuardTests` (no em dash in user-visible strings), `TestCategoryGuardTests`, `PageTestsPresentTests`, `CssTokenGuardTests`, `CascadeGuardTests`,
  `IconParityTests`, `DatasetParityTests`, `FooterVersionGuardTests`, `RetiredVocabularyTests`.
* Parity (JS table vs engine table): `CategoryParityTests`, `DetectionParityTests`, `PresetParityTests`, `StepParityTests`, `ResultShapeTests`, `ValueShapeTests`, ...
* Structural: `LocalOnlyGuardTests` (one file may construct a connection; every absolute address named file by file with a reason; the download tools are named),
  `IsolationGuardTests` (engine references only framework + repo; neither Engine nor App names a window), `ShellGuardTests` (no `.Result|.Wait(|.GetAwaiter().GetResult()` in the shell;
  App starts no own threads; bridge script parses and passes its Node tests), `FrontendGuardTests` (hash list), `TrimAnalyzerScopeTests`, `GeneratedMatcherSizeGuardTests`,
  `PdfKitSeamGuardTests`/`PdfKitPackageGuardTests` (native lib naming and its import table), `PublishSizeTests` (artefact vs budget; skip without a publish),
  `ThirdPartyNoticesGuardTests`, data-file hash guards.

### 8.5 Fakes and recorded fixtures

* `IHostServices` fake with scripted dialog answers + recording event sink; `TestApp` builds an `App` over fixed account name and `TestClock`.
* Model boundary fakes (fake `ITensorRunner`, fake tokenizer) in Engine.Tests so the engine is tested in a console with no model; the real runtime/tokeniser
  are exercised only in the `models` tier against floats recorded from the reference implementations (Python scripts under `build/model-fixtures/`).
* Native library fake: `FakePdfKit` replays JSON recordings (`tests/DocAnonymiser.PdfFixtures/Fixtures/<area>/`), refuses any unrecorded call, and `FixtureDriftTests`
  re-records against the real library to detect drift.
* Corpus (`corpus/`): recorded-bytes regression; each case runs twice in one process and the outputs must be identical; `corpus/MANIFEST.txt`
  hashes every file (written only by `build/corpus-manifest.ps1`); changing an expectation is a deliberate act stated in the PR.
* Child-process probe (`Inference.Probe`) for a check that can only run in a process that has loaded nothing yet.
* UI harness (`docs/UITESTING.md`) drives the real window through the WebView2 DevTools protocol (`--harness` switch) and writes screenshots to `artifacts/uitest`.

---

## 9. CLAUDE.md conventions

### 9.1 Which files exist

`CLAUDE.md` (root, 9 chapters, ~820 lines), `src/DocAnonymiser.Engine/CLAUDE.md` (25 chapters), `src/DocAnonymiser.App/CLAUDE.md` (13),
`src/DocAnonymiser.Shell/CLAUDE.md` (9), `frontend/CLAUDE.md` (17), `tests/CLAUDE.md` (5). Not every project has one (Contracts, Inference, PdfKit do not;
their csproj header comment states the role). Other authoritative non-CLAUDE docs: `frontend/BRIDGE.md`, `corpus/README.md`, `docs/PATTERNS.md`,
`docs/SPELLINGS.md`, `docs/TESTING.md`, `docs/UITESTING.md`, `README.md`, `THIRD-PARTY-NOTICES.md`, `docs/change-template.md`.

### 9.2 Root charter structure

1 Project overview; 2 Documentation (the authoritative-files table + the three writing rules); 3 Repository structure (tree + namespace list);
4 Architecture rules; 5 Domain model and vocabulary; 6 Coding rules; 7 Pinned versions and dependencies (table: component, version, notes);
8 Build and deployment (artefacts, size budgets, release); 9 Where to look next. Each nested charter opens `# CLAUDE.md - <subject>`, says
"Cross-cutting rules are owned by the root charter and are not restated here", has numbered `## N. Title` chapters, and ends `## N. Where to look next`.

### 9.3 The three writing rules for authoritative files (verbatim intent)

1. **Text is short, and exclusivity is stated as exclusivity.** Write "only A" instead of "A, and not B" / "A, never B". "A, but not Ab" only where Ab is a sub-case a reader would assume included.
   Shortening is a phrasing act: no stated reason, invariant, rule or example is deleted to make a file shorter.
2. **A file is timeless.** Present tense, citing no build phase, change order or past version of itself, never describing a feature as a delta. A past failure is stated as the rule's reason without naming the document that recorded it.
3. **Chapter titles are numbered, project-agnostic and bracket-free.** Top-level chapters cited by other files are `N. Title`, numbered sequentially; titles name the subject generically (no counts). `README.md` is the exception.
   A charter states every rule with its reason in one sentence; no rule moves out of a charter into a skill.
* Adding/removing an authoritative file moves the table in the same change. Root charter "overrides every other instruction" and is re-read before each session.
* Style habits: bold lead sentence = the rule, followed by the reason ("because ..."); tables for vocabularies (term, definition, identifier); vocabulary words are "the contract"
  (same word in C#, JS, JSON, copy and docs); a label rename and an identifier rename are separate changes.
* `docs/change-template.md` defines `docs/change-NN.md` change orders (parent) and `change-NNa.md` batches (self-contained plus parent), with deviation protocol and mandatory sections.

---

## 10. GitHub Actions

All third-party actions are pinned by full commit SHA with the version in a trailing comment. Pins used:

| Action | Pin |
|---|---|
| `actions/checkout` | `3d3c42e5aac5ba805825da76410c181273ba90b1 # v7.0.1` |
| `actions/setup-dotnet` | `a98b56852c35b8e3190ac28c8c2271da59106c68 # v6.0.0` (with `global-json-file: global.json`) |
| `actions/setup-node` | `820762786026740c76f36085b0efc47a31fe5020 # v7.0.0` (`node-version: '24'`) |
| `actions/cache` | `55cc8345863c7cc4c66a329aec7e433d2d1c52a9 # v6.1.0` |
| `actions/upload-artifact` | `043fb46d1a93c77aae656e7c1c64a875d1fc6a0a # v7.0.1` |
| `actions/download-artifact` | `3e5f45b2cfb9172054b4087a40e8e0b5a5461e7c # v8.0.1` |
| `softprops/action-gh-release` | `efb35369e0ad2afab669f228072c1b0d510eae64 # v3.0.3` |
| `actions/setup-python` | `5fda3b95a4ea91299a34e894583c3862153e4b97 # v7.0.0` |

### 10.1 Inventory

| File | Trigger | Runner | Jobs |
|---|---|---|---|
| `ci.yml` (name `ci`, 281 lines) | `push` (all branches), `pull_request` to main, `workflow_dispatch`; `concurrency` group `ci-${{ github.ref }}` cancel-in-progress | windows-latest | `build-and-test` (45 min), `publish` (90 min, needs build-and-test), `interface` (45 min, path-scoped) |
| `release.yml` (260 lines) | `push` tags `v*`; `workflow_dispatch` (inputs `version`, `record_budgets`) | windows-latest (matrix `rid: [win-x64, win-arm64]`, `fail-fast: false`), ubuntu-latest | `build` (120 min), `publish` (needs build; creates the release; `contents: write` for this job only), `budgets` (if `inputs.record_budgets`) |
| `export-judge.yml` | push to main on 3 paths; dispatch; `permissions: contents: write` | windows-latest | `export` (Python 3.12; export + quantise ONNX; upload to release `models` with `gh release upload --clobber`) |
| `spellings-refresh.yml` | `schedule: cron '30 4 2 * *'`; dispatch | windows-latest | `refresh`: require token, build, fetch, generate, test, open PR |
| `patterns-refresh.yml` | `schedule: cron '30 4 4 * *'`; dispatch | windows-latest | same pattern, different data |
| `actions/fetch-models/action.yml` (composite) | called by ci publish + release build | pwsh | cache `~/AppData/Local/doc-anonymiser/models` (key `models-${{ hashFiles('build/models.lock') }}`), `build/fetch-judge-graph.ps1` (env `GH_TOKEN`), `node build/fetch-models.mjs`; input `github-token` |
| `actions/open-refresh-pr/action.yml` (composite) | refresh workflows | bash | branch `<prefix>/<UTC date>`, commit as github-actions[bot], `git push --force`, `gh pr create`/`gh pr edit --body-file pull-request-body.md`; inputs `branch-prefix`, `title`, `token` |
| `actions/require-refresh-token/action.yml` (composite) | refresh workflows, first step | pwsh | throws with setup instructions if secret `SPELLINGS_REFRESH_TOKEN` empty |

Global workflow settings: `permissions: contents: read` at top; `env: DOTNET_CLI_TELEMETRY_OPTOUT: 1, DOTNET_NOLOGO: 1, TESTINGPLATFORM_TELEMETRY_OPTOUT: 1`;
every job has `timeout-minutes`; comments explain every step.

### 10.2 `ci.yml` (full structure; comments trimmed, all steps and commands verbatim)

```yaml
name: ci
on:
  push:
  pull_request:
    branches: [main]
  workflow_dispatch:
permissions:
  contents: read
concurrency:
  group: ci-${{ github.ref }}
  cancel-in-progress: true
env:
  DOTNET_CLI_TELEMETRY_OPTOUT: 1
  DOTNET_NOLOGO: 1
  TESTINGPLATFORM_TELEMETRY_OPTOUT: 1

jobs:
  build-and-test:
    runs-on: windows-latest
    timeout-minutes: 45
    steps:
      - uses: actions/checkout@3d3c42e5aac5ba805825da76410c181273ba90b1 # v7.0.1
      - name: Set up .NET
        uses: actions/setup-dotnet@a98b56852c35b8e3190ac28c8c2271da59106c68 # v6.0.0
        with: { global-json-file: global.json }
      - name: Set up Node
        uses: actions/setup-node@820762786026740c76f36085b0efc47a31fe5020 # v7.0.0
        with: { node-version: '24' }
      - name: Cache the pdfkit package
        uses: actions/cache@55cc8345863c7cc4c66a329aec7e433d2d1c52a9 # v6.1.0
        with:
          path: build/cache/pdfkit
          key: pdfkit-${{ hashFiles('build/pdfkit.lock') }}
      - name: Fetch pdfkit
        run: node build/fetch-pdfkit.mjs
      - name: Restore
        run: dotnet restore
      - name: Build                      # warnings are errors; CI=true also enables the trim analyzer
        run: dotnet build --no-restore -warnaserror
      - name: Unit tier, the guards included
        run: dotnet test --no-build --filter "Tier!=deep&Tier!=integration&Tier!=live&Tier!=models"
      - name: Page suite
        run: node --test "frontend/**/*.test.js"
      - name: Model fetch tool suite
        run: node --test build/fetch-models.test.mjs
      - name: pdfkit fetch tool suite
        run: node --test build/fetch-pdfkit.test.mjs
      - name: Spelling data tools suite
        run: node --test build/spellings/generate.test.mjs
      - name: Pattern data is generated from its inputs
        run: node build/patterns/generate.mjs --check
      - name: Pattern data tools suite
        run: node --test build/patterns/generate.test.mjs
      - name: Integration tier
        if: github.event_name == 'pull_request'
        run: dotnet test --no-build --filter "Tier=integration" --ignore-exit-code 8

  publish:                                # what a user installs, measured against its budget
    runs-on: windows-latest
    needs: build-and-test
    timeout-minutes: 90
    steps:
      - checkout; setup-dotnet (global.json)
      - name: Install the setup compiler
        run: choco install innosetup --version 6.7.1 --no-progress --yes
        shell: pwsh
      - setup-node 24
      - cache build/cache key runtime-${{ hashFiles('build/runtime.lock') }}
      - cache build/cache/pdfkit key pdfkit-${{ hashFiles('build/pdfkit.lock') }}; run node build/fetch-pdfkit.mjs
      - name: Fetch the model files
        uses: ./.github/actions/fetch-models
        with: { github-token: ${{ github.token }} }
      - run: dotnet build -warnaserror
      - name: Publish and measure
        run: .\build\publish.ps1 -Rid win-x64
        shell: pwsh
      - name: Models tier
        run: dotnet test --no-build --project tests/DocAnonymiser.Inference.Tests/DocAnonymiser.Inference.Tests.csproj --filter "Tier=models"
      - name: Keep the artefacts
        uses: actions/upload-artifact@043fb4... # v7.0.1
        with:
          name: doc-anonymiser-win-x64
          path: |
            publish/win-x64/*-portable.zip
            publish/win-x64/*.exe
            publish/win-x64/sizes.json
          if-no-files-found: error

  interface:                              # real-window harness, BLOCKING, path-scoped computed inside the job
    runs-on: windows-latest
    timeout-minutes: 45
    steps:
      - checkout with fetch-depth: 0
      - name: Decide whether the window can have changed    # id: scope; bash
        # workflow_dispatch -> run=yes; else diff base..HEAD and run=yes iff it touches
        # ^(frontend/|tests/DocAnonymiser\.UiHarness/|src/DocAnonymiser\.Shell/); output via $GITHUB_OUTPUT.
        # Computed in-job (not on the trigger) so the job never reports "skipped" for a required check.
      - setup-dotnet / build / test steps all guarded by `if: steps.scope.outputs.run == 'yes'`
      - run: dotnet build -warnaserror
      - run: dotnet test --no-build --project tests/DocAnonymiser.UiHarness/DocAnonymiser.UiHarness.csproj --filter "Tier=deep"
      - name: Keep the screenshots and the change report
        if: steps.scope.outputs.run == 'yes' && always()
        uses: actions/upload-artifact@043fb4... # v7.0.1
        with: { name: interface-measurements, path: artifacts/uitest, if-no-files-found: warn }
```

### 10.3 `release.yml` skeleton (all `uses:` and key commands)

* Trigger: tags `v*`; manual dispatch with inputs `version` (default `0.0.0-dryrun`) and `record_budgets` (boolean).
* Job `build` (windows-latest, matrix `rid: [win-x64, win-arm64]`, `fail-fast: false`, 120 min): checkout -> setup-dotnet(global.json) ->
  `choco install innosetup --version 6.7.1 --no-progress --yes` -> cache `build/cache` (`runtime-${{ hashFiles('build/runtime.lock') }}`) ->
  (x64 leg only) setup-node 24 + `./.github/actions/fetch-models` -> cache `build/cache/pdfkit` (`pdfkit-${{ matrix.rid }}-${{ hashFiles('build/pdfkit.lock') }}`) ->
  `node build/fetch-pdfkit.mjs --rid ${{ matrix.rid }}` ->
  **Read the version** (bash; `EVENT_NAME`/`INPUT_VERSION` via `env:`, never spliced; tag: `number="${GITHUB_REF_NAME#v}"`; validated with
  `grep -Eq '^[0-9]+\.[0-9]+\.[0-9]+(-[0-9A-Za-z.]+)?$'` before `echo "number=$number" >> "$GITHUB_OUTPUT"`) ->
  `.\build\publish.ps1 -Rid ${{ matrix.rid }} -Version ${{ steps.version.outputs.number }} ${{ matrix.rid != 'win-x64' && '-SkipModels' || '' }} ${{ inputs.record_budgets && '-RecordBudgets' || '' }}` (pwsh) ->
  refuse an artefact whose `publish/<rid>/sizes.json` has no `vcruntime` record (pwsh `throw`) ->
  (if record_budgets) upload `size-budgets-${{ matrix.rid }}` from `build/size-budgets.json` ->
  `Compress-Archive -Path "publish/<rid>/app/*" -DestinationPath "publish/<rid>/doc-anonymiser-<ver>-<rid>-app.zip" -CompressionLevel Optimal` ->
  upload artifact `release-${{ matrix.rid }}` (`publish/<rid>/*.zip`, `publish/<rid>/*.exe`, `if-no-files-found: error`).
* Job `publish` (ubuntu-latest, 15 min, `permissions: contents: write`, needs build): `actions/download-artifact` (`path: dist`, `pattern: release-*`, `merge-multiple: true`) ->
  `working-directory: dist` `sha256sum -b * > SHA256SUMS.txt && cat SHA256SUMS.txt` ->
  `softprops/action-gh-release` (`if: github.event_name == 'push'`; `files: dist/*.zip dist/*.exe dist/SHA256SUMS.txt`; `generate_release_notes: true`) ->
  on a dry run `upload-artifact` name `release-dry-run`, path `dist`.
* Job `budgets` (ubuntu-latest, `if: inputs.record_budgets`): downloads `size-budgets-*`, merges the two legs' JSON in pwsh (`ConvertTo-Json -Depth 4`, UTF-8 no BOM), uploads `size-budgets`. Never commits; a person commits `build/size-budgets.json` in a PR.

### 10.4 Refresh workflows (pattern worth copying for data refresh)

`spellings-refresh.yml`/`patterns-refresh.yml`: `schedule` cron (monthly, odd minute), `concurrency` group without cancel, read-only `permissions`; steps: checkout -> `./.github/actions/require-refresh-token`
(fail fast with instructions if `SPELLINGS_REFRESH_TOKEN` missing) -> setup-dotnet/node -> fetch sources -> generate -> run generator tests + unit tier -> write `pull-request-body.md` ->
`./.github/actions/open-refresh-pr` (PR is opened with a PAT because a PR opened with `GITHUB_TOKEN` starts no other workflow, so `ci` would never run on it). Workflow never pushes to main.

**New app note.** Add an `ubuntu-latest` job (or matrix `os: [windows-latest, ubuntu-latest]`) for the portable parts; see ch. 12.

---

## 11. Versioning, tagging and release packaging

### 11.1 Where the version lives

No `<Version>` in any csproj or props. It is supplied at publish time: `dotnet publish ... -p:Version=$Version`. Defaults and copies:
* `build/publish.ps1` parameter `[string]$Version = '0.1.0'` validated by `[ValidatePattern('^\d+\.\d+\.\d+(-[0-9A-Za-z.]+)?$')]` (also validates against injection since it reaches a build property, an installer define and file names).
* `build/setup.iss` `#define Version "0.1.0"` (overridden with `/DVersion=`).
* `frontend/copy.js` `FOOTER.version: "Version 0.1.0"`; `FooterVersionGuardTests` keeps the three equal.
* The release version is the git tag minus the leading `v` (release workflow). Other pinned versions: `<PdfKitVersion>` in `Directory.Build.props` (guarded equal to `build/pdfkit.lock`), `build/runtime.lock` `version` (bundled .NET runtime 10.0.8), `global.json` SDK.

### 11.2 `build/release.ps1 -Version X.Y.Z` (the only way to release)

Validates `^\d+\.\d+\.\d+(-[0-9A-Za-z.]+)?$`; then in order: (1) branch must be `main`; (2) `git status --porcelain` must be empty; (3) `git fetch origin main --tags`, local HEAD == `origin/main`;
(4) tag `vX.Y.Z` must not exist locally or on the remote (`git ls-remote --tags`); (5) version must be >= highest existing `v*` tag compared as `[version]` (label ignored); (6) `git tag -a vX.Y.Z -m "doc-anonymiser X.Y.Z"` and
`git push origin vX.Y.Z`; prints the release URL and `gh run list --workflow release.yml --limit 1`. Helper `RunGit` runs git with `ErrorActionPreference='Continue'` and throws on non-zero exit code (git writes progress to stderr).
Rule: nothing is built for a release on a laptop (only the runner carries the C++ runtime files); a tag is never moved or reused. Nothing is signed (first launch shows the reputation prompt; README documents it).

### 11.3 `build/publish.ps1 -Rid <win-x64|win-arm64> -Version X.Y.Z [-SkipSetup] [-SkipPortable] [-SkipModels] [-ModelsInto <dir>] [-RecordBudgets]`

Output under `publish/<rid>/`: `app/` (folder), `portable/` + `doc-anonymiser-<v>-<rid>-portable.zip`, `doc-anonymiser-<v>-<rid>-setup.exe`, `doc-anonymiser-<v>-models-<id>.zip` (per model), `sizes.json`.
1. **Application folder** (framework-dependent, the primary artefact):
   `dotnet publish <Shell.csproj> -c Release -r $Rid --self-contained false -p:Version=$Version -p:DebugType=none -p:DebugSymbols=false -o publish/$Rid/app --nologo`
2. **Portable** (self-contained, trimmed): `dotnet publish ... -c Release -r $Rid --self-contained true -p:Version=$Version -p:DebugType=none -p:DebugSymbols=false -p:PublishTrimmed=true -p:TrimMode=full -o publish/$Rid/portable --nologo`
   (no single-file: extraction to temp is what DLP tools flag; the folder is installed so native loaders beside the exe are free).
3. Copy the MSVC runtime files (`vcruntime140.dll`, `vcruntime140_1.dll` (x64 only), `msvcp140.dll`) from the Visual Studio redist (`vswhere -latest -products * -find "VC\Redist\MSVC\*\$arch\Microsoft.VC14*.CRT"`) into `app` and `portable`; record file/version/sha256.
4. Copy the lock-verified native lib (`pdfkit.dll`) from the cache into both folders after re-checking size + SHA-256 against `build/pdfkit.lock`.
5. Copy `THIRD-PARTY-NOTICES.md` into both folders (throws if missing).
6. **Remove what no run loads**, by name with reasons: `*.lib`, `createdump.exe`, `mscordaccore*.dll`, `mscordbi.dll`, `Microsoft.DiaSymReader.Native.*.dll`, `clrgc.dll`, `clrgcexp.dll`, `msquic.dll`, and the `runtimes\` folder; then **refuse the publish** if any file remains that
   is not `.dll`/`.exe` or one of `doc-anonymiser.deps.json`, `doc-anonymiser.runtimeconfig.json`, `THIRD-PARTY-NOTICES.md`.
7. Zip portable with `Compress-Archive ... -CompressionLevel Optimal`.
8. **Setup** (Inno Setup `ISCC.exe /Q /DAppDir /DRuntimeDir /DRuntimeVersion /DVersion /DRid /DOutputDir /DOutputName build\setup.iss`): the .NET runtime archive from `build/runtime.lock`
   is downloaded to `build/cache/dotnet-runtime-<ver>-<rid>.zip`, its SHA-256 verified (deleted on mismatch), unpacked; the setup provisions the runtime per user only when none is found (never machine-wide), asks per-user or all-users on its first screen,
   needs no elevation unless all-users, links the notices from the Start menu and refuses to compile without them.
9. **Model packs**: fetch-models (`--offline` locally, `--into` on CI), then the graph console for derived graphs (hash-checked against the lock), then each pack zipped entry by entry via `System.IO.Compression.ZipFileExtensions.CreateEntryFromFile` (paths preserved) with `manifest.json`.
10. **Measure** (`sizes.json`: rid, version, app bytes, portable zip bytes, setup bytes, models, vcruntime, pdfkit) and check against `build/size-budgets.json` (tolerance **10 percent**, `$tolerance = 0.10`): fails with a message naming each artefact over budget and saying to rerun with `-RecordBudgets` if intended.
    `-RecordBudgets` writes the measurements as new budgets and exits 0 (only the sizes measured are overwritten); recorded in CI via release.yml dispatch input and uploaded as `size-budgets`.

`build/size-budgets.json` shape: `{ "win-x64": {"app":..,"portable":..,"setup":..}, "win-arm64": {...}, "models": {"<model-id>": bytes} }` (bytes).
Helpers in the script: `Step([string])` prints `==> text`; `WriteJson` writes UTF-8 without BOM, two-space indent; `FolderBytes`; `FindIscc`.

### 11.4 Checksums, notes, changelog

* `SHA256SUMS.txt` generated on ubuntu with `sha256sum -b *` over the assembled `dist/` (one file for both architectures; named so downloads can be checked).
* Release notes: `generate_release_notes: true` (GitHub auto-generated from merged PRs). No CHANGELOG.md; no version-in-file bump commits besides the three copies above. Design history lives in `docs/change-NN*.md`.
* Artefact name pattern: `doc-anonymiser-<version>-<rid>-{app.zip|portable.zip|setup.exe}`, `doc-anonymiser-<version>-models-<id>.zip`.

**New app note.** Linux x64 artefacts: replace Inno Setup with `tar.gz` (portable, trimmed, self-contained) and optionally `.deb`/AppImage; a `linux-x64` leg in the release matrix on `ubuntu-latest`; add the Linux files to `SHA256SUMS.txt`.
Use `-r linux-x64 --self-contained true -p:PublishTrimmed=true` for the CLI.

---

## 12. Linux support

### 12.1 State in the reference

* **The application is Windows-only**: raw Win32 + WebView2 + COM dialogs; `Shell` targets `net10.0-windows`; every assembly carries
  `[SupportedOSPlatform("windows")]` (set in `Directory.Build.props`); `System.Drawing.Common` (GDI+) is the only picture codec; `build/publish.ps1`, `release.ps1`, `setup.iss` are Windows/PowerShell; artefacts are `win-x64` and `win-arm64` only.
* **CI and release run on `windows-latest`** (ci.yml header: "a Linux runner would be checking something nobody ships"); only the release `publish` and `budgets` jobs (artefact gather, `sha256sum`, merge) use `ubuntu-latest`.
* **What DOES run on Linux** (developer/agent sessions, not CI): the unit and integration tiers of the platform-neutral suites. Windows-targeted projects are built from Linux with the
  documented property `-p:EnableWindowsTargeting=true` (appears in the change orders' commands:
  `dotnet test --project <suite> -p:EnableWindowsTargeting=true --filter "Tier!=deep&Tier!=integration&Tier!=live&Tier!=models"`; it is not set in any csproj, props or workflow).
  `docs/TESTING.md` lists what skips on Linux, each via `Assert.SkipUnless(OperatingSystem.IsWindows(), "needs Windows: <what>; run it on Windows")`:
  picture decode/draw cases (GDI+), the ORT start-up check (needs `vcruntime140.dll` and the probe exe), a model-store folder-move case, `PublishSizeTests` (need a `pwsh` publish), and the UI harness window tiers (need WebView2).
  PdfKit tests run on Linux against a native `pdfkit.so` built by `build/pdfkit-linux.sh` (git clone at tag, CMake+Ninja, gcc then clang then gcc+`-Wno-error` fallback, output `$PDFKIT_CACHE/<ver>-linux-x64/pdfkit.so`); the PdfKit csproj copies `pdfkit.dll` or `pdfkit.so` beside test assemblies when present (`Condition="Exists(...)"`, `CopyToPublishDirectory="Never"`).
  Node suites (`node --test ...`) run unchanged on Linux.
* Scripts that are Linux-safe: `build/*.mjs` (Node 24), `build/pdfkit-linux.sh` (bash, `set -euo pipefail`, tool presence checks, SHA-256 print). `fetch-models.mjs` needs `--into` on Linux (its default reads `LOCALAPPDATA` and throws if unset).
* **Not** supported/never run on Linux: the Shell, Guards.Tests (net10.0-windows), UiHarness, `publish.ps1`/`release.ps1`, Inno Setup.

### 12.2 Recommended handling in the new app (cross-platform from day one)

* TFM `net10.0` for every project except any Windows-only host (which may be `net10.0-windows`); put the host in its own project so Linux builds can exclude it, or set `EnableWindowsTargeting=true` in `Directory.Build.props` so `dotnet build` of the full `.slnx` works on Linux.
* Remove the repo-wide `SupportedOSPlatform("windows")` item; use `OperatingSystem.IsWindows()/IsLinux()` guards and `[SupportedOSPlatform]` per type.
* ORT native: the `Microsoft.ML.OnnxRuntime` package ships both `win-x64` and `linux-x64` natives; adapt `InferenceRuntime` (`onnxruntime.dll` vs `libonnxruntime.so`) and drop the MSVC copy step on Linux.
* CI matrix `windows-latest` + `ubuntu-latest` for build + unit tier + Node suites; keep the Windows-only deep tier (real window) on Windows; publish `linux-x64` tarball from an ubuntu leg.
* Model/data folder helper: one function returning `%LOCALAPPDATA%\<app>` on Windows and `$XDG_DATA_HOME|~/.local/share/<app>` on Linux, referenced by the fetch tool and the app.

---

## 13. Mirror checklist

1. Root `CLAUDE.md` as the single source of truth with 9 numbered chapters and an authoritative-files table; add nested `CLAUDE.md` per major project (engine, app, shell, frontend, tests).
2. Three charter writing rules (short + exclusive phrasing, timeless present tense, numbered generic chapter titles); every rule stated with its reason ("because ...").
3. `global.json` pinning SDK 10.0.x (`rollForward: latestPatch`) and `test.runner = Microsoft.Testing.Platform`.
4. `Directory.Build.props`: `net10.0`, `LangVersion latest`, `Nullable enable`, `ImplicitUsings disable`, `TreatWarningsAsErrors`, `AnalysisLevel latest-recommended`, `EnforceCodeStyleInBuild`, `InvariantGlobalization`, `Deterministic`, `GenerateDocumentationFile false`, `AllowedReferenceRelatedFileExtensions none`.
5. Trim-analyzer-off-for-local-Debug block in props (`EnableTrimAnalyzer=false` unless `CI=true`), plus a guard asserting the scoping.
6. Central package management (`Directory.Packages.props` with `ManagePackageVersionsCentrally` + `CentralPackageTransitivePinningEnabled`); versions nowhere else; package table in root charter.
7. `.editorconfig` with LF, UTF-8, 2-space for xml/json/yml, 4-space C#, file-scoped namespaces `:error`, braces `:error`, usings outside namespace `:error`, explicit accessibility `:error`.
8. `.gitattributes` `* text=auto eol=lf` with binary list and `-text` for recorded fixtures; `.gitignore` for `bin/ obj/ publish/ build/cache/ artifacts/ node_modules/`.
9. `.slnx` with `/src/`, `/build/`, `/tests/` folders; project names `Product.Contracts`, `.Engine`, `.Inference`, `.App`, `.Shell` (+ `.Cli`).
10. Reference graph `Contracts <- Engine <- Inference <- App <- {Shell, Cli}`; Contracts depends on nothing; Engine names no window and no user-chosen path (bytes in, bytes out); a guard enforces it.
11. `RootNamespace` = project name; namespaces follow folders; file-scoped namespaces; tests mirror namespaces.
12. Every `.cs`/`.js`/csproj/workflow/script opens with a purpose header comment; guard test `PurposeHeaderGuardTests` ported.
13. `///` docs with `<remarks>` rationale; comments describe the code as it is now (`CommentGuardTests`); error messages state what failed, what was expected, how to fix.
14. `sealed` records with `init` props for wire shapes; `internal sealed` by default; `InternalsVisibleTo` for test projects instead of public.
15. One source-generated `BridgeJsonContext` + `BridgeJson` wrapper (relaxed encoder, no indent); optional property = nullable + omit-when-null; declaration order = wire order.
16. Bridge envelope identical: `{id,method,args}` / `{id,result|error}` / `{event,payload}` / `{drop}`; `BridgeMethods` + `BridgeEvents` const names; `host:error` event with code only.
17. `Dispatcher` = switch over `BridgeMethods`, `Arg<T>`/`Text`/`Result<T>` helpers, programming-fault replacement message; `Bridge` router in the App project; `IHostServices` seam with `Emit`.
18. App session: `sealed partial class App` split per area; one `Lock`; cancellation slots cancelled outside the lock; exactly one terminal event per run in a `finally`; non-decreasing progress.
19. Shell: raw Win32 window + `UiThread : SynchronizationContext` + hidden message-only window; WebView2 env with `%LOCALAPPDATA%\<app>\WebView2`; settings as in 4.6; bridge script injected before first navigation; exit codes 0/2/3/4; guard against blocking waits.
20. Frontend embedded as resources with forward-slash logical names, served from a virtual HTTPS origin via `WebResourceRequested` (no folder mapping), CSP header, `no-store`, 404 for everything else, navigation limited to origin.
21. Frontend is vanilla ES modules, no build step, `package.json` only with `"type":"module"`; `api.js` sole bridge caller (async wrappers); `state.js` sole state holder; `copy.js` all strings; no em dash; tokens/brand/style CSS order.
22. `frontend-hashes.txt` guard (`<sha256> *<path>`) and `node --test "frontend/**/*.test.js"` with `PageTestsPresentTests`.
23. xunit.v3 4.0.1 on Microsoft.Testing.Platform: test projects `OutputType=Exe`, `UseMicrosoftTestingPlatformRunner=true`, `IsPackable=false`; `Tier` and `Category` traits; unit tier = filter excluding the other four tiers by name.
24. Tests skip by name when a dependency or OS is missing (`Assert.SkipUnless`); guards fail when they read nothing; exemptions are named file by file with reasons.
25. Fakes for window (`IHostServices`), clock (`TimeProvider`), model boundary (`ITensorRunner`/tokenizer), native lib (recorded answers); real model only in the `models` tier.
26. Inference project: ORT `SessionOptions` (IntraOp = logical CPUs, InterOp 1, `ORT_SEQUENTIAL`, `ORT_ENABLE_ALL`, ERROR log level), session per model, `Terminate` on cancel, native-load pre-check with actionable sentence.
27. Model store in a per-user folder, `manifest.json` per model, packs verified by SHA-256 before install, atomic swap; app downloads nothing; one class opens a network connection (if any) and a guard scans for it.
28. `build/models.lock` entries `{path,url,sha256,bytes,role}` with commit-SHA-pinned URLs; roles `model|tokenizer|config|reference|source`; sibling locks for other pinned binaries.
29. `build/fetch-models.mjs`-style tool: Node 24 ESM, zero deps, `--offline/--model/--into/--lock`, streamed SHA-256, `.part` file then rename, mismatch deletes, idempotent, proxy re-exec with `--use-system-ca`; own `*.test.mjs` against a loopback server.
30. CI workflow `ci`: triggers push/PR-to-main/dispatch, `concurrency` cancel-in-progress, read-only permissions, telemetry-optout env, timeouts on every job, actions pinned by SHA with version comment, `setup-dotnet` with `global-json-file`, `dotnet build --no-restore -warnaserror`, unit tier, Node suites, integration tier on PR with `--ignore-exit-code 8`.
31. Cache downloads keyed on `hashFiles('<lock>')`; composite actions for repeated step groups (`fetch-models`); path-scoped blocking UI job computed inside the job.
32. Release workflow: tag `v*` trigger, matrix per RID with `fail-fast: false`, version read from tag with regex validation and `env:`-passed inputs, `publish.ps1`-equivalent per platform, `upload-artifact` then a write-permission `publish` job (`sha256sum -b *`, `softprops/action-gh-release` with `generate_release_notes: true`), dry-run on dispatch.
33. `build/release.ps1` flow (main only, clean, equals remote, new tag, not below highest, annotated tag, push); never build releases locally; never reuse or move a tag.
34. Publish script contract: framework-dependent app folder, trimmed self-contained portable (`PublishTrimmed=true`, `TrimMode=full`, `DebugType=none`), remove-by-name list with reasons, then refuse unexpected files; size budgets in `size-budgets.json` with 10 percent tolerance and `-RecordBudgets`; `sizes.json` record.
35. Version supplied via `-p:Version=` from tag; keep default copies (script, installer, UI footer) equal with a guard.
36. `THIRD-PARTY-NOTICES.md` shipped in every artefact and guarded against the lock's source list.
37. For Linux: cross-platform TFM, no repo-wide windows platform attribute, `EnableWindowsTargeting` or separate host project, Linux CI leg, `linux-x64` artefact, per-OS data-folder helper, `libonnxruntime.so` load check.
38. Docs: `docs/TESTING.md` (tiers, categories, scoping, commands, guards list), `docs/UITESTING.md`, `docs/change-template.md` for design-before-code change orders (`change-NN.md` parent, `change-NNa.md` batches).
