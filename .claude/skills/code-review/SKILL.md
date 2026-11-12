---
name: code-review
description: "Review committed changes against a fixed point or review work in progress, including new files. Runs Standards and Spec reviews in parallel sub-agents and reports them side by side."
---

Review the selected work along two axes:

- **Standards**: does the code conform to this repo's documented coding standards?
- **Spec**: does the code faithfully implement the originating issue / spec?

Both axes run as **parallel sub-agents** so they don't pollute each other's context, then this skill aggregates their findings.

Use the repository's issue tracker instructions when looking up a spec.

## Process

### 1. Select the review mode and capture its scope

- **Committed changes against a fixed point.** Resolve the supplied ref (commit, branch, tag, or merge-base) and `HEAD` with `git rev-parse`. If the user requested a committed review and the base is unclear, ask for it. Capture `git diff <fixed-point>...HEAD` (merge-base to `HEAD`) and `git log <fixed-point>..HEAD --oneline`, using the resolved SHAs for both commands. This mode excludes working-tree changes.
- **Current work before commit.** Use `git diff HEAD` for the combined staged and unstaged result. Inspect `git diff --cached` and `git diff` separately when the index and working tree differ, so an unstaged change cannot hide a staged change that might be committed. List untracked files with `git ls-files --others --exclude-standard` and read or capture the contents of every task-related file from that list. `git diff HEAD` omits untracked files. No commit is required.
- **Task spanning commits and current changes.** Identify and resolve the commit immediately before the task's first commit. Use `git diff <task-base>` for the tracked result from that base through the working tree, capture `git log <task-base>..HEAD --oneline`, and include task-related untracked files as above.

In all modes, confirm the scope contains changes before handing it to reviewers. Use the task and known edits to exclude unrelated work, including unrelated hunks in shared files. When a raw command would expose unrelated changes, give both reviewers the same scoped patch or selected hunks and file contents. Resolve material ambiguity about ownership before including changes. Leave the index and working tree as they are.

### 2. Identify the spec source

Look for the originating spec, in this order:

1. The current task's issue reference or references in the scoped commit messages, fetched via the workflow in the tracker doc.
2. A path the user passed as an argument.
3. A spec file under `docs/`, `specs/`, or `.scratch/` matching the branch name or feature.
4. If nothing is found, ask the user where the spec is. If they say there isn't one, the **Spec** sub-agent will skip and report "no spec available".

### 3. Identify the standards sources

Search the repo for every file that documents how code should be written. When `CODING_STANDARDS.md` or `CONTRIBUTING.md` exists, it must be on the list.

On top of whatever the repo documents, the Standards axis always carries the **smell baseline** below: a fixed set of Fowler code smells (_Refactoring_, ch.3) that applies even when a repo documents nothing. Two rules bind it:

- **The repo overrides.** A documented repo standard always wins; where it endorses something the baseline would flag, suppress the smell.
- **Always a judgement call.** Each smell is a labelled heuristic ("possible Feature Envy"), never a hard violation. Like any standard here, skip anything tooling already enforces.

Each smell reads *what it is* → *how to fix*; match it against the diff:

- **Mysterious Name**: a function, variable, or type whose name doesn't reveal what it does or holds. → rename it; if no honest name comes, the design's murky.
- **Duplicated Code**: the same logic shape appears in more than one hunk or file in the change. → extract the shared shape, call it from both.
- **Feature Envy**: a method that reaches into another object's data more than its own. → move the method onto the data it envies.
- **Data Clumps**: the same few fields or params keep travelling together (a type wanting to be born). → bundle them into one type, pass that.
- **Primitive Obsession**: a primitive or string standing in for a domain concept that deserves its own type. → give the concept its own small type.
- **Repeated Switches**: the same `switch`/`if`-cascade on the same type recurs across the change. → replace with polymorphism, or one map both sites share.
- **Shotgun Surgery**: one logical change forces scattered edits across many files in the diff. → gather what changes together into one module.
- **Divergent Change**: one file or module is edited for several unrelated reasons. → split so each module changes for one reason.
- **Speculative Generality**: abstraction, parameters, or hooks added for needs the spec doesn't have. → delete it; inline back until a real need shows.
- **Message Chains**: long `a.b().c().d()` navigation the caller shouldn't depend on. → hide the walk behind one method on the first object.
- **Middle Man**: a class or function that mostly just delegates onward. → cut it, call the real target direct.
- **Refused Bequest**: a subclass or implementer that ignores or overrides most of what it inherits. → drop the inheritance, use composition.

### 4. Spawn both sub-agents in parallel

Issue both sub-agent calls together, in the foreground, and aggregate the reports they return.

**Standards sub-agent prompt** should include:

- The scoped review input from step 1, with the diff command or selected patch, commit list if applicable, and task-related untracked files and their contents. Review only that scope.
- The list of standards-source files you found in step 3, **plus the smell baseline from step 3** pasted in full (the sub-agent has no other access to it).
- The brief: "Report, per file/hunk where relevant, (a) every place the diff violates a documented standard: cite the standard (file + the rule); and (b) any baseline smell you spot: name it and quote the hunk. Distinguish hard violations from judgement calls: documented-standard breaches can be hard, but baseline smells are always judgement calls, and a documented repo standard overrides the baseline. Skip anything tooling enforces. Under 400 words."

**Spec sub-agent prompt** should include:

- The same scoped review input from step 1, including task-related untracked files and their contents.
- The path or fetched contents of the spec.
- The brief: "Report: (a) requirements the spec asked for that are missing or partial; (b) behaviour in the diff that wasn't asked for (scope creep); (c) requirements that look implemented but where the implementation looks wrong. Quote the spec line for each finding. Under 400 words."

If the spec is missing, skip the Spec sub-agent and note this in the final report.

### 5. Aggregate

Present the two reports under `## Standards` and `## Spec` headings, verbatim or lightly cleaned. Do **not** merge or rerank findings, because the two axes are deliberately separate (see _Why two axes_).

End with a one-line summary: total findings per axis, and the worst issue _within each axis_ (if any). Don't pick a single winner across axes: that's the reranking the separation exists to prevent.

## Why two axes

A change can pass one axis and fail the other:

- Code that follows every standard but implements the wrong thing → **Standards pass, Spec fail.**
- Code that does exactly what the issue asked but breaks the project's conventions → **Spec pass, Standards fail.**

Reporting them separately stops one axis from masking the other.
