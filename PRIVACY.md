# Privacy

PeritumAI Protect runs entirely on your PC.

- **No network.** The app makes no network requests: no telemetry, no analytics, no update checks, no AI service, no
  cloud lookups for links. Links in Help open in your browser only when you click them.
- **No account.** Nothing to sign up for.
- **Reading the clipboard.** While clipboard watch is on, the app reads what you copy so it can check it. Text and
  pictures are checked in memory on your PC and are not saved. Pictures are read with the Windows OCR engine on your PC.

## What the app stores, and where

Everything is in `%LOCALAPPDATA%\Peritum\Protect\` (you can delete it from Settings → Data):

| What | File |
| --- | --- |
| Settings | `settings.json` |
| Activity: date, rule names and counts, never the values found. File paths too while **Remember file locations in Activity** is on (the default); turn it off in Settings. | `events-YYYY-MM.jsonl` |
| "Don't warn for this value" list, stored as keyed hashes, not as plain values | `allowlist.json` and its key |
| Restore maps for reversible copies, encrypted for your Windows account, deleted after 30 days by default. Secrets are never stored. | `maps\` |
| Sites I use (for the link check), encrypted for your Windows account | in this folder |
| Your own rules and terms | `rules\` |
| Error log | `logs\` |

The app may also create, when you turn on the matching setting:

- a shortcut in your Startup folder (**Start with Windows**),
- right-click menu entries under `HKEY_CURRENT_USER\Software\Classes` (**Explorer menu**).

Turning the setting off removes them. Your original files are never changed; protected copies are new files where you
choose to save them.

## Organizations

An administrator can set policy through `%ProgramData%\Peritum\Protect\policy.json` or
`HKLM\Software\Policies\Peritum\Protect`. The app only reads those places. Exported reports leave out file names and
paths unless you include them.

Questions: danilo@peritumai.com.
