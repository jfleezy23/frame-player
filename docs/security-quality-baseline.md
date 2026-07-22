# Security and Quality Baseline

This document is the maintainer source of truth for the repository's automated security and quality posture. Update it with any workflow or branch-protection change.

## Repository guardrails

- `main` accepts changes through pull requests and the required `Windows CI / build` check.
- macOS Avalonia, dependency review, SonarQube, and GitHub code-scanning checks provide additional evidence; repository settings determine which are merge-blocking.
- Secret scanning, push protection, the dependency graph, dependency submission, Dependabot, and private vulnerability reporting should remain enabled.
- Human review and media-corpus validation remain required; generic scanners do not establish media correctness.

## Active workflows

- `.github/workflows/windows-ci.yml` restores pinned runtimes, validates repository scripts, builds the Avalonia application, runs both test projects, and verifies a self-contained Windows package.
- `.github/workflows/macos-avalonia.yml` builds and tests the same Avalonia application on macOS and verifies the Apple Silicon bundle and archive.
- `.github/workflows/universal-package.yml` creates and attests the Windows release archive.
- `.github/workflows/dependency-review.yml` rejects newly introduced dependencies with high-severity known vulnerabilities.
- `.github/workflows/sonarqube.yml` analyzes the Avalonia application and shared libraries when `SONAR_TOKEN` is configured.

SonarQube may use `SONAR_ORGANIZATION` and `SONAR_PROJECT_KEY`; absent values fall back to repository ownership and name. Native interop and media-engine exclusions must remain narrow, explicit, and justified by separate tests.

## Workflow hygiene

- Pin every GitHub Action to a full commit SHA.
- Use least-privilege workflow permissions and disable persisted checkout credentials unless required.
- Build the real `src/FramePlayer.Avalonia` product path; do not add template or duplicate application workflows.
- Keep required workflow and job names stable, or update branch protection in the same operation.
- Run `scripts/Test-WorkflowActionPinning.ps1` and `scripts/Test-RepoHarnessScripts.ps1` for every workflow or harness change.
- Do not expose tokens, signing identities, local file paths, or runtime download credentials in workflow logs.

## Release security gate

- Review dependency, code-scanning, SonarQube, and independent security findings before merge.
- Verify runtime archives against checked-in hashes and provenance records.
- Verify shipped archives do not contain developer-only FFmpeg CLI tools.
- Sign platform payloads with approved identities and validate signatures after packaging.
- Notarize and staple public macOS distributions; attest the Windows release archive.
- Run platform media-corpus and export-host validation on the exact release commit.

## Known non-blocking gaps

- SonarQube and code scanning may be advisory depending on current branch-protection configuration.
- The maintained media corpus is not stored in git, so full release-candidate corpus validation runs outside ordinary hosted CI.
- The checked-in macOS runtime is Apple Silicon only; an Intel or universal2 release needs independently pinned and validated native artifacts.
