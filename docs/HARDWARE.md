# USB interactive whiteboard bring-up and troubleshooting

Written for classroom panels: infrared (IR/optical) whiteboards with a USB touch cable, and
capacitive flat panels. Also applies to USB pens used on any Windows 11 machine.

## 1. What the hardware needs from Windows

An interactive whiteboard is a USB HID **digitizer**. Windows sees it as a pointer device, not
as a mouse, and the app talks to it through the pointer stack. For a good experience the
digitizer should report:

| Capability | Effect when present | Effect when absent |
|---|---|---|
| Contact (touch) | Multi-touch ink | none |
| Pressure | Line width varies with force | uniform nominal width |
| In-range (hover) | Pen can be detected before contact, palm rejection is precise | palm rejection only while drawing |
| Eraser tip | Barrel flip erases | use the eraser tool or a finger gesture |
| Palm flag | OS classifies contact as a palm | engine's own pen-active rule applies |

IR whiteboards are usually **contact-only with a physical pen**: the pen is tracked optically,
so pressure and eraser tip do not exist. Those boards still work fully, they just draw uniform
lines. Capacitive panels and active digitizers (Wacom-style) report pressure properly.

## 2. Connection sequence

1. Power the panel. Connect the **touch/USB cable** from the panel to the PC or OPS box. This
   is a separate cable from HDMI/DP; a board that shows a picture but does not draw is almost
   always missing or loose on the touch cable.
2. Wait for Windows to enumerate. Check Device Manager > Human Interface Devices, or
   Settings > Bluetooth & devices > Pen & Windows Ink > Touch and pen > "What Windows can
   see".
3. Start `zEClass.exe` and read the status bar:
   - `Digitizer: USB pen + touch` — pen pressure and touch both available.
   - `Digitizer: USB touch` — touch only, no pen.
   - `Digitizer: none (mouse)` — nothing detected; work through section 4.
4. Press **Diagnostics** in the bottom-right for the enumerated device list with VID/PID.

## 3. Multi-user writing

The engine tracks contacts by pointer id, so several people can write simultaneously and the
strokes stay separate. Two caveats:

- **Palm rejection drops touch while a pen is in contact.** That is deliberate: a hand resting
  on the board must not scribble. It means finger-writing stops while a pen is being used. Set
  `PalmRejectionEnabled = false` on `InkEngine` if a class needs simultaneous pen and finger
  input on the same panel.
- Touch and pen contacts carry the Windows pointer id, which is distinct per finger, so
  multi-finger writing stays separate. The WPF stylus and touch events are consumed without
  drawing (see "Input architecture" in the README), so they cannot duplicate a contact under a
  second id. The Diagnostics panel reports the active input path.

## 4. When nothing is detected

Check in order:

1. **Cable and port.** Try a USB port on the PC itself rather than a hub. A hub that does not
   carry enough current is a common cause. Some OPS boxes have a dedicated touch-USB header.
2. **Device Manager.** Look for a HID device under Human Interface Devices, or an unknown
   device with a warning icon. An unknown device usually means a missing driver.
3. **Touch enabled in Windows.** Settings > Bluetooth & devices > Pen & Windows Ink > Touch and
   pen. Windows can have touch switched off even with hardware present.
4. **Digitizer not flagged as a touch device.** The app reads
   `HKLM\SOFTWARE\Microsoft\TabletTip\1.7\EnableTouch`. Some panels ship with a vendor
   utility that toggles this; run it once after installation.
5. **Pen without pressure.** Some pens need their driver installed before the digitizer reports
   pressure. Until then the board draws uniform lines, which is expected.
6. **Check the log.** `%LOCALAPPDATA%\zEClass\logs\zEClass.log` records whether DPI awareness had
   to be enabled from code and any `WM_POINTER` read failures. Read failures mean pen or touch
   input is being dropped (logged up to three times), which is a defect — report it.

## 5. Calibration (do this first)

A touch panel that is not perfectly parallel to its display, or whose reported coordinate frame
differs from the screen, will draw offset. This is normal on wall-mounted boards and is the
first thing to fix before judging anything else.

