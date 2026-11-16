// Timing on a larger synthetic install (run with DMM_PERF=1)
using System.Diagnostics;
using DMM.Core.Engine;

namespace DMM.Tests;

public static class PerfTests
{
    [Test]
    public static void LargeModsApplyAndToggle()
    {
        if (Environment.GetEnvironmentVariable("DMM_PERF") == null)
            throw new SkipTestException("set DMM_PERF=1 to run");
        using var game = new TestGame();
        var random = new Random(1);
        byte[] Bytes(int size) { var b = new byte[size]; random.NextBytes(b); return b; }

        // a big mod: 1500 files, about 300 MB, packs included; a small mod that shares nothing with it
        var bigFiles = new List<(string, byte[])>();
        for (int i = 0; i < 1200; i++)
            bigFiles.Add(($"/Assets/big/tex/t{i:d4}.ftexs", Bytes(150_000)));
        for (int i = 0; i < 250; i++)
            bigFiles.Add(($"/Assets/big/script/s{i:d4}.lua", Bytes(20_000)));
        for (int i = 0; i < 50; i++)
            bigFiles.Add(($"/Assets/big/pack/p{i:d2}.fpk", TestGame.Pack(false, ($"Assets/big/m{i}.fmdl", (byte)i), ($"Assets/big/g{i}.geom", (byte)(i + 1)))));
        string big = game.Mgsv("big.mgsv", "Big", "1", bigFiles, [("/plugins/big.dll", Bytes(2_000_000))]);
        string small = game.Mgsv("small.mgsv", "Small", "1", [("/Assets/small/x.lua", "x"u8.ToArray())], [("/plugins/small.dll", Bytes(1000))], [42UL]);

        using DmmManager manager = DmmManager.Open(game.GameDir, game.DataDir);
        manager.Setup(keepRetailCopies: false);
        var timer = Stopwatch.StartNew();
        manager.AddMods([big, small]);
        Console.WriteLine($"  add both: {timer.ElapsedMilliseconds} ms");
        timer.Restart();
        ApplyResult first = manager.Apply();
        Console.WriteLine($"  first apply: {timer.ElapsedMilliseconds} ms (00 {first.ZeroWritten}, 01 {first.OneWritten}, chunk0 {first.Chunk0Written})");
        timer.Restart();
        manager.SetEnabled(manager.ResolveId("Small"), false);
        ApplyResult toggle = manager.Apply();
        Console.WriteLine($"  toggle small off: {timer.ElapsedMilliseconds} ms (00 {toggle.ZeroWritten}, 01 {toggle.OneWritten}, chunk0 {toggle.Chunk0Written})");
        timer.Restart();
        manager.SetEnabled(manager.ResolveId("Small"), true);
        manager.Apply();
        Console.WriteLine($"  toggle small on: {timer.ElapsedMilliseconds} ms");
    }
}
