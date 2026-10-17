# Size and memory optimization

## Scope

Reduce the self-contained single-file executable and rendering memory without bundle compression, dependency changes or reduced rendering quality. The existing toolbar changes are preserved.

## Changes

- Single-file publishing disables `PublishReadyToRun` and `EnableCompressionInSingleFile`. Other Release deployment modes retain ReadyToRun.
- `PageFrame` creates its `WriteableBitmap` once, reuses that image on cache hits and releases its managed BGRA array after upload. Cache eviction and image creation share a lock; UI objects are created only on the dispatcher thread.
- Page items release their image references outside the visible range plus the existing two-page buffer on each side. The 128 MiB LRU pixel budget is unchanged. The cache and page items reference the same bitmap, rather than retaining separate pixel copies.
- Render cancellation sources are disposed in dispatcher cleanup after removing the active request, preventing scroll cancellation from calling `Cancel()` on a disposed source. Completion of an older request does not clear the rendering status of its replacement.

The pixel budget is not a total RAM limit. Native PDFium allocations, temporary render buffers, WinUI, the CLR, GC heap capacity and GPU resources are additional.

## Evaluated alternatives

Full and partial trimmed publishes were attempted with the existing dependencies. Both failed the warnings-as-errors gate. Expanding linker diagnostics with `TrimmerSingleWarn=false` identified `IL2081` errors in generic ABI fallback code in `Microsoft.Windows.SDK.NET.dll` and `WinRT.Runtime.dll`, followed by `NETSDK1144`. Trimming remains disabled; no warnings were suppressed and no packages were changed.

The Windows App SDK runtime still includes ONNX Runtime and DirectML. Neither DLL was loaded during the benchmark. Removing these vendor runtime files manually would reduce disk size without demonstrating a RAM benefit in this scenario; they are retained with the approved dependency set.

## Measurement method

- Windows x64 on the same machine; self-contained unpackaged Release single-file binaries.
- Baseline: source before the rendering optimization, with ReadyToRun enabled and compression disabled. The requested Reading / Select Text toolbar was included in both versions.
- Synthetic 120-page text PDF, 1280 x 900 window, dark reading theme and 150% zoom. Screenshot inspection confirms the displayed zoom percentage.
- Navigate through pages 5, 10, ..., 60, then back through 55, 50, ..., 5, 1, waiting 650 ms at each destination. Sample process Private Bytes and Working Set after each destination, then after a two-second settling period.
- These are sampled peaks, not continuous peak-memory traces. Private Bytes is committed private process memory, not an exact physical-RAM count. Working Set includes shared resident pages. GPU memory is not measured separately.
- Existing settings are backed up as bytes and restored after each measurement. Run with all other reader instances closed and leave the desktop available for keyboard automation.

The initial two baseline runs measured 834.26 / 841.81 MiB sampled peak Private Bytes and 917.42 / 924.65 MiB sampled peak Working Set. Three optimization runs measured 532.17 / 535.26 / 542.46 MiB Private Bytes and 629.07 / 632.06 / 638.76 MiB Working Set. Settled Private Bytes were 618.55–671.62 MiB before and 461.57–504.98 MiB after. Idle/opened memory was essentially unchanged (approximately 184–186 MiB Private Bytes in the first comparison).

This demonstrates lower memory usage for this workload, not a universal percentage reduction or proof that no leaks exist. Large scans, images, DPI, zoom and document complexity can change the result.

Startup is a separate trade-off. Initial baseline measurements after its extraction cache existed were 2236 / 2232 ms to an enabled navigation button. The first launch of the new optimization bundle took 24206 ms; subsequent launches took 3244 / 2164 ms. The first-launch result is retained here rather than compared against warmed baseline runs. Extraction, OS caches and security scanning were not isolated, so no particular cause or startup improvement is claimed.

## Final artifact results

After the cancellation cleanup fix, the checked-in benchmark scripts explicitly returned to page 1 before the initial capture. This final script was run once against the preserved baseline and twice against the final executable. The extra navigation can change allocation/GC timing; these runs are reported separately from the earlier measurements.

