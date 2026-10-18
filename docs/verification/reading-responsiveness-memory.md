# Reading responsiveness and memory experiments

Measured on 2026-10-18, Windows 11 x64, 96 DPI, 1280 × 900 window.

## Outcome

The measurements establish a repeatable software responsiveness baseline. None of the tested changes demonstrated a consistent reduction in total process memory while preserving the measured reading experience. All cache-budget and scheduling experiments were reverted. The retained application changes provide opt-in diagnostics; the page cache remains 128 MiB, with the original render scheduling, full-resolution output and theme processing.

In four baseline runs, the median of the per-run p95 UI rendering-callback gaps was **19.80 ms during small-step scrolling** and **21.92 ms during fast scrolling**. Navigation reached a matching, current-resolution primary-page bitmap at a median per-run p95 of **81.77 ms**. Zoom reached that state at **300.87 ms**, including the existing 180 ms zoom-settle delay.

These are UI-thread observations, **not presented-frame FPS or physical input-to-photon latency**. Windows policy denied the WPR GPU capture (`0xc5585011`), so actual display presentation was not measured.

The stress workload reached sampled Private Bytes peaks of **894.57–986.05 MiB** in the baseline. Final idle values varied from **407.99–545.15 MiB**. These results do not establish that 400–650 MB is necessary for a good experience, or that the app is free of leaks. The workload, continuous diagnostic observer and zoom sequence differ from the [earlier runtime profile](runtime-profiling-2026-10-18.md); its memory numbers are not a directly comparable control.

## Method and interpretation

- Local deterministic 120-page Letter-size text PDF; Warm reading theme, empty test history, no existing reader instance. The script backs up and restores the user's settings bytes, and closes only the instance it launched.
- Each run opens the PDF, waits 12 seconds, resets zoom to 100%, steps to 150%, waits, performs 400 small scroll requests and 50 fast scroll requests, navigates through 11 page requests, steps from 150% to 200% in five increments, then idles for 10 seconds.
- Small scrolling requests move in 0.1 percentage-point increments with at least 20 ms dwell, twice from 0% to 10% and back. Fast scrolling uses 2 percentage-point increments with at least 180 ms dwell, from 0% to 50% and back. This is a controlled UI Automation workload rather than a real pointer/wheel session. UI Automation call overhead is included in response time; requested dwell is a minimum.
- Process Private Bytes, working set, CPU, handles and threads are sampled every 250 ms. EventPipe collects `System.Runtime` counters at one-second intervals and CLR GC events. Peaks are sampled lower bounds. Private Bytes measures private committed memory, not physical RAM residency. Working set includes resident shared pages; neither metric identifies native/GPU ownership.
- `CompositionTarget.Rendering` records a monotonic timestamp, actual viewport position, actual primary visible page, zoom, bitmap readiness and page-cache bytes through a bounded asynchronous CSV writer. `Ready` requires the current resolution and reading theme. A false flag can mean a scaled previous bitmap is still being displayed; it does **not** necessarily mean a blank page.
- First response means the first callback with the requested viewport/page/zoom. Ready response additionally requires a current-resolution primary bitmap. Scroll matching uses a 0.025 percentage-point tolerance. A request is observed until the next request or two seconds, whichever comes first. Unmatched requests are censored, not assigned zero latency; successful-response percentiles exclude them and must be read with their counts.
- Callback gaps do not cover all visible pages or prove compositor presentation. Subscribing to this event itself creates continuous rendering activity, including during idle. Every comparison uses the same probe; normal launches have no frame-event subscription. See Microsoft's [Rendering event documentation](https://learn.microsoft.com/en-us/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.media.compositiontarget.rendering?view=windows-app-sdk-1.8).
- Tables use medians of per-run statistics, not a pooled p95. There are four baseline runs and three runs per candidate, without a statistical significance claim. Baseline2/cache32-1/baseline3/cache32-2/baseline4/cache32-3 were interleaved; cache96-1/baseline5/cache96-2/cache96-3 followed. The later scheduling trials ran consecutively. Machine state and GC timing introduce substantial variation.

