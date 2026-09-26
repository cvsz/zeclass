# zEClass: production roadmap

What a classroom-ready whiteboard needs, in build order. Status reflects the code as it stands.
**389 tests total: 388 passing, 1 conditional skip**, release build clean, self-contained x64 publish verified, installer verified.

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
| Shape set with fill styles | done |
| Highlighter | done |
| Right-click context menu (manual 4.3.8) | done |
| On-screen keyboard (replaces `myosk.exe`) | done |
| 4 simultaneous writers, verified on hardware | partial: per-contact ids implemented, untested on a real panel |

## Phase 3: gestures (manual 3.6)

| Item | Detection rule | Status |
|---|---|---|
| Fist-hold erase | dwell ≈ 1.5 s | done |
| Palm launch | palm dwell ≈ 1 s | done |
| Hand-wave page turn | fast horizontal swipe at mid-height | done |
| Two-finger pinch scale | contacts separating, via the pointer frame API | done |
| Two-finger pan | contacts translating | done |
| Two-finger twist | diagonal, horizontal < 2 cm and vertical > 2 cm | done |
| Recognition pen (circle → spotlight, square → magnifier) | enclosed-area-to-bounding-box ratio | done |

## Phase 4: annotation tools (manual 5.2.1)

| Tool | Status |
|---|---|
| Magnifier | done |
| Spotlight | done |
| Screen curtain | done |
| Clock (digital / simulated / counting / countdown) | done |
| Capture (full screen, to page) | done |
| Playback of recorded strokes, with speed and scrubbing | done |
| Screen recorder (self-contained uncompressed AVI muxer) | done |
| Audio recorder | done, MCI-based WAV through winmm.dll, no codec or library to install |
| Video player | not done: the packaged build's DirectShow-era dependencies will not run on a current image |
| Formula library | not done |

## Phase 5: geometry tools (manual 5.2.2)

Calculator, ruler, compass, set square, protractor, table sheet: all done.

## Phase 6: content and interoperability

| Item | Status |
|---|---|
| Insert image from disk | done |
| Insert screenshot | done |
| Import PDF (embedded page images) | done, with vector-only PDFs reported clearly |
| Import PPTX / DOCX (embedded preview) | done, preview only, not a reflow |
| Export PDF (one page per board page) | done |
| Export PNG per page | done |
| Print | done, via the shell print verb on a staged PDF |
| Email the board as a package | done, zips the board and opens a draft |
| Local resources and built-in library browser | not done |
| Localization | done: 24 languages, per-key English fallback, native names, RTL mirroring for ar/he, and a build-time audit that fails on missing keys, dropped `{0}` placeholders, or an untranslated copy of English. See `LOCALIZATION.md` |
| 20 languages | interface strings covered; help text and the acceptance prompts are still English-only, and no catalogue has native-speaker review |

## Phase 7: production hardening

| Item | Status |
|---|---|
| Self-contained publish, x64 / ARM64 / x86 | done, script plus per-runtime checksums |
| Per-user installer with a digitizer precheck | done, no elevation required |
| Code signing | hook in place, needs a certificate |
| Rotating crash log (2 MB, 3 generations) | done, with exception-chain flattening |
| Diagnostics export for support | done |
| Windows 11 manifest, PerMonitorV2 DPI | done |
| Performance budgets covered by tests | done |
| 10k strokes / 200 pages stress | done, covered by tests |
| Soak tests (history bounds, round-trip stability, erase ordering) | done, tagged `Category=Soak` |
| Three themes including a high-contrast one | done, default follows the OS accessibility setting |
| Keyboard focus states and automation names on controls | done |
| Telemetry | none, and must stay none |

## Phase 8: verification

| Item | Status |
|---|---|
| Unit tests per area | done, 389 total (388 passing, 1 conditional vendor-manual skip) |
| Real hardware test matrix (IR board, capacitive panel, pen, mouse) | **not done: highest remaining risk** |
| Keyboard-only walkthrough | not done |
| Screen-reader pass | not done |

## Explicitly out of scope

- Teacher-to-student control protocol: undocumented and vendor-paired. The local black-box
  lab notes are intentionally not published with this repository.
- `.TY` board format import: undocumented; reading it would require reverse engineering.
- Student tablet sync, exams, lock/unlock.
- Reproducing the vendor hook DLLs. Documented Windows APIs only.
