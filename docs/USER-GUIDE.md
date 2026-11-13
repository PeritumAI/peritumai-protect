# PeritumAI Protect: full guide

The short overview is in [README.md](../README.md). This page lists every feature, the organization policy keys, and how to build from source.

Local Windows tray app. It checks clipboard text and files before you paste them into a chat, and it can write a redacted copy. The original file is not modified.

Rules-based. Offline. No account. No network in the app. This version does not catch everything.

## What it does

- Tray icon. Left click opens the window. Right click: Open, a Pause menu (15 minutes, 1 hour, Until I resume), Settings, Quit. A timed pause shows "Paused until 14:30" and resumes on its own. A pause until you resume shows one tray reminder after 8 hours.
- Start with Windows is on by default. It is a Startup-folder shortcut, so it does not need an administrator. Turning it off deletes that shortcut.
- Main window: Check, Activity, Restore, Settings, and Help. Themes: System, Light, and Dark. High contrast uses Windows colors.
- First run and Settings choose a profile: General, Lawyer, Clinic/Health, Accountant/Finance, HR, Developer, or Journalist. Each profile turns on a set of checks, can suggest My terms, and sets the default clipboard action. A company policy that sets the profile locks that choice. Lawyer, Clinic/Health, HR, and Accountant/Finance flag the identifier after a reference label such as Case No., Patient ID, Employee ID, or Client no. Invoice no. and Policy no. are on for Accountant/Finance only. There is no built-in matter-number shape. Add a number format from 2 to 5 examples. US federal and EU court formats are off until you turn them on under Checks.
- Clipboard watch. An email, a card, a key, or one of your own terms raises a warning when a target app is in front.
- Clipboard scanning reads plain text, file-drop paths, HTML, RTF, and pictures (Win+Shift+S, Print Screen, or a copied image). A picture is read with Windows OCR on this PC. **Redact** puts sanitized plain text and HTML back. **Redacted image to clipboard** covers the hits with black bars. A picture larger than 12 MB, wider or taller than 8000 pixels, or over 16 megapixels is **Not checked**. It is not reported as clean. The warning still waits until a target app is in front.
- Drop or browse a file. Each category opens to list the findings: type, masked value, where it is, and a short context line. **Why is this risky?** opens Details with a short explanation and one tip. A file that was not checked shows that explanation on the result. **Show** reveals the value for 10 seconds and does not log it. **Show all findings** lists them in order. **Preview** shows the extracted text with each finding highlighted, and stays masked until Show. Next, Previous, F3, and Shift+F3 move between findings. An image shows boxes over the found text. The original file is not opened for writing. **Make protected copy**, **Make reversible copy**, **Clean for sharing**, and **Close** stay pinned to the bottom of the result. The findings list scrolls above them. A reversible copy uses tokens you can restore on this PC. Secrets stay removed. After a copy exists, the card says it was saved and offers Open folder, Open file, and Copy path.
- Drop several files or a folder, or right-click a folder in Explorer and choose **Check with PeritumAI Protect**. The list shows one row per file. At most 500 files and 5 folder levels are checked in one run. Hidden and system files are skipped. **Make protected copies for all with findings** and **Make reversible copies for all** write new files. The original files are not changed. Cancel stops after the file that is already being checked.
- Copies go next to the original, or into a folder you choose in Settings. If that folder is missing or not writable, the copy is saved next to the original and the result says so.
- Formats that are checked: txt, md, csv, json, xml, log, html, htm, docx, docm, xlsx, xlsm, pptx, pptm, pdf, eml, zip, png, jpg, jpeg, bmp, gif, tif, tiff. webp is accepted when Windows can read it.
- **Clean for sharing** writes `{name}_clean{ext}` even when nothing sensitive was found. It removes document properties, custom properties, PDF info, image metadata and GPS, comments, tracked changes, hidden sheets, rows, columns, and text, off-slide shapes, speaker notes, macros, external links, data connections, embedded objects, and thumbnails. A macro file is saved as a macro-free `.docx`, `.xlsx`, or `.pptx`. Accept tracked changes is on unless you clear it. The original file is not changed.
- An `.eml` file is read on this PC: From, To, Cc, Subject, the message text, and attachments the app already knows how to check. HTML is turned into text. The protected copy is a new `.eml`. Attachments that cannot be checked are left out and named in the result and inside the copy.
- A `.zip` file is checked entry by entry, including a zip inside a zip, up to the same size limits. The protected copy is a new zip. Entries that cannot be checked, including encrypted ones, are left out and named. They are not copied as they are.
- PDF copies are new text-only PDFs plus a `.txt` sibling. Layout is not kept. A scanned PDF with no text layer is reported, not faked. Hidden PDF text is reported with the page and the reason: invisible text, white text, text under 2 pt, text outside the page, hidden optional content, an annotation, a form field, or document info. Clean for sharing leaves that text out.
- Images: Windows OCR on this PC. A new image gets black bars over the hits, plus a `.txt` sidecar with labels and counts. It never stores the matched values.
- **Export as AI bundle** writes one Markdown file with reversible tokens and a short header. The first value in the file is `[[EMAIL_1]]`. The same value keeps that token. Headings, lists, tables, slides, and PDF pages are sections in that file. The map stays on this PC.
- Window text, the tray menu, dialogs, Help, and the monthly report sentences come from `lang/en.json`. The language picker in Settings lists English only. `de.json` and `sr-Latn.json` stay in the repo and are not installed as choices. A saved setting that names one of those languages opens in English. Pseudo-localization and the right-to-left check stay in the developer list at the bottom of Settings. A missing key shows the English sentence. Restore can save the answer as text, Markdown, or Word. That file is a new document from the answer. The original layout is not kept.
- Restore puts values back from reversible tokens (`[[EMAIL_1]]`) using a local encrypted map. Paste an answer, or drop a `.txt`, `.md`, or `.docx` file. The restored file is a new sibling. The original is not overwritten. Secrets are not stored for restore. Maps are kept for 30 days unless Settings says otherwise. Delete restore maps removes them.
- My terms: exact text, regex, or a text file with one term per line. Settings can export rules and My terms to a JSON file, or import one. The file is checked and nothing in it is run. A duplicate id is reported and is not merged.
- Settings can add Scan and Make protected copy to the Explorer right-click menu for this user. Remove Explorer menu deletes those keys.
- Activity lists events newest first, grouped as Today, Yesterday, or the date. Trial stats use rule labels. The CSV export keeps the rule id and adds a label column. **Report PDF** and **Report CSV** write this month's counts: scans, warnings, redactions, reversible copies, stops, hidden-content findings, command warnings, incomplete scans, top categories, and the false-positive rate per rule. File names are left out unless **File names** is checked. The report is not a compliance claim. File paths are stored on this PC when **Remember file locations in Activity** is on (the default). Turn it off in Settings, or with the policy key `rememberFileLocations`. Delete activity log and Delete all data remove stored paths. Export log and Export stats leave paths out unless **Include file locations** is checked. A result can open the scanned file and show it in the folder. After a copy exists, the copy can be opened too. A list of files can export a findings CSV with paths, types, counts, and locations, never the values.
- Help shows the version and a link to the third-party notices. Link check explains what a suspicious link is, and that this is not an antivirus, an email filter, or a browser extension.
- Settings → Sites I use lets you add a site, remove it, import sites from a bookmarks HTML file you choose, or export the list. A company policy can add organization domains and can lock that list.

