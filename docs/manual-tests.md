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
- `edited <RootName> prefab for <ids>` — once per graphical prefab, the
  first time the game converts its objects in this process: five lines at
  load — `SignArrow`, `SignPostSkull`, `SignPostBrute`, `YellowWarningSign`
  (for both warning signs) and `WoodenSign` (for both wooden signs). `<ids>`
  lists every target id on that prefab as `<name> (<id>)`. A hosting client
  converts twice, once for its client world and once for its server world;
  the line still appears only once. The dedup is process-wide, not per-world:
  loading a second world without restarting the game converts again but
  prints none of the five.

Placing a sign adds a few more lines, once per placement rather than once per
session:

- `placement at (<x>,<z>)` — the local player's own placement, read from
  their replicated `PlacementCD`.
- Then at most one of these three for the same tile:
  - `default <state> sent at (<x>,<z>) after <seconds> s` — the configured
    default was sent for the sign that spawned there. `<state>` is the
    `Default visibility` option as it read when the sign spawned, `<seconds>`
    the time from the placement to the send, to two decimals. The send waits
    for the server to confirm the sign (see *Default visibility* below), so
    this trails the spawn slightly. The line says the request went out, not
    that the server applied it.
  - `default skipped at (<x>,<z>): set in the sign window` — the player had
    the sign's window open and had already moved its toggle off Hover when
    the default was due, so their choice stands and nothing is sent.
  - `default not applied at (<x>,<z>): not confirmed by the server within 5 s`
    — the server never confirmed the sign within 5 seconds, so nothing was
    sent. Not expected in a healthy session.
- After a `sent` line, possibly `default <state> at (<x>,<z>) not confirmed by
  the server within 2 s` — the new state did not come back from the server
  within two seconds of the send, so the mod stopped watching for it. The send
  itself is not undone; check the sign's state in its window. Not expected in a
  healthy session.

  None of the three `default` lines above appears when the option is Hover (a new sign already
  starts there), when no `LabeledSign` spawns on that tile within the matching
  window, or when the sign despawns or its state changes from Hover before the
  send — someone already chose, which is not a failure.

Anything else is a warning or an error, and none of it is logged per frame. Most
lines appear at most once per session; the ones marked *per placement* can
repeat, once for each sign they concern.

- `failed to edit <RootName> prefab (<failure>); ids <ids> get no interaction
  triggers` — once per prefab. Those signs cannot be labelled, and the game
  still loads. `<failure>` names the step that failed; for all but the last two
  the prefab was not touched and the signs stay vanilla:
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
  - `the edit threw <exception>; the prefab may be partly edited` — an edit
    step raised an exception; the text names it. The root may already be a
    `LabeledSign`.
  - `the edit did not leave exactly one LabeledSign and one InteractableObject
    on the root; the prefab is partly edited` — the edit ran without an
    exception but its result is not the expected one.
- `<RootName> conversion threw (<exception>); <name> (<id>) gets no interaction
  triggers` — once per distinct exception. Something around the prefab edit
  threw outside the edit's own guard; the game still loads.
- `adding interaction triggers for <name> (<id>) threw (<exception>); it may not
  be interactable` — once per distinct exception. The prefab edit succeeded,
  but some of the trigger components are missing from that sign's entity.
- `LabeledSign <name> has no InteractableObject to move; it cannot be
  interacted with` — an instance of an edited prefab lost its interaction point
  before `Awake`.
- `LabeledSign <name> has no text object source; its label cannot show` — an
  instance woke before any prefab edit ran, or the text sign's `WorldText` had
  no `ObjectNameTag`.
- `LabeledSign <name> threw while <step> (<exception>); <consequence> (each
  distinct failure logged once per session)` — once per step and distinct
  exception, not per sign. `<step>` is moving its InteractableObject to a child,
  attaching its text object, or wiring its interaction. The game's own
  initialisation of the sign still ran; only that part of the mod's is missing.
- `AnySpawned handler threw` — followed by the exception; a subscriber to
  `LabeledSign.AnySpawned` failed. The sign itself spawned normally.
- `Init threw (<exception>); the defaultVisibility option is unavailable and new
  signs start at Hover` — building the Mod Settings Menu option failed. The
  labels are unaffected.
- `DefaultVisibility.Bind received a null handle; defaultVisibility stays at
  Hover and ignores the menu.` — Mod Settings Menu failed to build the
  Choice; the default stays at the vanilla Hover and the menu option has no
  effect.
- `DefaultVisibility.Bind called more than once — the later handle wins.` —
  a warning, not expected in a normal load; `Init` should call `Bind` once.
- `placement watch threw (<exception>); pending defaults were dropped, and new
  signs may not get the default visibility (each distinct failure logged once
  per session)` — once per distinct exception. The per-frame watch failed
  outside its per-sign guards. It keeps running every frame, so a one-off
  failure costs only the signs that were waiting; a persistent one means the
  default stops working for the rest of the session, with this line as its only
  trace.
