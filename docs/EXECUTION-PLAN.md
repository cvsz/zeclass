# Execution plan (remaining)

Status: 26 slices implemented and locally verified (see
`PRODUCTION-EVIDENCE.md`), via authenticated `gh` as `cvsz` (admin on the
repo). Git history on `origin/main`:
`fd392a0` initial import → `db344d3` digitizer classification →
`02c691a` trust boundary batch (board format version, string caps,
crash-safe save backup/read-back/stale-temp recovery, recorder
failure-path tests, performance budgets) → `c86f9ed` installer precheck
fix + installer E2E → `d7c3bee` accessibility name contract →
`52e9ef7` uninstaller + E2E → `86a6924`/`91e76be`/`3276f04` docs passes →
`baac507` release dry-run trigger → `eb27bae` §12 fixture matrix + §25
import budget → `1c14708` live touch-hardware detection docs →
`1989ee0` GPG-signed commit → `c993766`/`ad7c39c` governance + dependency
updates → `6953606` export safety → `4d0309a` autosave locking → `ff95e97`
.NET 10 migration → `742d4d6` NDI signature gate → `32e2074`
recorder-test isolation → `19cf3a3`/`dd160ef`/`2556db2` release-signing
fixes → `93a9800` dry-run evidence (`cb04b19` merge). CI + CodeQL green on
every push through `cb04b19` (API-verified; required checks green on every
merge #7–#17 by ruleset enforcement).

## Immediate (operator with git + GitHub)

1. Review the pushed diff (`git log -p fd392a0..origin/main`).
2. PR trail active: ruleset `protect-main` (id 24230715) enforces pull
   requests on `main` with required checks `build`, `repository-baseline`,
   `analyze`, `Analyze GitHub Actions`, and blocks force-push/deletion
   (0 bypass actors). `gh` authenticated as `cvsz` (admin). Every slice from
   here lands as one PR with the AGENTS §27 body (Problem/Root-cause/
   Implementation/Tests/Security/Performance/Compatibility/Docs/Evidence/
   Limitations/Rollback).
3. Watch CI: build, format, tests, soak, audit, hygiene, PSScriptAnalyzer,
   per-RID publish, SBOM. Fix forward, never weaken gates.
4. ~~Exercise the release pipeline: run `release.yml` via **workflow_dispatch**
   (safe dry run — builds, tests, signing gate, manifest; never publishes, no
   tag needed)~~ done 2026-10-08: dry run `37795170253` green end to end on
   `8c40e29` after three debug cycles (PSParser `${rid}:`, `& $array`
   invocation, `UnknownError`-vs-`NotTrusted` tolerance — see
   `PRODUCTION-EVIDENCE.md` 23–26). Signing secrets +
   `RELEASE_SIGNING_REQUIRED=true` hold the self-signed CI-validation
   certificate. The real tag `v1.0.0` stays an operator step afterwards,
   gated on hardware + production-certificate evidence.

## Next verification (panel machine)

5. `install.ps1` end-to-end (precheck, hash verify, start-and-close).
6. Digitizer status → Align → in-app Acceptance test → file the report.
7. Keyboard-only walkthrough; screen-reader pass; NDI live toggle with
   `ZECLASS_NDI_CAPTURE` set.

## Next engineering slices

8. ~~.NET 8 → 10 LTS migration assessment (own slice)~~ done
   2026-10-08: migrated `net8.0-windows` → `net10.0-windows`, SDK pinned
   `8.0.425` → `10.0.401` (installed locally via winget); explicit
   `System.Drawing.Common` references removed (NU1510 on .NET 10 — ships
   in-box); restore + format + Release build clean (0 warnings), full suite
   577 passed + 1 conditional skip, win-x64 self-contained publish verified
   (245 files, 132.9 MB). All five packages already at latest stable, no
   package changes required.
9. Portable board-asset package (design + migration + tests).
10. Structured digitizer error taxonomy (with UX copy sign-off).
11. Native-speaker localization review per language.
12. ~~Branch-protection ruleset + required-checks documentation~~ done
    2026-09-30: ruleset `protect-main` (id 24230715) active with required
    checks `build`, `repository-baseline`, `analyze`, `Analyze GitHub
    Actions` — recorded in `PRODUCTION-EVIDENCE.md` slice 19 and the
    verification matrix.

## Accepted standing risks

Hardware-dependent claims stay `CONDITIONAL` until (6) is filed. No `GO`
without CI green + signing evidence + hardware report.