The analyzer rejects incomplete workloads, missing completion footers, dropped UI samples, EventPipe error artifacts, nonempty collector stderr, empty recordings and absent allocation/Gen2 counters.

## Baseline responsiveness

Callback cadence across `baseline-2` through `baseline-5`:

| Activity | Median per-run p50 gap | Median per-run p95 gap | Largest gap across runs | Gaps >33.3 ms / >50 ms across runs |
| --- | ---: | ---: | ---: | ---: |
| Small-step scrolling | 16.66 ms | 19.80 ms | 24.72 ms | 0 / 0 |
| Fast scrolling | 16.68 ms | 21.92 ms | 29.77 ms | 0 / 0 |
| Page navigation | 16.66 ms | 18.67 ms | 37.42 ms | 1 / 0 |
| Zoom response | 16.66 ms | 18.23 ms | 41.55 ms | 2 / 0 |

Software interaction response:

| Activity | First matching callback p95 | Matching ready bitmap p95 | Ready successes / requests | No matching response / no ready response |
| --- | ---: | ---: | ---: | ---: |
| Small-step scrolling | 20.41 ms | 20.41 ms | 1,581 / 1,600 | 19 / 19 |
| Fast scrolling | 24.62 ms | 41.84 ms | 141 / 200 | 19 / 59 |
| Page navigation | 51.12 ms | 81.77 ms | 44 / 44 | 0 / 0 |
| Zoom | 18.91 ms | 300.87 ms | 20 / 20 | 0 / 0 |

Fast-scroll ready-response p95 ranged from 31.10 to 60.29 ms among successful observations, with a largest successful response of 174.04 ms. The 59 censored requests prevent interpreting 41.84 ms as an unconditional response guarantee. Primary-page readiness was false in **5.21–42.68% of fast-scroll callbacks**, versus 0% during small-step scrolling. Stable callback cadence alone therefore overstates reading smoothness during aggressive jumps.

Navigation ready-response p95 ranged from 71.83 to 117.39 ms; zoom ranged from 244.34 to 352.03 ms. Only 11 navigation and five zoom requests occur per run, so these tail estimates are exploratory. Scaling the old bitmap gives feedback well before the settled zoom render completes.

## Memory and allocation results

All memory values are MiB (`bytes / 1,048,576`); allocation traffic is GiB over the recording, not retained memory. CPU is process CPU seconds over the sampled interval.

| Variant | Runs | Median peak Private Bytes | Median final Private Bytes | Median peak working set | Median allocation traffic | Median Gen2 collections | Median CPU seconds |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| Baseline: page LRU 128, original Win2D cache | 4 | 905.17 | 509.86 | 1,005.94 | 2.489 GiB | 75 | 19.42 |
| Win2D cache capped at 32 MiB | 3 | 968.92 | 576.29 | 1,066.79 | 2.497 GiB | 72 | 19.09 |
| Page LRU 96, original Win2D cache | 3 | 908.13 | 439.81 | 986.18 | 2.923 GiB | 81 | 19.33 |
| Visible-page-first request order, LRU 128 | 3 | 998.06 | 600.64 | 1,096.84 | 2.483 GiB | 66 | 19.02 |

Per-run Private Bytes expose the variation hidden by the medians:

| Run | Peak MiB | Final MiB |
| --- | ---: | ---: |
| baseline-2 | 986.05 | 545.15 |
| baseline-3 | 913.79 | 539.74 |
| baseline-4 | 894.57 | 407.99 |
| baseline-5 | 896.55 | 479.99 |
| cache32-1 | 968.92 | 604.79 |
| cache32-2 | 991.84 | 552.70 |
| cache32-3 | 952.07 | 576.29 |
| cache96-1 | 986.36 | 543.46 |
| cache96-2 | 798.59 | 416.49 |
| cache96-3 | 908.13 | 439.81 |
| visible-first-1 | 959.24 | 536.27 |
| visible-first-2 | 1,061.39 | 657.07 |
| visible-first-3 | 998.06 | 600.64 |

### Experiment decisions

