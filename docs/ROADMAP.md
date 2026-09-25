# EBoard: production roadmap

What a classroom-ready whiteboard needs, in build order. Status reflects the code as it stands.
124 tests pass, release build clean, self-contained x64 publish verified.

## Phase 1: editing model

| Item | Why it matters | Status |
|---|---|---|
| Command-based edit history | Undo without redo is unusable in a lesson | done |
| Selection (rectangle + lasso) | Delete/copy/cut on ink, per manual 4.3.7 | done |
| Text tool with caret | Per manual 4.3.6 | done |
| Page add/delete/duplicate/reorder | Per manual 4.2.3-4.2.4 | done |
| Per-page background colour and image | Per manual 4.2.5 | done |
| Page preview strip | Per manual 4.1.2 | done |
| Page lock (shields content) | Per manual 4.2.7 | done |
| Pan / zoom | Per manual 4.3.9 | done |

## Phase 2: teaching surface

| Item | Status |
|---|---|
| Line types (solid/dashed/dotted) | done |
| Shape set (rect, ellipse, triangle, arrow, star) with fill styles | done |
| Highlighter blending | done |
| Right-click context menu (manual 4.3.8) | done |
| On-screen keyboard (replaces `myosk.exe`) | done |
| 4 simultaneous writers, verified on hardware | partial: per-contact ids implemented, untested on a real panel |

## Phase 3: gestures (manual 3.6)

| Item | Detection rule | Status |
|---|---|---|
| Fist-hold erase | dwell about 1.5 s | done |
| Palm launch | palm dwell about 1 s | done |
| Hand-wave page turn | fast horizontal swipe at mid-height | done |
| Two-finger rotate | diagonal, horizontal under 2 cm, vertical over 2 cm | not done |
| Two-finger scale/pan | any other two-finger gesture | not done |
| Recognition pen (circle to spotlight, square to magnifier) | geometry classification | not done |

Multi-touch gestures need a second contact path: WPF reports only one touch point per event, so
pinch and rotate need the pointer stack's frame API (`GetPointerFrameInfo`) rather than the
per-pointer messages currently used.

## Phase 4: annotation tools (manual 5.2.1)

| Tool | Status |
|---|---|
| Magnifier | done |
| Spotlight | done |
| Screen curtain | done |
| Clock (digital / simulated / counting / countdown) | done |
| Capture (full screen) | done |
| Playback of recorded pages | not done |
| Screen recorder | not done |
| Audio recorder | not done |
| Video player | not done: the packaged build's DirectShow-era dependencies will not run on a current image |
| Formula library | not done |

## Phase 5: geometry tools (manual 5.2.2)

Calculator, ruler, compass, set square, protractor, and table sheet: all done.

## Phase 6: content and interoperability

| Item | Status |
|---|---|
| Insert image from disk | done |
| Insert screenshot | done |
| Export PDF (one page per board page) | done |
| Export PNG per page | done |
| Open PDF / Word / PPT / video | not done |
| Save as a Microsoft-compatible format | not done |
| Email the board as a package | not done |
| Print | not done |
| Local resources and built-in library browser | not done |
| Localization (manual claims 20 languages) | not done |

## Phase 7: production hardening

| Item | Status |
|---|---|
| Self-contained publish, x64 / ARM64 / x86 | done, script plus per-runtime checksums |
| Code signing | hook in place, needs a certificate |
| Rotating crash log (2 MB, 3 generations) | done |
| Diagnostics export for support | done |
| Windows 11 manifest, PerMonitorV2 DPI | done |
| Startup and per-operation performance budgets | done, covered by tests |
| 10k strokes / 200 pages stress | done, covered by tests |
| No-elevation deployment | done: the app never requires admin |
| Accessibility: keyboard-only operation | partial: shortcuts exist, focus order and screen-reader labelling not reviewed |
| High-contrast theme | not done |
| MSI or installer with a digitizer precheck | not done |
| Telemetry | none, and must stay none |

## Phase 8: verification

| Item | Status |
|---|---|
| Unit tests per area | done, 124 passing |
| Real hardware test matrix (IR board, capacitive panel, pen, mouse) | **not done: highest remaining risk** |
| Long-run soak test across a full school day | not done |
| Keyboard-only walkthrough | not done |

## Explicitly out of scope

- Teacher-to-student control protocol: undocumented and vendor-paired. See
  `..\EClass_Win11\Spec\FINDINGS_Lab.md`.
- `.TY` board format import: undocumented; reading it would require reverse engineering.
- Student tablet sync, exams, lock/unlock.
- Reproducing the vendor hook DLLs. Documented Windows APIs only.
