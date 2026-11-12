---
name: prototype
description: Build a throwaway prototype to answer a design question about a state model, logic, or the WPF UI.
---

# Prototype

Write down the question the prototype should answer. Choose the smallest artifact that lets the user try the relevant cases.

- **State model or logic** — use [LOGIC.md](LOGIC.md) for a standalone simulation.
- **WPF behavior or appearance** — use a small WPF window with fake data. For layout discussion alone, a standalone HTML mockup is enough. See [UI.md](UI.md).

Keep the prototype separate from production code and easy to run. Use in-memory state and fake implementations. Never change real network settings or Windows network profiles from a prototype. Show the relevant state after each action. Create multiple variants only when the question calls for a comparison.

Record the question, what you observed, and the resulting decision in the relevant task or design notes. Keep the demo outside the production application. Implement accepted decisions separately through the normal workflow.
