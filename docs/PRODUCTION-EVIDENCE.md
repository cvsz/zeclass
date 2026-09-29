# Production evidence (this pass)

- Date: 2026-09-28/29 UTC. SDK: 8.0.425 (user-local install). OS: Windows 10.
- Git: available this pass (MinGit at `%LOCALAPPDATA%\MinGit\cmd`, not on the
  default PATH). History on `origin/main`
  (https://github.com/cvsz/zeclass):
  `fd392a0` initial import → `db344d3` digitizer classification →
  `02c691a` board-file version gate/string caps/crash-safe save/recorder
  failure tests → `c86f9ed` installer precheck fix + installer E2E evidence →
  `d7c3bee` accessibility name contract → `52e9ef7` uninstaller + E2E →
  `86a6924`/`91e76be`/`3276f04` docs evidence passes → `baac507` release
  dry-run trigger. All pushed; `HEAD == origin/main`.
  Commit signing disabled locally (`commit.gpgsign=false`): the configured
  `gpg.program` from an old Git install no longer exists on this machine.
  Artifact signing (the release gate that matters) is unaffected and still
  awaiting certificate secrets.
- Baseline before changes: `dotnet build -c Release` FAILED (9 errors:
  SYSLIB1062 missing `AllowUnsafeBlocks`, SYSLIB1051 un-marshalled bool,
  CS0227). README claims of a green build did not reproduce here.
- After changes: build 0 warnings/0 errors; **575 tests total (574 passed + 1
  conditional skip)**; fast suite `Category!=Soak` = 568 passed + 1 skip;
  soak 6/6; format clean; vulnerability audit clean; both `install.ps1` and
  `uninstall.ps1` parse and run under Windows PowerShell 5.1.
- CI evidence (GitHub Actions, API-verified): `CI` workflow run **success** on
  `fd392a0`, `db344d3`, `02c691a`, `c86f9ed`, `d7c3bee`, `52e9ef7`, `86a6924`,
  `3276f04`, `baac507` (restore, format, build, unit+integration, soak,
  dependency audit, workflow hygiene, PSScriptAnalyzer, per-RID publish,
  checksums, SBOM, repository-baseline); `91e76be`'s CI run was *cancelled* by
  `cancel-in-progress` when the next push superseded it (its CodeQL run
  succeeded — not a failure). `CodeQL` workflow **success** on every push
  through `baac507`. Neither `release.yml` path (tag push or
  `workflow_dispatch`) has run and no tags exist. GitHub Security pages were
  not browsed from here — dependency-review runs only on PRs and no PR exists
  (`gh auth login` device flow expired; repo has 0 issues / 0 PRs / 0 tags).
- Live-hardware API evidence (this machine, re-run 2026-09-30): SetupAPI probe
  12/12 HID vs independent recount; the live `DigitizerService` probe reports
  `TouchActive=true, PenActive=false, UsbDigitizerPresent=true, Ready=true`
  over 13 enumerated devices, classifying **3 touch interfaces with pressure**:
  (a) `VID_1FD2/PID_8105`, bus-reported "LGDisplay Incell Touch", manufacturer
  "Melfas", behind a Microchip hub tree on a root port — the connected Dell
  P2418HT's touch surface (identity INFERENCE from bus description + hub
  topology); (b) `VID_0483/PID_A581` (STMicroelectronics, no product strings)
  with two touch interfaces plugged directly into motherboard rear port
  `HS02` — physical device NOT IDENTIFIED (unplug rear USB port 2 to pin it
  down). Keyboard/mouse/hub HID collections are correctly classified
  `External`, not digitizers. The Dell **P2424HT is still absent**: no
  `VID_413C` device and no P2424 display anywhere in the tree. Digitizer
  *detection* is verified end-to-end; touch *input* acceptance (ink, accuracy,
  palm, gestures, calibration) still needs the interactive pass with a human.
- Installer E2E (this machine, PS 5.1): 4-case install matrix (fresh exit 0 +
  SHA-256 verify + app start/stop; reinstall w/o `-Force` exit 3; `-Force`
  upgrade exit 0 with backup retained; corrupt artifact exit 4 with rollback and
  previous-install hash preserved) and 6-case uninstall matrix (clean removal
  keeps user data; idempotent re-run exit 3; running instance *from the install
  directory* stopped then removed; `-Backup` moves aside; `-PurgeUserData`
  removes profile only on explicit flag; shortcuts created/removed on Desktop +
  Start Menu). Runs were isolated under a fake `LOCALAPPDATA`; the machine's
  real profile and shortcuts were untouched.
