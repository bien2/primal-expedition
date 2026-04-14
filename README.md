# WalaPaNameHehe

Co-op first-person dinosaur expedition prototype built in Unity. Players explore, collect samples, and coordinate extraction while avoiding hostile dinos. Includes inventory/quick-slot flow, drone support equipment, ragdoll + downed/revive states, and a networked expedition state machine.

## Unity Version
- `6000.3.6f1` (Unity 6.3.6f1)

## Quick Start
1. Open the project with Unity `6000.3.6f1`.
2. Load a play scene:
   - `Assets/Scenes/Prototype.unity` (primary)
   - `Assets/Scenes/TestScene.unity` (sandbox)
3. Press Play.

## Controls (Default)
- Move: `WASD` or arrow keys
- Look: mouse
- Sprint: `Shift`
- Jump: `Space`
- Interact / extract / revive / drone summon: `E`
- Pick up item: `F`
- Drop held item: `G`
- Inventory slot select: `1`-`5`
- Inventory slot scroll: mouse wheel
- Respawn (if enabled): `R`
- Cursor toggle: `Alt`
- Pause cursor: `Esc`

Notes:
- Extraction and drone summon both use `E`. Drone summon is a **hold** action and is suppressed while extracting.
- Revive also uses `E` and requires proximity to a downed player.

## Core Gameplay Loop
1. Start expedition (networked via `GameManager`).
2. Collect samples using the extractor tool.
3. Call extraction once the required sample count is met.
4. Hold out until extraction completes.

## Project Structure
- `Assets/Scenes`  
  Core play scenes (`Prototype`, `TestScene`).
- `Assets/Scripts`  
  Gameplay scripts and systems.
- `Assets/Scripts/AI Behaviors`  
  Dino AI, aggression behaviors, attack controller, rule sets.
- `Assets/Scripts/Inventory`  
  Inventory system, pickup flow, UI.
- `Assets/Scripts/Multiplayer`  
  Network game flow, spawn, ownership, session services.
- `Assets/Scripts/Procedural`  
  Procedural foot planning.
- `Assets/Scripts/Special Items`  
  Adrenaline, Lucky Charm, and related item logic.

## Key Systems
- Expedition state machine: `Assets/Scripts/Multiplayer/GameManager.cs`
- Player movement + camera: `Assets/Scripts/PlayerMovement.cs`
- Ragdoll/downed state: `Assets/Scripts/PlayerRagdollController.cs`, `Assets/Scripts/PlayerHitHandler.cs`
- Inventory + pickup UI: `Assets/Scripts/Inventory/InventorySystem.cs`
- Extraction flow: `Assets/Scripts/PlayerExtractor.cs`
- Drone equipment: `Assets/Scripts/WeaponDrone.cs`
- AI behavior: `Assets/Scripts/AI Behaviors/DinoAi.cs` and rule sets

## AI Behaviors
- `Assets/Scripts/AI Behaviors/DinoAi.cs`  
  Core AI state machine (Idle/Roam/Investigate/Chase/Attack/etc.), perception, NavMesh movement, and animator driving.
- `Assets/Scripts/AI Behaviors/DinoAttackController.cs`  
  Attack execution (instakill, down state, or grab hold) with cooldowns and bite-point handling.
- `Assets/Scripts/AI Behaviors/WorldSoundStimulus.cs`  
  Static sound event broadcaster that AI can react to.
- `Assets/Scripts/AI Behaviors/DinoBehaviorRuleSet.cs`  
  Factory that selects the aggression behavior implementation.
- `Assets/Scripts/AI Behaviors/DinoBehaviorRuleTemplate.cs`  
  Interface for aggression behavior handlers (Idle/Roam/Investigate/Chase).
- `Assets/Scripts/AI Behaviors/PassiveAggressionBehavior.cs`  
  Passive roaming only; no chase behavior.
- `Assets/Scripts/AI Behaviors/NeutralAggressionBehavior.cs`  
  Delayed detection, limited chase time, and loss-of-sight timeout.
