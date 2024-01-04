# NutcrackerFixes

## Overview
Lethal Company mod that patches the Nutcracker and Shotgun item to reduce the occurrence of lag spikes and non-registering shots. 

## Specific Changes
- Patched the Shotgun hit registration to make it more consistent.
    - The hit registration would bail out if some conditions for dealing damage failed. If the shot was obstructed, the shot's sphere cast started inside an enemy's hitbox, or if it hit a non-enemy collider, then all other hits after that would be skipped, causing some non-registering shots. With this mod, the subsequent hits will still be processed.
    - The array used to get the hits was limited to 10 elements, but blobs can cause many more colliders to be present, meaning that shooting nearby to a blob could cause a non-registering hit. With this mod, the size of that array is increased to 50, which appears to work even in cases where multiple blobs are present.
    - To avoid unnecessary work when shooting nearby to blobs or any other enemies with multiple colliders, the hit registration will ensure that it only applies a hit to an enemy once. This should have no effect on damage dealt to enemies that actually receive damage from the Shotgun.
- Removed some debug logging from the Nutcracker which would be spammed constantly from the Update() function.
- Prevented NullReferenceExceptions when a Nutcrackers shoot Blob or Spiders. The both AI scripts assumed that only players could hit them, which would cause a long stutter when a Nutcracker hits either enemy.
