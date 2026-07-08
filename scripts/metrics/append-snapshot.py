#!/usr/bin/env python3
"""Append a snapshot entry to the accumulated metrics.json time series.

Usage: append-snapshot.py <snapshot.json> <metrics.json>

The snapshot file holds a single JSON object; metrics.json holds a JSON array
of such objects. The array is append-only: existing entries are never
rewritten, reordered, or backfilled. A snapshot whose `sha` is already present
is skipped, so workflow reruns and push retries cannot duplicate a data point.
"""

import json
import sys
from pathlib import Path


def main() -> int:
    if len(sys.argv) != 3:
        print(__doc__, file=sys.stderr)
        return 2

    snapshot_path = Path(sys.argv[1])
    metrics_path = Path(sys.argv[2])

    snapshot = json.loads(snapshot_path.read_text(encoding="utf-8"))

    if metrics_path.exists():
        series = json.loads(metrics_path.read_text(encoding="utf-8"))
        if not isinstance(series, list):
            print(f"{metrics_path} does not contain a JSON array", file=sys.stderr)
            return 1
    else:
        series = []

    if any(entry.get("sha") == snapshot["sha"] for entry in series):
        print(f"Snapshot for {snapshot['sha']} already recorded; skipping append.")
        return 0

    series.append(snapshot)
    metrics_path.write_text(json.dumps(series, indent=2) + "\n", encoding="utf-8")
    print(f"Appended snapshot for {snapshot['sha']} ({len(series)} entries total).")
    return 0


if __name__ == "__main__":
    sys.exit(main())
