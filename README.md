# EBoard: interactive whiteboard for USB touch panels and pens

A clean-room Windows 11 interactive whiteboard for classroom panels. Built as a replacement for
the board surface of the legacy EClass 1.0.5 product, using no vendor code.

## What it does

**Ink**
- Variable-width strokes from pen pressure, with a per-device pressure envelope so different
  digitizer models both map to a usable 0..1 range.
- Tools: pen, highlighter, eraser, line, rectangle, ellipse, triangle, arrow, star.
- Solid, dashed and dotted line types; outline, filled and filled-plus-outlined shapes.
- Barrel-switch eraser tip overrides the selected tool.

**Editing**
- Undo **and** redo, command-based and bounded (200 deep), so a mistake is never a whole page.
- Rectangle and lasso selection with delete, copy, cut, paste.
- Text tool: tap to place, type with the keyboard or the on-screen keyboard, caret editing.
- Right-click context menu on the board.
- Pan and zoom (contents roam) with a Fit reset.

**Pages**
- Add, delete, duplicate, reorder; a thumbnail strip with live previews and per-page
  delete / copy / settings / lock.
- Per-page background colour and image. A background image behind a locked page is the
  vendor's "fill" pen: writing on it reveals the picture.
- Autosave every 60 s to `%LOCALAPPDATA%\EBoard\boards\autosave.ebboard`.

**Content**
- Insert images from disk, with a missing file shown as a placeholder rather than failing the
  page.
- Capture the desktop and place it on the page.
- Export to PDF (one page per board page) or PNG per page, or save the board file.

**Teaching tools** (vendor manual section 5)
- Live magnifier, spotlight, screen curtain, teaching clock (digital / simulated / counting /
  countdown), on-screen keyboard.
- Calculator, ruler, compass, set square, protractor, table sheet.

**Hardware**
- USB digitizer discovery with VID/PID, and a diagnostics panel plus a saveable report.
- Four-point calibration with a projective fit, per-board persistence, and worst-case error
  reporting.
- Palm rejection: touch is ignored while a pen is in contact.
- Gestures: fist-hold erase, hand-wave page turn, palm launch.

## Project layout

    src/EBoard/
      Core/InkModel.cs          document, page, stroke, image model + board file serializer
      Core/InkEngine.cs         pressure calibration, palm rejection, outline geometry
      Core/PointerNative.cs     WM_POINTER reader (pressure, eraser tip, tilt, palm flag)
      Core/DigitizerService.cs  SetupAPI HID digitizer discovery
      Core/Calibration.cs       4-target projective solve + per-board persistence
      Core/EditHistory.cs       reversible commands, undo/redo stacks
      Core/Selection.cs         rectangle and lasso selection
      Core/TextAndGestures.cs   text session, dwell/swipe gesture recognition
      InkSurface.cs             the board surface: input, selection, text, rendering
      PageStrip.cs              page thumbnails
      BoardRenderer.cs          page rendering + dependency-free PDF writer
      ExportDialog.cs           export options and file dialog
      Tools/                    annotation, geometry, and calculator overlays
      ColorPickerWindow.cs      colour picker
      MainWindow.xaml(.cs)      chrome, tool palette, file handling, wiring
      CrashLog.cs               rotating log and diagnostics export
    tests/EBoard.Tests/         xUnit tests, 124 passing
    tools/IconGen/              build-time icon generator (not shipped)
    docs/                       hardware notes, extracted vendor spec, roadmap
    build/publish.ps1           per-runtime self-contained publish with checksums

## Application icon

The mark is generated as vector art, not drawn by hand or scaled from a bitmap:

    dotnet run --project tools\IconGen -- src\EBoard\Assets\EBoard.ico
    dotnet run --project tools\IconGen -- <out-dir> --dump 16 32 48 256

Ten sizes are packed into one `.ico` (16, 20, 24, 32, 40, 48, 64, 96, 128, 256) as PNG entries,
which is what a current Windows shell expects and which preserves the antialiasing at small
sizes. Each size is re-rendered from the same unit-square geometry at 4x and downsampled, and
details that cannot survive are dropped per size: the bezier ink stroke becomes a single clean
bar below 32 px, the pen nib and the red mark appear only at 48 px and up. The `--dump` flag
writes individual frames to PNG, which is the only reliable way to confirm a 16 px icon is still
legible.

It is wired in two places, because they need different mechanisms:

- **Executable icon** via `ApplicationIcon` in the project file, so the exe, taskbar, Alt+Tab and
  Explorer all show it without the app running.
- **Window icon** loaded from `Assets\EBoard.ico` in code at startup. WPF's XAML type converter
  rejects a bare `.ico` path, so the `Icon="..."` attribute is deliberately absent; the asset is
  marked `CopyToOutputDirectory` so it ships with the publish.

## Build, test, publish

    dotnet build src\EBoard\EBoard.csproj -c Release
    dotnet test  tests\EBoard.Tests\EBoard.Tests.csproj -c Release
    powershell -ExecutionPolicy Bypass -File build\publish.ps1

Verified on Windows 11 build 26100 with .NET SDK 8.0.425: 0 warnings, 0 errors, 124 tests
passing. The self-contained win-x64 publish is 145 MB across 243 files and launches cleanly.

## Hardware bring-up

See `docs\HARDWARE.md`. In short: connect the panel's touch cable, confirm the status bar reads
`Digitizer: USB pen + touch` (or `USB touch`), then click **Align** and touch the four crosses
until the reported error is low.

## Input architecture

The primary input path is the Win32 pointer-message stack (`WM_POINTERDOWN/UPDATE/UP` plus
`GetPointerInfo` / `GetPointerPenInfo`). It is the only Windows input API that carries normalized
pressure, eraser-tip identity, tilt, palm flags, and stable contact ids for USB touch panels; the
legacy WPF stylus stack drops pressure on most vendor digitizers.

The fallback is the WPF stylus and touch events plus mouse, used when pointer registration is
refused. Both paths produce the same `PointerSample`, so the ink engine is unaware of which is
active. The Diagnostics panel reports which one is in use.

## Known limitations

These are real gaps, not oversights:

- **No real-hardware verification yet.** Calibration, pen pressure, multi-touch and the gestures
  have not been exercised against an actual panel. This is the largest remaining risk.
- **No PDF, Word or PPT import.** The vendor's "open file" accepts them; only images can be
  inserted. Rendering office documents needs a background approach that survives locked-down
  school images, which rules out office automation.
- **English UI only.** The vendor manual claims 20 languages.
- **No print or email export.** PDF and PNG export exist.
- **No video player or media tools.** The packaged build depends on DirectShow-era components
  that will not work on a current Windows image.
- **No screen or audio recorder.** Implementable, but it needs a storage policy for
  student-recorded material first.
- **No recognition pen** (draw a circle for a spotlight, a square for a magnifier).
- **No `.TY` board import.** Undocumented vendor format.
- **No teacher-to-student control protocol.** Undocumented and vendor-paired; see
  `..\EClass_Win11\Spec\FINDINGS_Lab.md`.

## Boundaries

No decompilation was performed and none is proposed: every fact about the legacy product comes
from the vendor's own user guide, PE metadata, import tables, string tables, and runtime
observation. The vendor hook DLLs (`CAPHOOK.dll`, `TEHOOK.dll`, `PCHook.dll`) are deliberately
not reproduced or emulated; all input injection in this app goes through documented Windows APIs
and only in response to an explicit operator action.
