# zEClass: interactive whiteboard for USB touch panels and pens

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
- Autosave every 60 s to `%LOCALAPPDATA%\zEClass\boards\autosave.ebboard`, serialized on a
  background thread and written through a temporary file that is renamed into place, so a
  crash mid-write leaves the previous copy intact. The final save runs synchronously while
  the window closes.
- Board files are validated on open: an oversized file is refused before it is read, and a
  truncated or hand-edited one is repaired into a usable board instead of crashing.

**Content**
- Insert images from disk, with a missing file shown as a placeholder rather than failing the
  page.
- Capture the desktop and place it on the page.
- Import PDF (embedded page images) and PPTX/DOCX (embedded preview), reporting clearly when a
  file has nothing extractable rather than showing an empty page.
- Export to PDF (one page per board page) or PNG per page, print via the shell print verb on a
  staged PDF, or email the board as a zip package.
- Replay a page's strokes in draw order, with pause, a scrubber, speed selection and an
  overlay mode. Timing comes from the timestamps recorded as you draw, so a page drawn before
  this feature existed replays as a single moment rather than pretending to be animated.
- Record the screen to a self-contained uncompressed AVI (10 fps, no external codec, no
  `avifil32` dependency).
- Record classroom audio to an uncompressed WAV file through MCI (`winmm.dll`, in-box since
  Windows 3.1), so there is nothing to install and nothing a locked-down image can refuse.
- Feed the board to a streaming setup over NDI: when vMix Desktop Capture is installed, the
  board starts it minimized in the background, supervises it (one instance, reaped on exit,
  never touching any other process), and offers a top-bar toggle. The helper's own Minimise
  on Startup stays the primary mechanism; the bridge is the supervisor, not a replacement.

**Presentation**
- Three themes: light, dark, and a high-contrast theme for projectors and low-vision use. The
  default follows the OS accessibility setting rather than a hardcoded light theme.
- 25 interface languages. The picker shows each language in its own script (`Deutsch`, `日本語`,
  `العربية`), and switching to Arabic or Hebrew mirrors the whole window rather than leaving a
  half-flipped layout. Untranslated keys fall back to English one word at a time, so a partial
  translation degrades instead of blanking.

**Hardware**
- USB digitizer discovery with VID/PID, and a diagnostics panel plus a saveable report.
- Four-point calibration with a projective fit, per-board persistence, and worst-case error
  reporting.
- Palm rejection: touch is ignored while a pen is in contact.
- Gestures: fist-hold erase, hand-wave page turn, palm launch, two-finger pinch/pan/twist, and a
  recognition pen (circle draws a spotlight, square opens the magnifier).

## Project layout

    src/zEClass/
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
    tests/zEClass.Tests/         xUnit tests, 428 total
    tools/IconGen/              build-time icon generator (not shipped)
    docs/                       hardware notes, extracted vendor spec, roadmap, localization
    build/publish.ps1           per-runtime self-contained publish with checksums
    build/install.ps1           per-user installer with a digitizer precheck

## Application icon and branding

The icon is built from the company logo, not drawn by hand:

    powershell -ExecutionPolicy Bypass -File build\icon-from-logo.ps1 -Source D:\eclass\branding\zeazdev.png