- NDI helper binary present at `C:\vMixDesktopCaptureNDI\`; bridge default path
  differs — set `ZECLASS_NDI_CAPTURE` on streaming machines.
- NuGet restore showed transient timeouts (`ResponseEnded`) but converged on
  retry; local per-RID publish attempted (`artifacts\{win-x64,win-arm64,win-x86}`
  with SHA-256 in `BUILDINFO.txt`), and CI publishes all three RIDs green.

## Slices completed

1. Build rescue (`AllowUnsafeBlocks`, bool marshalling).
2. Office ZIP hardening (`ZipLimits`, `IsSafeEntryName`, content-sniffed media,
   `.doc`/`.ppt` de-advertised).
3. Persistence (`DocumentSession`, transactional Save-As, dirty prompts,
   gated autosave, recovery).
4. Board aggregate limits (`BoardLimits`).
5. Import staging lifecycle (`ImportStaging` + startup cleanup).
6. NDI trust (canonical path, hash pin), MCI quoting/redaction, shareable
   diagnostics.
7. PDF bounded reads, installer rollback/hash/scope, release manifest + version
   gate.
8. Digitizer classification: HID caps → `SPDRP_DEVICEDESC` → path keywords →
   panel-vendor list (`InferKind`, 19 unit cases + live verification).
9. Board-file trust boundary completion: `Version` format gate (reject before
   use, legacy files read as v1), string caps for name/language/shape/paths.
10. Crash-safe persistence completion: `.bak` retention, read-back length
    verification with backup restore, `RecoverStaleTempFiles` (promote valid
    orphan temp / `.corrupt` aside / delete debris), wired into autosave
    startup recovery and file-open.
11. Recorder failure-path tests: low-disk trip finalizes prefix, interrupted
    final swap leaves no debris, dispose-without-stop finalizes, unusable
    target fails cleanly.
12. Performance budgets: 10k-stroke save/load <10 s, hostile max-cap load
    <5 s, 1000 page lookups <2 s (explicit thresholds).
13. Installer precheck fix: `-Class HIDClass` (the previous `-Class HID`
    matched nothing and always reported "no touch device"); non-`OK` devices
    flagged. Installer E2E matrix run and recorded.
14. Accessibility static contract: every one of the 49 window controls has an
    accessible name source (explicit `AutomationProperties.Name`, tooltip, or
    text content); window named for automation; status region declares a
    `LiveSetting`. Enforced by `AccessibilityTests`.
15. `build\uninstall.ps1`: scoped per-user removal (install-dir-only process
    stop, shortcut removal, user data kept unless `-PurgeUserData`, optional
    `-Backup`); E2E matrix run and recorded.
16. Release dry-run: `release.yml` `workflow_dispatch` trigger (manual run =
    never publishes; tag gates fall back to csproj SemVer) + local
    Authenticode round-trip with a throwaway certificate (sign → DigiCert
    timestamp → verifier refuses untrusted root; cert removed from all stores).
17. §12 fixture-matrix completion (EMF, WMF, no-preview, malformed ZIP, DOCX
    media, PNG/JPEG content-beats-name, DOCM/PPTM) + §25 import-latency budget
    (mixed image/Office/PDF batch <5 s).

## Deferred with rationale

- Portable `.ebboard` asset package: new file format + migration UI; needs
  design review, cannot be safely squeezed into this pass.
- Structured digitizer error taxonomy: existing tests pin graceful degradation;
  changing user-visible copy needs UX sign-off.
- True PDF/Office page rendering: requires a renderer dependency stack that
  violates the locked-down-image constraint; preview-extraction documented and
  vector-only PDFs are rejected explicitly with an actionable warning.
- .NET 8 → 10 LTS migration: dedicated slice (WPF/`System.Drawing`/native
  behavior validation); runtime pinned and supported in the meantime.
- Hardware touch/pen matrix, keyboard/screen-reader walkthroughs, native-speaker
  review: need panel machine and humans.
- Authenticode signing: needs a real certificate + GitHub secrets
  (`SIGN_PFX_BASE64`/`SIGN_PFX_PASSWORD`); release path blocked until then.
- Release dry-run: `release.yml` now accepts `workflow_dispatch` as a
  no-publish dry run (manual run from the Actions tab — no tag, never creates
  a GitHub release); the real `v1.0.0` tag stays an operator step after
  hardware + signing evidence exist.
- Branch protection + PR review trail: needs authenticated GitHub admin
  (`gh auth login` device flow expired) — repo currently has 0 PRs.
