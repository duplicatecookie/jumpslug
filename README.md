## Description

This is a mod for Rain World that contains an incomplete implementation of a pathfinding and movement system intended for use with AI slugcats. It does not modify the existing implementation of SlugNPCs provided by MSC.

In its current state the project is not fit for its intended purpose due to the large amount of bugs, mainly in the pathfinding and movement system, and is only shared in the hope that the code may be useful to anyone interested. The last few commits contain an incomplete rewrite of the movement system. The most complete version in terms of features is commit 9db31148242923f52000722ffd7eac35fe4794a7.

This project is currently abandoned and will not receive updates or bug fixes for an indeterminate amount of time. Due to the unfinished nature of the project, there is almost no documentation and the documentation that does exist is likely to be outdated.

## Building

A number of shared libraries (.dll files) are required to build this mod and are not included in the repo due to copyright reasons. To build the mod, copy or symlink the relevant files from the game's Managed directory to a folder named "lib" along with UnityEngine.InputLegacyModule.dll.

## Licenses

This mod is based on a fork of SlimeCubed's [SlugTemplate](https://github.com/SlimeCubed/SlugTemplate), which is is licensed under the CC0-1.0 license. This project as a whole is released under the MIT license.
The text for the relevant licenses can be found in the LICENSE-CC0-1.0 and LICENSE-MIT files respectively
