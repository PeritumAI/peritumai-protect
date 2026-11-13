# Security policy

## Reporting a vulnerability

Please **do not open a public issue** for a security problem. Email **security@peritumai.com** instead.

Include, if you can:

- the PeritumAI Protect version (shown on the Help page),
- your Windows version,
- what you did, what you expected and what happened,
- a sample file or steps that reproduce it. Use made-up data only. Never send real personal data or real secrets.

You will get a reply within 7 days. Fixes are released as a new version on GitHub and on https://peritumai.com/protect. The release notes
credit the reporter unless you ask not to be named.

## Supported versions

Only the latest release gets security fixes. PeritumAI Protect has no automatic updates, so please install new versions when they appear.

## Scope

In scope: the PeritumAI Protect program, its installer and the files in this repository.

PeritumAI Protect is an offline tool and makes no network requests. A report that it sends data anywhere is always treated as critical.

## Verifying a download

Releases are built by GitHub Actions from this repository. Each release has a `SHA256SUMS.txt` file and a GitHub build
attestation (`gh attestation verify <file> --repo peritumai/peritumai-protect`). See [docs/CODE-SIGNING-POLICY.md](docs/CODE-SIGNING-POLICY.md).
