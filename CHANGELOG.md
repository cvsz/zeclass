# Changelog

All notable changes to projects created from this template should be documented here.

The format is based on Keep a Changelog and projects are encouraged to follow Semantic Versioning.

## [Unreleased]

### Added

- Repository template baseline
- Security and contribution policies
- GitHub issue and pull request templates
- CI, CodeQL, dependency review, and Dependabot automation
- Release workflow and project documentation structure
- Office import tests: the full AGENTS §12 fixture matrix — EMF and WMF media
  identified by content, PNG/JPEG payloads never saved under a laundering
  extension, no-preview and malformed-ZIP packages, DOCX `word/media`, and
  explicit DOCM/PPTM support; plus the AGENTS §25 import-latency budget
  (mixed images + Office preview + PDF parse batch < 5 s). `ImporterTests`
  33 → 42; total 566 → 575 tests
- GPG commit signing: per-user GnuPG 2.5.24 (installer SHA-256 verified
  against the winget manifest), ed25519 sign-only key published to the
  GitHub account (key id `5362594`), repo-local `commit.gpgsign=true`;
  first signed commit `1989ee0` verified by GitHub (`verified=true`,
  `reason=valid` via REST)
- Repository governance: branch ruleset `protect-main` (id 24230715)
  enforcing pull requests on `main` with required checks `build`,
  `repository-baseline`, `analyze`, `Analyze GitHub Actions`, plus
  force-push and branch-deletion blocks (0 bypass actors); repo settings
  `allow_auto_merge` + `delete_branch_on_merge` enabled; Dependabot
  `docker` ecosystem removed (no Dockerfile exists)

### Changed

- Dependency updates (locally rebuilt and fully retested: 569 fast + 6 soak
  green, 1 documented conditional skip, format clean): Microsoft.NET.Test.Sdk
  17.11.1 → 18.10.1, xunit 2.9.2 → 2.9.3, xunit.runner.visualstudio 2.8.2 →
  4.0.0, Xunit.SkippableFact 1.5.23 → 1.5.85, System.Drawing.Common 8.0.0 →
  10.0.12 (both projects)

### Fixed

- Release build: `LibraryImport` source generator now compiles (`AllowUnsafeBlocks`, explicit bool marshalling on `AreDpiAwarenessContextsEqual`)
- Office import trust boundary: bounded ZIP extraction (`ZipLimits`), traversal/absolute/ADS entry rejection, content-sniffed media extensions, `IsSupported` no longer advertises legacy `.doc`/`.ppt`
- Persistence: explicit dirty-state tracking (`DocumentSession` — `CurrentRevision`/`SavedRevision`/`AutosavedRevision`/`IsDirty`); Save-As commits path and name only after the write succeeds; autosave honors `AutoSaveEnabled`/interval, skips clean boards, drops stale snapshots, rotates 2 generations; New/Open/Close prompt on unsaved work; crash recovery offered when an autosave is newer than the save
- Board limits: aggregate caps (strokes/page, total strokes, points/stroke, total points, images, page-name length, canvas dimensions) enforced in `Sanitize` with test-injectable `BoardLimits`
- Import staging: managed `ImportStaging` root with per-operation folders and startup orphan cleanup by age (7 d) and size (1 GB); PDF/Office importers use it
- NDI trust: canonicalized executable path, existence re-check, optional `ZECLASS_NDI_SHA256` hash pin, canonical identity match
- Audio recorder: MCI save path quoted, quote-bearing paths refused safely, no full paths in logs, invalid paths fail cleanly
- Diagnostics: exported report is now the shareable redacted form (no full paths, device instance ids, manufacturers); on-screen panel keeps the full local form
- PDF: bounded reads (TOCTOU-safe capped copy, head+tail page-count scan)
- Installer: hash/PE verification of staged artifact, rollback to backup on install/verify failure, only stops instances running from the install directory
- Installer: digitizer precheck now queries `-Class HIDClass` (the previous `-Class HID` matched nothing and always reported "no touch device" even with three working screens); devices not reporting `OK` are flagged for Device Manager
- New `build\uninstall.ps1`: per-user removal mirroring the installer — stops only processes running from the install directory being removed, removes only that directory and its shortcuts, keeps boards/calibration unless `-PurgeUserData` is explicit, optional `-Backup` moves the install aside instead of deleting; E2E-verified on this machine under PowerShell 5.1 (6 cases: clean uninstall, idempotence, running-instance stop, backup, purge, shortcut cycle)
- Release workflow: SemVer tag check, tag-vs-csproj version gate, per-artifact `release-manifest.json` (hashes, signature status, SBOM binding)
- Release workflow: `workflow_dispatch` dry-run trigger — a manual run exercises the whole pipeline (tests, signing gate, publish, sign/verify, SBOM, checksums, manifest) without a tag and can never create a GitHub release; tag gates fall back to the csproj SemVer version. Local partial dry run VERIFIED-BY-MANUAL with a throwaway certificate (sign → DigiCert timestamp → verify refuses untrusted root; cert removed afterwards)
- Digitizer detection: classification now uses a three-stage fallback — HID caps (`HidP_GetCaps`, usage page 0x0D) when the device can be opened, then the Windows device description (`SPDRP_DEVICEDESC`, e.g. "HID-compliant touch screen"), then path keywords and the known-panel vendor list; "pen"+"touch" descriptions classify as `TouchAndPen`; previously undetectable touch controllers (VID 0483/1FD2) now report `TouchScreen` with pressure
- Board files: explicit `Version` field stamped on save; load rejects a file claiming an unsupported format version before using its content; files without the field keep loading as version 1
- Board files: previously unbounded strings capped — board name, language tag, shape name, background/image paths (Win32 long-path ceiling) are truncated or dropped in `Sanitize`
- Persistence: previous board retained as `.bak` before every replace; read-back length verification restores the backup if the write came up short; `RecoverStaleTempFiles` reconciles interrupted saves at startup (autosave directory) and on open (promotes a valid orphan temp, moves a corrupt one aside as `.corrupt`, deletes debris beside a healthy board)
- Screen recorder: failure-path tests — low-disk trip mid-recording finalizes captured frames, interrupted final swap keeps the target and leaves no temp, dispose-without-stop finalizes, unusable target fails cleanly
- Performance budgets: explicit thresholds for 10k-stroke save/load, max-cap hostile board load, and page-switch lookup on a 200-page term board
- Accessibility: static-contract tests enforce an accessible name (explicit `AutomationProperties.Name`, tooltip, or text content) on every button in `MainWindow.xaml`, plus window name and live-region `LiveSetting`; NVDA/tab-order walkthrough still pending on hardware
- Export state safety: `WritePdf` restores the active page in a `finally` (a render throw mid-loop no longer strands the board on the wrong page), and PDF export refuses beyond `MaxExportPages` (100) before rendering anything, surfacing through the existing export failure dialog
- Page-index hot paths: `EnsurePages` returns consistent documents untouched (linear check instead of re-sort/rebuild on every `Active()` call); export page selection builds one index map instead of scanning per index

### Security
