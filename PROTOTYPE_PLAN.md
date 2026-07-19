# The Last Ward — Prototype Development Plan

Source of truth for design intent: [the_last_ward_game_bible.md](the_last_ward_game_bible.md). This document is the technical/production plan built from it. Human-reference only — coding sessions should load `PROJECT_CONTEXT.md` instead, not this file, to stay token-efficient. Update this file's Milestone Roadmap checkboxes as milestones land; keep everything else stable so it stays a reliable reference.

Naming note: standardize on **The Last Ward** everywhere (the bible also uses "The Lasr Ward" [typo] and "The Ward That Remembered" — treat those as historical/marketing variants only). Player count: prototype supports 1–4 (solo included for cheap testing; design targets 2–4).

---

## 1. Key Design Pillars (prototype must prove all five)

| Pillar | Prototype proof |
|---|---|
| Knowledge is expensive | Hidden per-player knowledge score visibly changes who the Entity hunts |
| Co-op under pressure | Puzzles unsolvable by one player's information alone |
| The dead still matter | Tethered spectator who spots at least one thing per session the living missed |
| Horror through implication | Aftermath scenes, sound design, zero gore assets |
| Memorable antagonist | Entity that stalks, mimics audio, and stages bodies as messages |

## 2. Prototype Scope

**In:** one map slice (roadside exterior → Lobby/Reception → Orphan Ward → Service Corridor → Exit Route); 1–4 player listen-server co-op via join code; Entity FSM (patrol/investigate/search/chase/lost/return) + knowledge-weighted targeting + 2 mimicry tricks; 3 puzzle archetypes (fuse/power, record-code, intercom sequence); hidden knowledge tracking; death → tethered spectator with player-switching; body/message system with 3 aftermath templates; one escape objective + one ending sequence (single-slot exit + farewell song stub); flashlight, key/fuse/note pickup, 2-slot inventory; basic settings save.

**Out (deferred — see §17):** multiple entity forms/monsters, Acts III–IV content, Staff/Burned/Basement wings, sanity meter, procedural layouts beyond clue shuffling, reconnect logic, accounts/telemetry, voice acting, cutscenes, branching endings beyond the one-slot choice, custom character models, mobile.

## 3. Tech Stack

| Layer | Choice | Why |
|---|---|---|
| Engine | Unity 6 (6000.5.3f1) + URP | Best free-asset ecosystem; URP handles the retro look cheaply |
| Networking | Unity Netcode for GameObjects (NGO) + Unity Relay + Lobby (UGS free tier) | Free at 4-player scale, no dedicated servers, join-by-code, best AI-codegen documentation coverage |
| Rendering | URP, render scale ~0.66 or a free PSX-style post shader; baked + few realtime lights | Retro look = performance headroom |
| Input | Unity Input System (new) | Rebindable, standard |
| Audio | Plain Unity AudioSource + Mixer snapshots | FMOD deferred — 4 snapshots cover the needed states |
| AI navigation | Unity NavMesh (AI Navigation package) + custom C# FSM | No behavior-tree asset needed at this complexity |
| Animation | Mixamo humanoid clips + Animator | Free, covers all locomotion needs |
| Modeling | Blender only for kitbash fixes; no custom modeling as policy | Scope rule |
| Version control | Git + Git LFS | Standard |
| Save | JSON via `JsonUtility` to `persistentDataPath` (settings only) | Simplest |

## 4. Co-op Implementation Plan

**Architecture: listen server (host = player 1 = authority).** No dedicated servers, no cost.

- **Join flow:** Main menu → Host (creates UGS Lobby + Relay allocation, shows a 6-char code) → others enter the code → lobby scene shows connected players → host starts the run. Solo = host alone, Relay skipped entirely.
- **Authority split:**
  - Server-authoritative: Entity AI, knowledge scores, puzzle state, door/lock state, clue placement seed, deaths, aftermath scenes, endgame state.
  - Client-authoritative (owner): player movement + look (`NetworkTransform` with owner authority), flashlight toggle.
  - Interactions: client sends `ServerRpc` "interact with X" → server validates range/state → applies → syncs via `NetworkVariable`.
- **Sync surface (kept small):** player transforms, Entity transform + one animation-state enum, door/lock booleans, held-item ids, alive/dead flags, objective stage, spectator target id. Everything cosmetic (flicker, ambient stingers) runs locally from server-broadcast event ids via `GameEvents`.
- **Voice:** Discord for the prototype; in-game proximity voice (Vivox) is a fast-follow, not M1 material.
- **Disconnect handling:** disconnected player's pawn dies in place (feeds the aftermath system for free). No reconnect in the prototype.

