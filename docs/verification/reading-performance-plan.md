# Reading performance measurement and memory optimization

## Scope and acceptance

Measure baseline UI render-callback cadence and automation-request-to-render-callback response for scrolling, page navigation and zoom. These are software/UI-thread proxies, not compositor presentation FPS or input-to-photon latency. Use the existing 120-page benchmark, fixed window/DPI/theme and identical scripts before and after. Retain full-resolution rendering, the two-page prefetch window, the warm luminance remap, image restoration and scanned-page behavior. The baseline page-cache budget is 128 MiB; a smaller budget is acceptable only with measured UX and pixel verification.

Search budget: at most two small production variants, with three baseline and three candidate runs where practical. Stop exploring when a repeated memory improvement preserves correctness and measured UI behavior. No new dependencies. No GC-forcing or working-set trimming.

Correctness gate: zero-warning x64 build, Core and settings tests, pixel comparisons on text/image/scanned fixtures, cancellation/lifetime checks for changed ownership, independent C# review and reproducible before/after data. Do not describe callback cadence as displayed FPS. Record failed attempts and test gaps.

## Hypotheses to evaluate

1. A bounded Win2D intermediate-surface cache can reduce memory retained by the graphics device while preserving output pixels. Verify the installed API and benchmark it before promotion.
2. After early cache32 runs showed no consistent RAM benefit, test a 96 MiB page-frame LRU with the original Win2D device-cache policy. At 150%/96 DPI the budget still accommodates approximately twelve Letter page payloads. This isolates one policy change and avoids an unnecessary native-buffer ownership refactor. Compare response distributions, censored/not-ready responses and complete rendered pixels before promotion.

The opt-in `PDFREADER_PERF_OUTPUT` diagnostic capture uses a bounded queue and a background CSV writer. The normal application does not subscribe to per-frame callbacks. Both baseline and candidate performance runs must use the same probe because subscribing changes UI-thread activity.

## Recorded outcome

Both memory variants failed acceptance and were reverted. An additional targeted visible-page-first scheduling trial also failed to show an overall benefit and was reverted. An earlier prototype that removed `Task.Run` was discarded after review identified synchronous worker-admission waits. Final source retains measurement instrumentation and the original rendering/cache policies. See [the completed report](reading-responsiveness-memory.md) for all 13 comparison runs, censored response counts, pixel differences and verification limits.

A later harness packaging error prevented restoring local user settings in one smoke run. Its pre-run settings existed only in process memory and could not be recovered. The corrected harness persists backups before mutation, rejects nonempty output directories, and restores settings in nested cleanup paths. The incident and failure-path validation are recorded in the completed report.
