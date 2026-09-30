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

Placing a sign adds up to two more lines, once per placement rather than once
per session:

- `placement at (<x>,<z>)` — the local player's own placement, read from
  their replicated `PlacementCD`.
- Then at most one of these three for the same tile:
  - `default <state> applied at (<x>,<z>) after <seconds> s` — the configured
    default was sent for the sign that spawned there. `<state>` is the
    `Default visibility` option as it read when the sign spawned, `<seconds>`
    the time from the placement to the send, to two decimals. The send waits
    for the server to confirm the sign (see *Default visibility* below), so
    this trails the spawn slightly.
  - `default skipped at (<x>,<z>): set in the sign window` — the player had
    the sign's window open and had already moved its toggle off Hover when
    the default was due, so their choice stands and nothing is sent.
  - `default not applied at (<x>,<z>): not confirmed by the server within 5 s`
    — the server never confirmed the sign within 5 seconds, so nothing was
    sent. Not expected in a healthy session.

  None of them appears when the option is Hover (a new sign already starts
  there), when no `LabeledSign` spawns on that tile within the matching window,
  or when the sign despawns or its state changes from Hover before the send —
  someone already chose, which is not a failure.

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

With the option at Off or Always, the sign window's toggle against the default:

- Place an arrow and open its window immediately: the toggle may show Hover when
  the window opens, then switches to the default on its own a moment later,
  without closing and reopening the window; the log shows a `default … applied`
  line.
- Place an arrow, open its window immediately and change the toggle before it
  switches: your choice is kept, and the log shows `default skipped at …: set
  in the sign window` instead of an applied line.
- Place an arrow, open its window immediately and type a text: the default
  still applies — text is not a visibility choice.

**A newly placed sign is a client-predicted spawn, and the default waits for
the server to confirm it.** The sign's entity carries no real ghost id yet
(`GhostInstance.ghostId == 0`) and still has `PredictedGhostSpawnRequest`; an
RPC naming it then cannot be resolved by the server. NetCode later promotes the
same entity to the confirmed ghost, so the mod checks every frame and sends
`SetWorldLabelVisibility` once the ghost id is real, provided the sign still
exists and is still at Hover. The game's sign window reads the state only when
it opens, so if it is open on that sign at the moment of the send, the mod sets
the window's toggle to match — or, if the player already moved the toggle off
Hover, sends nothing. A window opened after the send but before the server's
new state reaches the client still reads Hover; the mod watches for that state
for up to two seconds and, once it arrives, sets the toggle of a window still
open on that sign, unless the player has moved the toggle off Hover.

**Result, 2026-09-30 (CK 1.3.0.4, singleplayer, macOS/CrossOver, dev build
9999984):** the gap between placement and send was 0.12–0.18 s across ten
placements. Always ended at state 2 and Off at state 0 when checked a second
later. Signs loaded from the save kept their stored state, with no `placement
at` line at load.

**Result, 2026-09-30 (same setup, default Always), sign-window checks:**

- Open the window immediately: PASS — the toggle switched to Always on its own
  a moment later, without reopening.
- Open immediately and type a text: PASS — the sign ended at Always.
- Open immediately and change the toggle before it switches: not run — the
  gap of about 0.15 s is too short to click in, so this is not reproducible by
  hand. That the code keeps a toggle already changed in the window is reviewed
  in the code, not observed.

## Dedicated server and multiplayer

Against a dedicated server with the mod installed on both sides:

- The server log holds the five `edited … prefab for …` lines as listed under
  *Reading the log*, then `Mod initialized.`, and no `failed` line.
- A client with the mod joins without a mod warning.
- Labelling a sign and switching its toggle works as in singleplayer.
- The default visibility applies to newly placed signs, and a window opened
  immediately switches its toggle on its own, as under *Default visibility*.
- Mining a sign set to Always drops exactly one item.
- Painting the Arrow Sign keeps its colour and its label.
- Disconnecting and rejoining keeps every text and visibility state.
- With a second client: the default applies only to signs the placing player
  placed, and the other player sees the labels and states.

Over a server the client's `default … applied` line trails the placement by
more than in singleplayer, since the confirmation is a network round trip. A
`placement at` line with no `applied` line after it comes from placing
something other than a sign — the placement record changes for any placed
object — and is expected.

**Result, 2026-09-30 (CK 1.3.0.4, local dedicated server in the same
CrossOver bottle, world "Test", one client, dev build 9999984):**

- Server log: PASS — exactly five `edited` lines, all five graphical prefabs,
  then `Mod initialized.`; no `failed` line and no exception.
- Joining, labelling and toggling, default Always on new signs including the
  open window's toggle, mining a sign at Always (one item), painting the Arrow
  Sign, and disconnect plus rejoin: PASS.
- Client log: `default Always applied … after` 0.31–0.35 s, against
  0.12–0.18 s in singleplayer.
- Second client: not run yet — open.

More Labels 2.1.1 was loaded (`Successfully compiled NameChests`) during every
run on 2026-09-30, the singleplayer ones included, with its hover option at its
default; no interference was observed. Only that default option is covered.
