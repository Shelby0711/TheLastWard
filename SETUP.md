# Setup

## Why this file exists

This scaffold was built without a working Unity Editor on the machine — Unity Hub was installed but the `6000.5.3f1` entry it listed turned out to be metadata only, no actual editor binaries. So instead of running Unity in batch mode to generate the project (the normal way), the project files that are pure text — `Packages/manifest.json`, `ProjectSettings/ProjectVersion.txt`, all `Assets/_Project` scripts, the `.inputactions` asset — were hand-authored. Everything Unity itself has to generate (the rest of `ProjectSettings/`, `.meta` files, package resolution) happens automatically the first time the project is opened in a real Editor. This file is the one-time manual pass to get from "hand-authored scaffold" to "fully working project."

## 1. Install the Editor

Open Unity Hub → Installs → Install Editor. Pick **6000.5.3f1** if it's still offered; if not, any Unity 6000.5.x or 6000.4.x LTS is fine — the packages pinned in `Packages/manifest.json` will resolve to the closest compatible version either way. Default modules are enough (no WebGL/Android/iOS needed yet — this prototype targets PC first per `PROTOTYPE_PLAN.md`).

## 2. Open the project

Unity Hub → Projects → Add → select `C:\Users\saaha\The Last Ward`. Open it with the installed 6000.5.x editor. **First open will take several minutes** — Unity is generating the missing `ProjectSettings/*.asset` files and downloading/resolving every package in the manifest (URP, Input System, Netcode for GameObjects, Unity Services Multiplayer, ProBuilder, AI Navigation) from scratch.

If Unity prompts to enable the new Input System backends and offers to restart — accept it. This project only uses the new Input System (see `Assets/_Project/Input/PlayerControls.inputactions`), not the legacy one.

If Package Manager shows a resolution error on any single package, that's expected — the version numbers in `manifest.json` were best-effort pins made without a live registry check. Open that package's entry in Package Manager and click Update to the nearest available version; nothing in the scaffold depends on an exact patch.

## 3. Activate URP (one-time manual step)

Because this project wasn't created from Hub's "Universal 3D" template, the render pipeline isn't wired up yet:

1. **Window → Package Manager** — confirm `Universal RP` is installed.
2. **Project window → right-click `Assets/_Project/Data` → Create → Rendering → URP Asset (with Universal Renderer)**. This creates two assets: a pipeline asset and a renderer.
3. **Edit → Project Settings → Graphics** — set *Scriptable Render Pipeline Settings* to the URP asset from step 2.
4. **Edit → Project Settings → Quality** — for each quality level, set the same Render Pipeline Asset.
5. If any default material looks magenta: **Edit → Rendering → Materials → Convert Selected Built-in Materials to URP** (or Convert All).

## 4. Unity Services (needed for M2, not before)

M1 doesn't need this. When you reach M2 (co-op shell / NGO + Relay + Lobby): **Edit → Project Settings → Services**, link the project to a Unity Cloud project on the free tier, then enable Relay and Lobby in the Unity Cloud dashboard. Skip this entirely for now.

## 5. Sanity check

Once the above is done: `Assets/_Project/Scripts/**/*.cs` should compile with zero errors (they're plain C#/ScriptableObject definitions with no scene dependencies yet), and `Assets/_Project/Input/PlayerControls.inputactions` should open in the Input Actions editor without complaint. If both are true, M0 is verified and M1 (FP controller) can start.
