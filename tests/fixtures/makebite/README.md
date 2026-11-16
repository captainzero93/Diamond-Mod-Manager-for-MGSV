# MakeBite reference build

`mod/` is a small mod folder covering:
- a `_fpk` folder, including a file with the wrong type for the pack;
- a `_fpkd` folder;
- a prebuilt `.fpkd` (written with GzsTool);
- an `Assets` lua and a non-Assets lua;
- a custom `.wmv`;
- GameDir files, including `steam_api64.dll`, which MakeBite ignores;
- `readme.txt` and a `.docx`, which are left out.

`expected/` is the content of the `.mgsv` that MakeBite's `Build.BuildArchive` (SnakeBite-master-kap, makebite/Classes/MakeBite.cs) produced. It was run under mono with the folder dialog's prebuilt-pack extraction (`formMain.PopulateBoxes`), with these details:
- name `Test Mod`, version `1.2`, author `choom`, website `https://example.com/mod`;
- description from `description.txt`.

Harness changes needed to run on Linux:
- path separators;
- a GzsTool directory that accepts `/`;
- the zip step replaced by copying the `_build` folder.

The mono run differs from a Windows MakeBite run in three ways:
- its `metadata.xml` uses LF line ends;
- `SBVersion` is `0.0.0.0`, because there is no `Application.ProductVersion`;
- entries are listed in directory enumeration order.

The test compares file bytes and the metadata entries as sets.
