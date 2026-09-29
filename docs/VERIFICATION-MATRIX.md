# Verification matrix

Environment for all rows below unless noted: Windows 10, .NET SDK 8.0.425
(pinned), Release configuration, this machine. Windows 11 + panel hardware rows
are pending on the classroom machine.

| Claim | Method | Result | Evidence / limitation |
|---|---|---|---|
| Release build, 0 warnings/errors | TEST | PASS | `dotnet build zEClass.sln -c Release`, 0/0 |
| Unit+integration 556 pass, 1 conditional skip | TEST | PASS | `dotnet test --filter Category!=Soak`; skip = vendor-manual PDF (`ZECLASS_VENDOR_MANUAL`) |
| Soak 6/6 | TEST | PASS | history bounds, round-trip stability, erase ordering |
| Format clean | TEST | PASS | `dotnet format --verify-no-changes` |
| No vulnerable packages | DEPENDENCY-SCAN | PASS | `dotnet list package --vulnerable --include-transitive`: none |
| Build-compiles interop (LibraryImport) | TEST | PASS | fixed `AllowUnsafeBlocks` + bool marshalling; build green |
| SetupAPI enumeration correct | TEST (live API) | PASS | 12/12 HID vs raw SetupAPI recount, paths well-formed, VID/PID non-zero |
| Digitizer classification (description → path → vendor) | TEST + TEST (live API) | PASS | 19 `InferKind` cases; live probe classifies 3 touch interfaces as `TouchScreen` on this machine |
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
| Installer rollback/hash scoping | MANUAL/SCAN | NOT RUN | `install.ps1` parses; end-to-end run pending on target machine |
| Release manifest/version gate/signing | CI | NOT RUN | steps added to `release.yml`; execute on a `v*` tag |
| CodeQL/dependency-review/secret-scan | CI | NOT RUN HERE | workflows present; GitHub Security pages not accessible from here |
| Per-RID publish (x64/ARM64/x86) | CI | NOT RUN HERE | `build/publish.ps1` + CI publish job; needs full NuGet restore |
| Screen/audio recording limits | TEST | PASS | existing recorder test suites green (26/26 NDI/audio/recording) |
| Accessibility/keyboard/screen-reader | MANUAL | NOT DONE | automation names present; walkthrough pending |
| Native-speaker localization review | MANUAL | NOT DONE | machine audit green; fluency unverified |

Legend: TEST = local `dotnet` evidence; CI = workflow must run; INFERENCE avoided throughout.
