<p align="right"><a href="README.md">Deutsch</a> · <b>English</b></p>

<h1 align="center">EQLMaker</h1>

<p align="center"><b>Understand legacy software and rebuild it as a modern web application. Without source code.</b></p>

<p align="center">
  <img alt="Status: in development" src="https://img.shields.io/badge/Status-in%20development-orange">
  <img alt="License: AGPL-3.0" src="https://img.shields.io/badge/License-AGPL--3.0-blue">
  <img alt="Platform: Windows" src="https://img.shields.io/badge/Platform-Windows-0078D6">
  <img alt="PostgreSQL 15+" src="https://img.shields.io/badge/PostgreSQL-15%2B-336791">
  <img alt="MCP server" src="https://img.shields.io/badge/MCP-Server-6E56CF">
</p>

EQLMaker watches the application being used, measures what happens in the database at the same time, and reads the manual. The result is the foundation for a rebuild whose correctness can be demonstrated with comparison tests.

> [!NOTE]
> **Status:** EQLMaker is currently being built. Version 0.1 has not been released yet; until then, everything here is "in development" and nothing is available. The source code will be published in this repository with v0.1.

---

## The problem

Many businesses, associations and public administrations rely on old software whose vendor no longer exists and whose source code is missing. It no longer receives security updates and becomes a risk. Replacing it usually fails not because of technology, but because **nobody knows exactly everything the program does anymore**.

## What EQLMaker does

EQLMaker captures the knowledge embedded in a running application without needing its source code:

- Someone uses the old program as usual. EQLMaker **records** what happens on screen and at the same time **measures** which data the application reads and changes in the database.
- The **manual** is added, plus – optionally – a **video with spoken explanation**.
- Everything lands on a shared **timeline**: this click, this screen, this database change, this sentence in the manual.
- The timeline produces a **specification**. With it, a connected AI assistant rebuilds the application as a modern web app. EQLMaker provides template and instructions and then verifies with **comparison tests** whether the rebuild shows the same and changes the same data as the original.

## How it works

~~~mermaid
flowchart LR
    subgraph IN["1 · Input"]
        A["Record<br/>usage"]
        B["Measure<br/>database changes"]
        C["Read<br/>manual"]
        D["Video with<br/>timestamp & audio"]
    end
    subgraph MAKE["2 · What EQLMaker builds"]
        E["Timeline"]
        F["Specification"]
        G["Rebuild<br/>(migration / archive)"]
    end
    subgraph PROOF["3 · The proof"]
        H["Comparison tests"]
        I["Comparison video"]
    end
    A --> E
    B --> E
    C --> E
    D --> E
    E --> F --> G --> H --> I
~~~

| Stage | Step | What happens | Command |
|---|---|---|---|
| 1 | Observe usage | Screen video and every user action with time, window and control – for Windows programs and browser applications | `record` |
| 1 | Measure data changes | Statements, parameters, transactions and changed rows, using PostgreSQL's built-in means. Measured, not guessed. | `record` |
| 1 | Read the manual | PDF, HTML, DOCX, text or Markdown; chapters are linked to screens | `manual` |
| 1 | Video with timestamp and audio | An overlay shows date and time with milliseconds on top of all windows; record with any screen recorder; offline transcript via Whisper | `video-import` |
| 2 | Timeline | The on-screen timestamp is matched with the times of the database changes | `correlate` |
| 2 | Specification | Screens, queries, tables read and written, manual references, hints from the video | `spec` |
| 2 | Rebuild | Modern web app (migration) or read-only clone (archive) – guided steps for the AI assistant | `generate` · `clone` · `enrich` |
| 3 | Comparison tests | Visual comparison of screens and comparison of measured database changes; the report shows deviations | `verify` |
| 3 | Comparison video | Original and rebuild side by side on one timeline, with player | `video` |

All steps are also available as tools of an **MCP server**.

## Two modes

| | Mode 1 · Migration | Mode 2 · Archive |
|---|---|---|
| **Result** | New UI that reads and writes | 1:1 clone in the browser, read-only |
| **Basis** | Modern web app from a template, on the same tables as the original | The original's screens; save, post and create stay visible but greyed out |
| **Legacy app** | Can keep running in parallel | Can be switched off |
| **For whom** | Anyone who wants to keep using and evolving their software | Anyone who must retire a legacy system but still needs access to its data |

