# PdfReader

A fast, lightweight, native Windows 11 PDF reader built with C# 14, .NET 10, WinUI 3 (Windows App SDK), PDFium, and Win2D.

Its defining feature is a **comfortable Dark Reading Mode**: instead of harshly inverting page colors, PDF pages are recolored on the GPU using a luminance remap to a warm dark palette (modeled on Claude's dark theme) while preserving full-color embedded images and automatically adapting to scanned documents.

---

## Key Features

- **Comfortable Dark Reading Mode**
  - **Two Independent Theme Layers**: Switch the App UI Theme (`System`, `Light`, `Dark`) independently from the Page Reading Theme (`Original`, `Dark`).
  - **GPU Luminance Remap**: Maps page luminance smoothly between customizable **Page Background** (`#262624`), **Text** (`#FAF9F5`), and **Canvas** (`#1F1E1D`) colors via a Win2D `ColorMatrixEffect`.
  - **Image Preservation & Dimming**: Embedded images retain their original colors with an adjustable dimming slider (`0%`–`50%`) to prevent glare against dark pages.
  - **Scanned Document Detection**: Pages where images cover `>= 85%` of the page area are automatically detected as scanned pages and receive the full-page luminance remap.
- **High-Performance Virtualized Rendering**
  - Continuous vertical scrolling with viewport virtualization (only visible pages plus a buffer are rendered; off-screen render requests are automatically cancelled).
  - Dedicated single-threaded `PdfiumWorker` with a bounded work queue for thread-safe native PDFium execution and deterministic `SafeHandle`/`IDisposable` cleanup.
  - Display-DPI aware rendering (`XamlRoot.RasterizationScale * ZoomFactor`) backed by a byte-budgeted LRU bitmap cache (`256 MB` cap).
- **Reading & Navigation**
  - **Table of Contents**: Open the hamburger button to show the PDF's embedded outline or personal bookmarks. Clicking a heading follows its position within the page, including rotated/cropped pages. Destinations without a position fall back to the page top. PDFs without an outline show an empty state.
  - **Personal Bookmarks**: Press `Ctrl + D` to toggle a bookmark for the current page. In the **Bookmarks** tab, enter a title and use **Save / update bookmark** to add or rename it; click a bookmark to navigate or use its delete button to remove it. Bookmarks are saved locally per file, survive clearing recent files, and do not modify the PDF.
  - Toggle the **Contents / Bookmarks** sidebar with `Ctrl + B`.
  - Zoom modes: **Fit Width**, **Fit Page**, and **Custom Zoom** (`25%`–`500%`) via toolbar buttons or `Ctrl + Mouse Wheel` with debounced crisp re-rendering.
  - **Go to Page** using the toolbar page box and live page indicator.
  - Per-file view state persistence (automatically restores last visited page and zoom mode/level) and a **Recent Files** list on the home screen.
- **Interaction Modes & Text Tools**
  - **Minimal Toolbar**: Hamburger navigation, Open PDF, page controls, zoom controls and an **Advanced** ellipsis menu. Click the zoom percentage for Fit width, Fit page or Actual size.
  - **Reading Mode**: Pan and scroll pages naturally by clicking and dragging (or double-tap dragging on a trackpad).
  - **Text Selection Mode**: Enable **Advanced > Text selection**, then drag across text and copy it using **Advanced > Copy selected text** or `Ctrl + C`.
  - **Advanced Menu**: Find, Dark reading and Appearance are grouped with the text tools; the keyboard shortcuts remain available while the menu is closed.
  - **Full-Document Search**: Find bar (`Ctrl + F`) with highlighted page matches and next/previous navigation (`Enter` / `F3` / `Shift + F3`).
- **Windows 11 Shell Integration**
  - `.pdf` file type association, command-line file opening (`PdfReader.App.exe "path\to\document.pdf"`), and drag-and-drop onto the app window.

---

## Technology Stack

| Area | Technology / Package | Version |
| --- | --- | --- |
| **Language & Runtime** | C# 14, .NET 10 (`net10.0` / `net10.0-windows10.0.19041.0`) | .NET 10 SDK |
| **UI Framework** | WinUI 3 (`Microsoft.WindowsAppSDK`) | `2.5.1` |
| **Windows SDK BuildTools** | `Microsoft.Windows.SDK.BuildTools` | `10.0.28000.2705` |
| **PDF Engine** | PDFium (`PDFiumCore`) | `156.0.8076` |
| **Graphics & GPU Effects** | Win2D (`Microsoft.Graphics.Win2D`) | `1.4.0` |
| **MVVM Framework** | `CommunityToolkit.Mvvm` | `8.4.2` |
| **Testing Framework** | MSTest (`MSTest.Sdk`) | `4.5.1` |
| **Target Platforms** | Windows 11 (`win-x64`, `win-arm64`) | `10.0.19041.0+` |

---

## Solution Architecture

The repository is organized into four projects in `PdfReader.slnx`:

```text
PdfReader/
├── PdfReader.Core/         # UI-agnostic core library (net10.0)
│   ├── Caching/            # Byte-budgeted thread-safe LruCache<TKey, TValue>
│   ├── Document/           # PdfDocumentService, page metadata, text selection & search
│   ├── Interop/            # Single-threaded PdfiumWorker & NativeResource<THandle>
│   ├── Primitives/         # PixelSize, NormalizedRect, LaunchArgumentParser
│   ├── Theming/            # ReadingThemePalette, LuminanceRemapCalculator, ScannedPageDetector
│   └── View/               # ZoomMode, ZoomCalculator, DocumentViewState
├── PdfReader.App/          # WinUI 3 desktop application (net10.0-windows10.0.19041.0)
│   ├── Controls/           # PdfPageView (bitmap display, text/search overlays, pan/select input)
│   ├── Rendering/          # PageRenderPipeline, PageThemeProcessor (Win2D), ViewportRenderCoordinator
│   ├── Services/           # ReaderSettingsService (JSON source-generated persistence)
│   ├── Strings/en-US/      # Localized UI resources (Resources.resw)
│   └── ViewModels/         # MainViewModel, PdfPageViewModel, RecentFileItem
├── PdfReader.Core.Tests/   # MSTest unit & native PDFium integration test suite (net10.0)
└── PdfReader.Settings.Tests/ # Portable tests of the App settings sources and JSON persistence (net10.0)
```

---

## Building and Running Locally

### Prerequisites

1. **Operating System**: Windows 11 (x64 or ARM64).
2. **Windows Developer Mode**: Enable Developer Mode in **Settings** → **System** → **For developers** → **Developer Mode** (required to deploy and run packaged WinUI 3 applications locally).
3. **.NET SDK**: [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0).
4. **IDE**: **Visual Studio 2026** (Community, Professional, or Enterprise) with the **WinUI application development** workload installed.

### 1. Build and Test via Command Line (PowerShell)

Open PowerShell in the repository root directory and run:

```powershell
# Restore NuGet packages
dotnet restore .\PdfReader.slnx

# Build the solution in Debug mode for x64 (WinUI requires an explicit platform)
dotnet build .\PdfReader.slnx -c Debug -p:Platform=x64

# Run the Core unit and native PDFium integration test suite
dotnet test .\PdfReader.Core.Tests\PdfReader.Core.Tests.csproj -c Debug

# Run settings and bookmark persistence integration tests
dotnet test .\PdfReader.Settings.Tests\PdfReader.Settings.Tests.csproj -c Debug
```

To build optimized `Release` binaries for `x64` or `ARM64`:

```powershell
dotnet build .\PdfReader.slnx -c Release -p:Platform=x64
dotnet build .\PdfReader.slnx -c Release -p:Platform=ARM64
```

### 2. Publish a Single-File EXE

Run this command from the repository root to publish a single self-contained Windows x64 executable:

```powershell
dotnet publish .\PdfReader.App\PdfReader.App.csproj `
  -c Release `
  -r win-x64 `
  --self-contained true `
  -p:Platform=x64 `
  -p:WindowsPackageType=None `
  -p:WindowsAppSDKSelfContained=true `
  -p:PublishSingleFile=true `
  -p:IncludeAllContentForSelfExtract=true `
  -p:DebugType=None `
  -p:DebugSymbols=false `
  -o .\artifacts\single-file\win-x64
```

The distributable file is `artifacts\single-file\win-x64\PdfReader.App.exe`. Launch it directly:

```powershell
.\artifacts\single-file\win-x64\PdfReader.App.exe
```

- `PublishSingleFile=true` overrides the existing publish profile's folder-based output.
- `--self-contained true` and `WindowsAppSDKSelfContained=true` bundle both .NET and the Windows App SDK runtime.
- `IncludeAllContentForSelfExtract=true` bundles native libraries and content, including PDFium and app resources. At launch, these dependencies are automatically extracted to a temporary directory; you only distribute the EXE.
- `DebugType=None` and `DebugSymbols=false` omit separate debug-symbol files.
- For ARM64, replace `-r win-x64` with `-r win-arm64`, `Platform=x64` with `Platform=ARM64`, and the output folder with `artifacts\single-file\win-arm64`.

This publish mode is unpackaged. PDF file associations declared in `Package.appxmanifest` are applied through MSIX deployment; the standalone EXE can open PDFs through its picker, drag-and-drop, or command-line arguments.

See Microsoft's [single-file WinUI deployment documentation](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/unpackage-winui-app#single-file-exe) for the required settings and extraction behavior.

### 3. Run and Debug in Visual Studio 2026

1. Open `PdfReader.slnx` in **Visual Studio 2026**.
2. Set the solution configuration to **Debug** (or **Release**) and the solution platform to **x64** (or **ARM64**).
3. Ensure **`PdfReader.App (Package)`** is selected as the Startup Project.
4. Press **`F5`** to build, deploy, and launch with debugging attached (or **`Ctrl + F5`** to start without debugging).
