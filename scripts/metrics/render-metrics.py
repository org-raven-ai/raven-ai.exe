#!/usr/bin/env python3
"""Render repository metric graphs as static SVG.

Usage: render-metrics.py <metrics.json> <history.json> <output-dir> [--preview-png]

Reads the accumulated snapshot series (metrics.json) and the history-derived
metrics (history.json, see compute-history.py) and writes self-contained
static SVGs suitable for embedding in a README:

  metrics-loc.svg              -- lines of code over time
  metrics-activity.svg         -- commits and merged PRs over time
  metrics-loc-by-language.svg  -- donut of the current LOC per language (needs
                                  snapshots with loc_by_language; skipped until
                                  one exists)
  metrics-commits-per-day.svg  -- commits per calendar day
  metrics-churn-per-day.svg    -- lines added/removed per day
  metrics-prs-per-week.svg     -- merged PRs per ISO week

--preview-png additionally writes a .png beside each .svg for local
inspection only; CI never passes it.

The SVG output is deterministic (fixed hash salt, no embedded creation date),
so re-rendering unchanged data yields byte-identical files and the workflow
can detect no-op runs via `git diff`.
"""

import json
import sys
from datetime import date, datetime
from pathlib import Path

import matplotlib

matplotlib.use("Agg")
import matplotlib.dates as mdates
import matplotlib.pyplot as plt
from matplotlib.patches import Patch
from matplotlib.ticker import FuncFormatter, MaxNLocator

# Chart chrome and series colors (light surface; the SVG renders identically
# on GitHub's light and dark themes as a light "card").
SURFACE = "#fcfcfb"
PRIMARY_INK = "#0b0b0b"
SECONDARY_INK = "#52514e"
MUTED = "#898781"
GRIDLINE = "#e1e0d9"
BASELINE = "#c3c2b7"
# Categorical slots in the palette's fixed, CVD-validated order. Color follows
# the entity across charts: commits are always blue, merged PRs always aqua.
CATEGORICAL = ["#2a78d6", "#1baf7a", "#eda100", "#008300", "#4a3aa7"]
BLUE, AQUA = CATEGORICAL[0], CATEGORICAL[1]
DIVERGING_RED = "#e34948"  # paired with blue for added/removed polarity
OTHER_GRAY = "#898781"

MAX_LANGUAGE_SLOTS = 5  # languages beyond this fold into "Other", never a 6th hue

plt.rcParams.update(
    {
        "svg.hashsalt": "repo-metrics",  # deterministic SVG element ids
        "font.family": "sans-serif",
        "font.size": 10,
        "axes.titlesize": 12,
    }
)


def new_axes(title: str):
    fig, ax = plt.subplots(figsize=(8, 3.2), dpi=100)
    fig.set_facecolor(SURFACE)
    ax.set_facecolor(SURFACE)
    for side in ("top", "right", "left"):
        ax.spines[side].set_visible(False)
    ax.spines["bottom"].set_color(BASELINE)
    ax.grid(axis="y", color=GRIDLINE, linewidth=0.8)
    ax.set_axisbelow(True)
    ax.tick_params(colors=MUTED, labelsize=9, length=0, pad=6)
    ax.set_title(title, loc="left", color=PRIMARY_INK, fontweight="bold", pad=12)
    locator = mdates.AutoDateLocator()
    ax.xaxis.set_major_locator(locator)
    ax.xaxis.set_major_formatter(mdates.ConciseDateFormatter(locator))
    ax.yaxis.set_major_locator(MaxNLocator(5, integer=True))
    ax.yaxis.set_major_formatter(FuncFormatter(lambda v, _: f"{abs(int(v)):,}"))
    return fig, ax


def add_legend(ax, labels, handles):
    # Anchored above the axes (title sits left, legend right) so it can never
    # collide with the data, wherever the tall bars or lines end up.
    legend = ax.legend(
        handles,
        labels,
        loc="lower right",
        bbox_to_anchor=(1, 1),
        ncols=min(len(labels), 3),  # wide legends wrap so they clear the title
        frameon=False,
        fontsize=9,
        labelcolor=SECONDARY_INK,
        handlelength=1.2,
        borderaxespad=0.2,
    )
    for line in legend.get_lines():
        line.set_linewidth(2)


