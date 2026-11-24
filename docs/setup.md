<!-- SPDX-License-Identifier: AGPL-3.0-or-later -->
<!-- Copyright (c) 2026 Robert Binsack -->
[English](setup.md) · [Deutsch](de/setup.md)

# Setting up the Windows package

The Windows package is one zip file. Unpack it anywhere, then run `eqlmaker-setup.cmd` in the
unpacked folder (double click, or from a command prompt). The batch file downloads what is
still needed, checks it, unpacks it into the same folder and makes everything work there.

- No installer is run, no admin rights are needed, no question from Windows appears.
- Nothing is written outside the folder: no registry entries, no change of `PATH`, no Windows
  service, no firewall rule, no scheduled task.
- Removing it again: run `eqlmaker-db-stop.cmd`, then delete the folder.

## Requirements

- Windows 10 (version 1803 or later), Windows 11 or Windows Server 2019 or later, 64 bit (x64).
- Windows PowerShell 5.1, `curl.exe` and `tar.exe`, which are part of these Windows versions.
- A processor with AVX2 for the speech recognition (Intel from 2013, AMD from 2015). The setup
  asks Windows for it before it downloads anything. Without AVX2 it says so and continues:
  everything else works, only the speech recognition (the transcription of narrated videos)
  cannot run on that computer.
