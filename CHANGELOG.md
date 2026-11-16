# Changelog

## 0.9.86, 15/11/2026
- New setting "Show file names" (Settings > Mods): lists mods by file name instead of the author's mod name, and sorts by it, on the Load Order and Mods Folder tabs. Search finds file names on both tabs.
- Mods added from a .zip before 0.9.82 no longer keep their copy in D-Mod: while the .zip is still in the Mods folder, the .mgsv is taken out into the Mods folder and the copy is deleted. Done once on start.

## 0.9.85, 23/10/2026
- The status line shows what an apply is doing ("Applying mods: Building 00.dat (412 files)...").
- Load Order filter: All, On or Off, to show only the mods switched on or off.
- The Mods Folder tab no longer lists a .zip as a second copy of a mod once its .mgsv has been taken out next to it.

## 0.9.84, 19/10/2026
- Fixed DMM closing straight away on start on some PCs ("Thread exception mode cannot be changed once any Controls are created"). The crash handler is now set up first, and if Windows refuses it DMM still starts.

## 0.9.83, 17/10/2026
- The Files and Conflicts lists show their column names, with a fine line between columns.
- Warnings about files a mod packs that the game doesn't read now say they are ignored and harmless.

## 0.9.82, 15/10/2026
- Adding a mod from a .zip takes its .mgsv out into the Mods folder and reads it from there, so DMM no longer keeps its own copy in D-Mod. The log says when the .zip can be deleted. Mods added from a .zip before keep their copy.
- After changing the Mods folder, mods are looked for in the new one straight away.

## 0.9.81, 15/10/2026
- Switching theme or font keeps what you typed on the Build Mod page.
- Adding a mod from a .zip logs a tip: DMM keeps a copy of its .mgsv in the D-Mod folder, so taking the .mgsv out of the .zip first saves the space.

## 0.9.80, 15/10/2026
- A mod read from outside your Mods folder (an older DMM folder, say) is marked "Outside Mods folder", with a tip to copy it in.
- Once the same file is in your Mods folder, DMM reads it from there instead.

## 0.9.79, 15/10/2026
- On the Activity page, the line that finishes a successful apply, setup, take over or restore has a green tag.

## 0.9.78, 15/10/2026
- Sidebar: the big DMM title is gone, Diamond Mod Manager sits above the logo.
- Mod details show the website's name (nexusmods.com) instead of a long address wrapped mid-word. Clicking it still opens the full page.
- Shorter Load Order hint, so it stays on one line.

## 0.9.77, 15/10/2026
- Mod list text is drawn as before 0.9.76 again (the off-screen drawing made it look rough). Hovering still skips the background clear, which was most of the flicker.

## 0.9.76, 15/10/2026
- Less flicker in the mod lists when hovering and clicking: each row is drawn off screen and shown in one go.

## 0.9.75, 15/10/2026
- Fixed a stray line in the load order after switching between Load Order and Mods Folder. The lists now skip redrawing when nothing in them changed, instead of skipping Windows' background clear.

## 0.9.74, 15/10/2026
- Conflict warnings, the Conflicts tab and mod details talk about the mod and the game files, not pack files.

## 0.9.73, 15/10/2026
- Dialogs with a list (Mod files needed, Take over, Shared files and the rest) open tall enough to show the list. A long intro could squeeze it to nothing until the window was resized.

## 0.9.72, 15/10/2026
- Plain-English pass over the messages, dialogs, tips and log warnings.
- The status line only shows what DMM did, not raw log lines, and fades out after 10 seconds. Settings > Appearance can keep it.
- Warnings count up on the Activity entry until you open it. The Activity page shows each line with its time and a coloured tag.
- A load order mod whose .mgsv has gone from the Mods folder is marked File missing, with one log line instead of one per pack.
- Steadier window: the header buttons stay put when "Changes not applied" comes and goes, and the details pane keeps its scroll position.

## 0.9.71, 15/10/2026
- Launch MGSV turns into Close MGSV while the game is running. It asks the game to close, and offers to end mgsvtpp.exe if it is stuck in the background.

