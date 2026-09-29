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

### Changed

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
- Release workflow: SemVer tag check, tag-vs-csproj version gate, per-artifact `release-manifest.json` (hashes, signature status, SBOM binding)
- Digitizer detection: classification now uses a three-stage fallback — HID caps (`HidP_GetCaps`, usage page 0x0D) when the device can be opened, then the Windows device description (`SPDRP_DEVICEDESC`, e.g. "HID-compliant touch screen"), then path keywords and the known-panel vendor list; "pen"+"touch" descriptions classify as `TouchAndPen`; previously undetectable touch controllers (VID 0483/1FD2) now report `TouchScreen` with pressure

### Security
