<!-- SPDX-License-Identifier: AGPL-3.0-or-later -->
[English](narration.md) · [Deutsch](de/narration.md)

# Voice-over

`narrate` lays a spoken commentary over a finished demo video. The voice describes what happens
on the screen, step by step, and keeps talking through slow passages, so that the speech flows.
Optionally, background music runs underneath; it is lowered further whenever someone speaks.
The picture of the video is copied unchanged.

The speech is synthesised locally by [Fish Speech](https://github.com/fishaudio/fish-speech) with a
voice reference of your own. Nothing is sent to a cloud service.

## Prerequisite

The only prerequisite is a running Fish Speech server with its HTTP interface (`POST /v1/tts`)
and a voice reference registered on it (a `reference_id`, listed by `GET /v1/references/list`).
FFmpeg comes with the package. Before anything is synthesised, `narrate` checks the server
(`/v1/health`) and the voice; when one of them is missing, it stops with a clear message and never
falls back to another voice.

**Licences are your responsibility.** Use only voice references you have the right to use. The
Fish Speech model weights are under their own licence (the s2-pro model, for example, under a
research licence that does not allow commercial use without a separate agreement). Music needs
the rights for the use you have in mind as well.

## Steps

```text
eqlmaker narrate-draft captions.srt --video demo.mp4 --lang en --chars-per-second 15
# write the "text" of every segment in captions.narration.json
eqlmaker narrate --video demo.mp4 --script captions.narration.json --voice my-voice --out demo_narrated.mp4 --music music.mp3 --music-volume 25
```

1. **Caption timeline.** `narrate-draft` reads what the video already knows about its timing: a
   subtitle file (SRT, WebVTT), a whisper transcript, or a JSON list of segments with start, end,
   text and optional notes (for example the actions of the recording). The demo videos of this
   project write such a list when they are cut.
2. **Script skeleton.** The draft covers the whole video without gaps: the first segment starts at
   0, gaps go to the previous caption, the last segment ends with the video, captions longer than
   `--max-segment` are split. Every segment gets a `hint` (the caption and the notes) and a
   `budget_chars`: how many characters the voice speaks in that time.
3. **Texts.** A person or an AI writes the spoken text of every segment, at most `budget_chars`
   characters: what the viewer sees and why it matters, with fitting background where the
   screen is slow. An empty text means silence.
4. **Synthesis, fitting, mixing.** `narrate` speaks every segment, measures the clips and places
   them, then mixes and writes the video.

## Script format

```json
{
  "format": "eqlmaker-narration", "version": 1, "language": "en", "chars_per_second": 15,
  "video_duration": 140.68,
  "segments": [
    { "start": 0, "end": 4.8, "budget_chars": 66, "hint": "Open the artifact ...", "text": "First, we install the app." }
  ]
}
```

Segments are sorted and must not overlap. `budget_chars` and `hint` are only help for the author.

## How the clips are fitted

| Rule | Value |
|---|---|
| Requests | one per segment; longer texts are split at paragraphs and sentences (at most 500 characters per request) |
| Silence | trimmed at both ends below -45 dBFS, 60 ms kept; 0.45 s after a paragraph, 0.2 s between sentence groups |
| Level | peak at most -1 dBFS (only lowered, never raised) |
| Start | at the start of the segment, or 0.15 s after the previous clip |
| Room | up to the start of the next segment with text (or the end of the video) |
| Too long | sped up without changing the pitch, up to `--max-tempo` (default 1.1) |
| Still too long | no video is written; the message names every segment and about how many characters to cut |

Clips are kept in `<out>.narration/clips/` by text and voice, so after shortening a text only that
segment is synthesised again.

## Mixing

| Part | How |
|---|---|
| Speech | full level |
| Music | looped or cut to the length of the video, at `--music-volume` percent of its own level (default 25), faded in (1.5 s) and out (3 s) |
| Ducking | the music is compressed further while the speech runs (side chain); `--no-duck` switches it off |
| Output | picture copied, sound AAC 192 kbit/s, same length as the video |

## Report

`<out>.narration/report.json` lists every clip (segment, characters, measured length, start, end,
tempo, measured characters per second) and the coverage: seconds with speech, share of the video
with speech, longest pause, and whether clips overlap.

## Configuration

Defaults can be kept in `eqlmaker.json`, so that the command line does not need them:

```json
{ "narration": { "ttsUrl": "http://127.0.0.1:8080", "voices": { "de": "my-german-voice", "en": "my-english-voice" }, "charsPerSecond": { "de": 13, "en": 15 } } }
```

The voice is taken from `--voice`, else from `narration.voices.<language>` (language from `--lang`
or the script), else from `narration.voice`.
