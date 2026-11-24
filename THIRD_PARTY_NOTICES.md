<!-- SPDX-License-Identifier: AGPL-3.0-or-later -->
# Third-party notices

EQLMaker is licensed under AGPL-3.0-or-later, Copyright (c) 2026 Robert Binsack.

The engine itself (`eqlmaker.exe`, `eqlmaker-timestamp.exe`, the tests and the sample desktop
program) contains no third-party code: it is built from the sources in `src/`, `tests/` and
`examples/` with the C# compiler and the class library of the .NET Framework that are part of
Windows. No package manager and no library of another author is involved. That includes the
PostgreSQL client, the JSON reader, the PDF reader and the video writer (which calls Media
Foundation of Windows).

Everything that is not written by the author is listed here.

## In the source repository

| What | Where | Version | Licence | How it is delivered |
|---|---|---|---|---|
| Icons of the Lucide icon set (12 icons as inline SVG paths) | `src/templates/web/public/app.js`, copied into `examples/sample-app/web/public/app.js` and into generated web applications | – | ISC | in the repository; list of the icons in `src/templates/web/ICONS.md` |
| npm package `pg` and its 13 dependencies | named in `src/templates/web/package-lock.json`, `src/templates/clone/package-lock.json` and `examples/sample-app/web/package-lock.json` | see below | MIT, ISC | not in the repository; installed by `npm ci` from the npm registry, or from the package archives of the release package |

No font file is part of the repository: the web template uses the fonts of the system.

## In the Windows release package, or downloaded by its setup

None of these is in the source repository. Addresses, sizes and SHA-256 checksums are pinned in
`scripts/setup/components.json`; every file is checked against its checksum before it is used.
The notes on each component (what is unpacked, what is bundled inside) are in
[`scripts/setup/COMPONENTS.md`](scripts/setup/COMPONENTS.md).

| Component | Version | Licence (SPDX) | How it is delivered | Licence text |
|---|---|---|---|---|
| whisper.cpp (`whisper-cli.exe`), built from the source archive with a statically linked run-time; contains ggml (MIT), miniaudio (public domain or MIT-0), stb_vorbis (public domain or MIT), tinyBLAS (MIT) | 1.9.6 | MIT | inside the release zip | `tools\whisper\LICENSE` |
| Whisper model `ggml-small-q5_1.bin` (weights published by OpenAI, converted by the whisper.cpp project) | small-q5_1 | MIT | inside the release zip | `tools\whisper\models\LICENSE` |
| Larger Whisper models | see `components.json` | MIT | downloaded by the setup on request (`-model <name>`) | `tools\whisper\models\LICENSE` |
| FFmpeg, LGPL build with shared libraries | 9.0.2 | LGPL-3.0-or-later | downloaded by the setup from the build provider; not redistributed by EQLMaker | `tools\ffmpeg\LICENSE.txt` |
| Node.js with npm | 26.13.1 | MIT (Node.js), Artistic-2.0 (npm) | downloaded by the setup | `tools\node\LICENSE` |
| PostgreSQL, Windows x64 binaries (optional) | 18.4 | PostgreSQL | downloaded by the setup | `tools\pgsql\server_license.txt` |
| npm package `pg` and its dependencies, as package archives | pg 8.23.1 | MIT, ISC | inside the release zip (`downloads\npm`) | in every archive, and below |
| Microsoft Visual C++ run-time libraries, only in packages built with `-IncludeVcRuntime` | of the build tools used | Microsoft redistributable terms (not open source) | inside such a zip only | `tools\vcruntime\LICENSE.txt` |

EQLMaker starts FFmpeg, the speech recogniser, Node.js and PostgreSQL as separate programs. It
is not linked with any of them.

When EQLMaker is used with EQLServer in place of PostgreSQL, that server is a separate
commercial product with its own licence; no part of it is in this repository or in the package.

## npm packages

| Package | Version | Licence | Copyright |
|---|---|---|---|
| pg | 8.23.1 | MIT | Copyright (c) 2010 - 2021 Brian Carlson |
| pg-cloudflare | 1.4.1 | MIT | Copyright (c) 2010 - 2021 Brian Carlson |
| pg-connection-string | 2.14.1 | MIT | Copyright (c) 2014 Iced Development |
| pg-int8 | 1.0.1 | ISC | Copyright © 2017, Charmander |
| pg-pool | 3.14.0 | MIT | Copyright (c) 2017 Brian M. Carlson |
| pg-protocol | 1.16.1 | MIT | Copyright (c) 2010 - 2021 Brian Carlson |
| pg-types | 2.2.0 | MIT | Brian M. Carlson (author named in the package) |
| pgpass | 1.0.5 | MIT | Hannes Hörl (author named in the package) |
| postgres-array | 2.0.0 | MIT | Copyright (c) Ben Drucker |
| postgres-bytea | 1.0.1 | MIT | Copyright (c) Ben Drucker |
| postgres-date | 1.0.7 | MIT | Copyright (c) Ben Drucker |
| postgres-interval | 1.2.0 | MIT | Copyright (c) Ben Drucker |
| split2 | 4.2.0 | ISC | Copyright (c) 2014-2018, Matteo Collina |
| xtend | 4.0.2 | MIT | Copyright (c) 2012-2014 Raynos |

## Licence texts

### MIT License

Applies to the packages marked MIT above, each with its own copyright line.

```text
Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

### ISC License

Applies to `pg-int8` and `split2`, each with its own copyright line, and to the Lucide icons.

```text
Permission to use, copy, modify, and/or distribute this software for any
purpose with or without fee is hereby granted, provided that the above
copyright notice and this permission notice appear in all copies.

THE SOFTWARE IS PROVIDED "AS IS" AND THE AUTHOR DISCLAIMS ALL WARRANTIES WITH
REGARD TO THIS SOFTWARE INCLUDING ALL IMPLIED WARRANTIES OF MERCHANTABILITY AND
FITNESS. IN NO EVENT SHALL THE AUTHOR BE LIABLE FOR ANY SPECIAL, DIRECT,
INDIRECT, OR CONSEQUENTIAL DAMAGES OR ANY DAMAGES WHATSOEVER RESULTING FROM
LOSS OF USE, DATA OR PROFITS, WHETHER IN AN ACTION OF CONTRACT, NEGLIGENCE OR
OTHER TORTIOUS ACTION, ARISING OUT OF OR IN CONNECTION WITH THE USE OR
PERFORMANCE OF THIS SOFTWARE.
```

### Lucide icons

```text
ISC License

Copyright (c) for portions of Lucide are held by Cole Bemis 2013-2022 as part of Feather (MIT).
All other copyright (c) for Lucide are held by Lucide Contributors 2022.
```

The permission notice is the ISC text above; Feather, where portions come from, is under the
MIT text above.

### Other licences

The texts of the LGPL (FFmpeg), of the PostgreSQL licence, of the Node.js licence file with the
licences of its bundled parts, and of the whisper.cpp and model licences are long and belong to
files that are not in this repository. They are delivered with those files, at the places named
in the table above.

## Texts and standards

- `LICENSE`: GNU Affero General Public License version 3, Copyright (C) 2007 Free Software
  Foundation, Inc., verbatim.
- `DCO.md` contains the Developer Certificate of Origin 1.1, Copyright (C) 2004, 2006 The Linux
  Foundation and its contributors, verbatim.
- `CODE_OF_CONDUCT.md` is the Contributor Covenant 2.1, licensed under CC BY 4.0.
