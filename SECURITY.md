# Security Policy

## Reporting a Vulnerability

Please do not open a public GitHub issue for suspected security vulnerabilities.

For sensitive reports:

- use GitHub's private vulnerability reporting for this repository
- otherwise contact the maintainer directly through GitHub: [jfleezy23](https://github.com/jfleezy23)

When reporting an issue, include:

- a short description of the problem
- affected version or release tag
- reproduction steps or proof-of-concept details
- any known impact or mitigation notes

## Current Screening and Guardrails

This repository currently uses a mix of GitHub-native security tooling and workflow guardrails to reduce drift and catch issues earlier:

- `main` requires pull requests before merge, including for administrators.
- The `build` check from `Windows CI` is required before merges into `main`.
- Merged branches are deleted automatically to reduce branch sprawl and stale release drift.
- `Windows CI` runs on pushes and pull requests and verifies the pinned runtime restore plus a Release `x64` build.
- GitHub code scanning is enabled through GitHub's default CodeQL setup for `actions` and `csharp`.
- GitHub secret scanning is enabled to detect known leaked secret patterns in repository history.
- GitHub push protection is enabled to block many secrets before they are pushed.
- The dependency graph and automatic dependency submission are enabled so GitHub can reason about shipped dependencies beyond just manifest files.
- Dependabot security and version update pull requests are enabled for `nuget` and GitHub Actions dependencies.
- Dependency review now runs on pull requests to flag newly introduced vulnerable dependencies before merge.
- Desktop packaging artifacts can be attested with GitHub artifact attestations so published build provenance can be verified.
- A SonarQube Cloud workflow is configured for PRs and `main` pushes, and becomes active when the repository `SONAR_TOKEN` secret is present; repository-level overrides are available for organization and project key if the default mapping is not correct.
- Pull request templates and issue templates are in place to keep validation, documentation, and security review visible during review.

These checks improve detection and consistency, but they are not a guarantee that a release is free of vulnerabilities. Human review and release validation still matter.

## Runtime Network Posture

Frame Player is designed as a local desktop review tool. The shipped WPF app does not include telemetry, analytics, auto-update, HTTP client, socket, or background network-service code. It uses local media files, bundled FFmpeg runtime DLLs, DPAPI-protected local state, and a hidden local child process for export and export-side probe work.

Repository build and developer workflows can perform outbound network access for NuGet restore, HTTPS downloads of pinned FFmpeg runtime artifacts, optional official FFmpeg source clones, and optional signing timestamp requests. Those are build-time supply-chain paths, not runtime telemetry paths. Network-restricted review builds should restore NuGet packages from an approved local cache/feed, stage the required runtime folders locally, and build with `-p:SkipRuntimeBootstrap=true`.

## Scope and Background

This repository includes an internal security review note in [SECURITY_REVIEW.md](SECURITY_REVIEW.md). That document is supplemental background, not the intake path for new sensitive disclosures.

Maintainer-facing workflow and branch-protection expectations are documented in [docs/security-quality-baseline.md](docs/security-quality-baseline.md).

## Disclosure Expectations

- Please give the maintainer a reasonable opportunity to investigate and remediate before public disclosure.
- Non-sensitive bugs and hardening suggestions can still be opened as normal GitHub issues.
