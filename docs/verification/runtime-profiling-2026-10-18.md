# Runtime profiling: memory and application activity

Date: 2026-10-18 (Asia/Jakarta). This is a measurement report; application source code and dependencies were not changed.

## Findings

The measured reader can exceed 500 MiB because the 128 MiB cache limits accounted page pixels, while the process also holds temporary rendering buffers, native display resources, runtime state and GC heap capacity. The clearest measured optimization target is repeated allocation of page-sized byte arrays.

During the completed main workload:

- Sampled peak **Private Bytes: 630.56 MiB**; sampled peak **Working Set: 725.87 MiB**. Windows reported a process-lifetime peak Working Set of **740.68 MiB**, illustrating that interval sampling misses some transient peaks.
- After the final idle interval, Private Bytes fell to **317.29 MiB** and Working Set to **418.36 MiB**.
- Runtime counters recorded **5.06 GiB of cumulative managed allocations** over approximately **160.30 seconds**. This is allocation turnover, not simultaneously resident memory.
- **121 of 124 recorded collections** had reason `AllocLarge`, with generation/depth 2. The other three had reason `Induced`; their caller was not captured.
- `System.Byte[]` accounted for **99.84% of the estimated bytes attributed by allocation-tick samples**. This is a sampled allocation signal, not an exact per-type allocation total or retained-object inventory.
- CPU consumption was **16.31 CPU-seconds** during the sampled interval: approximately **10.18% of one logical core**, or **0.73% of this 14-logical-processor machine**, averaged across active and idle periods.

The repeated navigation runs did not show monotonically increasing end-of-idle memory. This short experiment does **not** establish absence of a leak. It also cannot identify which native component owns the remaining memory.

A separate actual-scroll run reproduced the reported approximately 500 MiB footprint: sampled peak **523.05 MiB Private Bytes / 620.35 MiB Working Set**, falling to **425.76 / 523.83 MiB** after its idle interval.

## Environment and binary identity

| Item | Value |
| --- | --- |
| OS | Windows 11 Pro, version 10.0.26300, build 26300 |
| Logical processors | 14 |
| Physical memory | 16,597,598,208 bytes, approximately 15.46 GiB |
| Profiled binary | `artifacts/single-file/win-x64/PdfReader.App.exe` |
| Binary size | 243,335,990 bytes |
| Binary SHA-256 | `507ABCBC79D18A31D5B5F0A4956FC3708695900963F51D2F29FFBB87A472AAD5` |
| Repository HEAD during investigation | `eab3ee6c6c01430528eac6d3425f9e31b6ba3455` |
| Fixture | `artifacts/optimization/reading-benchmark.pdf`: 120 synthetic text pages, US Letter, 612 x 792 PDF points |
| Fixture SHA-256 | `CBE851AB213EDD6674A1413D09C255F4AFE19CA7422BB2CC057BAD165BFDCD71` |
| Window | 1280 x 900 captured pixels; `GetDpiForWindow` returned 96 |
| Main run process | PID 8364 |
| Main process sample range | 2026-10-18 10:59:42.168–11:02:22.470 UTC, or 17:59:42.168–18:02:22.470 WIB |

No reader process was found at the beginning of the investigation. The profiling harness launched its own existing Release single-file artifact and closed only that test instance. This binary was not republished during the task; a source commit alone does not prove its exact build provenance. The binary hash identifies what was actually measured. The later Debug build validates the current source separately.

## Collection method

The harness used Windows UI Automation for actions, `System.Diagnostics.Process` for process measurements, and an EventPipe session for `System.Runtime` counters and CLR GC events. It used diagnostic assemblies already installed with .NET SDK 10.0.401 and Visual Studio 18 Community; no NuGet dependency or global diagnostic tool was installed.

