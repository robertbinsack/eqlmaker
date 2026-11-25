<!-- SPDX-License-Identifier: AGPL-3.0-or-later -->
[English](../sources.md) · [Deutsch](sources.md)

# Ereignisquellen

Eine Ereignisquelle sagt EQLMaker, was eine Anwendung in der Datenbank getan hat, während sie
bedient wurde. Man wählt sie mit einer Zeile in `eqlmaker.json`; sonst ändert sich beim Wechsel
nichts:

```json
{ "source": "postgres", "connection": "host=127.0.0.1 port=5432 user=postgres dbname=postgres" }
```

| Quelle | Für | Liefert |
|---|---|---|
| `postgres` | Anwendungen, die mit PostgreSQL ab Version 15 sprechen | Anweisungen, Parameter, Fehler, Sitzungen und die Zeilen, die jede Transaktion geändert hat |
| `eqlserver` | dasselbe, dazu Anwendungen, die den Server über andere Datenbankprotokolle erreichen | das Obige, dazu Anweisungen der anderen Protokolle |
| ein Plugin | alles andere | was das Plugin schreibt, siehe [Plugins](plugins.md) |

Was jede Quelle vom Ereignisformat füllt, steht unter [Ereignisformat](event-format.md). Anwendungen, die nicht PostgreSQL sprechen: siehe [andere Datenbanken](other-databases.md).

## `postgres`

Die Quelle nutzt nur, was PostgreSQL mitbringt. Sie installiert nichts auf dem Server, ändert
keine serverweite Einstellung und startet den Server nie neu.

**Was sie tut, während eine Erfassung läuft**

1. Für jede Rolle und Datenbank, die unter `capture` genannt ist, setzt sie
   `log_statement = 'all'` mit `ALTER ROLE ... SET` oder `ALTER DATABASE ... SET`. Das gilt für
   Sitzungen, die danach beginnen: Starten Sie die Anwendung, nachdem die Aufnahme begonnen hat.
2. Sind die Meldungen des Servers nicht englisch, setzt sie für dieselben Rollen und Datenbanken
   zusätzlich `lc_messages = 'C'`, damit sie deren Protokollzeilen lesen kann. Die Aufnahme
   trägt eine Warnung, weil Fehlermeldungen, die die Anwendung anzeigt, währenddessen englisch
   sind.
3. Ist `capture.changes` wahr und läuft der Server mit `wal_level = logical`, legt sie je
   erfasster Datenbank einen logischen Replikations-Slot an (Name `eqlmaker_` plus zufällige
   Zeichen, Ausgabe-Plugin `test_decoding`).
4. Sie fragt `pg_stat_activity` mehrmals pro Sekunde, welche Sitzungen es gibt, um Benutzer,
   Datenbank und Anwendung jeder Sitzung zu benennen.
5. Sie misst die Differenz zwischen der Uhr des Servers und der Uhr dieses Rechners.

**Was sie tut, wenn die Erfassung endet**

1. Sie setzt die Einstellungen aus Schritt 1 und 2 zurück.
2. Sie liest den Teil des Serverprotokolls, der während der Erfassung geschrieben wurde, über
   die Datenbankverbindung (`pg_read_binary_file`); der Server darf also ein anderer Rechner
   sein. Schreibt der Server sein Protokoll nicht über den Logging Collector, nennen Sie die
   Protokolldatei in `capture.logFile`; sie muss dann von diesem Rechner aus lesbar sein.
3. Sie liest die Änderungen aus den Replikations-Slots und löscht die Slots.

**Was der Server bieten muss**

| Anforderung | Warum | Wenn sie fehlt |
|---|---|---|
| eine Superuser-Verbindung | Einstellungen von Rollen ändern und das Protokoll lesen | die Erfassung beginnt nicht und sagt, warum |
| `log_destination` enthält `stderr` | das Protokoll im Klartext wird gelesen | die Erfassung beginnt nicht |
| `log_line_prefix` enthält eine Zeit (`%m`, `%n` oder `%t`) und die Prozesskennung (`%p` oder `%c`) | um zu erkennen, wann und in welcher Sitzung eine Anweisung lief; die PostgreSQL-Vorgabe `'%m [%p] '` genügt | die Erfassung beginnt nicht |
| `wal_level = logical` | geänderte Zeilen | die Aufnahme hat Anweisungen, aber keine geänderten Zeilen, und sagt das; `verify db` braucht geänderte Zeilen |
| `log_line_prefix` enthält `%x` (optional) | exakte Verbindung zwischen Anweisungen und Transaktionen | die Verbindung entsteht über Zeit und Tabelle, siehe [Ablauf](pipeline.md) |

`wal_level` kann nur der Administrator des Servers ändern, und es braucht einen Neustart:

```sql
ALTER SYSTEM SET wal_level = logical;
```

**Geänderte Zeilen ohne `test_decoding`**

