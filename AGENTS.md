## Project status

The recon build is done (`docs/recon.md`, issue #1); its manual runs are pending (`docs/recon-checklist.md`). The core library is done (stage 2, issue #10). The main tool is in stage 3: the window and switching. Start with `docs/roadmap.md`.

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

- `src/WiFiControl.Core` (the core library, no WPF or WinForms, public types) and `tests/WiFiControl.Core.Tests` (xUnit). Windows access goes through `INetworkSystem` (adapter settings, proxy) and `IWifiSystem` (Wi-Fi: networks, notifications); tests use `FakeSystem` and `FakeWifi`.
- `src/WiFiControl` (the main tool, WPF on CommunityToolkit.Mvvm). No automated tests: logic lives in the core, the window is checked by running it.
- `src/WiFiControl.Recon` (the recon build, WPF) and `tests/WiFiControl.Recon.Tests` (xUnit). All Windows access goes through `IReadOnlySystem`; tests use `FakeSystem` and `FakeClock`. The recon build shares no code with the core.

CI: `.github/workflows/build.yml` builds in Release and runs tests with `Category!=Machine` on push to `main` and on pull requests.

## Agent skills

### Issue tracker

Issues live in GitHub Issues (`AlexeyDesyatnik/WiFiControl`), managed with the `gh` CLI. See `docs/agents/issue-tracker.md`.

Finishing an issue, in this order:

1. After pushing to `main`, wait for the CI run of that commit (`gh run list --commit <sha>`, then `gh run watch <id> --exit-status`). Work goes straight to `main` without pull requests, so nothing else watches CI. If it fails, fix it first.
2. Only then close the issue or move on. When closing, tick every met acceptance criterion in the issue body itself (`gh issue edit --body-file`), not only in a comment: the body shows progress on the issue and in the parent's sub-issues. Leave unmet ones unticked and say why.

### Domain docs

Single-context: one `GLOSSARY.md` at the repo root and ADRs in `docs/adr/`. See `docs/agents/domain.md`.