- Process polling target: 250 ms. Actual main-run median spacing: **254.98 ms**; maximum: **358.49 ms**. There were **622 process samples**.
- Runtime counters: requested every second; **4,320 records**, covering 27 counters and 160 intervals.
- CLR events: **1,104 records**, comprising 732 allocation ticks, 124 GC starts, 124 GC stops and 124 heap-stat events.
- An activity log recorded phase timestamps and requested page destinations. Each page jump had at least 650 ms dwell. Reading back the destination textbox verifies input, not completion of page rendering.
- Screenshots confirmed displayed text, 150% and 200% zoom, and the change from the warm dark palette to original white pages. They do not verify every intermediate destination.
- Existing settings were held as bytes and restored by the harness in `finally` after closing its test instance. The test began with default per-file state and the warm dark reading palette.
- No forced GC, working-set trimming, application instrumentation or runtime source modification was used by the harness.

All memory tables use **MiB = 1,048,576 bytes**. The legacy `System.Runtime` counters reporting decimal MB were converted using `MB * 1,000,000 / 1,048,576`. Private Bytes is private committed process memory, not physical RAM. Working Set includes resident shared pages. CPU uses 100% per logical core unless explicitly normalized to the whole machine.

## Main activity timeline

The main workload requested pages `5, 10, ..., 60, 55, ..., 5, 1` three times at 150% zoom, with an idle interval after each pass. It then changed to 200%, disabled dark reading, requested pages `5, 10, ..., 60, 1` in Original mode, and remained idle.

"End" means the last observed sample before the next phase marker. Phase boundaries include UI Automation, menu and screenshot work; these values are not controlled steady-state snapshots. Runtime counter intervals may straddle phase boundaries.

| Phase | Duration, s | Peak Private, MiB | End Private, MiB | End Working Set, MiB | CPU-seconds within phase |
| --- | ---: | ---: | ---: | ---: | ---: |
| Opened document / idle | 13.00 | 187.31 | 177.38 | 273.15 | 0.344 |
| Set zoom to 150% | 5.62 | 293.71 | 293.59 | 389.99 | 0.234 |
| Navigation pass 1 | 16.73 | 630.56 | 515.45 | 612.42 | 5.016 |
| Idle after pass 1 | 12.01 | 515.58 | 491.51 | 588.56 | 0.047 |
| Navigation pass 2 | 16.71 | 539.77 | 517.85 | 615.38 | 3.625 |
| Idle after pass 2 | 12.01 | 517.98 | 486.36 | 584.01 | 0.141 |
| Navigation pass 3 | 16.61 | 525.94 | 410.32 | 509.77 | 4.031 |
| Idle after pass 3 | 12.06 | 378.68 | 310.98 | 410.50 | 0.188 |
| Set zoom to 200% | 12.59 | 521.92 | 484.86 | 583.61 | 0.188 |
| Switch to Original theme | 12.53 | 557.67 | 557.17 | 655.50 | 0.219 |
| Original-theme navigation | 8.99 | 516.91 | 348.37 | 448.12 | 1.656 |
| Final idle | 21.18 | 348.50 | 317.29 | 418.36 | 0.125 |

Per-phase CPU deltas omit the small gaps between the first and last samples of adjacent phases, so their sum differs from the total sampled CPU delta. The final idle phase also includes metadata collection after the planned 20-second wait.

Peak observed CPU over a single polling interval was **117.15% of one logical core**, approximately **8.37% of the machine**. Low average CPU therefore does not imply no short rendering bursts.

## Actual scrolling activity

A separate fresh process used the same binary, fixture, warm dark palette, 96 DPI and 1280 x 900 window. It opened the document, set 150% zoom, then issued **50 vertical `ScrollPattern.SetScrollPercent` changes**, requesting 2%, 4%, ..., 50%, 48%, ..., 0%, with a minimum 180 ms dwell. UI Automation reported a maximum actual position of **49.97%** and a final position of **0%**. The maximum sampled difference from the requested position was **0.81 percentage points**; scrolling/layout can still be settling at each readback.

