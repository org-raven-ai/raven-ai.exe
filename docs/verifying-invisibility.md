# Verifying the invisibility (the whole point)

1. Launch raven_ai — the window appears near the top-right, on top of everything.
2. Trigger any capture:
   - Press `Win+Shift+S` (Snip & Sketch), or
   - Start a Zoom / Meet / Teams **screen share**, or
   - Use OBS **Display Capture**, or
   - Press `Win+G` (Game Bar) and record.
3. In the capture, the raven_ai window is **absent** — whatever is *behind* it shows through —
   while it stays fully visible on your real monitor.
4. The title-bar status dot / banner reflects `GetWindowDisplayAffinity`; a red banner means
   protection is not fully active.
