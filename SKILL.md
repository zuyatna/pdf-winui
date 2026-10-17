---
name: pdf-reader-ecc
description: Project-specific ECC quality gate, C#/.NET 10 conventions, PDFium worker safety, and Win2D Dark Reading Mode workflow for the native WinUI 3 PDF Reader.
---

# PDF Reader — ECC Workflow & Quality Gate Skill

Use this skill whenever planning, implementing, testing, or reviewing code in `C:\Users\90156638\Documents\Project\PdfReader`. It binds the installed Everything Claude Code (ECC) `core` profile in `.agents/` to the project's technical specification in [`AGENTS.md`](./AGENTS.md) and progress tracker in [`PROGRESS.md`](./PROGRESS.md).

---

## 1. Installed ECC Surfaces Mapping

This repository has ECC `v2.2.3` (`core` profile) installed in `.agents/`. Use these canonical paths for each workflow step:

| Workflow Stage | Canonical ECC Surface | Purpose |
| --- | --- | --- |
| **Feature & Phase Planning** | `.agents/rules/common-development-workflow.md` | Define scope, file changes, and test cases before writing code. |
| **C# Coding Style** | `.agents/rules/csharp-coding-style.md` | File-scoped namespaces, nullable reference types, `record` models, async/await with `CancellationToken`. |
| **C# Architecture & Patterns** | `.agents/rules/csharp-patterns.md` | Focused constructors, interface boundaries between `PdfReader.Core` and `PdfReader.App`. |
| **TDD & Unit Testing** | `.agents/skills/tdd-workflow/SKILL.md`, `.agents/rules/csharp-testing.md` | Arrange-Act-Assert unit and integration tests in `PdfReader.Core.Tests`. |
| **Security & Native Interop** | `.agents/rules/csharp-security.md`, `.agents/rules/common-security.md` | Safe file handling, native pointer/handle lifetime, bounds checking. |
| **Verification Loop** | `.agents/skills/verification-loop/SKILL.md` | Build (`x64`), warnings-as-errors check, MSTest execution, and diff inspection. |
| **Code Review Gate** | Subagent `csharp-reviewer` (`.agents/agents/csharp-reviewer.md`) | Review all C# and XAML changes for concurrency, memory safety, and MVVM compliance. |
| **Delivery & Self-Audit** | `.agents/skills/delivery-gate/SKILL.md`, `.agents/skills/agent-self-evaluation/SKILL.md` | Block completion until build, tests, and [`PROGRESS.md`](./PROGRESS.md) updates are complete. |
| **ECC Component Lookup** | `.agents/skills/ecc-guide/SKILL.md` | Inspect live `.agents/` files when searching for additional workflows or agents. |

---

## 2. Project-Specific Overrides Over Generic ECC Rules

Always apply these project rules when they differ from generic ECC defaults:

1. **Test Framework (`MSTest`, not `xUnit`)**:
   - `PdfReader.Core.Tests` uses `MSTest.Sdk/4.5.1` on `net10.0` (`UseVSTest=true`).
   - Do **not** add `xUnit`, `NUnit`, `AwesomeAssertions`, `FluentAssertions`, `Moq`, or `NSubstitute` unless explicitly approved by the user.
   - Keep a single assembly-level `[assembly: DoNotParallelize]` in `PdfReader.Core.Tests/MSTestSettings.cs` because native PDFium tests share a process-wide state.
2. **WinUI 3 + MVVM Generator Syntax (.NET 10 / C# 14)**:
   - Use `CommunityToolkit.Mvvm` (`8.4.2`) `[ObservableProperty]` on **`public partial` properties**, never on private backing fields (prevents WinRT/AOT warning `MVVMTK0045` which fails the build under `TreatWarningsAsErrors=True`).
3. **Strict Layer Separation (`PdfReader.Core` vs `PdfReader.App`)**:
   - `PdfReader.Core` targets `net10.0` only and must never reference WinUI, Win2D, or Windows App SDK assemblies.
   - `PdfReader.App` targets `net10.0-windows10.0.19041.0` and hosts the Win2D rendering pipeline (`PdfReader.App/Rendering`), ViewModels, and Views.
4. **PDFium Threading & Resource Safety**:
   - `PDFiumCore` (`156.0.8076`) is not thread-safe.
   - Route **every** PDFium call (including `fpdfview.FPDF_InitLibrary()`, document load/close, page load/close, bitmap creation/rendering, and `fpdfview.FPDF_DestroyLibrary()`) through the single process-wide dedicated `PdfiumWorker` thread.
   - Wrap native pointers/handles in `IDisposable` or `SafeHandle` (`NativeResource`) and guarantee cleanup on success, cancellation, and exception paths.
5. **Dark Reading Mode Integrity**:
   - Never simplify Dark Reading Mode to a naive RGB invert.
   - Keep Layer 1 (App UI Theme: `System`, `Light`, `Dark` via `ThemeResource` and `RequestedTheme`) separate from Layer 2 (Page Reading Theme: `Original`, `Dark` with customizable Page Background `#262624`, Text `#FAF9F5`, Canvas `#1F1E1D`).
   - Implement page recoloring via GPU-accelerated Win2D `ColorMatrixEffect` luminance remap (`L = 0.299*R + 0.587*G + 0.114*B`), preserve original image bounding boxes (with optional dimming), and remap full pages when image coverage is `>= 85%` (scanned pages).

---

## 3. Step-by-Step Execution & Quality Gate Checklist

Run this loop for every milestone in [`PROGRESS.md`](./PROGRESS.md):

1. **Plan & Write/Update Tests First (TDD)**:
   - For `PdfReader.Core` features, write or update Arrange-Act-Assert tests in `PdfReader.Core.Tests` covering normal behavior, boundary inputs, cancellation, and disposal.
2. **Implement Minimal, Clean Code**:
   - Follow file-scoped namespaces, one public type per file, `<Nullable>enable</Nullable>`, and `<TreatWarningsAsErrors>True</TreatWarningsAsErrors>`.
3. **Run the Verification Commands**:
   ```powershell
   Set-Location -LiteralPath 'C:\Users\90156638\Documents\Project\PdfReader'
   dotnet build .\PdfReader.slnx -c Debug -p:Platform=x64
   dotnet test .\PdfReader.Core.Tests\PdfReader.Core.Tests.csproj -c Debug
   ```
4. **Run Code Review (`csharp-reviewer`)**:
   - Verify thread safety, `CancellationToken` propagation, `IDisposable`/`IAsyncDisposable` correctness, and XAML/MVVM binding hygiene.
5. **Update `PROGRESS.md`**:
   - Mark completed items `[x]` only after build and tests pass, and record the verified test count and status.
