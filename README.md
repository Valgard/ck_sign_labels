# Sign Labels

A Core Keeper mod that gives seven placeable signs a label — text and
visibility, edited in the game's own sign window, exactly like the vanilla
text sign.

Core Keeper's text sign, the metropolis sign and the excavation sign already
carry a label. The arrow and the other decorative signs don't — a player who
wants a waypoint has to plant a text sign next to the arrow. This mod removes
that workaround.

## Features

- **Seven signs get a label:**
  - Arrow Sign (*Richtungsschild*)
  - Skull Effigy (*Schädel-Schild*)
  - Brute Sign (*Schläger-Schild*)
  - Yellow Warning Sign (*Gelbes Warnschild*)
  - Red Warning Sign (*Rotes Warnschild*)
  - Wooden Sign "Core" (*Holzschild „Kern"*)
  - Wooden Sign "Tulip" (*Holzschild „Tulpe"*)

  The three signs that already carry a vanilla label — the text sign, the
  metropolis sign and the excavation sign — are untouched. Holoboards are
  not covered.
- **The game's own sign window.** Interacting with a labelled sign opens the
  same window the text sign uses: type a label and set its visibility — Off,
  only while targeted, or Always — with the game's own toggle, parental
  filter and controller support.
- **A default for new signs.** In **Options → Mod settings → Sign Labels**,
  set the visibility a freshly placed sign should start at — Off, Hover or
  Always, default Hover (the vanilla behaviour for anything you place). Only
  your own placements are affected; a sign loaded from a save or streamed in
  by walking into its chunk keeps its own state. A freshly placed sign
  briefly shows Hover before switching to the configured default, well under
  a second.
- **Mining drops exactly one item**, whatever the sign's visibility — a sign
  set to Always never drops two just because its underlying "amount" field
  is 2.

## Requirements

- Core Keeper 1.3 (verified on 1.3.0.4)
- [CoreLib](https://mod.io/g/corekeeper/m/corelib) — required dependency
- *Mod Settings Menu* — required dependency. Hosts the in-game settings
  screen (Options → Mod settings) where the default visibility is set.
- **Needed on both sides in multiplayer.** The server adds the label data to
  these signs' entities, and a client without the mod would get a different
  entity layout for the same signs, so this mod must be installed on the
  server and on every connecting client — a client without it cannot join a
  server that has it.

## Installation

Subscribe in-game through the **Mods** menu (or on the mod.io website) and
restart the game. CoreLib and the Mod Settings Menu must both be installed
alongside this mod — on every client, and on the server.

## How to use

1. Place one of the seven signs above, or find one already standing.
2. Interact with it: the game's sign window opens.
3. Type a label and pick its visibility, exactly as on a text sign.

To change what a *new* sign starts at, open **Options → Mod settings → Sign
Labels** and set **Default visibility**.

## Compatibility

Works alongside **More Labels** with its default settings — both mods were
loaded together throughout testing. More Labels patches the game's own label
rendering and hover logic globally, so its hover option is expected to apply to
these signs' labels too, as it does to the game's other world labels; that
option itself has not been tested with this mod.

## Known Limitations

- **Holoboards are not covered** — the small, large and broken info board.
  Giving them a label the way these seven signs get one would lose their
  break effects and, for the small board, its sprite-variant logic.
- **The default-visibility setting only covers these seven signs** — not the
  three vanilla label carriers or chests.

## Localisation

The mod's own strings — the Mod Settings Menu section and its option — ship
in **English and German** and follow the in-game language. A sign's label
text is whatever you type into it; it is not translated.

## Build (developer)

See `CLAUDE.md` for the build and deploy procedure, and `docs/manual-tests.md`
for the in-game verification checklist.

## License

Personal-use, non-commercial — Pugstorm Core Keeper EULA. Built against the
official `CoreKeeperModSDK`.
