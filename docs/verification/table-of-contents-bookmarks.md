# Table of contents and bookmarks verification

Verified on 2026-10-17. Scope: the requested table of contents and personal bookmarks milestone. Existing workspace rendering/theme changes were preserved.

This is the initial milestone report. Subsequent destination-position fixes and the toolbar redesign are covered by [Navigation precision and minimal toolbar](./navigation-precision-toolbar.md).

## Delivered behavior

- The sidebar displays the PDF's embedded outline in document order with hierarchy indentation and page destinations. Clicking an internal destination navigates to its page. Missing outlines display an empty state.
- Personal bookmarks support adding, renaming, deleting and navigating to pages. Ctrl+D toggles the current page; Ctrl+B toggles the sidebar.
- Bookmarks are stored locally per full file path, independently of the ten-entry recent-file list. Clearing or evicting recent files preserves bookmarks. Removing the final bookmark removes the archived copy.
- Settings saves run off the UI thread, in order, through a temporary file before replacement. Bookmark commands await completion and display a resource-backed error if persistence fails; successful retries clear that error.
- Opening another document resets navigation state. Canceled opens cannot overwrite a newer open's status.

## Automated checks actually executed

| Check | Result |
| --- | --- |
| `dotnet restore .\PdfReader.slnx` | PASS |
| `dotnet build .\PdfReader.slnx -c Debug -p:Platform=x64` | PASS, 0 warnings, 0 errors |
| `dotnet build .\PdfReader.slnx -c Debug -p:Platform=ARM64` | PASS, 0 warnings, 0 errors |
| `dotnet test .\PdfReader.Core.Tests\PdfReader.Core.Tests.csproj -c Debug` | PASS, 44/44 |
| `dotnet test .\PdfReader.Settings.Tests\PdfReader.Settings.Tests.csproj -c Debug` | PASS, 7/7 |
| Coverage reruns of both test commands with `--collect:"Code Coverage;Format=cobertura"` | PASS, 51 tests total |
| `dotnet format .\PdfReader.slnx --verify-no-changes --no-restore --include ...` | PASS for the new feature C# models, outline service, navigation view model, settings classes and tests |
| `git diff --no-ext-diff --check` | PASS |

The settings test project compiles the actual portable App settings source files and source-generated JSON context, referencing Core only. It adds no dependency outside the approved stack and does not load WinUI.

## Test guarantees and RED/GREEN evidence

| Guarantee | Evidence |
| --- | --- |
| Embedded outlines are read hierarchically and flattened in document order | Native hierarchical PDF fixture; initial compilation failed because `GetOutlineAsync` was absent, then passed after implementation |
| Direct destinations and local GoTo actions resolve correctly | The hierarchical fixture includes both destination forms |
| Remote/URI actions do not navigate to local pages | GoToR test initially failed with actual page 0 instead of null; checking action type before resolving destinations made all three invalid/external destination cases pass |
| Structural headings retain children and Unicode titles | Native UTF-16 title fixture with a parent heading without a destination |
| Circular sibling references terminate without duplicate entries | Native malformed outline fixture |
| Canceled requests leave the document usable; closed/disposed services reject reads | Native cancellation and lifecycle tests |
| Bookmark updates are sorted, immutable, unique per page, and preserve creation time | Mutation tests initially failed to compile because `WithBookmark` was missing; then passed after implementation |
| Removing a bookmark affects only its page; invalid page numbers are rejected | Core bookmark mutation and validation tests |
| Old JSON remains readable and bookmark metadata round-trips | Core JSON tests and actual source-generated settings reload; the legacy settings test first failed with a null `BookmarkedDocuments`, then passed after collection normalization |
| Recent-file clearing/eviction preserves bookmarks without resurrecting deleted bookmarks | Portable settings tests |
| Same-named files in different folders have separate bookmarks; Windows path matching ignores case | Portable settings tests |
| Rapid saves preserve the latest snapshot; failed writes preserve the last valid file and can recover | Real filesystem save/order/failure/retry tests |

RED/GREEN evidence is recorded here. No checkpoint commits were created in the existing dirty workspace.

## Coverage and boundaries

Cobertura reports are under `artifacts/coverage/core` and `artifacts/coverage/settings` (ignored build artifacts). Aggregated executable source-line coverage, merging duplicate/state-machine entries:

| Source file | Covered / total | Coverage |
| --- | --- | --- |
| `PdfDocumentService.Outline.cs` | 57 / 61 | 93.4% |
| `DocumentViewState.cs` | 38 / 38 | 100% |
| `PdfOutlineItem.cs` | 23 / 25 | 92% |
| `ReaderSettingsService.cs` | 62 / 72 | 86.1% |
| `ReaderSettings.cs` | 61 / 83 | 73.5% |

The combined coverage of these files is 86.4%. The whole Core assembly has 77.4% line coverage in the Core run; this report does not claim 80% project-wide coverage. Existing settings palette/default-path branches and WinUI interactions are not fully covered by these tests.

Core retains no WinUI, Win2D or Windows App SDK references. New PDFium calls execute exclusively within the worker operation. Outline/action/destination handles are borrowed from the document and remain on that worker; the temporary title allocation is freed in `finally`. Traversal is bounded to 64 child levels, 10,000 items and 64 KiB per title.

Read-only review using `.agents/agents/csharp-reviewer.md` found no CRITICAL/HIGH issues. Two MEDIUM findings were fixed and re-reviewed: clicking the active sidebar choice could visually deselect it (resolved with grouped RadioButtons), and successful bookmark retries left the prior error visible (resolved by clearing only the tracked bookmark save error). A final reviewed refinement routes RadioButton `Checked` events to view-model commands so keyboard selection also updates the displayed panel.

API behavior was checked against the installed PDFiumCore XML documentation and the [official PDFium outline/action headers](https://pdfium.googlesource.com/pdfium/+/refs/heads/main/public/fpdf_doc.h). Grouped selection behavior was checked against [Microsoft's RadioButton documentation](https://learn.microsoft.com/en-us/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.controls.radiobutton).

## Interactive checks remaining

No interactive F5 session was performed. In Visual Studio 2026, verify sidebar sizing at narrow and wide window widths, keyboard shortcuts, repeated sidebar selection, destination navigation, bookmark title editing/removal, current-page indicators, and reopening a bookmarked PDF. ARM64 was compiled, not executed.

## Self-evaluation

| Axis | Score | Evidence / improvement |
| --- | --- | --- |
| Accuracy | 4/5 | Native behavior and actual persistence are tested; UI behavior still needs an interactive run |
| Completeness | 4/5 | Requested operations, legacy data and failure recovery are covered; visual verification remains |
| Clarity | 4/5 | Usage is documented in README; this evidence report is intentionally more detailed than the user summary |
| Actionability | 4/5 | Builds pass on both target architectures; interactive checklist is explicit |
| Conciseness | 4/5 | The final user summary can stay short; detailed evidence is kept here |

Overall: 4.0/5. Highest-value follow-up: perform the interactive checklist. This assessment should match the user's distinction between implemented/tested behavior and visual confirmation.
