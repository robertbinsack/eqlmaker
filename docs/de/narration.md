<!-- SPDX-License-Identifier: AGPL-3.0-or-later -->
[English](../narration.md) · [Deutsch](narration.md)

# Vertonung

`narrate` legt einen gesprochenen Kommentar über ein fertiges Demo-Video. Die Stimme beschreibt
Schritt für Schritt, was auf dem Bildschirm geschieht, und spricht auch in ruhigen Passagen
weiter, sodass ein natürlicher Redefluss entsteht. Auf Wunsch läuft Hintergrundmusik darunter;
sie wird beim Sprechen zusätzlich abgesenkt. Das Bild des Videos bleibt unverändert.

Die Sprache wird lokal mit [Fish Speech](https://github.com/fishaudio/fish-speech) und einer
eigenen Stimmreferenz erzeugt. Nichts wird an einen Cloud-Dienst geschickt.

## Voraussetzung

Einzige Voraussetzung ist ein laufender Fish-Speech-Server mit seiner HTTP-Schnittstelle
(`POST /v1/tts`) und einer dort registrierten Stimmreferenz (eine `reference_id`, aufgeführt von
`GET /v1/references/list`). FFmpeg ist im Paket enthalten. Bevor etwas erzeugt wird, prüft
`narrate` den Server (`/v1/health`) und die Stimme; fehlt eines davon, bricht es mit einer klaren
Meldung ab und weicht nie auf eine andere Stimme aus.

**Für die Lizenzen sind Sie verantwortlich.** Verwenden Sie nur Stimmreferenzen, die Sie nutzen
dürfen. Die Modellgewichte von Fish Speech stehen unter einer eigenen Lizenz (das Modell s2-pro
zum Beispiel unter einer Forschungslizenz, die kommerzielle Nutzung ohne gesonderte Vereinbarung
nicht erlaubt). Auch für Musik brauchen Sie die Rechte für die geplante Verwendung.

## Schritte

```text
eqlmaker narrate-draft captions.srt --video demo.mp4 --lang en --chars-per-second 15
# write the "text" of every segment in captions.narration.json
eqlmaker narrate --video demo.mp4 --script captions.narration.json --voice my-voice --out demo_narrated.mp4 --music music.mp3 --music-volume 25
```

1. **Untertitel-Zeitachse.** `narrate-draft` liest, was das Video bereits über seinen Ablauf
   weiß: eine Untertiteldatei (SRT, WebVTT), ein whisper-Transkript oder eine JSON-Liste von
   Abschnitten mit Anfang, Ende, Text und optionalen Notizen (zum Beispiel den Aktionen der
   Aufnahme). Die Demo-Videos dieses Projekts schreiben eine solche Liste beim Schnitt.
2. **Skript-Gerüst.** Der Entwurf deckt das ganze Video lückenlos ab: Der erste Abschnitt
   beginnt bei 0, Lücken gehen an den vorherigen Untertitel, der letzte Abschnitt endet mit dem
   Video, Untertitel länger als `--max-segment` werden geteilt. Jeder Abschnitt erhält einen
   `hint` (Untertitel und Notizen) und ein `budget_chars`: wie viele Zeichen die Stimme in dieser
   Zeit spricht.
3. **Texte.** Ein Mensch oder eine KI schreibt den gesprochenen Text jedes Abschnitts, höchstens
   `budget_chars` Zeichen: was zu sehen ist und warum es wichtig ist, mit passendem Hintergrund,
   wo das Bild ruhig ist. Ein leerer Text bedeutet Stille.
4. **Synthese, Einpassen, Mischen.** `narrate` spricht jeden Abschnitt, misst die Clips, setzt
   sie ein, mischt und schreibt das Video.

## Skriptformat

```json
{
  "format": "eqlmaker-narration", "version": 1, "language": "en", "chars_per_second": 15,
  "video_duration": 140.68,
  "segments": [
    { "start": 0, "end": 4.8, "budget_chars": 66, "hint": "Open the artifact ...", "text": "First, we install the app." }
  ]
}
```

Abschnitte sind sortiert und dürfen sich nicht überlappen. `budget_chars` und `hint` sind nur
Hilfen für die Autorin oder den Autor.

## Wie die Clips eingepasst werden

| Regel | Wert |
|---|---|
| Anfragen | eine je Abschnitt; längere Texte werden an Absätzen und Sätzen geteilt (höchstens 500 Zeichen je Anfrage) |
| Stille | an beiden Enden unter -45 dBFS entfernt, 60 ms bleiben; 0,45 s nach einem Absatz, 0,2 s zwischen Satzgruppen |
| Pegel | Spitze höchstens -1 dBFS (nur abgesenkt, nie angehoben) |
| Beginn | am Anfang des Abschnitts oder 0,15 s nach dem vorherigen Clip |
| Platz | bis zum Anfang des nächsten Abschnitts mit Text (oder bis zum Ende des Videos) |
| Zu lang | ohne Änderung der Tonhöhe beschleunigt, bis `--max-tempo` (Vorgabe 1.1) |
| Immer noch zu lang | es wird kein Video geschrieben; die Meldung nennt jeden Abschnitt und etwa wie viele Zeichen zu kürzen sind |

Clips werden in `<out>.narration/clips/` nach Text und Stimme aufbewahrt; nach dem Kürzen eines
Textes wird nur dieser Abschnitt neu erzeugt.

## Mischen

| Teil | Wie |
|---|---|
| Sprache | voller Pegel |
| Musik | auf die Länge des Videos wiederholt oder gekürzt, mit `--music-volume` Prozent ihres eigenen Pegels (Vorgabe 25), ein- (1,5 s) und ausgeblendet (3 s) |
| Absenken | die Musik wird während der Sprache zusätzlich komprimiert (Sidechain); `--no-duck` schaltet das ab |
| Ausgabe | Bild kopiert, Ton AAC 192 kbit/s, gleiche Länge wie das Video |

## Bericht

`<out>.narration/report.json` führt jeden Clip auf (Abschnitt, Zeichen, gemessene Länge, Beginn,
Ende, Tempo, gemessene Zeichen je Sekunde) und die Abdeckung: Sekunden mit Sprache, Anteil des
Videos mit Sprache, längste Pause und ob sich Clips überlappen.

## Konfiguration

Vorgaben können in `eqlmaker.json` stehen, damit die Kommandozeile sie nicht braucht:

```json
{ "narration": { "ttsUrl": "http://127.0.0.1:8080", "voices": { "de": "my-german-voice", "en": "my-english-voice" }, "charsPerSecond": { "de": 13, "en": 15 } } }
```

Die Stimme kommt aus `--voice`, sonst aus `narration.voices.<language>` (Sprache aus `--lang`
oder dem Skript), sonst aus `narration.voice`.
