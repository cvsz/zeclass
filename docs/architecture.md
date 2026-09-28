# Architecture (production view)

Local-first Windows 11 classroom whiteboard. WPF + .NET 8 (`net8.0-windows`,
SDK pinned `8.0.425`), self-contained per-RID publish (`win-x64`, `win-arm64`,
`win-x86`). No telemetry, no network calls, no cloud dependencies.

## Components

- `Core/InkModel.cs` — document/page/stroke/image model, `BoardSerializer`
  (atomic tmp+flush+rename save, 256 MB pre-check), `Sanitize` repair with
  `BoardLimits` aggregate caps.
- `Core/DocumentSession.cs` — dirty-state revisions
  (`Current`/`Saved`/`Autosaved`/`IsDirty`), autosave gating, stale-snapshot
  guard, autosave rotation (2 generations), crash-recovery predicate.
- `Core/InkEngine.cs`, `Core/PointerNative.cs` — Win32 `WM_POINTER` stack via
  struct-marshal `LibraryImport` (x64/x86/ARM64-safe, `PEN_MASK`-gated fields).
- `Core/DigitizerService.cs` — read-only SetupAPI/HID enumeration + OS
  touch/ink registry probes; failures degrade to "no devices", never throw.
- `InkSurface.cs`, `PageStrip.cs`, `BoardRenderer.cs` — input/render/page UI,
  dependency-free PDF writer.
- `PdfImporter.cs` (`PdfLimits`), `OfficePreviewExtractor.cs` (`ZipLimits`),
  `ImportStaging.cs`, `Importer.cs` — bounded, content-sniffed import pipeline
  into per-operation staging folders with startup orphan cleanup.
- `Tools/ScreenRecorder.cs` — streaming uncompressed-AVI muxer (header →
  append → backpatch → rename) with duration/size/disk/rate/resolution caps.
- `Tools/AudioRecorder.cs` — MCI/`winmm.dll` WAV capture, quoted paths.
- `Tools/NdiBridge.cs` — supervises an external capture helper by
  PID+start-time+canonical-path identity; optional SHA-256 pin.
- `MainWindow.xaml(.cs)` — chrome, transactional Save/Save-As, dirty prompts,
  recovery UI, full vs shareable diagnostics.
- `build/publish.ps1`, `build/install.ps1` — per-RID publish with checksums;
  per-user install with precheck, hash/PE verification, backup+rollback.

## Data flows

Board file (`.ebboard` JSON, external image paths) → size-gated load →
`Sanitize` repair → live document → atomic save. Imports → bounded staging →
board pages. Recordings → temp AVI/WAV → atomic rename. Diagnostics stay local
unless the operator exports the redacted form.

## Trust boundaries

Untrusted: board files, PDF/Office/images, device metadata, executable paths,
env vars, ZIP contents, persisted calibration/settings. Every boundary above
has size/count/time/content validation; see `THREAT-MODEL.md`.