`test_decoding` ist ein optionales Modul von PostgreSQL; manche Server haben es nicht. Steht
`capture.decoding` auf `auto` (Vorgabe), fragt die Quelle den Server, ob das Modul da ist. Wenn nicht,
liest sie die geänderten Zeilen mit `pgoutput`, das jeder Server hat: Für jede mitgeschnittene
Datenbank legt sie eine Publikation `FOR ALL TABLES` und den Replikations-Slot gleichen Namens an
(`eqlmaker_` plus Zufallszeichen), liest die Änderungen in binärer Form und löscht beides, wenn der
Mitschnitt endet. `eqlmaker reset` löscht sie nach einem abgebrochenen Mitschnitt. Die Aufnahme nennt
in einer Warnung, welcher Weg genommen wurde. Mit `capture.decoding` auf `test_decoding` oder
`pgoutput` lässt sich einer erzwingen.

**Konfiguration**

```json
{
  "source": "postgres",
  "connection": "host=127.0.0.1 port=5432 user=postgres dbname=postgres",
  "capture": { "roles": [], "databases": ["appdb"], "changes": true, "logFile": "" }
}
```

Nennen Sie Rollen, Datenbanken oder beides. Das Passwort gehört nicht in diese Datei: Legen Sie
es in die Umgebungsvariable `EQLMAKER_PGPASSWORD`, oder nennen Sie mit `"passfile"` eine
Passwortdatei im pgpass-Format, siehe [Konfiguration](configuration.md). EQLMaker nimmt
`EQLMAKER_PGPASSWORD` nach dem Lesen aus seiner eigenen Umgebung, sodass die Anwendung, die es
startet, sie nicht erbt.

**Nach einer abgebrochenen Erfassung**

Wurde eine Aufnahme abgebrochen, können die Einstellungen aus Schritt 1 und ein Replikations-Slot
auf dem Server zurückbleiben. Ein Slot, den niemand liest, hindert den Server daran, altes
Write-Ahead-Log zu entfernen. Führen Sie aus:

```text
eqlmaker reset
```

Das setzt die Einstellungen zurück und löscht die Slots, deren Name mit `eqlmaker_` beginnt.

**Grenzen**

- Die Zeiten der Anweisungen sind auf die Millisekunde genau; das Serverprotokoll hat keine
  feinere Zeit.
- Eine Sitzung, die kürzer als etwa 100 ms dauert, kann enden, bevor sie in `pg_stat_activity`
  gesehen wird; der Name ihrer Anwendung ist dann unbekannt.
- Die logische Dekodierung sagt nicht, welche Sitzung eine Änderung gemacht hat. `correlate`
  verbindet Änderungen mit Anweisungen; ohne `%x` ist diese Verbindung eine Näherung.
- Änderungen anderer Sitzungen in einer erfassten Datenbank werden ebenfalls erfasst.
- `pgaudit` wird nicht gelesen.

## `eqlserver`

EQLServer ist ein kommerzieller Datenbankserver des Autors von EQLMaker, der auf PostgreSQL die
Protokolle mehrerer anderer Datenbanksysteme spricht. Diese Quelle liest dasselbe wie `postgres`
und, wenn ein Statistik-Port konfiguriert ist, die Anweisungsverfolgung, die der Server über
HTTP für Verbindungen anbietet, die nicht das PostgreSQL-Protokoll nutzen.

```json
{
  "source": "eqlserver",
  "connection": "host=127.0.0.1 port=5432 user=postgres dbname=postgres",
  "capture": { "roles": ["app_login"], "databases": ["appdb"], "changes": true, "logFile": "C:\\Server\\eqlserver-server.log" },
  "eqlserver": { "statsPort": 6433 }
}
```

- Alle Verbindungsdaten kommen aus `eqlmaker.json`; die Quelle liest keine Datei des Servers
  außer seinem Protokoll und der Verfolgungsdatei, die der Server nennt.
- Der Server nutzt den Logging Collector nicht: `capture.logFile` muss seine Protokolldatei
  nennen.
- Die Verfolgung wird mit `/trace/start` eingeschaltet und nach `/trace/stop` gelesen, am
  Statistik-Port auf 127.0.0.1; dieser Teil funktioniert also auf dem Rechner des Servers.
- Anweisungen der Verfolgung tragen Zeit, Sitzung, Benutzer, Anwendung und Text; Parameter,
  Tabellen, Zeilen und Transaktion sind `null`.
- Geänderte Zeilen brauchen `wal_level = logical` auf dem Server, wie bei PostgreSQL.

Wie es getestet ist: Die automatischen Tests lassen diese Quelle gegen reines PostgreSQL und
einen nachgestellten Statistik-Port laufen. Ein optionaler Ende-zu-Ende-Test lässt sie gegen das
Paket von EQLServer 1.10.0 laufen: Ein T-SQL-Client arbeitet über TDS, und die Zeitachse enthält
seine Stapel, seine Aufrufe, einen Fehler und die geänderten Zeilen, siehe
[andere Datenbanken](other-databases.md). Dieser Server hat kein Modul `test_decoding`, die
geänderten Zeilen kommen daher über `pgoutput`.