## 5. Entity AI Plan

Single NavMesh agent, server-side FSM, six states:

| State | Behavior | Exit conditions |
|---|---|---|
| Patrol | Walk waypoint graph biased toward the highest-knowledge player's *zone* | Hears noise → Investigate; sees player → Chase |
| Investigate | Move to last stimulus point, idle-scan | Nothing found → Search; sees player → Chase |
| Search | Sweep 2–3 nearby rooms/hiding spots | Timer expires → Return; contact → Chase |
| Chase | Pursue seen player; speed slightly above player walk, below sprint | LOS lost + timer → Lost Target |
| Lost Target | Predict-move toward last velocity, then Search | — |
| Return | Walk toward patrol zone; teleport only when fully unobserved | — |

**Senses:** vision cone (crushed by darkness) + hearing events (noise radius per interaction). Both feed a per-player suspicion value.

**Knowledge layering:** target weight = `base + knowledgeScore × k + recentNoise`. Highest-knowledge player gets patrol-zone bias, +2–3s chase LOS memory, and priority when two players are visible. Team-total knowledge drives a 0–3 aggression tier that raises patrol speed, search thoroughness, and event frequency — this gives Act-structure pacing for free.

**Fear/mimicry (rationed):** (1) audio mimicry — distorted teammate sound from the wrong direction near the top-knowledge player, max 2/run; (2) corner presence — Entity patrol paths deliberately cross corridor ends, seen not engaging; (3) stalk-then-leave — stands near a hiding player 5–10s, then leaves.

**Motion:** locomotion clip at 0.85× speed while movement is 1.0×, plus subtle head-track toward target even while walking away — cheap "feels wrong."

## 6. Knowledge System Plan

Hidden per-player float, server-side, never shown numerically. Sources (ScriptableObject-tunable): note read (+small, once/note/player), puzzle step solved (+medium, credited to interactor), new area unlocked (+large), first into a zone (+small). Slow linear decay to a floor (never to zero). Consumption: Entity target weighting + aggression tier. One tell: the highest-knowledge player alone hears faint whispers within ~20m of the Entity — makes the mechanic legible and lets the group infer who's "marked." Reading (not proximity) is what scores, so a group can nominate one "reader" — that's the intended social tension, not an exploit.

## 7. Puzzle and Clue Plan

| # | Puzzle | Gates | Clue distribution | Co-op pressure |
|---|---|---|---|---|
| P1 | Fuse/power routing — 2 fuses + order note into a breaker box | Lobby → Orphan Ward | Fuses in 2 rooms, order-note in a third | Splitting up is faster but lonelier; loud interaction |
| P2 | Record-code — Ward door keypad from 3 patient files matched by a criterion note | Ward → Service Corridor | 4 files + 1 criterion note; one file is a deliberate contradiction (wrong date) | No one player has all files; brute force blocked by keypad noise + digit space |
| P3 | Intercom sequence — 3 stations activated in an order from a looping radio announcement | Corridor → Exit | Announcement audible only near the reception radio; stations spread across the map | One player camps the radio and calls out the order while others run stations |

Clue spawn positions shuffle among ~3 pool points each per playthrough. Every solving interaction emits noise and awards knowledge — progress = danger, mechanically. Every clue doc doubles as lore per the bible's clue-tier list.

## 8. Dead-Player / Spectator Plan

