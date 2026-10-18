"""Analyze the opt-in UI callback probe and matching process/EventPipe recordings."""

import bisect
import csv
import io
import json
import math
import sys
from datetime import datetime
from pathlib import Path


def percentile(values, quantile):
    values = sorted(values)
    if not values:
        return None
    index = (len(values) - 1) * quantile
    return values[math.floor(index)] + (values[math.ceil(index)] - values[math.floor(index)]) * (index % 1)


def distribution(values):
    return {"count": len(values), "p50_ms": percentile(values, 0.50), "p95_ms": percentile(values, 0.95),
            "p99_ms": percentile(values, 0.99), "max_ms": max(values) if values else None}


def load_lines(path):
    return [json.loads(line) for line in path.read_text(encoding="utf-8-sig").splitlines() if line.strip()]


def utc(text):
    return datetime.fromisoformat(text.replace("Z", "+00:00")).timestamp()


def analyze(folder):
    if list(folder.glob("eventpipe-*-error.txt")):
        raise ValueError("EventPipe collection failed")
    if (folder / "collector-error.txt").read_text(encoding="utf-8-sig").strip():
        raise ValueError("Collector stderr is not empty")
    text = (folder / "ui-frames.csv").read_text(encoding="utf-8-sig")
    metadata = dict(line[2:].split("=", 1) for line in text.splitlines() if line.startswith("# "))
    if metadata.get("DroppedSamples") != "0":
        raise ValueError("Missing completion footer or dropped UI samples: reject this capture")
    frequency = int(metadata["StopwatchFrequency"])
    frames = [{key: float(value) for key, value in row.items()}
              for row in csv.DictReader(io.StringIO("\n".join(line for line in text.splitlines() if not line.startswith("#"))))]
    if len(frames) < 100:
        raise ValueError("Too few UI frame callbacks")
    activity = load_lines(folder / "activity.jsonl")
    markers = [row for row in activity if "Detail" in row]
    if markers[-1]["Phase"] != "complete":
        raise ValueError("Incomplete workload")
    process = load_lines(folder / "process.jsonl")
    runtime = load_lines(folder / "runtime.jsonl")
    if not process or not runtime:
        raise ValueError("Missing process or runtime measurements")
    counter_names = {row["Payload"]["Payload"]["Name"] for row in runtime}
    if not {"alloc-rate", "gen-2-gc-count"}.issubset(counter_names):
        raise ValueError("Missing allocation or generation-2 GC counters")
    ticks = [row["Ticks"] for row in frames]
    result = {"label": folder.name, "probe": metadata, "ui_samples": len(frames),
              "private_peak_mib": max(row["PrivateBytes"] for row in process) / 2**20,
              "working_peak_mib": max(row["WorkingSet"] for row in process) / 2**20,
              "private_final_mib": process[-1]["PrivateBytes"] / 2**20,
              "working_final_mib": process[-1]["WorkingSet"] / 2**20,
              "cpu_seconds": process[-1]["CpuSeconds"] - process[0]["CpuSeconds"],
              "max_cached_pixel_mib": max(row["CachedBytes"] for row in frames) / 2**20,
              "allocated_gib": sum(row["Payload"]["Payload"].get("Increment", 0) for row in runtime
                                   if row["Payload"]["Payload"]["Name"] == "alloc-rate") / 2**30,
              "gen2": sum(row["Payload"]["Payload"].get("Increment", 0) for row in runtime
                          if row["Payload"]["Payload"]["Name"] == "gen-2-gc-count"),
              "phases": {}, "latency": {}, "latency_by_phase": {}}
    for start, end in zip(markers, markers[1:]):
        selected = [row for row in frames if start["Ticks"] <= row["Ticks"] < end["Ticks"]]
        intervals = [(b["Ticks"] - a["Ticks"]) * 1000 / frequency for a, b in zip(selected, selected[1:])]
        observed = [row for row in process if utc(start["Utc"]) <= utc(row["Utc"]) < utc(end["Utc"])]
        phase = {"ui_callbacks": len(selected), "gaps": distribution(intervals),
                 "gaps_over_33_3_ms": sum(value > 1000 / 30 for value in intervals),
                 "gaps_over_50_ms": sum(value > 50 for value in intervals),
                 "primary_not_ready_fraction": sum(not row["Ready"] for row in selected) / len(selected) if selected else None}
        if observed:
            phase["private_peak_mib"] = max(row["PrivateBytes"] for row in observed) / 2**20
            phase["private_end_mib"] = observed[-1]["PrivateBytes"] / 2**20
        result["phases"][start["Phase"]] = phase
    actions = [row for row in activity if "Kind" in row and row.get("Phase") in ("smooth-scroll", "fast-scroll", "navigation", "zoom-response")]
    groups = [(kind, None) for kind in ("scroll", "navigate", "zoom")]
    groups += [("scroll", "smooth-scroll"), ("scroll", "fast-scroll")]
    for kind, phase_name in groups:
        requests = [row for row in actions if row["Kind"] == kind and
                    (phase_name is None or row["Phase"] == phase_name)]
        response, ready_response, command = [], [], []
        missing_response = missing_ready = 0
        for request in requests:
            command.append((request["ReturnedTicks"] - request["Ticks"]) * 1000 / frequency)
            following = next((row["Ticks"] for row in actions if row["Ticks"] > request["Ticks"]), request["Ticks"] + 2 * frequency)
            horizon = min(following, request["Ticks"] + 2 * frequency)
            begin = bisect.bisect_left(ticks, request["Ticks"])
            candidates = frames[begin:bisect.bisect_left(ticks, horizon)]
            if kind == "scroll":
                matching = [row for row in candidates if row["Extent"] > row["Viewport"] and
                            abs(100 * row["Offset"] / (row["Extent"] - row["Viewport"]) - request["Target"]) <= 0.025]
            elif kind == "navigate":
                matching = [row for row in candidates if row["Page"] == request["Target"]]
            else:
                matching = [row for row in candidates if abs(row["Zoom"] - request["Target"]) < 0.001]
            if matching:
                response.append((matching[0]["Ticks"] - request["Ticks"]) * 1000 / frequency)
            else:
                missing_response += 1
            ready = [row for row in matching if row["Ready"]]
            if ready:
                ready_response.append((ready[0]["Ticks"] - request["Ticks"]) * 1000 / frequency)
            else:
                missing_ready += 1
        result["latency" if phase_name is None else "latency_by_phase"][kind if phase_name is None else phase_name] = {
            "requests": len(requests), "command_return": distribution(command),
            "first_matching_ui_callback": distribution(response), "missing_response": missing_response,
            "first_matching_ready_callback": distribution(ready_response), "missing_ready": missing_ready}
    (folder / "experience-summary.json").write_text(json.dumps(result, indent=2), encoding="utf-8")
    return result


if __name__ == "__main__":
    summary = analyze(Path(sys.argv[1]))
    print(json.dumps(summary, indent=2))
