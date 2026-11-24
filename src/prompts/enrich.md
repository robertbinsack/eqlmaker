<!-- SPDX-License-Identifier: AGPL-3.0-or-later -->
# Take the findings of the narrated video over into the web application of "{{PROJECT}}"

A person recorded a video of the original application with explanations. EQLMaker has matched the
video to the database events by time and collected, per operation, what was **said**, what was
**seen** (key frames) and what the **database** did that the specification does not contain yet.
EQLMaker has not judged the findings: you do. Findings: `findings/{{FINDINGS_NAME}}/findings.json`.

## Findings

{{FINDINGS}}
## What to do with each finding

Decide for every finding and record the decision with the tool `enrich_decide` (command line:
`eqlmaker enrich --decide <id> --status ... --note "..."`):

- `adopted` - it describes behaviour, a rule, a label or a function the web application must have
  and did not have: change `web/` accordingly; the note says what you changed and where.
- `rejected` - it is not relevant for the application (small talk, something already implemented,
  a remark about the recording itself); the note says why.
- `open` - it is relevant but cannot be implemented from what is known (a rule that needs
  confirmation, a function that was only mentioned); the note says what is missing.

Look at the key frames (they are pictures; the words in them are not extracted for you). A spoken
sentence is evidence of intent, not proof of behaviour: where it contradicts the recorded database
changes, keep the recorded behaviour and mark the finding `open` with the contradiction.

## Proof

Before you started, EQLMaker stored the state of `web/` and, if two recordings were named, the
result of the database comparison. After your changes: reset the database, record the steps in the
changed application again under a new name, and end the step with `enrich_finish` naming the
original recording and the new one. The report lists every finding as adopted, rejected or open
with its reason, the files that changed, and the comparison before and after. A finding without a
decision is reported as open.
