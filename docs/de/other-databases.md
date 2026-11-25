<!-- SPDX-License-Identifier: AGPL-3.0-or-later -->
[English](../other-databases.md) · [Deutsch](other-databases.md)

# Anwendungen auf anderen Datenbanken

## Das Prinzip

Ein Nachbau lässt sich nur belegen, wenn das, was eine Person am Bildschirm tut, und das, was die
Anwendung in der Datenbank tut, auf **einer Zeitachse** zu sehen sind. „Dieser Klick hat diese
Zeilen geschrieben“ ist der Satz, den die Vergleichstests von EQLMaker für das Original und für
den Nachbau prüfen ([Vergleichstests](verify.md)). EQLMaker misst die Datenbankseite auf
PostgreSQL. Für jede Anwendung lautet die Frage also: Liegen ihre Daten in einem PostgreSQL, das
EQLMaker beobachten kann?

| Die Anwendung benutzt | Was zu tun ist | Was EQLMaker sieht |
|---|---|---|
| PostgreSQL | nichts: ein normales PostgreSQL, siehe [Ereignisquellen](sources.md) | Anweisungen, Parameter, Fehler, Sitzungen, geänderte Zeilen |
| SQL Server, MySQL oder MariaDB, Oracle, SQLite, Firebird oder InterBase, SAP SQL Anywhere, Advantage, Informix, Access, SAP oder Sybase ASE | die Datenbank durch einen Server ersetzen, der die Anwendung unverändert annimmt und in PostgreSQL speichert, zum Beispiel EQLServer | dasselbe, soweit die Tabelle unten es sagt |
| eine Datenbank, die sich nicht ersetzen lässt, oder kein Zugriff darauf | nur mit Bildschirm, Video und Handbuch arbeiten | keine Datenbankereignisse; siehe „Ohne Datenbankzugriff“ |

