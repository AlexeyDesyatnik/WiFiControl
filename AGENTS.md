## Project status

The recon build is done (`docs/recon.md`, issue #1); its manual runs are pending (`docs/recon-checklist.md`). The main tool is in stage 2: the core library (issue #10). Start with `docs/roadmap.md`.

## Language

Docs, UI texts and code comments are in Russian. Identifiers are in English.

## Domain docs

Single-context: one `GLOSSARY.md` at the repo root and ADRs in `docs/adr/`. Read the glossary and the ADRs touching your area before exploring or changing code; use glossary terms, not the synonyms it lists under _Avoid_. If your change contradicts an ADR, say so explicitly instead of silently overriding it.

## Predecessor: NetSwitch

Much of the core (IPv4 parsing and validation, netsh commands, DHCP without a connection, system and WinHTTP proxy writing, the step-based applier with undo, the `INetworkSystem` + fake-system test approach, atomic JSON store) is ported from NetSwitch: https://github.com/AlexeyDesyatnik/NetSwitch. Read its ADRs before changing anything in how settings are written. Do not modify the NetSwitch repo from this project.

## Build and test

Requires .NET SDK 10 on Windows.

- Build: `dotnet build WiFiControl.sln -c Release`. Warnings are errors (`Directory.Build.props`). Build output goes to `artifacts/`.
- Test: `dotnet test WiFiControl.sln`.
- Tests marked `[Trait("Category", "Machine")]` read the machine they run on: they change nothing and need no admin rights. `MachineSmokeTests` leaves `machine-report.txt` in the test build directory (`artifacts/bin/WiFiControl.Recon.Tests/debug/`). To skip them: `dotnet test WiFiControl.sln --filter "Category!=Machine"`.

Projects:

- `src/WiFiControl.Core` (the core library, no WPF or WinForms, public types) and `tests/WiFiControl.Core.Tests` (xUnit).
- `src/WiFiControl.Recon` (the recon build, WPF) and `tests/WiFiControl.Recon.Tests` (xUnit). All Windows access goes through `IReadOnlySystem`; tests use `FakeSystem` and `FakeClock`. The recon build shares no code with the core.

CI: `.github/workflows/build.yml` builds in Release and runs tests with `Category!=Machine` on push to `main` and on pull requests.

## Agent skills

### Issue tracker

Issues live in GitHub Issues (`AlexeyDesyatnik/WiFiControl`), managed with the `gh` CLI. See `docs/agents/issue-tracker.md`.

### Domain docs

Single-context: one `GLOSSARY.md` at the repo root and ADRs in `docs/adr/`. See `docs/agents/domain.md`.
