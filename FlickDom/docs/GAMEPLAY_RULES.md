# FlickDom Gameplay Rules

Last updated: August 5, 2026

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

- A match has 3 stages, and stages do not have a difficulty.
- The full 9-card pool is shuffled once at the start of a match.
- The shuffled cards are dealt without duplicates: 3 random cards per stage.
- Card difficulty belongs to each card and does not depend on the current stage.

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
