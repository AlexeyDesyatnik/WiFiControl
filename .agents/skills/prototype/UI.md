# UI Prototype

Use a small WPF prototype when the question depends on real WPF controls, layout, interaction, or the Fluent theme. Put the relevant view in a temporary WPF project outside the production solution. Use the application's WPF target and Fluent theme where they matter to the question.

Use representative fake networks, profiles, and connection states. For interactive flows, simulate actions and outcomes with in-memory state or fakes at the existing interfaces. Never write real IPv4 or proxy settings, change a Windows network profile, or connect or disconnect the machine.

For layout discussion alone, a self-contained HTML file is acceptable. State in the mockup and when sharing it that it does not verify WPF behavior or the Fluent theme.

Build one version by default. When comparing designs is the question, vary the relevant structure or interaction and use the same fake data and scenarios for each. Share how to open the mockup or run the WPF project and what to look for.

Record what the prototype showed and the decision it informed. Keep the prototype out of the production application.
