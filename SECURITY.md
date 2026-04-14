# Security Policy

## Reporting a Vulnerability

Please do not open a public GitHub issue for suspected security vulnerabilities.

For sensitive reports:

- use GitHub's private vulnerability reporting for this repository if it is available
- otherwise contact the maintainer directly through GitHub: [jfleezy23](https://github.com/jfleezy23)

When reporting an issue, include:

- a short description of the problem
- affected version or release tag
- reproduction steps or proof-of-concept details
- any known impact or mitigation notes

## Current Screening and Guardrails

This repository currently uses a mix of GitHub-native security tooling and workflow guardrails to reduce drift and catch issues earlier:

- `main` requires pull requests before merge, including for administrators.
- Merged branches are deleted automatically to reduce branch sprawl and stale release drift.
- `Windows CI` runs on pushes and pull requests and verifies the pinned runtime restore plus a Release `x64` build.
- GitHub code scanning is enabled through GitHub's default CodeQL setup for `actions` and `csharp`.
- GitHub secret scanning is enabled to detect known leaked secret patterns in repository history.
- GitHub push protection is enabled to block many secrets before they are pushed.
- The dependency graph and automatic dependency submission are enabled so GitHub can reason about shipped dependencies beyond just manifest files.
- Dependabot security and version update pull requests are enabled for `nuget` and GitHub Actions dependencies.
- Dependency review now runs on pull requests to flag newly introduced vulnerable dependencies before merge.
- Pull request templates and issue templates are in place to keep validation, documentation, and security review visible during review.

These checks improve detection and consistency, but they are not a guarantee that a release is free of vulnerabilities. Human review and release validation still matter.

## Scope and Background

This repository includes an internal security review note in [SECURITY_REVIEW.md](SECURITY_REVIEW.md). That document is supplemental background, not the intake path for new sensitive disclosures.

## Disclosure Expectations

- Please give the maintainer a reasonable opportunity to investigate and remediate before public disclosure.
- Non-sensitive bugs and hardening suggestions can still be opened as normal GitHub issues.
