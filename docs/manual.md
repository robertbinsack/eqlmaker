<!-- SPDX-License-Identifier: AGPL-3.0-or-later -->
[English](manual.md) · [Deutsch](de/manual.md)

# Manual

`eqlmaker manual` takes the manual of the original application over as context of the project:
its text and its chapters. The specification then names, for every step and mask, the chapters
that belong to it.

```text
eqlmaker manual <file> [--name <name>]
eqlmaker spec
```

Run `spec` again afterwards; it makes the links. Several manuals can be taken over, each under
its own name (default: the file name).

## Formats

| Format | How chapters are found |
|---|---|
| Plain text (`.txt`) | numbered headings (`2.1 Invoices`), underlined headings, headings in capital letters |
| Markdown (`.md`) | `#` headings |
| HTML (`.html`, `.htm`) | `h1` to `h6` |
| Word (`.docx`) | heading styles and outline levels of the paragraphs |
| PDF (`.pdf`) | headings by font size, see below |

No other program is needed: the readers for DOCX and PDF are part of EQLMaker.

## What is extracted

`manual/<name>.json`:

| Key | Content |
|---|---|
| `format`, `version` | `eqlmaker-manual` and the format version |
| `name`, `title` | name in the project; title of the document |
| `source_file`, `source_type` | file name and format of the original |
| `characters` | length of the text |
| `chapters` | list of chapters: `id` (`c1`, `c2`, ...), `level`, `title`, `page` (PDF only), `text` |
| `warnings` | what could not be read |

`manual/<name>.txt` is the whole text, for reading and for the guided steps (`generate` copies
it into `web/spec/`).

Text before the first heading becomes a chapter with the title `(start)`.

Pictures, tables as tables, and the layout of the manual are not extracted. Only text and
headings are.

## How chapters are linked

`spec` compares the words of a chapter with the words of a step and of its masks. A shared word in the chapter title counts 3 points, in the chapter text 1; a
link needs at least 3 points. The rule is fixed and gives the same links for the same input.
A manual in another language than the user interface of the application therefore gets few
links.

## The PDF reader

The built-in reader handles what text PDFs usually contain:

- compressed content streams (Flate) and object streams;
- character maps (`ToUnicode`) and the standard encodings;
- glyph widths, to tell where a word ends;
- headings by font size: text set larger than the body text becomes a heading, the larger the
  higher the level.

Its limits:

- **Encrypted PDFs are refused.** Remove the protection first, if you may.
- **Scanned PDFs give no text.** A scan contains pictures of pages. The result has a warning;
  EQLMaker has no text recognition for pictures.
- **Fonts without a character map** cannot be read; the result has a warning for such text.
- Text in columns, in tables or set along a path may come out in another order than a reader
  would expect.
- Headings that are not larger than the body text are not found. If a PDF gives few chapters,
  export the manual as DOCX or HTML when you have the source.

How it is tested: with PDFs built for the tests (compressed, with character map and object
stream; with a standard font and headings by size) and with a PDF printed by a browser. Open
`manual/<name>.txt` after an import and check that the text is what you expect.
