# What Peritum Protect does

This note says what the current build catches and what it does not. It is not a compliance claim.

## Organization policy

What it does: at startup, and when the file or registry key changes, the app reads `%ProgramData%\Peritum\Protect\policy.json` and `HKLM\Software\Policies\Peritum\Protect`. Policy overrides the user's settings. Locked controls are disabled and labeled "Set by your organization". An invalid file or registry set is ignored as a whole, with one warning.

What it does not do: it does not download policy, write the policy file, or apply part of an invalid source. It does not change the original document.

## Profiles

What it does: first run and Settings offer General, Lawyer, Clinic/Health, Accountant/Finance, HR, Developer, and Journalist. Each profile turns on rule ids that exist, can suggest My terms, and sets the default clipboard action. Developer includes every secret rule and the high-entropy check. Clinic includes a labeled health-record pattern and date of birth. HR includes salary, JMBG, and IBAN. Lawyer, Clinic, HR, and Accountant/Finance flag the identifier that follows a reference label. Invoice and policy labels are on for Accountant/Finance only. A company policy that locks the profile wins. You can add a number format from examples. US federal and EU court number checks are off until you turn them on.

What it does not do: it does not guess a matter-number shape. `MAT-year-number` is only an example in Add a number format. It does not turn on court formats by itself. It does not flag a number that has no reference label, and it does not flag the words after a label when they are not an identifier.

What it does not do: it does not download profiles. It does not detect names, street addresses, or most national IDs unless a rule or a My term covers them. Sample client, patient, and source names are examples until you replace them. A date with no "date of birth" or "DOB" label is not treated as a date of birth. A locked profile cannot be changed.

## Rule import and export

What it does: Settings can save your rule packs and My terms as a JSON file, and can read that file back. The file is checked first. A duplicate id is named in the message, and that import is not merged. Imported packs are stored on this PC under the data folder.

What it does not do: it does not run patterns, scripts, or anything else in the file. It does not replace a built-in rule pack. It does not upload the file.

## Reversible file copies

What it does: Make reversible copy writes numbered tokens such as `[[EMAIL_1]]` and a `Peritum map:` line. The same value in one file gets the same token. You can restore those tokens on this PC. Secrets are written as `[[SECRET_REMOVED]]` and are not stored. The original file is not changed. The map is encrypted on this PC and is not placed next to the copy.

What it does not do: it does not make secrets restorable. It does not promise the original layout for a PDF. An image copy still covers hits with black bars. It does not upload the map.

## Restore from a file

What it does: Restore reads pasted text or a `.txt`, `.md`, or `.docx` file that contains tokens and a `Peritum map:` line. It writes a new sibling file with the values put back. It tolerates extra spaces, single brackets, lowercase, and markdown escapes around a token. A map older than the Settings value (30 days unless you change it) is refused. Delete restore maps removes the maps on this PC. The result uses singular or plural ("1 token was not in the answer", "2 tokens were not in the map") and the Details list names those tokens.

What it does not do: it does not overwrite the file you opened. It does not restore a secret. It does not restore a token that was never in the map, and it says so. It does not accept other file types for restore.

## Email files

What it does: an `.eml` file is read on this PC. From, To, Cc, and Subject are checked, and so are the plain-text body and HTML turned into text. Attachments the app already supports (text, Office, PDF, and images when OCR is on) are checked with the same size limits as other packages. A nested package counts toward the limit of 3 levels. Make protected copy writes a new `.eml`. Headers and text are redacted. Supported attachments are replaced with protected copies. An attachment that cannot be checked is left out, named on the result, and named in a short note inside the copy.

What it does not do: it does not keep the original HTML layout. It does not copy headers other than From, To, Cc, and Subject. It does not read `.msg` Outlook files. It does not read `.doc`, `.xls`, `.ppt`, `.rtf`, or `.odt`, including when they are attached. An image attachment is removed and listed when OCR is off. A message that is not valid mail is Not checked. An attachment past the package limits makes the whole message incomplete, and no protected copy is written.

## Zip archives

