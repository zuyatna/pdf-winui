# OLED page reading theme

The Layer 2 selector now offers Original, Dark (Warm Palette), and OLED (Pure Black). OLED defaults to black paper/canvas (`#000000`) with warm text (`#FAF9F5`). The three-color editor, swatches and reset command target the selected dark palette. Warm and OLED palettes persist independently; image dimming remains shared. Layer 1 stays independent.

OLED uses the same palette-based rendering path, luminance remap, image preservation, scanned-page handling and cache invalidation as Dark. No dependency, native interop or rendering algorithm changed. The Advanced Dark reading toggle treats either palette as dark and restores the last dark choice when turned on again in the same session. A restart with Original selected defaults that toggle back to Warm.

## Automated verification

| Check | Result |
| --- | --- |
| `dotnet restore .\PdfReader.slnx` | Passed |
| `dotnet build .\PdfReader.slnx -c Debug -p:Platform=x64` | Passed; 0 warnings, 0 errors |
| `dotnet test .\PdfReader.Core.Tests\PdfReader.Core.Tests.csproj -c Debug` | 57/57 passed, none skipped |
| `dotnet test .\PdfReader.Settings.Tests\PdfReader.Settings.Tests.csproj -c Debug` | 11/11 passed, none skipped |
| Targeted `dotnet format whitespace --verify-no-changes` | Passed for all six changed/new C# files |
| `git diff --check` | Passed |
| Independent C# review | Approved; no CRITICAL/HIGH/MEDIUM findings |

TDD RED: the new settings suite failed compilation on the missing OLED enum member/settings fields. GREEN: defaults, separate custom palettes, invalid-color fallback, save/reload, Layer 1 persistence and old settings compatibility pass. A Core test verifies white maps to pure black, ink maps to warm text, and gray interpolates between them.

The Core project remains free of UI references. Existing `PdfiumWorker` serialization and deterministic native handle ownership were not changed. The enum preserves Original=0 and Dark=1; OLED is appended as 2, keeping existing numeric JSON settings compatible.

## Desktop verification limitation

The self-contained unpackaged build passed with 0 warnings and 0 errors:

```powershell
dotnet build .\PdfReader.App\PdfReader.App.csproj -c Debug -p:Platform=x64 -p:WindowsPackageType=None -p:WindowsAppSDKSelfContained=true -p:OutputPath=bin/oled-smoke/
```

The local UI Automation harness (`artifacts/desktop-smoke/run-oled-theme.ps1`, ignored by Git) stopped at its existing-reader guard. The user's downloaded reader was open with a document, so no automated UI actions or settings changes were performed. No screenshots, interactive visual results, Visual Studio F5 results or ARM64 execution are claimed.

Pending interactive checks: choose OLED under Appearance; confirm black paper/canvas and warm text; edit/apply colors; switch to Warm and back; reset OLED; toggle Dark reading off/on; restart with OLED selected and confirm persistence. Check images and scanned pages using representative PDFs.

## Self-evaluation

Accuracy 4/5: remap and settings behavior have executable coverage, but WinUI binding transitions have not been exercised live. Completeness 4/5: implementation and mandatory CLI/review gates pass; interactive verification is pending because a reader session is active. Clarity 5/5: OLED defaults and palette independence are documented in README. Actionability 4/5: source and desktop test build are available; visual confirmation still needs an idle reader session. Conciseness 4/5: separate settings fields and editor bindings add code but retain the existing settings format and avoid a migration. Overall 4.2/5. The highest-impact follow-up is the pending desktop check; these limitations should match the user's assessment.
