#!/usr/bin/env python3
"""Compute history-derived metrics from git history and merged-PR dates.

Usage: compute-history.py <merged-pr-dates.json> <output-history.json> [--repo <path>]

Unlike the snapshot series in metrics.json (append-only, grows from the first
run), everything here is recomputed in full from the repository's history on
every run, so it is accurate for all time including history that predates the
pipeline. Output shape:

  {
    "commits_per_day":     {"2026-05-02": 3, ...},
    "churn_per_day":       {"2026-05-02": {"added": 120, "removed": 30}, ...},
    "merged_prs_per_week": {"2026-W19": 2, ...}
  }

<merged-pr-dates.json> is a JSON array of ISO-8601 merge timestamps (produced
in CI via the GitHub API); weeks are ISO weeks. Day bucketing uses committer
dates.
"""

import argparse
import json
import re
import subprocess
import sys
from datetime import datetime
from pathlib import Path

COMMIT_MARK = "@"
DAY_LINE = re.compile(r"^@(\d{4}-\d{2}-\d{2})$")
NUMSTAT_LINE = re.compile(r"^(\d+|-)\t(\d+|-)\t")


def git_log(repo: str, *args: str) -> list[str]:
    result = subprocess.run(
        ["git", "-C", repo, "log", *args],
        check=True,
        capture_output=True,
        text=True,
        encoding="utf-8",
        errors="replace",
    )
    return result.stdout.splitlines()


def commits_per_day(repo: str) -> dict[str, int]:
    days: dict[str, int] = {}
    for line in git_log(repo, "--pretty=format:%cs"):
        if line:
            days[line] = days.get(line, 0) + 1
    return dict(sorted(days.items()))


def churn_per_day(repo: str) -> dict[str, dict[str, int]]:
    days: dict[str, dict[str, int]] = {}
    current = None
    for line in git_log(repo, "--numstat", f"--pretty=format:{COMMIT_MARK}%cs"):
        day_match = DAY_LINE.match(line)
        if day_match:
            current = days.setdefault(day_match.group(1), {"added": 0, "removed": 0})
            continue
        stat_match = NUMSTAT_LINE.match(line)
        if stat_match and current is not None:
            added, removed = stat_match.group(1), stat_match.group(2)
            if added != "-":  # "-" marks binary files
                current["added"] += int(added)
            if removed != "-":
                current["removed"] += int(removed)
    return dict(sorted(days.items()))


def merged_prs_per_week(merge_dates: list[str]) -> dict[str, int]:
    weeks: dict[str, int] = {}
    for stamp in merge_dates:
        iso_year, iso_week, _ = datetime.fromisoformat(stamp).isocalendar()
        key = f"{iso_year}-W{iso_week:02d}"
        weeks[key] = weeks.get(key, 0) + 1
    return dict(sorted(weeks.items()))


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("merged_pr_dates", type=Path)
    parser.add_argument("output", type=Path)
    parser.add_argument("--repo", default=".")
    args = parser.parse_args()

    merge_dates = json.loads(args.merged_pr_dates.read_text(encoding="utf-8"))
    history = {
        "commits_per_day": commits_per_day(args.repo),
        "churn_per_day": churn_per_day(args.repo),
        "merged_prs_per_week": merged_prs_per_week(merge_dates),
    }
    args.output.write_text(
        json.dumps(history, indent=2, sort_keys=True) + "\n", encoding="utf-8"
    )
    print(
        f"Wrote {args.output}: {len(history['commits_per_day'])} days of commits, "
        f"{len(history['churn_per_day'])} days of churn, "
        f"{len(history['merged_prs_per_week'])} weeks of merged PRs."
    )
    return 0


if __name__ == "__main__":
    sys.exit(main())
