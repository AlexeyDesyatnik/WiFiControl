---
name: to-spec
description: "Synthesize agreed decisions into a spec and publish it to the project issue tracker. Ask only about material uncertainties."
disable-model-invocation: true
---

Synthesize the spec from decisions already made in the conversation and evidence in the repository. Ask a focused question only when a material uncertainty affects scope, behavior, or verification and cannot be resolved from those sources.

## Process

1. Explore the repo to understand the current state of the codebase, if you haven't already. Use the project's domain glossary vocabulary throughout the spec, and respect any ADRs in the area you're touching.

2. Use the existing public test seams and the testing decisions already agreed. Record any proposed changes to a public contract or material changes to testing scope and resolve consequential uncertainty before publishing.

3. Write the spec using the template below, then publish it using the repository's issue tracker instructions. Apply `ready-for-agent` if that label is configured.

<spec-template>

## Problem Statement

The problem that the user is facing, from the user's perspective.

## Solution

The solution to the problem, from the user's perspective.

## Requirements

List the smallest set of user-facing requirements and acceptance criteria that covers the agreed behavior. Use a user story where it clarifies the actor or outcome.

## Implementation Decisions

A list of implementation decisions that were made. This can include:

- The modules that will be built/modified
- The interfaces of those modules that will be modified
- Technical clarifications from the developer
- Architectural decisions
- Schema changes
- API contracts
- Specific interactions

Do NOT include specific file paths or code snippets. They may end up being outdated very quickly.

Exception: if a prototype produced a snippet that encodes a decision more precisely than prose can (state machine, reducer, schema, type shape), inline it within the relevant decision and note briefly that it came from a prototype. Trim to the decision-rich parts, not a working demo, just the important bits.

## Testing Decisions

Record the behavior to verify, the existing public seams and relevant test examples, and any agreed manual checks or testing limitations.

## Out of Scope

A description of the things that are out of scope for this spec.

## Further Notes

Any further notes about the feature.

</spec-template>
