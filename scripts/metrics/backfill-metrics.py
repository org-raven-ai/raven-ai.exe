#!/usr/bin/env python3
"""One-off backfill: seed metrics.json from the repository's past history.

NOT part of the recurring workflow. Run it locally once (before the first
workflow run, or merge its output into an existing metrics.json by hand),
then commit the result to the `metrics` branch yourself.

Usage: backfill-metrics.py [--branch main] [--repo .] [--output metrics.json]

Walks the first-parent history of the branch (one commit per merged PR under
squash/merge-commit workflows) and records, per commit:

  date       -- committer date, UTC ISO-8601
  sha        -- short sha
  loc        -- total lines of code at that commit, counted with scc
  commits    -- number of commits on the branch up to and including it
  merged_prs -- APPROXIMATION: commits whose subject ends with "(#N)", the
                marker GitHub adds to squash/merge commits. The recurring
                workflow instead asks the API for the true count, so expect a
                small step between the last backfilled entry and the first
                live one.

Requires: git, and scc on PATH (https://github.com/boyter/scc), Python 3.12+.
"""

import argparse
import io
import json
import re
import subprocess
import sys
import tarfile
import tempfile
from datetime import datetime, timezone
from pathlib import Path

MERGE_SUBJECT = re.compile(r"\(#\d+\)$")


def git(*args: str, repo: str) -> bytes:
    return subprocess.run(
        ["git", "-C", repo, *args], check=True, capture_output=True
    ).stdout


def count_loc(repo: str, sha: str) -> int:
    tar_bytes = git("archive", "--format=tar", sha, repo=repo)
    with tempfile.TemporaryDirectory() as tree_dir:
        with tarfile.open(fileobj=io.BytesIO(tar_bytes)) as tar:
            tar.extractall(tree_dir, filter="data")
        scc = subprocess.run(
            ["scc", "--format", "json", tree_dir], check=True, capture_output=True
        )
    return sum(language["Code"] for language in json.loads(scc.stdout))


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--branch", default="main")
    parser.add_argument("--repo", default=".")
    parser.add_argument("--output", default="metrics.json", type=Path)
    args = parser.parse_args()

    shas = (
        git("rev-list", "--first-parent", "--reverse", args.branch, repo=args.repo)
        .decode()
        .split()
    )
    entries = []
    merged_prs = 0
    for position, sha in enumerate(shas, start=1):
        subject = git("show", "--no-patch", "--format=%s", sha, repo=args.repo).decode().strip()
        if MERGE_SUBJECT.search(subject):
            merged_prs += 1
        committer_date = git("show", "--no-patch", "--format=%cI", sha, repo=args.repo).decode().strip()
        date = (
            datetime.fromisoformat(committer_date)
            .astimezone(timezone.utc)
            .strftime("%Y-%m-%dT%H:%M:%SZ")
        )
        short_sha = git("rev-parse", "--short", sha, repo=args.repo).decode().strip()
        entries.append(
            {
                "date": date,
                "sha": short_sha,
                "loc": count_loc(args.repo, sha),
                "commits": position,
                "merged_prs": merged_prs,
            }
        )
        print(f"[{position}/{len(shas)}] {short_sha} {date} loc={entries[-1]['loc']}", file=sys.stderr)

    args.output.write_text(json.dumps(entries, indent=2) + "\n", encoding="utf-8")
    print(f"Wrote {len(entries)} entries to {args.output}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
