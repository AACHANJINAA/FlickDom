# FlickDom Gameplay Rules

Last updated: July 24, 2026

## Token material rules

- Each player uses 3 flick pieces.
- Within a player's 3 pieces, the token materials must all be different.
- The material set is:
  - Wood
  - Iron
  - Rubber
- `TokenData` is the source of truth for both:
  - `physicMaterial`
  - visual `renderMaterial`

## Card draw rules

- The card deck is shuffled before cards are presented.
- After shuffling, 3 random cards are shown to players.
- The 3 visible cards are selected from the shuffled deck.

## Card score rules

- Easy card: 1 point
- Normal card: 2 points
- Hard card: 3 points

## Win condition

- The first player to reach 10 points wins the match.

## Implementation notes

- Scene setup for token sequences should be consistent across gameplay scenes that use `LocalFlickTurnTestRig`.
- Card scoring values should come from `PatternCardData`.
- Match flow should check the 10-point win condition immediately after score gain.
