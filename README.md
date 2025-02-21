# SRTimestampManager
Quick standalone utilities for applying timestamp fixes to custom songs, as well as generating the file to apply the fixes locally w/o the Z site.

## SRTimestampFixer
Run this utility to apply timestamp fixes to all song files found in the Synth Riders custom songs directory.

You may optionally pass the path to your SynthRidersUC folder as the first argument, if it is in a custom location.

For example (run from the downloaded and extracted build): `.\SRTimestampFixer.exe 'E:\SteamLibrary\steamapps\common\SynthRiders\SynthRidersUC'`

Default locations that are used if it is not provided:

Windows - `C:\Program Files (x86)\Steam\steamapps\common\SynthRiders\SynthRidersUC`  
Mac - `~/Library/Application Support/Steam/steamapps/common/SynthRiders/SynthRidersUC`  
Linux - `~/.steam/steam/steamapps/common/SynthRiders/SynthRidersUC`

## SRTimestampFileGen
Most folks will never need to run this. It extracts the date modified data for all local song files and creates the JSON file used by SRTimestampFixer.
