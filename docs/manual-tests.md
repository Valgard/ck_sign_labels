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
  current `Default visibility` option, `<seconds>` the time since the
  `placement at` line above, to two decimals. Absent when the option is Hover
  (a freshly placed sign already starts there), when no `LabeledSign` spawns
  on that tile within the matching window, or when the sign that spawns there
  already carries text (a re-placed sign the game recognises as the old one).

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

- Place a new arrow: its label state matches the option right away, without
  touching the sign's own toggle in its window. `Player.log` shows a
  `placement at` line for the placement, followed by a `default … applied`
  line naming the same tile and the matching state — except at Hover, where
  no `default … applied` line appears, since a freshly placed sign already
  starts there.
- A sign loaded from a save keeps its stored state regardless of the current
  option, with no `placement at` or `default … applied` line for it.
- A sign streamed in by walking into its chunk keeps its state the same way.
- Mine a sign and place a new one on the same tile right away: the new one gets
  the current default too, with its own `placement at` / `default … applied`
  pair.
