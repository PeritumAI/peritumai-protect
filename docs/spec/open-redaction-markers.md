# Open Redaction Markers v0.1 (draft)

A small convention for text that has had sensitive values replaced by stable tokens.

## Token

`[[TYPE_N]]`

- TYPE is uppercase ASCII letters, digits, or underscore: EMAIL, PHONE, NAME, CLIENT, ORG, IBAN, CARD, SECRET, ID, DATE, AMOUNT, CUSTOM_LABEL.
- N is a positive integer, consistent inside one bundle.
- `[[SECRET_REMOVED]]` is permanent. It has no map entry.

## Header

Place this at the top of a bundle given to an AI:

```
Peritum markers: keep every [[TOKEN]] exactly. Do not guess hidden values. Do not merge or renumber tokens.
Peritum map: 7F3K-92QA
```

## Map

The map is JSON `{ id, created, tokens }` and is a secret. Implementations encrypt it at rest. It is never placed next to the shared file and never sent to the AI. The bundle only names the id.

## Bundle

An AI bundle is one Markdown file. Headings stay headings. Lists stay lists. A spreadsheet or a Word table becomes a Markdown table. Each slide is its own section. Each PDF page is its own section. Tokens are reversible. The header block is at the top. The map stays on the machine that created the bundle.

## Import

An answer is restored on that machine and saved as a new `.txt`, `.md`, or `.docx`. The Word file maps headings, paragraphs, lists, tables, bold, and italic. It is a new document from the answer. The original layout is not promised.

## Restore

Accept extra spaces, single brackets, lowercase, and markdown escapes such as `\[\[EMAIL\_1\]\]`. Report tokens with no map entry, and map entries the answer never used.

## Security

Token frequency can still leak information. Numbering may be random. Maps are secrets. Secrets are never reversible.
