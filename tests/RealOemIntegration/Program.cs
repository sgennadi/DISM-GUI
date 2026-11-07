using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using DISM_GUI;

// Opt-in Windows-only end-to-end fixture for a REAL Windows install.wim and a
// legitimately signed OEM driver. It NEVER services the original WIM.
// It is intentionally separate from the regular CI synthetic-WIM smoke suite.
internal static class RealOemIntegration
{
    private const string ConfirmValue = "TEST_COPY_ONLY";

    private sealed record Options(
        bool SelfTest, bool Execute, string Wim, int Index,
        string Inf, string WorkRoot, string Confirm);

    private sealed class Report
    {
        public string Result { get; set; } = "Not started";
        public string SourceWim { get; set; } = "";
        public string SourceSha256Before { get; set; } = "";
        public string SourceSha256After { get; set; } = "";
        public string CopyWim { get; set; } = "";
        public string Inf { get; set; } = "";
        public int Index { get; set; }
        public string Architecture { get; set; } = "";
        public string DriverVersion { get; set; } = "";
        public string CatalogSignature { get; set; } = "";
        public string BaselineFingerprint { get; set; } = "";
        public int DriverCountBefore { get; set; }
        public int DriverCountAfterStage { get; set; }
        public int DriverCountAfterCommit { get; set; }
        public bool StagedConfirmed { get; set; }
        public bool CommittedConfirmed { get; set; }
        public List<string> Events { get; set; } = new();
        public List<string> Warnings { get; set; } = new();
    }

    private static Options Parse(string[] args)
    {
        if (args.Length == 1 && args[0] == "--self-test")
            return new Options(true, false, "", 0, "", "", "");

        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        bool execute = false, plan = false;
        for (int i = 0; i < args.Length; i++)
        {
            var key = args[i];
            if (key == "--plan" || key == "--execute")
            {
                if (key == "--plan") plan = true;
                else execute = true;
                continue;
            }

            if (key is not ("--wim" or "--index" or "--inf" or
                           "--work-root" or "--confirm") ||
                i + 1 >= args.Length || args[i + 1].StartsWith("--", StringComparison.Ordinal) ||
                !values.TryAdd(key, args[++i]))
                throw new ArgumentException("Unknown, repeated or incomplete argument: " + key);
        }

        if (execute == plan)
            throw new ArgumentException("Choose exactly one mode: --plan or --execute.");
        if (!values.TryGetValue("--wim", out var wim) ||
            !values.TryGetValue("--index", out var indexText) ||
            !int.TryParse(indexText, out int index) || index <= 0 ||
            !values.TryGetValue("--inf", out var inf) ||
            !values.TryGetValue("--work-root", out var workRoot))
            throw new ArgumentException("Specify --wim, positive --index, --inf and --work-root.");
        values.TryGetValue("--confirm", out var confirm);
        if (execute && confirm != ConfirmValue)
            throw new ArgumentException(
                "Execution requires --confirm TEST_COPY_ONLY. It will change a disposable COPY.");
        if (!execute && confirm is not null)
            throw new ArgumentException("--confirm is only valid with --execute.");
        return new Options(false, execute, wim, index, inf, workRoot, confirm ?? "");
    }

    private static void Require(bool ok, string message)
    {
        if (!ok) throw new InvalidOperationException(message);
    }