| Metric | Baseline final run | Optimized final run 1 | Optimized final run 2 |
| --- | ---: | ---: | ---: |
| EXE bytes | 315,184,269 | 243,331,694 | 243,331,694 |
| EXE MiB | 300.58 | 232.06 | 232.06 |
| Sampled peak Private Bytes, MiB | 1006.05 | 551.04 | 551.26 |
| Sampled peak Working Set, MiB | 1087.79 | 647.98 | 647.66 |
| Settled Private Bytes, MiB | 572.82 | 520.02 | 472.14 |
| Startup to enabled navigation, ms | 2240 | 21392 | 2321 |

The final EXE is **22.80% smaller**, without compression. The final sampled private-memory peaks were approximately 45% below the final baseline run; the earlier comparisons were approximately 35–36% lower. The variation is reported rather than choosing one percentage as a general RAM guarantee. The first final-artifact launch required a new bundle extraction; the second reused its extraction cache. No controlled cold-start comparison was performed.

The deliverable is `artifacts/single-file/win-x64/PdfReader.App.exe`; the output directory contains exactly one file. `dotnet msbuild` evaluated single-file properties as `PublishReadyToRun=false`, `EnableCompressionInSingleFile=false` and `PublishTrimmed=False`.

## Verification

- Solution restore passed. Final Debug x64 and ARM64 builds passed with 0 warnings and 0 errors.
- Core unit/native integration tests passed 56/56; settings integration tests passed 7/7. No Core APIs, native ownership or PDFium worker boundaries changed.
- C# review approved the bitmap lifetime, UI-thread creation and cancellation cleanup with no CRITICAL/HIGH findings. Targeted whitespace and `git diff --check` passed.
- Native UI Automation verified five repeated outline destinations and their visible page indicators, Advanced commands, Appearance open/close, Find focus and toolbar visibility at 900 pixels on the optimized build.
- The benchmark visited page 60 and returned to page 1 successfully. Inspected screenshots confirm 150% zoom and rendered text. Comparing 96,330 sampled pixels within the initial page region produced zero differences between the baseline and final artifact.
- The final executable passed 160 rapid scroll-position changes and returned to the top without a crash. Its Reading / Select Text toggle passed four alternating transitions; Advanced no longer contained the old Text selection item.
- No Visual Studio F5 session, ARM64 runtime session, continuous memory trace or separate GPU-memory measurement was performed. Visual comparison covered the text benchmark; image/scanned behavior was not visually rechecked.

## Reproduce

Run from the repository root in PowerShell with no reader instances open:

```powershell
.\scripts\New-ReaderBenchmarkPdf.ps1
.\scripts\Measure-ReaderFootprint.ps1 `
  -Executable .\artifacts\single-file\win-x64\PdfReader.App.exe `
  -Label optimized-repeat
```

JSON measurements and first/middle/returned-page screenshots are written under `artifacts/optimization`. Repeat with a separately published baseline executable. Use the same document, desktop scale and settings for both. Inspect the captures to confirm 150% zoom and successful navigation.

Publish using the command in [README](../../README.md#2-publish-a-single-file-exe). To generate the original publish behavior for an isolated comparison, use the source before the rendering changes and explicitly set `-p:PublishReadyToRun=true -p:EnableCompressionInSingleFile=false`, writing to a separate output directory.

## Reference documentation

- [Microsoft: ReadyToRun compilation and its size/performance trade-offs](https://learn.microsoft.com/en-us/dotnet/core/deploying/ready-to-run).
- [Microsoft: Trimming self-contained applications and compatibility diagnostics](https://learn.microsoft.com/en-us/dotnet/core/deploying/trimming/trim-self-contained).
- [Microsoft: WinUI single-file deployment and dependency extraction](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/unpackage-winui-app#single-file-exe).
- [Microsoft: Working Set](https://learn.microsoft.com/en-us/windows/win32/memory/working-set).
