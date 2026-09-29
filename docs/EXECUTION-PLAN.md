# Execution plan (remaining)

Status: 15 slices implemented and locally verified (see
`PRODUCTION-EVIDENCE.md`). Git history on `origin/main`: `fd392a0`
initial import → `db344d3` digitizer classification → `02c691a`
trust boundary batch (board format version, string caps, crash-safe save
backup/read-back/stale-temp recovery, recorder failure-path tests,
performance budgets) → `c86f9ed` installer precheck fix + installer E2E
→ `d7c3bee` accessibility name contract → `52e9ef7` uninstaller + E2E.
CI + CodeQL green on every push through `d7c3bee`; `52e9ef7` runs were
pending at last check.

## Immediate (operator with git + GitHub)

1. Review the pushed diff (`git log -p fd392a0..origin/main`).
2. PRs for remaining history if a review trail is required
   (`fix:`/`feat:`/`security:`/`test:` with Problem/Root-cause/
   Implementation/Tests/Security/Performance/Compatibility/Docs/
   Evidence/Limitations/Rollback in each body). `gh auth login` was
   blocked on the interactive device flow — complete it or open PRs in
   the browser.
3. Watch CI: build, format, tests, soak, audit, hygiene, PSScriptAnalyzer,
   per-RID publish, SBOM. Fix forward, never weaken gates.
4. Tag `v1.0.0` to exercise the version gate + manifest + signing path
   (dry-run first with `RELEASE_SIGNING_REQUIRED` unset).
5. Configure branch protection (required checks, no force push).

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