What it does: a `.zip` is checked on this PC, one entry at a time, with the same readers used for loose files. A zip inside a zip counts toward the limit of 3 nested packages. Make protected copy writes a new zip. Entries this version can check are replaced with protected copies. An entry that cannot be checked is left out and named on the result. An encrypted entry is Not checked and left out. The original zip is not changed.

What it does not do: it does not copy an entry it could not check. It does not decrypt encrypted entries. It does not check `.doc`, `.xls`, `.ppt`, `.rtf`, `.odt`, or `.msg` inside a zip. A zip that expands past the size limits is incomplete, and no protected copy is written.

## Several files or a folder

What it does: you can drop several files or a folder, or right-click a folder in Explorer and choose Check with Peritum Protect. The list has one row per file, with the name, type, and status, and Details opens the usual result for that file. A run checks at most 500 files and does not go deeper than 5 folder levels. It says so when it stops. Hidden and system files are skipped and counted. **Make protected copies for all with findings** writes a protected copy of each file that had a finding. **Make reversible copies for all** writes a reversible copy of each file that was checked. Cancel finishes the current file and then stops. Files are checked one at a time on this PC.

What it does not do: it does not change the original files. It does not check hidden or system files. It does not follow a folder link. It does not copy a file that was Not checked or incomplete. It does not check more than 500 files or deeper than 5 folder levels in one run.

## Clipboard pictures and rich clipboard

What it does: a copied picture is read with Windows OCR on this PC, including a snip or a print-screen. The same rules run on that text. Redacted image to clipboard covers each hit with a black bar. HTML and RTF on the clipboard are read too. The visible words are scanned. Hidden HTML (comments, text that is not displayed, off-screen text, and alt or title text) is reported, including a phrase addressed to an AI. Redact writes plain text and a cleaned HTML version. The warning still appears only when a target app is in front, unless that option is off.

What it does not do: it does not read a picture larger than 12 MB, larger than 8000 pixels on a side, or over 16 megapixels. That picture is Not checked, and it is not called clean. It does not upload the picture. It does not change the clipboard until you choose Redact or Stop. It does not catch a clever instruction that is ordinary visible text, beyond the phrase list.

## Hidden text in PDFs

What it does: a PDF result names the page and why text is hidden. That includes invisible text, white or near-white text, fonts under 2 pt, text outside the page, hidden optional content, annotations, form fields, document info, and XMP. Clean for sharing writes a text-only PDF without that text. The original PDF is not changed.

What it does not do: it does not keep the original layout. It does not read a scanned page that has no text layer. It does not treat ordinary visible text as hidden. Near-white means a fill at or above 0.9 on each channel. Optional content is reported when PdfPig returns it.

## Clean for sharing

What it does: Clean for sharing writes a new file named `{name}_clean` with the same extension, or `.docx` / `.xlsx` / `.pptx` when the original is a macro file. It can run when nothing sensitive was found. It removes document properties, custom properties, the template path, PDF info and XMP, image metadata including GPS, comments, tracked changes, hidden text, hidden sheets, hidden rows and columns, shapes that sit off the slide, speaker notes, macros, external links, data connections, embedded objects, thumbnails, and hidden HTML. The result lists each thing that was removed. Accept tracked changes keeps insertions and drops deletions. Clear it to reject the changes instead. The original file is not changed.

What it does not do: it does not redact visible text. Use Make protected copy for that. It does not keep the original PDF layout. It does not open or save `.doc`, `.xls`, or `.ppt`. A JPEG saved again may contain encoder tables; the original EXIF and GPS are not copied.

## Newer version

What it does: Help shows the version, the build date, and how many days old that build is. Check for a newer version opens https://peritumai.com/protect in the browser. If the build is more than 180 days old, Check shows one quiet line with the same link.

What it does not do: it does not download an update. It does not contact the site itself. It does not stop working when the build is old.

## AI bundle

What it does: Export as AI bundle writes one Markdown file. Word headings and lists stay headings and lists. A spreadsheet or a Word table becomes a Markdown table. Each slide and each PDF page is its own section. Sensitive values become reversible tokens. A header tells the chat to keep the tokens. The map stays on this PC. An answer can be saved as a new `.txt`, `.md`, or `.docx`. The Word file keeps headings, paragraphs, lists, tables, bold, and italic.

