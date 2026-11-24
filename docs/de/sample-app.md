<!-- SPDX-License-Identifier: AGPL-3.0-or-later -->
[English](../sample-app.md) · [Deutsch](sample-app.md)

# Beispielanwendung

„Sample Shop“ ist eine kleine Geschäftsanwendung, die für dieses Repository gemacht wurde:
Kunden, Artikel und Rechnungen in fünf Masken, auf PostgreSQL. Es gibt sie zweimal mit demselben
Verhalten, als Windows-Programm und im Browser, damit sich der ganze Ablauf ohne fremde Software
zeigen und testen lässt. Die Daten sind erfunden.

| Teil | Wo | Was |
|---|---|---|
| Windows-Programm | `examples/sample-app/desktop`, gebaut als `bin\sample-desktop.exe` | spielt das Original; Windows Forms, englisch |
| Browser-Variante | `examples/sample-app/web` | spielt die nachgebaute Anwendung; auf der Web-Vorlage gebaut, englisch und deutsch |
| Datenbank | `examples/sample-app/db` | Tabellen und erfundene Daten; Rolle `sample_app`, Datenbank `eqlmaker_sample` |
| Szenarien | `examples/sample-app/scenarios` | dieselben fachlichen Schritte für beide Varianten |
| Handbuch | `examples/sample-app/manual/handbook.md` | ein kurzes Handbuch, das die Masken erklärt und die meisten Regeln auslässt |
| Rechenfälle | `examples/sample-app/test-vectors.json` | Rechnungen mit ihren erwarteten Beträgen, von beiden Varianten benutzt |

## Die verborgenen Regeln

Der Sinn des Beispiels: Die Regeln beim Speichern einer Rechnung stehen nirgends auf dem
Bildschirm. Wird eine Rechnung gespeichert, geschieht in einer Transaktion:

1. Jede Zeile erhält ihren Nettobetrag aus Menge und dem Preis des Artikels in diesem Moment.
2. Rabatt nach Kundengruppe: Gruppe A erhält 5 % ab einem Netto von 500, Gruppe B 3 % ab einem
   Netto von 1000, Gruppe C keinen.
3. Steuer je Steuerklasse (19 % und 7 %) auf den rabattierten Betrag der Klasse, einmal je
   Klasse gerundet, dann addiert.
4. Kreditlimit: Die offenen Rechnungen des Kunden plus die neue dürfen das Limit nicht
   überschreiten; sonst wird nichts geschrieben.
5. Die Rechnungsnummer kommt aus einem Zähler: `INV-<year>-<5 digits>`.
6. Für jede Zeile wird der Bestand des Artikels verringert und eine Lagerbewegung geschrieben.

Das Handbuch nennt keine der Zahlen. Ein Nachbau, der gleich aussieht, kann trotzdem anders
runden oder die Lagerbewegungen vergessen. Dafür ist der Vergleich der gemessenen
Datenbankänderungen da.

## eqlmaker sample

```text
eqlmaker sample [--flaw] [--no-video] [--visible]
```

Der Befehl durchläuft den ganzen Ablauf gegen den Datenbankserver der Projektkonfiguration. Er
braucht eine Superuser-Verbindung, `wal_level = logical` auf dem Server, Node.js und die eine
installierte Abhängigkeit der Browser-Variante (`npm ci` in `examples/sample-app/web`; die
Einrichtung des Pakets erledigt das).

Was er tut, der Reihe nach:

1. Legt die Rolle `sample_app` und die Datenbank `eqlmaker_sample` mit Tabellen und Daten an.
   Eine vorhandene Beispieldatenbank wird ersetzt. Das Passwort der Rolle wird für den Lauf
   zufällig erzeugt, sofern `SAMPLE_APP_PASSWORD` nicht gesetzt ist.
2. Zeichnet das Windows-Programm auf: Das Szenario `desktop.json` bedient es im Modus
   `background` auf einem eigenen Desktop, der nie angezeigt wird. Aufnahme `desktop`.
