# Governance

## Scope
This document defines the default governance model for projects created from this template. Replace placeholders with the generated project's real maintainers and decision process.

## Roles
- **Maintainers**: review changes, protect project quality and security, and manage releases.
- **Contributors**: propose changes through issues and pull requests and follow CONTRIBUTING.md.
- **Security contacts**: receive vulnerability reports through SECURITY.md channels.

## Decisions
Prefer documented, reviewable decisions. Significant architecture or security decisions should use an ADR under `docs/adr/`. Changes affecting public contracts, security boundaries, release policy, or compatibility require maintainer review.

## Changes
Normal changes flow through pull requests with required checks. Emergency changes should still be documented, reviewed as soon as practical, and include rollback evidence.

## Branch protection
The required `main` ruleset (pull requests, required checks, approvals, no force pushes, no deletion, merge policy), how to apply it, and its current verification status live in `docs/BRANCH-PROTECTION.md`. Machine-checkable status lives in `docs/governance-checklist.json` and is enforced by `GovernanceChecklistTests`.

## Conflicts of interest
Reviewers should disclose material conflicts and avoid sole approval when impartial review is reasonably available.

## Amendments
Governance changes are made by pull request and should explain the reason, impact, and migration expectations.
