# Unity Project Context

<!-- unity-onboarding:generated:start -->

## Project Summary

- Project root: `C:\Users\tlsdn\Desktop\FlickDom\FlickDom`
- Last analyzed: 2026-07-29
- Last analyzed commit: `d4b3ce326c609add2439f435d6687d491f1c1c5e`
- FlickDom is a local/online two-player board game prototype. Players choose an order for three physics pieces, flick them in alternating turns, and later use final positions as candidates for a separate territory-placement phase.

## Confirmed Environment

- Unity version: 6000.5.1f1 (`0d9463e84828`)
- Render pipeline: Universal Render Pipeline 17.5.0
- Input system: Input System package 1.19.0; project uses `UnityEngine.InputSystem`
- Target platforms: Windows/Standalone build profile is present. Other supported release targets are not documented.

## Important Packages And Frameworks

| Area | Finding | Confidence | Evidence |
| --- | --- | --- | --- |
| Rendering | URP 17.5.0 | Confirmed | `Packages/manifest.json`, `Assets/Settings/*RPAsset.asset` |
| Input | Unity Input System 1.19.0 | Confirmed | `Packages/manifest.json`, `Assets/InputSystem_Actions.inputactions`, first-party code |
| Multiplayer | Netcode for GameObjects 2.13.0 is actively used by a custom Host/Client bootstrap | Confirmed | `Packages/manifest.json`, `Assets/02_Scripts/Network/FlickDomNetworkBootstrap.cs` |
| Tests | Unity Test Framework 1.7.0 is installed; no first-party EditMode or PlayMode test assemblies were found | Confirmed | `Packages/manifest.json`, repository file scan |
| Unity MCP | `com.coplaydev.unity-mcp` is installed from Git, but no Unity Editor MCP capability is callable in the current Codex session | Confirmed | `Packages/manifest.json`, current tool discovery |

## Directory Structure

| Path | Purpose | Confidence | Evidence |
| --- | --- | --- | --- |
| `Assets/01_Scenes` | Main and developer/test scenes | Confirmed | Scene assets |
| `Assets/02_Scripts/Core` | Match state, round flow, local turn rig, placement camera | Confirmed | Representative code |
| `Assets/02_Scripts/Flick_Scripts` | Physics piece input, motion, visuals, token configuration | Confirmed | Representative code |
| `Assets/02_Scripts/Board` | Grid ownership model, candidate resolution, placement selection and grid view | Confirmed | Representative code |
| `Assets/02_Scripts/Cards` | Pattern card data, display, matching and scoring | Confirmed | Representative code |
| `Assets/02_Scripts/Network` | NGO listen-server bootstrap and state synchronization | Confirmed | `FlickDomNetworkBootstrap.cs` |
| `Assets/02_Scripts/UI` | Game and score HUD | Confirmed | UI scripts |
| `Assets/04_Arts` | First-party board, tile, tray and disk models plus materials/shaders | Confirmed | Asset inventory |
| `Assets/Suriyun` | Imported third-party character/environment content | Confirmed | Asset layout and documentation |

## Assembly Boundaries

| Assembly | Responsibility | Key references | Notes |
| --- | --- | --- | --- |
| `Assembly-CSharp` | All first-party runtime gameplay and networking code | UnityEngine, Input System, NGO | No first-party `.asmdef` or `.asmref` files were found |
| `Assembly-CSharp-Editor` | Character setup editor menu and tutorial editor code | Runtime assembly, UnityEditor | Editor scripts are isolated under `Editor` folders |

## Scenes And Startup Flow

- Build scenes: enabled `Assets/01_Scenes/Test_CJ/good_Scene.unity`; disabled `Assets/01_Scenes/Test_SW/SinWoo Scene.unity`
- Likely startup scene: `good_Scene`
- Scene loading flow: build settings start directly in `good_Scene`; networking bootstrap is intentionally limited to that scene.
- `SinWoo Scene` is currently a manually edited development scene. Its uncommitted layout contains a `Board` hierarchy, 25 `SM_CellTile` instances, two `SM_StartTray` instances and six `SM_FlickDisk` instances.

## Architecture