    private static bool Inside(string path, string directory)
    {
        var d = Path.GetFullPath(directory).TrimEnd('\\', '/') +
                Path.DirectorySeparatorChar;
        var p = Path.GetFullPath(path);
        return p.StartsWith(d, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(p.TrimEnd('\\', '/'), directory.TrimEnd('\\', '/'),
                             StringComparison.OrdinalIgnoreCase);
    }

    // No source file or driver package may live inside the test root. The test
    // root must not be inside the driver package directory either: otherwise
    // the source package audit would hash our temporary WIM/mount directory.
    private static void ValidateLocations(string wim, string inf, string workRoot)
    {
        Require(Path.IsPathFullyQualified(wim) &&
                Path.IsPathFullyQualified(inf) &&
                Path.IsPathFullyQualified(workRoot),
                "Use fully qualified paths only.");
        Require(string.Equals(Path.GetExtension(wim), ".wim",
                              StringComparison.OrdinalIgnoreCase),
                "Only a complete .wim is accepted. Do not use .esd or split SWM.");
        Require(string.Equals(Path.GetExtension(inf), ".inf",
                              StringComparison.OrdinalIgnoreCase),
                "Select exactly one .inf file.");
        Require(File.Exists(wim) && File.Exists(inf),
                "WIM and OEM INF source files must already exist.");
        Require(Directory.Exists(workRoot),
                "Create a dedicated empty working-root directory on a local fixed disk first.");
        Require(!Inside(wim, workRoot) && !Inside(inf, workRoot),
                "Input WIM and driver package must be outside the working root.");
        Require(!Inside(workRoot, Path.GetDirectoryName(inf)!),
                "The working root cannot be nested inside the INF source package folder.");
        Require(!string.Equals(Path.GetPathRoot(workRoot)?.TrimEnd('\\', '/'),
                               workRoot.TrimEnd('\\', '/'),
                               StringComparison.OrdinalIgnoreCase),
                "A drive root cannot be used as a working root.");

        var drive = new DriveInfo(Path.GetPathRoot(workRoot)!);
        Require(drive.IsReady && drive.DriveType == DriveType.Fixed,
                "Working root must be on an available local fixed disk, not a network share.");
        foreach (var file in new[] { wim, inf })
            Require((File.GetAttributes(file) & FileAttributes.ReparsePoint) == 0,
                    "Symlink/reparse input files are not allowed: " + file);
        foreach (var folder in new[] {
            workRoot, Path.GetDirectoryName(wim)!, Path.GetDirectoryName(inf)!
        })
        {
            var item = new DirectoryInfo(folder);
            while (item is not null)
            {
                Require((item.Attributes & FileAttributes.ReparsePoint) == 0,
                        "Symlink/junction directory is not supported: " + item.FullName);
                item = item.Parent;
            }
        }
    }

    private static long RequiredFreeBytes(string wim, long uncompressed)
    {
        decimal imageBytes = new FileInfo(wim).Length;
        // Conservative scratch estimate, not a space reservation.
        decimal need = Math.Max(16m * 1024 * 1024 * 1024,
            Math.Max(imageBytes * 2 + 4m * 1024 * 1024 * 1024,
                     (decimal)Math.Max(uncompressed, 0) * 0.6m +
                         4m * 1024 * 1024 * 1024));
        if (need > long.MaxValue) throw new IOException("Space estimate overflow.");
        return (long)Math.Ceiling(need);
    }

    private static string HashFile(string file)
    {
        using var stream = new FileStream(file, FileMode.Open, FileAccess.Read,
                                           FileShare.Read);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private static string DismExecutable()
    {
        var windir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        var dir = Environment.Is64BitOperatingSystem && !Environment.Is64BitProcess
            ? "Sysnative" : "System32";
        var file = Path.Combine(windir, dir, "dism.exe");
        if (!File.Exists(file)) throw new FileNotFoundException("Windows DISM is missing.", file);
        return file;
    }

    private static async Task<string> RunDismAsync(Report report, params string[] arguments)
    {
        var psi = new ProcessStartInfo(DismExecutable()) {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardError = true, RedirectStandardOutput = true
        };
        foreach (var arg in arguments) psi.ArgumentList.Add(arg);
        report.Events.Add("DISM: " + string.Join(" ", arguments.Select(a =>
            a.StartsWith("/Driver:", StringComparison.OrdinalIgnoreCase)
                ? "/Driver:<selected signed INF>" : a)));
        using var process = new Process { StartInfo = psi };
        if (!process.Start()) throw new IOException("Could not start DISM.");
        // Do not kill or timeout a servicing write. Interrupted DISM writes
        // may leave a WIM mount corrupted. Drain both streams concurrently.
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        var output = (await stdout) + "\n" + (await stderr);
        report.Events.Add("DISM exit=" + process.ExitCode);
        if (process.ExitCode != 0)
            throw new IOException("DISM exited " + process.ExitCode + ": " +
                                  output[^Math.Min(2000, output.Length)..]);
        return output;
    }

    private static bool MountedHere(string copy, string mount)
    {
        try
        {
            var inspected = Wim360Reader.Inspect(copy);
            return inspected.Images.Any(x =>
                x.MountStatus.StartsWith("Mounted", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(Path.GetFullPath(x.MountFolder).TrimEnd('\\', '/'),
                              mount.TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase));
        }
        catch { return false; }
    }

    private static async Task DiscardOursAsync(Report report, string copy, string mount)
    {
        if (!MountedHere(copy, mount)) return;
        await RunDismAsync(report, "/English", "/Unmount-Image",
                           "/MountDir:" + mount, "/Discard");
    }

    private static void SaveReport(Report report, string file)
    {
        var json = JsonSerializer.Serialize(report, new JsonSerializerOptions {
            WriteIndented = true
        });
        File.WriteAllText(file, json);
    }

    private static int SelfTest()
    {
        var root = Path.Combine(Path.GetTempPath(), "DismOemPreflight-" +
                               Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var wim = Path.Combine(root, "source.wim");
            var infFolder = Path.Combine(root, "drivers");
            var work = Path.Combine(root, "isolated");
            Directory.CreateDirectory(infFolder);
            Directory.CreateDirectory(work);
            var inf = Path.Combine(infFolder, "sample.inf");
            File.WriteAllText(wim, "NOT A WINDOWS WIM - path validation only");
            File.WriteAllText(inf, "NOT A SIGNED INF - path validation only");
            Require(!Inside(wim, work) && !Inside(inf, work),
                    "Independent source paths should not overlap the work root.");
            Require(Inside(inf, infFolder) && !Inside(wim, infFolder),
                    "Path containment guard gave incorrect results.");
            ValidateLocations(wim, inf, work);
            bool blocked = false;
            try { ValidateLocations(wim, inf, infFolder); }
            catch (InvalidOperationException) { blocked = true; }
            Require(blocked, "Input inside working root was not rejected.");
            blocked = false;
            try { ValidateLocations(wim, inf, root); }
            catch (InvalidOperationException) { blocked = true; }
            Require(blocked, "Working root containing both sources was not rejected.");
            blocked = false;
            try { Parse(new[] { "--execute", "--wim", wim, "--index", "1",
                                "--inf", inf, "--work-root", work }); }
            catch (ArgumentException) { blocked = true; }
            Require(blocked, "Write mode without explicit confirmation was accepted.");
            blocked = false;
            try { Parse(new[] { "--plan", "--execute", "--wim", wim, "--index",
                                "1", "--inf", inf, "--work-root", work }); }
            catch (ArgumentException) { blocked = true; }
            Require(blocked, "Conflicting plan/execute flags were accepted.");
            Require(RequiredFreeBytes(wim, 1) >= 16L * 1024 * 1024 * 1024,
                    "Scratch capacity estimate must fail conservatively.");
            Console.WriteLine("PASS OEM test runner argument, path, opt-in and space guards.");
            return 0;
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static (string Wim, string Inf, string Root,
        Wim360Reader.ImageInfo Image, DriverStudioEngine.DriverPackage Package,
        DriverStudioPackageAudit.AuditResult Audit, long RequiredBytes)
        Preflight(Options options)
    {
        Require(Path.IsPathFullyQualified(options.Wim) &&
                Path.IsPathFullyQualified(options.Inf) &&
                Path.IsPathFullyQualified(options.WorkRoot),
                "Use explicit absolute WIM, INF and working-root paths.");
        var wim = Path.GetFullPath(options.Wim);
        var inf = Path.GetFullPath(options.Inf);
        var workRoot = Path.GetFullPath(options.WorkRoot);
        ValidateLocations(wim, inf, workRoot);
        Require(DriverStudioInstaller.IsAdministrator() || !options.Execute,
                "Run an elevated Windows terminal to execute the copy-only test.");

        var info = Wim360Reader.Inspect(wim);
        Require(!info.IsSplitSet, "Split WIM/SWM is not supported by this test.");
        var image = info.Images.SingleOrDefault(x => x.Index == options.Index);
        Require(image is not null, "Selected WIM index does not exist.");
        Require(!info.Images.Any(x =>
                    x.MountStatus.StartsWith("Mounted", StringComparison.OrdinalIgnoreCase)),
                "Source WIM is mounted. Unmount it before testing a copy.");
        var arch = image!.Architecture;
        Require(arch.Contains("64", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(arch, "x86", StringComparison.OrdinalIgnoreCase),
                "Unknown image architecture: " + arch);
        var driver = DriverStudioEngine.ReadInf(inf);
        Require(string.IsNullOrWhiteSpace(driver.ValidationError),
                "INF metadata invalid: " + driver.ValidationError);
        Require(!DriverStudioEngine.ArchitectureMismatch(driver, arch),
                "Driver architecture mismatch for selected WIM index.");
        var audit = DriverStudioPackageAudit.Audit(driver, arch, checkSignature: true);
        Require(audit.CanInstall,
                "Only intact OEM packages with a valid signed CAT are accepted: " +
                audit.Summary);
        var required = RequiredFreeBytes(wim, image.UncompressedBytes);
        var free = new DriveInfo(Path.GetPathRoot(workRoot)!).AvailableFreeSpace;
        Require(free >= required,
                "Insufficient local scratch space. Available " + free +
                " bytes; conservative minimum " + required + " bytes.");
        return (wim, inf, workRoot, image, driver, audit, required);
    }

    private static async Task<int> ExecuteAsync(Options options)
    {
        var plan = Preflight(options);
        var report = new Report {
            SourceWim = plan.Wim, Inf = plan.Inf,
            Index = options.Index, Architecture = plan.Image.Architecture,
            DriverVersion = plan.Package.Version, CatalogSignature = plan.Audit.CatalogStatus
        };
        Console.WriteLine("Target WIM: " + plan.Wim + " | index " + options.Index +
                          " | " + plan.Image.Architecture);
        Console.WriteLine("Signed CAT: " + plan.Audit.CatalogName +
                          " | status " + plan.Audit.CatalogStatus);
        Console.WriteLine("Scratch free-space requirement: " +
                          plan.RequiredBytes / 1024 / 1024 + " MiB");
        if (!options.Execute)
        {
            Console.WriteLine("PLAN ONLY: no files created, no WIM mounted or changed.");
            Console.WriteLine("To run, specify --execute --confirm TEST_COPY_ONLY.");
            return 0;
        }

        var job = Path.Combine(plan.Root, "OEM-" + Guid.NewGuid().ToString("N"));
        var copy = Path.Combine(job, "test-copy.wim");
        var mount = Path.Combine(job, "mount");
        report.CopyWim = copy;
        var reportFile = Path.Combine(job, "oem-validation-report.json");
        Directory.CreateDirectory(job);
        Directory.CreateDirectory(mount);
        bool successful = false;
        try
        {
            using var writerLease = WimImageOperationLease.Acquire(
                WimImageOperationLease.WriterResource, plan.Wim, copy, mount);
            report.SourceSha256Before = HashFile(plan.Wim);
            report.Events.Add("Copying original WIM to isolated test WIM");
            File.Copy(plan.Wim, copy, overwrite: false);
            Require(HashFile(copy) == report.SourceSha256Before,
                    "Copy SHA-256 differs from original WIM.");
            Require(HashFile(plan.Wim) == report.SourceSha256Before,
                    "Original WIM changed during the copy; aborting.");

            await RunDismAsync(report, "/English", "/Mount-Image",
                "/ImageFile:" + copy, "/Index:" + options.Index,
                "/MountDir:" + mount, "/CheckIntegrity");
            DriverStudioInstaller.RequireWritableTarget(copy, options.Index, mount);
            var before = await DriverStudioInventory.ReadMountedAsync(
                copy, options.Index, mount, CancellationToken.None);
            report.DriverCountBefore = before.Count;
            report.BaselineFingerprint =
                DriverStudioInstaller.SnapshotInstalledInventory(before);

            var decision = DriverStudioEngine.CompareCandidates(
                new[] { plan.Package }, before, true, plan.Image.Architecture).Single();
            Require(DriverStudioInstaller.IsAllowed(decision),
                "Driver comparison returned '" + decision.Status +
                "'. A driver must be New/Newer to test import safely.");
            var freshSource = new[] { DriverStudioEngine.ReadInf(plan.Inf) };
            var approved = DriverStudioInstaller.ValidatePlan(
                new[] { decision }, freshSource, before, plan.Image.Architecture,
                report.BaselineFingerprint);
            DriverStudioPackageAudit.VerifyUnchanged(
                plan.Audit, approved.Single(), plan.Image.Architecture);

            var outcome = await DriverStudioInstaller.AddOneAsync(
                mount, approved.Single(), plan.Audit, plan.Image.Architecture);
            Require(outcome.Succeeded,
                "DISM Add-Driver failed, exit=" + outcome.ExitCode + ": " + outcome.Details);
            Require(!outcome.RebootRequired,
                "DISM requested reboot; no automatic commit is permitted.");

            var afterStage = await DriverStudioInventory.ReadMountedAsync(
                copy, options.Index, mount, CancellationToken.None);
            report.DriverCountAfterStage = afterStage.Count;
            report.StagedConfirmed =
                DriverStudioInstaller.IsPresentInInventory(plan.Package, afterStage);
            Require(report.StagedConfirmed,
                "DISM succeeded but staged-driver inventory did not confirm the INF.");
            await RunDismAsync(report, "/English", "/Unmount-Image",
                               "/MountDir:" + mount, "/Commit");
            Require(!MountedHere(copy, mount), "Mounted test copy remained after Commit.");

            // Remount the committed COPY read-only to verify that staging really
            // survived commit, not merely the transient read/write mount.
            await RunDismAsync(report, "/English", "/Mount-Image",
                "/ImageFile:" + copy, "/Index:" + options.Index,
                "/MountDir:" + mount, "/ReadOnly", "/CheckIntegrity");
            var verifiedMount = Wim360Reader.Inspect(copy).Images.Single(
                x => x.Index == options.Index);
            Require(verifiedMount.MountStatus == "Mounted (read-only)",
                "Reopened test WIM was not mounted read-only as required.");
            var afterCommit = await DriverStudioInventory.ReadMountedAsync(
                copy, options.Index, mount, CancellationToken.None);
            report.DriverCountAfterCommit = afterCommit.Count;
            report.CommittedConfirmed =
                DriverStudioInstaller.IsPresentInInventory(plan.Package, afterCommit);
            Require(report.CommittedConfirmed,
                "Staged OEM INF was not found after committing and remounting the test COPY.");
            await DiscardOursAsync(report, copy, mount);
            successful = true;
        }
        catch (Exception ex)
        {
            successful = false;
            report.Result = "FAIL - see events and retained isolated copy";
            report.Warnings.Add(ex.GetType().Name + ": " + ex.Message);
            Console.Error.WriteLine("OEM TEST FAILED: " + ex.Message);
        }
        finally
        {
            // Never discard a non-owned mount. A failure to dismount is reported;
            // the test copy, mounted folder and report remain for inspection.
            try
            {
                await DiscardOursAsync(report, copy, mount);
            }
            catch (Exception ex)
            {
                successful = false;
                report.Result = "FAIL - test copy may remain mounted";
                report.Warnings.Add("Unmount cleanup: " + ex.Message);
                Console.Error.WriteLine("Manual WIM cleanup required: " + ex.Message);
            }
            try
            {
                if (File.Exists(plan.Wim) && report.SourceSha256Before.Length == 64)
                {
                    report.SourceSha256After = HashFile(plan.Wim);
                    if (report.SourceSha256After != report.SourceSha256Before)
                    {
                        successful = false;
                        report.Result = "FAIL - original WIM hash unexpectedly changed";
                        report.Warnings.Add("Original WIM source hash mismatch.");
                    }
                }
            }
            catch (Exception ex)
            {
                successful = false;
                report.Result = "FAIL - could not verify original WIM after test";
                report.Warnings.Add("Final original hash: " + ex.Message);
            }
            if (successful)
                report.Result = "PASS - staged and committed test copy; original unchanged";
            try
            {
                SaveReport(report, reportFile);
                Console.WriteLine("Report: " + reportFile);
            }
            catch (Exception ex)
            {
                successful = false;
                Console.Error.WriteLine("Unable to write local validation report: " + ex.Message);
            }
            Console.WriteLine("Isolated copy: " + copy +
                              (successful ? " (verified)" : " (investigate before reuse)"));
        }
        return successful ? 0 : 1;
    }

    private static async Task<int> Main(string[] args)
    {
        try
        {
            var options = Parse(args);
            if (options.SelfTest) return SelfTest();
            return await ExecuteAsync(options);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.GetType().Name + ": " + ex.Message);
            Console.Error.WriteLine("Usage: --plan|--execute --wim <install.wim> " +
                "--index <N> --inf <signed.inf> --work-root <local-folder> " +
                "[--confirm TEST_COPY_ONLY]\nOr: --self-test");
            return 2;
        }
    }
}
