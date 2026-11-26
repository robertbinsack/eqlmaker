<!-- SPDX-License-Identifier: AGPL-3.0-or-later -->
[English](index.md) · [Deutsch](de/index.md)

# Documentation

This is the map of the documentation of EQLMaker 0.2. Every page exists in English and German
with the same file name. What is available and tested is listed in the
[README](../README.en.md) and the [roadmap](../ROADMAP.md).

## Start

| Page | Content |
|---|---|
| [Quick start](quick-start.md) | Build from source or set up the package, run the sample, start your own project |
| [Setup of the Windows package](setup.md) | `eqlmaker-setup.cmd`: options, exit codes, folder layout, portable PostgreSQL, offline use |
| [Sample application](sample-app.md) | The sample, its hidden rules, `eqlmaker sample` and what its numbers mean |
| [FAQ](faq.md) | Short answers, among them: where data goes, personal data, limits |
| [Applications on other databases](other-databases.md) | The principle, replacing the database by a server that stores in PostgreSQL, what is tested |

## The pipeline

| Page | Content |
|---|---|
| [Pipeline](pipeline.md) | record → correlate → spec → generate / clone → video → verify: inputs, outputs, files, fixed rules |
| [Remote control](remote-control.md) | Operating Windows programs and web pages: real and background mode, controls, scenario files |
| [Manual](manual.md) | Taking over the manual of the original: formats, what is extracted, limits of the PDF reader |
| [PowerBuilder libraries](powerbuilder.md) | Reading the libraries of a PowerBuilder application: menus, windows, labels, SQL |
| [Guided steps](guided-steps.md) | `generate`, `clone`, `enrich`: what EQLMaker prepares, who does the work, how it is checked |
| [Comparison tests](verify.md) | `verify screens` and `verify db`: rules, options, reading the report |
| [Time stamp overlay](timestamp-overlay.md) | The strip that puts the time into every video frame |
| [Video import](video-import.md) | Matching a narrated video to the database events; measured accuracy |
| [Voice-over](narration.md) | Laying a spoken commentary with optional background music over a finished video (local Fish Speech) |

## Reference

| Page | Content |
|---|---|
| [Command reference](cli.md) | Every command and every option of `eqlmaker` |
| [MCP server](mcp.md) | Connecting an MCP client; tools and prompts |
| [Configuration](configuration.md) | `eqlmaker.json`, passwords, where tools are looked for, languages |
| [Event sources](sources.md) | What the capture does on the database server and what the server must provide |
| [Event format](event-format.md) | The JSON Lines format of database events, version 0.1 |
| [Plugins](plugins.md) | Own event sources, readers, web templates, clone renderers and video layouts |

## Contributing

| Page | Content |
|---|---|
| [Translating](translating.md) | Adding a language to documentation and programs; the check |
| [Contributing](../CONTRIBUTING.md) | Build, tests, style, sign-off, how a feature becomes "available" |