On death: camera locks to a living player's synced view (no extra streaming — the spectator's client already has full world state). Q/E cycles targets (5s cooldown). View is desaturated with static + muffled audio. One tool: a 30s-cooldown "focus" ping that chimes softly at the spectator's gaze point on the watched player's screen. Anti-omniscience: no free camera, same frustum/darkness as the watched player, notes render blurred to spectators (can point, can't read), Entity only visible if the watched player could see it.

## 9. Body / Message System Plan

Server picks a template on death; scene spawns at the nearest hand-placed aftermath anchor. Three templates for the prototype: **Warning** (body posed facing danger, true ~always — teaches the system), **False clue** (arranged toward a dead end, aggression tier ≥2 only), **Mockery/memory** (posed like earlier-found child drawings, tagged to the victim's dominant behavior). Implementation: static posed prefabs (shrouded/silhouetted forms, no ragdoll puzzles, no dynamic posing). **No blood decals anywhere in the project** — dread comes from implied intent, not gore.

## 10. Art Direction Plan

Low/mid-poly, 256–512px textures, heavy fog, dim lighting. URP render scale ~0.66 or a free PSX shader; one flickering fluorescent per corridor + baked ambient; flashlight as primary dynamic light. 6-swatch palette (sickly green, faded white, rust brown, dim red, pale emergency amber, dirty yellow) applied via a shared post-process LUT to regrade mixed third-party textures into cohesion. Long sightlines with a single light at the far end; doorframes as picture frames for staged scenes; Entity always lit from behind/silhouetted. Entity model: Mixamo-compatible humanoid scaled 1.15× height / 0.9× width, black material, faint emissive stitching, no face. Greybox everything first (ProBuilder); art-dress only at Milestone 8.

## 11. Asset Sourcing Plan

| Need | Source | License notes |
|---|---|---|
| Hospital props/furniture | Kenney (CC0), Quaternius (CC0), itch.io PSX horror packs (CC0/CC-BY filter), Sketchfab (CC0 filter only) | CC0 = zero risk; CC-BY logged in `CREDITS.md` |
| Modular walls/corridors | itch.io PSX horror kits, Unity Asset Store free hospital packs | Avoid anything marked restricted |
| Textures/materials | Poly Haven (CC0), ambientCG (CC0) | Regrade to palette |
| UI | Kenney UI packs (CC0) | — |
| SFX | Freesound (CC0 filter only), Sonniss GDC bundles, Kenney audio | Never NC or SA |
| Animations | Mixamo (free, commercial OK) | Covers all humanoid needs |
| Humanoid models | Mixamo, Quaternius | No unverified Sketchfab "fan" models |

Rule: CC0 first, CC-BY second (attribute immediately on import), nothing NC/SA/ND. `ThirdParty/` is the import quarantine folder — only palette-graded, prefab-wrapped assets enter `Assets/_Project`.

## 12. Audio / SFX Plan

Principle: heard before seen — Entity audio radius > vision radius, always. AudioMixer with 4 snapshots (Explore / Stalked / Chase / Dead-view), driven by server-broadcast state ids, played back locally per client. Layers: bed ambience (room tone + hum, CC0 loops), building stress (randomized creaks/thumps, frequency scales with aggression tier), Entity presence (footsteps + breathing + whisper layer gated to the top-knowledge player), mimicry (bitcrushed player foley), chase drone + heartbeat, scripted silence (duck all ambience 4–8s near an Investigate state — cheapest scare in the plan), child fragments (self-recorded music-box/hum, ≤3/run, seeds the farewell song).

## 13. Map / Level Slice Plan

Single scene, ~10–15 minute route, greybox-first (ProBuilder):
1. **Roadside exterior** (~2 min) — broken car, fog boundary, tutorial note, Entity absent (dread via ambience only).
2. **Lobby/Reception** — P1 fuse puzzle; first Entity Patrol sighting scripted as a corner-crossing.
3. **Orphan Ward** — P2 record-code; core lore zone, highest clue density, most aftermath anchors.
4. **Service Corridor** — pure traversal tension; loop + two branches for chase counterplay.
5. **Exit Route** — P3 intercom + one-slot exit mechanism (door seals after one person crosses) + farewell sting + ending summary screen.

Every zone has ≥2 routes between key points; doors are the pacing/noise currency; ~4 aftermath anchors per zone; NavMesh covers the exterior too (Entity can appear at windows).

## 14. Milestone Roadmap

- [x] **M0** — Project setup: Unity 6 URP, Input System, NGO + UGS packages, Git/LFS, folder structure, `CREDITS.md`, palette LUT stub.
- [ ] **M1** — Solo core: FP controller, flashlight, interact raycast, note UI, doors, pickups + inventory, greybox Lobby.
- [ ] **M2** — Co-op shell: host/join by code (Relay+Lobby), player sync, networked doors/pickups/notes, 4-player smoke test.
- [ ] **M3** — Entity core: NavMesh FSM (all 6 states), vision/hearing, kill sequence, corridor waypoint authoring.
- [ ] **M4** — Knowledge system: score table, decay, target weighting, aggression tiers, whisper tell.
- [ ] **M5** — Puzzles: P1/P2/P3 + clue shuffle pools + full greybox map + objective flow.
- [ ] **M6** — Death & spectator: death flow, tethered cam, switching, blur-notes rule, ping, audio filter.
- [ ] **M7** — Aftermath + endgame: 3 message templates, anchors, one-slot exit, choice beat, ending screen, farewell sting.
- [ ] **M8** — Art & audio pass: asset dress per zone, palette grade, lighting, full audio table, mimicry events, Entity model/anim.
- [ ] **M9** — Stabilize & tune: 4-player playtests, aggression/knowledge tuning, bug triage, settings save, build.

M2 and M3 are the heavy lifts (~20% each); M8 is time-boxed, not open-ended.

## 15. Risks and Mitigation

| Risk | Mitigation |
|---|---|
| Networking eats the project | NGO + tiny sync surface; network from M2, never retrofit; solo mode keeps testing cheap |
| Entity isn't scary (the #1 creative risk) | Test fear in greybox at M3 with lighting+audio only, before more content is built |
| Knowledge targeting feels random | Whisper tell + epitaph screen make it inferable; ask every playtest "who did it hunt, and why do you think so?" |
| Mixed free assets look incoherent | Palette LUT + texture regrade + darkness/fog; quarantine-folder workflow |
| Spectator boring or overpowered | Tethered cam + blurred notes + single ping; tune the ping cooldown, not the concept |
| Mimicry overused → players tune out audio | Hard cap 2/run |
| Scope creep toward Acts III–IV | Deferred list (§17) is a contract; new ideas go to `BACKLOG.md` |
| License contamination | CC0-first, `CREDITS.md` at import time, no NC/SA ever |
| UGS free-tier setup friction | Set up in M0, not M2; fallback is direct-IP LAN connect |

## 16. Token-Efficient Implementation Strategy

- `PROJECT_CONTEXT.md` + `CLAUDE.md` are what coding sessions load — never this file or the full game bible.
- One prompt = one system with a defined interface. Systems talk via `GameEvents` (event bus) and ScriptableObject data tables so prompts don't need each other's internals.
- Ask for file-complete scripts, not snippets. End each milestone with an integration/test-checklist prompt.
- Sonnet for scaffolding/boilerplate/asset-wiring; Opus for networking (M2), AI + targeting (M3–M4), and debugging sessions.

## 17. Deferred to Later Iterations

Staff/Burned/Basement wings; Acts III–IV clue escalation and contradiction-lore depth; multiple Entity forms and teammate-impersonation mimicry; proximity voice with Entity voice-mimicry; sanity/stamina systems; reconnect + session persistence; additional puzzle archetypes (full audio reconstruction, medical restoration); more aftermath templates + dynamic posing; full farewell-song composition; multiple ending variants; difficulty modes; mobile; any custom modeling; FMOD migration; matchmaking beyond join-code.

## 18. Next Prompts — Implementation Order

1. **Project scaffold** (Sonnet) — done, see `PROJECT_CONTEXT.md`.
2. **FP controller + interaction** (Sonnet) — movement, flashlight, `IInteractable` raycast, note UI, doors, inventory.
3. **Greybox Lobby + test scene** (Sonnet) — ProBuilder blockout + interactables placed.
4. **NGO co-op shell** (Opus) — Relay/Lobby join-code flow, player spawn/sync, networked `IInteractable` pattern.
5. **Entity FSM + senses** (Opus) — state classes, NavMesh, vision/hearing, kill flow, server-side.
6. **Knowledge system + targeting** (Opus) — score service, weighting hooks into Entity, aggression tiers, whisper tell.
7. **Puzzle framework + P1** (Sonnet) — generic `PuzzleStep`/gate pattern, fuse puzzle, noise events.
8. **P2 + P3 + clue shuffle** (Sonnet) — reuses framework from step 7.
9. **Full map greybox + objective flow** (Sonnet).
10. **Death + spectator system** (Opus) — tethered camera, switching, restrictions, ping.
11. **Aftermath message system** (Sonnet) — template prefab spawner + selection logic.
12. **Endgame sequence** (Sonnet) — one-slot exit, choice beat, ending screen.
13. **Audio system + mixer states** (Sonnet) — event-driven playback, snapshots, mimicry events.
14. **Art/lighting dress passes** (Sonnet, per zone) — checklists more than code.
15. **Tuning + bugfix sessions** (Opus, as needed) — driven by playtest notes.