[EQLServer](https://eqlserver.com) ist ein kommerzieller Datenbankserver des Autors von EQLMaker.
Er ist ein eigenes Produkt, optional, und **EQLMaker braucht ihn nicht**: Alles in diesem Repository
läuft mit einem normalen PostgreSQL. Andere Datenbanken brauchen einen Server, der in PostgreSQL
speichert, damit EQLMaker die Transaktionen sehen kann. EQLServer ist ein solcher Server; EQLMaker liest
ihn mit der Quelle `eqlserver`, und den Server eines anderen Herstellers könnte ein
[Quellen-Plugin](plugins.md) lesen.

## Schritte mit EQLServer

1. EQLServer auf dem Rechner starten, auf dem die Anwendung läuft, oder auf einem anderen. Für einen
   Test auf einem Rechner in der Konfigurationsdatei des Servers `ListenAddresses` auf `localhost` setzen.
2. Die Anwendung so auf ihn richten, wie sein Handbuch es beschreibt: derselbe Treiber, dieselbe
   Art Verbindungszeichenfolge, der Port des Protokolls der ursprünglichen Datenbank. Die Anwendung
   wird nicht verändert.
3. Geänderte Zeilen brauchen logische Dekodierung, die EQLServer von PostgreSQL erbt. Einmalig:
   `ALTER SYSTEM SET wal_level = logical;` als Administrator, danach den Server neu starten
   (`eqlserverctl restart`). Ohne das zeichnet EQLMaker Anweisungen auf und sagt, dass geänderte
   Zeilen fehlen; `verify db` braucht sie.
4. Die Projektkonfiguration schreiben. Die Anmeldung ist der PostgreSQL-Superuser des Servers
   (EQLMaker ändert `log_statement` der mitgeschnittenen Rollen und liest das Protokoll).

```json
{
  "source": "eqlserver",
  "connection": "host=127.0.0.1 port=5432 user=postgres dbname=postgres",
  "capture": { "roles": ["app_login"], "databases": ["eqlserver"], "changes": true,
               "logFile": "C:/EQLServer/eqlserver-server.log" },
  "eqlserver": { "statsPort": 7434 }
}
```

   - `roles` sind die Logins, die die Anwendung benutzt, `databases` die PostgreSQL-Datenbank, in der
     die Daten liegen (so benennen, wie `psql -l` sie zeigt).
   - `logFile` ist das Protokoll des Servers: EQLServer benutzt den Logging-Collector nicht, er
     schreibt `eqlserver-server.log` in seinen Ordner.
   - `statsPort` ist der Statistikport des Verbindungspools; das Handbuch des Servers sagt, wo man
     ihn findet. Dort wird der Anweisungsmitschnitt der T-SQL-Verbindungen gelesen.
   - Das Passwort der Verbindung gehört in die Umgebungsvariable `EQLMAKER_PGPASSWORD`, siehe
     [Konfiguration](configuration.md).
5. `eqlmaker record --start`, die Anwendung starten, arbeiten, `eqlmaker record --stop`, dann
   `eqlmaker correlate <Aufnahme>` und `eqlmaker spec`. Die Anwendung nach dem Start der Aufnahme
   starten: Der Mitschnitt gilt für Sitzungen, die danach beginnen.

## Was EQLMaker sieht

| | T-SQL (TDS) | PostgreSQL-Protokoll | andere Protokolle |
|---|---|---|---|
| Text der Anweisung | Stapel als `statement`, Aufrufe mit Parametern (RPC) als `execute`, aus dem Anweisungsmitschnitt des Servers | aus dem Protokoll des Servers | hängt vom Server ab; hier nicht getestet |
| Parameter eines Aufrufs | nicht sichtbar (`params` ist `null`) | sichtbar bei Anweisungen, die mit Parametern gesendet wurden | hier nicht getestet |
| Benutzer, Anwendung, Sitzung | ja | ja | hier nicht getestet |
| geänderte Zeilen | ja, aus der logischen Dekodierung der Datenbank, in der der Server speichert | ja | ja, gleich welches Protokoll: Die Zeilen liegen in PostgreSQL |

Was beim Lesen des Ergebnisses zu wissen ist:

- Eine Anweisung nennt die T-SQL-Datenbank der Anwendung (`shopdb`), eine geänderte Zeile die
  physische. Im Mehrdatenbank-Modus des Servers heißt die physische Tabelle
  `<Datenbank>_<Schema>.<Tabelle>` (`shopdb_dbo.customer`). `correlate` kennt das und ordnet eine
  Transaktion der Anweisung derselben Datenbank und Tabelle zu.
- Die Zeiten des Mitschnitts haben Millisekunden-Genauigkeit. Laufen zwei Anweisungen einer Sitzung
  in derselben Millisekunde, wird eine Transaktion der letzten Anweisung vor ihrem Commit
  zugeordnet, die in die Tabelle schreibt (Verfahren `time+table`, siehe [Ablauf](pipeline.md));
  das muss nicht die richtige der beiden sein.
- `eqlmaker export-sql <Aufnahme>` schreibt die Anweisungen als `sql.tsv`; Stapel und Aufrufe des
  Mitschnitts haben die Arten `tsql-batch` und `tsql-rpc`.

## Was getestet ist und was nicht

Getestet durch `tests/eqlserver/run-eqlserver-e2e.ps1`, optional, gegen einen laufenden EQLServer
1.10.0 (gelaufen auf 127.0.0.1 mit eigenen Ports):

- Ein T-SQL-Client (`System.Data.SqlClient`) macht Einfügungen, eine Änderung, eine Löschung und
  eine fehlschlagende Anweisung über TDS, während `eqlmaker record --scenario` mitschneidet;
- die Aufnahme enthält die Stapel und die Aufrufe mit Benutzer, Datenbank und Anwendung, den
  Fehler und die geänderten Zeilen mit ihren Werten;
- `correlate` bildet drei Kapitel und ordnet jede Transaktion ihrer Anweisung nach Zeit und
  Tabelle zu; `export-sql` schreibt Zeilen `tsql-batch`, `tsql-rpc` und `error`; `spec` nennt die
  Tabelle;
- `eqlmaker reset` hinterlässt keinen Replikations-Slot, keine Publikation und keine
  `log_statement`-Einstellung;
Der Server des Tests hat kein Modul `test_decoding`. EQLMaker liest die geänderten Zeilen dann mit
dem Ausgabe-Plugin `pgoutput`, das jedes PostgreSQL enthält; siehe [Ereignisquellen](sources.md).

Das Skript installiert, startet und stoppt keinen Server. Es braucht einen laufenden: den
Anweisungsverlauf auf dessen Statistik-Port, `wal_level = logical`, ein T-SQL-Login, das eine
Datenbank anlegen darf, und einen PostgreSQL-Superuser. Die Ports und, nur über die Umgebung, die
beiden Kennwörter angeben (`EQLMAKER_E2E_TDS_PORT`, `EQLMAKER_E2E_PG_PORT`,
`EQLMAKER_E2E_STATS_PORT`, `EQLMAKER_E2E_TDS_PASSWORD`, `EQLMAKER_E2E_PG_PASSWORD`; optional
`EQLMAKER_E2E_HOST`, `EQLMAKER_E2E_DATABASE`, `EQLMAKER_E2E_TDS_USER`, `EQLMAKER_E2E_PG_USER`,
`EQLMAKER_E2E_PSQL`, `EQLMAKER_E2E_LOGFILE`; die Parameter des Skripts heißen genauso, ohne das
Präfix). `bin\eqlmaker-tests.exe` ruft das Skript auf, wenn `EQLMAKER_E2E_TDS_PORT` gesetzt ist.
Ohne die Ports wird der Test übersprungen.

**Nicht getestet:** die Protokolle von MySQL und MariaDB, Oracle, SQLite, Firebird und InterBase,
SAP SQL Anywhere, Advantage, Informix, Access sowie SAP oder Sybase ASE gegen den Mitschnitt von
EQLMaker; eine Anwendung mit Bildschirm (der Testclient ist ein Skript); ein Server auf einem
anderen Rechner; TLS zwischen Anwendung und Server; andere Versionen von EQLServer als 1.10.0;
EQLServer als Windows-Dienst. Was EQLServer selbst für diese Protokolle testet, beschreibt seine
eigene Dokumentation.

## Ohne Datenbankzugriff

EQLMaker arbeitet auch dann mit Bildschirm, Video und Handbuch: Aufzeichnung der Bedienung, das
Handbuch, das Video mit Erklärung, die Spezifikation der Masken und ihrer Steuerelemente, der
Nachbau als geführter Schritt und der Bildvergleich `verify screens`. Nicht möglich ist dann der
Vergleich der Daten: `verify db` vergleicht die gemessenen Änderungen zweier Aufnahmen, und ohne
sie gibt es nichts zu vergleichen. Der Nachbau wird dann durch Ansehen geprüft, nicht durch
Messen.