## 0.9.70, 15/10/2026
- Moving DMM or its Mods folder (or rebuilding into a new folder) no longer loses track of the mods: each .mgsv is found under the same name in the current Mods folder. Its size and MD5 are still checked before use.
- Selecting a mod on the Mods Folder tab no longer logs a "candidate unreadable" warning when it shares a pack with a load order mod.

## 0.9.69, 15/10/2026
- Adding mods from the Mods Folder tab stays on that tab, so you can add several in a row. The status line says what was added.
- Load order rows show "Update available" when the newest file of that mod in the Mods folder has another version. A "replaced" warning still takes priority.
- Reapply shows for every enabled mod. For a mod with no game folder files it explains there is nothing to put back.
- If MGSV is still closing, DMM waits up to 15 seconds for mgsvtpp.exe to exit before saying it is running, and says how to end it.
- Clearer wording when removing a mod.

## 0.9.68, 11/10/2026
- Renamed to DMM (Diamond Mod Manager): DMM.exe, window, menus and messages. The data folder in the game folder stays MGS_TPP\D-Mod and settings stay in %LocalAppData%\NewBite, so existing installs carry on. Copy the Mods and Presets folders from an old D-Mod folder next to DMM.exe.

## 0.9.67, 09/10/2026
- Restore to retail empties D-Mod's folder in MGS_TPP except the logs (library entries, staging, emptied backup folders, the old state and snakebite.xml records) and removes the retail copies (*.original), which match the game files again and are made again at the next setup. The load order is saved as a preset first. dmod.lock is removed when D-Mod closes.
- The move arrows no longer go grey when a search is typed; they explain why mods can't move instead.

## 0.9.66, 06/10/2026
- Presets: a save button beside delete puts the current load order into the selected preset, keeping its name.

## 0.9.65, 06/10/2026
- The move up/down buttons (and Ctrl+Up/Down) work in a sorted or searched load order too: they say why mods cannot move there and offer to show the load order. The sort is remembered between sessions, so a list once sorted by name looked like moving was broken.

## 0.9.64, 06/10/2026
- Add all on the Mods Folder page adds every listed mod that is not in the load order (search narrows it), in the order shown. With two files of one mod, the newest is used.

## 0.9.63, 05/10/2026
- On a Steam install, the broken game files bar offers Open Steam verify again next to Restore from retail copies.

## 0.9.62, 05/10/2026
- A .mgsv with other data in front of the zip (for example a .rar holding the .mgsv, renamed to .mgsv) is read, as SnakeBite does. Before, it was refused with "Number of entries expected in End Of Central Directory...".

## 0.9.61, 05/10/2026
- Reapply in mod details writes the mod's game folder files again, as they are in the mod, over edits made to them. A plain apply keeps edited files, so switching a mod off and on again does not count as a change.

## 0.9.60, 05/10/2026
- When Steam has replaced the game files, "Stay unmodded" removes what D-Mod left behind (its game folder files, with your own files put back, a_chunk7/a_texture7 and its records). Finish restore does the same instead of refusing.
- When the game files are broken (a_chunk7/a_texture7 or the baseline deleted) and D-Mod holds retail copies that check out, "Restore from retail copies" puts the game back without a Steam download.
- Both save the load order as a preset first.

## 0.9.59, 05/10/2026
- Audit fixes: deleting a D-Mod folder never touches what a junction or link inside it points to; Remove mod and Cancel restore go back to the saved load order if saving fails; after a cancelled Windows shutdown D-Mod keeps its log, folder watcher and game folder lock; take-over wording for an unreadable snakebite.xml and for files SnakeBite's uninstall left behind.

## 0.9.58, 05/10/2026
- Take over says up front when 00.dat/01.dat hold files no recorded mod explains (mods installed by hand, or a deleted D-Mod folder), and that they are removed.

## 0.9.57, 04/10/2026
- Fixed lockout: folders holding read-only files (D-Mod\baseline, library copies) could not be deleted, so Finish restore stopped at "Access to the path '00.dat' is denied" on every start. Read-only flags are now cleared before any folder is deleted; a stuck restore finishes on the next start.