## Examples: legacy application and rebuild side by side

> [!IMPORTANT]
> These proofs of concept were created with the same method on EQLServer, each by an AI agent, **before EQLMaker became a program of its own**. EQLMaker v0.1 did not produce them. They show what the method can achieve.

| Application | Technology | AI build time, new UI | Coverage |
|---|---|---|---|
| Property management | PowerBuilder 6.5 · SQL Anywhere | 24.5 min | 70 / 70 menu functions (100 %) |
| Inventory management | Delphi · Advantage | 16.9 min + 5 h 17 min extension | 398 / 494 menu functions (81 %) |
| Informix sample application | ASP.NET · Informix | 7.7 min | 8 / 8 pages and actions (100 %) |
| InvoicePlane (open source) | PHP · MySQL/MariaDB | 41 min | 81 / 82 GUI functions (99 %) |
| TurboCASH (open source) | Delphi · Firebird | 42 min | 228 / 266 menu functions (86 %) |
| Northwind (Access template) | Microsoft Access · ODBC | 45 min | 58 / 58 GUI functions (100 %) |
| Contoso University (Microsoft sample) | ASP.NET Core · SQL Server | 14 min | 32 / 32 GUI functions (100 %) |
| Dolibarr ERP/CRM (open source) | PHP · PostgreSQL | 3 h 09 min | 81 / 93 GUI functions (87 %) |

