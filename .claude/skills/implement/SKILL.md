---
name: implement
description: "Implement a piece of work based on a spec or set of tickets."
disable-model-invocation: true
---

Implement the work described by the user in the spec or tickets.

If the user passes a ticket reference, fetch it from the issue tracker and state its title before starting. If the reference is ambiguous, ask.

Use `tdd` where applicable. Reuse existing public seams and test patterns unless the task requires changing the public contract or materially expanding test scope.

Use the repository's build and test commands. Run focused checks during implementation and the relevant full suite before finishing.

Before committing, use `code-review` to review the current task changes, including staged, unstaged, and task-related untracked files. If the task already spans commits, give the reviewer the base immediately before the first task commit so the review covers all task commits and current changes. Exclude unrelated user changes. Address findings before the final commit.

Commit the reviewed task changes to the current branch. Follow the user's instructions on whether to commit.
