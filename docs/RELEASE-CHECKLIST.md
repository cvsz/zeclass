# Release checklist

Block the release while any box is unchecked.

## Source

- [x] no known P0/P1 bugs in implemented slices (remaining items deferred in
      `PRODUCTION-EVIDENCE.md` with rationale)
- [x] no dead critical paths (unused `RegisterPointerInputTarget` already removed
      upstream; `JpegStart` regex removed this pass)
- [x] no unsafe broad catches hiding failures (all catches enumerated exception
      types; verified by review)
- [x] no uncontrolled allocations (importer/recorder/board paths bounded)
- [x] no unbounded parsing (PDF/Office/board caps + tests)
- [x] no architecture-specific ABI assumptions (struct-marshal interop)
- [x] no unsafe process execution (NDI canonical+pin; installer scoped kills)

## Build

- [x] SDK pinned (`global.json` 10.0.401)
- [x] warnings = errors (csproj + green build)
- [x] deterministic build (`Deterministic=true`)
- [x] Release build re-run after final doc edits this pass: build 0 warnings
      /0 errors, 584 tests (583 + 1 conditional skip: fast subset 577 + 1,
      soak 6/6, Release, re-verified 2026-10-09), format clean,
      audit clean. Docs-only commits followed; re-run once more before any tag
- [x] x64 verified — CI publish green on every push through `cb04b19`, plus
      local publish and repeated app execution (installer E2E ran the binary);
      release dry run `37795170253` packaged all three RIDs with checksums,
      manifest, and SBOM
- [ ] ARM64 verified — CI publish green (build/package only); not executed on
      ARM64 hardware
- [ ] x86 verified or removed — CI publish green (build/package only); not
      executed on x86 as a 32-bit process (x64 runs under WoW64 only)

## Tests

- [x] unit / integration / adversarial / soak / persistence / importer /
      recorder / native interop (local evidence)
- [ ] installer end-to-end on target machine — **local E2E done** (4-case
      install matrix + 6-case uninstall matrix, PS 5.1, this machine);
      target-panel run still pending
- [ ] hardware touch/pen matrix on panel machine
- [ ] accessibility + localization human passes

## Security

- [x] CodeQL green (CI) — workflow success on `db344d3`, `02c691a`,
      `c86f9ed`, `d7c3bee` (API-verified), plus `2355303` (`37787802635`),
      `a9fa2a6` (`37791142602`), `8c40e29` (`37795165475`), `cb04b19`
      (`37798687365`); no findings surfaced
- [x] dependency scan clean (local NuGet audit)
- [ ] secret scan (GitHub Security pages not browsed from here; CI
      repository-baseline secret-*file-name* check passes)
- [x] PowerShell parse check (`install.ps1` + `uninstall.ps1`); PSScriptAnalyzer
      green in CI
- [x] workflow hardening (SHA-pinned actions, least privilege; hygiene job)
- [x] SBOM (CI CycloneDX job)
- [x] no embedded secrets (reviewed; none found)
- [x] untrusted files bounded; diagnostics redacted

## Reliability / release

- [x] crash-safe save (tmp→flush→rename, `.bak`, read-back, stale-temp
      recovery), autosave + recovery, atomic replacement
- [x] installer rollback, hash/PE verification; uninstaller scoped removal
- [x] recording limits, import cleanup
- [ ] Authenticode signing with a production certificate (release job dry run
      `37795170253` green end to end with the self-signed CI-validation cert:
      sign → DigiCert timestamp → verify → scoped tolerate; `Status=Valid`
      still needs a production cert)
- [x] SHA-256 + SBOM + `release-manifest.json` (workflow)
- [x] changelog updated; roadmap updated (`docs/ROADMAP.md`, README counts)
- [x] production evidence for CI-only rows — CI/CodeQL success recorded in
      `PRODUCTION-EVIDENCE.md` and `VERIFICATION-MATRIX.md` (tag path still unrun)

## Docs

- [x] `ARCHITECTURE.md`, `THREAT-MODEL.md`, `VERIFICATION-MATRIX.md`,
      `PRODUCTION-EVIDENCE.md`, `EXECUTION-PLAN.md` current
- [x] limitations explicit; no unsupported claims/Test-count claims match runs