## 0.9.56, 04/10/2026
- Final audit tidy-ups: removing a mod no longer reports "Not changed" when only its library folder was locked; interrupted adds leave no folders behind; files dropped while D-Mod is working get a message; a mods folder file is never left open after a read error; a stale click could select the wrong row after Alt+Tab mid-click.

## 0.9.55, 04/10/2026
- A copy into the mods folder that fails part way leaves no .part file behind.

## 0.9.54, 04/10/2026
- Files dropped or added are copied into the mods folder under a temporary name and renamed when complete, so the folder scan never reads one half-copied ("used by another process").

## 0.9.53, 04/10/2026
- A mods folder file another program has open (a download or copy still writing, antivirus) shows as "In use" and is tried again every few seconds, instead of staying "not listed" until it changes.

## 0.9.52, 04/10/2026
- Fixed: a nameless .mgsv at the top of a .zip was named differently in the Mods Folder and the load order.
- Ctrl/Shift+click on a switch changes only that mod; a double-click on a selected switch keeps the selection.
- If saving the load order fails, the list goes back to what is saved instead of showing the unsaved change.

## 0.9.51, 04/10/2026
- With several mods selected, their switch, Space or the Enable/Disable button in the details switches all of them.

## 0.9.50, 04/10/2026
- Mods Folder count says how many are not in the load order, share a name with another file, or are not mods, so it adds up against the load order.

## 0.9.49, 04/10/2026
- Fixed lockout: when state.json (or another D-Mod file) could not be replaced, the unfinished apply failed on every start with "Access to the path is denied". Read-only files are now cleared and a replace is retried for a few seconds; if it still fails, the message names the file.

## 0.9.48, 04/10/2026
- Mods whose metadata.xml has no name are no longer refused; they are named after their file, as SnakeBite installs them.

## 0.9.47, 04/10/2026
- An apply refused because MGSV was started meanwhile now deletes the archives it had built, instead of leaving them in the game folder.

## 0.9.46, 04/10/2026
- Fixed (since 0.9.43): on Windows set to a Unicode-only language (Hindi, Georgian, ...) no mod could be read.
- Setup refuses when 00.dat or 01.dat differs from retail copies that check out, so a half-finished verify cannot overwrite them.
- metadata.xml that only reads correctly in its declared encoding (Latin-1, UTF-16 without BOM) is read that way again.
- Library switch-over on start only uses a Mods folder file identical to the copy in the load order, never a different build of the same version; it waits while a dialog is open.
- Mod files are checked against their recorded MD5 when read, so a package changed behind D-Mod's back is refused instead of written.
- A Mods folder file updated with a pack removed no longer breaks the conflict view.
- Ctrl+Up/Down with gaps in the selection moves each mod one step; closing during a background task waits; a leftover old library copy no longer stops the game folder opening.
- Smaller: duplicate preset entries ignored; removing a file whose path became a folder no longer blocks apply.

## 0.9.45, 04/10/2026
- The library no longer copies mods: files are read from the .mgsv in the mods folder when applying (a .mgsv inside a .zip is kept once, packed). Existing extracted copies switch over on start when their .mgsv is in the mods folder, freeing the space.
- A mod whose .mgsv is moved, deleted or changed after adding is caught before apply; D-Mod asks for the file, or it can be switched off or removed.

## 0.9.44, 04/10/2026
- Fixed: the Mods Folder sort buttons were cut off after "Load Order".

## 0.9.43, 04/10/2026
- Fixed: mods whose zip stores non-ASCII file names the way MakeBite writes them (no UTF-8 flag) were refused; names are now read as SnakeBite reads them.

## 0.9.42, 04/10/2026
- Adding mods never applies unless you click Apply changes. The Settings switch "Apply dropped mods right away" is off for everyone (an older saved setting is ignored) and never applies other unapplied edits.
- "All on" box replaced by an "Enable all" / "Disable all" button, hidden while a search or sort shows part of the list.
- Fixed: Setup after a half-finished Steam verify could overwrite the good retail copies or copy D-Mod's 01.dat as the textures; it now checks first and refuses.
- Fixed: Finish restore now also refuses while 01.dat is still the modded one.
- Fixed: an interrupted restore could switch every mod off in the saved load order.
- metadata.xml is read as SnakeBite reads it, so a wrong encoding declaration no longer refuses a mod.
- An unwritable config.json no longer stops D-Mod closing or opening the game folder.
- Ctrl+Up/Down moves all selected mods; Up/Down buttons update after a search; actions that would do nothing during a restore or build are disabled.
- Smaller fixes: a bad preset file no longer hides all presets; a locked library folder no longer reports a finished apply as failed; Build Mod skips file names the game cannot hash; Open Steam verify closes without the unapplied-changes question.