The scroll interval lasted **9.74 seconds**. It was followed by a 20-second no-input wait and a screenshot/metadata capture. The full sample interval lasted **51.24 seconds** (18:04:44.780–18:05:36.015 WIB), with **200 process samples**, median spacing **254.61 ms**, and maximum spacing **443.62 ms**.

| Scroll-run phase | Peak Private, MiB | End Private, MiB | Peak Working Set, MiB | End Working Set, MiB | CPU-seconds |
| --- | ---: | ---: | ---: | ---: | ---: |
| Opened document / idle | 190.45 | 174.42 | 284.67 | 270.24 | 0.297 |
| Set zoom to 150% | 271.18 | 270.70 | 370.74 | 367.07 | 0.203 |
| Scroll to approximately 50% and back | 523.05 | 474.96 | 620.35 | 572.80 | 4.953 |
| Idle after scrolling | 475.08 | 425.76 | 572.88 | 523.83 | 0.156 |

During the scrolling phase, counter increments recorded **1.37 GiB** of managed allocation traffic, with the highest observed one-second rate around **222.41 MiB/s**. There were **36 Gen 2 GC counter increments** in that phase. The full scroll run recorded 43 collections and zero exception-counter increments. The last reported managed heap size was **30.80 MiB**, compared with **107.29 MiB** GC commitment and **425.76 MiB** process Private Bytes.

Windows' full-process lifetime peak Working Set for this run was **650.52 MiB**, above the sampled maximum of 620.35 MiB. These observations support substantial transient buffer traffic during scrolling, but do not measure frame rate, missed frames, smoothness or per-input latency.

## Managed allocation and GC evidence

| Observation | Measurement |
| --- | ---: |
| Total managed allocation counter increments | 5,437,464,760 bytes / 5.06 GiB |
| Highest one-second allocation rate | Approximately 150.04 MiB/s |
| Maximum reported managed heap size | 134.34 MiB |
| Maximum reported GC committed memory | 181.23 MiB |
| Final reported managed heap size | 40.71 MiB |
| Final reported GC committed memory | 69.11 MiB |
| Final LOH size at the most recent collection | 26.30 MiB |
| Gen 2 GC counter increments / CLR GC starts | 124 / 124 |
| Recorded GC pause counter increments | 468.16 ms total |
| Allocation ticks attributed to `System.Byte[]` | 647 of 732 |
| Estimated sampled bytes attributed to `System.Byte[]` | 5,430,644,592 bytes |

GC committed memory and generation/LOH size counters describe GC state at the most recent collection; they can lag current allocations. They must not be summed with other counters as independent pools. The allocation rate is managed allocation traffic and excludes native rendering allocations. Allocation-tick estimates identify the allocation type triggering each sample and can include bytes since an earlier tick; they are not an exact allocation census.

Many sampled byte-array object sizes were around **7,755,296 bytes** at 150% and **13,787,168 bytes** at 200%. The fixture's expected BGRA pixel payloads are:

| Zoom at 96 DPI | Pixel dimensions | One BGRA payload |
| --- | --- | ---: |
| 150% | 1224 x 1584 | 7,755,264 bytes / 7.40 MiB |
| 200% | 1632 x 2112 | 13,787,136 bytes / 13.15 MiB |

The small difference between payload and sampled object size is consistent with array overhead. Together with the source allocation sites, this is strong evidence that page-sized pixel buffers dominate managed allocation turnover. No allocation stacks were captured, so the measurements do not assign every array to a particular method.

At the end of idle after pass 2, Private Bytes was **486.36 MiB**, while the latest reported managed heap size was **104.83 MiB** and GC commitment **114.88 MiB**. At final idle, these were **317.29**, **40.71** and **69.11 MiB**, respectively. This confirms that managed object size alone is not an adequate explanation of the process footprint. The difference is not a measurement of PDFium memory: it can include native allocations, display buffers, runtime/JIT state, thread stacks and other private commitments.

## Source correlation and optimization priorities

These explanations come from source inspection, correlated with the runtime measurements; native ownership was not measured directly.

