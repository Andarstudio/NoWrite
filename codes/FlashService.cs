using System.Diagnostics;
namespace NoWrite;

public record FlashProgress(double Percent, string Operation, double? SpeedBps, TimeSpan? Eta, string Phase);

public static class FlashService
{
    const int Chunk = 4 * 1024 * 1024; // multiple of 512 / 4096 sector sizes

    /// Hybrid ISOs (most Linux distros) carry an MBR boot signature; Windows ISOs do not.
    public static bool IsHybridIso(string path)
    {
        using var f = File.OpenRead(path);
        var b = new byte[512];
        return f.Read(b, 0, 512) == 512 && b[510] == 0x55 && b[511] == 0xAA;
    }

    public static async Task FlashAsync(string iso, UsbDrive drive, IProgress<FlashProgress> progress, CancellationToken ct)
    {
        await Task.Run(() =>
        {
            progress.Report(new(0, "Preparing drive (clearing partitions)...", null, null, "flash"));
            CleanDisk(drive.Index);

            long len = new FileInfo(iso).Length;
            string dev = $@"\\.\PhysicalDrive{drive.Index}";
            using var src = File.OpenRead(iso);
            using var h = File.OpenHandle(dev, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite, FileOptions.WriteThrough);

            // Pass 1: write (0–50%), Pass 2: verify (50–100%)
            var buf = new byte[Chunk];
            long done = 0; var sw = Stopwatch.StartNew();
            while (done < len)
            {
                ct.ThrowIfCancellationRequested();
                int n = src.Read(buf, 0, Chunk);
                if (n <= 0) break;
                int padded = (n + 4095) / 4096 * 4096; // sector-align final block
                if (padded > n) Array.Clear(buf, n, padded - n);
                RandomAccess.Write(h, buf.AsSpan(0, padded), done);
                done += n;
                Report(progress, done, len, 0, sw, "Writing image", "flash");
            }

            progress.Report(new(50, "Verifying written data...", null, null, "verify"));
            src.Position = 0; done = 0; sw.Restart();
            var back = new byte[Chunk];
            while (done < len)
            {
                ct.ThrowIfCancellationRequested();
                int n = src.Read(buf, 0, Chunk);
                if (n <= 0) break;
                int padded = (n + 4095) / 4096 * 4096;
                int r = RandomAccess.Read(h, back.AsSpan(0, padded), done);
                if (r < n || !buf.AsSpan(0, n).SequenceEqual(back.AsSpan(0, n)))
                    throw new IOException($"Verification failed near {Fmt.Bytes(done)}. The drive may be faulty.");
                done += n;
                Report(progress, done, len, 50, sw, "Verifying", "verify");
            }
            progress.Report(new(100, "Done", null, null, "verify"));
        }, ct);
    }

    static void Report(IProgress<FlashProgress> p, long done, long len, double offset, Stopwatch sw, string op, string phase)
    {
        double frac = (double)done / len, bps = done / Math.Max(sw.Elapsed.TotalSeconds, 0.001);
        TimeSpan? eta = bps > 0 ? TimeSpan.FromSeconds((len - done) / bps) : null;
        p.Report(new(offset + frac * 50, op, bps, eta, phase));
    }

    static void CleanDisk(int index)
    {
        var script = Path.Combine(Path.GetTempPath(), "nowrite_dp.txt");
        File.WriteAllText(script, $"select disk {index}\r\nclean\r\n");
        var psi = new ProcessStartInfo("diskpart", $"/s \"{script}\"")
        { CreateNoWindow = true, RedirectStandardOutput = true, UseShellExecute = false };
        using var p = Process.Start(psi)!;
        var output = p.StandardOutput.ReadToEnd();
        p.WaitForExit();
        File.Delete(script);
        if (p.ExitCode != 0 || !output.Contains("succeeded", StringComparison.OrdinalIgnoreCase))
            throw new IOException("Could not clear the drive with diskpart:\n" + output.Trim());
    }
}
