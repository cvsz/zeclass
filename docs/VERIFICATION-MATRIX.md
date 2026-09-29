# Verification matrix

Environment for all rows below unless noted: Windows 10, .NET SDK 8.0.425
(pinned), Release configuration, this machine. Windows 11 + panel hardware rows
are pending on the classroom machine.

| Claim | Method | Result | Evidence / limitation |
|---|---|---|---|
| Release build, 0 warnings/errors | TEST | PASS | `dotnet build zEClass.sln -c Release`, 0/0 |
| Unit+integration 559 pass, 1 conditional skip | TEST | PASS | `dotnet test --filter Category!=Soak`; skip = vendor-manual PDF (`ZECLASS_VENDOR_MANUAL`) |
| Soak 6/6 | TEST | PASS | history bounds, round-trip stability, erase ordering |
| Format clean | TEST | PASS | `dotnet format --verify-no-changes` |
| No vulnerable packages | DEPENDENCY-SCAN | PASS | `dotnet list package --vulnerable --include-transitive`: none |
| Build-compiles interop (LibraryImport) | TEST | PASS | fixed `AllowUnsafeBlocks` + bool marshalling; build green |
| SetupAPI enumeration correct | TEST (live API) | PASS | 12/12 HID vs raw SetupAPI recount, paths well-formed, VID/PID non-zero |
| Digitizer classification (description → path → vendor) | TEST + TEST (live API) | PASS | 19 `InferKind` cases; live probe classifies 3 touch interfaces as `TouchScreen` on this machine |
| DPI: PerMonitorV2, no silent 96-DPI assumption | TEST + CODE-REVIEW | PASS (static) | manifest declares `PerMonitorV2` (+`true/pm` fallback) and `EnablePerMonitorV2()` re-applies it at runtime (InkSurface:226); awareness query tested (`PointerNativeTests`); all 5 coordinate-conversion sites use `TransformFromDevice`/`GetDpi().PixelsPerDip` — grep for hard-coded 96 finds none. Mixed-DPI/monitor-move/calibration-scaling: NOT DONE (needs panel hardware pass) |
| Board format version gate | TEST | PASS | future/non-positive version rejected before use; legacy files without the field load as v1 |
| Board string caps (name/language/shape/paths) | TEST | PASS | truncation + unusable-path drop cases; legitimate Unicode paths untouched |
| Crash-safe save (tmp→flush→rename, .bak, read-back) | TEST | PASS | backup retention, short-write restore path, locked target, missing parent, Unicode + >260-char paths |
| Stale temp recovery | TEST | PASS | promote valid orphan, `.corrupt` aside, delete debris beside primary, no-op on missing dir |
| Recorder failure paths | TEST | PASS | low-disk trip finalizes prefix, interrupted swap leaves no debris, dispose finalizes, bad target clean fail |
| Performance budgets (explicit ms) | TEST | PASS | 10k-stroke save/load <10 s, hostile max-cap load <5 s, 1000 page lookups <2 s |
| Touch/pen/pressure/tilt/palm/gestures/calibration | HARDWARE | NOT DONE | no panel on this machine; use in-app Acceptance test on panel PC |
| Office ZIP bombs/traversal/mislabel | TEST | PASS | 33/33 `ImporterTests` incl. bomb, traversal, ratio, caps |
| Board aggregate caps | TEST | PASS | `BoardSerializerTests` trim/budget/clamp cases |
| Save-As transactional, dirty prompts, autosave revisions, recovery predicate | TEST | PASS | `DocumentSessionTests` (13); UI wiring thin and untested headless |
| Import staging lifecycle | TEST | PASS | `ImportStagingTests` (5) |
| NDI identity + hash pin | TEST | PASS | `NdiBridgeTests` incl. pin match/mismatch, canonical exec |
| MCI quoting/refusal/redaction | TEST | PASS | `AudioRecorderTests` hostile-path cases |
| Diagnostics redaction | TEST | PASS | `ShareableDiagnosticsTests` (3) |
| Installer rollback/hash scoping | MANUAL (local E2E) | PASS | 4-case install matrix on this machine (PS 5.1): fresh install exit 0 + hash verify + app start/stop; reinstall w/o `-Force` exit 3; `-Force` upgrade exit 0 with backup retained; corrupt artifact exit 4 with rollback and previous install hash preserved |
| Uninstaller scoped removal | MANUAL (local E2E) | PASS | 6-case matrix (PS 5.1): uninstall exit 0 with user data kept; idempotent re-run exit 3; running instance from install dir stopped then removed; `-Backup` moves aside; `-PurgeUserData` removes profile only on explicit flag; shortcuts created on install and removed on uninstall (Desktop + Start Menu), foreign processes/installs untouched |
| CI workflow (restore/format/build/tests/soak/audit/hygiene/PSScriptAnalyzer/per-RID publish/SBOM) | CI | PASS | GitHub Actions run success on `db344d3` and `02c691a` (API-verified); `release.yml` tag path still unrun |
| CodeQL | CI | PASS | CodeQL workflow success on `db344d3` and `02c691a`; GitHub Security pages not browsed from here |
| dependency-review / PR secret scan | CI | NOT RUN HERE | dependency-review triggers on PRs and no PR has been opened yet; repo-baseline secret-*file-name* check passes in CI |
| Release manifest/version gate/signing | CI | NOT RUN | steps added to `release.yml`; execute on a `v*` tag |
| Per-RID publish (x64/ARM64/x86) | CI | NOT RUN HERE | `build/publish.ps1` + CI publish job; needs full NuGet restore |
| Screen/audio recording limits | TEST | PASS | existing recorder test suites green (26/26 NDI/audio/recording) |
| Accessibility/keyboard/screen-reader | TEST + MANUAL | PARTIAL | static name contract for all 49 window controls PASS (`AccessibilityTests`); keyboard-only + NVDA walkthrough still NOT DONE |
| Native-speaker localization review | MANUAL | NOT DONE | machine audit green; fluency unverified |

Legend: TEST = local `dotnet` evidence; CI = workflow must run; INFERENCE avoided throughout.