▶️ **Videos (original and rebuild side by side):** [eqlserver.com](https://eqlserver.com) · [eqlserver.de/eqlmaker](https://eqlserver.de/eqlmaker/)

<sub>The applications come from third-party vendors or are open source. Product and vendor names of third-party programs are anonymized.</sub>

## Features

### In development · version 0.1

| Feature | Description |
|---|---|
| Record usage | Windows programs and browser applications: screen video and every user action with time, window and control |
| Measured database changes | Statements, parameters, transactions and changed rows using PostgreSQL's built-in means – no server-wide changes, no restart |
| Open event format | Database accesses as JSON Lines, documented, version 0.1 |
| Read the manual | PDF, HTML, DOCX, text or Markdown; chapters are linked to screens |
| Timestamp overlay | Small program showing date and time with milliseconds on top of all windows |
| Add video with audio later | The timestamp is read from the image and matched with the database changes |
| Transcript with Whisper | Speech recognition via whisper.cpp, included in the package, fully offline |
| Timeline and specification | Click, screen, statement and transaction on one timeline; screens, queries and tables derived from it |
| Rebuild as web app | Guided step with web template and instructions for a connected AI assistant |
| Read-only archive clone | Guided step with reference images per screen |
| Comparison tests | Visual comparison and comparison of measured database changes between two recordings |
| Comparison video with player | Original and rebuild on one timeline |
| MCP server and CLI | The same steps as tools for any MCP client and as commands |
| Plugin interfaces | For event sources, web templates, clone renderers and video layouts, documented |
| Setup without admin rights | One batch file sets everything up in its own folder: no installer, no registry, no service |
| German and English | Programs, documentation and web template in both languages |

### Planned · project from June 2027

A clearly scoped new module. So far there is only the concept, no code. Funding is being applied for; there is no commitment yet.

- **Live questioning chatbot** – asks at the right moment during use: What are you doing? What is this field for? What must never be entered here?
- **Screen element recognition** – windows, fields and buttons get stable identifiers.
- **Rules checked against real data** – answers become readable rules ("employee number: digits only") that are tested against existing data.
- **Automatically generated new manual** – from steps, answers and screenshots.
- **Masking of personal data** – before storage and before every request to a language model.

## The honest limit

EQLMaker sees what was operated during the recording and what happened in the database at the time. **Calculation logic that the old program never displays and that appears in no recording can be missed by a rebuild.** The comparison tests show deviations – but only for what was recorded.

That is why rarely used functions and error cases belong in the recording, and for migrations a human checks screen by screen. For the archive mode the limit hardly matters, because no write or posting logic is rebuilt.

The rebuild steps are **guided, not fully automatic**: EQLMaker does not generate the code itself; it prepares and verifies the work of an AI assistant.

## Quick start (planned)

> [!WARNING]
> The package does not exist yet. Command names and options may change until the first release.

~~~bat
:: sets everything up in its own folder, no admin rights
> eqlmaker-setup.cmd
:: record usage and database changes
> eqlmaker record
:: build the timeline
> eqlmaker correlate
:: write the specification
> eqlmaker spec
:: prepare the rebuild: migration ...
> eqlmaker generate
:: ... or archive clone
> eqlmaker clone
:: compare original and rebuild
> eqlmaker verify
~~~

**Requirements (planned):** Windows, PostgreSQL 15 or later. No installer, no admin rights, no system changes – uninstalling means deleting the folder.

## Who it is for – and what society gains

Old line-of-business software is a silent risk. Knowledge about it lives in the heads of a few people and retires with them. This hits hardest where there is no money for consulting projects.

- **People who use old programs every day** – in small municipalities, associations, medical practices and craft businesses. They are the real experts and should be able to pass on their knowledge without workshops and without programming skills.
- **Developers replacing legacy software** – instead of oral tradition they get a verifiable foundation: recording, measured database changes, specification and comparison tests.
- **Organizations that must retire a legacy system** – a read-only clone keeps the data accessible even after the old program stops running.

**Public benefit:** The method is described openly and published as free software, so it helps not only those who can afford an expensive replacement. Software does not solve the problem alone: it can observe, compare and reveal contradictions; the decision stays with people. Because screens and recordings may contain personal data, speech recognition runs offline on the user's own machine.

## Roadmap

- **Now · in development**
  - [ ] Finish and test version 0.1
  - [ ] End-to-end run with an own sample application on plain PostgreSQL
  - [ ] Publish source code, first release as a Windows package
- **From June 2027 · planned**
  - [ ] Recognize screen elements and link them to data changes
  - [ ] Live dialogue via text, later via voice
  - [ ] Detect and replace personal data
  - [ ] Derive rules from answers and check them against existing data
  - [ ] Illustrated manual, German and English
  - [ ] Tests with users, case study on a free application
- **Later · no date**
  - [ ] Event format as a standalone specification 1.0 for other tools
  - [ ] More open sources: MySQL/MariaDB and SQL Server
  - [ ] Accessibility of generated web apps (WCAG 2.1 AA)
  - [ ] Template gallery for contributors

<sub>The roadmap is a plan, not a commitment.</sub>

## Related work

EQLMaker does not replace any of these approaches – it connects what they keep separate.

| Approach | What it does | What is missing |
|---|---|---|
| RPA tools | Keep driving the old UI | Do not replace it |
| Application reconstruction | Analyze program files, find structure and code | Not the intent behind a user action |
| Low-code migration | Move to new platforms | Usually require source code or a specific source product |
| Click documentation | Generate illustrated steps from clicks | Do not know the database |
| Website cloning tools | Copy a page's look | Not an application's behavior |

**What EQLMaker does differently:** it brings user interaction and measured database changes together on one timeline and proves the rebuild with comparison tests.

## Other databases

EQLMaker runs entirely on a standard **PostgreSQL** and thus reaches applications that speak PostgreSQL. For applications on other database servers, PostgreSQL can be replaced by [EQLServer](https://eqlserver.com); the configuration stays the same. EQLServer is a commercial product by the same author and **not part of EQLMaker**. No part of EQLMaker requires EQLServer.

## Contributing and license

- **License:** [AGPL-3.0](https://www.gnu.org/licenses/agpl-3.0.html) (GNU Affero General Public License, version 3 or later). Copyright Robert Binsack.
- **Contributions:** via the [Developer Certificate of Origin (DCO)](https://developercertificate.org/). Contributors confirm with the `Signed-off-by` line in the commit that the contribution may be made under the project's license. There are no special rights to contributions.
- **Contact:** office@eqlserver.de · [binsack.com](https://www.binsack.com)
