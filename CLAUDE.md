# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project overview

NorthLand: Last Stand (팀 유유아) is a grid-based tower-defense + village-management hybrid with roguelike elements, built in Unity. The full game design is documented in `Docs/GDD.md` — read it before making design or gameplay-logic decisions, since core systems (day/night loop, resource flow, territory expansion, unit placement) are specified there and several mechanics are still marked as open issues/TODO in that doc.

Core loop: day phase (경영 — gather resources, place villagers, build production buildings, place towers) → night phase (수비 — towers auto-attack, soldiers block waypoints, player casts targeted skills) → wave-end reward choice → repeat. Two independently expandable spaces: 경영 공간 (management) and 전투 공간 (combat).

## Project state

This is an early-stage project scaffolded from Unity's URP Empty Template — there is no gameplay code yet (`Assets/Scripts` is empty). When adding code, check `Docs/GDD.md` first for the intended system design rather than inventing mechanics.

## Tooling

- Unity Editor version: 6000.3.15f1 (see `ProjectSettings/ProjectVersion.txt` — open/build with this exact version).
- Render pipeline: Universal Render Pipeline (URP), with separate PC and Mobile renderer/pipeline assets in `Assets/Settings`.
- Input: new Input System (`InputSystem_Actions.inputactions`).
- IDE integration: Visual Studio / Rider via `com.unity.ide.visualstudio` / `com.unity.ide.rider`; `.vscode/` is configured to attach to Unity for debugging (`dotnet.defaultSolution` = `NorthLand.slnx`).
- There are no CLI build/lint/test scripts in this repo — Unity projects are built and run from the Unity Editor (or `Unity -batchmode` if a build pipeline is added later). `com.unity.test-framework` is listed as a package dependency but no tests exist yet.

## Repository conventions

- `Assets/Personal/<name>/` — per-team-member scratch folders (currently `muchan`, `SUNJIN`, `SUNGSOO`). Work-in-progress or experimental assets belonging to one person go here rather than in shared folders.
- `Assets/Imported/` — contains its own nested git repo; treat as a vendored/imported asset source, not something to edit as part of normal feature work.
- `Assets/TutorialInfo/` and `Assets/Readme.asset` are leftovers from the URP template's default Readme window — not part of the game.