- `Assets/Scripts/AI Behaviors/AggressiveAggressionBehavior.cs`  
  Immediate detection; prioritizes chase; can investigate sounds.
- `Assets/Scripts/AI Behaviors/HunterAggressionBehavior.cs`  
  Stalking/encounter-driven hunter logic with cues, commits, and flee logic.
- `Assets/Scripts/AI Behaviors/PlundererAggressionBehavior.cs`  
  Scheduled plunder attempts, grab-and-carry flow, and return/drop logic.
- Editor tooling:
  - `Assets/Scripts/AI Behaviors/Editor/DinoAIEditor.cs`
  - `Assets/Scripts/AI Behaviors/Editor/DinoHitboxSetupTool.cs`

## Items
- **Adrenaline Shot** (`Assets/Scripts/Special Items/AdrenalineShotItem.cs`)  
  Temporary speed boost for the local player. Applies a multiplier for a short duration, then consumes the selected item.
- **Lucky Charm** (`Assets/Scripts/Special Items/LuckyCharmItem.cs`)  
  Placeholder/bonus item that exposes a `bonusPrefab` reference. Actual behavior is driven by the prefab setup.
- **Extractor Tool** (`Assets/Scripts/PlayerExtractor.cs`)  
  Allows sampling/harvesting from extractable resources. Handles extraction timing, rewards, and validation.
- **Drone Spawner** (`Assets/Scripts/WeaponDrone.cs`)  
  Summons a controllable weapon drone. Handles drone lifetime, movement, and projectile firing. Consumes spawner uses.

## Items Matrix
Item | Purpose | Duration/Cooldown | Consumes
--- | --- | --- | --- | ---
Adrenaline Shot | Temporary speed boost | Hold-to-use: `holdToUseSeconds`, effect: `effectDurationSeconds` | Yes (consumes selected item)
Lucky Charm | Bonus prefab payload | N/A | Depends on prefab behavior
Extractor Tool | Sample extraction | Extract time: `extractDuration` | No (tool), rewards items
Drone Spawner | Weapon drone summon/use | Lifetime: `droneLifetime`, fire cooldown: `fireCooldown` | Yes (uses per spawner item)

## AI Behavior Matrix
Aggression Type | Detect Delay | Chase Duration | Sound Reaction | Special Notes
--- | --- | --- | --- | ---
Passive | None | None | No | Roams only; never chases
Neutral | Yes (random delay) | Limited (`chaseDuration`) | Not prioritized | Loses chase on `loseSightChaseTimeout`
Aggressive | None | Uses chase timeout | Yes | Prioritizes chase; investigates recent sounds
Hunter | Staged cues | Commit after cues | Yes | Stalk/cue system, can flee/commit
Plunderer | Timed spawn | Limited active window | No | Grab-and-carry flow, returns/drops

Key timing/tuning fields live in `Assets/Scripts/AI Behaviors/DinoAi.cs`.

## Performance Notes
- AI uses `NavMeshAgent`; avoid extreme agent counts without profiling.
- Detection uses overlap/raycast buffers; keep `detectionRadius` and `soundReactionRadius` reasonable.
- Ragdoll + physics-heavy scenes can spike; test on low-spec profiles.
- If you add many active drones/projectiles, watch GC allocations and pooling.

## Multiplayer
Uses Unity Netcode for GameObjects + Relay/Authentication packages.  
Session flow is driven by `NetworkSessionLauncher`, `MultiplayerPlayerSpawner`, and `GameManager`.

## Dependencies (Selected)
- Input System
- Netcode for GameObjects
- URP
- Animation Rigging
- AI Navigation
- Unity Services (Authentication, Relay)

## Imported Assets / Demos
The project contains third-party assets and demo scenes under:
- `Assets/Imports`
- `Assets/FerociousIndustries`
- `Assets/QuickOutline`
- `Assets/Terrains`
- `Assets/Sky_Protective_suit`

These folders include their own demo scenes and scripts. The primary project scripts live in `Assets/Scripts`.

