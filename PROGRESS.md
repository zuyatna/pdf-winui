# PDF Reader — Development Progress

Tracks implementation milestones, automated test coverage, and ECC quality gate status for `PdfReader.slnx`.

- **Current Active Phase**: Phase 3 — Reading basics (`done`)
- **Last Verified Git Commit**: `1dcf31e` (`feat: win ui rendering pipeline and virtualized reader`)
- **Quality Gate Policy**: [`AGENTS.md`](./AGENTS.md) & [`SKILL.md`](./SKILL.md) (`0` warnings, `0` errors, all MSTest suites green)

---

## Phase Overview

| Phase | Name | Status | Summary |
| --- | --- | --- | --- |
| **Phase 0** | Setup | `done` | Solution structure (`PdfReader.slnx`), `PdfReader.App` (WinUI 3) + `PdfReader.Core` (.NET 10), `MicaBackdrop`, Git baseline. |
| **Phase 1** | Skeleton | `done` | `PDFiumCore` worker & service, LRU bitmap cache, file picker, continuous vertical scroll, virtualized rendering. |
| **Phase 2** | Dark reading mode | `done` | UI theme + Page reading theme (`#262624` / `#FAF9F5` / `#1F1E1D`), Win2D `ColorMatrixEffect` luminance remap, image preservation, scanned-page detection (`>= 85%`), settings UI. |
| **Phase 3** | Reading basics | `done` | Zoom (fit width, fit page, `Ctrl`+wheel), go-to-page, page indicator, per-file state persistence, recent files. |
| **Phase 4** | Text | `todo` | Text selection, copy, search with highlighted results. |
| **Phase 5** | Shell integration | `todo` | `.pdf` file association, CLI/drag-and-drop open, MSIX packaging. |

---

## Project & Dependency Matrix

| Project | Target Framework | Packages & References | Compiler Guardrails |
| --- | --- | --- | --- |
| `PdfReader.Core` | `net10.0` | `PDFiumCore` `156.0.8076` | `Nullable=enable`, `TreatWarningsAsErrors=True` |
| `PdfReader.Core.Tests` | `net10.0` | `MSTest.Sdk` `4.5.1` (`UseVSTest=true`), ProjectRef -> `PdfReader.Core` | `Nullable=enable`, `TreatWarningsAsErrors=True` |
| `PdfReader.App` | `net10.0-windows10.0.19041.0` | `Microsoft.WindowsAppSDK` `2.5.1`, `Microsoft.Windows.SDK.BuildTools` `10.0.28000.2705`, `Microsoft.Graphics.Win2D` `1.4.0`, `CommunityToolkit.Mvvm` `8.4.2`, ProjectRef -> `PdfReader.Core` | `Nullable=enable`, `TreatWarningsAsErrors=True` |

---

## Phase 0 — Setup (`done`)

- [x] Create `PdfReader.slnx` with `PdfReader.App` (WinUI 3) and `PdfReader.Core` (`net10.0`).
- [x] Configure `Nullable=enable` and `TreatWarningsAsErrors=True` across all projects and platforms (`x86`, `x64`, `ARM64`).
- [x] Configure `MicaBackdrop` in `PdfReader.App/MainWindow.xaml`.
- [x] Commit baseline setup (`5f295ec`).

---

## Phase 1 — Skeleton (`done`)

### 1.1 Packages & Test Project Setup
- [x] Install `PDFiumCore` (`156.0.8076`) in `PdfReader.Core`.
- [x] Install `Microsoft.Graphics.Win2D` (`1.4.0`) and `CommunityToolkit.Mvvm` (`8.4.2`) in `PdfReader.App`.
- [x] Create `PdfReader.Core.Tests` (`MSTest.Sdk/4.5.1`, `net10.0`) referencing `PdfReader.Core`.
- [x] Move `[assembly: DoNotParallelize]` into `PdfReader.Core.Tests/MSTestSettings.cs` and remove template `Class1.cs` / `Test1.cs`.

### 1.2 Dedicated PDFium Worker (`PdfReader.Core`)
- [x] Complete `PdfReader.Core/PdfiumWorker.cs` (`RunAsync`, bounded queue backpressure, cancellation support, and clean `DisposeAsync` without `CS1998` warnings).
- [x] Add `PdfReader.Core.Tests/WorkerLearningTests.cs` and `PdfReader.Core.Tests/PdfiumWorkerTests.cs` (5 tests: return value, single-thread affinity, pre-canceled token, bounded queue backpressure, post-dispose rejection).

