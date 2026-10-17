# AGENTS.md

Instructions for AI agents working on this project. Read this file fully before doing anything.

## Your role: full-control autonomous developer and architect

You have full control to design, implement, build, test, debug, and verify this application directly in `C:/Users/90156638/Documents/Project/PdfReader/PdfReader.slnx`.

### Core operating rules

1. **Full project implementation authority**: You may create, modify, rename, move, or delete source code, XAML, `.csproj`, solution files, manifests, resource files, and tests as needed to deliver each roadmap phase cleanly.
2. **Build and test verification**: Run `dotnet restore`, `dotnet build`, and `dotnet test` to verify your changes. Do not declare work finished until the ECC quality gate passes.
3. **Track progress continuously**: Keep [`PROGRESS.md`](./PROGRESS.md) updated after every completed milestone, test addition, or phase checkpoint.
4. **Strict dependency discipline**: Do not add NuGet packages, test frameworks, assertion libraries, mocking libraries, or DI containers outside the approved tech stack without asking the user first.
5. **Phase discipline**: Implement one phase (or milestone) at a time according to [`PROGRESS.md`](./PROGRESS.md) and the technical spec below. Do not introduce speculative abstractions or unrequested later-phase features.

## Communication

- Talk to the user in Indonesian (Bahasa Indonesia).
- Write code, identifiers, comments, commit messages, and documentation in English, as requested by the user. Continue the conversation in Indonesian.
- If something is ambiguous, ask. Do not guess on anything that affects architecture or dependencies.
- Do not invent API names, package names, or version numbers. Check official sources (Microsoft Learn, NuGet, the PDFium headers) before using them.

## @ECC: quality gate and workflow

Use the installed Everything Claude Code (ECC) configuration in `.agents/` and the project skill in [`SKILL.md`](./SKILL.md) (mirrored at [`.agents/skills/pdf-reader-ecc/SKILL.md`](./.agents/skills/pdf-reader-ecc/SKILL.md)) to enforce engineering quality.

### Active ECC surfaces

- **Project ECC skill**: Read [`SKILL.md`](./SKILL.md) before starting implementation or review tasks.
- **ECC discovery**: Use [`.agents/skills/ecc-guide/SKILL.md`](./.agents/skills/ecc-guide/SKILL.md) when you need to locate another installed ECC skill, workflow, or agent.
- **TDD & verification skills**:
  - [`.agents/skills/tdd-workflow/SKILL.md`](./.agents/skills/tdd-workflow/SKILL.md) — Red-Green-Refactor cycle for testable Core and pipeline logic.
  - [`.agents/skills/verification-loop/SKILL.md`](./.agents/skills/verification-loop/SKILL.md) — Build, analyzer/warning check, test execution, and diff review before completion.
  - [`.agents/skills/delivery-gate/SKILL.md`](./.agents/skills/delivery-gate/SKILL.md) & [`.agents/skills/agent-self-evaluation/SKILL.md`](./.agents/skills/agent-self-evaluation/SKILL.md) — Quality gate enforcement and self-audit.
- **C# and common rules**:
  - [`.agents/rules/csharp-coding-style.md`](./.agents/rules/csharp-coding-style.md)
  - [`.agents/rules/csharp-patterns.md`](./.agents/rules/csharp-patterns.md)
  - [`.agents/rules/csharp-testing.md`](./.agents/rules/csharp-testing.md) *(with MSTest project override below)*
  - [`.agents/rules/csharp-security.md`](./.agents/rules/csharp-security.md)
  - [`.agents/rules/common-development-workflow.md`](./.agents/rules/common-development-workflow.md)
  - [`.agents/rules/common-code-review.md`](./.agents/rules/common-code-review.md)
