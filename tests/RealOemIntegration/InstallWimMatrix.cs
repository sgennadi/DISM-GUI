using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

// Opt-in, copy-only matrix for real Windows 11 / Windows Server install.wim.
// Orchestrates the EXISTING verified single-image runner; original WIMs
// are never mounted or modified and the running OS is not serviced.
internal static partial class RealOemIntegration
{
    private sealed record MatrixTarget(string Name, string Wim, int Index, string Inf);

    private static (bool Execute, string Manifest, string WorkRoot)
        ParseMatrixArguments(string[] arguments)
    {
        bool plan = false, execute = false;
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var seenFlags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < arguments.Length; i++)
        {
            string flag = arguments[i];
            if (flag == "--plan" || flag == "--execute")
            {
                Require(seenFlags.Add(flag), "Repeated matrix mode flag: " + flag);
                if (flag == "--plan") plan = true; else execute = true;
                continue;
            }
            Require(flag is "--matrix" or "--work-root" or "--confirm",
                "Matrix mode accepts only --plan/--execute, --matrix, --work-root and --confirm.");
            Require(i + 1 < arguments.Length &&
                    !arguments[i + 1].StartsWith("--", StringComparison.Ordinal) &&
                    values.TryAdd(flag, arguments[i + 1]),
                "Missing or repeated value for " + flag);
            i++;
        }
        Require(plan != execute, "Specify exactly one matrix mode: --plan or --execute.");
        Require(values.TryGetValue("--matrix", out var manifest) &&
                !string.IsNullOrWhiteSpace(manifest) &&
                values.TryGetValue("--work-root", out var workRoot) &&
                !string.IsNullOrWhiteSpace(workRoot),
                "Specify --matrix <manifest.json> and --work-root <local-folder>.");
        bool confirmed = values.TryGetValue("--confirm", out var confirmation);
        Require(!confirmed || execute, "--confirm is valid only with --execute.");
        Require(!execute || (confirmed && confirmation == ConfirmValue),
                "Execution requires --confirm TEST_COPY_ONLY for disposable WIM copies.");
        Require(Path.IsPathFullyQualified(manifest) && Path.IsPathFullyQualified(workRoot),
                "Use absolute manifest and work-root paths.");
        return (execute, Path.GetFullPath(manifest), Path.GetFullPath(workRoot));
    }

    private static void CheckProperties(JsonElement value, params string[] names)
    {
        Require(value.ValueKind == JsonValueKind.Object, "Expected a JSON object.");
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in value.EnumerateObject())
        {
            Require(seen.Add(property.Name) &&
                    names.Contains(property.Name, StringComparer.Ordinal),
                    "Unexpected or duplicate matrix property: " + property.Name);
        }
    }

    private static string RequiredString(JsonElement node, string field)
    {
        Require(node.TryGetProperty(field, out var property) &&
                property.ValueKind == JsonValueKind.String &&
                !string.IsNullOrWhiteSpace(property.GetString()),
                "Missing or invalid string property: " + field);
        return property.GetString()!.Trim();
    }

    private static List<MatrixTarget> ReadMatrix(string manifest)
    {
        Require(File.Exists(manifest), "Matrix file not found: " + manifest);
        Require((File.GetAttributes(manifest) & FileAttributes.ReparsePoint) == 0,
                "Symlink/reparse matrix files are not supported.");
        Require(new FileInfo(manifest).Length <= 65536,
                "Matrix manifest exceeds the 64 KiB safety limit.");
        using var document = JsonDocument.Parse(File.ReadAllText(manifest),
            new JsonDocumentOptions { MaxDepth = 8, AllowTrailingCommas = false,
                                      CommentHandling = JsonCommentHandling.Disallow });
        var root = document.RootElement;
        CheckProperties(root, "schema", "targets");
        Require(root.TryGetProperty("schema", out var schema) &&
                schema.ValueKind == JsonValueKind.Number &&
                schema.TryGetInt32(out int number) && number == 1,
                "Matrix manifest requires schema: 1.");
        Require(root.TryGetProperty("targets", out var array) &&
                array.ValueKind == JsonValueKind.Array,
                "Matrix manifest requires a targets array.");

        var targets = new List<MatrixTarget>();
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var selectedImages = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in array.EnumerateArray())
        {
            Require(targets.Count < 12, "At most 12 real WIM/index targets are supported.");
            CheckProperties(entry, "name", "wim", "index", "inf");
            string name = RequiredString(entry, "name");
            string wim = RequiredString(entry, "wim");
            string inf = RequiredString(entry, "inf");
            Require(entry.TryGetProperty("index", out var indexProperty) &&
                    indexProperty.ValueKind == JsonValueKind.Number &&
                    indexProperty.TryGetInt32(out int index) && index > 0,
                    "Matrix index must be a positive integer.");
            Require(name.Length <= 72 && name.IndexOfAny(new[] { '\r', '\n', '\0' }) < 0 &&
                    names.Add(name),
                    "Matrix target names must be unique, short and single-line.");
            Require(Path.IsPathFullyQualified(wim) && Path.IsPathFullyQualified(inf),
                "All matrix WIM and INF paths must be absolute.");
            wim = Path.GetFullPath(wim);
            inf = Path.GetFullPath(inf);
            Require(string.Equals(Path.GetExtension(wim), ".wim", StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(Path.GetExtension(inf), ".inf", StringComparison.OrdinalIgnoreCase),
                "Use a complete install.wim and a single signed OEM INF for each target.");
            Require(selectedImages.Add(wim + "\0" + index),
                "Duplicate WIM/index in matrix. Do not test the same image twice.");
            targets.Add(new MatrixTarget(name, wim, index, inf));
        }
        Require(targets.Count >= 1, "Matrix needs at least one WIM/index target.");
        return targets;
    }

    private static async Task<int> ExecuteMatrixAsync(string[] arguments)
    {
        var input = ParseMatrixArguments(arguments);
        var targets = ReadMatrix(input.Manifest);
        var options = targets.Select(t =>
            new Options(false, input.Execute, t.Wim, t.Index,
                        t.Inf, input.WorkRoot, input.Execute ? ConfirmValue : "")).ToList();

        // ALL WIM sources and signed OEM packages must pass preflight before
        // changing even the first disposable copy. The plan is read-only.
        var plans = options.Select(x => Preflight(x)).ToList();
        long totalScratch = 0;
        foreach (var candidate in plans)
            totalScratch = checked(totalScratch + candidate.RequiredBytes);
        var available = new DriveInfo(Path.GetPathRoot(input.WorkRoot)!).AvailableFreeSpace;
        if (input.Execute)
            Require(available >= totalScratch,
                "Matrix scratch disk needs at least " +
                (totalScratch / (1024 * 1024 * 1024)) +
                " GiB before executing disposable copies; available " +
                (available / (1024 * 1024 * 1024)) + " GiB.");

        Console.WriteLine("Matrix: " + targets.Count + " real WIM/index targets; " +
            (input.Execute ? "COPY-ONLY EXECUTE" : "READ-ONLY PLAN"));
        for (int i = 0; i < targets.Count; i++)
            Console.WriteLine("  " + targets[i].Name + " | " +
                Path.GetFileName(targets[i].Wim) + " | index " +
                targets[i].Index + " | CAT valid | " +
                plans[i].Image.Architecture);
        if (!input.Execute)
        {
            Console.WriteLine("Plan successful: all targets validated; no copy, mount, or write.");
            return 0;
        }

        // Sequential DISM servicing only; no concurrent write operations.
        // ExecuteAsync rechecks sources, creates a random per-target test copy,
        // verifies staged+committed inventories and hashes the original WIM.
        var outcomes = new List<object>();
        bool passed = true;
        for (int i = 0; i < targets.Count; i++)
        {
            var target = targets[i];
            Console.WriteLine("Matrix executing: " + target.Name);
            int exitCode;
            try { exitCode = await ExecuteAsync(options[i]); }
            catch (Exception ex)
            {
                Console.Error.WriteLine("Matrix target failed: " + target.Name +
                                        ": " + ex.Message);
                exitCode = 2;
            }
            outcomes.Add(new {
                Name = target.Name, ImageIndex = target.Index,
                WimFile = Path.GetFileName(target.Wim),
                Result = exitCode == 0 ? "PASS" : "FAIL", ExitCode = exitCode
            });
            if (exitCode != 0) { passed = false; break; }
        }
        // Local summary excludes source paths and user-specific information.
        var summary = Path.Combine(input.WorkRoot,
            "oem-matrix-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") +
            "-" + Guid.NewGuid().ToString("N")[..8] + ".json");
        File.WriteAllText(summary, JsonSerializer.Serialize(new {
            Schema = 1, Mode = "COPY_ONLY", TargetsRequested = targets.Count,
            TargetsCompleted = outcomes.Count, Result = passed ? "PASS" : "FAIL",
            Results = outcomes
        }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine("Matrix report: " + summary);
        Console.WriteLine("Matrix result: " + (passed ? "PASS" : "FAIL") +
                          "; per-target reports retained under OEM-*.");
        return passed ? 0 : 1;
    }

    private static void VerifyMatrixSelfTest(string root, string wim,
                                              string inf, string work)
    {
        var manifest = Path.Combine(root, "matrix-fixture.json");
        var images = new[] { wim, Path.Combine(root, "server2022.wim"),
                            Path.Combine(root, "server2025.wim") };
        var data = new {
            schema = 1,
            targets = new[] {
                new { name = "Windows 11", wim = images[0], index = 1, inf },
                new { name = "Server 2022", wim = images[1], index = 2, inf },
                new { name = "Server 2025", wim = images[2], index = 4, inf }
            }
        };
        File.WriteAllText(manifest, JsonSerializer.Serialize(data));
        var parsed = ReadMatrix(manifest);
        Require(parsed.Count == 3 && parsed[2].Index == 4,
                "Three-family OEM matrix parser did not preserve image index order.");
        var plan = ParseMatrixArguments(new[] {
            "--plan", "--matrix", manifest, "--work-root", work
        });
        Require(!plan.Execute && plan.Manifest == manifest,
                "A read-only matrix plan was not recognized.");
        bool blocked = false;
        try
        {
            ParseMatrixArguments(new[] {
                "--execute", "--matrix", manifest, "--work-root", work
            });
        }
        catch (InvalidOperationException) { blocked = true; }
        Require(blocked, "Matrix write path must require explicit copy-only confirmation.");

        blocked = false;
        File.WriteAllText(manifest, JsonSerializer.Serialize(new {
            schema = 1, targets = new[] {
                new { name = "Windows 11", wim = images[0], index = 1, inf },
                new { name = "Windows 11", wim = images[1], index = 2, inf }
            }
        }));
        try { ReadMatrix(manifest); }
        catch (InvalidOperationException) { blocked = true; }
        Require(blocked, "Matrix must reject duplicate target names.");
        blocked = false;
        File.WriteAllText(manifest, JsonSerializer.Serialize(new {
            schema = 1, targets = new[] {
                new { name = "Windows 11", wim = images[0], index = 1, inf },
                new { name = "Server 2022", wim = images[0], index = 1, inf }
            }
        }));
        try { ReadMatrix(manifest); }
        catch (InvalidOperationException) { blocked = true; }
        Require(blocked, "Matrix must reject duplicate WIM/index targets.");
        Console.WriteLine("PASS three-family OEM matrix parsing and explicit opt-in safeguards.");
    }
}
