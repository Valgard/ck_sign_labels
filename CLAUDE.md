# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with
code in this repository.

## What this repo is

A Core Keeper mod that gives seven vanilla signs a label, edited in the
game's own sign window: `SignArrow` (4151), `SignSkull` (4152), `SignBrute`
(4153), `SignYellowWarning` (4156), `SignRedWarning` (4157), `WoodenSign1`
(5809) and `WoodenSign2` (5810) — every sign without a vanilla label; the
text sign, `SignTextMetropolis` and the excavation sign already have one and
are untouched. A Mod Settings Menu option sets the visibility state a
freshly placed sign starts at (Off / Hover / Always, default Hover).

Hard-depends on **CoreLib** and **Mod Settings Menu**. `requiredOn: 3`
(Client+Server — see the parent `../CLAUDE.md`'s `requiredOn` rule): the
server needs the mod present to give these signs' entities a description
buffer and interaction triggers at bake time, and the client flag makes the
server announce the mod as required, turning away a client without it. That
is the intended failure — the alternative, letting such a client in with a
different entity layout for these signs, is untested and is exactly what the
mod check exists to prevent. Personal-use, non-commercial (Pugstorm EULA).

The parent `../CLAUDE.md` holds the mod-agnostic SDK/CrossOver guidance
shared with the sibling mods.

## Build and deploy

```bash
source .envrc           # or, from a worktree: source ../../../.envrc && source .envrc
../utils/build.sh       # Unity batchmode build; on Darwin auto-runs install-macos.sh
                        # from a worktree: ../../../utils/build.sh
```

Unity Editor must be closed (it locks the project). `utils/link.sh` symlinks
the repo's `unity/` mirror into `$SDK_PATH/Assets/`; `build.sh` re-runs it on
every build, so worktree switches and repo moves self-heal.

**Concurrent-build / shared-SDK caveat:** all sibling mods share one
`CoreKeeperModSDK` clone with a single `UnityLockfile`. If another session is
building, wait for the lock to release — do not kill it.

## Verification

Two layers, neither wired into the build:

- **Offline, for `PlacementMatcher`'s pure logic:** `dotnet run --project
  tests/placement-harness` — prints one line per assertion and exits non-zero on
  a failure. Not part of the mod build and not a gate; run it after touching
  `PlacementMatcher.cs`, before building.
- **In-game, for everything else:** `docs/manual-tests.md` lists what to
  check per sign, what each log line means, and the last recorded run's
  result. Read it before an in-game check, and record a new run's result
  there rather than only in a commit message.

Every line the mod logs starts with `[SignLabels]`: `grep SignLabels
Player.log`. `docs/manual-tests.md`'s "Reading the log" section explains each
one, including which are a healthy warning (a failed prefab edit just leaves
that prefab's signs without interaction; the game still loads) versus what
should never appear.

## Architecture

| Unit | Responsibility |
|---|---|
| `SignLabelTargets` | The target list, as data: `ObjectID` → the vanilla root component expected on its graphical prefab, checked with an `is` pattern (the load-time sandbox forbids reflection-based type checks). |
| `SignLabelConverter : Converter` | Per target entity, at bake time: `AlwaysDropOneCD` and a `DescriptionBuffer` unconditionally, then — only once `GraphicalPrefabEditor` confirms the prefab edit succeeded — the interaction trigger components. |
| `GraphicalPrefabEditor` | Edits one graphical prefab exactly once, **keyed by the prefab, not by `ObjectID`** — the two warning signs share one prefab, and so do the two wooden signs. Swaps the vanilla root component for `LabeledSign`, carrying its fields over with `JsonUtility`; adds an `InteractableObject` copied from the text sign's. |
| `LabeledSign : WorldLabel` | Per instance, in `Awake`: moves the `InteractableObject` onto a child, clones the text sign's `WorldText` child, wires `Interact`/`OnPlayerLeft`. Raises the static `AnySpawned` event from `OnSpawn`. |
| `PlacementMatcher` | Pure logic, no Unity types: matches a placement observation (start tick + tile) against a spawned sign on the same tile within a short window. Exercised by the offline harness. |
| `DefaultVisibility` | Reads the local player's `PlacementCD` every frame, feeds `PlacementMatcher`, and — once `LabeledSign.AnySpawned` fires for the matched tile — defers `SetWorldLabelVisibility` until the sign's predicted spawn is confirmed by the server. |

## Working rules for this code

Four rules this code depends on. Each is enforced in exactly one place;
breaking it fails silently — a hung load under Wine, a sign rendered
edge-on, or a duplicated drop — never a compile error.

1. **A loaded graphical prefab accepts components, but not children.**
   `PrefabInfo.GetGraphical()` returns a loaded asset (`scene.IsValid() ==
   false`); `AddComponent` and `DestroyImmediate(c, true)` work on it, but
   `Instantiate(x, parent)` does not — Unity logs "Cannot instantiate objects
   with a parent which is persistent" and creates the object unparented. So
   `GraphicalPrefabEditor` only ever adds or removes **components** on the
   asset. Anything that needs its own hierarchy — the moved
   `InteractableObject`, the cloned `WorldText` — is built **per instance in
   `LabeledSign.Awake`**, where the object is a scene object rather than the
   loaded asset.
2. **`interactable` must be set on every instance, and must point at a
   child, never the root.** Spawning copies `interactable` into
   `InteractableObjectReferenceCD`; left null, the hover outline appears but
   interaction does nothing. For a `DirectionCD` entity (the arrow and both
   warning signs), `OnSpawn` also rotates `interactable.transform` to the
   object's direction — on the root that turns the whole sign edge-on, and
   both the sprite and the label vanish. `LabeledSign.Awake` moves the
   asset's root `InteractableObject` onto a new child and points
   `interactable` there, before `base.Awake()` caches it — for all seven
   targets, not only the directional ones, so there is one code path.
3. **`AlwaysDropOneCD` is added unconditionally, before anything that can
   fail.** A sign set to Always has `amount = 2` — the same field visibility
   uses — so without this component mining it would drop two.
   `SignLabelConverter.Convert` adds it first, ahead of the prefab edit and
   the trigger components, so no combination of edit success and failure can
   end up without it.
4. **The trigger components go on only after the prefab edit is verified.**
   `InteractablePostConverter` reads the graphical prefab's first
   `InteractableObject` with no existence check; a trigger buffer on an
   unedited prefab throws there, ECS initialisation fails, and the game
   hangs on the way out under Wine. `SignLabelConverter` adds
   `TriggerUseInteractionBuffer`/`TriggerExitInteractionBuffer` and their
   companion components only after `GraphicalPrefabEditor.EnsureEdited`
   reports success for that prefab — never speculatively.

`../docs/ck/prefabs-and-rendering.md` and `../docs/ck/world-and-mechanics.md`
are the handbook chapters for prefab bake-time edits and world-object
mechanics generally — check them before carrying one of these rules into a
different mod, and correct or extend them there rather than rediscovering
the same failure mode a second time.

## The default's predicted-spawn / echo mechanics

`DefaultVisibility` cannot send `SetWorldLabelVisibility` the moment a placed
sign spawns: a freshly placed sign is a **client-predicted** entity
(`GhostInstance.ghostId == 0`, `PredictedGhostSpawnRequest` present in the
frame it spawns — the parent handbook's multiplayer chapter has NetCode
remove that component one step later regardless, so it is no marker to wait
on), and an RPC naming it cannot be resolved by the server. NetCode later
promotes the *same* entity to the real ghost rather than spawning a second
one, so a matched sign is queued (`_pendingSends`) and the RPC is sent from
`ProcessPendingSends` only once the ghost id is confirmed real. Measured in
singleplayer: 0.12–0.18 s between placement and send.

A second wrinkle: the game's sign window reads the sign's visibility state
once, when it opens, and never again. A window opened in the gap between the
send and the server's answer keeps showing Hover until the mod's own echo
watch (`_awaitingEcho`, up to 2 s) sees the confirmed state and sets the
window's toggle directly — never through `SignTextUI.SetVisibilityState`,
which would send the RPC a second time. If the player has already moved the
toggle off Hover by the time the default is due, their choice stands and
nothing is sent (`default skipped` in the log).

## Where tests live

- `docs/manual-tests.md` — in-game checks, one section per feature, each
  holding the last recorded run's result. Extend it, don't overwrite a run's
  result, when a change needs a new check.
- `tests/placement-harness/` — the offline `PlacementMatcher` harness (see
  *Verification* above); `tests/placement-harness/Harness.cs` has the full
  assertion list.

## Publishing to mod.io

`../utils/upload.sh` publishes via the shared `CLIPublishHelper`; see
`../docs/publishing.md`. The real ids live in
`unity/SignLabels/Editor/SignLabels_modio.asset` and
`unity/SignLabels/SignLabels_Steam.asset` — read them there, not from any
line of prose, this one included.

## macOS / CrossOver

Deployed through the fake-mod.io workaround (parent `../CLAUDE.md`). This
mod's fake mod.io ID is `9999984` — every sibling uses a distinct one, listed
across the sibling `*/.envrc.example` files.

## Conventions

- Commit messages: Conventional Commits (`type(scope): subject`), imperative,
  no emoji.
- Documentation files (`CLAUDE.md`, `README.md`, `docs/`) are English; chat
  answers are German.
- Prefer `git commit --amend` / `git reset --soft` over fix-up commits on a
  personal branch, and `git rebase` over `git merge`.