1. Start `zEClass.exe` and maximize it.
2. Click **Align** in the left palette. The board goes black and shows a cross.
3. Touch the centre of each cross with the pen or your finger, four times, in the order shown.
   Each hit is marked with a green circle once recorded.
4. The banner reports the worst-case error across the four targets:
   - **under 12 px** — good, press `Enter` to accept.
   - **above 12 px** — accepted with a warning. Ink will be consistently but slightly off.
     Press `Esc` and redo it, hitting closer to each cross centre.
5. If the banner says the calibration *failed*, the four hits were too close together or fell on
   a line, which makes the mapping unsolvable. Nothing was changed; try again.

Calibration is saved automatically and reloaded on the next start. It is stored per board, so
if you swap the panel or change the display resolution, the **Align** button returns to its
uncalibrated state and you should redo it. Confirm the state from the button border: green means
calibrated, plain means not.

Note: with a mouse you can also run a calibration, and it will produce a valid transform for
whatever offset exists, but a mouse reports no true digitizer coordinates, so this only serves
as a way to exercise the flow.

## 6. Performance notes

- Strokes are rebuilt into cached `Drawing` objects and only the in-progress stroke is
  re-rendered each frame, so cost scales with stroke count at page load, not while drawing.
- `MaxStrokePoints` (200,000 per stroke) is a runaway guard. Exceeding it drops further samples
  rather than growing without bound.
- Very large pages (10k+ strokes) will show a pause on page switch because the whole page is
  re-cached. Split lessons across pages or boards.

## 7. Verifying the panel: the acceptance run

Everything above assumes the panel works. That assumption is the largest remaining risk in this
project, so the app ships a guided run that produces evidence instead of an assertion. It can be
exercised anywhere a touch surface is attached — as of 2026-09-30 this development machine has two
(the Dell P2418HT's in-cell touch and an unidentified rear-port controller) — but the classroom
panel's own acceptance pass is still required before release.

Click **Acceptance test** in the status bar. It walks through one capability at a time, shows the
instruction, and records what actually arrives through the real input pipeline — the same
`InkSurface` a teacher draws with, not a parallel test path that could pass while the real one
fails:

| Check | What it proves |
| --- | --- |
| Digitizer enumerated | the panel is visible to Windows, with VID/PID recorded |
| Touch accuracy | touch lands within 40 px of an on-screen target, measured across four hits |
| Simultaneous contacts | how many fingers the panel reports at once |
| Pen pressure | normalized pressure spans at least 0.25 across one stroke |
| Barrel-switch eraser | flipping the pen changes the reported tip |
| Palm rejection | no touch sample is accepted while the pen is in contact |
| Calibration accuracy | worst-case error is within the documented 12 px |
| Pointer to render latency | p95 latency is within 40 ms |

Save the report to `Documents\zEClass` as both Markdown (for a human) and JSON (for a ticket or a
procurement record).

### A run is only honest if it can fail

Two rules are deliberate and are covered by tests:

- **A missing capability is SKIP, never PASS.** A pen-only panel reports pen checks as skipped,
  and a run containing a skip is explicitly *not* a complete pass. A green report that quietly
  ignored absent hardware would hide the exact problem the run exists to find.
- **Skipping a step by hand records a skip**, not a pass.

The report also records the input path: pen and touch always arrive on `WM_POINTER`, where the app
gets pressure, eraser-tip identity and palm flags from the driver, and mouse always arrives on the
WPF mouse events. A capability the hardware cannot report (for example pressure on a contact-only
panel) is a skip — which is the correct answer, not a defect in the app.

Thresholds live in `AcceptanceThresholds`. The defaults are loose enough to pass a mediocre panel
and tight enough to catch a broken one. A school with good hardware can tighten them and record
why.

## 8. What is not covered

- No `.TY` board import (vendor format, undocumented).
- No teacher-to-student control channel.
- No handwriting recognition or formula library.
- No audio recorder or video player.
- The legacy `TouchServer.exe` calibration daemon is not used. Calibration here is solved
  directly from four measured points, which removes the vendor helper rather than reimplementing
  it.
