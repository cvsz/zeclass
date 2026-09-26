# eClass User Guide: extracted feature set

Source: `D:\eclass\EClass_ExtractedMSI\disk1\Newusersmanual.pdf` (31 pages, "eClass User Guide",
© Tacteasy). Text was extracted with a local PDF stream decoder; a few icon captions rendered as
garbled glyphs in the PDF font, so those entries are marked as inferred from context.

This is the **vendor's own documentation of intended board behavior**. It is a far better
requirements source than reverse engineering, and it is what the remake is measured against. The
manual states the English version is copyrighted by Tacteasy; treat it as a specification
reference, not as text to copy into the product.

## Hardware model (the USB story, section by section)

- The board is a **USB HID touch device** on a dedicated touch cable, separate from the video
  cable. "Make sure the Touch-Board is plugged in with USB cable."
- **A separate service process owns the hardware connection.** Manual 2.2.6-2.2.7: connect the
  IWB USB cable, then "After service Program is connected, will display on the right-down corner
  of PC. Right-click 'TouchServer' icon and choose 'Calibrate'". So `TouchServer.exe` is the
  board connection and calibration daemon, and its tray icon is the connection status
  indicator. This explains the hidden `TE_SERVER` / `te_boardser` windows observed earlier.
- **Calibration is 4-point.** 2.2.8: touch four crosses one by one, holding each about one
  second. Calibration parameters persist across reinstalls (9.3), so they are stored on disk.
- **Connection state is user-visible.** 3.2: the tray icon distinguishes "connection
  interrupted" from "connected". FAQ (2) lists the causes: bad USB connection, missing driver,
  improper USB ports, or the writing software not running.
- **The board has its own status LED**: FAQ (5), blue when writing means normal.
- **The board can launch the software.** 2.2.9 and 3.4: after calibration you can either
  double-click `eClass` **or just rest your palm on the board** and the app opens by itself.
- Non-transparent objects all work as pointers (marker, finger, opaque object). 3.7.

## Gesture recognition (3.6)

| Gesture | Behavior | Detection rule from the manual |
|---|---|---|
| Rotation | Rotate a selected object | Two fingers on the object, diagonal, horizontal separation under 2 cm and vertical over 2 cm distinguishes rotation from scale |
| Rotation (pivot) | Rotate around a point | One finger holds a corner, the other sweeps diagonally clockwise |
| Fist-hold erase | Fist held about 1.5 s shows a green mark, then acts as eraser | Fist dwell, not swipe |
| Hand-wave page turn | Wave at horizon level, slightly fast, pages up/down | Horizontal fast swipe at board mid-height |
| Palm launch | Palm on board about 1 s opens the software | Palm dwell |
| Fist erase | Fist erases board contents | 3.8.3 |

## Writing tools (4.3)

- **Pen**: pen type, color, line type, line size. A special "filling" pen reveals a hidden
  background picture. A **recognition pen** that "can only recognize the circle and square": draw
  a circle and it becomes a **spotlight**, draw a square and it becomes a **magnifier** (needs
  about 90 degree corners or the magnifier fails). A 4-color multi pen (blue, green, red, brown).
- **Line**: line type, color, size.
- **Geometry**: geometric shapes.
- **Filling**: the background-reveal pen above.
- **Eraser**: point eraser and object eraser (3.8.1/3.8.2); "erasing functions are based on a
  calculated circle around a single point".
- **Text**: click anywhere, type via keyboard (4.3.6).
- **Select**: reveals delete / copy / cut for the selection (4.3.7).
- **Right click**: context menu (4.3.8).
- **Contents roam**: pan in / pan out / reset (4.3.9).
- **Multi-writing**: "as long as you use 4 pens, you can write by four pens at the same time" --
  the board supports **4 simultaneous writers** (3.1.1, 3.5).

## Pages (4.1, 4.2)

- New page, next/previous page, and a **page contents preview** (thumbnail strip).
- **Undo and redo** (4.1.4).
- Per-page management via the preview: **delete, copy, and new-page-settings** (4.2.2-4.2.5).
- **Page settings**: page size, background color **and background image**, page switching mode.
- **Page preview lock/unlock**: keep a page pinned visible or hidden (4.2.7).
- Locked pages shield content rather than deleting it (FAQ 4).

## Tools and resources (5)