def day_axis(ax, dates):
    # AutoDateLocator drops to 12-hourly ticks on short ranges, which reads
    # oddly for day-bucketed data; force whole-day ticks there.
    if (dates[-1] - dates[0]).days <= 14:
        ax.xaxis.set_major_locator(mdates.DayLocator())
        ax.xaxis.set_major_formatter(mdates.DateFormatter("%b %d"))


def plot_series(ax, dates, values, color):
    (line,) = ax.plot(
        dates,
        values,
        color=color,
        linewidth=2,
        marker="o",
        markersize=5,
        markeredgecolor=SURFACE,  # surface ring so dense points stay legible
        markeredgewidth=1,
        clip_on=False,
    )
    return line


def label_line_end(ax, dates, values, text):
    ax.annotate(
        text,
        (dates[-1], values[-1]),
        xytext=(10, 0),
        textcoords="offset points",
        va="center",
        color=SECONDARY_INK,
        fontsize=9,
        annotation_clip=False,
    )


def save(fig, path: Path, preview_png: bool):
    fig.savefig(
        path,
        format="svg",
        metadata={"Date": None},  # no timestamp -> byte-identical re-renders
        bbox_inches="tight",
        pad_inches=0.2,
        facecolor=SURFACE,
    )
    if preview_png:
        fig.savefig(path.with_suffix(".png"), bbox_inches="tight", pad_inches=0.2, facecolor=SURFACE)
    plt.close(fig)
    print(f"Wrote {path}")


def render_snapshot_charts(series, out_dir: Path, preview_png: bool):
    series = sorted(series, key=lambda entry: entry["date"])
    dates = [datetime.fromisoformat(entry["date"]) for entry in series]
    loc = [entry["loc"] for entry in series]
    commits = [entry["commits"] for entry in series]
    merged_prs = [entry["merged_prs"] for entry in series]

    # Lines of code over time: single series, so the title names it (no legend).
    fig, ax = new_axes("Lines of code")
    plot_series(ax, dates, loc, BLUE)
    label_line_end(ax, dates, loc, f"{loc[-1]:,}")
    ax.set_ylim(bottom=0)
    day_axis(ax, dates)
    save(fig, out_dir / "metrics-loc.svg", preview_png)

    # Commits and merged PRs share one count axis (never a dual-axis chart).
    fig, ax = new_axes("Commits and merged PRs")
    commit_line = plot_series(ax, dates, commits, BLUE)
    pr_line = plot_series(ax, dates, merged_prs, AQUA)
    label_line_end(ax, dates, commits, f"commits · {commits[-1]:,}")
    label_line_end(ax, dates, merged_prs, f"merged PRs · {merged_prs[-1]:,}")
    ax.set_ylim(bottom=0)
    day_axis(ax, dates)
    add_legend(ax, ["Commits", "Merged PRs"], [commit_line, pr_line])
    save(fig, out_dir / "metrics-activity.svg", preview_png)

    render_loc_by_language(series, out_dir, preview_png)