## 0.9.41, 04/10/2026
- Fixed lockout: after Restore to retail, before Steam verify, nothing could be done. A restore can now be cancelled (info bar and Settings); applying puts the mods back.
- Fixed: Finish restore could run while 00.dat was still the modded one, and Setup could then build a broken game; both now refuse.
- Lockouts also fixed: unreadable snakebite.xml blocked Take over; a failed apply after Take over left Apply disabled; missing `D-Mod\baseline` now says so and offers Steam verify; a game folder that cannot be opened or read shows why, with Retry.
- Mods SnakeBite accepts are accepted: a .mgsv renamed to .zip; metadata listing files SnakeBite skips; QAR files stored under their dictionary name. Only files metadata.xml installs are copied to the library, so junk entries no longer refuse a mod.
- Pack entries of a type the pack cannot hold are dropped as SnakeBite does.
- Refused mods and add problems are written to the log.
- "All on" box above the load order switches every mod on or off.

## 0.9.40, 04/10/2026
- Library keeps only each mod's extracted files; the packed `mod.mgsv` copy is no longer stored, and old ones are deleted on start.
- Adding a file whose mod is already in the load order asks before replacing it.
- Installing dropped mods straight away is off by default.
- Sorts: click the chosen sort again to flip its direction (arrow shown). New Date sort (the .mgsv or .zip file date) on both lists; the date is shown in the Overview.
- Several selected mods can be dragged together.
- Minimum window width 1120 so the list toolbar fits.

## 0.9.39, 03/10/2026
- Fixed: restoring an original game folder file whose folder had been emptied and removed left a journal that failed on every start.
- Fixed: read-only source files (01.dat, SnakeBite `.original` copies) made setup, retail copies and restore fail; read-only files now also delete cleanly.
- Fixed: a QAR path with no extension crashed the mod views; it is skipped with a warning.
- Fixed: an interrupted library replace left a mod's copy under `.replaced`; it is put back on start.
- Fixed: Open Steam verify could fail to close D-Mod; the restore check now runs only when D-Mod regains focus from another program.
- Fixed: no "changes not applied" prompts while a restore waits for Steam.
- Search clears when a mod is added; the details pane is rebuilt only when the search changes the selection.

## 0.9.x, 03/10/2026
- Renamed to D-Mod; data folder `D-Mod\` (an old `NewBite\` folder is moved). Logo, exe icon.
- Fonts: Inter by default, Atkinson Hyperlegible Next as a readability option. Light and dark themes.
- Search box, copy load order, copy diagnostics, Launch MGSV, middle-button drag scroll, multi-select removal.
- Show Conflicts filter and Conflicts tab; Markdown and BBCode descriptions.
- `.zip` downloads must contain a `.mgsv`. Hostile mod files (zip bombs, bad paths, device names, slow descriptions) are refused.
- Crash safety: journal fixes, flushed copies, game-running check before committing, a message on start after an interrupted session.
- Restore through Steam verify closes D-Mod while Steam runs and finishes by itself when the retail files are back.
- Edited mod files in the game folder are no longer moved to `gamedir-changed`.
- Command-line tool removed. MIT licence.

## 0.8.x
- Code review and UI wording passes.

## 0.7.x
- Show Conflicts, Build Mod Files list, closing blocked while working.

## 0.6.0
- Build Mod page (MakeBite's job).

## 0.5.x
- Presets.

## 0.3.x to 0.4.x
- New window layout, themes, dialogs, Steam release as the target, single-file exe.

## 0.2.0
- SnakeBite installs are taken over to a clean prepared state.

## 0.1.0
- First build.