Function list (5.2.1) -- these map almost one-to-one onto the `TOOLS\` executables observed in the
package:

| Tool | Manual wording | Vendor binary |
|---|---|---|
| Magnifier | magnifier function | `InZoom.exe` / `Zoom.exe` / `Zoom.dll` |
| Video player | play video, capture video image from the player | `VideoPlayer.exe` / `Player.dll` |
| Capture | select-and-capture / capture without window frame / capture whole screen | capture family |
| Play back | play back the page contents | stroke playback |
| Clock | simulated / digital / counting / countdown clock | `Clock.exe` / `Clock.dll` |
| Screen record | record the teaching operation, replay after class | `RecordScr.exe` |
| Spotlight | spotlight function | `ScreenHighLight.exe` |
| Audio record | record any audio | audio record |
| Formula | all kinds of math formula figures | `TOOLS\physical` WPF tools |
| Screen curtain | hide the answer | `DrawCurtain.exe` |
| Log in status | online/offline check | `config.ini [Login]` |
| Exit | exit the network function | -- |

Disciplinary tools (5.2.2): **Calculator, Ruler, Compass, Set squares, Protractor, Sheet** (table
insert). Note this is a *different* list from the packaged `TOOLS\physical` (physics, electricity,
chemistry content) -- the manual and the shipped build have drifted apart.

Library (5.1): **Local Resources** (browse PC images) and **My Resources** (built-in content
library), drag or tap to attach an image to a page.

## Save, share, language (8)

- New page, **Open file** (video, Word, PPT, PDF), **Insert** picture, **Save** (eClass format),
  **Save as** (Microsoft format), **Auto save** (periodic), **Language** (**20 languages**),
  Quit, **Email** (send the board as a package), **Print**.

## Tablet and classroom integration (5.2.3, 6)

Teacher-side PC functions: **Main Control** (who is online), **Exam**, **Exam History**, **File**,
**Message Center**, **Lock/Unlock student tablets**, **Syncboard** (push board contents to
students), **Look up board** (view a student's tablet), **Question**, **Sent file**, plus shortcut
keys for Main Control and Syncboard. Section 6 covers interacting with the board via tablets.

This confirms `TouchServer.exe` is the **student-side session component** of that pairing, and
that it is the piece that cannot be reproduced without a live teacher console.

## Transparent writing (7)

Listed as a top-level feature with no body text in the extracted text -- the section is
illustrated. Inferred from `config.ini` and the packaged files to be the semi-transparent
desktop-overlay writing mode, where you write on top of running software rather than on a white
page. Verify against a screenshot before implementing.

## System requirements (2.1) -- partially garbled, but decodable facts

- Supported OS per the manual: **Windows 7 / 8 / 10**. **Windows 11 is not listed**, which
  confirms the Win11 compatibility work was necessary rather than premature.
- CPU: an i3-class part or better; RAM 2 GB or more; the OS drive needs 20 GB free; a projector
  is **recommended** (this manual covers IR whiteboards used with a short-throw projector, which
  is why the FAQ discusses trapezoid and keystone calibration).

## Feature gap: zEClass (the remake) vs. the manual

| Manual feature | zEClass status |
|---|---|
| Pen with color, type, size | done (color, size, solid/dashed/dotted) |
| Line, geometry, filling pen | done (line, rect, ellipse, triangle, arrow, star, outline/filled) |
| Point eraser, object eraser | done (radius eraser plus selection delete) |
| Text tool | done (click to place, keyboard entry, caret) |
| Select, delete, copy, cut | done (rectangle and lasso, Delete, Ctrl+C/X/V) |
| Right-click context menu | done |
| Contents roam (pan/zoom) | done (Pan tool, zoom buttons, Fit) |
| Page new/next/prev/preview | done (thumbnail strip with live previews) |
| Undo **and redo** | done (Ctrl+Z, Ctrl+Y, Ctrl+Shift+Z) |
| Page delete, copy, settings | done (strip context menu) |
| Page lock (shield content) | done |
| Background color and image | done (page settings; an image behind a locked page is the fill pen) |
| 4 simultaneous writers | partial (per-contact ids; untested on real hardware) |
| Gestures: fist-erase, wave page turn, palm launch | done (dwell and swipe recognition) |
| 4-point calibration | done (projective fit, per-board persistence, error reporting) |
| Magnifier, capture, clock, spotlight, curtain | done |
| Calculator, ruler, compass, set square, protractor, sheet | done |
| On-screen keyboard | done |
| Insert image, open external formats | partial (images yes; PDF/Word/PPT import not done) |
| 20 languages | not done (UI is English only) |
| Email, print | partial (PDF and PNG export done; print and email not done) |
| Tablet sync, exam, lock, main control | out of scope (vendor protocol) |

## Remaining work, in priority order

1. **Hardware verification on a real panel.** Calibration, pen pressure, multi-touch, and the
   gestures have not been exercised against real digitizer hardware. This is the largest
   remaining risk, not code volume.
2. **PDF/Word/PPT import.** The manual's "open file" covers these. Rendering to an image on a
   background worker is the approach; no office automation, which would not survive a locked-down
   school image.
3. **Localization.** The manual claims 20 languages. A resource-based scheme is needed.
4. **Print and email export.**
5. **Video player and media tools.** The packaged build leans on DirectShow-era components that
   will not work on a current Windows image; these need a modern replacement.
6. **Screen and audio recorders.** Implementable with WPF media APIs, but they need a policy for
   where student-recorded material is stored.
7. **Recognition pen** (circle to spotlight, square to magnifier). A geometry classifier plus
   shape tolerance; the manual's own note that corners must be about 90 degrees is a hint that
   the vendor implementation is naive here too.
