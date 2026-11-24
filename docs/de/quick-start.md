<!-- SPDX-License-Identifier: AGPL-3.0-or-later -->
[English](../quick-start.md) · [Deutsch](quick-start.md)

# Schnellstart

Diese Seite führt von null zu einem ersten Lauf des Beispiels und zu einem ersten eigenen
Projekt. Es gibt zwei Wege: aus dem Quellcode oder mit dem Windows-Paket der
[neuesten Veröffentlichung](https://github.com/robertbinsack/eqlmaker/releases/latest).

## Weg 1: aus dem Quellcode

Sie brauchen:

- Windows ab Version 10 oder Windows Server ab 2019 (x64). Der C#-Compiler, den der Bau nutzt,
  gehört zu Windows.
- Ein PostgreSQL ab Version 15, das Sie verwalten dürfen, mit `wal_level = logical` und einer
  Superuser-Verbindung. Nehmen Sie einen Server für Tests, keinen Produktivserver. Was der
  Server bieten muss, steht unter [Ereignisquellen](sources.md).
- Node.js ab Version 18 und FFmpeg im `PATH`.
- Microsoft Edge oder Chrome (Edge gehört zu Windows).

Bauen:

```text
git clone https://github.com/robertbinsack/eqlmaker.git
cd eqlmaker
build.cmd
```

Das Ergebnis ist der Ordner `bin`. Nichts wird installiert. Prüfen Sie es:

```text
bin\eqlmaker version
bin\eqlmaker help
```

Installieren Sie die eine Abhängigkeit der Browser-Variante des Beispiels:

```text
cd examples\sample-app\web
npm ci
cd ..\..\..
```

Legen Sie einen Projektordner an und sagen Sie ihm, wo der Datenbankserver ist:

```text
bin\eqlmaker init demo
```

Öffnen Sie `demo\eqlmaker.json` und setzen Sie `connection` auf Ihren Server. Schreiben Sie das
Passwort nicht in die Datei; geben Sie es für diese Eingabeaufforderung in der Umgebung an:

```text
set EQLMAKER_PGPASSWORD=<password of the PostgreSQL superuser>
```

Führen Sie das Beispiel aus:

```text
bin\eqlmaker sample --project demo
```

Der Befehl legt auf Ihrem Server die Datenbank `eqlmaker_sample` an, bedient die
Beispielanwendung als Windows-Programm und im Browser, ohne ein Fenster zu zeigen, zeichnet
beides auf, bildet Zeitachse und Spezifikation, vergleicht die Datenbankänderungen und schreibt
eine Player-Seite. Jede Zeile der Ausgabe beginnt mit `ok` oder `FAIL`. Was im Einzelnen
geschieht, steht unter [Beispielanwendung](sample-app.md).

## Weg 2: das Windows-Paket

Die [neueste Veröffentlichung](https://github.com/robertbinsack/eqlmaker/releases/latest) besteht aus
`eqlmaker-windows-x64.zip`, der Prüfsummendatei `eqlmaker-windows-x64.zip.sha256` und
`eqlmaker-version.json`. Laden Sie das Zip herunter, vergleichen Sie bei Bedarf die Prüfsumme
(`certutil -hashfile eqlmaker-windows-x64.zip SHA256`), entpacken Sie es an beliebiger Stelle,
dann:

```text
eqlmaker-setup.cmd
eqlmaker sample --project project
```

Die Einrichtung braucht keine Adminrechte und schreibt nichts außerhalb ihres Ordners. Sie lädt
FFmpeg und Node.js herunter, bringt die Spracherkennung mit und installiert auf Wunsch ein
portables PostgreSQL. Am Ende läuft ein Selbsttest. Optionen, Rückgabewerte und Grenzen stehen
unter [Einrichtung](setup.md).

Was getestet wurde: das Paket der Version 0.1.0 unter Windows Server 2025, als Benutzer ohne
Adminrechte, in einem frischen Ordner mit Leerzeichen und Umlaut im Namen, mit allen Downloads,
dem Selbsttest mit Spracherkennung und dem Entfernen. Noch nicht getestet: ein frisches
Windows 10 oder Windows 11, ein Download über einen Proxy und der Bau des Pakets mit
PowerShell 7. Schlägt die Einrichtung auf Ihrem Rechner fehl, eröffnen Sie bitte ein Issue mit
der Datei `downloads\setup.log`.

Im Paket ist `eqlmaker` die Batchdatei `eqlmaker.cmd` im Paketordner; sie startet
`bin\eqlmaker.exe`. Aus dem Quellcode schreiben Sie stattdessen `bin\eqlmaker`, oder Sie nehmen
`bin` in den `PATH` auf.

## Das erste eigene Projekt

Die Befehle unten setzen voraus, dass `eqlmaker` gefunden wird (Paketordner, oder `bin` im
`PATH`).

1. **Projekt anlegen.**

   ```text
   eqlmaker init my-project
   cd my-project
   ```

   Bearbeiten Sie `eqlmaker.json`: `connection` (ein Superuser des Servers, den die Anwendung
   nutzt) und `capture.databases` (die Datenbank der Anwendung). Siehe
   [Konfiguration](configuration.md).

2. **Das Original aufzeichnen.** Starten Sie zuerst die Aufnahme und danach die Anwendung, denn
   die Erfassung gilt für Datenbanksitzungen, die danach beginnen.

   ```text
   eqlmaker record --start --name original
   eqlmaker record --stop
   ```

   Zwischen den beiden Befehlen arbeiten Sie mit der Anwendung. So entstehen das Bildschirmvideo
   und die Datenbankereignisse. Um zusätzlich jede Bedienhandlung mit ihrem Steuerelement und
   ein Kapitel je fachlichem Schritt zu erhalten, lassen Sie EQLMaker die Anwendung bedienen:
   mit einer Szenariodatei (`eqlmaker record --scenario <file.json>`) oder über einen
   MCP-Client. Siehe [Fernbedienung](remote-control.md).

3. **Zeitachse und Spezifikation bilden.**

   ```text
   eqlmaker correlate original
   eqlmaker spec original
   ```

   Lesen Sie `spec\spec.md`. Öffnen Sie `recordings\original\viewer.html`, um Video,
   Bedienhandlungen und Datenbankereignisse nebeneinander zu sehen.

4. **Das Handbuch hinzufügen**, falls es eines gibt.

   ```text
   eqlmaker manual handbook.pdf
   eqlmaker spec original
   ```

5. **Den Nachbau vorbereiten.** Das sind [geführte Schritte](guided-steps.md): EQLMaker
   bereitet Arbeitsordner und Anleitung vor; ein als MCP-Client angeschlossener Assistent oder
   ein Mensch baut.

   ```text
   eqlmaker generate
   eqlmaker clone
   ```

6. **Belegen.** Setzen Sie die Datenbank auf den Stand zurück, von dem die erste Aufnahme
   ausging, zeichnen Sie dieselben Schritte in der nachgebauten Anwendung mit denselben
   Kapitelüberschriften als `web` auf, dann vergleichen Sie:

   ```text
   eqlmaker verify db original web
   eqlmaker video original web
   ```

   `verify db` endet mit Rückgabewert 0, wenn jede geänderte Zeile übereinstimmt, sonst mit 2.
   Siehe [Vergleichstests](verify.md).

## Wenn etwas fehlschlägt

- `eqlmaker reset` nimmt zurück, was eine abgebrochene Erfassung auf dem Datenbankserver
  hinterlassen hat.
- Eine Aufnahme ohne geänderte Zeilen sagt das in ihren Warnungen: Der Server läuft nicht mit
  `wal_level = logical`.
- `FFmpeg not found`: `ffmpeg` in den `PATH` aufnehmen oder unter `tools.ffmpeg` in
  `eqlmaker.json` nennen.
- Mehr in den [häufigen Fragen](faq.md).
