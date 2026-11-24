<!-- SPDX-License-Identifier: AGPL-3.0-or-later -->
[English](../event-format.md) · [Deutsch](event-format.md)

# Ereignisformat v0.1

EQLMaker schreibt, was eine Anwendung in der Datenbank getan hat, als Ereignisse, ein
JSON-Objekt je Zeile (JSON Lines, UTF-8, ohne Byte-Order-Mark). Die Datei einer Aufnahme heißt
`events.jsonl`. Jede Ereignisquelle schreibt dieses Format, auch Plugins, die eine eigene Quelle
mitbringen.

Dies ist Version 0.1. Sie beschreibt, was EQLMaker heute liest und schreibt. Eine eigenständige,
versionierte Spezifikation für andere Werkzeuge (v1.0) ist geplant und nicht Teil dieser
Version; bis dahin ist mit Änderungen zu rechnen.

## Beispiel

```json
{"v":"0.1","seq":17,"ts":1795630831123000,"time":"2026-11-25T18:20:31.123000Z","offset":12.481,"kind":"execute","source":"postgres","via":"log","session":"4711","user":"sample_app","database":"eqlmaker_sample","application":"sample-desktop","sql":"INSERT INTO customer (name, city) VALUES ($1, $2)","params":["Harbour Books","Rostock"],"tx":null,"tables":null,"rows":null,"error":null}
{"v":"0.1","seq":18,"ts":1795630831124377,"time":"2026-11-25T18:20:31.124377Z","offset":12.482377,"kind":"transaction","source":"postgres","via":"replication","session":null,"user":null,"database":"eqlmaker_sample","application":null,"sql":null,"params":null,"tx":"739","tables":["public.customer"],"rows":[{"table":"public.customer","op":"insert","new":{"id":"9","name":"Harbour Books","city":"Rostock"},"old":null}],"error":null}
```

## Felder

Jedes Ereignis hat jedes Feld. Ein Feld, das eine Quelle nicht liefern kann, ist `null`; es wird
nie weggelassen und nie mit einer Vermutung gefüllt.

| Feld | Typ | Bedeutung |
|---|---|---|
| `v` | Zeichenfolge | Version des Formats, `"0.1"`. |
| `seq` | Ganzzahl | Position des Ereignisses in seiner Datei, beginnend bei 1. |
| `ts` | Ganzzahl | Zeit in Mikrosekunden seit 1970-01-01 00:00:00 UTC, auf der Uhr des Datenbankservers. |
| `time` | Zeichenfolge | Dieselbe Zeit als ISO-8601-Text in UTC, für Menschen. Gerechnet wird mit `ts`. |
| `offset` | Zahl oder null | Sekunden seit dem Start der Aufnahme, auf der Uhr des Aufnahmerechners. Der Rekorder berechnet sie aus `ts` und der gemessenen Differenz der beiden Uhren. `null` außerhalb einer Aufnahme. |
| `kind` | Zeichenfolge | `statement` (eine als Text gesendete Anweisung), `execute` (eine mit Parametern ausgeführte Anweisung), `error` (der Server hat eine Anweisung abgelehnt), `transaction` (eine abgeschlossene Transaktion, die Zeilen geändert hat). |
| `source` | Zeichenfolge | Name der Ereignisquelle: `postgres`, `eqlserver` oder der Name eines Plugins. |
| `via` | Zeichenfolge | Wie die Quelle das Ereignis gesehen hat: `log` (Serverprotokoll), `replication` (logische Dekodierung), `trace` (Anweisungsverfolgung des Servers). |
| `session` | Zeichenfolge oder null | Sitzung der Anwendung auf dem Server (PostgreSQL: Prozesskennung des Backends). |
| `user` | Zeichenfolge oder null | Anmeldename der Sitzung. |
| `database` | Zeichenfolge oder null | Datenbank der Sitzung. |
| `application` | Zeichenfolge oder null | Name, den die Anwendung dem Server genannt hat (`application_name`). |
| `sql` | Zeichenfolge oder null | Text der Anweisung. Bei `error`: die fehlgeschlagene Anweisung, wenn der Server sie meldet. |
| `params` | Liste oder null | Werte der Parameter als Text, in ihrer Reihenfolge; ein Eintrag `null` ist SQL NULL. `null`, wenn die Anweisung keine Parameter hatte oder die Quelle sie nicht sehen kann. |
| `tx` | Zeichenfolge oder null | Transaktionskennung des Servers. |
| `tables` | Liste oder null | Bei `transaction`: die geänderten Tabellen, als `schema.table`. Gemessen, nicht aus dem SQL-Text abgeleitet. |
| `rows` | Liste oder null | Bei `transaction`: die geänderten Zeilen in der Reihenfolge der Änderungen. Jede Zeile: `table`, `op` (`insert`, `update`, `delete`, `truncate`), `new` (Spalte: Wert nach der Änderung) und `old` (Spalte: Wert vor der Änderung, soweit der Server ihn liefert). Werte sind Text; SQL NULL ist `null`. |
| `error` | Zeichenfolge oder null | Bei `error`: die Meldung des Servers. |

