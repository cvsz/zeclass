# Execution plan (remaining)

Status: 7 slices implemented and locally verified (see
`PRODUCTION-EVIDENCE.md`). No git here — committing, PRs, and CI runs are the
next operator steps.

## Immediate (operator with git + GitHub)

1. Review the working-tree diff (7 slices, all additive except `MainWindow`
   save/autosave wiring and `install.ps1`).
2. Commit per slice, one PR per slice (`fix:`/`feat:`/`security:`/`test:`),
   with Problem/Root-cause/Implementation/Tests/Security/Performance/
   Compatibility/Docs/Evidence/Limitations/Rollback in each body.
3. Watch CI: build, format, tests, soak, audit, hygiene, PSScriptAnalyzer,
   per-RID publish, SBOM. Fix forward, never weaken gates.
4. Tag `v1.0.0` to exercise the version gate + manifest + signing path
   (dry-run first with `RELEASE_SIGNING_REQUIRED` unset).

## Next verification (panel machine)

5. `install.ps1` end-to-end (precheck, hash verify, start-and-close).
6. Digitizer status → Align → in-app Acceptance test → file the report.
7. Keyboard-only walkthrough; screen-reader pass; NDI live toggle with
   `ZECLASS_NDI_CAPTURE` set.

## Next engineering slices

8. `.NET` 8 → 10 LTS migration assessment (own slice).
9. Portable board-asset package (design + migration + tests).
10. Structured digitizer error taxonomy (with UX copy sign-off).
11. Native-speaker localization review per language.
12. Branch-protection ruleset + required-checks documentation
    (admin action, mark verified when done).

## Accepted standing risks

Hardware-dependent claims stay `CONDITIONAL` until (6) is filed. No `GO`
without CI green + signing evidence + hardware report.
