# OMODFramework

[![Build Status](https://dev.azure.com/erri120/OMODFramework/_apis/build/status/erri120.OMODFramework?branchName=master)](https://dev.azure.com/erri120/OMODFramework/_build/latest?definitionId=3&branchName=master)
[![Nuget](https://img.shields.io/nuget/v/OMODFramework)](https://www.nuget.org/packages/OMODFramework/)

This project is the continuation and overhaul of my previous `OMOD-Framework`. Aside from the fact that I remove the `-` from the name, this project will be more refined than the last one. I've implemented more features from the [Oblivion Mod Manager](https://www.nexusmods.com/oblivion/mods/2097) and finally use continuous integration with Azure DevOps to build, test and release this project. **This is not a MO2 plugin**.

## Features

- Extraction
- Creation
- Script Execution

## OMOD

`.omod` files are used exclusively by the [Oblivion Mod Manager](https://www.nexusmods.com/oblivion/mods/2097) aka `OBMM`. This was fine 11 years ago. Today the Oblivion modding community still stands strong and continues to mod their favorite game. There are sadly some huge and essential mods still in the OMOD format. [Mod Organizer 2](https://github.com/Modorganizer2/modorganizer) has [recently](https://github.com/ModOrganizer2/modorganizer/releases/tag/v2.2.0) added more support for [running Oblivion OBSE with MO2](https://github.com/ModOrganizer2/modorganizer/wiki/Running-Oblivion-OBSE-with-MO2) and made me wanna mod Oblivion again, only to find out that you still need OBMM for some stuff.

The source code for the original OBMM, written in .NET 2 ... yes _.NET 2_, was made available in 2010 under the _GPLv2_ license.

This Framework uses a lot of the original algorithms for extraction, compression and of course all functions needed for script executing.

## Download

- `OMODFramework`: [NuGet](https://www.nuget.org/packages/OMODFramework/), [GitHub Packages](https://github.com/erri120/OMODFramework/packages/63159), [GitHub Release](https://github.com/erri120/OMODFramework/releases)
- `OMODFramework.Scripting`: [NuGet](https://www.nuget.org/packages/OMODFramework.Scripting/), [GitHub Release](https://github.com/erri120/OMODFramework/releases)

## Usage

Check the [Wiki](https://github.com/erri120/OMODFramework/wiki) here on GitHub.

## License

```text
Copyright (C) 2019-2020  erri120

This program is free software: you can redistribute it and/or modify
it under the terms of the GNU General Public License as published by
the Free Software Foundation, either version 3 of the License, or
(at your option) any later version.

This program is distributed in the hope that it will be useful,
but WITHOUT ANY WARRANTY; without even the implied warranty of
MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
GNU General Public License for more details.

You should have received a copy of the GNU General Public License
along with this program.  If not, see <https://www.gnu.org/licenses/>.
```
