## Topsail Crossyards

A mod that automatically adds a crossyard (a lower yard) to square topsails placed above fore-and-aft sails.

<img width="1276" height="1184" alt="Screenshot_43" src="https://github.com/user-attachments/assets/af246d23-0865-4db4-b7fe-933a29cd8efc" />

### How it works

- A square sail gets a yard at its foot if there are only fore-and-aft sails (gaff, lateen etc.) below it.
- If there is another square sail below, no yard is added. The sail uses the yard of the sail below, as in the vanilla game.
- If there is nothing below the sail (it is just above the deck), no yard is added.
- Masts and topmasts are treated as one rig, so a topsail on a topmast is checked against the sails on the mast below it.
- The yard copies the look of the sail's own yard. Braces, lift lines and foot ropes are re-rigged to it.
- Wide square sails, spritsails and junk sails are skipped.

### Notes

- The yard itself is only visual. It does not change physics or add any new items.
- Set `Debug → VerboseLog = true` in the config to see why a yard was added or not for each square sail.

### Installation

Requires BepInEx. Put `Topsail Yards.dll` into `BepInEx/plugins`.

### A bit of history

On a real square-rigged ship, the foot of a topsail is held by sheets that run down to the ends of the yard below. On the mizzenmast of a ship or barque, the space under the topsail is taken by a fore-and-aft spanker. Ships kept a lower yard there, the crossjack, which usually carried no sail. It stayed in place to give the topsail something to sheet to. This mod adds the same yard.
