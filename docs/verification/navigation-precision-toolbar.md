# Navigation precision and minimal toolbar

Verified on 2026-10-17. This milestone fixes the reported outline navigation defect and implements the requested toolbar redesign.

## Result

Outline navigation now preserves the destination's position within its page. Previously only the page number reached the view. The Core service reads XYZ, horizontal/vertical fit and rectangle destinations on `PdfiumWorker`, then converts positions with PDFium's page-to-device transform. This matches bitmap rotation and crop-box handling while preserving the reader's current zoom. When a destination has no coordinates, navigation falls back to the page top.

The view first realizes the target page, then aligns against the actual bitmap area. A bounded 16 ms timer keeps the target pending until the measured offset, extent and viewport remain stable for three frames, with a 60-tick maximum. New navigation replaces the target; document changes, closing the window and direct scrolling/zooming cancel it. The 24-DIP reading margin keeps a heading below the top edge when the scroll range permits.

Live testing exposed two additional virtualization effects: a late extent/anchor update could undo a one-shot alignment, and cumulative-height estimates could report page 7 while page 6 was visible. Alignment now survives layout changes, and the render coordinator/page indicator consume `ItemsStackPanel`'s real visible indices and measured container positions. A fallback remains for initial layout.

The toolbar contains the hamburger, Open PDF, page navigation, zoom and Advanced buttons. The hamburger exposes Contents and Bookmarks in a sidebar that starts closed. Advanced contains Text selection, Copy selected text, Find in document, Dark reading and Appearance. Fit width, Fit page and Actual size are under the zoom percentage. Appearance has a direct close button. Ctrl+B/D/F/C remain root-level accelerators; text-input copy retains priority.

## Automated evidence

| Check | Result |
| --- | --- |
| `dotnet restore .\PdfReader.slnx` | PASS |
| `dotnet build .\PdfReader.slnx -c Debug -p:Platform=x64` | PASS, 0 warnings / 0 errors |
| `dotnet build .\PdfReader.slnx -c Debug -p:Platform=ARM64` | PASS, 0 warnings / 0 errors |
| `dotnet test .\PdfReader.Core.Tests\PdfReader.Core.Tests.csproj -c Debug` | PASS, 56/56 |
| `dotnet test .\PdfReader.Settings.Tests\PdfReader.Settings.Tests.csproj -c Debug` | PASS, 7/7 |
| Targeted `dotnet format --verify-no-changes --no-restore --include ...` | PASS |
| `git diff --no-ext-diff --check` | PASS |
| Read-only C# review following `.agents/agents/csharp-reviewer.md` | PASS, no remaining CRITICAL/HIGH issues |

New tests in `PdfOutlineAndBookmarksTests` cover XYZ, FitH/FitBH, FitV/FitBV, FitR, explicit null XYZ positions and rotated crop boxes. `PdfPageDestinationTests` cover measured position conversion at multiple zoom heights, horizontal destinations, page-only fallback and scroll-range clamping. Existing worker, native lifecycle, rendering, text, theme, cache and persistence tests remain green. Coverage was not recollected for this milestone.

TDD evidence: the new tests first failed to compile because outline coordinates and `PdfPageDestination` did not exist; the targeted suite then passed 24 tests after implementation. Desktop testing subsequently reproduced an actual target of 7203 versus requested 6187 in the one-shot prototype. Stable-frame alignment corrected the race. A later uninstrumented desktop run exposed the incorrect page indicator, and actual visible-range tracking corrected that regression. No checkpoint commits were created in the existing dirty workspace.

## Desktop verification

The ordinary packaged executable could not be launched directly in this shell: Windows App Runtime deployment activation failed with `REGDB_E_CLASSNOTREG`. No runtime was installed and no package versions were changed. A separate local test build ran successfully using:

```powershell
dotnet build .\PdfReader.App\PdfReader.App.csproj -c Debug -p:Platform=x64 -p:WindowsPackageType=None -p:WindowsAppSDKSelfContained=true -p:OutputPath=bin/desktop-smoke/
```

Built-in Windows UI Automation drove a seven-page fixture with varying page heights, a chapter and subheading on page 2, and another chapter on page 6. Five repeated jumps followed 6 → 2 → 2 → 6 → 2. An instrumented test measured final offsets against the actual page geometry within 0.001 DIP; those temporary diagnostics were removed. The final production-code desktop run also checked the correct page indicator for every jump, all Advanced menu entries, opening/closing Appearance, Find focusing the search box, and toolbar layout at 900 × 700 pixels.

The UIA harness reacquires recycled elements after transient provider closure and waits for state/layout stabilization. Screenshots were inspected for the heading position, narrow toolbar and Appearance pane. Test recents were removed from the app's settings afterward while retaining other entries and appearance values. No third-party automation packages were installed. These checks follow the local `windows-desktop-e2e` skill using native UIA rather than pywinauto.

Local artifacts (ignored by Git) are under `artifacts/desktop-smoke`: `navigation-results.json`, `subheading-final.png`, `minimal-toolbar-narrow.png`, `appearance-final.png`, and the two PowerShell run scripts. The earlier script records instrumented alignment evidence; the final script exercises the production code without diagnostic writes.

No Visual Studio F5 session was performed. ARM64 was compiled, not executed. A PDF whose outline supplies only a page reference cannot specify an exact heading position; such entries use page-top navigation.

## Review and self-evaluation

Review confirmed worker confinement, deterministic page disposal during coordinate conversion, document identity checks, bounded timer lifetime, actual visible-range bounds, menu bindings and shutdown. The same-page active marker now retains the clicked heading instead of always selecting the final heading on that page.

Self-evaluation: accuracy 4/5 (native fixtures and live UI checked; the user's original PDF was not available), completeness 4/5 (requested behavior delivered; coordinate-less destinations remain page-only), clarity 4/5 (usage updated in README; detailed evidence kept here), actionability 5/5 (builds and runnable UI verified), conciseness 4/5 (short user summary with separate technical evidence). Overall: 4.2/5. The next useful check is reading the user's original PDF in their normal Visual Studio deployment.

Official API references: [PDFium destination APIs](https://pdfium.googlesource.com/pdfium/+/refs/heads/main/public/fpdf_doc.h), [ItemsStackPanel visible indices](https://learn.microsoft.com/en-us/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.controls.itemsstackpanel), and [self-contained Windows App SDK deployment](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/self-contained-deploy/deploy-self-contained-apps).
