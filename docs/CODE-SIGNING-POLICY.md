# Code signing policy

This page explains how PeritumAI Protect release files are built and signed, so you can check that a download is genuine.

## How releases are made

- Every release is built by GitHub Actions from the public source code in [peritumai/peritumai-protect](https://github.com/peritumai/peritumai-protect),
  on GitHub-hosted runners. Release files are never built on a personal computer.
- Each release includes `SHA256SUMS.txt` and a GitHub build provenance attestation for every file.
- Releases are published as immutable GitHub releases: once published, their files cannot be changed or replaced.

## Signing

<!-- RELEASE-KIT: keep exactly one of the two blocks below, matching the current status. -->

<!-- Block A, before signing is approved: -->
This version is **not code-signed yet**. Windows may show "Windows protected your PC" (SmartScreen). Before you run it,
check the SHA-256 hash or the attestation as described in the [README](../README.md#verify-your-download). Signed releases
will be announced here.

<!-- Block B, after SignPath approves the project. Replace Block A with this text:
Free code signing provided by [SignPath.io](https://about.signpath.io), certificate by [SignPath Foundation](https://signpath.org).

Windows shows the publisher as **SignPath Foundation**. Only files built from this repository by the release workflow are
signed, and each signing request is approved by hand.
-->

## Team roles

| Role | Member |
| --- | --- |
| Committers and reviewers | Danilo Stojković |
| Approvers (approve each signing request) | Danilo Stojković |

All members use multi-factor authentication on GitHub and SignPath.

## Privacy

PeritumAI Protect does not transfer any information to any networked system. It makes no network requests. Data it keeps stays on
your PC; see [PRIVACY.md](../PRIVACY.md).