What it does not do: it does not keep the original layout. It does not promise the answer looks like the file you started with. It does not put the map in the bundle. It does not make a secret restorable.

## Monthly report

What it does: Activity can write this month's counts as a PDF or a CSV. The PDF has the period, scans, warnings, redactions, reversible copies, stops, hidden-content findings, command warnings, incomplete scans, top categories, the false-positive rate per rule, a short "what this means" note, three to five suggested actions, and a line that this is not a compliance claim. The footer says Peritum Protect — peritumai.com. File names are included only when File names is checked.

What it does not do: it does not include matched values. It does not include file paths. It does not claim GDPR or EU AI Act compliance. It does not send the report anywhere.

## Why is this risky?

What it does: every built-in rule, every hidden-content type, the command warning, the wallet-address warning, and every not-checked or incomplete reason has one or two plain sentences and one tip. The words are in `lang/en.json`. Details on a result shows them for each finding. A result that was not checked, or that stopped early, shows the matching explanation on the card. The command warning and the wallet-address warning include the tip.

What it does not do: it does not explain a My term you wrote yourself. It does not decide that a finding is really sensitive. It does not send the text anywhere. Other languages are not translated yet.

## Link check

What it does: a copied link is checked on this PC for tricks: a lookalike of a known name, a brand on someone else's site, text before @, a dangerous scheme, a numeric address, and a risky download. An email or a document also checks whether the visible words and the link disagree, and whether the sender name matches the address. Safe Links and similar wrappers are opened on this PC before the check. A short link or an unusual port is noted and does not warn by itself. Settings → Sites I use can add a site, remove it, import a bookmarks HTML file you choose, or export the list. Warn about suspicious links is off until you, a profile, or a policy turns it on. The public-suffix list used for the site name is dated 2026-10-01.

What it does not do: it does not know whether a new site is a phishing site. It does not look up DNS, follow a redirect, or open the network. It does not see a link you click without copying, a QR code, or an attachment that is itself malicious. It is not an antivirus, an email filter, or a browser extension.

## Checks in this build

The app checks clipboard text and supported files for emails, phones, cards, IBANs, keys, terms you add, and the country identity numbers that are turned on. Serbia covers JMBG, PIB, and Serbian phone numbers. Croatia covers OIB. Slovenia covers EMŠO. Bosnia and Herzegovina and Montenegro cover their JMBG registers. North Macedonia covers EMBG. Germany covers Steuer-ID and German phone numbers. France covers NIR. Spain covers DNI and NIE. Italy covers the codice fiscale. The Netherlands covers BSN. Poland covers PESEL. The United Kingdom covers the National Insurance number and the NHS number. The United States covers the Social Security number and the ITIN. Brazil covers CPF and CNPJ. India covers Aadhaar, with the Verhoeff check, and PAN. JMBG stays on everywhere. The other country checks follow the Windows region or a profile, not the language of the window. JMBG, EMŠO, EMBG, PESEL, the codice fiscale, DNI, and NIE are found without a label. A printed Social Security number, ITIN, NHS number, CPF, CNPJ, Aadhaar number, Steuer-ID, BSN, NIR, or National Insurance number is found without a label. An unformatted SSN, ITIN, PIB, BSN, OIB, NHS number, Steuer-ID, National Insurance number, CPF, CNPJ, or Aadhaar needs a nearby label such as SSN, PIB, BSN, OIB, NHS, Steuer-ID, CPF, CNPJ, or NI number. The app does not detect names or street addresses unless you add them. It does not check `.doc`, `.xls`, `.ppt`, `.rtf`, `.odt`, or `.msg`. It does check `.eml` and `.zip`. Password-protected Office files and encrypted PDFs are Not checked. An encrypted entry inside a zip is Not checked; the other entries are still checked. Not covered: other countries, and identity numbers that are not in this list. A date, an order number, or a phone number from another country is not treated as one of these IDs.
