## Project status

The recon build and stage 2 of the core are complete. Stage 3 of the main tool is in progress. See the [roadmap](docs/roadmap.md) for implementation status and manual check results.

## Language

Docs, UI texts and code comments are in Russian. Identifiers are in English.

## Domain docs

The [glossary](GLOSSARY.md) is at the repo root and [ADRs](docs/adr/) are in `docs/adr/`. Read the glossary and relevant ADRs before exploring or changing code. Use project terms and flag any ADR conflict explicitly. See the [domain documentation rules](docs/agents/domain.md).

## Predecessor: NetSwitch

The core inherits settings writes and other behavior from [NetSwitch](https://github.com/AlexeyDesyatnik/NetSwitch). Start with [WiFiControl ADR 0003](docs/adr/0003-settings-written-as-in-netswitch.md). For changes to the IPv4 write mechanism or DHCP restoration, read NetSwitch ADR 0002 or 0003 respectively, as referenced there. For other inherited behavior, consult NetSwitch primary sources when local docs are insufficient. Do not modify the NetSwitch repository.

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
