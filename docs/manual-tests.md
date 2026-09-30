# Manual tests

The placement matcher has an offline harness (`tests/placement-harness`); the
signs themselves only show their behaviour in the running game, so they are
checked here, in game, after a build. Each section lists what to check, then
what the last run found.

The seven target signs are the arrow (`SignArrow`, 4151), the skull sign
(`SignSkull`, 4152), the brute sign (`SignBrute`, 4153), the yellow and the red
warning sign (`SignYellowWarning` 4156, `SignRedWarning` 4157) and the two
wooden signs (`WoodenSign1` 5809, `WoodenSign2` 5810). They sit on five
graphical prefabs: the two warning signs share one, and so do the two wooden
signs.

## Reading the log

Every line the mod writes starts with `[SignLabels]`, so `grep SignLabels
Player.log` shows all of them. A healthy session prints only these, each at most
once:

- `Mod initialized.` — at load.
- `edited <RootName> prefab for <ids>` — once per graphical prefab while the
  game converts its objects, so five lines at load: `SignArrow`,
  `SignPostSkull`, `SignPostBrute`, `YellowWarningSign` (for both warning
  signs) and `WoodenSign` (for both wooden signs). `<ids>` lists every target
  id on that prefab as `<name> (<id>)`. A hosting client converts twice, once
  for its client world and once for its server world; the line still appears
  only once.

Placing a sign adds two more lines, once per placement rather than once per
session:

- `placement at (<x>,<z>)` — the local player's own placement, read from
  their replicated `PlacementCD`.
- `default <state> applied at (<x>,<z>) after <seconds> s` — the configured
  default was applied to the sign that spawned on that tile. `<state>` is the
  current `Default visibility` option, `<seconds>` the time from the
  `placement at` line above to the moment the RPC was actually sent, to two
  decimals. **Not the same moment as the spawn**, and can trail it by a
  perceptible fraction of a second: a freshly placed sign is a client-side
  *predicted* spawn (no server-confirmed ghost id yet), so the mod holds the
  RPC until the server confirms the same entity's ghost — see "Default
  visibility" below. Absent when the option is Hover (a freshly placed sign
  already starts there), when no `LabeledSign` spawns on that tile within the
  matching window, or when the sign that spawns there already carries text (a
  re-placed sign the game recognises as the old one).