- **Subagents for review and resolution**:
  - `csharp-reviewer` ([`.agents/agents/csharp-reviewer.md`](./.agents/agents/csharp-reviewer.md)) and `code-reviewer` ([`.agents/agents/code-reviewer.md`](./.agents/agents/code-reviewer.md)) for reviewing code changes.
  - `build-error-resolver` ([`.agents/agents/build-error-resolver.md`](./.agents/agents/build-error-resolver.md)) for minimal-diff build fixes.

### Mandatory ECC quality gate

Every milestone must pass all five checks before being marked `[x]` in [`PROGRESS.md`](./PROGRESS.md):

1. **Zero-warning build**: `dotnet build .\PdfReader.slnx -c Debug -p:Platform=x64` succeeds with `0 Warning(s)` and `0 Error(s)` (`TreatWarningsAsErrors=True`).
2. **Passing test suite**: `dotnet test .\PdfReader.Core.Tests\PdfReader.Core.Tests.csproj -c Debug` succeeds with all unit and native integration tests green.
3. **Architecture & boundary check**: `PdfReader.Core` has zero WinUI / Windows App SDK UI references; all `PDFiumCore` calls go through `PdfiumWorker`; all native handles are deterministically disposed on success, cancellation, and failure.
4. **Code review check**: Review changes against `csharp-reviewer` / `.agents/rules/csharp-*.md` with zero CRITICAL or HIGH issues.
5. **Honest verification reporting**: Report only commands and checks actually executed. Clearly distinguish automated CLI checks (`dotnet build`, `dotnet test`) from interactive visual checks that require running `F5` in Visual Studio 2026.

### Output quality standards

- Lead with the concrete outcome, change, or verification result. Use short sentences and precise terms.
- Remove generic introductions, filler, repeated summaries, excessive praise, and buzzwords.
- Recommend and implement the simplest approach that meets the stated requirements. Do not add speculative abstractions, dependencies, or later-phase features.

## @ECC: C# and .NET

Apply the installed ECC C# rules within this project's specific stack and requirements. Current user instructions and project requirements take precedence over general ECC recommendations.

- Keep nullable reference types enabled (`<Nullable>enable</Nullable>`), treat warnings as errors (`<TreatWarningsAsErrors>True</TreatWarningsAsErrors>`), use file-scoped namespaces, and keep one public type per file.
- Prefer immutable document and page metadata (`record` or `record struct`).
- In WinUI view models, use `[ObservableProperty]` on **`public partial` properties** and `[RelayCommand]` with the supported .NET 10 / C# 14 and `CommunityToolkit.Mvvm` (`8.4.2`) versions. Avoid field-based `[ObservableProperty]` declarations that trigger WinRT/AOT analyzer warnings (`MVVMTK0045`).
- Keep rendering and file I/O asynchronous and propagate `CancellationToken`. Use `await` instead of `.Result` or `.Wait()`; reserve `async void` strictly for UI event handlers.
- Keep `PdfReader.Core` free of UI references and route PDFium access through Core services.
- Serialize every PDFium call—including initialization and disposal—through the single process-wide dedicated `PdfiumWorker` thread with a bounded queue. Ensure native handles are disposed on success, cancellation, and failure.
- Add abstractions only for actual service boundaries or a demonstrated need. Keep each change within the active phase.
- Test Core behavior and boundary cases using Arrange-Act-Assert in `PdfReader.Core.Tests` (**MSTest** `MSTest.Sdk/4.5.1`, targeting `net10.0` and referencing `PdfReader.Core` only, with a single assembly-level `[assembly: DoNotParallelize]` in `MSTestSettings.cs`):
  - Worker execution, concurrency, cancellation, and shutdown
  - Coordinate conversion (`PixelSize`)
  - Native PDFium initialization, rendering, and handle lifetime
  - LRU cache capacity and eviction
  - Luminance remap matrix endpoints and interpolation
  - Scanned-page detection (`>= 85%` image area threshold)

## Technical spec

Everything below describes the app this project builds. Treat it as fixed unless the user changes it.

### Project

