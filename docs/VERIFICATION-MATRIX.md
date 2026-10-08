# Verification matrix

Environment for all rows below unless noted: Windows 10, .NET SDK 8.0.425
(pinned), Release configuration, this machine. Windows 11 + panel hardware rows
are pending on the classroom machine.

| Claim | Method | Result | Evidence / limitation |
|---|---|---|---|
| Release build, 0 warnings/errors | TEST | PASS | `dotnet build zEClass.sln -c Release`, 0/0 |
| Unit+integration 568 pass, 1 conditional skip | TEST | PASS | `dotnet test --filter Category!=Soak`; skip = vendor-manual PDF (`ZECLASS_VENDOR_MANUAL`) |
| Soak 6/6 | TEST | PASS | history bounds, round-trip stability, erase ordering |
| Format clean | TEST | PASS | `dotnet format --verify-no-changes` |
| No vulnerable packages | DEPENDENCY-SCAN | PASS | `dotnet list package --vulnerable --include-transitive`: none |
| Build-compiles interop (LibraryImport) | TEST | PASS | fixed `AllowUnsafeBlocks` + bool marshalling; build green |
| SetupAPI enumeration correct | TEST (live API) | PASS | 12/12 HID vs raw SetupAPI recount, paths well-formed, VID/PID non-zero |
| Digitizer classification (description → path → vendor) | TEST + TEST (live API) | PASS | 19 `InferKind` cases; live probe classifies 3 touch interfaces as `TouchScreen` on this machine |
| DPI: PerMonitorV2, no silent 96-DPI assumption | TEST + CODE-REVIEW | PASS (static) | manifest declares `PerMonitorV2` (+`true/pm` fallback) and `EnablePerMonitorV2()` re-applies it at runtime (InkSurface:226); awareness query tested (`PointerNativeTests`); all 5 coordinate-conversion sites use `TransformFromDevice`/`GetDpi().PixelsPerDip` — grep for hard-coded 96 finds none. Mixed-DPI/monitor-move/calibration-scaling: NOT DONE (needs panel hardware pass) |
| Board format version gate | TEST | PASS | future/non-positive version rejected before use; legacy files without the field load as v1 |
| Board string caps (name/language/shape/paths) | TEST | PASS | truncation + unusable-path drop cases; legitimate Unicode paths untouched |
| Crash-safe save (tmp→flush→rename, .bak, read-back) | TEST | PASS | backup retention, locked target, missing parent, Unicode + >260-char paths TEST-covered; read-back short-write restore present in code but not fault-injected (true ENOSPC not simulable in this environment) |
| Stale temp recovery | TEST | PASS | promote valid orphan, `.corrupt` aside, delete debris beside primary, no-op on missing dir |
| Recorder failure paths | TEST | PASS | low-disk trip finalizes prefix, interrupted swap leaves no debris, dispose finalizes, bad target clean fail |
| Performance budgets (explicit ms) | TEST | PASS | 10k-stroke save/load <10 s, hostile max-cap load <5 s, 1000 page lookups <2 s, mixed import batch (images + Office preview + PDF parse) <5 s |
| Touch detection (live digitizer probe) | HARDWARE | PASS | 2026-09-30 live `DigitizerService` probe on this machine: `TouchActive=true`, `Ready=true`; 3 touch interfaces classified `TouchScreen` with pressure — `VID_1FD2` "LGDisplay Incell Touch" (Dell P2418HT via its upstream hub, identity INFERENCE) and `VID_0483` on rear port HS02 (device unidentified); Dell P2424HT absent from the tree |
| Touch input/alignment/calibration/palm/gestures acceptance | HARDWARE | NOT DONE | detection only; the in-app Acceptance run with a human touching the screen is still required, on this machine's surfaces and on the classroom panel; no pen device attached anywhere |
| Office ZIP bombs/traversal/mislabel | TEST | PASS | 42/42 `ImporterTests` incl. bomb, traversal, ratio, caps, plus the full §12 fixture matrix (EMF, WMF, no-preview, malformed ZIP, DOCX media, PNG/JPEG content-beats-name, DOCM/PPTM) |
| Board aggregate caps | TEST | PASS | `BoardSerializerTests` trim/budget/clamp cases |
| Save-As transactional, dirty prompts, autosave revisions, recovery predicate | TEST | PASS | `DocumentSessionTests` (12); UI wiring thin and untested headless |
| Import staging lifecycle | TEST | PASS | `ImportStagingTests` (5) |
| NDI identity + hash pin | TEST | PASS | `NdiBridgeTests` incl. pin match/mismatch, canonical exec |
| MCI quoting/refusal/redaction | TEST | PASS | `AudioRecorderTests` hostile-path cases |
| Diagnostics redaction | TEST | PASS | `ShareableDiagnosticsTests` (3) |
| Installer rollback/hash scoping | MANUAL (local E2E) | PASS | 4-case install matrix on this machine (PS 5.1): fresh install exit 0 + hash verify + app start/stop; reinstall w/o `-Force` exit 3; `-Force` upgrade exit 0 with backup retained; corrupt artifact exit 4 with rollback and previous install hash preserved |
| Uninstaller scoped removal | MANUAL (local E2E) | PASS | 6-case matrix (PS 5.1): uninstall exit 0 with user data kept; idempotent re-run exit 3; running instance from install dir stopped then removed; `-Backup` moves aside; `-PurgeUserData` removes profile only on explicit flag; shortcuts created on install and removed on uninstall (Desktop + Start Menu), foreign processes/installs untouched |
| CI workflow (restore/format/build/tests/soak/audit/hygiene/PSScriptAnalyzer/per-RID publish/SBOM) | CI | PASS | GitHub Actions run success on every push through `1989ee0` (API-verified: `db344d3`, `02c691a`, `c86f9ed`, `d7c3bee`, `52e9ef7`, `86a6924`, `3276f04`, `baac507`, `eb27bae`, `1c14708`, `1989ee0`; `91e76be` CI run cancelled by `cancel-in-progress`, superseded — not a failure); `release.yml` tag and dispatch paths still unrun |
| CodeQL | CI | PASS | CodeQL workflow success on every push through `1989ee0` (API-verified for `db344d3`…`baac507`, `eb27bae`, `1c14708`, `1989ee0`); GitHub Security pages not browsed from here |
| dependency-review / PR secret scan | CI | NOT RUN HERE | dependency-review triggers on PRs; the PR carrying this row is the first PR — result recorded after merge. repo-baseline secret-*file-name* check passes in CI |
| Release manifest/version gate/signing | CI + MANUAL (local artifact check) | PASS (dry run) | Dry run `37795170253` green on `8c40e29`: build → tests → audit → sign → verify → SBOM → package → checksums → manifest → artifact upload. Three debug cycles on the way (PSParser `${rid}:`, `& $array` invocation, `UnknownError`-vs-`NotTrusted` tolerance — see PRODUCTION-EVIDENCE 23–26). Artifact `release-dry-run` contains per-RID zips + `bom.json` + `SHA256SUMS.txt` + `release-manifest.json`; all 5 checksums re-verified locally OK; manifest commit/run binding correct; shipped x64 exe extracted locally: signed by `CN=zEClass self-signed CI validation` + DigiCert timestamp, `UnknownError`/untrusted-root as designed (manifest honestly reports `signed:false` for the validation signature). Secrets `SIGN_PFX_BASE64`/`SIGN_PFX_PASSWORD` + variable `RELEASE_SIGNING_REQUIRED=true` configured 2026-09-30 with the self-signed CI-validation PFX (thumbprint `026C9BFE…`, removed from this machine after upload). Still NOT VERIFIED: tag-release path; `Status=Valid` (requires a production certificate) |
| Per-RID publish (x64/ARM64/x86) | CI + LOCAL | PASS (build/package) | CI publish job green on every push through `86a6924`; local `publish.ps1 -Runtime all` produced `artifacts\{win-x64,win-arm64,win-x86}` with SHA-256 in `BUILDINFO.txt`. Only the x64 binary is executed on this machine |
| Screen/audio recording limits | TEST | PASS | NDI 20 + audio 11 + recorder 18 = 49/49 green (incl. streaming-memory, throttle, limit, failure-path cases) |
| Accessibility/keyboard/screen-reader | TEST + MANUAL | PARTIAL | static name contract for all 49 window controls PASS (`AccessibilityTests`); keyboard-only + NVDA walkthrough still NOT DONE |
| Native-speaker localization review | MANUAL | NOT DONE | machine audit green; fluency unverified |
| Commit signature (GPG ed25519) | TEST + MANUAL (API) | PASS | key `B9CD55DF…` published as GitHub key `5362594`; commit `1989ee0` shows local `Good signature` and GitHub REST returns `verification.verified=true`, `reason=valid` (2026-09-30); key has no passphrase (profile-ACL protected, recorded tradeoff) |
| Branch governance ruleset | MANUAL (REST) | PASS | ruleset `24230715` `protect-main`: `pull_request` (0 approvals), required checks `build`/`repository-baseline`/`analyze`/`Analyze GitHub Actions`, `non_fast_forward`, `deletion`; enforcement active, 0 bypass actors; PUT with the normalized `rules` array (this API silently drops top-level `parameters`) then GET-verified |
| Release signing secrets configured | MANUAL (REST) | PASS (plumbing) | `SIGN_PFX_BASE64`/`SIGN_PFX_PASSWORD` secrets + `RELEASE_SIGNING_REQUIRED=true` variable set with a self-signed CI-validation certificate (thumbprint `026C9BFE…`, removed from this machine after upload); dry run `37795170253` proves the full sign → timestamp → verify → tolerate chain end to end. Chain trust and `Status=Valid` NOT VERIFIED (needs a production certificate) |

Legend: TEST = local `dotnet` evidence; CI = workflow must run; INFERENCE avoided throughout.