- `default not applied at (<x>,<z>): not confirmed by the server within 5 s`
  — a matched placement's entity never received a confirmed ghost id inside
  the 5-second window, so the mod gave up without sending. Not expected in a
  healthy session — see "Default visibility" below. (A sign that despawns or
  changes state/text before its ghost is confirmed is dropped from the wait
  silently, with no line at all — that is the normal "someone else already
  handled it" case, not a failure.)

Anything else is a warning or an error. Each is logged once per session, never
per frame:

- `failed to edit <RootName> prefab (<failure>); ids <ids> stay vanilla` —
  those signs cannot be labelled, and the game still loads. `<failure>` names
  the step that failed:
  - `the object has no graphical prefab` — the target's `PrefabInfo` returned
    none.
  - `the text sign's graphical prefab is not available` — `SignText`'s own
    prefab could not be read, so there is nothing to copy the interaction
    point and the text object from. Every target fails with it.
  - `the text sign has no Interactable child with an InteractableObject` /
    `the text sign has no WorldText child with an ObjectNameTag` — a game
    update restructured `SignText`'s prefab.
  - `the root has no EntityMonoBehaviour` / `the root is <component>, expected
    <RootName>` — a game update renamed or replaced the target's root class.
    Check the class in `Pug.Objects` and the entry in `SignLabelTargets.cs`.
  - `the prefab already has an InteractableObject` — a game update gave the
    sign an interaction of its own; the mod stays out of its way.
  - `the prefab has no SpriteObject to outline` — the sign's sprite is no
    longer a `Pug.Sprite.SpriteObject`.
  - `the edit threw <exception>` — an edit step raised an exception; the text
    names it.
  - `the edit did not leave exactly one LabeledSign and one InteractableObject
    on the root` — the edit ran without an exception but its result is not the
    expected one.
- `LabeledSign <name> has no InteractableObject to move; it cannot be
  interacted with` — an instance of an edited prefab lost its interaction point
  before `Awake`.
- `LabeledSign <name> has no text object source; its label cannot show` — an
  instance woke before any prefab edit ran, or the text sign's `WorldText` had
  no `ObjectNameTag`.
- `AnySpawned handler threw` — followed by the exception; a subscriber to
  `LabeledSign.AnySpawned` failed. The sign itself spawned normally.

## Labelled signs

For each of the seven target signs:

- Place it and target it: the sign shows the hover outline.
- Interact with it: the game's sign window opens — the same one a text sign
  opens.
- Enter a text: the label shows while the sign is targeted (Hover). Switch the
  window's toggle to Always: the label shows without targeting. Switch it to
  Off: the label never shows.
- Clear the text: the label disappears.
- The sign still renders upright, facing the direction it was placed in, with
  its shadow.
- Save, quit to the menu, reload the world: each sign's text and visibility are
  as they were.

Additionally, place the arrow and one warning sign in all four directions and
interact with each: the window opens for every one of them, and the label sits
above the sign it belongs to.

Mine a labelled sign and place it again at once, so the game reuses a pooled
instance: the new sign shows the outline, opens the window and shows its label
like a fresh one.

Place a vanilla text sign and label it: it still works as before, its hover
outline included.

`Player.log` holds five `[SignLabels] edited … prefab for …` lines as listed
under *Reading the log*, and no `[SignLabels] failed` line.

**Result, 2026-09-30 (CK 1.3.0.3, singleplayer, macOS/CrossOver, dev build
9999984):**

Passed, every check. All seven signs rendered upright in their direction with
their shadow, showed the hover outline, opened the game's sign window, showed
the entered text and followed Hover, Always and Off; clearing the text removed
the label. The arrow and a warning sign behaved correctly in all four
directions. A mined sign placed again at once worked like a fresh one, and a
vanilla text sign still worked, outline included. After save, quit and reload
every text and visibility was as before. `Player.log` held exactly the five
`edited` lines (warning signs, wooden signs, skull, brute, arrow, in that order)
followed by `Mod initialized.`; no `failed` line and no `CompileFailed`. The only
exception in the log was the game's own Steam-session `ObjectDisposedException`
at quit, unrelated to the mod.

## Default visibility

In **Options → Mod Settings → Sign Labels**, the `Default visibility` option
cycles Off / Hover / Always, default Hover. With it set to Off, then Hover,
then Always in turn:

- Place a new arrow: its label state matches the option shortly after
  placing it, without touching the sign's own toggle in its window.
  `Player.log` shows a `placement at` line for the placement, followed by a
  `default … applied` line naming the same tile and the matching state —
  except at Hover, where no `default … applied` line appears, since a
  freshly placed sign already starts there.
- A sign loaded from a save keeps its stored state regardless of the current
  option, with no `placement at` or `default … applied` line for it.
- A sign streamed in by walking into its chunk keeps its state the same way.
- Mine a sign and place a new one on the same tile right away: the new one gets
  the current default too, with its own `placement at` / `default … applied`
  pair.

**A newly placed sign is a client-predicted spawn, and the default waits for
the server to confirm it.** The entity the sign spawns as carries no real
ghost id yet (`GhostInstance.ghostId == 0`) and still has
`PredictedGhostSpawnRequest` — NetCode later promotes that *same* entity to
the confirmed ghost rather than replacing it with a second spawn, but an RPC
sent before that promotion would have named an entity the server cannot
resolve. So the mod queues a matched placement and sends
`SetWorldLabelVisibility` only once the entity's ghost id is confirmed,
checked every frame; on a singleplayer/host session this is normally within
the same tick or two (the `<seconds>` in the applied line stays at or near
`0.00`), so the delay is not expected to be visible to a player watching the
sign. If the ghost is never confirmed within 5 seconds — or the sign
despawns, or its state/text changes first — the mod drops the wait instead
of sending (see *Reading the log*). Found and fixed in Task 4's two fix
rounds (2026-09-30): the first RPC attempt targeted the predicted entity
directly and silently had no effect, even though the log showed it as
applied.
