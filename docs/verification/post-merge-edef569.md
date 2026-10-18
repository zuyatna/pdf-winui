# Post-merge application verification

Verified on 2026-10-18, Windows 11 x64, 96 DPI, against merge commit `edef569fe04a246a293ecb3afe4ee5b3c949a9ca`.

## Result

No application crash or functional failure was found in the scenarios exercised. All four fresh application instances closed gracefully with exit code 0, without forced termination. Settings and PDF history present before this verification were preserved byte-for-byte. This is scoped verification, not a guarantee for every document or environment.

The merged source tree is identical to first parent `2d070f7`: both have tree ID `5e62ad89c2bba4c8a9e5ab29fbe7061bb2549d9a`. No conflict markers were found in application, Core, test or tool sources. Core, native ownership, package references, render scheduling, the 128 MiB page cache and two-page buffer are unchanged from the earlier reviewed implementation.

## Executed checks

| Check | Result |
| --- | --- |
| `dotnet restore .\PdfReader.slnx` | Passed |
| `dotnet build .\PdfReader.slnx -c Debug -p:Platform=x64` | Passed; 0 warnings, 0 errors |
| `dotnet test .\PdfReader.Core.Tests\PdfReader.Core.Tests.csproj -c Debug` | 57/57 passed, 0 skipped |
| `dotnet test .\PdfReader.Settings.Tests\PdfReader.Settings.Tests.csproj -c Debug` | 11/11 passed, 0 skipped |
| Release x64 self-contained single-file publish | Passed; fresh executable used for runtime checks |
| Standalone diagnostic collector build | Passed; 0 warnings, 0 errors |
| Targeted C# whitespace, diff/conflict checks | Passed |
| Three committed PowerShell scripts parsed; both Python tools compiled | Passed |
| Independent review of merged code and scripts | Approved; no CRITICAL/HIGH findings |

The publish used the [documented single-file options](../../README.md#2-publish-a-single-file-exe), with output `artifacts/post-merge/edef569/app/`. Executable SHA-256: `DC665ABB46BF43602781559C33C01A65D55F7A4E14222D990332A98B36E624D0`.

## Desktop runtime checks

1. **Diagnostic capture enabled:** opened the 120-page local benchmark; completed 450 scroll requests, 11 page-navigation requests, zoom from 100% through 150% to 200%, and final idle. The final analyzer accepted 3,980 UI samples with zero dropped samples and no collection error. All 11 navigation and five measured zoom requests reached a matching ready primary-page bitmap. Every final-idle callback reported the current primary bitmap ready. Fast-scroll observations retain the censoring/readiness limitations described in the [performance report](reading-responsiveness-memory.md).
2. **Normal launch, capture disabled:** opened the three-page quality fixture in Original and Warm themes. Navigated through text/vector, embedded-image and scanned pages at fit-page zoom. No frame CSV was created. The Warm run also verified Reading → Select Text → Reading, Appearance open/close, and a search for `Rendering` returning `1 / 1`, followed by closing search.
3. **Diagnostic output failure:** launched the quality fixture in OLED with diagnostic output pointing to an existing regular file, making directory creation fail. The application continued through all three pages and closed normally. No frame CSV was expected or created. This exercises the diagnostic writer's handled I/O-error path.
4. **Shutdown:** all four instances exited with code 0 within the five-second graceful-close window; none required `Kill`. No reader process remained. Application event-log inspection for IDs 1000, 1001 and 1026 since the verification start found no matching `PdfReader.App` crash events.
5. **Display inspection:** reviewed fresh screenshots of the Warm embedded image and OLED scanned page. The embedded image retained its colors; the scanned image was remapped across the page. This was screenshot inspection through UI Automation, not Visual Studio F5 or an exact-pixel comparison.

Local verification copies of the committed harnesses added exit-code/forced-termination recording. Quality copies disabled the probe or deliberately supplied the failing path; the Warm copy added toolbar/search assertions. No application source was modified. An additional outer settings backup was saved before any launch, alongside each harness's own backup. The final settings SHA-256 matched the outer backup: `B616E46365AE2BA1DDE487396AB128D7B91D61252A0D254B24A7AD303FC426C7`. This preserves the state at the start of this verification; it does not recover preferences lost in the earlier profiling incident.

## Evidence and limits

- Fresh executable, collector, local verification scripts, initial context, settings backup, event-log result and final process/settings state: `artifacts/post-merge/edef569/`.
- Instrumented workload actions, process/runtime/GC measurements, screenshots, exit record and analyzed summary: `artifacts/reading-performance/runs/postmerge-edef569/`.
- Three theme runs and their screenshots/exit records: `artifacts/reading-performance/quality/postmerge-edef569-t0/`, `-t1/`, `-t2/`.
- No ARM64 runtime, Visual Studio F5 session, MSIX/file-association installation, mouse-drag text selection, long-duration leak test or arbitrary real-world PDF corpus was exercised. No claim of reduced RAM, physical display FPS or input-to-photon timing is made by this verification.

Self-audit: accuracy 4/5 (observed exits, assertions, counters and independent review; bounded runtime coverage), completeness 4/5 (build/tests and normal/enabled/error diagnostic paths covered; platform/document limits remain), clarity 4/5 (outcome and scope explicit; supporting artifacts are local), actionability 4/5 (fresh binary and reproducible scripts available; diagnostic assemblies are installation-specific), conciseness 4/5 (evidence summarized with raw data retained separately). Overall 4.0/5. The main remaining improvements are ARM64/real-document coverage and isolated test settings. The user can reasonably rely on the exercised scenarios, but not infer universal absence of defects.
