# SiteSense

## What SiteSense is
Multi-tenant .NET 10 backend for GPS tracking of workers on outdoor sites.
It imports site maps, ingests and filters positions, routes pedestrians (including evacuation), and detects anomalies with ML.NET.
It pushes real-time events and enforces strict tenant isolation and privacy. Git history must be preserved (no squash, no rewrite).

## Language
- Everything in English: code, comments, commits, PRs, issues, reviews, docs, skills.
- Use English even when the user writes in Italian.
- Only exception: the user explicitly asks for another language.

## Session start
1. Follow the session-start steps in `.claude/skills/ai-process/SKILL.md`.
2. Load the skills the task needs from the map below.

## Doubt = ask
- Anything ambiguous: ask before starting. Never assume.
- Ask short, specific questions. Prefer multiple choice.
- A spec gap or ambiguity: never pick an assumption yourself. Ask the user, offering options
  with a recommended one. The user decides; only then record their decision in `ASSUMPTIONS.md`
  (format in `ai-process`).

## Private folder
- `local/` is a private, git-excluded working folder (notes and the original brief).
- Never commit anything under `local/`.
- Never quote it verbatim.

## Skills live in the repo
- Skills sit in `.claude/skills/`, so reviewers see the AI process.
- **Proposed (Q56, open):** personal rules stay in the user's own config, not in the repo.

## Skill map

| Path | Load when |
|---|---|
| `.claude/skills/sitesense-context/SKILL.md` | You need the problem, domain terms, data formats or priorities |
| `.claude/skills/sitesense-security/SKILL.md` | You touch endpoints, roles, tenants, privacy or auth |
| `.claude/skills/sitesense-architecture/SKILL.md` | You add a project, folder, endpoint or store document |
| `.claude/skills/dotnet-coding/SKILL.md` | You write or review C# code |
| `.claude/skills/sitesense-testing/SKILL.md` | You write tests, benchmarks or run test commands |
| `.claude/skills/sitesense-stack/SKILL.md` | You pick a library, a version or a dependency |
| `.claude/skills/ai-process/SKILL.md` | You plan, delegate to subagents, verify, log decisions or use memory |
| `.claude/skills/delivery-workflow/SKILL.md` | You branch, commit, open PRs, review or close the project |
| `.claude/skills/clear-description/SKILL.md` | You write a PR description |

## One rule, one place
- Each rule lives in exactly one skill.
- Link to the skill. Never copy its rules here or into other skills.
