## Project status

Documents only, no code yet. Start with `docs/roadmap.md`; the first task is the recon build described in `docs/recon.md`.

## Language

Docs, UI texts and code comments are in Russian. Identifiers are in English.

## Domain docs

Single-context: one `GLOSSARY.md` at the repo root and ADRs in `docs/adr/`. Read the glossary and the ADRs touching your area before exploring or changing code; use glossary terms, not the synonyms it lists under _Avoid_. If your change contradicts an ADR, say so explicitly instead of silently overriding it.

## Predecessor: NetSwitch

Much of the core (IPv4 parsing and validation, netsh commands, DHCP without a connection, system and WinHTTP proxy writing, the step-based applier with undo, the `INetworkSystem` + fake-system test approach, atomic JSON store) is ported from NetSwitch: https://github.com/AlexeyDesyatnik/NetSwitch. Read its ADRs before changing anything in how settings are written. Do not modify the NetSwitch repo from this project.

## Build and test

Planned: .NET SDK 10, `dotnet build WiFiControl.sln -c Release`, `dotnet test WiFiControl.sln`. Update this section when the solution exists.

## Agent skills

### Issue tracker

Issues live in GitHub Issues (`AlexeyDesyatnik/WiFiControl`), managed with the `gh` CLI. See `docs/agents/issue-tracker.md`.

### Domain docs

Single-context: one `GLOSSARY.md` at the repo root and ADRs in `docs/adr/`. See `docs/agents/domain.md`.
