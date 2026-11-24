<!-- SPDX-License-Identifier: AGPL-3.0-or-later -->
<!-- Copyright (c) 2026 Robert Binsack -->
# Third-party components of the Windows package

EQLMaker itself is licensed under AGPL-3.0-or-later. The package uses the components below.
None of them is in the source repository: `scripts/pack-release.ps1` fetches the ones that are
inside the release zip, `eqlmaker-setup.cmd` downloads the others into the package folder on
the user's computer. The exact addresses, sizes and SHA-256 checksums are in
`components.json` (in the package: `setup\components.json`); every file is checked against
its checksum before it is used.

| Component | Version | Source | Licence (SPDX) | Delivery | Licence text in the package |
|---|---|---|---|---|---|
| whisper.cpp (`whisper-cli.exe`, built from the source archive, C/C++ run-time linked statically) | 1.9.6 | https://github.com/ggml-org/whisper.cpp/archive/refs/tags/v1.9.6.tar.gz | MIT | in the zip | `tools\whisper\LICENSE` |
| Whisper model `ggml-small-q5_1.bin` (multilingual, quantised) | small-q5_1 | https://huggingface.co/ggerganov/whisper.cpp (fixed revision, see `components.json`) | MIT | in the zip | `tools\whisper\models\LICENSE` |
| Larger Whisper models (`-model <name>`) | see `components.json` | https://huggingface.co/ggerganov/whisper.cpp | MIT | downloaded by setup on request | `tools\whisper\models\LICENSE` |
| FFmpeg, LGPL build with shared libraries | 9.0.2 (n9.0.2-39-g7adfbe40dc, build of 2026-10-31) | https://github.com/BtbN/FFmpeg-Builds/releases/tag/autobuild-2026-10-31-12-59 | LGPL-3.0-or-later | downloaded by setup | `tools\ffmpeg\LICENSE.txt` |
| Node.js (LTS) with npm | 26.13.1 | https://nodejs.org/dist/v26.13.1/node-v26.13.1-win-x64.zip | MIT (Node.js), Artistic-2.0 (npm), see below | downloaded by setup | `tools\node\LICENSE`, `tools\node\node_modules\npm\LICENSE` |
| PostgreSQL, Windows x64 binaries of EnterpriseDB (optional) | 18.4 | https://get.enterprisedb.com/postgresql/postgresql-18.4-1-windows-x64-binaries.zip | PostgreSQL | downloaded by setup | `tools\pgsql\server_license.txt`, `tools\pgsql\commandlinetools_3rd_party_licenses.txt` |
| `pg` and its dependencies (npm packages of the browser sample and of generated web applications) | pg 8.23.1, see the table below | https://registry.npmjs.org | MIT, ISC | in the zip (`downloads\npm\*.tgz`) | in every package archive; after the setup in `examples\sample-app\web\node_modules\<name>\LICENSE` |
| Microsoft Visual C++ run-time libraries (only when the package was built with `-IncludeVcRuntime`) | of the build tools used | Visual Studio build tools, folder `VC\Redist` | Microsoft Visual Studio redistributable terms (not open source) | in the zip, only in such builds | `tools\vcruntime\LICENSE.txt` |

## Notes on the single components

### whisper.cpp
Built by `scripts/setup/build-whisper.ps1` from the pinned source archive: CPU only, AVX2
instruction set, no OpenMP, static C/C++ run-time. The program imports only `KERNEL32.dll`
and `ADVAPI32.dll` (the build fails otherwise; the list is written to `MANIFEST.json`).
Code of other authors that is compiled into the program, all under permissive terms:

