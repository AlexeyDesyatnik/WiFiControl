---
name: ask-matt
description: Choose a skill available in this repository for the current task.
disable-model-invocation: true
---

# Choose a skill

Pick the skill that fits the current task and read its `SKILL.md` before recommending it.

| Task | Skill |
| --- | --- |
| Stress-test a plan or design | `grilling`, or `grill-with-docs` when the discussion should update project decisions or terms |
| Clarify domain language or record an ADR | `domain-modeling` |
| Explore module interfaces or find architectural improvements | `codebase-design` for a module, `improve-codebase-architecture` for a survey |
| Answer a UI or state-model question with a disposable demo | `prototype` |
| Turn agreed decisions into a spec, or split a spec into tickets | `to-spec` or `to-tickets` |
| Implement a spec or ticket, or work test-first | `implement` or `tdd` |
| Investigate a difficult, unclear, or recurring bug | `diagnosing-bugs` |
| Review committed work or current changes | `code-review` |
| Transfer work or learn from a completed session | `handoff` or `retro` |

See [Phase boundaries](PHASE-BOUNDARIES.md) when deciding whether to continue or transfer work.
