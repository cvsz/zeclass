# Release

## Versioning

Use an explicit versioning policy. Semantic Versioning is recommended for reusable software unless the project has a better-defined scheme.

## Release dry run (no tag, no publish)

`release.yml` accepts two triggers:

- **Tag push (`v*`)** — the real release: runs the full pipeline and creates a
  GitHub release with artifacts (`contents: write`).
- **`workflow_dispatch` (manual run from the Actions tab)** — always a dry run:
  exercises build → tests → signing gate → publish → sign/verify → SBOM →
  checksums → release manifest, but **never creates a GitHub release** (that
  step runs on push events only) and never needs a tag.

A manual run replaces the tag gates with the csproj `Version` (still validated
as SemVer) and names assets `zEClass-dry-run-<rid>.zip`. Use it to rehearse the
whole release pipeline — including the Authenticode step once signing secrets
are configured — before any real tag exists. Evidence from a local partial dry
run (throwaway certificate, sign → DigiCert timestamp → verify) is recorded in
`VERIFICATION-MATRIX.md`.

## Release checklist

1. Ensure required CI and security checks pass.
2. Update `CHANGELOG.md`.
3. Confirm migrations and compatibility notes.
4. Verify deployment and rollback procedures.
5. Create and push the release tag according to project policy.
6. Publish artifacts only from trusted workflows.
7. Verify the release after publication.

## Rollback

Document how to restore the last known-good version, revert migrations safely, invalidate compromised artifacts, and communicate operational impact.
