# Test fixtures

Reference output from GzsTool.Core.dll and CityHash.dll as shipped with SnakeBite 0.9.2.5 (Nexus), run under mono, plus SnakeBite's own sort code.

- hashes/gzstool_hashes.tsv: every 40th name from the dictionaries plus edge cases. Columns: name, HashFileName, HashFileName(no extension removal), HashFileNameWithExtension, HashFileNameLegacy, HashFileNameLegacy(no extension removal), SnakeBite Tools.NameToHash. ERR:<type> where GzsTool throws. The full 125217-name run passed before commit; point NEWBITE_HASHES_FULL at a full table to rerun it.
- qar/: source files, list files and GzsTool QarFile.Write output (gzs_plain.dat flags 3150304, gzs_plain_c7.dat flags 3146208, gzs_all.dat flags 3150048 with compressed fpk/fpkd). GzsTool's reader throws on entries under 4 bytes (BitConverter.ToUInt32 on the data), including its own.
- fpk/: SnakeBite SortFpksFiles output, GzsTool FpkFile.Write output, and crafted.fpk with GzsTool FpkFile.Read export in crafted_expected/. crafted_expected/0004 holds the decrypted data, since mono's Path.GetFileName does not split backslash paths and GzsTool under mono derives the wrong key.
