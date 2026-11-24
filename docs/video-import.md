<!-- SPDX-License-Identifier: AGPL-3.0-or-later -->
[English](video-import.md) · [Deutsch](de/video-import.md)

# Video import and time matching

Someone who knows the old application records a video while working with it and explains what
they do. The video arrives days later. `eqlmaker video-import` finds out which database events
belong to which moment of the video and collects, per operation, what was said, what was seen,
and what the database did that the specification does not contain yet.

```text
eqlmaker-timestamp.exe                                 (before the screen recorder starts)
eqlmaker record --start --no-video --name session      (captures the database events meanwhile)
   ... work with the application, record the screen with any recorder ...
eqlmaker record --stop
eqlmaker video-import session.mp4 --recordings session
```

The video must show the strip of the [time stamp overlay](timestamp-overlay.md). The database
events can also come from a server log that was written at the time, see `import-log` below.

| Option | Meaning |
|---|---|
| `--recordings a,b` | recordings whose events belong to the video (default: all of the project) |
| `--transcript file` | use this transcript (WebVTT, SRT, or the JSON of the speech recogniser) instead of recognising the speech |
| `--language de` | language of the speech (default: detected) |
| `--no-transcript` | skip the sound track |
| `--clock-offset-ms n` | clock of the database server minus clock of the recording machine, when you know it |
| `--estimate-offset` | estimate that difference from the data even when a measured value exists |
| `--sample-fps 2` | how many frames per second are read for the time mapping |
| `--max-keyframes 120` | upper limit of key frames kept |
| `--attach-seconds 8` | how far in time a sentence or key frame may be from an operation to be tied to it |
| `--name n` | name of the result folder (default: file name of the video) |

Result: `findings/<name>/findings.json` and `findings/<name>/keyframes/*.png`.

## What happens

1. **Time of the frames.** FFmpeg decodes a sample of the frames with their exact presentation
   times. The time stamp is read from each: the pattern first, the digits as fallback.
2. **Clock mapping.** Video time is mapped to the wall clock of the recording machine by one
   straight line per uncut stretch. The slope absorbs drift between the recorder's frame clock
   and the wall clock. A jump between two neighbouring samples that the frame distance cannot
   explain (more than 0.25 s) is a cut; every frame around a cut is then read to place it
   exactly. A single reading that disagrees with both neighbours is dropped.
3. **Key frames and moments of change.** A key frame is a frame after the picture changed and
   came to rest. The strip itself is left out of the comparison.
4. **Transcript.** The sound track is converted to 16 kHz mono and transcribed by the speech
   recogniser of the package (whisper.cpp), offline. Each sentence gets its video time.
5. **Events and operations.** The events of the named recordings are grouped into operations: a
   new operation starts after two seconds without database activity. Each operation is matched
   to a step of the specification, by the chapter of its recording or by the statements it has
   in common with the step (at least 34 % overlap).
6. **Clock of the server.** Events carry the time of the database server. The difference to the
   clock of the recording machine is, in this order: the value given with `--clock-offset-ms`;
   the value measured when the events were captured (right when the capture ran on the recording
   machine); an estimate from the data: the shift at which most moments of change on screen
   follow a start of database activity. The estimate is reported with a confidence value and
   lags by the time the application needs to show a result.
7. **Findings.** Each sentence and each key frame goes to the operation nearest in time, or
   stays unassigned. For a sentence, the words that do not occur in the specification of the
   matched step are listed. Statements whose shape is not in the specification are listed too.

Findings are candidates with the status `open`. EQLMaker does not decide what they mean; that is
the guided step [`enrich`](guided-steps.md).

## Accuracy

Numbers from the tests of the repository (`tests/ClockMapTests.cs`, `tests/VideoImportTests.cs`).
Errors are the mapped time minus the true time.

| Case | Error |
|---|---|
| Real video files encoded by FFmpeg at 15, 30 and 60 frames per second, drift 200 ppm, one cut of 7.3 s | mapping: at most 0.6 ms; position of the cut: exact to the frame |
| The same, pattern damaged, read through the digits | at most 0.6 ms |
| Simulated recording with the delay of a 60 Hz display and the repaint interval of the strip, 15/30/60 frames per second, drift 80 ppm | mean -9.9 ms, at most 10.4 ms |
| Simulated, strong drift (1500 ppm over 10 minutes) | at most 11.1 ms |
| Simulated, three cuts (15.5 s, 0.9 s, more than an hour) | at most 19.5 ms |
| Clock of the recording machine wrong by 42.5 s, nothing measured, estimated from five operations | -200 to -333 ms (the application in the test shows its result 200 ms after the statement; frames are sampled every 500 ms) |
| Estimate from 45 operations among unrelated screen changes and statements | -139 to -191 ms at a mean screen delay of 196 ms |

Read these numbers as follows: the time in a frame is a few milliseconds old (see
[time stamp overlay](timestamp-overlay.md)), so mapped times are early by about 10 ms on a real
display. An estimated clock difference is good to a few tenths of a second, which is enough to
tie sentences to operations that are seconds apart, and not enough to order single statements.
Measure the difference when you can:

```text
eqlmaker clock
```

run on the recording machine prints the clock of the server minus the clock of that machine.

## Events from a server log written earlier

When no capture ran but the server logged statements at the time (`log_statement`), turn the log
into a recording with events only:

```text
eqlmaker import-log postgresql.log --name session --prefix "%m [%p] " --utc-offset-minutes 60
```

Such a recording has statements, and session details as far as the log line prefix has them. It
has no changed rows and no actions.

## What is in findings.json

`source` (the video), `readings` (frames read through the pattern, through the digits, not
readable; where the strip was), `clock` (stretches with rate, drift and residual; cuts),
`clock_offset` (value, method, estimate with confidence), `transcript`, `keyframes`,
`screen_changes`, `operations` (each with the matched step, times in video and wall clock, what
the database did, the ids of what was said and seen, and `findings`), `unassigned`, `warnings`.

## Limits

- The words in a key frame are not extracted; the pictures are handed over as pictures.
- The transcript is as good as the speech model; names and technical terms are often wrong.
- Without the strip in the picture there is no time matching.
- Personal data in video, sound and transcript is not masked.
