# Limitations (read this)

- **Remote Desktop (RDP)** disables DWM composition, which disables the exclusion — the window
  may appear in an RDP session.
- **Bare VMs without GPU acceleration** may not honor the flag.
- **This is not a hard security boundary.** It defeats normal screen-share and recording
  software, but a determined attacker running **kernel-level capture** or reading the
  **GPU framebuffer** on the same machine can still see the window. Do not rely on it to hide
  content from a compromised host.
- Windows builds **below 19041** get the `WDA_MONITOR` black-box fallback, not true invisibility.

## Interactive mode

- The frozen real cursor is **pinned but stays faintly visible** where you left it (we avoid the
  crash-unsafe route of hiding the system cursor globally).
- **ComboBox dropdowns** can't be opened/picked with the fake cursor (the popup is a separate
  visual); editable model boxes still accept typed text.
- Like any `WH_MOUSE_LL` hook, it **won't intercept over elevated windows** unless raven_ai itself
  runs elevated (UIPI), and never on the secure desktop (UAC / lock screen).
- Apps that read **raw input exclusively** (some full-screen games) still receive movement even
  while legacy input is swallowed.
