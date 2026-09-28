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

- [x] SDK pinned (`global.json` 8.0.425)
- [x] warnings = errors (csproj + green build)
- [x] deterministic build (`Deterministic=true`)
- [ ] Release build reproducible — NOT RE-RUN here after final doc edits
      (docs-only; re-run `dotnet build` + full suite before tagging)
- [ ] x64 verified — via CI publish
- [ ] ARM64 verified — via CI publish
- [ ] x86 verified or removed — via CI publish

## Tests

- [x] unit / integration / adversarial / soak / persistence / importer /
      recorder / native interop (local evidence)
- [ ] installer end-to-end on target machine
- [ ] hardware touch/pen matrix on panel machine
- [ ] accessibility + localization human passes

## Security

- [ ] CodeQL green (CI)
- [x] dependency scan clean (local NuGet audit)
- [ ] secret scan (CI / GitHub Security)
- [x] PowerShell parse check (`install.ps1`); PSScriptAnalyzer via CI
- [x] workflow hardening (SHA-pinned actions, least privilege; hygiene job)
- [x] SBOM (CI CycloneDX job)
- [x] no embedded secrets (reviewed; none found)
- [x] untrusted files bounded; diagnostics redacted

## Reliability / release

- [x] crash-safe save, autosave + recovery, atomic replacement
- [x] installer rollback, hash/PE verification
- [x] recording limits, import cleanup
- [ ] Authenticode signing + timestamp + chain verification (release job;
      needs secrets)
- [x] SHA-256 + SBOM + `release-manifest.json` (workflow)
- [x] changelog updated; roadmap updated (`docs/ROADMAP.md`, README counts)
- [ ] production evidence for CI-only rows

## Docs

- [x] `ARCHITECTURE.md`, `THREAT-MODEL.md`, `VERIFICATION-MATRIX.md`,
      `PRODUCTION-EVIDENCE.md`, `EXECUTION-PLAN.md` current
- [x] limitations explicit; no unsupported claims/Test-count claims match runs
