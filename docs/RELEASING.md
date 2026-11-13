# Releasing PeritumAI Protect

Developer notes. The owner's full launch checklist lives outside this repository.

## What a release contains

| File | What |
| --- | --- |
| `PeritumAI-Protect-<version>-setup-x64.exe` | Inno Setup installer, per-user, no admin rights |
| `PeritumAI-Protect-<version>-portable-x64.zip` | The same app without an installer |
| `SHA256SUMS.txt` | SHA-256 of both files |

The app is published self-contained and single-file for win-x64, so users do not need to install .NET.

## Make a release

1. Update `<Version>` in the app csproj and add a `## [<version>] - YYYY-MM-DD` section to `CHANGELOG.md`.
2. Commit, then tag and push:
   ```powershell
   git tag -a v<version> -m "PeritumAI Protect <version>"
   git push origin main --follow-tags
   ```
3. The `Release` workflow tests, builds, attests and creates a **draft** GitHub release with the files above.
4. Download the installer from the draft, install it on a clean Windows PC or VM, and check that it starts, works
   and uninstalls cleanly.
5. Publish the draft. Immutable releases are on, so the files cannot change after this.

## Build locally (dry run)

Needs the .NET 10 SDK and, for the installer, Inno Setup 6.4 or later.

```powershell
powershell -ExecutionPolicy Bypass -File build\publish-release.ps1 -Version 1.0.0
```

`-SkipInstaller` builds only the portable zip. `-SkipTests` skips the tests. Output goes to `artifacts\`.

## Signing

Release files are not signed yet. When the SignPath Foundation application is approved, the release workflow uploads
the unsigned files to SignPath, waits for a manual approval and attaches the signed files instead. See
[CODE-SIGNING-POLICY.md](CODE-SIGNING-POLICY.md).
