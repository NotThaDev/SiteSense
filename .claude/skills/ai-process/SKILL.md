---
name: ai-process
description: >-
  How AI is used on SiteSense: session start, the direct vs plan/validate/implement/verify
  workflow with models, subagent prompt rules, context and tool-output economy, claude-mem
  memory, handoff.md, and where decisions and AI usage are recorded (ADR, ASSUMPTIONS,
  KNOWN_ISSUES, DATA_SOURCES, AI_LOG, RESULTS). Use when starting a session, planning a
  change, spawning subagents, writing an ADR or AI_LOG entry, recording an assumption, or
  when the context gets long.
---

# AI process

Source: the spec §2 (rules), §9.2 (docs list) and §10 (AI_LOG format).
Definition of done, branches and PRs: `../delivery-workflow/SKILL.md`.
Coding rules for subagents: `../dotnet-coding/SKILL.md`.

## 1. Session start

Do these before any other tool call:

| # | Step | Done when |
|---|---|---|
| 1 | Check claude-mem for this project: use the `claude-mem:mem-search` skill (search, then timeline, then get_observations). | Relevant past decisions are known or "none found" is noted. |
| 2 | Say in one line which workflow applies: `direct` or `plan/validate/implement`. | The line is in the reply. |

If you skip the workflow, say why in one line. Example: "direct: single-file edit, 12 lines."

## 2. Choose the workflow

| Task | Workflow |
|---|---|
| Questions, git or gh operations, single-file edits under ~30 lines | `direct` (no subagents) |
| Code or config change in more than 1 file, or more than ~30 lines | `plan/validate/implement/verify` |
| Anything touching the frontend, endpoints or the build | `plan/validate/implement/verify` |

## 3. plan / validate / implement / verify

| Step | Who | Output | Rule |
|---|---|---|---|
| 1. Plan | Opus | Numbered steps, one file or one concern each, with "done when" per step | Main session. Lists exact file paths. |
| 2. Validate | Sonnet subagent | Gaps, risks, missing steps | Fix the plan before implementing. |
| 3. Implement | Haiku subagents | Changed files, one step per subagent | One small, verifiable step each. |
| 4. Verify | Haiku subagent (Sonnet only if the check needs judgment) | Short pass/fail report | Runs tests, build and browser checks. |

Rules:

- Track each step as a task (TaskCreate, TaskUpdate). One task per small, verifiable step.
- The main session reads only the verify report. It does not run the full test, build or browser pass itself.
- It may run one quick check itself (for example one test name). Anything bigger goes to verify.
- A failed verify: send the first error line to an implement subagent, then run verify again.
- Why: each main-session call re-reads the whole context. Verification in the main session is the largest cost.

### Verify report format

```text
RESULT: PASS | FAIL
tests: pass|fail (<count> run)
build: pass|fail
browser: pass|fail|n/a   screenshots: <paths or none>
FAIL 1: <file:line or test name> — <first error line>
```

Keep it under 15 lines. No full logs.

## 4. Subagent prompt rules

Every subagent prompt contains:

| Part | Content |
|---|---|
| Goal | One sentence. |
| Done when | A check the subagent can run. |
| Paths | Exact file paths and the relevant snippets, inline. |
| Constraints | What not to touch. |
| Steps | Numbered, no implicit knowledge. |
| Output | Required format, short. |

- Default model: Haiku.
- Escalate to Sonnet only if Haiku fails, or the task needs real reasoning.
- Never send a subagent the whole context. Send only what its step needs.

## 5. Context and tool-output economy

| Rule |
|---|
| Grep before you read. |
| Read line ranges, not whole files. |
| No large dumps: cap command output (`head`, `tail`, `-n`). |
| Do not re-read a file already in context. |
| Prefer Explore subagents for broad searches; they return conclusions, not file dumps. |

Browser checks:

| Rule |
|---|
| Prefer `read_page`, `find` and `get_page_text` over screenshots. |
| Screenshots at scale 0.5 or less, unless the image goes into a PR or is sent to the user. |
| Use `browser_batch` for multi-step flows. |

## 6. Context limit and handoff.md

At about 400k tokens, stop and:

1. Write `handoff.md` in the repo root with these sections:

| Section | Content |
|---|---|
| Goal | What the work is for. |
| Current state | Done, in progress, not started. |
| Decisions | Each with its reason. Link the ADR if one exists. |
| Files changed | Exact paths. |
| Next steps | Ordered. |
| Open questions | Ask the user, do not guess. |
| Commands | Test, build and run commands. |

2. Give the user a ready-to-paste prompt for the new session: `Read handoff.md, confirm the next step, then continue. Ask if anything is unclear.`
3. Tell the user to start a new session.

**Proposed:** `handoff.md` is git-ignored. It is a working file, not a deliverable.

## 7. Memory (claude-mem)

| When | Action |
|---|---|
| Session start | Check (step 1 in §1). |
| A durable decision or convention is made | Save it with claude-mem (`save_memory` or observation tools, as available). |
| Corrections from the user | Save them. |

Do not save secrets, credentials or private material the user asked you not to keep.

## 8. Where things go

| File | What goes in | When updated | Required |
|---|---|---|---|
| `docs/adr/NNNN-<slug>.md` | One decision with alternatives | When a decision is made | Yes, at least 3: path graph structure, position storage, anomaly detector parameters |
| `ASSUMPTIONS.md` | Each ambiguous point and the assumption taken | When an assumption is taken | Yes |
| `KNOWN_ISSUES.md` | Known limits and what to do with more time | At the end of each story, and when a limit is found | Yes |
| `DATA_SOURCES.md` | Source and licence of each external data item | When external data is added | Yes |
| `AI_LOG.md` | Significant AI use (format in §10) | At the end of each story | Yes |
| `docs/RESULTS.md` | Benchmark results (JDB-09) | Only if JDB-09 is implemented | Conditional |

ADR file names: four-digit number, then a short slug, for example `docs/adr/0001-path-graph-structure.md`.

## 9. ADR template

```markdown
# NNNN — <Title>

- Status: Proposed | Accepted | Superseded by NNNN
- Requirement IDs: <e.g. NAV-01>

## Context
<The problem, in 3–6 lines.>

## Decision
<One paragraph, present tense.>

## Alternatives considered
| Option | Pro | Con | Why not |
|---|---|---|---|

## Consequences
- <What gets easier.>
- <What gets harder.>
- <What we will revisit, and when.>
```

## 10. Entry formats

### ASSUMPTIONS.md entry

```markdown
## Qnn — <question>
- Assumption: <what we decided>
- Why: <reason, 1–2 lines>
- Requirement IDs: <IDs>
- Status: Open | Accepted | Changed
```

### AI_LOG.md entry (spec §10)

```markdown
## <date> — <story or decision>
- Goal: <what we wanted>
- Prompt (summary): <short, no full prompt>
- AI produced: <files or decisions>
- Corrected or discarded: <what changed, and why>
```

**Proposed (Q58, open):** one AI_LOG entry per story, plus one per significant AI-assisted decision (for example each ADR). Update it at the end of the story, not after the fact.

## 11. Checklist for a session

- [ ] Session start done (§1)
- [ ] Workflow line said (§1, §2)
- [ ] Plan validated before implementation (§3)
- [ ] Verify report read, not the raw logs (§3)
- [ ] Decisions in an ADR, assumptions in ASSUMPTIONS.md, AI use in AI_LOG.md (§8)
- [ ] Context under the limit, or handoff.md written (§6)