3. Lädt dieselben Daten erneut, startet die Browser-Variante auf einem freien Port von
   127.0.0.1 und zeichnet das Szenario `web.json` in einem Browser ohne Fenster auf. Aufnahme
   `web`.
4. `correlate` für beide, das Handbuch über `manual` und `spec` aus der Aufnahme `desktop`.
5. `verify db desktop web`.
6. `video desktop web`: die Player-Seite.

Jede Zeile der Ausgabe beginnt mit `ok` oder `FAIL`; der Rückgabewert ist 0, wenn alles wie
erwartet war, sonst 2. Auf dem Bildschirm bewegt sich nichts, und Maus und Tastatur bleiben frei.

| Option | Bedeutung |
|---|---|
| `--flaw` | startet die Browser-Variante mit ihren absichtlichen Fehlern; die Aufnahme heißt `web-flawed`, der Lauf besteht, wenn `verify db` Abweichungen findet; Spezifikation und Player-Seite entfallen |
| `--no-video` | zeichnet kein Video auf und schreibt keine Player-Seite |
| `--visible` | bedient beide Varianten mit der echten Maus auf dem sichtbaren Desktop, für eine Vorführung; währenddessen Maus und Tastatur nicht berühren |

Die fachlichen Schritte beider Szenarien, jeder ein Kapitel: „New customer“, „Change article
price“, „New invoice“ (drei Zeilen), „Mark invoice paid“.

## Was die Zahlen bedeuten

Gemessen im Ende-zu-Ende-Test des Repositorys
(`PipelineTests.SampleEndToEndOnPlainPostgres`) auf PostgreSQL 18:

| Zahl | Bedeutung |
|---|---|
| 38 Bedienhandlungen | was das Szenario im Windows-Programm getan hat: Klicks, gefüllte Felder, Kapitelmarken |
| 43 Anweisungen | SQL-Anweisungen, die das Windows-Programm dabei gesendet hat |
| 4 Transaktionen, 14 geänderte Zeilen | eine Transaktion je fachlichem Schritt; die Rechnung allein ändert 11 Zeilen |
| „equal (14 changed rows agree)“ | Ergebnis von `verify db` für die treue Browser-Variante |
| 5 Zeilen nur im Original, 2 nur im Nachbau | Ergebnis mit `--flaw`: Drei Lagerbewegungen fehlen, und die Rechnung trägt Steuer 108.33 statt 108.34 |

Die 11 Zeilen der Rechnung sind das, was der Bildschirm nicht zeigt: die Rechnung, ihre drei
Zeilen, der Zähler, die drei geänderten Bestände und die drei Lagerbewegungen.

## Die absichtlichen Fehler

Mit `SAMPLE_FLAW=1` gestartet,

- rundet die Browser-Variante die Steuer je Rechnungszeile statt einmal je Steuerklasse, und
- schreibt sie keine Lagerbewegungen.

Beide Seiten sehen aus wie zuvor. `verify db` meldet beides, Zeile für Zeile; der Bericht ist
unter [Vergleichstests](verify.md) abgebildet.

## Die Teile von Hand ausführen

Wie man die Datenbank anlegt, das Windows-Programm startet und die Browser-Variante selbst
startet, steht in `examples/sample-app/README.md` und `examples/sample-app/web/README.md`.
Fenstertitel, Texte der Steuerelemente und Element-Kennungen für die Automatisierung stehen in
`examples/sample-app/CONTROLS.md`.

## Tests

| Test | Prüft |
|---|---|
| `PipelineTests.SampleEndToEndOnPlainPostgres` | den Lauf oben, treu und mit Fehlern |
| `node --test` in `examples/sample-app/web` | Rechenfälle, die HTTP-Schnittstelle mit Datenbank, Sprachressourcen, die aus der Web-Vorlage übernommenen Dateien |
| `bin\sample-desktop.exe --selftest examples\sample-app\test-vectors.json` | die Rechnung des Windows-Programms gegen dieselben Fälle |
