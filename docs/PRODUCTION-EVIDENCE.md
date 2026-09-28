# Production evidence (this pass)

- Date: 2026-09-28/29 UTC. SDK: 8.0.425 (user-local install). OS: Windows 10.
- Git: no `git` binary in this environment — HEAD SHA unrecorded, changes
  uncommitted in the working tree. Commit + PR must happen where git exists.
- Baseline before changes: `dotnet build -c Release` FAILED (9 errors:
  SYSLIB1062 missing `AllowUnsafeBlocks`, SYSLIB1051 un-marshalled bool,
  CS0227). README claims of a green build did not reproduce here.
- After changes: build 0 warnings/0 errors; tests 514 passed + 1 conditional
  skip (`Category!=Soak`); soak 6/6; format clean; vulnerability audit clean;
  `install.ps1` parses.
- Live-hardware API evidence: SetupAPI probe 12/12 HID vs independent recount;
  no touch/pen panel attached (all `External`, touch support `[None]`).
- NDI helper binary present at `C:\vMixDesktopCaptureNDI\`; bridge default path
  differs — set `ZECLASS_NDI_CAPTURE` on streaming machines.
- NuGet restore showed transient timeouts (`ResponseEnded`) but converged on
  retry; full per-RID publish not attempted here (multi-hundred-MB runtime packs
  over a flaky link).

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

## Deferred with rationale

- Portable `.ebboard` asset package: new file format + migration UI; needs
  design review, cannot be safely squeezed into this pass.
- Structured digitizer error taxonomy: existing tests pin graceful degradation;
  changing user-visible copy needs UX sign-off.
- True PDF/Office page rendering: requires a renderer dependency stack that
  violates the locked-down-image constraint; preview-extraction documented.
- .NET 8 → 10 LTS migration: dedicated slice (WPF/`System.Drawing`/native
  behavior validation); runtime pinned and supported in the meantime.
- Hardware touch/pen matrix, keyboard/screen-reader walkthroughs, native-speaker
  review: need panel machine and humans.
