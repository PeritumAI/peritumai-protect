# Contributing to PeritumAI Protect

Thank you for helping. PeritumAI Protect is maintained by one person, so small, focused changes are the easiest to accept.

## Before you start

- **Bugs:** open an issue with the bug template. Use made-up data in examples, never real personal data or real secrets.
- **Ideas:** open an issue first, so we can agree on the change before you spend time on it.
- **Security problems:** do not open an issue. See [SECURITY.md](SECURITY.md).

## Making a change

1. Install the .NET 10 SDK (x64). Windows is required for the app and its tests.
2. Build and run the tests. They must pass with zero failures:
   ```powershell
   dotnet build
   dotnet test
   ```
3. Keep the app offline. Pull requests that add network calls, telemetry, accounts or AI model calls are not accepted.
4. Keep wording short and plain, like the existing text in the app.
5. Open a pull request against `main` and fill in the template.

## Developer Certificate of Origin (sign-off)

Every commit must be signed off. By signing off you confirm the [Developer Certificate of Origin 1.1](https://developercertificate.org/):
that you wrote the change, or otherwise have the right to submit it under this project's license.

Add the sign-off with `-s`:

```powershell
git commit -s -m "Fix: short description"
```

This adds a line such as `Signed-off-by: Your Name <you@example.com>` to the commit message. Use your real name.

## License of contributions

This project is licensed under the [Apache License 2.0](LICENSE). As section 5 of that license says, any contribution you
submit is under the same license, with no extra terms.
