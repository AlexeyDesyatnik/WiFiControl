---
name: diagnosing-bugs
description: Investigate difficult, unclear, or recurring bugs and performance regressions.
---

# Diagnosing Bugs

Read `GLOSSARY.md` and the relevant ADRs. Identify the reported symptom and redact secrets from any commands, logs, or artifacts you share.

## 1. Reproduce and minimise

Aim for a quick check that distinguishes the reported failure from normal behavior. Read the relevant code, existing tests, and recent changes to design the check. Use preliminary hypotheses to choose inputs and capture the right evidence.

Prefer a failing test at the public boundary that exposes the symptom. Use a trace replay, focused harness, bisection, or repeatable manual steps when they fit the failure better. For intermittent failures, record the conditions and reproduction rate. For performance regressions, measure a baseline under representative conditions.

Run the check before editing when possible. Capture the relevant output and reduce the scenario while retaining the failure.

### When the equipment or environment is unavailable

Continue with static diagnosis using the relevant call path, contracts, tests, and available traces. State which conclusions the evidence establishes, which remain hypotheses, and which check needs the unavailable environment. Request specific evidence if it is necessary to distinguish plausible causes.

## 2. Test the cause

State what observation would confirm or disprove each plausible cause. One hypothesis is enough when the evidence points to one cause. Rank competing hypotheses when there are several.

Change one variable at a time. Prefer a debugger or a targeted trace. Mark temporary logs with a unique prefix such as `[DEBUG-a4f2]` so they can be found and removed.

## 3. Fix and verify

Tie the fix to the demonstrated cause or to a defect established by static evidence. When an existing public seam can reproduce the failure, write a focused regression test, observe it fail, apply the fix, and observe it pass. If the seam cannot reproduce the failure, choose a suitable check and explain its limits.

Re-run the original scenario when possible. Report any hardware-dependent or other verification that remains outstanding. Remove temporary instrumentation and diagnostic artifacts from the production change and record the cause and the evidence for the fix.
