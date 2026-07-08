#!/usr/bin/env python3
"""Render repository metric graphs from metrics.json as static SVG.

Usage: render-metrics.py <metrics.json> <output-dir> [--preview-png]

Reads the full accumulated time series (a JSON array of snapshot objects) and
writes two self-contained static SVGs suitable for embedding in a README:

  metrics-loc.svg      -- lines of code over time
  metrics-activity.svg -- commits and merged PRs over time

--preview-png additionally writes a .png beside each .svg for local
inspection only; CI never passes it.

The SVG output is deterministic (fixed hash salt, no embedded creation date),
so re-rendering unchanged data yields byte-identical files and the workflow
can detect no-op runs via `git diff`.
"""

import json
import sys
from datetime import datetime
from pathlib import Path

import matplotlib

matplotlib.use("Agg")
import matplotlib.dates as mdates
import matplotlib.pyplot as plt
from matplotlib.ticker import FuncFormatter, MaxNLocator

# Chart chrome and series colors (light surface; the SVG renders identically
# on GitHub's light and dark themes as a light "card").
SURFACE = "#fcfcfb"
PRIMARY_INK = "#0b0b0b"
SECONDARY_INK = "#52514e"
MUTED = "#898781"
GRIDLINE = "#e1e0d9"
BASELINE = "#c3c2b7"
BLUE = "#2a78d6"
AQUA = "#1baf7a"

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
    ax.yaxis.set_major_formatter(FuncFormatter(lambda v, _: f"{int(v):,}"))
    return fig, ax


def plot_series(ax, dates, values, color):
    ax.plot(
        dates,
        values,
        color=color,
        linewidth=2,
        marker="o",
        markersize=5,
        markeredgecolor=SURFACE,  # 2px-ish surface ring so dense points stay legible
        markeredgewidth=1,
        clip_on=False,
    )


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


def main() -> int:
    args = [a for a in sys.argv[1:] if a != "--preview-png"]
    preview_png = "--preview-png" in sys.argv[1:]
    if len(args) != 2:
        print(__doc__, file=sys.stderr)
        return 2

    series = json.loads(Path(args[0]).read_text(encoding="utf-8"))
    out_dir = Path(args[1])
    if not series:
        print("metrics.json is empty; nothing to render.")
        return 0

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
    save(fig, out_dir / "metrics-loc.svg", preview_png)

    # Commits and merged PRs share one count axis (never a dual-axis chart).
    fig, ax = new_axes("Commits and merged PRs")
    plot_series(ax, dates, commits, BLUE)
    plot_series(ax, dates, merged_prs, AQUA)
    label_line_end(ax, dates, commits, f"commits · {commits[-1]:,}")
    label_line_end(ax, dates, merged_prs, f"merged PRs · {merged_prs[-1]:,}")
    ax.set_ylim(bottom=0)
    legend = ax.legend(
        ["Commits", "Merged PRs"],
        loc="upper left",
        frameon=False,
        fontsize=9,
        labelcolor=SECONDARY_INK,
        handlelength=1.2,
    )
    for line in legend.get_lines():
        line.set_linewidth(2)
    save(fig, out_dir / "metrics-activity.svg", preview_png)
    return 0


if __name__ == "__main__":
    sys.exit(main())
