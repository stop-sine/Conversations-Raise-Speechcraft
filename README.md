# ConversationsRaiseSpeechcraft

A patcher for Skyrim: Special Edition to implement Conversations Raise Speechcraft using Synthesis

## Description
This patcher uses Mutagen and Synthesis to dynamically implement [AndrealletiusVIII](https://www.nexusmods.com/users/5646623)'s [Conversations Raise Speechcraft](https://www.nexusmods.com/skyrimspecialedition/mods/93435)﻿. This dramatically expands the amount of dialog that award Speechcraft experience while also enabling compatibility for new dialog added by other plugins. The original mod manually patches ~700 Dialog Topic records while the patcher automatically patches >4,000 records in a standard Anniversary Edition load order.

More information about the Speech experience script itself can be found on the Nexus page for [Conversations Raise Speechcraft](https://www.nexusmods.com/skyrimspecialedition/mods/93435).

The patcher filters out certain dialog options that realistically should not award Speech experience. This includes records that do not contain player dialog. Records that contain the following strings in their Editor ID are filtered: "Decorate," "Generic," "Shout," and "Cast." Likewise, records containing certain strings in their Name field are filtered: "(Invisible Continue)," "(forcegreet)," and "(remain silent)." In future, I will look into making the filters configurable within Synthesis.

## Requirements
- **[Synthesis](http://github.com/Mutagen-Modding/Synthesis/releases)**
- **[Conversations Raise Speechcraft﻿](https://www.nexusmods.com/skyrimspecialedition/mods/93435) (scripts only)**

## Installation & Usage
This patcher requires the use of Synthesis. A guide to installation and applying patchers can be found in the Synthesis [documentation](https://mutagen-modding.github.io/Synthesis/). This repository and the patcher's Nexus mirror contain a .synth file that will load the project's metadata into your Synthesis client. Alternatively, the patcher is also available within Synthesis via either the link to this repository or the patcher registry.

**Please do not ask me for help using Synthesis.** There are plenty of guides on Nexus, GitHub, and Mutagen's Discord.

To use this patcher, **the scripts included in [Conversations Raise Speechcraft](https://www.nexusmods.com/skyrimspecialedition/mods/93435) must be installed** on your Skyrim: Special Edition client. To install the scripts, download and install the original mod as normal using a mod manager (MO2/Vortex) or manually. Then, deactivate or remove the plugin `Conversations Raise Speechcraft.esp` from your load order. Please note that while the scripts are not required to apply the patcher to your load order in Synthesis, the resulting plugin will not function in game without the scripts.

## Compatibility
By leveraging Synthesis, this patcher is nearly weightless and (nominally) compatible with any load order. If you experience errors executing the patcher, please report them so I can try to resolve the issue. I have successfully tested the patcher on my own load order without encountering any issues. 

## Credits
- **[AndrealletiusVIII](https://www.nexusmods.com/users/5646623) for [Conversations Raise Speechcraft](https://www.nexusmods.com/skyrimspecialedition/mods/93435)﻿**
- **[Noggog](https://github.com/Noggog), et al. for [Mutagen](https://github.com/Mutagen-Modding/Mutagen) and [Synthesis](https://github.com/Mutagen-Modding/Synthesis)**

## Mirrors
- **[Nexus](https://www.nexusmods.com/skyrimspecialedition/mods/160105)**