Reihenfolge: Ereignisse sind nach `ts` sortiert; Ereignisse mit gleichem `ts` behalten die
Reihenfolge, in der die Quelle sie geliefert hat. Eine Anweisung steht vor der Transaktion, zu
der sie gehört, weil der Server eine Anweisung bei ihrem Beginn protokolliert und eine
Transaktion bei ihrem Abschluss meldet.

## Welche Quelle was liefert

| Feld | `postgres` | `eqlserver`, Teil der Anweisungsverfolgung | Später importierte Protokolldatei (`import-log`) |
|---|---|---|---|
| `ts` | Anweisungen: auf die Millisekunde genau (das Serverprotokoll hat keine feinere Zeit); Transaktionen: Mikrosekunden (Zeit des Abschlusses) | Millisekunden | wie das Protokoll sie hat |
| `session` | ja | ja | ja |
| `user`, `database`, `application` | ja. Bei einer Sitzung, die kürzer als etwa 100 ms dauerte: `database` oder `user` nur, wenn genau eine Datenbank oder Rolle erfasst wird, `application` ist `null` | `user` und `application` ja; `database`, soweit der Server sie meldet | nur, wenn `log_line_prefix` `%u`, `%d`, `%a` enthält |
| `sql` | ja | ja | ja |
| `params` | bei Anweisungen, die mit Parametern gesendet wurden | `null` | wenn das Protokoll die Parameterzeilen hat |
| `tx` | Transaktionen: ja. Anweisungen: nur, wenn `log_line_prefix` `%x` enthält | `null` | nur mit `%x` |
| `tables`, `rows` | wenn der Server mit `wal_level = logical` läuft; sonst gar keine Ereignisse `transaction`, und die Aufnahme sagt das | `null` | `null` |
| `error` | ja | `null` | ja |

Die Quelle `eqlserver` liefert alles, was die Quelle `postgres` liefert, für Verbindungen, die
das PostgreSQL-Protokoll sprechen, und ergänzt für die übrigen Verbindungen den Teil der
Anweisungsverfolgung. Siehe [Ereignisquellen](sources.md).

Die Werte `old` von Zeilen `update` und `delete` folgen der `REPLICA IDENTITY` der Tabelle: in
der Vorgabe die Schlüsselspalten, mit `REPLICA IDENTITY FULL` die ganze Zeile.

## Das Format lesen

- Zeile für Zeile lesen; leere Zeilen überspringen.
- Eine Datei ablehnen, deren `v` eine Hauptversion hat, die Sie nicht kennen. EQLMaker 0.1 liest
  `0.x`.
- Felder ignorieren, die Sie nicht kennen.
- Sich nicht darauf verlassen, dass `seq` über zusammengeführte Dateien hinweg lückenlos ist.

## Verwandte Dateien einer Aufnahme

`actions.jsonl` enthält, was bedient wurde (Klicks, getippter Text, Kapitelmarken), mit
derselben Skala für `ts` und `offset`, aber auf der Uhr des Aufnahmerechners. `timeline.json`,
geschrieben von `eqlmaker correlate`, verbindet Bedienhandlungen, Anweisungen und Transaktionen.
Beide sind unter [Ablauf](pipeline.md) beschrieben.
