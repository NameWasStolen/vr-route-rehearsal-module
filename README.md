# VR Route Rehearsal Module

A Unity VR training module for FIT4701/02 investigating whether guided route rehearsal can reduce travel anxiety and improve navigation outcomes for older immigrant users. The finished experience should prioritise clear, comfortable and accessible interaction.

## Current development status

The `feat/initial-unity-setup` branch establishes the shared Unity and XR development foundation. It includes OpenXR configuration, imported XR Interaction Toolkit samples, a reusable player rig, a bootstrap scene and a developer locomotion sandbox.

The final menu, tutorial, training-run system and locomotion behaviour are not implemented. Scenes bearing those feature names are currently placeholders and should not be treated as working functionality.

## Requirements

- Unity Hub.
- Unity Editor **6000.3.21f1**. Use this exact version to avoid unintended project or asset upgrades.
- Git.
- For physical-headset testing, an active OpenXR runtime compatible with the connected hardware.

The repository contains OpenXR settings for Standalone and Android. It does not establish a final build target or prove which optional Unity Hub platform modules are installed, so install build support only after the team confirms the target platform.

## Getting started

1. Clone the repository and enter the project directory:

   ```text
   git clone <repository-url>
   cd vr-route-rehearsal-module
   ```

2. In Unity Hub, select **Add > Add project from disk** and choose the repository root.
3. Open the project with Unity `6000.3.21f1`.
4. Allow Unity Package Manager to restore the dependencies from `Packages/manifest.json` and wait for asset import and script compilation to finish.
5. Check the Console before making changes. Do not commit generated folders such as `Library`, `Logs`, `Temp` or `UserSettings`.

## Project structure

- `Assets/` contains project scenes, prefabs, settings, imported samples and future game content.
  - `Art/`, `Audio/` and `Data/` provide organised locations for project content; several are currently empty scaffolding.
  - `Prefabs/Player/XRPlayerRig.prefab` is the shared XR player rig.
  - `Scenes/` contains shared, placeholder and development scenes. Developer-only scenes are under `Scenes/Development/`.
  - `Scripts/` is organised by planned feature area but currently contains no project scripts.
  - `Settings/` contains Universal Render Pipeline assets.
  - `XR/` and `XRI/` contain OpenXR, XR Plug-in Management and XR Interaction Toolkit settings.
  - `Samples/XR Interaction Toolkit/3.3.2/` contains imported Starter Assets and XR Interaction Simulator sample content.
- `Packages/` declares and locks Unity Package Manager dependencies.
- `ProjectSettings/` contains the Unity editor and project configuration shared by the team.

Keep Unity `.meta` files with their corresponding assets. Avoid moving or renaming assets without a clear need because doing so can break serialized references.

## XR foundation

| Package | Version | Role |
| --- | --- | --- |
| `com.unity.xr.openxr` | `1.16.1` | Connects Unity XR to an active OpenXR runtime. |
| `com.unity.xr.management` | `4.7.0` | Configures XR loaders and startup. |
| `com.unity.xr.interaction.toolkit` | `3.3.2` | Supplies XR interaction components, Starter Assets and the imported simulator sample. |
| `com.unity.inputsystem` | `1.20.0` | Supplies action-based headset, controller, simulator and UI input. |

`Assets/Prefabs/Player/XRPlayerRig.prefab` contains the XR Origin, headset camera, left and right controllers, input-action configuration and controller ray interactors. Both rays are enabled as a development default and have a maximum raycast distance of 10 Unity units. Handedness policy and contextual ray visibility remain future work.

`Assets/Scenes/Bootstrap.unity` contains the shared `XRPlayerRig`, `XR Interaction Manager` and an `EventSystem` using `XR UI Input Module`. The simulator must remain outside both `XRPlayerRig` and `Bootstrap` so production scenes do not depend on editor simulation.

## Testing without a headset

1. Open `Assets/Scenes/Development/LocomotionSandbox.unity`.
2. In the Hierarchy, enable the **XR Interaction Simulator** GameObject if it is disabled. The scene currently stores it disabled for physical-device testing.
3. Enter Play Mode, select the **Game** view and click inside it so keyboard and mouse input is captured.
4. Use the controls below. They are taken from the imported simulator `.inputactions` assets rather than assumed from general XRI defaults.

### Simulator pose and selection controls

