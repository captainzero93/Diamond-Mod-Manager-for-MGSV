# DMM (Diamond Mod Manager)

A mod manager for Metal Gear Solid V: The Phantom Pain, written from scratch on how SnakeBite installs mods. It uses the same `.mgsv` mods and they end up the same in game, but installing, removing and reordering mods is much quicker; SnakeBite takes significantly longer.

Needs the Steam version of the game (1.0.15.4), Windows and the [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0) (x64).

## How to use

1. Unzip DMM anywhere outside Program Files and run `DMM.exe`.
2. Put your `.mgsv` mods (or the `.zip` they came in) in the `Mods` folder next to `DMM.exe` and leave them there. Dragging them onto the DMM window does the same: it copies them into the Mods folder.
3. Set up the game when DMM asks, then open the **Mods Folder** tab, add the mods you want to your load order and press **Apply changes**.

Fresh game files are best. If SnakeBite has been used on the game, DMM offers to take it over: it cleans SnakeBite's changes out of the game files, then you add your mods again in DMM. Stick to one manager after that.

When two mods change the same file, the lower one in the load order wins.

## Uninstalling

Settings > Restore game to retail. Then delete the DMM folder and `MGS_TPP\D-Mod`.

## Problems

Logs are in `MGS_TPP\D-Mod\logs`. Send the one from the day it went wrong.

Coming from a D-Mod test build: it's the same manager renamed. Copy your `Mods` and `Presets` folders next to `DMM.exe` and delete the old one.

## Building

`BUILD_DMM.cmd` with the .NET 10 SDK. Output goes to `verified-vNNN\DMM`.

## Credits

SnakeBite and MakeBite by Topher Sheridan, kept going by TinManTex and kapuragu. GzsTool by Atvaark. CityHash by Google. Inter and Atkinson Hyperlegible Next fonts.

MIT licence, see `LICENSE` and `THIRD-PARTY-NOTICES.md`.
