# PROJECT_CONTEXT.md

Load this file (not `PROTOTYPE_PLAN.md`, not the game bible) at the start of any coding session. It's the only context a session needs to write code that fits the rest of the project. Design rationale lives in [PROTOTYPE_PLAN.md](PROTOTYPE_PLAN.md); lore/tone source of truth is [the_last_ward_game_bible.md](the_last_ward_game_bible.md) — don't load either unless the task specifically needs them.

## Stack

Unity **6000.5.3f1**, URP. Networking: **Netcode for GameObjects** (`com.unity.netcode.gameobjects`) + **Unity Relay/Lobby** via `com.unity.services.multiplayer` (listen-server, host = authority, free tier). Input: new Input System. Audio: plain `AudioSource` + one `AudioMixer` with 4 snapshots (Explore/Stalked/Chase/DeadView). AI: NavMesh (`com.unity.ai.navigation`) + hand-written C# FSM, no behavior-tree asset. Greybox: ProBuilder. See `Packages/manifest.json` for pinned versions — if Package Manager flags a version as unavailable on first open, just click Update to the nearest compatible one, that's expected drift, not a project problem.

**Unity Editor is not installed on this machine as of M0.** Install it via Unity Hub before opening the project — see `SETUP.md`.

## Folder Map

```
Assets/_Project/
  Scripts/
    Core/        GameEvents.cs, GameEnums.cs, IInteractable.cs — shared vocabulary, no upward deps
    Player/       FP controller, interaction raycast (M1)
    Entity/       FSM, senses, EntityTuning.cs (SO) (M3)
    Net/          NGO session/lobby/relay glue (M2)
    Puzzles/      Puzzle framework, ClueDefinition.cs / PuzzleStepDefinition.cs (SO) (M5)
    Knowledge/    Score service, KnowledgeConfig.cs (SO) (M4)
    Spectator/    Tethered camera system (M6)
    Aftermath/    Message template spawner, AftermathTemplateDefinition.cs (SO) (M7)
    UI/           HUD, spectator UI, endgame UI
    Audio/        Mixer-state driver, AudioEventDefinition.cs (SO)
  Prefabs/        Player/ Entity/ Puzzles/ Aftermath/ UI/
  Art/            Environment/ Characters/ Props/  (palette-graded assets only — see ThirdParty/ below)
  Audio/          Ambience/ SFX/ Music/
  Data/           ScriptableObject *instances* live here (assets, not classes)
  Materials/
  Input/          PlayerControls.inputactions (Gameplay + Spectator action maps)
  Scenes/
ThirdParty/       Import quarantine — raw downloaded packs land here first, get palette-graded
                  and prefab-wrapped, THEN move into Assets/_Project. Never reference ThirdParty/
                  paths directly from gameplay code or prefabs.
```

## Namespace Convention

`LastWard.<System>` matching the Scripts subfolder, e.g. `LastWard.Core`, `LastWard.Entity`, `LastWard.Knowledge`. `Core` has no dependencies on other `LastWard.*` namespaces — everything else may depend on `Core`.

## Cross-System Communication: `GameEvents`

Static event bus at `Assets/_Project/Scripts/Core/GameEvents.cs`. Systems raise/subscribe without referencing each other's internals. **It is not a sync mechanism** — server-authoritative state syncs first via `NetworkVariable`/`ClientRpc`, and only then does the local system call the matching `GameEvents.Raise...` so other local systems (UI, audio) react. Current events: `OnKnowledgeChanged(ulong,float)`, `OnAggressionTierChanged(int)`, `OnEntityStateChanged(EntityState)`, `OnNoiseEmitted(Vector3,float,NoiseSource)`, `OnPlayerDied(ulong)`, `OnPuzzleStepCompleted(string,ulong)`, `OnObjectiveStageChanged(ObjectiveStage)`, `OnAftermathTriggered(AftermathType,Vector3)`, `OnSpectatorTargetChanged(ulong)`. Add new events here rather than building a parallel channel.

`playerId` is always `ulong`, matching NGO's `NetworkManager.LocalClientId`/`OwnerClientId` — do this even before M2 wires up real networking, so nothing needs retrofitting.

Shared enums (`EntityState`, `NoiseSource`, `AftermathType`, `ObjectiveStage`) live in `Core/GameEnums.cs`.

## ScriptableObject Data Tables

All under `CreateAssetMenu(menuName = "The Last Ward/...")`. Class definitions in `Scripts/<System>/`; actual asset instances get created in-Editor and live in `Assets/_Project/Data/`.

| Class | Location | Purpose |
|---|---|---|
| `KnowledgeConfig` | `Scripts/Knowledge/` | Score values per source, decay, targeting weight `k`, whisper-tell range |
| `EntityTuning` | `Scripts/Entity/` | Per-state speeds, vision/hearing, state timing, aggression tier curves, mimicry cap |
| `ClueDefinition` | `Scripts/Puzzles/` | One clue: text, knowledge value, spawn pool id, contradiction flag |
| `PuzzleStepDefinition` | `Scripts/Puzzles/` | One puzzle: required clue ids, gated area, noise radius/source |
| `AftermathTemplateDefinition` | `Scripts/Aftermath/` | One death-message template: type, aggression tier range, prefab, weight |
| `AudioEventDefinition` | `Scripts/Audio/` | One audio event: clip pool, mixer snapshot, volume/spatial params |

## Networking Sync-Surface Table (authoritative reference for M2+)

| Data | Authority | Sync method |
|---|---|---|
| Player transform/look | Owning client | `NetworkTransform` (owner-authoritative) |
| Flashlight toggle | Owning client | `NetworkVariable<bool>` (owner-writable) |
| Entity transform + state enum | Server | `NetworkTransform` + `NetworkVariable<EntityState>` |
| Knowledge scores | Server | `NetworkVariable<float>` per player, not broadcast to clients as raw numbers (hidden — see `PROTOTYPE_PLAN.md` §6) |
| Puzzle/door/lock state | Server | `NetworkVariable<bool>` |
| Held item ids | Server | `NetworkVariable<int>` |
| Alive/dead flags | Server | `NetworkVariable<bool>` |
| Objective stage | Server | `NetworkVariable<ObjectiveStage>` |
| Spectator target id | Owning client (local choice) | Not networked — each dead client picks its own watch target locally |
| Interactions | Client requests, server validates | `ServerRpc` → server applies → state syncs via the table above |

Everything not in this table (ambience timing, mimicry stingers, cosmetic flicker) is decided locally per-client off the `GameEvents` broadcast — don't add it to `NetworkVariable`s.

## Milestone Status

M0 (this scaffold) complete. Next: M1 — FP controller + interaction (see `PROTOTYPE_PLAN.md` §14 for the full roadmap and §18 for the exact next-prompt sequence).

## Conventions

- No comments unless explaining a non-obvious constraint (see repo-wide style rule — this file itself follows it).
- File-complete scripts only when generating code, no snippets/ellipses.
- CC0 first for any asset import; log every CC-BY asset in `CREDITS.md` immediately, never NC/SA/ND — see `PROTOTYPE_PLAN.md` §11.
- No blood decals anywhere in the project — dread comes from implication (`PROTOTYPE_PLAN.md` §9).
