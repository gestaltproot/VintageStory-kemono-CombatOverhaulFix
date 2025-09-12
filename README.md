# Vintage Story kemono mod Combat Overhaul patch
This is a fork of [xeth's kemono mod](https://gitlab.com/xeth/kemono) which adds compatibility with Combat Overhaul.

## **THIS IS A DROP IN REPLACEMENT**
Download the [latest release](https://github.com/gestaltproot/VintageStory-kemono-CombatOverhaulFix/releases/latest) and add it to your mods folder.

It uses the same mod ID as kemono and is incompatible. If you leave both enabled _only the original_ mod will be loaded, not the patch.

## The Problem
- Combat Overhaul adds new animations for base game weapons, these animations are not remapped onto the kemono skeleton
- The kemono skeleton is custom, likely to allow for the pony content, but the overall structure is compatible with a seraph skeleton
- despite compatible skeletal layout, the bones are not named the same, leading to animations not working
- Combat Overhaul changes how weapon damage is handled to be based on the actual position of the weapon in the first person viewmodel
- because the weapon swing animation never occurs, the weapon can ONLY do damage if the stationary weapon is intersecting an enemy

## What has been done so far
- Rename the bones (torso and arms) for the kemono model to match the vanilla skeleton (copying and building upon work by Coden364)
- Updates some hardcoded strings in the code to match the new bone names
- Moves the kemono project to Visual Studio away from VS code and the weird workflows
- Update the animation data for the legs, because changing the name of the upper body bones inverts their Y-axis rotation?????

## HERE IS WHAT WORKS
- All animations (i think)

## What I need help with
- some animations look a little wacky
	- riding an elk
	- sitting on the ground
	- probably some other animations i have not tested

- oh yeah, the emotes prolly won't work
