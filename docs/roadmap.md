# SignLabels — roadmap

Planned, not-yet-built work after 1.0.0. Each point stands on its own; pick the
next one, and let the version number collect whatever has shipped.

## Test with a second client

1.0.0 was verified in singleplayer and against a dedicated server with **one**
client. What only a second player can show is still unobserved:

- **The default stays with the placing player.** `DefaultVisibility` reacts to
  the local player's own `PlacementCD`, so a sign the other player places must
  start at the game's Hover, whatever this player's Default visibility says —
  and the other player's option must not touch this player's signs. That
  follows from the code; nobody has watched it happen.
- **Labels and states reach the other player.** Text and visibility travel
  through the game's own `SetDescription` and `SetWorldLabelVisibility` RPCs,
  which is why nothing is expected to differ — but a second client is the only
  way to see what a player who did not write the label actually gets.
- **A sign the other player changes while this one has its window open.** The
  sign window reads the state once when it opens; whether a foreign change
  leaves it showing the old value is unknown.

Setup: two clients on the local dedicated server, both with the mod. The
checklist line is already in `docs/manual-tests.md` (*Dedicated server and
multiplayer*); record the result there. The published mod.io build will serve
as well as a dev build, as long as each client runs only one of the two.
