<!-- SPDX-License-Identifier: AGPL-3.0-or-later -->
# Security

## Reporting a vulnerability

Write to **office@eqlserver.de** with the subject "EQLMaker security". Please do not open a
public issue for a vulnerability.

Say what you found, which version or commit, and how to reproduce it. A fix is published with a
note in the release; you are named if you want to be.

Do not send recordings, database dumps or screenshots of real applications: they may contain
personal data. A description, or a reproduction with the sample application of the repository,
is enough.

## Supported versions

| Version | Supported |
|---|---|
| 0.1.x | yes |

## In scope

- The programs of this repository: `eqlmaker.exe` (command line and MCP server),
  `eqlmaker-timestamp.exe`, the setup (`eqlmaker-setup.cmd`), the build and packaging scripts.
- The web template and the clone template in `src/templates`, and what they do in an
  application built from them.
- The handling of passwords and connection data, of downloaded components (checksums, archive
  paths) and of plugin programs.

## Out of scope

- Vulnerabilities of the third-party components the package downloads (FFmpeg, Node.js,
  PostgreSQL, whisper.cpp): report them to their projects. Tell us when a pinned version in
  `scripts/setup/components.json` should be raised.
- The application that somebody builds in a guided step. EQLMaker prepares and checks that
  work; it does not write it.
- EQLServer, a separate commercial product of the same author: use the same address and name
  the product.

## What users should know

- **Recordings contain what was on the screen and in the database.** Videos, reference
  pictures, typed text, SQL statements with their parameters, changed rows and transcripts are
  stored unmasked in the project folder. Version 0.1 has no masking of personal data. Treat a
  project folder like the data of the application itself, and do not publish recordings of real
  data. Text typed in a step marked `secret` is not written into the recording.
- **The capture needs a superuser connection** to the database server. Keep the password out of
  `eqlmaker.json`: use the environment variable `EQLMAKER_PGPASSWORD` or a password file.
- **The MCP server operates the computer.** A connected MCP client can start programs, click
  and type. Connect only clients you trust, and run it under an account that may do no more
  than the task needs.
- **No network use of its own.** EQLMaker talks to the database server you configure and to
  browsers and programs on the same machine. Speech recognition runs offline. Only the setup of
  the package downloads files, from pinned addresses with pinned checksums.
