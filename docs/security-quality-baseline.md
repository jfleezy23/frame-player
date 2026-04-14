# Security and Quality Baseline

This note is the maintainer-facing source of truth for the repository's current GitHub quality and security posture.

It exists to reduce drift between:

- workflow files in `.github/workflows/`
- live GitHub repository settings
- public-facing policy text in [SECURITY.md](../SECURITY.md)

If a maintainer changes any of those areas, this file should be updated in the same pull request.

## Current Intent

The repository currently aims to do three things well:

1. keep `main` protected from unreviewed drift
2. catch obvious dependency, secret, CI, and code-quality regressions early
3. keep the setup honest and understandable enough that future maintainers can extend it safely

This baseline does not claim that GitHub automation replaces human review, release validation, or product-specific correctness testing.

## Live Repository Guardrails

The current expected GitHub repository settings are:

- `main` requires pull requests before merge
- branch protection applies to administrators
- merged branches are deleted automatically
- `Windows CI / build` is a required merge check on `main`
- SonarQube is active, but is not currently a required merge check

If the required check name changes in GitHub Actions, branch protection must be updated immediately or merges may be blocked unexpectedly.

## Active Workflows

### [`.github/workflows/windows-ci.yml`](../.github/workflows/windows-ci.yml)

Purpose:

- clean-runner compile validation for the real Windows build path

Current role:

- required on `main`
- restores the pinned FFmpeg runtime
- runs Release `x64` restore/build

### [`.github/workflows/dependency-review.yml`](../.github/workflows/dependency-review.yml)

Purpose:

- flags newly introduced vulnerable dependencies on pull requests

Current role:

- advisory check on pull requests
- currently configured to fail on `high` severity findings

### [`.github/workflows/sonarqube.yml`](../.github/workflows/sonarqube.yml)

Purpose:

- SonarQube Cloud analysis for pull requests and `main`

Current role:

- active and green
- uses the Windows build path that matches this repository
- not currently required for merge

Required secret:

- `SONAR_TOKEN`

Optional repository variables:

- `SONAR_ORGANIZATION`
- `SONAR_PROJECT_KEY`

Default fallback behavior:

- organization defaults to `github.repository_owner`
- project key defaults to `owner_repo`

### [`.github/workflows/dotnet-desktop.yml`](../.github/workflows/dotnet-desktop.yml)

Purpose:

- packaging helper for test-drop style release artifacts

Current role:

- manual and release-published helper
- uploads packaged artifacts
- supports GitHub artifact attestations for release provenance

## GitHub-Native Security Features Expected To Be Enabled

The repo is expected to keep these GitHub-native features active:

- default CodeQL setup for `actions` and `csharp`
- secret scanning
- push protection
- dependency graph
- automatic dependency submission
- Dependabot security/version update PRs for `nuget` and GitHub Actions
- private vulnerability reporting

If any of those are deliberately disabled, that should be called out in [SECURITY.md](../SECURITY.md) and in the pull request that changed the posture.

## Workflow Hygiene Rules

When editing GitHub workflows in this repository:

- pin third-party and GitHub actions to full commit SHAs, not only floating tags
- prefer narrow permissions blocks instead of default token scopes
- prefer reusing the repo's real build path over creating sample or template workflows
- avoid adding duplicate scanners that conflict with GitHub-native features already enabled
- keep workflow names and check names stable when possible, especially for required checks

Examples of drift we want to avoid:

- changing a required check name without updating branch protection
- reintroducing a custom CodeQL workflow while GitHub default CodeQL setup is enabled
- adding placeholder/sample CI files that do not match the actual build system

## When To Update This File

Update this baseline when any of the following changes:

- required branch protection checks
- active quality/security workflows
- expected repo secrets or variables for CI/security tooling
- enabled or disabled GitHub-native security features
- artifact provenance or release-screening posture

## Current Known Non-Blocking Gaps

These are intentional gaps, not accidents:

- SonarQube is active but not yet required for merge
- Code scanning results are active but not yet enforced as a blocking branch rule
- product-specific media regression coverage still lives outside generic GitHub quality tooling and remains a separate validation concern

That split is intentional. Generic platform security tooling should support, not replace, the repo's domain-specific validation.
