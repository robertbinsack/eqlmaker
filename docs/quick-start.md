<!-- SPDX-License-Identifier: AGPL-3.0-or-later -->
[English](quick-start.md) · [Deutsch](de/quick-start.md)

# Quick start

This page takes you from nothing to a first run of the sample and to a first project of your
own. There are two routes: from source, or with the Windows package of the
[latest release](https://github.com/robertbinsack/eqlmaker/releases/latest).

## Route 1: from source

You need:

- Windows 10 or later, or Windows Server 2019 or later (x64). The C# compiler that the build
  uses is part of Windows.
- A PostgreSQL 15 or later that you may administer, with `wal_level = logical` and a superuser
  connection. Use a server for tests, not a production server. See [event sources](sources.md)
  for what the server must provide.
- Node.js 18 or later and FFmpeg on the `PATH`.
- Microsoft Edge or Chrome (Edge is part of Windows).

Build:

```text
git clone https://github.com/robertbinsack/eqlmaker.git
cd eqlmaker
build.cmd
```

The result is the folder `bin`. Nothing is installed. Check it:

```text
bin\eqlmaker version
bin\eqlmaker help
```

Install the one dependency of the browser variant of the sample:

```text
cd examples\sample-app\web
npm ci
cd ..\..\..
```

Create a project folder and tell it where the database server is:

```text
bin\eqlmaker init demo
```

Open `demo\eqlmaker.json` and set `connection` to your server. Do not write the password into
the file; give it in the environment for this command prompt:

```text
set EQLMAKER_PGPASSWORD=<password of the PostgreSQL superuser>
```

Run the sample:

```text
bin\eqlmaker sample --project demo
```

It creates the database `eqlmaker_sample` on your server, operates the sample application as a
Windows program and in the browser without showing a window, records both, builds time line and
specification, compares the database changes and writes a player page. Each line of the output
starts with `ok` or `FAIL`. What happens in detail is described in
[sample application](sample-app.md).

## Route 2: the Windows package

The [latest release](https://github.com/robertbinsack/eqlmaker/releases/latest) consists of
`eqlmaker-windows-x64.zip`, its checksum file `eqlmaker-windows-x64.zip.sha256` and
`eqlmaker-version.json`. Download the zip, compare its checksum if you like
(`certutil -hashfile eqlmaker-windows-x64.zip SHA256`), unpack it anywhere, then:

```text
eqlmaker-setup.cmd
eqlmaker sample --project project
```

The setup needs no admin rights and writes nothing outside its folder. It downloads FFmpeg and
Node.js, brings the speech recognition, and installs a portable PostgreSQL if you want one. At
the end it runs a self test. Options, exit codes and limits are in [setup](setup.md).

What was tested: the package of version 0.2.0 on Windows Server 2025, as a user without admin
rights, in a fresh folder with a blank and an umlaut in its name, with all downloads, the self
test with speech recognition, and the removal. Not tested yet: a clean Windows 10 or Windows 11,
a download through a proxy, and building the package with PowerShell 7. If the setup fails on
your computer, please open an issue with the file `downloads\setup.log`.

In the package, `eqlmaker` is the batch file `eqlmaker.cmd` in the package folder; it starts
`bin\eqlmaker.exe`. From source, write `bin\eqlmaker` instead, or put `bin` on the `PATH`.

## Your first own project

The commands below assume that `eqlmaker` is found (package folder, or `bin` on the `PATH`).

1. **Create the project.**

   ```text
   eqlmaker init my-project
   cd my-project
   ```

   Edit `eqlmaker.json`: `connection` (a superuser of the server the application uses) and
   `capture.databases` (the database of the application). See [configuration](configuration.md).

2. **Record the original.** Start the recording first and the application after it, because
   the capture applies to database sessions that start afterwards.

   ```text
   eqlmaker record --start --name original
   eqlmaker record --stop
   ```

   Between the two commands, work with the application. This records the screen video and the
   database events. To also get every action with its control and a chapter per business step,
   let EQLMaker operate the application: with a scenario file
   (`eqlmaker record --scenario <file.json>`) or through an MCP client. See
   [remote control](remote-control.md).

3. **Build time line and specification.**

   ```text
   eqlmaker correlate original
   eqlmaker spec original
   ```

   Read `spec\spec.md`. Open `recordings\original\viewer.html` to see video, actions and
   database events side by side.

4. **Add the manual**, if there is one.

   ```text
   eqlmaker manual handbook.pdf
   eqlmaker spec original
   ```

5. **Prepare the rebuild.** These are [guided steps](guided-steps.md): EQLMaker prepares a
   workspace and instructions; an assistant connected as MCP client, or a person, builds.

   ```text
   eqlmaker generate
   eqlmaker clone
   ```

6. **Prove it.** Reset the database to the state the first recording started from, record the
   same steps in the rebuilt application with the same chapter captions as `web`, then compare:

   ```text
   eqlmaker verify db original web
   eqlmaker video original web
   ```

   `verify db` ends with exit code 0 when every changed row agrees and with 2 when not. See
   [comparison tests](verify.md).

## When something fails

- `eqlmaker reset` undoes what an aborted capture left on the database server.
- A recording without changed rows says so in its warnings: the server does not run with
  `wal_level = logical`.
- `FFmpeg not found`: put `ffmpeg` on the `PATH`, or name it under `tools.ffmpeg` in
  `eqlmaker.json`.
- More in the [FAQ](faq.md).