- About 0.8 GB of free disk space with the portable PostgreSQL (about 0.6 GB without it), and
  an internet connection for the first run (about 170 MB of downloads with PostgreSQL, about
  118 MB without it), unless you use `-offline`. The downloaded archives stay in `downloads\`
  for later runs; they may be deleted when the setup is complete.
- No further run-time libraries and no admin rights. The only exception is the optional
  EnterpriseDB build of PostgreSQL, see [Portable PostgreSQL](#portable-postgresql).

## Running the setup

```
eqlmaker-setup.cmd [options]
```

| Option | Meaning |
|---|---|
| `-unattended` | No questions and no pause at the end; defaults for everything. For scripts. |
| `-offline` | No network access at all. Uses the archives in `downloads\`. |
| `-lang en` / `-lang de` | Language of the messages. Default: the language of Windows, English otherwise. |
| `-model <name>` | Downloads a larger speech recognition model and makes it the default. |
| `-no-postgres` | Does not install the portable PostgreSQL; you use your own server. |
| `-postgres mingw` / `-postgres edb` | Build of the portable PostgreSQL. Default: `mingw`. |
| `-with-psql` | Also installs psql with the default build; fetches two libraries under the GPL. |
| `-no-selftest` | Does not run the self test at the end. |
| `-help` | Shows the options. |

All options can be combined, also with `-unattended`.

Without `-unattended` the setup asks one question, and only on the first run: whether the
portable PostgreSQL shall be installed (default: yes). When started by a double click, the
window stays open at the end until a key is pressed.

### Exit codes

| Code | Meaning |
|---|---|
| 0 | Ready. Also when everything was set up before and the run only verified it. |
| 1 | Failed. The reason is the line that starts with `SETUP FAILED:`. |
| 2 | Everything was set up, but the self test failed. |
| 3 | `-offline` was given and archives are missing or damaged. Nothing was changed. The output names every missing file, where to get it and where to put it. |

The complete output of every run is also appended to `downloads\setup.log`.

### What the setup does

1. Checks the speech recognition that is shipped in the package against `MANIFEST.json`.
2. Makes sure the archives of FFmpeg, Node.js and (optionally) PostgreSQL are in `downloads\`:
   downloads them over HTTPS, continues an interrupted download, and compares the SHA-256 of
   every archive with `setup\components.json`. An archive with a wrong checksum is downloaded
   once more and then refused. Nothing is unpacked before all archives are verified.
3. Unpacks the archives. Every entry is checked before the first file is written; an archive
   with an entry that would land outside its target folder is refused.
4. Installs the packages of the browser variant of the sample application with the npm of the
   package, from the package archives shipped in `downloads\npm` (no network access).
5. Creates the portable database cluster, starts the server and checks the login.
6. Writes `project\eqlmaker.json`, the ready project configuration for the sample.
7. Runs the self test: a sentence spoken by a Windows voice is recognised by the speech
   recognition (English and German, when Windows has such voices), then
   `bin\eqlmaker.exe selftest --project <folder>\project`, which gets the spoken sentence
   through `--audio` and `--expect` and checks its own way to the speech recognition with it.
   The self test also creates the sample database.
8. Prints a summary: what is where, versions, port, how to start and how to remove.

A second run does nothing but verify. It needs no network access.

## Layout of the folder

```
eqlmaker-setup.cmd            the setup
eqlmaker.cmd                  starts bin\eqlmaker.exe with the arguments given
eqlmaker-npm.cmd              npm of the package, with the cache inside the package
eqlmaker-db-start.cmd         starts the portable PostgreSQL
eqlmaker-db-stop.cmd          stops it
LICENSE, THIRD_PARTY_NOTICES.md, README.md, MANIFEST.json
bin\                          the programs and share\
docs\, examples\
setup\                        setup script, components.json, message texts, COMPONENTS.md
tools\whisper\                speech recognition: whisper-cli.exe, models\, model.txt   (in the zip)
tools\ffmpeg\bin\             ffmpeg.exe, ffprobe.exe and their libraries                (downloaded)
tools\node\                   node.exe, npm                                              (downloaded)
tools\pgsql\                  PostgreSQL programs                                        (downloaded, optional)
tools\zstd\                   zstd.exe, unpacks the PostgreSQL packages                  (downloaded with PostgreSQL)
data\pg\                      database cluster of the portable PostgreSQL
data\pg-port.txt              its port
data\pgpass.conf              its passwords (password file format of PostgreSQL)
downloads\                    downloaded archives, npm\ (package archives), npm-cache\, setup.log
project\eqlmaker.json         project configuration for the sample
```

The third-party components, their versions, sources and licences are listed in
`setup\COMPONENTS.md`.

## Without internet access

Run the setup once on a computer with internet access and copy the whole folder, or copy only
the archives: put the files named in `setup\components.json` into `downloads\` and run

```
eqlmaker-setup.cmd -offline
```

When something is missing, the setup stops with exit code 3 before it changes anything and
lists each missing file with its address, its target path and its checksum.

## Proxy

Downloads use the proxy of the environment variables `HTTPS_PROXY`, `ALL_PROXY` or `HTTP_PROXY`
when one of them is set, otherwise the proxy that is configured in Windows. Example:

```
set HTTPS_PROXY=http://proxy.example.org:3128
eqlmaker-setup.cmd
```

Only HTTPS addresses are used, redirects to other protocols are refused.

## Speech recognition

The package contains whisper.cpp (`tools\whisper\whisper-cli.exe`) and one multilingual model.
It runs on the processor, needs no graphics card and no network access. `tools\whisper\model.txt`
holds the file name of the default model in `tools\whisper\models\`.

Larger models recognise more accurately and are slower. `-model <name>` downloads one and
makes it the default; `-model small-q5_1` switches back:

| Name | Size | Remark |
|---|---|---|
| `base` | 141 MB | fastest, clearly more errors in German |
| `small-q5_1` | 181 MB | shipped default |
| `small` | 465 MB | |
| `medium-q5_0` | 514 MB | |
| `large-v3-turbo-q5_0` | 547 MB | best German in our measurement, about a quarter of the speed of the default |
| `large-v3-turbo-q8_0` | 834 MB | |

How EQLMaker calls it (audio first converted to 16 kHz mono WAV with FFmpeg):

```
working directory: <folder>\tools\whisper
whisper-cli.exe -m models\<model> -f <audio.wav> -l <en|de|auto> -t <threads> -oj -of <output path without extension> -np
```

The model is given relative to the working directory on purpose: `whisper-cli.exe` ends with
an error when the full path of the model has characters outside ASCII (an umlaut in the folder
name, for example). The paths of the audio file and of the output may have such characters, as
long as they exist in the ANSI code page of the computer.

The result is `<output path>.json` (UTF-8). `transcription` is the list of segments; `offsets.from`
and `offsets.to` are milliseconds from the start of the audio, `timestamps` holds the same times
as text (`hh:mm:ss,mmm`), `text` starts with a blank. `result.language` is the language used or
detected. With `-ojf` instead of `-oj` every segment also has `tokens` with their own times.

`setup\make-test-audio.ps1` makes test audio with a speech synthesis voice of Windows
(`-Text`, `-Language en|de`, `-OutFile`); it ends with exit code 3 and a message when Windows
has no voice for the language. No recorded audio is shipped.

## Portable PostgreSQL

Unless `-no-postgres` is given, the setup unpacks the PostgreSQL 18 programs into `tools\pgsql`
and creates a cluster in `data\pg`:

- It listens on `127.0.0.1` only. The port is the first free one from 55432 and is written to
  `data\pg-port.txt`.
- Logins need a password (`scram-sha-256`). The password of the user `postgres` and the one of
  the sample user `sample_app` are generated at random on your computer and written to
  `data\pgpass.conf`, which only your Windows user can read. They are not shown and not logged.
- `wal_level = logical` and `logging_collector = on` are set; the server log is in `data\pg\log`.
- It is not a Windows service. `eqlmaker-db-start.cmd` starts it and `eqlmaker-db-stop.cmd`
  stops it. It keeps running when the window is closed and ends when you log off. After the
  setup it is running.
- The sample database `eqlmaker_sample` is created by the self test, or later by
  `eqlmaker.cmd sample --project project`.

**Two builds.** The default is the MinGW build of the MSYS2 project (`-postgres mingw`). It
needs nothing beyond what Windows 10 and later bring along, so it works without any installer
and without admin rights. The setup fetches it as pinned package archives from the MSYS2
repository and unpacks them with the Zstandard tool it downloads for this purpose
(`tools\zstd`). `-postgres edb` selects the binaries of EnterpriseDB instead; they need the
Microsoft Visual C++ run-time, see below. A later run without the option keeps the build that
is installed.

**psql is not part of the default.** In the MinGW build `psql.exe` needs two libraries that are
licensed under the GPL (readline, GPL-3.0-or-later, and termcap, GPL-2.0-or-later). EQLMaker
does not need psql: it talks to the server with its own client, and the setup checks the login
with `eqlmaker clock`. So the default leaves psql and the two libraries out. `-with-psql`
fetches them and installs psql; the setup names both libraries and their licences before it
downloads them. The EnterpriseDB build always has its psql.

To connect with psql (after `-with-psql`, or with a psql of your own):

```
set PGPASSFILE=<folder>\data\pgpass.conf
tools\pgsql\bin\psql.exe -h 127.0.0.1 -p <port> -U postgres
```

**Microsoft Visual C++ run-time (only with `-postgres edb`).** The PostgreSQL programs of
EnterpriseDB need `vcruntime140.dll`, `vcruntime140_1.dll` and `msvcp140.dll`, and the archive
does not contain them. They are present on most computers because many programs install them.
When they are missing, the setup says so before it downloads anything and names the ways out:

- Use the default build: run the setup without `-postgres edb`.
- Install the "Microsoft Visual C++ Redistributable (x64)" once with the installer of
  Microsoft, `https://aka.ms/vc14/vc_redist.x64.exe`, then run the setup again. This is the
  one step that needs admin rights.