| Pattern | Finding | Confidence | Evidence |
| --- | --- | --- | --- |
| MonoBehaviour composition | Gameplay is scene-composed through serialized references with occasional runtime type lookup fallback | Confirmed | Core, Board, Cards and Network scripts |
| Explicit game-state machine | `GameModeManager` owns `FlickDomGameState`, active player, turn order and round transitions | Confirmed | `GameModeManager.cs`, `FlickDomGameState.cs` |
| Piece orchestration | `LocalFlickTurnTestRig` owns player piece arrays, selection order, round resets and physics completion | Confirmed | `LocalFlickTurnTestRig.cs` |
| Scene-authored piece setup | `SinWoo Scene` registers six placed model Transforms; the rig adds missing gameplay components without cloning or repositioning the visual objects | Confirmed | `LocalFlickTurnTestRig.cs`, `SinWoo Scene.unity` |
| Legacy dynamic piece fallback | Other scenes may still supply one `TurnBasedFlickPiece`; the rig can clone missing pieces to reach three when no authored Transform set exists | Confirmed | `EnsurePieceCount` and `ArrangePieceStarts` |
| Data-driven token material | `TokenData` is the source for mass, drag, physics material and render material | Confirmed | `TokenData.cs`, `TokenSetup.cs`, `docs/GAMEPLAY_RULES.md` |
| Separate territory board | `TokenMapGridView` dynamically creates the later placement grid; it is distinct from the authored flick board | Confirmed | `TokenMapGridView.cs`, scene configuration |

## Coding Conventions

- Namespace style: first-party gameplay uses `FlickDom.Gameplay`; networking uses `FlickDom.Networking`; some early flick scripts remain in the global namespace.
- Serialized fields: mostly `[SerializeField] private`; older scripts and `TokenSetup` contain public serialized fields.
- Async: no project-wide async abstraction; coroutines are used for physics-settle waits.
- Comments/docs: code comments are mixed Korean/English; some older source comments show encoding corruption. Repository workflow documentation is in `docs/`.

## Testing And Validation

- EditMode tests: none found
- PlayMode tests: none found
- CI/build validation: no CI test command found
- Repository policy: agents modify code/documentation; the user performs Unity execution and final runtime verification.

## Available Unity Tooling

| Capability | Status | Evidence |
| --- | --- | --- |
| Unity MCP package | available | `com.coplaydev.unity-mcp` in package manifest |
| `unity.connection.status` | unavailable | No callable Unity MCP tools discovered in this Codex session |
| `unity.editor.version` | unavailable | Version read from `ProjectSettings/ProjectVersion.txt` |
| `unity.console.read` | unavailable | No callable Unity MCP tools discovered |
| `unity.scene.list` | unavailable | Scene assets inspected from serialized files |
| `unity.scene.inspect` | unavailable | Scene assets inspected read-only from serialized YAML |
| `unity.buildsettings.read` | unavailable | `ProjectSettings/EditorBuildSettings.asset` inspected directly |
| `unity.gameobject.inspect` | unavailable | Serialized scene evidence used |
| `unity.asset.search` | unavailable | Repository search used |
| `unity.package.read` | unavailable | Package manifests inspected directly |
| `unity.tests.list` | unavailable | Repository scan used |
| `unity.tests.run` | unavailable | Not run |
| `unity.playmode.read` | unavailable | Not entered |
| `unity.profiler.read` | unavailable | Not available |

## Important Constraints

- Preserve the user's uncommitted changes in `Assets/01_Scenes/Test_SW/SinWoo Scene.unity`.
- Do not re-enable automatic piece cloning for the manually authored scene layout.
- Keep the authored flick board separate from the future territory-placement board.
- Preserve `TokenData` as the authoritative material/physics configuration.
- Game implementation tasks must update `docs/DEVELOP_LOG.md`.
- Final runtime testing is performed by the user according to `README.md`.

## Unknowns And Confidence

- The production target beyond the present Windows build profile is unknown.
- Runtime behavior of the current uncommitted `SinWoo Scene` has not been observed in Play Mode.
- The intended final art-prefab workflow is not documented; the current scene uses direct imported-model prefab instances.

## Source Files Inspected

- `ProjectSettings/ProjectVersion.txt`
- `ProjectSettings/EditorBuildSettings.asset`
- `ProjectSettings/ProjectSettings.asset`
- `Packages/manifest.json`
- `Packages/packages-lock.json`
- `README.md`
- `docs/AGENTS.md`
- `docs/CONTRIBUTING.md`
- `docs/GAMEPLAY_RULES.md`
- `docs/DEVELOP_LOG.md`
- `Assets/01_Scenes/Test_SW/SinWoo Scene.unity`
- `Assets/02_Scripts/Core/GameModeManager.cs`
- `Assets/02_Scripts/Core/LocalFlickTurnTestRig.cs`
- `Assets/02_Scripts/Flick_Scripts/TurnBasedFlickPiece.cs`
- `Assets/02_Scripts/Flick_Scripts/Token/TokenData.cs`
- `Assets/02_Scripts/Flick_Scripts/Token/TokenSetup.cs`
- `Assets/02_Scripts/Board/TokenMapGridView.cs`
- `Assets/02_Scripts/Board/TokenMapManager.cs`
- `Assets/02_Scripts/Network/FlickDomNetworkBootstrap.cs`

<!-- unity-onboarding:generated:end -->