def render_loc_by_language(series, out_dir: Path, preview_png: bool):
    """Donut of the LATEST snapshot's per-language LOC, every count stated."""
    with_languages = [entry for entry in series if entry.get("loc_by_language")]
    if not with_languages:
        print("No snapshots carry loc_by_language yet; skipping the language chart.")
        return

    newest = with_languages[-1]
    breakdown = newest["loc_by_language"]
    ranked = sorted(breakdown, key=breakdown.get, reverse=True)
    top = ranked[:MAX_LANGUAGE_SLOTS]
    labels = list(top)
    values = [breakdown[language] for language in top]
    colors = CATEGORICAL[: len(top)]
    other = sum(breakdown[language] for language in ranked[MAX_LANGUAGE_SLOTS:])
    if other:
        labels.append("Other")
        values.append(other)
        colors.append(OTHER_GRAY)
    total = sum(values)

    fig, ax = plt.subplots(figsize=(8, 3.6), dpi=100)
    fig.set_facecolor(SURFACE)
    ax.set_facecolor(SURFACE)
    ax.set_title(
        "Lines of code by language", loc="left", color=PRIMARY_INK, fontweight="bold", pad=12
    )
    wedges, _ = ax.pie(
        values,
        colors=colors,
        startangle=90,  # largest slice starts at 12 o'clock,
        counterclock=False,  # reading clockwise in size order
        wedgeprops={"width": 0.38, "edgecolor": SURFACE, "linewidth": 2},
    )
    ax.text(
        0, 0.10, f"{total:,}", ha="center", va="center",
        color=PRIMARY_INK, fontsize=17, fontweight="bold",
    )
    ax.text(0, -0.16, "lines of code", ha="center", va="center", color=MUTED, fontsize=9)
    # Every slice's exact count lives in the list beside the donut, so no
    # label ever collides on thin slices.
    ax.legend(
        wedges,
        [
            f"{label} — {value:,} ({value / total:.1%})"
            for label, value in zip(labels, values)
        ],
        loc="center left",
        bbox_to_anchor=(1.02, 0.5),
        frameon=False,
        fontsize=10,
        labelcolor=SECONDARY_INK,
        handlelength=1.0,
    )
    ax.text(
        0, -1.28, f"as of {newest['date'][:10]} · {newest['sha']}",
        ha="center", va="center", color=MUTED, fontsize=8,
    )
    save(fig, out_dir / "metrics-loc-by-language.svg", preview_png)


def render_history_charts(history, out_dir: Path, preview_png: bool):
    per_day = history.get("commits_per_day", {})
    if per_day:
        keys = sorted(per_day)
        days = [datetime.fromisoformat(day) for day in keys]
        fig, ax = new_axes("Commits per day")
        ax.bar(days, [per_day[key] for key in keys], width=0.75, color=BLUE)
        day_axis(ax, days)
        save(fig, out_dir / "metrics-commits-per-day.svg", preview_png)

    churn = history.get("churn_per_day", {})
    if churn:
        keys = sorted(churn)
        days = [datetime.fromisoformat(day) for day in keys]
        added = [churn[key]["added"] for key in keys]
        removed = [-churn[key]["removed"] for key in keys]  # mirrored below the axis
        fig, ax = new_axes("Code churn per day")
        ax.bar(days, added, width=0.75, color=BLUE)
        ax.bar(days, removed, width=0.75, color=DIVERGING_RED)
        ax.axhline(0, color=BASELINE, linewidth=1)
        day_axis(ax, days)
        add_legend(
            ax,
            ["Lines added", "Lines removed"],
            [Patch(color=BLUE), Patch(color=DIVERGING_RED)],
        )
        save(fig, out_dir / "metrics-churn-per-day.svg", preview_png)

    per_week = history.get("merged_prs_per_week", {})
    if per_week:
        # "2026-W23" -> the Monday of that ISO week, for a real date axis.
        mondays = [
            date.fromisocalendar(int(key[:4]), int(key.split("-W")[1]), 1)
            for key in sorted(per_week)
        ]
        fig, ax = new_axes("Merged PRs per week")
        ax.bar(mondays, [per_week[key] for key in sorted(per_week)], width=5, color=AQUA)
        save(fig, out_dir / "metrics-prs-per-week.svg", preview_png)


def main() -> int:
    args = [a for a in sys.argv[1:] if a != "--preview-png"]
    preview_png = "--preview-png" in sys.argv[1:]
    if len(args) != 3:
        print(__doc__, file=sys.stderr)
        return 2

    series = json.loads(Path(args[0]).read_text(encoding="utf-8"))
    history = json.loads(Path(args[1]).read_text(encoding="utf-8"))
    out_dir = Path(args[2])

    if series:
        render_snapshot_charts(series, out_dir, preview_png)
    else:
        print("metrics.json is empty; skipping the snapshot charts.")
    render_history_charts(history, out_dir, preview_png)
    return 0


if __name__ == "__main__":
    sys.exit(main())