1. [PdfDocumentService.cs](../../PdfReader.Core/PdfDocumentService.cs) allocates a native PDFium bitmap, allocates a managed BGRA array and copies the rendered pixels into it. Native bitmap/page handles use deterministic disposal.
2. [PageThemeProcessor.cs](../../PdfReader.App/Rendering/PageThemeProcessor.cs) uploads that array into a `CanvasBitmap`, draws into a `CanvasRenderTarget`, then calls `GetPixelBytes()` to return another managed array. Native/GPU and managed representations coexist during parts of this path.
3. [PageFrame.cs](../../PdfReader.App/Rendering/PageFrame.cs) copies the result into a `WriteableBitmap`. It then clears the managed array reference and reuses the display bitmap. The earlier persistent duplicate-array problem is already addressed, but temporary copy traffic remains.
4. [PageRenderPipeline.cs](../../PdfReader.App/Rendering/PageRenderPipeline.cs) limits the LRU to **128 MiB of accounted pixel payload**. It does not measure total process memory or actual device resource allocation. A visible page can also retain its display image after its cache entry is evicted; cache bookkeeping does not account for every remaining display reference.
5. [ViewportRenderCoordinator.cs](../../PdfReader.App/Rendering/ViewportRenderCoordinator.cs) renders visible pages plus two pages on each side, cancels requests outside that range and releases distant page-image references. PDFium work is serialized, but subsequent theming/upload and pending results can overlap. No in-flight buffer count or cache-hit trace was captured.

The first optimization to investigate is reducing full-page copies, especially the GPU-to-CPU readback followed by another display upload. A second candidate is limiting the number of in-flight full-page buffers and checking cancellation between rendering stages. Either change needs measurement against image preservation, scans, visual quality and scrolling performance. Reducing the cache budget blindly may increase re-rendering and allocation traffic; it should be tested as a trade-off.

The Original-mode measurements are order-dependent and use a different navigation sequence at 200%, after dark-mode allocations. They do not establish a controlled RAM saving from disabling dark mode. The temporary increase after switching themes is real in this run, but its allocation ownership was not isolated.

## Other observed activity

- Main-run process thread count ranged from **29 to 57**; handles ranged from **927 to 1,001**. Final values were **29 threads and 944 handles**. This short run shows no monotonically increasing handle count, but is not a handle-leak test.
- Sampled thread-pool queue length was **0** at all one-second observations. Short queues between observations remain possible.
- The runtime recorded **four thrown exceptions**, all in the Original navigation interval. Exception types and stacks were not enabled, so these cannot be classified as expected cancellation or application faults from this trace. The scripted workload still completed and the reader closed normally.
- Loaded modules included PDFium (`156.0.8076.0`), WinUI and Direct3D/Direct2D components. Module presence proves loading, not allocation ownership or which rendering path ran for every page.
- No per-method CPU stacks, file/network I/O trace, native heap census, GPU memory counters, frame timing or interaction-latency measurements were collected.

## Evidence and reproduction

Local evidence is saved under `artifacts/profiling/2026-10-18/`. This directory is ignored by Git; the Markdown report is the durable repository artifact.

| Artifact | Purpose |
| --- | --- |
| `validated/process.jsonl` | Timestamped Private Bytes, Working Set, lifetime peak Working Set, CPU time, threads and handles |
| `validated/runtime.jsonl` | Unmodified System.Runtime counter payloads |
| `validated/gc-events.jsonl` | GC allocation ticks, starts/stops and heap stats |
| `validated/activity.jsonl` | Phase markers and requested navigation inputs |
| `validated/metadata.json` | Binary/fixture hashes, DPI, loaded modules and final thread data |
| `validated/summary.json` | Derived phase and global metrics |
| `validated/*.png` | Window captures at selected phases |
| `Collector.csproj`, `Program.cs` | Local collector using preinstalled diagnostic assemblies |
| `Profile.ps1`, `Analyze.py` | Workload automation and calculation source |
| `Scroll.ps1`, `scroll/*` | Actual-scroll workload, its raw process/runtime/GC data, activity/readback log, metadata, screenshots and derived summary |

