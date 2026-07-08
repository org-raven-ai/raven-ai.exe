# Repo metrics pipeline

The charts in the README are produced by an automated pipeline
(`.github/workflows/repo-metrics.yml`) that runs on every push to `main`
(plus `workflow_dispatch` for manual runs). Nothing generated is ever
committed to `main`; all data and images live on the orphan **`metrics`**
branch and the README embeds the SVGs by raw URL.

## What each run does

1. Counts **lines of code** (total and per language) with [`scc`](https://github.com/boyter/scc),
   **commits** with `git rev-list --count HEAD`, and fetches every merged PR's
   merge date from the GitHub API.
2. Appends one snapshot (`date`, `sha`, `loc`, `loc_by_language`, `commits`,
   `merged_prs`) to `metrics.json` on the `metrics` branch — append-only,
   deduplicated by `sha` so reruns never double-record.
3. Recomputes `history.json` in full from git history: commits per day, code
   churn (lines added/removed) per day, and merged PRs per ISO week. These are
   accurate for all history on every run, independent of the snapshot series.
4. Renders six static SVGs from that data with matplotlib (pinned in
   `scripts/metrics/requirements.txt`; rendering is byte-deterministic so
   unchanged data produces no commit).
5. Commits and pushes everything back to `metrics` in a single commit.

## Concurrency

Rapid successive merges would race on the read-modify-write of
`metrics.json`, so the workflow is guarded twice: a `concurrency` group with
`cancel-in-progress: false` serializes runs without dropping any, and the
push itself retries on rejection (fetch, reset to the remote branch, reapply
the append, re-render).

## Scripts (`scripts/metrics/`)

| Script | Role |
|---|---|
| `append-snapshot.py` | appends one snapshot to `metrics.json` (dedupes by `sha`) |
| `compute-history.py` | derives per-day/per-week series from git log + PR merge dates |
| `render-metrics.py` | renders the six SVGs; `--preview-png` for local inspection |
| `backfill-metrics.py` | optional one-off seeder that walks past history; run locally, never in CI |

## Charts

| SVG on `metrics` | Shows |
|---|---|
| `metrics-loc.svg` | total lines of code over time |
| `metrics-loc-by-language.svg` | LOC stacked by language (top 5 + Other) |
| `metrics-activity.svg` | cumulative commits and merged PRs |
| `metrics-commits-per-day.svg` | commits per calendar day |
| `metrics-churn-per-day.svg` | lines added/removed per day |
| `metrics-prs-per-week.svg` | merged PRs per ISO week |

GitHub caches raw assets via Camo, so an embedded chart can lag the latest
push by a few minutes.