- ggml (part of the whisper.cpp sources), MIT
- miniaudio (audio file decoding), public domain or MIT-0
- stb_vorbis (inside miniaudio's decoding path), public domain or MIT
- tinyBLAS of llamafile (`ggml/src/ggml-cpu/llamafile`), MIT

### Whisper model
The model weights were published by OpenAI under the MIT licence
(https://github.com/openai/whisper); the conversion to the ggml format is from the whisper.cpp
project. The licence text of OpenAI Whisper is shipped next to the model.

### FFmpeg
The build is the "lgpl" variant of the BtbN builds. Evidence: `ffmpeg -version` prints the
configuration; it contains `--enable-version3` and neither `--enable-gpl` nor
`--enable-nonfree`, and x264, x265, xvid, fdk-aac, avisynth, frei0r, rubberband and vidstab are
disabled. The setup checks this line after unpacking and refuses a build that names GPL or
non-free parts. Because of `--enable-version3` the build as a whole is LGPL version 3 or later.
Only `ffmpeg.exe`, `ffprobe.exe`, the FFmpeg libraries (`av*.dll`, `sw*.dll`) and `LICENSE.txt`
are unpacked. Libraries linked into the build (from its configuration): zlib (Zlib), libxml2
(MIT), fontconfig, freetype (FTL), harfbuzz (MIT), fribidi (LGPL-2.1), lcms2 (MIT), dav1d
(BSD-2-Clause), libvorbis, libtheora, libopus (BSD-3-Clause), rav1e (BSD-2-Clause), librsvg
(LGPL-2.1), gmp (LGPL-3.0), lzma (0BSD), libaom (BSD-2-Clause), chromaprint (LGPL-2.1), libgme
(LGPL-2.1), kvazaar (BSD-3-Clause), libaribb24 (LGPL-3.0), libass (ISC), libbluray (LGPL-2.1),
libjxl (BSD-3-Clause), libmp3lame (LGPL-2.0), libplacebo (LGPL-2.1), librist (BSD-2-Clause),
libssh (LGPL-2.1), libvpx (BSD-3-Clause), libwebp (BSD-3-Clause), libzmq (MPL-2.0), openal-soft
(LGPL-2.0), opencore-amr (Apache-2.0), openh264 (BSD-2-Clause), openjpeg (BSD-2-Clause),
libopenmpt (BSD-3-Clause), SDL2 (Zlib), snappy (BSD-3-Clause), soxr (LGPL-2.1), srt (MPL-2.0),
SVT-AV1 (BSD-3-Clause-Clear), twolame (LGPL-2.1), uavs3d (BSD-3-Clause), vvenc
(BSD-3-Clause-Clear), zimg (WTFPL), zvbi (LGPL-2.0), libvmaf (BSD-2-Clause-Patent), and others
named in the configuration line. The authoritative list is the build provider's:
https://github.com/BtbN/FFmpeg-Builds. The source code of this build is available from the
provider and from https://ffmpeg.org (tag n9.0.2 plus the commits named in the version).
The archive is not redistributed by EQLMaker; it is downloaded from the provider.

### Node.js
Node.js is MIT licensed. Its `LICENSE` file also holds the licences of the bundled parts,
among them V8 (BSD-3-Clause), OpenSSL (Apache-2.0), ICU (Unicode-3.0), libuv (MIT), zlib (Zlib),
c-ares (MIT), nghttp2 (MIT), llhttp (MIT), and npm. npm is licensed under Artistic-2.0, a
permissive licence that is not in the usual short list; it is used unchanged as a tool and is
not linked into EQLMaker or into generated applications. `install_tools.bat` of the archive is
not unpacked.

### PostgreSQL
PostgreSQL is released under the PostgreSQL licence, a permissive licence similar to BSD and
MIT. Only the server and client parts are unpacked (`bin`, `lib`, `share` without the graphical
tools, the documentation and the header files). Bundled libraries, with their licence texts in
`tools\pgsql\commandlinetools_3rd_party_licenses.txt`: OpenSSL (Apache-2.0), zlib (Zlib),
libxml2 and libxslt (MIT), ICU (Unicode-3.0), lz4 (BSD-2-Clause), zstd (BSD-3-Clause),
libcurl (curl), gettext runtime, libiconv and winpthreads (LGPL-2.1).
The programs need the Microsoft Visual C++ run-time libraries `vcruntime140.dll`,
`vcruntime140_1.dll` and `msvcp140.dll`, which the archive does not contain; see `docs/setup.md`.

### npm packages
Used by the browser variant of the sample application and by generated web applications. The
archives are fetched by `scripts/pack-release.ps1` from the npm registry and are shipped in
`downloads\npm`. What protects them is the SHA-512 `integrity` value of
`examples/sample-app/web/package-lock.json`: the pack script checks every archive against it
when it fetches it, the setup checks it again before the archive goes into the local npm cache,
and `npm ci` checks it a third time when it installs from that cache.

| Package | Version | Licence |
|---|---|---|
| pg | 8.23.1 | MIT |
| pg-cloudflare | 1.4.1 | MIT |
| pg-connection-string | 2.14.1 | MIT |
| pg-int8 | 1.0.1 | ISC |
| pg-pool | 3.14.0 | MIT |
| pg-protocol | 1.16.1 | MIT |
| pg-types | 2.2.0 | MIT |
| pgpass | 1.0.5 | MIT |
| postgres-array | 2.0.0 | MIT |
| postgres-bytea | 1.0.1 | MIT |
| postgres-date | 1.0.7 | MIT |
| postgres-interval | 1.2.0 | MIT |
| split2 | 4.2.0 | ISC |
| xtend | 4.0.2 | MIT |

## Test audio
No recorded audio is shipped. The self test and the tests make their audio on the spot with a
speech synthesis voice of Windows (`setup\make-test-audio.ps1`), because the terms for
redistributing the output of those voices are not clearly stated.