### 1.3 Core Domain Models & Native PDFium Service (`PdfReader.Core`)
- [x] Add Core models and contracts (`PdfDocumentMetadata`, `PdfPageSize`, `PixelSize`, `RenderedPageBuffer`, `IPdfDocumentService`).
- [x] Add `PdfReader.Core/NativeResource.cs` and `PdfReader.Core/PdfDocumentService.cs` (serializing all `PDFiumCore` calls through `PdfiumWorker`).
- [x] Add `PdfReader.Core.Tests/PixelSizeTests.cs` (3 coordinate conversion tests).
- [x] Add sample PDF fixture (`samples/text.pdf`) and `PdfReader.Core.Tests/PdfDocumentServiceTests.cs` (2 native lifecycle and rendering tests; 10 Core tests total).

### 1.4 Memory-Bounded LRU Cache (`PdfReader.Core`)
- [x] Implement `PdfReader.Core/LruCache.cs` with byte-budget eviction and deterministic item disposal.
- [x] Add `PdfReader.Core.Tests/LruCacheTests.cs` (4 cache behavior and eviction tests; 14 Core tests total).

### 1.5 WinUI Rendering Pipeline & Virtualized Reader (`PdfReader.App`)
- [x] Add user-facing string resources in `PdfReader.App/Strings/en-US/Resources.resw`.
- [x] Add `PdfReader.App/ViewModels/PdfPageItem.cs` (using `[ObservableProperty]` on `public partial` properties).
- [x] Add `PdfReader.App/Rendering/PageFrame.cs` and `PdfReader.App/Rendering/PageRenderPipeline.cs`.
- [x] Add `PdfReader.App/ViewModels/MainViewModel.cs` (file picker open command, document lifecycle, status/error state).
- [x] Add `PdfReader.App/Views/PdfPageView.xaml` and `PdfPageView.xaml.cs`.
- [x] Add `PdfReader.App/Rendering/ViewportRenderCoordinator.cs` (visible range + buffer rendering, out-of-view cancellation, rasterization scale handling).
- [x] Wire `PdfReader.App/MainWindow.xaml` and `MainWindow.xaml.cs`.

### 1.6 Phase 1 ECC Quality Gate
- [x] `dotnet build .\PdfReader.slnx -c Debug -p:Platform=x64` passes with `0` warnings and `0` errors.
- [x] `dotnet build .\PdfReader.slnx -c Debug -p:Platform=ARM64` passes with `0` warnings and `0` errors.
- [x] `dotnet test .\PdfReader.Core.Tests\PdfReader.Core.Tests.csproj -c Debug` passes all 14 Core tests.
- [x] Code review via `csharp-reviewer` passes with 0 CRITICAL/HIGH issues.
- [x] Visual verification in Visual Studio (`F5`): open PDF, scroll smoothly, cancel off-screen renders, verify memory cap.

---

## Phase 2 — Dark Reading Mode (`done`)

- [x] **Layer 1 (App UI Theme)**: `System`, `Light`, `Dark` options using `ThemeResource` and root `RequestedTheme` persistence; title bar and `MicaBackdrop` sync.
- [x] **Layer 2 (Page Reading Theme)**: Data-driven theme model (`Original`, `Dark` with default Page Background `#262624`, Text `#FAF9F5`, Canvas `#1F1E1D`) stored in settings.
- [x] **GPU Luminance Remap**: Win2D `ColorMatrixEffect` using `CreateRemap(Color background, Color text)` (`L = 0.299*R + 0.587*G + 0.114*B`) + unit tests for matrix endpoints and interpolation.
- [x] **Image Preservation & Dimming**: Enumerate PDF image object bounding boxes via PDFium, composite original unremapped image regions over remapped page, apply configurable image dim setting.
- [x] **Scanned Document Detection**: Detect pages where image objects cover `>= 85%` of page area and apply luminance remap to the full page + unit tests for threshold boundaries.
- [x] **Settings UI & Cache Invalidation**: Settings view for UI theme, reading theme, custom 3-color palette, and image dimming; invalidate LRU bitmap cache on reading theme change.

---

## Phase 3 — Reading Basics (`done`)

- [x] Zoom modes (Fit Width, Fit Page, custom zoom via `Ctrl`+Mouse Wheel) with scaled placeholder bitmap until re-render settles.
- [x] Page indicator and Go-to-Page navigation.
- [x] Per-file persistence of last viewed page and zoom level.
- [x] Recent files list.

---

## Phase 4 — Text (`todo`)

- [ ] PDFium text page integration on `PdfiumWorker`.
- [ ] Text selection and copy to clipboard.
- [ ] Find in document with highlighted match overlays and next/previous navigation.

---

## Phase 5 — Shell Integration (`todo`)

- [ ] `.pdf` file type association in `Package.appxmanifest`.
- [ ] Open PDF from command-line arguments and window drag-and-drop.
- [ ] MSIX packaging and release configuration verification (`x64` and `ARM64`).