A native PDF reader for Windows 11, written in C# with WinUI (Windows App SDK). It must feel like a first-party Windows 11 app: fast, lightweight, Fluent design.

Its defining feature is a **comfortable dark reading mode**: PDF pages are recolored to a warm dark palette (modeled on Claude's dark theme), not simply inverted. This is the most important requirement. Do not simplify it.

### Tech stack

| Area | Choice | Installed Version |
| --- | --- | --- |
| Language / runtime | C# 14, .NET 10 (`net10.0` / `net10.0-windows10.0.19041.0`) | .NET 10 SDK |
| UI framework | WinUI (`Microsoft.WindowsAppSDK`) | `2.5.1` |
| Windows SDK BuildTools | `Microsoft.Windows.SDK.BuildTools` | `10.0.28000.2705` |
| PDF engine | PDFium via `PDFiumCore` | `156.0.8076` |
| Graphics / effects | Win2D (`Microsoft.Graphics.Win2D`) | `1.4.0` |
| App pattern | MVVM with `CommunityToolkit.Mvvm` | `8.4.2` |
| Test framework | MSTest (`MSTest.Sdk`) | `4.5.1` |
| Target OS | Windows 11, x64 and ARM64 | `win-x64`, `win-arm64` |
| IDE | Visual Studio 2026 Community ("WinUI application development" workload) | VS 2026 |

- Check NuGet for the current stable version of every package before recommending or updating it.
- Confirm that the Win2D and PDFium packages are compatible with the Windows App SDK version in use.
- Do not add a dependency outside this list without asking the user first.

### Do not use

- WebView2, Electron, or any browser-based PDF viewer. Rendering must be native.
- WPF, UWP, or .NET MAUI.
- `Windows.Data.Pdf`. It cannot report where images are on a page, which the dark reading mode needs.
- Commercial PDF components (Syncfusion, Telerik, DevExpress, and similar).
- AGPL-licensed libraries such as MuPDF.

### Build and test commands

```powershell
Set-Location -LiteralPath 'C:\Users\90156638\Documents\Project\PdfReader'
dotnet restore .\PdfReader.slnx
dotnet build .\PdfReader.slnx -c Debug -p:Platform=x64   # WinUI requires an explicit platform
dotnet test .\PdfReader.Core.Tests\PdfReader.Core.Tests.csproj -c Debug
```

For interactive debugging and visual verification, run with `F5` in Visual Studio 2026 (with Windows Developer Mode enabled).

### Architecture

Three layers, each in its own project or folder:

1. **Core** (`PdfReader.Core`, no UI references): PDFium interop, document and page model, rendering, text extraction, search.
2. **Rendering pipeline** (`PdfReader.App/Rendering`): turns a PDF page into a themed bitmap (PDFium render, then Win2D color remap, then image compositing).
3. **App** (`PdfReader.App`, WinUI): views, view models, settings, navigation.

Architectural invariants:

- No PDFium calls from view models or code-behind. Go through Core services.
- PDFium is not thread-safe. Every PDFium call goes through the single dedicated `PdfiumWorker` thread.
- Every native handle (document, page, bitmap, text page) is wrapped in `SafeHandle` or `IDisposable`.
- Rendering and file I/O never block the UI thread. Use async with cancellation.

### Dark reading mode (core requirement)

Two independent theme layers, kept separate in code and in settings.

**Layer 1: app UI theme**

- Options: System, Light, Dark.
- All colors come from `ThemeResource`. No hardcoded colors in XAML or code.
- The choice is applied by setting `RequestedTheme` on the root element, and is persisted.
- The title bar and window backdrop follow the theme.

**Layer 2: page reading theme**

- Options: Original (unchanged), Dark. Designed so more themes (for example Sepia) can be added later as data, not new code paths.
- A reading theme is three colors, stored in settings and never hardcoded:

| Role | Default (Dark) | Meaning |
| --- | --- | --- |
| Page background | `#262624` | Replaces white paper |
| Text | `#FAF9F5` | Replaces black ink |
| Canvas | `#1F1E1D` | Area around and between pages |

These defaults approximate Claude's dark theme. The user may adjust them.

**Recoloring is a luminance remap, not an inversion.** Each pixel is mapped by its luminance onto the line between the text color and the background color:

```
L   = 0.299*R + 0.587*G + 0.114*B        (0 = black, 1 = white)
out = text + (background - text) * L
```

White becomes the page background, black becomes the text color, and anti-aliased edges interpolate smoothly. It is implemented as a single Win2D `ColorMatrixEffect` so it runs on the GPU:

```csharp
static Matrix5x4 CreateRemap(Color background, Color text)
{
    float tr = text.R / 255f, tg = text.G / 255f, tb = text.B / 255f;
    float dr = background.R / 255f - tr;
    float dg = background.G / 255f - tg;
    float db = background.B / 255f - tb;

    return new Matrix5x4
    {
        M11 = .299f * dr, M12 = .299f * dg, M13 = .299f * db,
        M21 = .587f * dr, M22 = .587f * dg, M23 = .587f * db,
        M31 = .114f * dr, M32 = .114f * dg, M33 = .114f * db,
        M44 = 1f,
        M51 = tr, M52 = tg, M53 = tb
    };
}
```

**Images keep their original colors.**

1. Enumerate the page objects with PDFium and collect the bounding boxes of image objects.
2. Render the page normally, then apply the remap to the whole page.
3. Draw the original (unremapped) pixels back over each image bounding box.
4. Optionally dim those regions slightly so they do not glare against the dark page. The dim amount is a setting.

Bounding boxes are an approximation (rotated or masked images, text overlapping an image). That is acceptable for the first version.

**Scanned documents.** A scanned PDF is one large image per page. If image objects cover roughly 85% or more of the page area, the page is treated as scanned and the remap is applied to the entire page, including the image.

**Known limitation.** The luminance remap turns colored text, highlights, and vector diagrams into a single tone. A later phase may replace it with a custom `PixelShaderEffect` that keeps hue and only remaps lightness. Do not plan that until the user asks.

### Performance rules

- The page list is virtualized. Only visible pages plus a small buffer are rendered.
- Pages render at the display scale (`XamlRoot.RasterizationScale`) times the zoom level. After zoom settles they re-render; the scaled old bitmap is shown in the meantime.
- Render requests for pages that scrolled out of view are cancelled.
- Rendered bitmaps are cached with a memory cap (LRU). Changing the reading theme invalidates the cache.
- A 1,000-page PDF opens quickly and scrolls smoothly without loading all pages into memory.

### Roadmap phases

0. **Setup**: Visual Studio 2026 workload, Developer Mode, create the WinUI project from the template, solution structure, source control.
1. **Skeleton**: add PDFium, open a PDF with a file picker, continuous vertical scroll, virtualized rendering.
2. **Dark reading mode**: both theme layers, luminance remap, image preservation, scanned-page handling, settings page with the three colors.
3. **Reading basics**: zoom (fit width, fit page, Ctrl+wheel), go to page, page indicator, remember last page and zoom per file, recent files.
4. **Text**: text selection, copy, search with highlighted results.
5. **Shell integration**: `.pdf` file association, open from command line and drag and drop, packaging.

### Coding conventions

- Nullable reference types enabled. Warnings treated as errors.
- File-scoped namespaces, one public type per file.
- View models use `[ObservableProperty]` (on `public partial` properties) and `[RelayCommand]` from `CommunityToolkit.Mvvm`.
- No logic in code-behind beyond view-specific wiring.
- User-facing strings go in resource files (`Strings/en-US/Resources.resw`).
- Unit tests for Core logic that needs no UI (worker queue, remap matrix, scanned-page detection, cache eviction, coordinate conversion).