| Input | Action |
| --- | --- |
| `W` / `S`, `A` / `D`, `Q` / `E` | Translate forward/back, left/right and down/up. |
| Arrow keys | Rotate the currently manipulated target. |
| Hold right mouse button + move mouse | Rotate using mouse movement. |
| Hold right mouse button + scroll | Translate or roll on the Z axis, depending on the current manipulation mode. |
| `[` / `]` | Toggle manipulation of the left/right device; press both to target both devices or change simulated device mode. |
| `H` | Toggle head-only manipulation. |
| `Tab` | Cycle the available simulated devices. |
| `V`, `C`, `Z` | Constrain manipulation/reset to the X, Y or Z axis respectively. |
| `R` | Reset the manipulated pose, respecting active axis constraints. |
| Backquote (`` ` ``) / `Space` | Cycle / perform the current quick action. |
| `X` / `Y` | Toggle the action menu / input-selection menu. |

### Simulated controller controls

Controller actions affect the right device by default; hold `Shift` to direct them to the left device.

| Input | Action |
| --- | --- |
| `G` / `T` | Grip / trigger. |
| `1` / `2` | Primary / secondary button. |
| `M` | Menu button. |
| `I`, `J`, `K`, `L` | Up, left, down and right on the selected 2D axis. |
| `9` / `0` | Select the primary / secondary 2D axis target. |
| `3` / `4` | Primary / secondary 2D-axis click. |
| `5` / `6` | Primary / secondary 2D-axis touch. |
| `7` / `8` | Primary / secondary touch. |

The imported hand-control asset also binds `N` (poke), `M` (pinch), `K` (grab), `L` (thumb), `O` (open) and `P` (fist). The current package manifest does not include XR Hands, so hand simulation is not treated as supported project functionality.

`LocomotionSandbox` and the simulator are development tools only. Do not present this scene as a production experience or move the simulator into `XRPlayerRig` or `Bootstrap`.

## Testing with a physical headset

1. Disable the **XR Interaction Simulator** in `LocomotionSandbox` before entering Play Mode.
2. Connect the headset and controllers and start the OpenXR runtime required by that hardware.
3. Enter Play Mode and confirm that headset pose and both controllers are tracked.
4. Check controller input, ray pointing and selection, world-space UI interaction through `XR UI Input Module`, and audio behaviour.
5. Check the Console for errors or warnings and test comfort on the physical device. Do not assume simulator results prove headset behaviour.

No single headset model or final deployment target is mandated by the repository at this stage.

## Scenes

| Scene | Status |
| --- | --- |
| `Assets/Scenes/Bootstrap.unity` | Shared XR foundation containing the player rig, interaction manager and XR-capable EventSystem. It does not contain the simulator. |
| `Assets/Scenes/Development/LocomotionSandbox.unity` | **Developer-only.** Contains the player rig, simulator, test environment and basic interaction/UI objects. It is not a production scene. |
| `Assets/Scenes/MainMenu.unity` | Placeholder; the finished main menu is not implemented. |
| `Assets/Scenes/Tutorial.unity` | Placeholder; tutorial functionality is not implemented. |
| `Assets/Scenes/Training.unity` | Placeholder; training-run functionality is not implemented. |
| `Assets/Scenes/SampleScene.unity` | Unity template/sample scene retained in the project; not a production route-rehearsal scene. |
| `Assets/Samples/XR Interaction Toolkit/3.3.2/Starter Assets/DemoScene.unity` | Imported XRI Starter Assets demo; package sample content, not a project production scene. |

## Git workflow

- `main` contains stable, production-ready milestone code. Do not commit directly to it.
- `dev` is the integration branch.
- Create feature and bugfix branches from `dev` using `feat/{short-description}` and `bugfix/{short-description}`.
- Merge feature and bugfix work into `dev` through pull requests. Promote stable milestones from `dev` to `main` through a pull request.
- Keep branches, commits and pull requests small and focused.
- Use lowercase, present-tense commit messages in `type: description` format, for example `docs: document xr project setup`.

## Current limitations and planned work

- Final comfortable locomotion behaviour.
- Controller handedness support and configuration.
- Contextual controller-ray visibility.
- Production main menu and accessible settings flow.
- Guided tutorial functionality.
- Route-rehearsal training runs, timing and performance logging.
- Validation on the selected physical headset and final build target.