Screenshots: [`docs/screenshots/`](screenshots/) (`light-check`, `dark-check`, and the same for activity, restore, settings, help, and result).

## What it does not check

- It is not an antivirus, an email filter, or a browser extension. It does not catch every new phishing site, a link you open without copying, a QR code, or an attachment that is itself malicious.
- Serbia: JMBG (any valid 13-digit number, same check as before), PIB, and Serbian phone numbers. Croatia: OIB. Slovenia: EMŠO. Bosnia and Herzegovina: JMBG for registers 01 and 10–19. Montenegro: JMBG for registers 02 and 21–29. North Macedonia: EMBG. Germany: Steuer-ID and German phone numbers. France: NIR, including Corsica 2A and 2B. Spain: DNI and NIE. Italy: codice fiscale. Netherlands: BSN. Poland: PESEL. United Kingdom: National Insurance number and NHS number. United States: Social Security number and ITIN. Brazil: CPF and CNPJ. India: Aadhaar (Verhoeff) and PAN. JMBG stays on for every region. The other numbers turn on for that country, or when you enable them.
- IPv6 is not in the shipped packs. IPv4 is off unless you turn that rule on.
- Password-protected Office files and encrypted PDFs are **Not checked**. So are corrupt Office and PDF files.
- These types are not checked: `.doc`, `.xls`, `.ppt`, `.rtf`, `.odt`, `.msg`. `.msg` stays unchecked because the maintained reader pulls licenses this app does not ship. An encrypted file inside a zip is Not checked on its own; the rest of the zip is still checked.
- A file that expands past the limits (256 MB uncompressed, 64 MB per part, 10,000 parts, 100:1 compression, or packages nested more than 3 deep) is incomplete. No protected copy is written. PDFs stop after 2,000 pages or 15 seconds.

## Organization policy

The app reads policy at startup and when the file or the registry key changes. It never writes those places, and it does not use the network.

An administrator can deploy either source:

