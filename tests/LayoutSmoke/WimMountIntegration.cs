using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.ExceptionServices;
using System.Security.Cryptography;
using System.Threading;
using DISM_GUI;

// Creates one disposable, non-OS WIM with DISM, mounts it READ ONLY,
// checks WIMGAPI mount state and dismounts without committing image changes.
// This proves the mount/metadata integration, not Windows servicing APIs.
internal static class WimMountIntegration
{
    private static (int ExitCode, string Output) RunDism(string[] args, int timeoutMs)
    {
        var start = new ProcessStartInfo("dism.exe")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (var arg in args) start.ArgumentList.Add(arg);
        using var process = new Process { StartInfo = start };
        if (!process.Start())
            throw new InvalidOperationException("Unable to start Windows DISM.");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(timeoutMs))
        {
            process.Kill(entireProcessTree: true);
            process.WaitForExit(10000);
            throw new TimeoutException("Windows DISM timed out: " + string.Join(" ", args));
        }
        var output = stdout.GetAwaiter().GetResult() + "\n" +
                     stderr.GetAwaiter().GetResult();
        return (process.ExitCode, output);
    }

    private static void CheckDism(string[] args, int timeoutMs)
    {
        var result = RunDism(args, timeoutMs);
        if (result.ExitCode != 0)
            throw new InvalidOperationException("DISM returned " + result.ExitCode +
                " for " + string.Join(" ", args) + ":\n" +
                result.Output[^Math.Min(result.Output.Length, 2200)..]);
    }

    private static string Digest(string image)
    {
        using var stream = File.Open(image, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    public static void VerifySplit(string image, string root)
    {
        if (Environment.GetEnvironmentVariable("DISMGUI_TEST_WIM_MOUNT") != "1")
        {
            Console.WriteLine("SKIP real split SWM WIM integration test (opt-in)");
            return;
        }
        var output = Path.Combine(root, "split-image");
        Directory.CreateDirectory(output);
        var first = Path.Combine(output, "tiny.swm");
        var sourceHash = Digest(image);
        CheckDism(new[] {
            "/English", "/Split-Image", "/ImageFile:" + Path.GetFullPath(image),
            "/SWMFile:" + first, "/FileSize:3"
        }, 180000);

        var manifest = Wim360SplitSet.Discover(first);
        if (manifest.PartCount < 2 ||
            manifest.Parts.Distinct(StringComparer.OrdinalIgnoreCase).Count() != manifest.PartCount)
            throw new InvalidOperationException("DISM did not generate a multi-part SWM set.");
        var before = manifest.Parts.Select(Digest).ToArray();
        var inspected = Wim360Reader.Inspect(first);
        if (!inspected.IsSplitSet || inspected.SplitSet.PartCount != manifest.PartCount ||
            inspected.ImageCount != 1 || inspected.Images[0].Name != "WIM360 CI" ||
            inspected.Images[0].MountStatus != "Split image (read-only)")
            throw new InvalidOperationException("WIMGAPI could not read a real split SWM catalog.");
        var secondPart = manifest.Parts[1];
        var bySecond = Wim360Reader.Inspect(secondPart);
        if (!bySecond.IsSplitSet ||
            !string.Equals(bySecond.ImageFile, first, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("SWM part #2 did not resolve the first part.");

        var hidden = secondPart + ".missing";
        File.Move(secondPart, hidden);
        try
        {
            bool rejected = false;
            try { Wim360SplitSet.Discover(first); }
            catch (FileNotFoundException) { rejected = true; }
            if (!rejected)
                throw new InvalidOperationException("A missing SWM part was not detected.");
        }
        finally
        {
            File.Move(hidden, secondPart);
        }
        if (!before.SequenceEqual(manifest.Parts.Select(Digest)) ||
            sourceHash != Digest(image))
            throw new InvalidOperationException("SWM inspection modified a split or source image.");
        Console.WriteLine("PASS real DISM SWM split: metadata, all parts, part #2, " +
                          "missing part rejected and SHA-256 unchanged.");
    }

    // Diagnostic probe: determine the exact output of Windows' supported
    // native file listing API before enabling an unmounted multi-index parser.
    // Uses the disposable CI fixture only. Never modifies or mounts the image.
    public static void ProbeOfflineList(string image)
    {
        if (Environment.GetEnvironmentVariable("DISMGUI_TEST_WIM_MOUNT") != "1")
            return;
        var result = RunDism(new[] {
            "/English", "/List-Image", "/ImageFile:" + Path.GetFullPath(image),
            "/Index:1"
        }, 180000);
        Console.WriteLine("OFFLINE DISM /List-Image probe exit=" + result.ExitCode +
            " output:\n" + result.Output[..Math.Min(3500, result.Output.Length)]);

        var psi = new ProcessStartInfo("powershell.exe") {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        var literal = Path.GetFullPath(image).Replace("'", "''");
        psi.ArgumentList.Add("-NoProfile");
        psi.ArgumentList.Add("-NonInteractive");
        psi.ArgumentList.Add("-Command");
        psi.ArgumentList.Add("$ErrorActionPreference='Stop'; Import-Module Dism; " +
            "$items=@(Get-WindowsImageContent -ImagePath '" + literal +
            "' -Index 1); Write-Output ('COUNT=' + $items.Count); " +
            "$items | ConvertTo-Json -Compress -Depth 3");
        using var child = new Process { StartInfo = psi };
        if (child.Start()) {
            var output = child.StandardOutput.ReadToEndAsync();
            var error = child.StandardError.ReadToEndAsync();
            if (!child.WaitForExit(180000)) {
                child.Kill(entireProcessTree: true);
                child.WaitForExit(10000);
            }
            var combined = output.GetAwaiter().GetResult() +
                "\n" + error.GetAwaiter().GetResult();
            Console.WriteLine("OFFLINE Get-WindowsImageContent probe exit=" +
                child.ExitCode + " output:\n" + combined[..Math.Min(3500, combined.Length)]);
        }
    }

    // Real 3-index WIM regression: DISM enumerates contents without mounting.
    // All sources are temporary files created only by the CI test itself.
    // CI-only stress fixture. This is a >1 GiB synthetic WIM, not an actual
    // Microsoft install.wim. All three indexes are captured with Windows DISM.
    // No real OS images, partitions or mounted production folders are touched.
    public static void VerifyLargeThreeIndexWim(string root)
    {
        if (Environment.GetEnvironmentVariable("DISMGUI_TEST_LARGE_WIM") != "1")
        {
            Console.WriteLine("SKIP large synthetic 3-index WIM stress test (opt-in)");
            return;
        }

        var timer = Stopwatch.StartNew();
        var folder = Path.Combine(root, "large-install-fixture");
        Directory.CreateDirectory(folder);
        var wim = Path.Combine(folder, "install.wim");
        var input = Path.Combine(folder, "index-1");
        Directory.CreateDirectory(Path.Combine(input, "Windows", "System32"));
        var payload = Path.Combine(input, "Windows", "System32", "large-resource.bin");

        // 1,152 MiB. Fixed-size buffers avoid allocating gigabytes of RAM.
        // /Compress:none produces a genuine large WIM rather than a tiny
        // highly-compressed placeholder full of zeros.
        var block = new byte[4 * 1024 * 1024];
        new Random(360224).NextBytes(block);
        using (var writer = new FileStream(payload, FileMode.CreateNew,
                                           FileAccess.Write, FileShare.None, block.Length))
        {
            for (var n = 0; n < 288; n++)
                writer.Write(block);
            writer.Flush(flushToDisk: true);
        }
        File.WriteAllText(Path.Combine(input, "Windows", "System32", "shared.txt"),
                          "large-WIM stress index 1");

        CheckDism(new[] {
            "/English", "/Capture-Image", "/ImageFile:" + wim,
            "/CaptureDir:" + input, "/Name:Windows 11 Pro",
            "/Compress:none", "/CheckIntegrity"
        }, 900000);

        for (var index = 2; index <= 3; index++)
        {
            var source = Path.Combine(folder, "index-" + index);
            Directory.CreateDirectory(Path.Combine(source, "Windows", "System32"));
            File.WriteAllText(Path.Combine(source, "Windows", "System32", "shared.txt"),
                              "large-WIM stress index " + index);
            File.WriteAllText(Path.Combine(source, "Windows", "System32",
                                            "only-index-" + index + ".txt"), "unique");
            CheckDism(new[] {
                "/English", "/Append-Image", "/ImageFile:" + wim,
                "/CaptureDir:" + source, "/Name:Windows 11 Index " + index
            }, 300000);
        }

        var report = Wim360Reader.Inspect(wim);
        var totalBytes = new FileInfo(wim).Length;
        if (report.ImageCount != 3 || totalBytes < 1024L * 1024 * 1024)
            throw new InvalidOperationException(
                "Stress fixture did not create a >1 GiB three-index WIM. " +
                "Size: " + totalBytes.ToString("N0") + " bytes.");
        var digest = Digest(wim);
        var modified = File.GetLastWriteTimeUtc(wim);
        var result = Wim360OfflineList.Compare(report, new[] { 1, 2, 3 },
                                                CancellationToken.None);
        if (!result.Rows.Any(x => x.Name.EndsWith("large-resource.bin",
                              StringComparison.OrdinalIgnoreCase) &&
                            x.State == "Only in Index 1") ||
            !result.Rows.Any(x => x.Name.EndsWith("only-index-3.txt",
                              StringComparison.OrdinalIgnoreCase) &&
                            x.State == "Only in Index 3") ||
            digest != Digest(wim) ||
            modified != File.GetLastWriteTimeUtc(wim))
            throw new InvalidOperationException(
                "Large real-DISM-created three-index WIM comparison or preservation failed.");

        timer.Stop();
        var memory = Process.GetCurrentProcess().PeakWorkingSet64;
        Console.WriteLine("PASS 1+ GiB synthetic install.wim stress: " +
            totalBytes.ToString("N0") + " bytes, 3 indexes, " +
            result.Rows.Count.ToString("N0") + " compared paths, " +
            "elapsed=" + timer.Elapsed.TotalSeconds.ToString("F1") + " s, " +
            "peak process RAM=" + (memory / 1048576L).ToString("N0") +
            " MiB; SHA-256 and timestamp unchanged.");
    }

    public static void VerifyOfflineThreeIndexes(string root)
    {
        if (Environment.GetEnvironmentVariable("DISMGUI_TEST_WIM_MOUNT") != "1")
            return;
        var wim = Path.Combine(root, "offline-3-index.wim");
        for (var index = 1; index <= 3; index++)
        {
            var src = Path.Combine(root, "offline-source-" + index);
            Directory.CreateDirectory(Path.Combine(src, "Windows", "System32"));
            Directory.CreateDirectory(Path.Combine(src, "Documents", "Shared"));
            Directory.CreateDirectory(Path.Combine(src, "Documents", "מסמכים"));
            File.WriteAllText(Path.Combine(src, "Windows", "System32", "common.txt"), "same");
            File.WriteAllText(Path.Combine(src, "Windows", "System32", "Тест-драйвер.txt"), "unicode");
            File.WriteAllText(Path.Combine(src, "Documents", "מסמכים", "שלום.txt"), "utf8");
            if (index != 2)
                File.WriteAllText(Path.Combine(src, "Windows", "System32", "not-in-2.txt"), "exists");
            if (index == 3)
            {
                File.WriteAllText(Path.Combine(src, "Windows", "System32", "index3.txt"), "unique");
                Directory.CreateDirectory(Path.Combine(src, "Documents", "Only3Empty"));
            }
            var cmd = new[] {
                "/English", index == 1 ? "/Capture-Image" : "/Append-Image",
                "/ImageFile:" + wim, "/CaptureDir:" + src,
                "/Name:Index " + index
            };
            CheckDism(cmd, 180000);
        }
        var report = Wim360Reader.Inspect(wim);
        if (report.ImageCount != 3)
            throw new InvalidOperationException("Disposable image has the wrong index count.");
        var before = Digest(wim);
        var originalStamp = File.GetLastWriteTimeUtc(wim);
        Wim360IndexComparer.Result diff;
        try
        {
            diff = Wim360OfflineList.Compare(report, new[] { 1, 2, 3 },
                                             CancellationToken.None);
        }
        catch
        {
            Console.WriteLine("OFFLINE WIM failure diagnostics: " +
                "SHA256 unchanged=" + (before == Digest(wim)) +
                ", timestamp unchanged=" +
                (originalStamp == File.GetLastWriteTimeUtc(wim)));
            throw;
        }
        var common = diff.Rows.Single(x => x.Kind == "Files" &&
                                           x.Name.EndsWith("common.txt", StringComparison.Ordinal));
        var absent2 = diff.Rows.Single(x => x.Kind == "Files" &&
                                            x.Name.EndsWith("not-in-2.txt", StringComparison.Ordinal));
        var unique3 = diff.Rows.Single(x => x.Kind == "Files" &&
                                            x.Name.EndsWith("index3.txt", StringComparison.Ordinal));
        var emptyOnly3 = diff.Rows.Single(x => x.Kind == "Folders" &&
                                               x.Name.EndsWith("Only3Empty", StringComparison.Ordinal));
        var russianName = diff.Rows.Single(x => x.Kind == "Files" &&
                         x.Name.EndsWith("Тест-драйвер.txt", StringComparison.Ordinal));
        var hebrewName = diff.Rows.Single(x => x.Kind == "Files" &&
                         x.Name.EndsWith("שלום.txt", StringComparison.Ordinal));
        if (common.State != "Present in all" ||
            absent2.State != "Missing in some" || absent2.At(2) != "—" ||
            unique3.State != "Only in Index 3" ||
            emptyOnly3.State != "Only in Index 3" ||
            russianName.State != "Present in all" ||
            hebrewName.State != "Present in all" ||
            diff.Rows.Any(x => x.Values.Values.Any(v => v != "Present")))
            throw new InvalidOperationException("Offline three-index file/folder presence is wrong.");
        if (before != Digest(wim) ||
            report.Images.Any(x => x.MountStatus.StartsWith("Mounted",
                                                StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("Offline index comparison modified or mounted the WIM.");
        // Cross-container integration: capture an entirely separate WIM with
        // its own Index 1, rather than reusing two indexes in one file.
        // Neither source may be mounted, edited or left with a changed mtime.
        var otherRoot = Path.Combine(root, "cross-image-source-b");
        var otherWim = Path.Combine(root, "different-install.wim");
        Directory.CreateDirectory(Path.Combine(otherRoot, "Windows", "System32"));
        File.WriteAllText(Path.Combine(otherRoot, "Windows", "System32", "common.txt"), "different bytes");
        File.WriteAllText(Path.Combine(otherRoot, "Windows", "System32", "new-in-b.txt"), "only in B");
        CheckDism(new[] {
            "/English", "/Capture-Image", "/ImageFile:" + otherWim,
            "/CaptureDir:" + otherRoot, "/Name:Windows Second Container"
        }, 180000);
        var otherReport = Wim360Reader.Inspect(otherWim);
        var digestA = Digest(wim);
        var digestB = Digest(otherWim);
        var stampA = File.GetLastWriteTimeUtc(wim);
        var stampB = File.GetLastWriteTimeUtc(otherWim);
        var crossPaths = Wim360CrossImage.Compare(report, 1, otherReport, 1,
                                                  true, CancellationToken.None);
        if (crossPaths.Rows.Single(x => x.Name.EndsWith("common.txt",
                StringComparison.Ordinal)).State != "Present in all" ||
            crossPaths.Rows.Single(x => x.Name.EndsWith("new-in-b.txt",
                StringComparison.Ordinal)).State != "Only in Index 2" ||
            crossPaths.Rows.Single(x => x.Name.EndsWith("not-in-2.txt",
                StringComparison.Ordinal)).State != "Only in Index 1" ||
            !crossPaths.ColumnLabels[1].Contains("offline-3-index.wim") ||
            !crossPaths.ColumnLabels[2].Contains("different-install.wim") ||
            digestA != Digest(wim) || digestB != Digest(otherWim) ||
            stampA != File.GetLastWriteTimeUtc(wim) ||
            stampB != File.GetLastWriteTimeUtc(otherWim))
            throw new InvalidOperationException(
                "Cross-container comparison failed presence, labels or WIM source integrity checks.");
        var crossMetadata = Wim360CrossImage.Compare(report, 1, otherReport, 1,
                                                     false, CancellationToken.None);
        if (crossMetadata.Rows.Single(x => x.Name == "Image name").State != "Different" ||
            crossMetadata.Rows.Count != 10)
            throw new InvalidOperationException(
                "Real-WIM cross-container metadata did not compare independent indexes.");
        Console.WriteLine("PASS two independent DISM-created WIMs, Index 1 versus Index 1, " +
                          "offline metadata/Unicode-safe path differences, verified source SHA-256 and mtime.");
        Console.WriteLine("PASS real 3-index Unicode-safe DISM offline file/folder presence, " +
                          "Russian/Hebrew Unicode, unique/missing/empty-folder filters " +
                          "and unchanged WIM SHA-256.");
    }

    public static void Verify(string image, string root)
    {
        if (Environment.GetEnvironmentVariable("DISMGUI_TEST_WIM_MOUNT") != "1")
        {
            Console.WriteLine("SKIP disposable read-only DISM mount integration (opt-in)");
            return;
        }

        var path = Path.GetFullPath(image);
        var mount = Path.Combine(root, "read-only-mount");
        Directory.CreateDirectory(mount);
        var beforeHash = Digest(path);
        bool attemptedMount = false;
        Exception? primaryError = null;
        Exception? cleanupError = null;

        try
        {
            attemptedMount = true;
            CheckDism(new[]
            {
                "/English", "/Mount-Image", "/ImageFile:" + path,
                "/Index:1", "/MountDir:" + mount, "/ReadOnly"
            }, 180000);

            Wim360Reader.WimReport? inspected = null;
            for (var i = 0; i < 10; i++)
            {
                inspected = Wim360Reader.Inspect(path);
                if (inspected.Images.Single().MountStatus == "Mounted (read-only)")
                    break;
                Thread.Sleep(350);
            }
            if (inspected is null || inspected.ImageCount != 1 ||
                inspected.Images[0].MountStatus != "Mounted (read-only)")
                throw new InvalidOperationException(
                    "WIMGAPI did not recognize the real DISM read-only mount: " +
                    inspected?.Images.FirstOrDefault()?.MountStatus + " / " +
                    inspected?.MountScanWarning);

            var actualMount = Path.GetFullPath(inspected.Images[0].MountFolder).
                TrimEnd(Path.DirectorySeparatorChar);
            if (!string.Equals(actualMount,
                Path.GetFullPath(mount).TrimEnd(Path.DirectorySeparatorChar),
                StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(
                    "WIMGAPI mounted path does not match the selected image/index.");

            var verifiedRoot = DriverStudioInventory.ValidateTarget(path, 1, mount);
            if (!string.Equals(
                Path.GetFullPath(verifiedRoot).TrimEnd(Path.DirectorySeparatorChar),
                Path.GetFullPath(mount).TrimEnd(Path.DirectorySeparatorChar),
                StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(
                    "Driver Studio target validation returned an unexpected mount path.");

            var actualContents = Path.Combine(mount, "smoke.txt");
            if (!File.Exists(actualContents) ||
                !File.ReadAllText(actualContents).Contains("read-only catalog"))
                throw new InvalidOperationException(
                    "DISM read-only mounted contents differ from captured source.");
            Console.WriteLine("PASS real DISM /ReadOnly mount recognized by WIMGAPI; " +
                "image/index/path and mounted contents agree.");
        }
        catch (Exception ex)
        {
            primaryError = ex;
        }
        finally
        {
            if (attemptedMount)
            {
                try
                {
                    CheckDism(new[]
                    {
                        "/English", "/Unmount-Image", "/MountDir:" + mount, "/Discard"
                    }, 180000);
                }
                catch (Exception ex)
                {
                    cleanupError = ex;
                    Console.WriteLine("DISM WIM cleanup failure; test mount may remain: " +
                        ex.Message);
                }
            }
        }

        // Never delete the mounted directory on cleanup failure.
        if (cleanupError != null)
            throw new AggregateException("Read-only WIM mount test cleanup failed.",
                new[] { primaryError, cleanupError }.OfType<Exception>());
        if (primaryError != null)
            ExceptionDispatchInfo.Capture(primaryError).Throw();

        var after = Wim360Reader.Inspect(path);
        if (after.Images.Single().MountStatus is not ("Not mounted" or "Not checked"))
            throw new InvalidOperationException(
                "DISM /Discard left a mounted image behind: " + after.Images[0].MountStatus);
        if (!string.Equals(beforeHash, Digest(path), StringComparison.Ordinal))
            throw new InvalidOperationException(
                "Read-only mount/dismount changed source WIM SHA-256 unexpectedly.");
        if (File.Exists(Path.Combine(mount, "smoke.txt")))
            throw new InvalidOperationException("Unmount left mounted image contents exposed.");
        Console.WriteLine("PASS DISM /Discard unmount, no image change, SHA-256 stable.");
    }
}
