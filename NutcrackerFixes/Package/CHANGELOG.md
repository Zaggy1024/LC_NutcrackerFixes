## Version 1.1.1
- Removed some leftover debug code.

## Version 1.1.0
- Added an opt-out feature to help ensure that shotguns stay synchronized.
    - When a client picks up a shotgun, the number of shells loaded as well as the current safety setting will be synchronized. This prevents an issue where having too many items on the ship would cause the safety and shell count to be desynced.
    - The safety setting will also be synchronized to all clients when it is changed to ensure that it doesn't remain desynced if anything changes the safety unexpectedly.

## Version 1.0.1
- Disabled logging in the shotgun hitreg loop

## Version 1.0.0
- Fixed stutters on nutcrackers shooting other monsters
- Improved shotgun hit registration to avoid non-registering shots