- Copy `policy.json` to `%ProgramData%\Peritum\Protect\` with Group Policy or Intune. Give administrators write access and leave users with read access.
- Or set the same names as values under `HKLM\Software\Policies\Peritum\Protect`.

If both are valid, a registry value replaces the same key from the file. Other keys from the file still apply. A rule id that appears in both a disable list and an enable list is rejected inside one source. Across the two sources, the registry list wins for that id.

Each value that is set is locked. Settings disables that control and shows a lock with "Set by your organization". `lockIgnore` set to 1 turns off "Don't warn for this value".

If a file or the registry values are not valid, that whole source is ignored. The app shows one warning: "Organization policy was ignored because the policy file is not valid." (or the registry, or both). It does not apply part of an invalid source, and it does not crash. Removing the policy restores the user's saved settings.

JSON keys, and the matching registry value names:

| Key | JSON | Registry |
| --- | --- | --- |
| profile | string id: `general`, `lawyer`, `clinic`, `accountant`, `hr`, `developer`, or `journalist` | REG_SZ |
| watchClipboard, watchCommands, watchCryptoSwap, onlyWhenTargetFocused, lockIgnore, rememberFileLocations | true or false. `rememberFileLocations` defaults to on. A policy value of false stops Activity from storing file paths. | REG_DWORD 0 or 1 |
| defaultPasteAction | `ask`, `ignore`, `stop`, or `redact` | REG_SZ |
| stopBehavior | `restore-previous` or `clear-clipboard` | REG_SZ |
| redactionMode | `reversible` or `permanent` | REG_SZ |
| minHitsForPopup | integer from 1 to 100 | REG_DWORD |
| disabledRuleIds, enabledRuleIds | array of rule ids | REG_MULTI_SZ |
| numberFormats | array of `{name, pattern, category, reversible}` taught number formats. `category` is personal, payment, secrets, or custom. | not a registry value |

Unknown names, nulls, and a file larger than 64 KB are not valid. Example:

```json
{
  "profile": "lawyer",
  "watchClipboard": true,
  "lockIgnore": true,
  "defaultPasteAction": "stop",
  "disabledRuleIds": ["pii.ipv4"]
}
```

## Data

Data stays in `%LOCALAPPDATA%\Peritum\Protect\` unless `PERITUM_PROTECT_DATA` is set. The screenshot script sets that variable so it does not write your activity log.

Settings → Data can delete the activity log, the "don't warn" list, restore maps, or everything in that folder except `settings.json`. The delete-all confirmation names each file.

## Icons

The window, the program file, and the Startup shortcut use AppUserModelID `Peritum.Protect`. After an update, Windows may still show a cached taskbar icon until you refresh the icon cache (`ie4uinit.exe -show`) or sign out and back in.

Regenerate the icon files with `tools\make-icons.ps1` after a build. Tests do not write those files.

## Build and run

Install the .NET 10 SDK (x64). This repo pins the 10.0 feature band in `global.json` (`rollForward: latestFeature`). Builds use `C:\Program Files\dotnet\dotnet.exe`.

```powershell
cd peritum-protect
$env:DOTNET_CLI_TELEMETRY_OPTOUT = "1"
dotnet test PeritumProtect.sln
dotnet run --project src\Peritum.Protect.App
```

The app starts in the tray. The first launch shows a short notice. Continue leaves it running. Quit exits.

## Publish

Framework-dependent, for a PC that already has the .NET 10 Desktop Runtime. That folder was 39,747,920 bytes here. The exe was 184,832 bytes.

```powershell
dotnet publish src\Peritum.Protect.App -c Release /p:PublishProfile=FrameworkDependent-win-x64
```

Self-contained single-file, for a PC without .NET installed:

```powershell
dotnet publish src\Peritum.Protect.App -c Release /p:PublishProfile=SelfContained-win-x64
dotnet publish src\Peritum.Protect.App -c Release /p:PublishProfile=SelfContained-win-arm64
```

Self-contained profiles set `EnableCompressionInSingleFile`. WinForms stays, because the tray icon and the folder picker use it. Measured here with the x64 .NET 10 SDK: win-x64 `Peritum.Protect.exe` is 85,575,038 bytes. win-arm64 `Peritum.Protect.exe` is 81,319,560 bytes. The win-x64 exe, copied alone into an empty folder, scanned `fixtures\secrets.txt` and logged one email and one OpenAI-style key.

`LICENSE`, `NOTICE`, `SECURITY.md`, and `THIRD-PARTY-NOTICES.md` are copied next to the app. Report a vulnerability privately to security@peritumai.com (see [SECURITY.md](../SECURITY.md)).

## Try the clipboard

1. Open Settings.
2. For a quick test on this PC, turn off **Only when an AI app or browser is in front**. Save.
3. Copy this line:

```text
Contact ada@example.com about the invoice. The test key is sk-testFAKEkey1234567890.
```

4. The warning lists Email and OpenAI-style key.
5. **Redact**, then paste into Notepad. You should see a reversible email token and a removed secret.

## Try a file

Drag `fixtures\secrets.txt` onto the window. **Make protected copy** writes a sibling such as `secrets_protect.txt` unless Settings says to use another folder. The original text is still in the source file.

From Explorer, after **Add Explorer menu** in Settings:

```text
Peritum.Protect.exe --scan "C:\path\Invoice.docx"
Peritum.Protect.exe --protect "C:\path\Invoice.docx"
```

## Not in version 1.0

Screenshot monitoring, browser extensions, cloud or team services, and automatic updates are not part of this version. Help can open https://peritumai.com/protect in the browser. The app itself makes no network request and works offline. If the build is more than 180 days old, Check shows one quiet line with the same link, so you know to look for a newer version.

The Windows installer and the release build are described in [RELEASING.md](RELEASING.md).