1. **Win2D32: rejected.** The measured device default was 128 MiB and the trial set 32 MiB. Overall memory was higher, with no consistent benefit. `MaximumCacheSize` describes cached intermediate images and is not a cap on all graphics/process memory; it can also be exceeded within a frame. See the [Win2D property documentation](https://microsoft.github.io/Win2D/WinUI3/html/P_Microsoft_Graphics_Canvas_CanvasDevice_MaximumCacheSize.htm).
2. **Page LRU 96: rejected.** Maximum observed cached pixel payload fell from 127.78 to 95.33 MiB. The final-private median was descriptively 13.7% lower, but the ranges overlap widely and peak-private median did not improve. Allocation traffic increased 17.4%; Gen2 counts were 81–83 versus 66–78 in baseline. A smaller retained page cache increases reconstruction work and does not establish a safe overall RAM saving here.
3. **Visible-page-first ordering: rejected.** This additional targeted responsiveness trial retained the original `Task.Run` admission boundary, resolution and two-page buffer. Peak-private median was 10.3% higher and final-private median 17.8% higher. Navigation/zoom readiness did not improve. Primary fast-scroll not-ready fractions overlapped baseline substantially. Reordering `Task.Run` submissions is best-effort and does not enforce worker priority.

| Variant | Small-scroll callback-gap p95 | Fast-scroll callback-gap p95 | Fast-scroll ready p95 | Fast-scroll ready successes / requests | Navigation ready p95 | Zoom ready p95 |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| Baseline | 19.80 ms | 21.92 ms | 41.84 ms | 141 / 200 | 81.77 ms | 300.87 ms |
| Win2D32 | 19.78 ms | 21.29 ms | 66.77 ms | 127 / 150 | 72.00 ms | 283.25 ms |
| Page LRU 96 | 19.66 ms | 21.58 ms | 74.58 ms | 133 / 150 | 84.88 ms | 281.98 ms |
| Visible-page-first | 19.80 ms | 21.41 ms | 42.57 ms | 120 / 150 | 87.22 ms | 316.47 ms |

The two memory variants exhausted the planned RAM experiment budget; the additional scheduling trial also failed acceptance. No speculative native-buffer ownership refactor, forced GC, working-set trimming, resolution reduction or dependency addition was retained.

An earlier `priority` prototype removed `Task.Run`. Review found that `PdfiumWorker.RunAsync` can synchronously wait for bounded queue admission; that prototype could therefore block the UI thread. It was discarded, and its two pilot recordings are excluded from all comparison tables. The planned third launch was deliberately prevented using the harness's existing-output guard. `baseline-1` was also a probe-development pilot and is excluded because its diagnostic build differed.

## Display-quality checks

The deterministic three-page quality fixture contains text/vector content, a partially covering RGB image with surrounding text, and a full-page RGB image that takes the scanned-page path. Original, Warm and OLED reading themes were captured at fit-page zoom. Each comparison examines 935,664 content-region pixels, excluding most window chrome.

| Candidate | Exact matches / nine cases | Nonzero differences by theme/page | Largest channel difference |
| --- | ---: | --- | ---: |
| Page LRU 96 | 4 / 9 | Original p2:38px, p3:7px; Warm p1:39px, p2:51px; OLED p2:46px | 1 / 255 |
| Visible-page-first | 6 / 9 | Original p2:38px, p3:8px; OLED p2:46px | 1 / 255 |

The original **strict exact-pixel gate failed** for LRU 96. A separate unchanged-baseline repeat of all three Warm pages matched exactly. The small candidate differences cannot therefore be dismissed as proven capture noise. The later scheduling trial passed an explicitly relaxed gate of at most 100 changed pixels with at most one channel step; this is tolerance-based agreement, not exact identity. Neither candidate was promoted.

Representative Warm embedded-image and OLED scanned-page screenshots were inspected: the embedded image remains colored, while the scanned page is remapped across the full image. This is a limited screenshot check, not an interactive comfort study. Final source keeps the original luminance remap, image restoration, scanned detection, page resolution, prefetch buffer and cache policy. No F5 visual verification was performed.

## Retained tools and reproduction

Run commands from the repository root in separate PowerShell processes for each workload. No reader instance should be running. Raw artifacts remain local under `artifacts/reading-performance/`; they are ignored by Git.

The collector references the already installed .NET diagnostic client and Visual Studio TraceEvent assemblies. It adds no app package dependency or new NuGet package. On another installation, override `DiagnosticsClientPath` and `TraceEventPath` when building it. Reference versions in this environment were 0.2.14.42413 and 3.1.27, respectively. The comparison runs used the prior local collector with the same collection logic; the committed copy removes an unused cancellation source and formats the code.

```powershell
dotnet build scripts/ReadingExperienceCollector/Collector.csproj -c Release -o artifacts/reading-performance/collector
powershell -NoProfile -File scripts/New-ReaderBenchmarkPdf.ps1
python scripts/New-RenderingQualityPdf.py

# Use a Release x64 binary published with the retained diagnostic source.
powershell -NoProfile -File scripts/Measure-ReadingExperience.ps1 `
  -Executable artifacts/reading-performance/retained/PdfReader.App.exe -Label my-run-1
python scripts/Analyze-ReadingExperience.py artifacts/reading-performance/runs/my-run-1

powershell -NoProfile -File scripts/Verify-ReadingPixels.ps1 `
  -Executable artifacts/reading-performance/retained/PdfReader.App.exe -Label my-quality-t1 -Theme 1
```

The executable can be published using the [README single-file command](../../README.md), changing only its output folder to `artifacts/reading-performance/retained`. The measurement script sets `PDFREADER_PERF_OUTPUT` only for its child launch. Without that environment variable, the app does not subscribe to the per-frame observer or create its queue/writer. Cache diagnostics read the existing Win2D limit; they do not change it.

### Harness incident and correction

The first retained-build smoke run completed its workload but failed during cleanup. A newly added typed PowerShell parameter named `Collector` collided, case-insensitively, with the process variable `$collector`; cleanup attempted `WaitForExit` on a string. The user's local settings/history had been replaced with benchmark settings and restoration was skipped. The pre-run settings were held only in the terminated script's memory. No backup was found, so the original preferences/history could not be recovered. The test reader was subsequently identified by its recorded PID and executable path and closed. The remaining benchmark settings were preserved as incident evidence, not represented as the user's recovered settings.

The corrected parameter is `CollectorPath`. Both harnesses now save `settings.original.json` or an originally-absent marker before mutation, reject any nonempty output directory, and use nested `finally` blocks so settings restoration still runs after collector/reader cleanup errors. A deliberately injected exception immediately after settings overwrite returned a failure exit code and restored the exact pre-test bytes: the before/after SHA-256 values matched, with no reader left running. Evidence: `cleanup-failure-result.json` and `runs/cleanup-failure-check/`. This validates recovery of the state present after the incident; it does not recover the lost earlier preferences.

The 13 comparison runs preceded this parameter change and are unaffected. The failed `retained-smoke` recording is excluded from comparison statistics. The scripts temporarily use the real local settings path; persistent backups and failure cleanup are therefore required even for future measurement-only runs.

For two complete quality sets named `before-t0..2` and `after-t0..2`:

```powershell
powershell -NoProfile -File scripts/Compare-ReadingPixels.ps1 `
  -BaselinePrefix before -CandidatePrefix after
```

This defaults to exact comparison. Explicit tolerance switches are available, and must be disclosed if used. Each run includes binary/fixture hashes, process/runtime/GC data, actions, frame samples and screenshots. `experience-summary.json` is generated by the analyzer; `final-results.json` aggregates the four baseline and nine accepted-for-analysis candidate recordings.

Identity of measured binaries (SHA-256):

| Build | SHA-256 |
| --- | --- |
| Baseline2–5 | `B3CF5F4F85C04C97AB3878EADF505C6E593CE8D81F60354E9E438DEFCEC6BA0C` |
| Win2D32 | `214A531B41F6051B70CCA85B67AECF33F28000F79B8C5C5D1B8CB9E11B4BFA3E` |
| Page LRU 96 | `3244C34FE3B0502739588EF44BED1975DF9DFCAD0085FEE94C48C6F20DD50611` |
| Visible-page-first | `4CE767448412EC0391C6D6A812F14C1742C93EDC100F0001A3C6EC62A8CBAB92` |
| Retained diagnostic build | `88A5940BA7361EFD1F245517DCBC83C3F73C3B94DB24252F2F6D42C897AB02B8` |

Benchmark SHA-256: `CBE851AB213EDD6674A1413D09C255F4AFE19CA7422BB2CC057BAD165BFDCD71`. The existing distributable under `artifacts/single-file/win-x64/` was not overwritten with a trial build.

## Verification and limits

- Solution restore passed. Debug x64 build passed with 0 warnings and 0 errors. Core tests passed 57/57; settings tests passed 11/11. The retained Release x64 single-file publish succeeded. The standalone collector built with 0 warnings and 0 errors.
- PowerShell parser checks passed for all three new harness/comparison scripts. Python compilation checks passed for the analyzer and quality-fixture generator. Targeted C# whitespace verification, `git diff --check` and report local-link checks passed. All 13 comparison recordings passed the final analyzer's validation.
- The retained diagnostic build completed the full workload using the committed collector and corrected harness (`runs/retained-smoke-fixed`): 3,987 UI samples, zero dropped samples, successful final analysis, exact settings-backup hash restoration and no remaining reader. This is a tooling smoke check, excluded from baseline/trial statistics. A separate retained-build Warm quality run matched all three baseline screenshots exactly and restored settings successfully.
- The intentional post-overwrite failure and nonempty-directory reuse checks passed without changing the pre-test settings bytes. These checks validate the corrected cleanup, not recovery of the settings lost during the earlier incident.
- Independent C# and measurement review approved the retained changes with no CRITICAL/HIGH issues; its output-reuse hardening finding was resolved and checked. No new behavior tests were added for the reverted cache constants/order; the runtime trials and pixel checks supplied the relevant acceptance evidence.
- Core has no UI references; no Core/native code or native ownership changed. The pipeline/coordinator have no retained diff. Native admission remains off the UI thread, and shutdown starts native cleanup before awaiting diagnostic drain.
- No physical presentation trace, input-to-photon capture, high-DPI run, ARM64 runtime test, sustained open/close leak test or precise native/GPU memory attribution was obtained. Text-only stress measurements do not predict large scanned production PDFs. Small cohorts and nonrandomized order limit causal claims.

The next useful memory investigation is ownership attribution during the settled-zoom peak: live managed page buffers, native PDFium allocations and graphics surfaces. Reducing duplicate/transient buffers is a hypothesis requiring allocation evidence and cancellation/lifetime tests, not a change justified by the cache trials alone. Actual presentation smoothness needs an approved ETW/Present capture environment or an external high-speed camera.

## Self-audit

| Criterion | Score | Evidence and remaining improvement |
| --- | ---: | --- |
| Accuracy | 4/5 | Raw measurements and independent recalculation support the tables; actual display presentation and allocation ownership remain unavailable. |
| Completeness | 3/5 | Responsiveness proxies and RAM trials completed; presented frames were not measured, RAM reduction was not established, and the harness incident lost prior local preferences. |
| Clarity | 4/5 | Explicit units, censoring and trial decisions; interpreting callback readiness still requires the method notes. |
| Actionability | 3/5 | Reusable tools, raw artifacts, corrected failure recovery and a published diagnostic build are available; diagnostic assembly paths remain installation-specific and earlier preferences cannot be restored. |
| Conciseness | 4/5 | Results lead the report; detailed failure/variance evidence makes the document longer. |

Overall: 3.6/5. Highest-impact improvements are obtaining real presentation/allocation traces, isolating future measurements from the user's settings store, and testing any buffer-lifetime optimization against the same readiness/pixel workload. The user would reasonably regard the unresolved RAM goal and lost preferences as material limitations despite the completed measurements.
