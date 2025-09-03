# Vintage Story kemono mod Combat Overhaul patch
This is a fork of [xeth's kemono mod](https://gitlab.com/xeth/kemono) which adds compatibility with Combat Overhaul.

## The Problem
- Combat Overhaul adds new animations for base game weapons, these animations are not remapped onto the kemono skeleton
- The kemono skeleton is custom, likely to allow for the pony content, but the overall structure is compatible with a seraph skeleton
- despite compatible skeletal layout, the bones are not named the same, leading to animations not working
- Combat Overhaul changes how weapon damage is handled to be based on the actual position of the weapon in the first person viewmodel
- because the weapon swing animation never occurs, the weapon can ONLY do damage if the stationary weapon is intersecting an enemy

## What has been done so far
- Rename the bones for the kemono model to match the vanilla skeleton (copying and building upon work by Coden364)
- Updates some hardcoded strings in the code to match the new bone names
- Moves the kemono project to Visual Studio away from VS code and the weird workflows

## HERE IS WHAT WORKS
- First person Combat Overhaul animations
- Third person walking animations

## What I need help with
- Many vanilla animations aren't working correctly, and need to be fixed
- Third person walking animation looks ridiculous, and upper body is too stiff
- kemono emotes are borked
- some clothes don't attach in the right spot
- sitting animation is borked

I have absolutely no idea how to fix the animations, and want help working on this.