- Use your own PostgreSQL: run the setup with `-no-postgres`.
- Use a package that was built with `-IncludeVcRuntime`: it carries the three libraries in
  `tools\vcruntime` and the setup copies them next to the PostgreSQL programs.

The setup does not fetch these libraries by itself, and the standard package does not contain
them. Microsoft offers them for download only as that installer; there is no archive of the
plain files, and the package of Microsoft on NuGet with a similar name
(`Microsoft.VCRTForwarders.140`) only forwards to an installed run-time. According to Microsoft,
passing on the installer or the single files is reserved to licensed users of Visual Studio,
under its licence terms; that is what `-IncludeVcRuntime` is for, to be used by whoever builds
the package and holds such a licence. No other part of the package needs these libraries.

### Your own PostgreSQL

With `-no-postgres` nothing of PostgreSQL is downloaded. On the first run the setup writes
`project\eqlmaker.json` as a template; enter your connection there. Run the setup again
afterwards to get the self test, which also creates the sample database (or load it with
`examples\sample-app\db\setup.ps1` and the psql of your server, see
`examples\sample-app\README.md`). An existing `project\eqlmaker.json` is never
overwritten.

## npm of the package

`eqlmaker-npm.cmd <arguments>` runs the npm of `tools\node` in the current folder with the cache
in `downloads\npm-cache`. The cache holds the packages of the sample, so in the folder of a
generated web application that uses the same versions this works without network access:

```
<folder>\eqlmaker-npm.cmd ci --offline
```

## Limits

- **Length of the path.** The path of the package folder may have at most 140 characters;
  the setup refuses a longer one. (The longest file of the downloaded components then has a
  path of 259 characters, the limit of Windows PowerShell.)
- **Characters in the path.** Blanks and umlauts are fine. PostgreSQL itself cannot work below
  a path with characters outside ASCII; for such a folder the setup and the start and stop
  batch files run it through the short name (8.3) that Windows keeps for the folder. On a drive
  where short names are switched off the setup says so and stops before it downloads anything;
  then use a path with plain letters or `-no-postgres`. Avoid `%`, `!`, `^` and `&` in the
  path: the batch files of Windows do not handle them reliably.
- **Temporary files.** The setup and the programs it starts use `downloads\tmp` inside the
  folder and empty it at the end. Known exceptions outside the folder: Windows PowerShell may
  update its own start-up caches in the user profile, and the speech synthesis of Windows used
  for the self test may do the same. Nothing of EQLMaker is stored there.
- **One user.** The folder belongs to the Windows user who ran the setup; the password file is
  readable by that user only.

## Building the package

`scripts\pack-release.ps1` builds `out\eqlmaker-windows-x64.zip` (with
`eqlmaker-windows-x64.zip.sha256` and `eqlmaker-version.json`) from the repository; see the
comment at the top of that script. Third-party files are fetched with pinned checksums into a
cache folder and never enter the repository. The tests of the package are in
`scripts\setup\tests`.