- `<source>: further distinct failures suppressed after 8` — `<source>` is
  `placement watch` or `LabeledSign.Awake`. Once each; after it, new kinds of
  failure from that source are no longer logged.
- `default send at (<x>,<z>) threw (<exception>); dropped` — *per placement*.
  Sending the default for that sign failed; it stays at Hover.
- `echo watch threw (<exception>); dropped a pending window refresh` — *per
  placement*. Watching for the server's answer failed; a sign window opened
  in that gap may keep showing Hover until it is reopened.

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
  `default … sent` line naming the same tile and the matching state —
  except at Hover, where no `default … sent` line appears, since a
  freshly placed sign already starts there.
- A sign loaded from a save keeps its stored state regardless of the current
  option, with no `placement at` or `default … sent` line for it.
- A sign streamed in by walking into its chunk keeps its state the same way.
- Mine a sign and place a new one on the same tile right away: the new one gets
  the current default too, with its own `placement at` / `default … sent`
  pair.

With the option at Off or Always, the sign window's toggle against the default:

- Place an arrow and open its window immediately: the toggle may show Hover when
  the window opens, then switches to the default on its own a moment later,
  without closing and reopening the window; the log shows a `default … sent`
  line.
- Place an arrow, open its window immediately and change the toggle before it
  switches: your choice is kept, and the log shows `default skipped at …: set
  in the sign window` instead of a `sent` line.
- Place an arrow, open its window immediately and type a text: the default
  still applies — text is not a visibility choice.
- Place an arrow, open its window, close it and reopen it right away, so the
  reopening falls between the send and the server's answer: the reopened
  window may show Hover, then switches to the default on its own, and the log
  shows no `not confirmed by the server within 2 s` line.

**A newly placed sign is a client-predicted spawn, and the default waits for the
server to confirm it.** The sign's entity carries no real ghost id yet
(`GhostInstance.ghostId == 0`) and, in the frame it spawns, a
`PredictedGhostSpawnRequest` — the handbook's multiplayer chapter has NetCode
remove that component one step later regardless, so it is no marker to wait on;
an RPC naming the entity cannot be resolved by the server either way. NetCode
later promotes the same entity to the confirmed ghost, so the mod checks every
frame and sends `SetWorldLabelVisibility` once the ghost id is real, provided
the sign still exists and is still at Hover. The game's sign window reads the
state only when it opens, so if it is open on that sign at the moment of the
send, the mod sets the window's toggle to match — or, if the player already
moved the toggle off Hover, sends nothing. A window opened after the send but
before the server's new state reaches the client still reads Hover; the mod
watches for that state for up to two seconds and, once it arrives, sets the
toggle of a window still open on that sign, unless the player has moved the
toggle off Hover.

**Result, 2026-09-30 (CK 1.3.0.4, singleplayer, macOS/CrossOver, dev build
9999984):** the gap between placement and send was 0.12–0.18 s across ten
placements. Always ended at state 2 and Off at state 0 when checked a second
later. Signs loaded from the save kept their stored state, with no `placement
at` line at load. Not run: confirming no `default … sent` line at Hover
(the ten placements covered Off and Always only), a sign streamed in by
walking into its chunk, and mining a sign and placing a new one on the same
tile right away.

**Result, 2026-09-30 (same setup, default Always), sign-window checks:**

- Open the window immediately: PASS — the toggle switched to Always on its own
  a moment later, without reopening.
- Open immediately and type a text: PASS — the sign ended at Always.
- Window closed and reopened between the send and the server's answer: not run
  — added after this run, and like the next case the gap is short enough that
  hitting it by hand is uncertain.
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

Over a server the client's `default … sent` line trails the placement by
more than in singleplayer, since the confirmation is a network round trip. A
`placement at` line with no `sent` line after it comes from placing
something other than a sign — the placement record changes for any placed
object — and is expected.

**Result, 2026-09-30 (CK 1.3.0.4, local dedicated server in the same
CrossOver bottle, world "Test", one client, dev build 9999984):**

- Server log: PASS — exactly five `edited` lines, all five graphical prefabs,
  then `Mod initialized.`; no `failed` line and no exception.
- Joining, labelling and toggling, default Always on new signs including the
  open window's toggle, mining a sign at Always (one item), painting the Arrow
  Sign, and disconnect plus rejoin: PASS.
- Client log: `default Always applied … after` (the send line's wording at the
  time; it now reads `sent`) 0.31–0.35 s, against 0.12–0.18 s in
  singleplayer.
- Second client: not run yet — open.

More Labels 2.1.1 was loaded (`Successfully compiled NameChests`) during every
run on 2026-09-30, the singleplayer ones included, with its hover option at its
default; no interference was observed. Only that default option is covered.