An exploratory run in the parent directory recorded **620.09 MiB** peak Private Bytes before the automation failed to locate a theme toggle under the wrong accessible name. Its process/runtime counters remain saved, but it is not the completed main workload. The corrected run used a fresh directory and successfully completed the theme change. The exploratory GC event file was empty before the CLR parser subscription was corrected; no allocation findings are based on that file.

On this machine, with the same installed SDK/Visual Studio assemblies, no existing reader instance and the desktop available:

```powershell
dotnet build .\artifacts\profiling\2026-10-18\Collector.csproj -c Release
powershell -NoProfile -ExecutionPolicy Bypass -File .\artifacts\profiling\2026-10-18\Profile.ps1 -RunLabel repeat-01
python .\artifacts\profiling\2026-10-18\Analyze.py repeat-01
powershell -NoProfile -ExecutionPolicy Bypass -File .\artifacts\profiling\2026-10-18\Scroll.ps1 -RunLabel scroll-repeat-01
python .\artifacts\profiling\2026-10-18\Analyze.py scroll-repeat-01
```

The harness refuses to reuse an output directory containing process samples and refuses to start when another `PdfReader.App` instance is found. Measurement includes observer overhead from EventPipe, counters, UI Automation and screenshots; it is not an uninstrumented baseline. EventPipe attaches after process launch, so startup allocations and the complete startup peak are not fully captured.

## Verification

- Solution restore passed: `dotnet restore .\PdfReader.slnx`.
- Debug x64 build passed with **0 warnings and 0 errors**: `dotnet build .\PdfReader.slnx -c Debug -p:Platform=x64 --no-restore`.
- Core unit/native integration tests passed **57/57**: `dotnet test .\PdfReader.Core.Tests\PdfReader.Core.Tests.csproj -c Debug --no-restore`.
- Settings tests passed **11/11**: `dotnet test .\PdfReader.Settings.Tests\PdfReader.Settings.Tests.csproj -c Debug --no-restore`.
- Independent profiling-harness review identified and resolved the CLR event subscription issue and required explicit limitations for sampling, allocation attribution, CPU normalization and navigation evidence.
- Both PowerShell workload scripts passed parser checks. Both completed runs had empty collector stderr and no EventPipe attach/read error artifact. Repository diff/whitespace checks passed.
- App/Core source and native ownership boundaries were not changed. The collector is an isolated local diagnostic program outside the solution.
- No Visual Studio F5 session, ARM64 execution, sustained open/close leak test, memory dump or native/GPU ownership profile was performed.

Self-audit: accuracy 4/5 (raw counters and independent calculation review; no native ownership trace), completeness 4/5 (idle/navigation/zoom/theme/actual scrolling covered; long-duration leak and frame-latency tests absent), clarity 4/5 (explicit units and phase limitations, with several distinct memory metrics), actionability 4/5 (prioritized source sites and local reproduction harness; diagnostic assembly paths are machine-specific), conciseness 4/5 (detailed evidence retained for review). Overall 4.0/5. The next highest-value measurement is native allocation ownership combined with live pixel-buffer/cache occupancy counters.

## Reference documentation

- [Microsoft: Well-known EventCounters](https://learn.microsoft.com/en-us/dotnet/core/diagnostics/available-counters) defines the runtime counters and units used here.
- [Microsoft: Large object heap on Windows](https://learn.microsoft.com/en-us/dotnet/standard/garbage-collection/large-object-heap) explains large-array collection and reuse.
- [Microsoft: Working Set](https://learn.microsoft.com/en-us/windows/win32/memory/working-set) explains resident process memory.
- [Microsoft: Debug a memory leak](https://learn.microsoft.com/en-us/dotnet/core/diagnostics/debug-memory-leak) describes the additional evidence needed to diagnose retained-object growth.