Ten sizes are packed into one `.ico` (16, 20, 24, 32, 40, 48, 64, 96, 128, 256) as PNG entries,
which is what a current Windows shell expects and which preserves the antialiasing at small
sizes. The script validates what it wrote (magic, entry count, every blob's PNG signature) and
fails loudly rather than shipping a corrupt icon. The About dialog shows the same logo plus the
organization emblem from `Assets\`; both are loaded defensively, so a missing file degrades to
text rather than a crash. (`tools\IconGen` remains for the original vector mark but is no
longer the shipped icon.)

It is wired in two places, because they need different mechanisms:

- **Executable icon** via `ApplicationIcon` in the project file, so the exe, taskbar, Alt+Tab and
  Explorer all show it without the app running.
- **Window icon** loaded from `Assets\zEClass.ico` in code at startup. WPF's XAML type converter
  rejects a bare `.ico` path, so the `Icon="..."` attribute is deliberately absent; the asset is
  marked `CopyToOutputDirectory` so it ships with the publish.

## Build, test, publish

    dotnet build zEClass.sln -c Release
    dotnet test  tests\zEClass.Tests\zEClass.Tests.csproj -c Release
    powershell -ExecutionPolicy Bypass -File build\publish.ps1
    powershell -ExecutionPolicy Bypass -File build\install.ps1

Verified on Windows 10 (this machine) with .NET SDK 8.0.425 (pinned in `global.json`): 0 warnings,
0 errors, 575 tests total: 574 passing and 1 conditional vendor-manual skip. Set
`ZECLASS_VENDOR_MANUAL` to a local vendor-manual PDF to run the remaining real-document check.
The self-contained win-x64 publish launches cleanly, and `install.ps1` has been run end to end
against a real per-user install directory, including the digitizer precheck and a start-and-close
verification.

Continuous integration (`.github/workflows/`) runs on Windows runners: restore, format check,
Release build with warnings as errors, unit and integration tests, a separate soak step, a
dependency vulnerability audit, workflow hygiene checks (SHA-pinned actions, least-privilege
permissions), PSScriptAnalyzer, then publishes all three RIDs with a PE check, SHA-256 checksums
and a CycloneDX SBOM. CodeQL analyzes every push and pull request; dependency review fails pull
requests that add a high-severity vulnerable package; the release workflow on `v*` tags re-runs
the gates, packages each RID, and blocks the release when signing is required but not configured
(`RELEASE_SIGNING_REQUIRED` / `RELEASE_SIGNING_THUMBPRINT`).

The soak tests are tagged so the fast loop stays fast:

    dotnet test tests\zEClass.Tests\zEClass.Tests.csproj -c Release --filter "Category!=Soak"
    dotnet test tests\zEClass.Tests\zEClass.Tests.csproj -c Release --filter "Category=Soak"

They cover the failures that would otherwise only appear after a lesson: unbounded history
growth, a board file that grows on every save/load round trip, erase ordering, and page index
drift across repeated inserts and deletes.

## Installing on a classroom machine

`build\install.ps1` installs per-user, so it needs no elevation and no Windows Installer service,
which is what usually works on a locked-down school image. It checks for a running instance,
probes for HID touch or pen devices, warns if Windows touch is disabled, copies the publish,
creates shortcuts, and then starts the app to confirm it comes up.

    powershell -ExecutionPolicy Bypass -File build\install.ps1

Use `-Force` to replace an existing install (it backs the old directory up first) and
`-SkipPrecheck` on a machine where the digitizer is deliberately not attached.

The screen recorder writes uncompressed AVI, so a ten-minute recording is a large file. It needs
`Videos\zEClass` to be writable and to have room; there is no retention policy, so a machine used
for recorded student work should have that folder managed.

## Hardware bring-up

See `docs\HARDWARE.md`. In short: connect the panel's touch cable, confirm the status bar reads
`Digitizer: USB pen + touch` (or `USB touch`), then click **Align** and touch the four crosses
until the reported error is low.

## Input architecture

The primary input path is the Win32 pointer-message stack (`WM_POINTERDOWN/UPDATE/UP` plus
`GetPointerInfo` / `GetPointerPenInfo`). It is the only Windows input API that carries normalized
pressure, eraser-tip identity, tilt, palm flags, and stable contact ids for USB touch panels; the
legacy WPF stylus stack drops pressure on most vendor digitizers.

Pen and touch contacts are handled exclusively on this path. Mouse contacts stay on the WPF mouse
events, because Windows does not generate `WM_POINTER` for mouse input unless a window opts in with
`EnableMouseInPointer` (this app never does). The WPF stylus and touch events are consumed without
drawing so WPF's promotion engine cannot turn the same contact into a second stroke through the
mouse path. Both paths produce the same `PointerSample`, so the ink engine is unaware of which is
active. The Diagnostics panel reports the active path.

An earlier build tried to opt in with `RegisterPointerInputTarget`. That API registers a *global*
redirection target for all input of a type and requires UI Access, so it always failed with
`ERROR_ACCESS_DENIED`; it was removed rather than left as dead code. Registering is not needed:
Windows posts `WM_POINTER` to the window under the pointer automatically.

## Known limitations

These are real gaps, not oversights:

- **No real-hardware verification yet.** Calibration, pen pressure, multi-touch and the gestures
  have not been exercised against an actual panel. This is the largest remaining risk. The app
  ships a guided **Acceptance test** that produces an auditable report for exactly this, but it
  needs a machine with the panel attached; see `docs\HARDWARE.md`.
- **PDF, Word and PPT import is partial.** PDFs are read for embedded page images, so a
  vector-only PDF reports that it cannot be imported. Office formats are read for their embedded
  preview and media, not reflowed: real layout needs a rendering approach that survives
  locked-down school images, which rules out office automation.
- **The translations need a native speaker.** 25 languages ship, but they were produced without
  review by a speaker of each. The wording is plausible and the audit proves nothing is missing or
  broken, but plausibility is not fluency: a teacher in front of a class will notice a term that is
  technically right and not idiomatic. Treat them as a reviewed-once-needed starting point, and get
  them checked before classroom deployment. Adding or fixing a language is a JSON edit in
  `src\zEClass\lang` plus an entry in `LanguageCatalog`.
- **Not every string on screen is translated.** The catalogue covers 67 keys: tool names,
  actions, the ribbon section labels, the page bar, the top bar, the status line and the
  calibration messages. Help text, context menus, the acceptance-test prompts, the diagnostics
  report and the transient status messages are English-only. A full translation is a much larger
  job than the file count suggests.
- **No video player.** The vendor's packaged build depends on DirectShow-era
  components that will not run on a current Windows image.
- **No retention policy on recordings.** The screen recorder writes uncompressed
  AVI and the audio recorder uncompressed WAV, so storage needs managing on a machine recording student work.
- **No local resource library browser.** Files can be imported; there is no bundled library.
- **No `.TY` board import.** Undocumented vendor format; reading it would require reverse
  engineering, which is out of scope here.
- **No teacher-to-student control protocol.** Undocumented and vendor-paired; the local
  black-box lab notes are intentionally not published with this repository.
- **NDI needs the vMix tool installed separately.** It is not bundled: set
  `ZECLASS_NDI_CAPTURE` to its location when it is not at the default path, and the board
  does nothing at all when it is absent.
- **Unsigned.** The signing hook is wired up in `build\publish.ps1` but no certificate is
  committed, so SmartScreen will warn on first run until one is supplied.

## Boundaries

No decompilation was performed and none is proposed: every fact about the legacy product comes
from the vendor's own user guide, PE metadata, import tables, string tables, and runtime
observation. The vendor hook DLLs (`CAPHOOK.dll`, `TEHOOK.dll`, `PCHook.dll`) are deliberately
not reproduced or emulated; all input injection in this app goes through documented Windows APIs
and only in response to an explicit operator action.
