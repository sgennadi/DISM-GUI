using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using DISM_GUI;

internal static class LayoutSmoke
{
    private static Control? Find(Control parent, string name)
    {
        if (parent.Name == name) return parent;
        foreach (Control child in parent.Controls)
        {
            var nested = Find(child, name);
            if (nested is not null) return nested;
        }
        return null;
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static void NoOverlap(Control left, Control right, string description)
    {
        Require(left.Visible && right.Visible, description + ": both controls must be visible");
        var a = left.RectangleToScreen(left.ClientRectangle);
        var b = right.RectangleToScreen(right.ClientRectangle);
        Require(!a.IntersectsWith(b), description + ": " + a + " intersects " + b);
    }

    private static void Snapshot(Form window, string name)
    {
        try
        {
            var folder = System.IO.Path.Combine(Environment.CurrentDirectory, "ui-screenshots");
            System.IO.Directory.CreateDirectory(folder);
            using var snapshot = new Bitmap(window.Width, window.Height);
            window.DrawToBitmap(snapshot, new Rectangle(Point.Empty, window.Size));
            snapshot.Save(System.IO.Path.Combine(folder, name + ".png"),
                          System.Drawing.Imaging.ImageFormat.Png);
        }
        catch (Exception ex)
        {
            Console.WriteLine("Optional screenshot unavailable: " + ex.Message);
        }
    }

    private static Label? FindLabel(Control parent, string text)
    {
        if (parent is Label label && label.Text == text) return label;
        foreach (Control child in parent.Controls)
        {
            var match = FindLabel(child, text);
            if (match is not null) return match;
        }
        return null;
    }

    private static System.Collections.Generic.IEnumerable<Control> Leaves(Control root)
    {
        foreach (Control child in root.Controls)
        {
            if (child is Label or Button or CheckBox or ComboBox or TextBox or
                MaskedTextBox or NumericUpDown)
            {
                if (child.Visible && child.Width > 0 && child.Height > 0)
                    yield return child;
            }
            foreach (var nested in Leaves(child))
                yield return nested;
        }
    }

    private static Rectangle ClippedRectangle(Control control)
    {
        if (!control.Visible) return Rectangle.Empty;
        var visible = control.RectangleToScreen(control.ClientRectangle);
        for (Control? parent = control.Parent; parent is not null; parent = parent.Parent)
        {
            visible = Rectangle.Intersect(visible,
                parent.RectangleToScreen(parent.ClientRectangle));
            if (visible.IsEmpty) return Rectangle.Empty;
        }
        return visible;
    }

    private static System.Collections.Generic.IEnumerable<Control> AllControls(Control root)
    {
        foreach (Control child in root.Controls)
        {
            yield return child;
            foreach (var nested in AllControls(child))
                yield return nested;
        }
    }

    private static void ValidateSection(Control page, int section, string dimension)
    {
        Require(Find(page, "SectionResponsiveRoot") is TableLayoutPanel,
            $"Section {section} still has legacy absolute positions");
        var left = Find(page, "SectionFields") as FlowLayoutPanel;
        var actions = Find(page, "SectionActions") as FlowLayoutPanel;
        Require(left is not null && actions is not null,
            $"Section {section} fields/actions are missing");
        Require(left!.AutoScroll && actions!.AutoScroll,
            $"Section {section} must allow vertical scrolling at high DPI");
        var leaves = Leaves(page).ToList();
        Require(leaves.OfType<Button>().Any() && leaves.OfType<TextBoxBase>().Any(),
            $"Section {section} lacks original interactive elements");
        for (int i = 0; i < leaves.Count; ++i)
        {
            var ctrl = leaves[i];
            var bounds = ClippedRectangle(ctrl);
            if (bounds.IsEmpty) continue; // Currently scrolled outside the viewport
            foreach (var other in leaves.Skip(i + 1))
            {
                var peer = ClippedRectangle(other);
                if (peer.IsEmpty) continue;
                Require(!bounds.IntersectsWith(peer),
                    $"Section {section} at {dimension}: {ctrl.Name} " +
                    $"{bounds} overlaps {other.Name} {peer}");
            }
            if (ctrl is Button button)
            {
                var measured = TextRenderer.MeasureText(button.Text, button.Font,
                    new Size(Math.Max(70, button.ClientSize.Width - 15), 1000),
                    TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl);
                Require(measured.Height <= button.ClientSize.Height + 3,
                    $"Section {section} at {dimension}: button '{button.Text}' " +
                    $"is vertically clipped ({measured.Height}>{button.Height})");
            }
            if (ctrl is Label label && !string.IsNullOrWhiteSpace(label.Text))
            {
                var measured = TextRenderer.MeasureText(label.Text, label.Font);
                Require(measured.Height <= label.ClientSize.Height + 3,
                    $"Section {section} at {dimension}: label '{label.Text}' is clipped");
            }
        }
        Console.WriteLine($"PASS {dimension} section {section} ({leaves.Count} interactive/text elements)");
    }

    private static void VerifyWindowGeometry()
    {
        var work = Screen.PrimaryScreen!.WorkingArea;
        var saved = new Rectangle(work.Left + 26, work.Top + 25,
            Math.Min(800, Math.Max(530, work.Width - 110)),
            Math.Min(570, Math.Max(460, work.Height - 105)));

        using (var first = new Form { MinimumSize = new Size(500, 420),
                                       StartPosition = FormStartPosition.Manual,
                                       ShowInTaskbar = false })
        {
            WindowGeometry.Install(first);
            first.StartPosition = FormStartPosition.Manual;
            first.Bounds = saved;
            first.Show();
            Application.DoEvents();
            first.Close(); // FormClosed persists its final normal bounds.
        }
        using (var second = new Form { MinimumSize = new Size(500, 420),
                                        ShowInTaskbar = false })
        {
            WindowGeometry.Install(second);
            second.Show();
            Application.DoEvents();
            Require(Math.Abs(second.Left - saved.Left) <= 6 &&
                    Math.Abs(second.Top - saved.Top) <= 6 &&
                    Math.Abs(second.Width - saved.Width) <= 8 &&
                    Math.Abs(second.Height - saved.Height) <= 8,
                "Window size or position did not restore from user preferences");
            second.Close();
        }

        // Unplugged monitor / obsolete display coordinates must not strand a
        // restored window outside every active desktop.
        var prefPath = @"Software\DISMGUI\UserInterface";
        using (var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(prefPath))
        {
            key!.SetValue("WindowLeft", 50000);
            key.SetValue("WindowTop", 50000);
            key.SetValue("WindowWidth", 850);
            key.SetValue("WindowHeight", 610);
        }
        using (var rescued = new Form { MinimumSize = new Size(500, 420),
                                         ShowInTaskbar = false })
        {
            WindowGeometry.Install(rescued);
            rescued.Show();
            Application.DoEvents();
            Require(Screen.AllScreens.Any(screen =>
                Rectangle.Intersect(rescued.Bounds, screen.WorkingArea).Width >= 120 &&
                Rectangle.Intersect(rescued.Bounds, screen.WorkingArea).Height >= 120),
                "A restored window is stranded outside all connected screens");
            rescued.Close();
        }
        Console.WriteLine("PASS window size/position persistence and off-screen recovery");
    }

    private const string WIM_METADATA_FIXTURE =
        "<WIM>" +
        "<TOTALBYTES>1048576</TOTALBYTES>" +
        "<IMAGE INDEX=\"1\">" +
        "<NAME>Microsoft Windows PE</NAME>" +
        "<DESCRIPTION>Windows PE for deployment</DESCRIPTION>" +
        "<TOTALBYTES>12456789</TOTALBYTES>" +
        "<WINDOWS><ARCH>9</ARCH><EDITIONID>WindowsPE</EDITIONID>" +
        "<INSTALLATIONTYPE>WindowsPE</INSTALLATIONTYPE>" +
        "<PRODUCTNAME>Microsoft Windows PE</PRODUCTNAME>" +
        "<LANGUAGES><LANGUAGE>en-US</LANGUAGE><LANGUAGE>he-IL</LANGUAGE></LANGUAGES>" +
        "<VERSION><MAJOR>10</MAJOR><MINOR>0</MINOR><BUILD>26100</BUILD></VERSION>" +
        "</WINDOWS></IMAGE>" +
        "<IMAGE INDEX=\"2\"><NAME>Microsoft Windows Setup</NAME>" +
        "<FLAGS>Setup</FLAGS><TOTALBYTES>23456789</TOTALBYTES>" +
        "<WINDOWS><ARCH>12</ARCH><EDITIONID>WindowsSetup</EDITIONID>" +
        "<VERSION><MAJOR>10</MAJOR><MINOR>0</MINOR><BUILD>26100</BUILD></VERSION>" +
        "</WINDOWS></IMAGE></WIM>";

    private static void VerifyWimMetadata()
    {
        var report = Wim360Reader.ParseMetadataXml(
            WIM_METADATA_FIXTURE, @"C:\Images\boot.wim", 87654321);
        Require(report.ImageCount == 2 &&
                report.Images[0].Index == 1 && report.Images[1].Index == 2,
                "WIM metadata did not preserve all image indexes");
        Require(report.Images[0].Architecture == "x64" &&
                report.Images[1].Architecture == "ARM64",
                "WIM XML architecture mapping failed");
        Require(report.Images[0].Version == "10.0.26100" &&
                report.Images[0].Edition == "WindowsPE" &&
                report.Images[0].Languages.Contains("he-IL"),
                "WIM XML OS version, edition or languages were lost");
        Require(report.FileSizeBytes == 87654321 &&
                Wim360Reader.FormatReport(report).Contains("Index 2:"),
                "WIM 360 report generation is incomplete");
        Require(Wim360Reader.HumanSize(1073741824) == "1 GB",
                "WIM file size formatting is incorrect");

        bool dtdRejected = false;
        try {
            Wim360Reader.ParseMetadataXml(
                "<!DOCTYPE WIM [<!ENTITY x SYSTEM 'file:///C:/Windows/win.ini'>]>" +
                "<WIM><IMAGE INDEX='1'><NAME>&x;</NAME></IMAGE></WIM>",
                "untrusted.wim", 0);
        } catch (System.Xml.XmlException) {
            dtdRejected = true;
        }
        Require(dtdRejected, "WIM XML parser must forbid external entities");
        bool duplicateRejected = false;
        try {
            Wim360Reader.ParseMetadataXml(
                "<WIM><IMAGE INDEX='1'/><IMAGE INDEX='1'/></WIM>", "bad.wim", 0);
        } catch (System.IO.InvalidDataException) {
            duplicateRejected = true;
        }
        Require(duplicateRejected, "WIM XML parser must reject duplicate indexes");
        Console.WriteLine("PASS WIM 360 metadata, architecture, report and XML safety tests");
    }

    private static void VerifyRuntimeDiagnostics()
    {
        var temp = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
            "DISMGUI-Local-Diagnostics-" + Guid.NewGuid().ToString("N"));
        var oldTest = Environment.GetEnvironmentVariable("DISMGUI_TEST_DIAGNOSTICS");
        var oldPath = Environment.GetEnvironmentVariable("DISMGUI_TEST_LOG_DIRECTORY");
        Environment.SetEnvironmentVariable("DISMGUI_TEST_DIAGNOSTICS", "1");
        Environment.SetEnvironmentVariable("DISMGUI_TEST_LOG_DIRECTORY", temp);
        try
        {
            Require(RuntimeDiagnostics.ReadRecent().Contains("no recorded runtime errors"),
                "Test runtime journal should be empty initially.");
            RuntimeDiagnostics.RecordException("Mounted WIM",
                new InvalidOperationException("Password=DO_NOT_KEEP_THIS_SECRET"));
            RuntimeDiagnostics.RecordDismExit("DISM command", "87");
            RuntimeDiagnostics.RecordDismExit("DISM command", "0");
            RuntimeDiagnostics.RecordDismExit("DISM command", "3010");
            var history = RuntimeDiagnostics.ReadRecent(15);
            Require(history.Contains("Mounted WIM") &&
                    history.Contains("DISM command") &&
                    history.Contains("code=87") &&
                    !history.Contains("DO_NOT_KEEP_THIS_SECRET"),
                    "Persistent local diagnostics should be redacted, bounded and store failed DISM exits.");
            var raw = System.IO.File.ReadAllText(
                System.IO.Path.Combine(temp, "events.log"));
            Require(raw.Contains("sig=") &&
                    !raw.Contains("DO_NOT_KEEP_THIS_SECRET") &&
                    raw.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length == 2,
                "Local journal must use coarse signatures and exclude successful DISM exits.");
            var withoutHistory = ErrorDiagnostics.BuildReport(
                "Failure", "Test", false, "", "System", 96);
            Require(!withoutHistory.Contains("DISM command") &&
                    withoutHistory.Contains("not included (opt-in only)"),
                "GitHub reports must exclude local history by default.");
            var withHistory = ErrorDiagnostics.BuildReport(
                "Failure", "Test", false, "", "System", 96, true);
            Require(withHistory.Contains("Recent DISM GUI runtime errors") &&
                    withHistory.Contains("code=87") &&
                    !withHistory.Contains("DO_NOT_KEEP_THIS_SECRET"),
                "Opt-in local diagnostics must appear only after explicit inclusion.");
            RuntimeDiagnostics.ClearHistory();
            Require(RuntimeDiagnostics.ReadRecent().Contains("no recorded runtime errors"),
                "Clear History must erase both local diagnostic files.");
            Console.WriteLine("PASS persistent local exception/DISM exit capture, redaction and opt-in history.");
        }
        finally
        {
            Environment.SetEnvironmentVariable("DISMGUI_TEST_DIAGNOSTICS", oldTest);
            Environment.SetEnvironmentVariable("DISMGUI_TEST_LOG_DIRECTORY", oldPath);
            try { System.IO.Directory.Delete(temp, recursive: true); }
            catch { /* Disposable test artifacts are not production files. */ }
        }
    }

    private static void VerifyErrorDiagnostics()
    {
        var raw = "Password: DO_NOT_UPLOAD_123\n" +
                  "RecoveryKey=214516-214516-214516-214516-214516-214516-214516-214516\n" +
                  "Operator: tech@example.com\n" +
                  @"Mounted at C:\Users\tester\Private\install.wim" + "\n" +
                  "Host IP 10.0.0.193\n";
        var clean = ErrorDiagnostics.Redact(raw);
        Require(!clean.Contains("DO_NOT_UPLOAD_123") &&
                !clean.Contains("214516-214516") &&
                !clean.Contains("tech@example.com") &&
                !clean.Contains("tester") &&
                !clean.Contains("10.0.0.193") &&
                clean.Contains("[REDACTED:"),
                "Diagnostic report redaction failed to remove credentials, recovery key, account or path");

        ErrorDiagnostics.RememberFailure("WIM 360",
            new InvalidOperationException(@"C:\Users\tester\Private\image.wim"));
        var report = ErrorDiagnostics.BuildReport(
            "WIM 360 returned an error", "How to reproduce:\nOpen read-only image",
            false, raw, "WIM 360", 144);
        Require(report.Contains("DISM GUI: ") &&
                report.Contains("Display DPI: 144") &&
                report.Contains("DISM output: not included") &&
                !report.Contains("DO_NOT_UPLOAD_123"),
                "Default diagnostic report must exclude logs and show version/DPI");
        var withLog = ErrorDiagnostics.BuildReport(
            "WIM 360 returned an error", "Scan failed", true, raw, "WIM 360", 144);
        Require(withLog.Contains("DISM output (last 60 lines") &&
                !withLog.Contains("214516-214516") &&
                !withLog.Contains("tech@example.com"),
                "Opt-in log report must redact before preview");
        var url = ErrorDiagnostics.BuildGitHubIssueUrl("WIM 360 error", withLog);
        var decoded = Uri.UnescapeDataString(url);
        Require(url.StartsWith("https://github.com/sgennadi/DISM-GUI/issues/new?",
                               StringComparison.Ordinal) &&
                !decoded.Contains("DO_NOT_UPLOAD_123") &&
                !decoded.Contains("tech@example.com"),
                "GitHub draft URL must be restricted to the project and sanitized");

        using var dialog = new ErrorReportForm(raw, "WIM 360 diagnostics");
        dialog.Show();
        Application.DoEvents();
        var include = Find(dialog, "chkSupportIncludeLog") as CheckBox;
        var reviewed = Find(dialog, "chkSupportReviewed") as CheckBox;
        var history = Find(dialog, "chkSupportIncludeHistory") as CheckBox;
        var openHistory = Find(dialog, "btnSupportOpenHistory") as Button;
        var clearHistory = Find(dialog, "btnSupportClearHistory") as Button;
        var preview = Find(dialog, "txtSupportPreview") as TextBox;
        var title = Find(dialog, "txtSupportTitle") as TextBox;
        var description = Find(dialog, "txtSupportDescription") as TextBox;
        var copy = Find(dialog, "btnSupportCopy") as Button;
        var draft = Find(dialog, "btnSupportGitHub") as Button;
        var close = Find(dialog, "btnSupportClose") as Button;
        Require(include is not null && reviewed is not null && preview is not null &&
                history is not null && openHistory is not null && clearHistory is not null &&
                title is not null && description is not null &&
                copy is not null && draft is not null && close is not null,
                "Error report UI is missing required preview, consent or export controls");
        Require(!include!.Checked && !history!.Checked &&
                !reviewed!.Checked && !draft!.Enabled,
                "Diagnostic logs must opt-in and GitHub action must require review");
        NoOverlap(openHistory!, clearHistory!, "Open / Clear diagnostic logs");
        NoOverlap(clearHistory, copy!, "Clear diagnostic logs / Copy");
        NoOverlap(copy, draft, "Diagnostics Copy / GitHub draft actions");
        NoOverlap(draft, close!, "Diagnostics GitHub draft / Close actions");

        include.Checked = true;
        Application.DoEvents();
        Require(preview!.Text.Contains("DISM output (last 60 lines") &&
                !preview.Text.Contains("DO_NOT_UPLOAD_123"),
                "Opt-in diagnostic preview must be redacted");
        history.Checked = true;
        Application.DoEvents();
        Require(preview.Text.Contains("Recent DISM GUI runtime errors") &&
                !reviewed.Checked,
                "Including local history must require new explicit review");
        history.Checked = false;
        Application.DoEvents();
        Require(preview.Text.Contains("Local application error history: not included"),
                "Unchecking local history must exclude it from the preview");
        reviewed.Checked = true;
        Application.DoEvents();
        Require(draft.Enabled, "Reviewed report should allow opening GitHub draft");
        preview.AppendText("\nEdited report");
        Application.DoEvents();
        Require(!draft.Enabled && !reviewed.Checked,
                "Editing report after consent must invalidate approval");
        Snapshot(dialog, "DISM-GUI-Error-Diagnostics");
        dialog.Close();
        Application.DoEvents();
        Console.WriteLine("PASS diagnostic sanitization, explicit consent and GitHub draft UI");
    }

    private static void VerifyMountedAudit()
    {
        const string json = "[{\"PackageName\":\"Package_A\",\"PackageState\":\"Installed\",\"ReleaseType\":\"Update\"}," +
                            "{\"PackageName\":\"Package_B\",\"PackageState\":\"Install Pending\",\"ReleaseType\":\"Feature Pack\"}," +
                            "{\"PackageName\":\"Package_C\",\"PackageState\":\"Staged\",\"ReleaseType\":\"Update\"}]";
        var packages = Wim360Servicing.ParsePackagesJson(json);
        Require(packages.Count == 3 && packages[1].State == "Install Pending",
                "WIM 360 mounted package inventory lost package states");
        Require(Wim360Servicing.ParsePackagesJson("[]").Count == 0 &&
                Wim360Servicing.ParsePackagesJson("{\"PackageName\":\"One\",\"PackageState\":\"Installed\"}").Count == 1,
                "Mounted package parser must accept empty arrays and PowerShell singleton JSON");
        bool invalidRejected = false;
        try { Wim360Servicing.ParsePackagesJson("[null]"); }
        catch (System.IO.InvalidDataException) { invalidRejected = true; }
        Require(invalidRejected, "Malformed package JSON must not give a misleading clean audit");
        var drivers = new[] {
            new DriverStudioEngine.DriverPackage { InfName = "a.inf", ClassName = "Net", Provider = "Microsoft", IsInbox = true },
            new DriverStudioEngine.DriverPackage { InfName = "b.inf", ClassName = "Net", Provider = "Vendor", IsInbox = false }
        };
        var audit = Wim360Servicing.Summarize(@"C:\images\install.wim", 2,
            @"C:\MountAuditFixture", drivers, packages);
        Require(audit.DriverCount == 2 && audit.InboxDriverCount == 1 &&
                audit.ThirdPartyDriverCount == 1 && audit.PackageCount == 3 &&
                audit.InstalledPackageCount == 1 && audit.PendingPackageCount == 1 &&
                audit.StagedPackageCount == 1 && audit.DriverClasses.Contains("Net (2)"),
                "Mounted audit package/driver counts or class breakdown are incorrect");
        var report = Wim360Reader.ParseMetadataXml(WIM_METADATA_FIXTURE,
            @"C:\images\install.wim", 1024);
        report.Images[1].ServicingAudit = audit;
        Require(Wim360Reader.FormatReport(report).Contains("Windows packages: 3") &&
                Wim360Reader.FormatReport(report).Contains("Driver packages: 2"),
                "WIM 360 copy report must include mounted audit results");
        var partial = Wim360Servicing.Summarize(@"C:\images\install.wim", 2,
            @"C:\MountAuditFixture", Array.Empty<DriverStudioEngine.DriverPackage>(),
            Array.Empty<Wim360Servicing.PackageEntry>(),
            driverWarning: "Driver query timed out", packageWarning: "Package query failed");
        var partialText = Wim360Servicing.FormatAudit(partial);
        Require(partial.HasWarnings && partial.DriverWarning.Length > 0 &&
                partial.PackageWarning.Length > 0 &&
                partialText.Contains("Driver packages: unavailable") &&
                partialText.Contains("Windows packages: unavailable") &&
                partialText.Contains("PARTIAL AUDIT"),
                "Partial DISM failures must be marked unavailable, never as zero counts");
        var incompleteExport = Wim360SnapshotDiff.ParseSnapshotJson(
            Wim360Catalog.ToJson(partial, Wim360Catalog.BuildRows(partial)));
        Require(incompleteExport.DriverWarning.Length > 0 &&
                incompleteExport.PackageWarning.Length > 0,
                "Snapshot JSON must preserve sanitized driver/package errors");
        var completeExport = Wim360SnapshotDiff.ParseSnapshotJson(
            Wim360Catalog.ToJson(audit, Wim360Catalog.BuildRows(audit)));
        var partialComparison = Wim360SnapshotDiff.Compare(completeExport, incompleteExport);
        Require(partialComparison.RemovedCount == 0 &&
                partialComparison.SuppressedMembershipCount >= 2 &&
                partialComparison.Warnings.Any(w => w.Contains("Driver inventory")) &&
                partialComparison.Warnings.Any(w => w.Contains("package inventory")),
                "Partial driver/package inventory cannot create false snapshot removals");
        Console.WriteLine("PASS partial mounted audit and safe incomplete inventory comparison");
        Console.WriteLine("PASS mounted WIM 360 driver/package audit and read-only report");
    }

    private static Wim360Servicing.AuditResult VerifyDetailedInventory()
    {
        const string json = "{\"Features\":[" +
            "{\"Name\":\"SMB1Protocol\",\"State\":\"Disabled\"}," +
            "{\"Name\":\"NetFx3\",\"State\":\"Enabled\"}]," +
            "\"Capabilities\":[" +
            "{\"Name\":\"OpenSSH.Client~~~~0.0.1.0\",\"State\":\"Installed\"}," +
            "{\"Name\":\"SomeOptionalCapability\",\"State\":\"NotPresent\"}]," +
            "\"FeatureWarning\":\"\",\"CapabilityWarning\":\"\"}";
        var parsed = Wim360Servicing.ParseComponentsJson(json);
        Require(parsed.Items.Count == 4 &&
                parsed.Items[0].Kind == "Features" &&
                parsed.Items[2].Kind == "Capabilities",
                "WIM 360 feature/capability JSON classification failed");
        Require(Wim360Servicing.ParseComponentsJson(
                "{\"Features\":[],\"Capabilities\":[],\"FeatureWarning\":\"Unavailable\",\"CapabilityWarning\":\"\"}")
                .FeatureWarning == "Unavailable",
                "Missing optional component APIs should preserve explicit warnings");
        bool malformedRejected = false;
        try { Wim360Servicing.ParseComponentsJson("{\"Features\":[]}"); }
        catch (System.IO.InvalidDataException) { malformedRejected = true; }
        Require(malformedRejected, "Missing component array must fail rather than report zero entries");

        var drivers = new[] {
            new DriverStudioEngine.DriverPackage {
                InfName = "oem1.inf", PublishedName = "oem17.inf",
                Provider = "Vendor", ClassName = "Net", DriverDate = "2026-03-08",
                Version = "4.2.0", IsInbox = false },
            new DriverStudioEngine.DriverPackage {
                InfName = "=SUM(1,2).inf", PublishedName = "net.inf",
                Provider = "Microsoft", ClassName = "Net", Version = "1.0",
                IsInbox = true }
        };
        var packages = new[] {
            new Wim360Servicing.PackageEntry {
                Name = "Package \"test\", \r\nline", State = "Install Pending", ReleaseType = "Update" }
        };
        var audit = Wim360Servicing.Summarize(@"C:\images\install.wim", 2,
            @"C:\Mounted", drivers, packages, parsed.Items);
        Require(audit.FeatureCount == 2 && audit.EnabledFeatureCount == 1 &&
                audit.CapabilityCount == 2 && audit.InstalledCapabilityCount == 1 &&
                audit.Drivers.Count == 2 && audit.Packages.Count == 1,
                "Detailed inventory totals are incorrect");

        var all = Wim360Catalog.BuildRows(audit);
        Require(all.Count == 7 && all.Count(x => x.Kind == "Drivers") == 2,
                "Detailed catalog does not contain every driver/package/component");
        var net = Wim360Catalog.FilterRows(all, "Drivers", "Third-party", "VENDOR");
        Require(net.Count == 1 && net[0].Name == "oem1.inf",
                "Catalog must combine type, state and case-insensitive keyword filtering");
        var disabled = Wim360Catalog.FilterRows(all, "Features", "Disabled", "smb");
        Require(disabled.Count == 1 && disabled[0].Name == "SMB1Protocol",
                "Features filter must respect both state and search");
        Require(Wim360Catalog.FilterRows(all, "All types", "All states", "unlikely").Count == 0,
                "Nonmatching search should give an empty, not stale, result");

        var csv = Wim360Catalog.ToCsv(audit, all);
        Require(csv.Contains("\"'=SUM(1,2).inf\"") &&
                csv.Contains("\"Package \"\"test\"\", \r\nline\"") &&
                csv.Contains("\"Image\",\"Index\",\"Scanned UTC\""),
                "CSV export must protect spreadsheet formulas and quote newlines");
        var filteredCsv = Wim360Catalog.ToCsv(audit, net);
        Require(filteredCsv.Contains("\"oem1.inf\"") && !filteredCsv.Contains("\"SMB1Protocol\""),
                "CSV must export only currently filtered rows");
        using var jsonDoc = System.Text.Json.JsonDocument.Parse(Wim360Catalog.ToJson(audit, disabled));
        Require(jsonDoc.RootElement.GetProperty("SchemaVersion").GetInt32() == 2 &&
                jsonDoc.RootElement.GetProperty("Rows").GetArrayLength() == 1 &&
                jsonDoc.RootElement.GetProperty("ImageIndex").GetInt32() == 2 &&
                jsonDoc.RootElement.GetProperty("InventoryRowCount").GetInt32() == 7 &&
                !jsonDoc.RootElement.GetProperty("IsCompleteInventory").GetBoolean(),
                "JSON export must retain source image and filtered records");
        Console.WriteLine("PASS detailed feature/capability inventory, filters and safe CSV/JSON exports");
        return audit;
    }

    private static void VerifyCatalogWindow(Form owner,
        Wim360Servicing.AuditResult audit)
    {
        using var window = new Wim360InventoryForm(audit);
        window.Show(owner);
        Application.DoEvents();
        var grid = Find(window, "grid360Catalog") as DataGridView;
        var type = Find(window, "cmb360CatalogType") as ComboBox;
        var state = Find(window, "cmb360CatalogState") as ComboBox;
        var search = Find(window, "txt360CatalogSearch") as TextBox;
        var clear = Find(window, "btn360CatalogClear") as Button;
        var csv = Find(window, "btn360ExportCsv") as Button;
        var json = Find(window, "btn360ExportJson") as Button;
        var close = Find(window, "btn360CatalogClose") as Button;
        Require(grid is not null && type is not null && state is not null &&
                search is not null && csv is not null && json is not null &&
                clear is not null && close is not null,
                "WIM 360 catalog search, grid and export buttons must be present");
        Require(grid!.VirtualMode && grid.RowCount == 7,
                "Catalog must show all inventory rows via virtual, read-only grid");
        NoOverlap(type!, state!, "WIM catalog type and state filters");
        NoOverlap(state, search!, "WIM catalog state and search filters");
        NoOverlap(csv!, json!, "WIM catalog CSV / JSON buttons");
        NoOverlap(json, close!, "WIM catalog JSON / Close buttons");

        type!.SelectedItem = "Features";
        Application.DoEvents();
        Require(grid.RowCount == 2, "Feature filter should show only feature rows");
        state!.SelectedItem = "Disabled";
        Application.DoEvents();
        Require(grid.RowCount == 1, "Feature state should further narrow the catalog");
        search!.Text = "NONMATCH";
        Application.DoEvents();
        Require(grid.RowCount == 0, "Catalog search did not hide unmatched feature rows");
        clear!.PerformClick();
        Application.DoEvents();
        Require(grid.RowCount == 7, "Clear filters did not restore complete inventory");
        Snapshot(window, "DISM-GUI-WIM360-Inventory");
        window.Close();
        Application.DoEvents();
        Console.WriteLine("PASS WIM 360 catalog window layout, filtering, sorting and buttons");
    }

    private static Wim360SnapshotDiff.DiffReport VerifySnapshotDiff(
        Wim360Servicing.AuditResult baselineAudit)
    {
        var all = Wim360Catalog.BuildRows(baselineAudit);
        var baselineJson = Wim360Catalog.ToJson(baselineAudit, all);
        var baseline = Wim360SnapshotDiff.ParseSnapshotJson(baselineJson);
        Require(baseline.SchemaVersion == 2 && baseline.CoverageKnown &&
                baseline.IsCompleteInventory && baseline.InventoryRowCount == 7 &&
                baseline.Rows.Count == 7,
                "Complete JSON inventory exports must use schema 2 with coverage flags");
        var reordered = Wim360SnapshotDiff.ParseSnapshotJson(
            Wim360Catalog.ToJson(baselineAudit, all.AsEnumerable().Reverse()));
        Require(reordered.IsCompleteInventory,
                "Reordered full exports must remain marked complete");
        var filtered = Wim360SnapshotDiff.ParseSnapshotJson(
            Wim360Catalog.ToJson(baselineAudit, all.Take(1)));
        Require(filtered.SchemaVersion == 2 && !filtered.IsCompleteInventory &&
                filtered.InventoryRowCount == 7 && filtered.Rows.Count == 1,
                "Filtered snapshots cannot claim complete inventory");

        // Older schema 1 exports did not record whether a filter was applied.
        var legacy = Wim360SnapshotDiff.ParseSnapshotJson(
            baselineJson.Replace("\"SchemaVersion\": 2", "\"SchemaVersion\": 1"));
        Require(!legacy.CoverageKnown && !legacy.IsCompleteInventory,
                "Legacy snapshot must never be considered complete");
        var legacyDiff = Wim360SnapshotDiff.Compare(legacy, baseline);
        Require(!legacyDiff.CanDetectMembershipChanges && legacyDiff.AddedCount == 0 &&
                legacyDiff.RemovedCount == 0 && legacyDiff.Warnings.Count > 0,
                "Legacy snapshots must not report uncertain adds/removals");

        var newDrivers = new[] {
            new DriverStudioEngine.DriverPackage {
                InfName = "oem1.inf", PublishedName = "oem17.inf",
                Provider = "Vendor", ClassName = "Net",
                DriverDate = "2026-04-08", Version = "4.3.0", IsInbox = false },
            new DriverStudioEngine.DriverPackage {
                InfName = "wifi.inf", PublishedName = "oem27.inf",
                Provider = "Second Vendor", ClassName = "Net",
                DriverDate = "2026-04-09", Version = "1.0", IsInbox = false }
        };
        var newPackages = new[] {
            new Wim360Servicing.PackageEntry {
                Name = "Package \"test\", \r\nline", State = "Installed", ReleaseType = "Update" }
        };
        var newComponents = new[] {
            new Wim360Servicing.ComponentEntry {
                Kind = "Features", Name = "SMB1Protocol", State = "Enabled" },
            new Wim360Servicing.ComponentEntry {
                Kind = "Features", Name = "NetFx3", State = "Enabled" },
            new Wim360Servicing.ComponentEntry {
                Kind = "Capabilities", Name = "OpenSSH.Client~~~~0.0.1.0", State = "Installed" },
            new Wim360Servicing.ComponentEntry {
                Kind = "Capabilities", Name = "BrandNew.Capability", State = "Installed" }
        };
        var newAudit = Wim360Servicing.Summarize(@"C:\images\install.wim", 2,
            @"C:\Mounted", newDrivers, newPackages, newComponents);
        var current = Wim360SnapshotDiff.ParseSnapshotJson(
            Wim360Catalog.ToJson(newAudit, Wim360Catalog.BuildRows(newAudit)));
        var report = Wim360SnapshotDiff.Compare(baseline, current);
        Require(report.CanDetectMembershipChanges && report.AddedCount == 2 &&
                report.RemovedCount == 2 && report.ChangedCount == 3 &&
                report.UnchangedCount == 2 && report.Changes.Count == 7 &&
                report.AmbiguousCount == 0,
                "Snapshot diff failed to distinguish Added/Removed/Changed/Unchanged");
        var driverChanges = Wim360SnapshotDiff.FilterChanges(
            report.Changes, "Changed", "Drivers", "4.3");
        Require(driverChanges.Count == 1 && driverChanges[0].Name == "oem1.inf" &&
                driverChanges[0].Details.Contains("Version"),
                "Snapshot filters should combine type, change and version text");
        var changedCsv = Wim360SnapshotDiff.ToCsv(report, driverChanges);
        Require(changedCsv.Contains("\"oem1.inf\"") &&
                !changedCsv.Contains("\"wifi.inf\"") &&
                !changedCsv.Contains("\"=SUM(1,2).inf\""),
                "Diff CSV must export only filtered changes");
        var allCsv = Wim360SnapshotDiff.ToCsv(report, report.Changes);
        Require(allCsv.Contains("\"'=SUM(1,2).inf\""),
                "Diff CSV must protect against spreadsheet formula evaluation");
        using var exportJson = System.Text.Json.JsonDocument.Parse(
            Wim360SnapshotDiff.ToJson(report, driverChanges));
        Require(exportJson.RootElement.GetProperty("Rows").GetArrayLength() == 1 &&
                exportJson.RootElement.GetProperty("Warnings").ValueKind ==
                System.Text.Json.JsonValueKind.Array,
                "Diff JSON must include only filtered changes and warnings");

        var uncertain = Wim360SnapshotDiff.Compare(filtered, current);
        Require(!uncertain.CanDetectMembershipChanges &&
                uncertain.AddedCount == 0 && uncertain.RemovedCount == 0 &&
                uncertain.SuppressedMembershipCount > 0 &&
                uncertain.ChangedCount == 1,
                "Filtered baseline must suppress unverifiable membership differences");

        var partialAudit = Wim360Servicing.Summarize(@"C:\images\install.wim", 2,
            @"C:\Mounted", newDrivers, newPackages,
            newComponents.Where(x => x.Name != "SMB1Protocol").ToArray(),
            "Feature servicing API is not supported", "");
        var partial = Wim360SnapshotDiff.ParseSnapshotJson(Wim360Catalog.ToJson(
            partialAudit, Wim360Catalog.BuildRows(partialAudit)));
        var partialReport = Wim360SnapshotDiff.Compare(current, partial);
        Require(partial.IsCompleteInventory && partialReport.RemovedCount == 0 &&
                partialReport.SuppressedMembershipCount == 1 &&
                partialReport.Warnings.Any(x => x.Contains("feature", StringComparison.OrdinalIgnoreCase)),
                "Partial optional-feature inventories must not report false removals");

        var duplicateAudit = Wim360Servicing.Summarize(@"C:\images\install.wim", 2,
            @"C:\Mounted", newDrivers.Append(newDrivers[0]), newPackages, newComponents);
        var duplicateReport = Wim360SnapshotDiff.Compare(current,
            Wim360SnapshotDiff.ParseSnapshotJson(
                Wim360Catalog.ToJson(duplicateAudit, Wim360Catalog.BuildRows(duplicateAudit))));
        Require(duplicateReport.AmbiguousCount == 1 && duplicateReport.Warnings.Count > 0,
                "Repeated driver identities must be flagged rather than silently matched");

        bool malformedRejected = false;
        try { Wim360SnapshotDiff.ParseSnapshotJson("{\"SchemaVersion\":99}"); }
        catch (System.IO.InvalidDataException) { malformedRejected = true; }
        Require(malformedRejected, "Unrecognized JSON schema must be rejected");

        Console.WriteLine("PASS snapshot comparison engine, legacy/filtered protections, partial features and exports");
        return report;
    }

    private static void VerifySnapshotCompareWindow(
        Form owner, Wim360SnapshotDiff.DiffReport report)
    {
        using var window = new Wim360CompareForm();
        window.Show(owner);
        Application.DoEvents();
        var baselineField = Find(window, "txt360Baseline") as TextBox;
        var currentField = Find(window, "txt360Current") as TextBox;
        var runButton = Find(window, "btn360CompareRun") as Button;
        var grid = Find(window, "grid360Changes") as DataGridView;
        var change = Find(window, "cmb360CompareChange") as ComboBox;
        var type = Find(window, "cmb360CompareType") as ComboBox;
        var search = Find(window, "txt360CompareSearch") as TextBox;
        var csv = Find(window, "btn360CompareCsv") as Button;
        var json = Find(window, "btn360CompareJson") as Button;
        var close = Find(window, "btn360CompareClose") as Button;
        Require(baselineField is not null && currentField is not null &&
                runButton is not null && grid is not null && change is not null &&
                type is not null && search is not null && csv is not null &&
                json is not null && close is not null,
                "WIM 360 snapshot comparison form must expose files, filters and exports");
        Require(!csv!.Enabled && !json!.Enabled &&
                grid!.VirtualMode && grid.RowCount == 0,
                "Snapshot compare must not export before a comparison");
        NoOverlap(baselineField!, currentField!, "Baseline / Current JSON inputs");
        NoOverlap(change!, type!, "Change and type filters");
        NoOverlap(type, search!, "Type and free-text search");
        NoOverlap(csv, json, "Comparison CSV / JSON buttons");
        NoOverlap(json, close!, "Comparison JSON / Close buttons");

        window.PreviewComparisonForTest(report);
        Application.DoEvents();
        Require(grid.RowCount == 7 && csv.Enabled && json.Enabled,
                "Loaded comparison must show changes and enable export");
        change!.SelectedItem = "Changed";
        Application.DoEvents();
        Require(grid.RowCount == 3, "Changed filter should show exactly three changes");
        type!.SelectedItem = "Drivers";
        Application.DoEvents();
        Require(grid.RowCount == 1, "Combined type and change filters did not apply");
        search!.Text = "not-a-driver";
        Application.DoEvents();
        Require(grid.RowCount == 0, "Diff search must filter out missing records");
        search.Clear();
        change.SelectedIndex = 0;
        type.SelectedIndex = 0;
        Application.DoEvents();
        Require(grid.RowCount == 7, "Clearing filters should restore the full diff");
        Snapshot(window, "DISM-GUI-WIM360-Snapshot-Compare");
        window.Close();
        Application.DoEvents();
        Console.WriteLine("PASS snapshot comparison UI layout and filters");
    }

    private static void VerifyIntegratedTestCenter(Form owner)
    {
        var dpi150 = IntegratedTestChecks.InspectDisplay(144, 1100, 780);
        var dpi200Small = IntegratedTestChecks.InspectDisplay(192, 600, 420);
        Require(dpi150.Status == "PASS" && dpi150.Details.Contains("150%") &&
                dpi200Small.Status == "WARN", "DPI diagnostics must flag small layouts");
        var records = new[] {
            new IntegratedTestChecks.Result { Status = "PASS", Name = "DISM binary",
                                                Details = "Available" },
            new IntegratedTestChecks.Result { Status = "WARN", Name = "DPI / layout",
                                                Details = "200% scale needs visual check" },
            new IntegratedTestChecks.Result { Status = "FAIL", Name = "SWM parts",
                                                Details = @"C:\Users\private-user\install.swm" }
        };
        var report = IntegratedTestChecks.FormatReport(records);
        Require(report.Contains("PASS: 1") && report.Contains("WARN: 1") &&
                report.Contains("FAIL: 1") && !report.Contains("private-user"),
                "Test Center must count outcomes and redact private file paths");

        using var window = new IntegratedTestCenterForm();
        window.Show(owner);
        Application.DoEvents();
        var grid = Find(window, "gridTestCenterResults") as DataGridView;
        var browse = Find(window, "btnTestCenterBrowse") as Button;
        var image = Find(window, "txtTestCenterImage") as TextBox;
        var optional = Find(window, "chkTestCenterAuditMounted") as CheckBox;
        var run = Find(window, "btnTestCenterRun") as Button;
        var copy = Find(window, "btnTestCenterCopy") as Button;
        var save = Find(window, "btnTestCenterSave") as Button;
        var support = Find(window, "btnTestCenterSupport") as Button;
        var cancel = Find(window, "btnTestCenterCancel") as Button;
        Require(grid is not null && browse is not null && image is not null &&
                optional is not null && run is not null && copy is not null &&
                save is not null && support is not null && cancel is not null,
                "Integrated Test Center must have all checking/reporting controls");
        Require(!optional!.Checked && run!.Enabled &&
                !copy!.Enabled && !save!.Enabled && !support!.Enabled,
                "Mounted audit must be opt-in and reports disabled until checks complete");
        NoOverlap(image!, browse!, "Test Center image path and Browse");
        NoOverlap(copy, save, "Test Center Copy and Save");
        NoOverlap(save, support, "Test Center Save and Support");
        NoOverlap(support, cancel!, "Test Center Support and Close");
        window.PreviewForTest(records);
        Application.DoEvents();
        Require(grid!.Rows.Count == 3 && copy.Enabled && save.Enabled && support.Enabled,
                "Test Center must display results and activate local report controls");
        Snapshot(window, "DISM-GUI-Integrated-Test-Center");
        window.Close();
        Application.DoEvents();
        Console.WriteLine("PASS integrated Test Center controls, redaction and DPI diagnostics");
    }

    private static void VerifyNativeWimReader()
    {
        var root = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
            "DISMGUI-WIM360-" + Guid.NewGuid().ToString("N"));
        var source = System.IO.Path.Combine(root, "source");
        System.IO.Directory.CreateDirectory(source);
        System.IO.Directory.CreateDirectory(System.IO.Path.Combine(source, "Windows", "System32"));
        try
        {
            System.IO.File.WriteAllText(
                System.IO.Path.Combine(source, "Windows", "System32", "sentinel.txt"),
                "This is a disposable test folder, NOT a Windows installation.");
            System.IO.File.WriteAllText(
                System.IO.Path.Combine(source, "smoke.txt"),
                "DISM GUI WIM 360 read-only catalog smoke test");
            var payload = new byte[9 * 1024 * 1024];
            System.Security.Cryptography.RandomNumberGenerator.Fill(payload);
            System.IO.File.WriteAllBytes(System.IO.Path.Combine(source, "payload.bin"), payload);
            var image = System.IO.Path.Combine(root, "tiny.wim");
            var start = new System.Diagnostics.ProcessStartInfo("dism.exe")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            start.ArgumentList.Add("/Capture-Image");
            start.ArgumentList.Add("/ImageFile:" + image);
            start.ArgumentList.Add("/CaptureDir:" + source);
            start.ArgumentList.Add("/Name:WIM360 CI");
            start.ArgumentList.Add("/Compress:fast");

            using var capture = new System.Diagnostics.Process { StartInfo = start };
            Require(capture.Start(), "Could not launch Windows DISM to create the fixture");
            var output = capture.StandardOutput.ReadToEndAsync();
            var errors = capture.StandardError.ReadToEndAsync();
            if (!capture.WaitForExit(120000))
            {
                capture.Kill(entireProcessTree: true);
                throw new InvalidOperationException("DISM WIM capture timed out");
            }
            var capturedOutput = output.GetAwaiter().GetResult();
            var capturedError = errors.GetAwaiter().GetResult();
            Require(capture.ExitCode == 0 && System.IO.File.Exists(image),
                "Windows DISM could not create a one-file WIM fixture: " +
                capturedOutput[^Math.Min(1800, capturedOutput.Length)..] +
                " " + capturedError);

            var inventory = Wim360Reader.Inspect(image);
            Require(inventory.ImageCount == 1 &&
                    inventory.Images[0].Name == "WIM360 CI",
                    "WIMGAPI metadata-query did not retrieve the actual WIM index");
            Require(inventory.FileSizeBytes > 512 &&
                    inventory.Images[0].Index == 1,
                    "WIM 360 returned incorrect catalog dimensions");
            Require(inventory.Images[0].MountStatus is "Not mounted" or "Not checked",
                    "WIM 360 should not mount or modify the inspected WIM");
            Require(System.IO.File.ReadAllText(System.IO.Path.Combine(
                    source, "smoke.txt")).Contains("read-only catalog"),
                    "WIM metadata inspection modified the source fixture");
            Console.WriteLine("PASS native WIMGAPI read-only WIM catalog: " +
                inventory.Images[0].Name + ", " + inventory.FileSizeBytes + " bytes");
            WimMountIntegration.Verify(image, root);
            WimMountIntegration.VerifySplit(image, root);
        }
        finally
        {
            try { System.IO.Directory.Delete(root, recursive: true); }
            catch (Exception e) {
                Console.WriteLine("Nonfatal test cleanup warning: " + e.Message);
            }
        }
    }

    private static (DriverStudioEngine.DriverPackage Source,
                    DriverStudioEngine.DriverPackage Installed) VerifyDriverStudioEngine()
    {
        var temp = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
            "DriverStudioSmoke-" + Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(temp);
        try
        {
            var driver = System.IO.Path.Combine(temp, "contoso.inf");
            System.IO.File.WriteAllText(driver, string.Join("\r\n", new[] {
                "[Version]",
                "Signature=\"$Windows NT$\"",
                "Class=Net",
                "Provider=%Contoso%",
                "DriverVer=11/03/2026,2.1.4.0",
                "CatalogFile=contoso.cat",
                "",
                "[Manufacturer]",
                "%Contoso%=ContosoModels,NTamd64",
                "[ContosoModels.NTamd64]",
                "%Device%=Install,PCI\\VEN_1234&DEV_4567",
                "",
                "[Strings]",
                "Contoso=\"Contoso Corporation\"",
                "Device=\"Contoso Ethernet\"",
                ""
            }));
            var source = DriverStudioEngine.ReadInf(driver);
            Require(source.InfName == "contoso.inf" &&
                    source.Version == "2.1.4.0" &&
                    source.Provider == "Contoso Corporation" &&
                    source.ClassName == "Net" &&
                    source.DriverDate == "11/03/2026" &&
                    source.HardwareIds.Any(x => x.Contains("VEN_1234")) &&
                    source.Sha256.Length == 64 &&
                    string.IsNullOrEmpty(source.ValidationError),
                    "INF parser lost DriverVer, strings, provider, class, hardware IDs or hash");

            var json = "[{\"InfName\":\"contoso.inf\",\"PublishedName\":\"oem8.inf\"," +
                "\"Provider\":\"Contoso Corporation\",\"ClassName\":\"Net\"," +
                "\"Version\":\"2.1.4.0\",\"Date\":\"2026-11-03\",\"Inbox\":false}]";
            var inventory = DriverStudioInventory.ParseInventoryJson(json);
            Require(inventory.Count == 1 &&
                    inventory[0].PublishedName == "oem8.inf" &&
                    !inventory[0].IsInbox,
                    "Driver Studio structured installed-driver inventory parse failed");
            var same = DriverStudioEngine.CompareCandidates(new[] { source }, inventory, true);
            Require(same[0].Status == "Same version" && !same[0].Selected,
                    "Same version and date must be recognized and skipped by default");
            var unverified = DriverStudioEngine.CompareCandidates(
                new[] { source }, Array.Empty<DriverStudioEngine.DriverPackage>(), false);
            Require(unverified[0].Status == "Not checked" && !unverified[0].Selected,
                    "Missing mount inventory must NOT be treated as no installed driver");
            var duplicate = DriverStudioEngine.CompareCandidates(
                new[] { source, source }, inventory, true);
            Require(duplicate[1].Status == "Duplicate source" && !duplicate[1].Selected,
                    "Duplicate source INF should be skipped");
            var newer = new DriverStudioEngine.DriverPackage {
                InfName=source.InfName, FilePath=source.FilePath,
                Provider=source.Provider, ClassName=source.ClassName,
                Version="2.2.0.0", DriverDate="11/04/2026" };
            var comparisonNewer = DriverStudioEngine.CompareCandidates(
                new[] { newer }, inventory, true);
            Require(comparisonNewer[0].Status == "Newer" && comparisonNewer[0].Selected,
                    "Newer driver should be selected by default");
            var newerWithOlderDate = new DriverStudioEngine.DriverPackage {
                InfName=source.InfName, FilePath=source.FilePath,
                Provider=source.Provider, ClassName=source.ClassName,
                Version="2.5.0.0", DriverDate="10/01/2026" };
            var ranked = DriverStudioEngine.CompareCandidates(
                new[] { newerWithOlderDate }, inventory, true);
            Require(ranked[0].Status == "Review" && !ranked[0].Selected,
                    "A newer version with older DriverVer date requires review");
            var older = new DriverStudioEngine.DriverPackage {
                InfName=source.InfName, FilePath=source.FilePath,
                Provider=source.Provider, ClassName=source.ClassName,
                Version="2.0.1.0", DriverDate="10/01/2026" };
            var comparisonOlder = DriverStudioEngine.CompareCandidates(
                new[] { older }, inventory, true);
            Require(comparisonOlder[0].Status == "Older" && !comparisonOlder[0].Selected,
                    "Older driver should not be selected");
            var fresh = new DriverStudioEngine.DriverPackage {
                InfName="different.inf", Provider="Contoso", ClassName="Display",
                Version="1.0.0.0", DriverDate="11/04/2026" };
            var comparisonNew = DriverStudioEngine.CompareCandidates(
                new[] { fresh }, inventory, true);
            Require(comparisonNew[0].Status == "New" && comparisonNew[0].Selected,
                    "Missing driver identity in verified image must be shown as New");
            var invalid = new DriverStudioEngine.DriverPackage {
                InfName="missing.inf", ValidationError="No DriverVer" };
            Require(DriverStudioEngine.CompareCandidates(
                new[] { invalid }, inventory, true)[0].Status == "Review",
                "Incomplete INF metadata must not silently count as New");
            var scanned = DriverStudioEngine.ScanSource(temp, true);
            Require(scanned.Count == 1 && scanned[0].InfName == "contoso.inf",
                    "Recursive driver-folder scanner failed");

            // Pure selective-install preflight: no live DISM writes in CI.
            var newDecision = DriverStudioEngine.CompareCandidates(
                new[] { source }, Array.Empty<DriverStudioEngine.DriverPackage>(), true);
            Require(newDecision[0].Status == "New" &&
                    DriverStudioInstaller.IsAllowed(newDecision[0]) &&
                    !DriverStudioInstaller.IsAllowed(same[0]) &&
                    !DriverStudioInstaller.IsAllowed(unverified[0]) &&
                    !DriverStudioInstaller.IsAllowed(comparisonOlder[0]),
                    "Selective import must permit only verified New/Newer decisions");
            var safePlan = DriverStudioInstaller.ValidatePlan(
                newDecision, new[] { source }, Array.Empty<DriverStudioEngine.DriverPackage>());
            Require(safePlan.Count == 1 && safePlan[0].Sha256 == source.Sha256,
                    "Selected single-INF import plan did not retain the reviewed hash");

            bool staleBlocked = false;
            try {
                DriverStudioInstaller.ValidatePlan(newDecision, new[] { source }, inventory);
            } catch (InvalidOperationException) { staleBlocked = true; }
            Require(staleBlocked,
                "The installer must block a stale comparison after inventory changed");
            var tampered = new DriverStudioEngine.DriverPackage {
                FilePath=source.FilePath, InfName=source.InfName, Provider=source.Provider,
                ClassName=source.ClassName, Version=source.Version,
                DriverDate=source.DriverDate, Sha256=new string('0', 64) };
            bool hashBlocked = false;
            try {
                DriverStudioInstaller.ValidatePlan(newDecision, new[] { tampered },
                    Array.Empty<DriverStudioEngine.DriverPackage>());
            } catch (InvalidOperationException) { hashBlocked = true; }
            Require(hashBlocked, "The installer must reject an INF changed since scanning");

            var args = DriverStudioInstaller.BuildArguments(
                @"C:\mount dir", @"C:\drivers\new driver.inf");
            Require(args.Count == 3 && args[0] == @"/Image:C:\mount dir" &&
                    args[1] == "/Add-Driver" &&
                    args[2] == @"/Driver:C:\drivers\new driver.inf" &&
                    !string.Join(" ", args).Contains("Recurse") &&
                    !string.Join(" ", args).Contains("ForceUnsigned") &&
                    !string.Join(" ", args).Contains("Commit") &&
                    !string.Join(" ", args).Contains("Discard"),
                    "Selective install must build a single INF command without unsafe flags");
            Console.WriteLine("PASS selective import preflight, stale inventory/hash rejection and safe DISM args");
            Console.WriteLine("PASS Driver Studio INF parsing, duplicates, versions and inventory safety");
            return (source, inventory[0]);
        }
        finally
        {
            try { System.IO.Directory.Delete(temp, true); }
            catch { /* Optional test cleanup */ }
        }
    }

    [STAThread]
    private static int Main()
    {
        try
        {
            Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            using var form = new frmMain();
            form.Show();
            Application.DoEvents();
            var appVersion = typeof(ModernTheme).Assembly.GetName().Version!.ToString(3);
            Require(form.Text.StartsWith($"DISM GUI {appVersion}", StringComparison.Ordinal),
                    "Main title must display actual EXE version without v prefix");
            var tabs = form.Controls.Find("TabControl1", true)
                .OfType<TabControl>().FirstOrDefault();
            Require(tabs is not null, "Original DISM TabControl was not preserved");
            tabs!.SelectedIndex = 0;
            Application.DoEvents();
            VerifyWimMetadata();
            VerifyMountedAudit();
            VerifyRuntimeDiagnostics();
            VerifyErrorDiagnostics();
            VerifyIntegratedTestCenter(form);
            var detailFixture = VerifyDetailedInventory();
            var snapshotReport = VerifySnapshotDiff(detailFixture);
            VerifyNativeWimReader();
            var driverFixture = VerifyDriverStudioEngine();
            Require(tabs.TabPages.Count == 11 &&
                    tabs.TabPages[9].Name == "TabPageWim360" &&
                    tabs.TabPages[10].Name == "TabPageDriverStudio",
                    "Original nine tabs plus WIM 360 and Driver Studio workspaces must remain intact");
            var wimNav = Find(form, "navWim360") as Button;
            Require(wimNav is not null && wimNav.Text.Contains("WIM 360"),
                    "WIM 360 entry must be visible in top navigation");
            var dashboard = Find(tabs.TabPages[9], "Wim360Dashboard") as Wim360Dashboard;
            Require(dashboard is not null &&
                    Find(dashboard, "Wim360Layout") is TableLayoutPanel &&
                    Find(dashboard, "grid360Indexes") is DataGridView,
                    "WIM 360 dashboard controls must exist in the main window");
            var wimPathField = Find(dashboard!, "txt360Path") as TextBox;
            Require(wimPathField is not null, "WIM 360 file chooser missing");
            wimPathField!.Text = @"C:\test\boot.wim";
            dashboard!.PreviewMetadataForTest(WIM_METADATA_FIXTURE);
            var indexGrid = Find(dashboard, "grid360Indexes") as DataGridView;
            Require(indexGrid!.Rows.Count == 2,
                    "WIM 360 should display both indexes from the fixture");
            var auditWimButton = Find(dashboard, "btn360AuditMounted") as Button;
            Require(auditWimButton is not null && !auditWimButton.Enabled,
                    "Mounted audit must be disabled for an unmounted WIM index");
            var inventoryButton = Find(dashboard, "btn360Inventory") as Button;
            Require(inventoryButton is not null && !inventoryButton.Enabled,
                    "WIM 360 inventory detail action must require a finished audit");
            var selectedDetails = Find(dashboard, "txt360Details") as TextBox;
            Require(selectedDetails!.Text.Contains("WindowsPE") &&
                    selectedDetails.Text.Contains("10.0.26100"),
                    "WIM 360 detail panel missing the selected index metadata");
            tabs.SelectedIndex = 9;
            Application.DoEvents();
            var selectedWimButton = Find(dashboard, "btn360UseInMount") as Button;
            var reportField = typeof(Wim360Dashboard).GetField("report",
                System.Reflection.BindingFlags.NonPublic |
                System.Reflection.BindingFlags.Instance);
            var currentReport = reportField?.GetValue(dashboard) as Wim360Reader.WimReport;
            Console.WriteLine("WIM 360 handoff diagnostics: " +
                $"dashboardVisible={dashboard.Visible}; " +
                $"dashboardEnabled={dashboard.Enabled}; " +
                $"buttonEnabled={selectedWimButton?.Enabled}; " +
                $"rows={indexGrid.Rows.Count}; selected={indexGrid.SelectedRows.Count}; " +
                $"current={indexGrid.CurrentRow?.Cells[0].Value}; " +
                $"fieldPath='{wimPathField.Text}'; " +
                $"reportPath='{currentReport?.ImageFile}'; " +
                $"reportCount={currentReport?.Images.Count}; " +
                $"pathEquals={string.Equals(wimPathField.Text.Trim(), currentReport?.ImageFile,
                    StringComparison.OrdinalIgnoreCase)}");
            Require(selectedWimButton is not null && selectedWimButton.Enabled,
                    "WIM 360 Use in Mount action should be available after metadata load");
            Require(currentReport is not null, "WIM 360 report fixture missing");
            currentReport!.Images[0].MountStatus = "Mounted (read-only)";
            currentReport.Images[0].MountFolder = @"C:\MountAuditFixture";
            indexGrid.Rows[0].Selected = true;
            dashboard.ActivateFromMount();
            Application.DoEvents();
            Require(auditWimButton.Enabled,
                    "A read-only mounted index must enable Scan mounted index");
            detailFixture.ImageFile = @"C:\test\boot.wim";
            detailFixture.Index = 1;
            detailFixture.MountFolder = @"C:\MountAuditFixture";
            currentReport.Images[0].ServicingAudit = detailFixture;
            dashboard.ActivateFromMount();
            Application.DoEvents();
            Require(inventoryButton!.Enabled &&
                    selectedDetails.Text.Contains("Optional features: 2"),
                    "Finished audit must enable details and display component summary");
            VerifyCatalogWindow(form, detailFixture);
            var compareSnapshotButton = Find(dashboard, "btn360CompareSnapshots") as Button;
            Require(compareSnapshotButton is not null && compareSnapshotButton.Enabled,
                    "WIM 360 must expose offline snapshot comparison even without an active audit");
            NoOverlap(auditWimButton!, inventoryButton!, "WIM 360 audit and inventory buttons");
            NoOverlap(compareSnapshotButton!, auditWimButton, "WIM 360 compare and audit buttons");
            VerifySnapshotCompareWindow(form, snapshotReport);
            indexGrid.Rows[1].Selected = true;
            Application.DoEvents();
            selectedWimButton.PerformClick();
            Application.DoEvents();
            Require(tabs.SelectedIndex == 0 &&
                    ((TextBox)Find(tabs.TabPages[0], "txtWIM")!).Text == @"C:\test\boot.wim" &&
                    ((NumericUpDown)Find(tabs.TabPages[0], "nudImageIndex")!).Value == 2,
                    "WIM 360 selected index must populate the visible Mount selector without mounting");
            ((TextBox)Find(tabs.TabPages[0], "txtWIM")!).Text = "";
            wimPathField.Text = "";
            tabs.SelectedIndex = 0;
            Application.DoEvents();
            Console.WriteLine("PASS WIM 360 embedded UI, two indexes and safe Mount handoff");

            var studioNav = Find(form, "navDriverStudio") as Button;
            var studioPanel = Find(tabs.TabPages[10], "DriverStudioPanel") as DriverStudioPanel;
            Require(studioNav is not null && studioPanel is not null &&
                    Find(studioPanel, "gridStudioDrivers") is DataGridView &&
                    Find(studioPanel, "btnStudioCompare") is Button &&
                    Find(studioPanel, "btnStudioScan") is Button &&
                    Find(studioPanel, "btnStudioCopyPlan") is Button,
                    "Driver Studio navigation and scan/compare preview must exist");
            tabs.SelectedIndex = 10;
            Application.DoEvents();
            var studioPreview = DriverStudioEngine.CompareCandidates(
                new[] { driverFixture.Source }, new[] { driverFixture.Installed }, true);
            studioPanel!.PreviewForTest(studioPreview);
            Application.DoEvents();
            var studioGrid = (DataGridView)Find(studioPanel, "gridStudioDrivers")!;
            Require(studioGrid.Rows.Count == 1 &&
                    Convert.ToString(studioGrid.Rows[0].Cells[4].Value) == "Same version" &&
                    (bool)studioGrid.Rows[0].Cells[0].Value! == false,
                    "Driver Studio preview must show existing driver version not pre-selected");
            Require(((Button)Find(studioPanel, "btnStudioCopyPlan")!).Enabled,
                    "Driver Studio preview should offer Copy comparison");
            var installStudio = Find(studioPanel, "btnStudioInstallSelected") as Button;
            var stopStudio = Find(studioPanel, "btnStudioStopAfterCurrent") as Button;
            Require(installStudio is not null && stopStudio is not null &&
                    installStudio.Width >= 100 && stopStudio.Width >= 100,
                    "Driver Studio must show readable per-INF install and safe-stop controls");
            Require(!installStudio!.Enabled && !stopStudio!.Enabled &&
                    studioGrid.Rows[0].Cells[0].ReadOnly,
                    "Identical driver versions must not enable import or user override");
            var newStudio = DriverStudioEngine.CompareCandidates(
                new[] { driverFixture.Source },
                Array.Empty<DriverStudioEngine.DriverPackage>(), true);
            studioPanel.PreviewForTest(newStudio);
            Application.DoEvents();
            Require(installStudio.Enabled && !studioGrid.Rows[0].Cells[0].ReadOnly,
                    "Verified New INF must enable selective install");
            var studioSource = Find(studioPanel, "txtStudioSource") as TextBox;
            Require(studioSource is not null, "Driver Studio source path entry missing");
            studioSource!.Text = @"C:\ChangedDrivers";
            Application.DoEvents();
            Require(!installStudio.Enabled,
                    "Changing the driver source must invalidate the install plan");
            studioPanel.PreviewForTest(studioPreview);
            Application.DoEvents();
            var visibleScan = (Button)Find(studioPanel, "btnStudioScan")!;
            var visibleCompare = (Button)Find(studioPanel, "btnStudioCompare")!;
            Require(visibleScan.Width >= 85 && visibleCompare.Width >= 130 &&
                    studioGrid.Height >= 145,
                    "Driver Studio must have readable scan buttons and comparison grid");
            Snapshot(form, "DISM-GUI-Driver-Studio");
            tabs.SelectedIndex = 0;
            Application.DoEvents();
            Console.WriteLine("PASS embedded Driver Studio preview and safe comparison UI");
            var page = tabs.TabPages[0];
            Require(Find(page, "MountResponsiveRoot") is TableLayoutPanel,
                "Mount page is not using the responsive grid");
            var tools = Find(form, "btnToolsMenu") as Button;
            Require(tools is not null && tools.Visible && tools.Width >= 100,
                "Tools must be a readable button, not a menu-strip caption");
            Require(tools!.ContextMenuStrip is not null &&
                    tools.ContextMenuStrip.Items.Cast<ToolStripItem>()
                         .Any(item => item.Text == "About"),
                    "Tools button must contain the original About and DISM commands");
            Require(tools.ContextMenuStrip.Items.Cast<ToolStripItem>()
                    .Any(item => item.Text == "Email Support..." &&
                        item.Name == "mnuEmailSupport"),
                    "Tools dropdown must contain Email Support");
            Require(tools.ContextMenuStrip.Items.Cast<ToolStripItem>()
                    .Any(item => item.Name == "mnuIntegratedTestCenter"),
                    "Tools menu must expose the Integrated Test Center");
            Require(Find(dashboard!, "btn360TestCenter") is Button,
                    "WIM 360 must provide the direct complete-test shortcut");
            var splitter = Find(form, "mainOutputSplitter") as SplitContainer;
            var copyLog = Find(form, "btnCopyOutput") as Button;
            var clearLog = Find(form, "btnClearOutput") as Button;
            var expandLog = Find(form, "btnExpandOutput") as Button;
            var exitApp = Find(form, "btnExit") as Button;
            var logText = Find(form, "txtOutput") as TextBox;
            Require(splitter is not null && copyLog is not null && clearLog is not null &&
                    expandLog is not null && exitApp is not null && logText is not null,
                    "Resizable DISM console and toolbar are missing");
            Require(splitter!.Orientation == Orientation.Horizontal &&
                    splitter.FixedPanel == FixedPanel.Panel2 &&
                    splitter.Panel2MinSize >= 130,
                    "Output pane must have a horizontal draggable splitter with a usable minimum");
            var wim = Find(page, "txtWIM")!;
            var browseWim = Find(page, "btnOpenWIM")!;
            var mount = Find(page, "txtMount")!;
            var browseMount = Find(page, "btnOpenMount")!;
            var index = Find(page, "nudImageIndex") as NumericUpDown;
            var mountAction = Find(page, "btnMount")!;
            var dismountAction = Find(page, "btnDismount")!;
            var info = Find(page, "btnDisplayWIMInfo")!;
            Require(index is not null, "Numeric image index selector missing");
            Require(dismountAction.BackColor != ModernTheme.Red,
                "Dismount action must not be red like an error state");

            foreach (var size in new[] { new Size(900, 650), new Size(1180, 760) })
            {
                var allowed = Screen.PrimaryScreen!.WorkingArea;
                var effective = new Size(Math.Min(size.Width, allowed.Width - 40),
                                         Math.Min(size.Height, allowed.Height - 60));
                form.ClientSize = effective;
                form.PerformLayout();
                Application.DoEvents();
                Require(index!.Width >= 75, $"Numeric image index too narrow at {size}");
                Require(browseWim.Width >= 110 && browseMount.Width >= 110,
                    $"Browse actions too narrow at {size}");
                NoOverlap(wim, browseWim, "WIM text / Browse WIM");
                NoOverlap(mount, browseMount, "Mount path / Browse folder");
                NoOverlap(mountAction, dismountAction, "Mount / Dismount");
                NoOverlap(dismountAction, info, "Dismount / Image info");
                NoOverlap(index, browseWim, "Numeric image index / Choose WIM");
                NoOverlap(copyLog!, clearLog!, "Copy log / Clear");
                NoOverlap(clearLog!, exitApp!, "Clear / Exit");
                NoOverlap(expandLog!, copyLog!, "Expand / Copy log");
                NoOverlap(copyLog!, logText!, "Copy log / DISM output text");
                NoOverlap(clearLog!, logText!, "Clear / DISM output text");
                NoOverlap(exitApp!, logText!, "Exit / DISM output text");
                Require(logText!.Height >= 45 && copyLog!.Height >= 24,
                    $"Output textbox or toolbar clipped at {effective}");
                Require(splitter!.Panel2.Height >= splitter.Panel2MinSize,
                    "DISM output pane below minimum height");
                var heading = form.Controls.Find("btnToolsMenu", true).First();
                Require(heading.RectangleToScreen(heading.ClientRectangle).Right <=
                        form.RectangleToScreen(form.ClientRectangle).Right,
                        "Tools button extends off-window");
                Console.WriteLine($"PASS {size}: header, Tools, Index, Mount and Dismount are non-overlapping");
                Snapshot(form, $"DISM-GUI-{form.ClientSize.Width}x{form.ClientSize.Height}");
                if (size.Width == 900)
                {
                    var prior = splitter!.Panel2.Height;
                    expandLog!.PerformClick();
                    Application.DoEvents();
                    Require(splitter.Panel2.Height > prior + 25,
                        "Expand button did not grow DISM output");
                    Snapshot(form, "DISM-GUI-Output-Expanded");
                    expandLog.PerformClick();
                    Application.DoEvents();
                    Require(Math.Abs(splitter.Panel2.Height - prior) <= 7,
                        "Restore button did not restore previous DISM output height");
                    Console.WriteLine("PASS resizable output: Expand and Restore");
                }
            }

            // Exercise ALL nine sections at both realistic window sizes.
            foreach (var size in new[] { new Size(900, 650), new Size(1180, 760) })
            {
                var allowed = Screen.PrimaryScreen!.WorkingArea;
                form.ClientSize = new Size(Math.Min(size.Width, allowed.Width - 40),
                    Math.Min(size.Height, allowed.Height - 60));
                for (var section = 1; section <= 8; section++)
                {
                    tabs.SelectedIndex = section;
                    Application.DoEvents();
                    ValidateSection(tabs.TabPages[section], section + 1,
                        $"{form.ClientSize.Width}x{form.ClientSize.Height}");
                    Snapshot(form, $"DISM-GUI-Section-{section + 1}-{form.ClientSize.Width}x{form.ClientSize.Height}");
                }
            }
            tabs.SelectedIndex = 9;
            Application.DoEvents();
            wimPathField.Text = @"C:\test\boot.wim";
            dashboard.PreviewMetadataForTest(WIM_METADATA_FIXTURE);
            Snapshot(form, "DISM-GUI-WIM-360");
            tabs.SelectedIndex = 0;
            Application.DoEvents();
            form.Hide();
            form.Dispose();

            using (var about = new frmAbout())
            {
                about.Show();
                Application.DoEvents();
                Require(about.Text == $"About DISM GUI {appVersion}",
                        "About title must show actual version without v prefix");
                var aboutTitle = FindLabel(about, "DISM GUI");
                var aboutSub = FindLabel(about, "Windows Image Toolkit");
                Require(aboutTitle is not null && aboutSub is not null,
                        "About heading labels missing");
                NoOverlap(aboutTitle!, aboutSub!, "About title and subtitle");
                var supportEmailLink = Find(about, "lnkSupportEmail") as LinkLabel;
                var supportEmailButton = Find(about, "btnEmailSupport") as Button;
                var reportIssueButton = Find(about, "btnReportProblem") as Button;
                Require(reportIssueButton is not null &&
                        reportIssueButton.Text == "Report error to GitHub",
                        "About must provide a direct diagnostics and GitHub Issue workflow");
                Require(supportEmailLink is not null && supportEmailButton is not null,
                        "About Support email link and button must exist");
                Require(supportEmailLink!.Text.Contains("sgennadi@hotmail.com") &&
                        supportEmailButton!.Text == "Email Support",
                        "About must display the intended support email address");
                Require(supportEmailButton.Height >= 28 && supportEmailButton.Width >= 120,
                        "Email Support button is too small or clipped");
                var mailto = SupportContact.BuildSupportMailto();
                var decoded = Uri.UnescapeDataString(mailto);
                Require(mailto.StartsWith("mailto:sgennadi@hotmail.com?subject=",
                                         StringComparison.OrdinalIgnoreCase),
                        "Support mailto is addressed incorrectly");
                Require(decoded.Contains("DISM GUI Support - " + appVersion) &&
                        decoded.Contains("DISM GUI version: " + appVersion) &&
                        decoded.Contains("Architecture: ") &&
                        decoded.Contains("Issue description:"),
                        "Support mailto must include subject, version, architecture, and issue prompt");
                Snapshot(about, "DISM-GUI-About");
                about.Hide();
            }

            var updateType = typeof(UpdateManager).GetNestedType("UpdateWindow",
                System.Reflection.BindingFlags.NonPublic);
            Require(updateType is not null, "Update center form type missing");
            using (var updateForm = (Form)Activator.CreateInstance(updateType!, nonPublic: true)!)
            {
                updateForm.Show();
                Application.DoEvents();
                Require(updateForm.Text == $"DISM GUI {appVersion} - Updates",
                        "Update Center title must show actual version without v prefix");
                var notesBox = Find(updateForm, "txtReleaseChangelog") as TextBox;
                var notesHeader = Find(updateForm, "lblChangelogTitle") as Label;
                var notesLink = Find(updateForm, "lnkFullChangelog") as LinkLabel;
                Require(notesBox is not null && notesHeader is not null &&
                        notesLink is not null,
                        "Update Center inline changelog controls are missing");
                Require(notesBox!.ReadOnly && notesBox.Multiline &&
                        notesBox.ScrollBars == ScrollBars.Vertical,
                        "Release changelog must be read-only, multiline and scrollable");
                Require(notesBox.Height >= 72,
                        "Changelog box too short to read at the default window size");
                // Changelog has its own layout row above the footer, never
                // overlaying Update, Reinstall, Releases or Close.
                var boxBounds = notesBox.RectangleToScreen(notesBox.ClientRectangle);
                foreach (var button in AllControls(updateForm).OfType<Button>())
                {
                    var bounds = button.RectangleToScreen(button.ClientRectangle);
                    Require(!boxBounds.IntersectsWith(bounds),
                        $"Changelog overlaps the {button.Text} action");
                }
                var updatesTitle = FindLabel(updateForm, "UPDATES");
                var updatesSub = FindLabel(updateForm,
                    "Stable releases  ·  SHA-256 verified");
                Require(updatesTitle is not null && updatesSub is not null,
                        "Update Center heading labels missing");
                NoOverlap(updatesTitle!, updatesSub!, "Update Center title and subtitle");
                Snapshot(updateForm, "DISM-GUI-Update-Center");
                updateForm.Hide();
            }

            // Markdown is rendered as plain text and preserves the original
            // release descriptions without loading HTML in WinForms.
            var formatter = typeof(UpdateManager).GetMethod(
                "FormatReleaseNotesMarkdown",
                System.Reflection.BindingFlags.NonPublic |
                System.Reflection.BindingFlags.Static);
            Require(formatter is not null, "Release changelog formatter missing");
            var formatted = (string)formatter!.Invoke(null, new object[] {
                "## Changes\n- Fixed controls\nSee [Release](https://github.com/example)"
            })!;
            Require(formatted.Contains("Changes") &&
                    formatted.Contains("• Fixed controls") &&
                    formatted.Contains("See Release") &&
                    !formatted.Contains("](https://"),
                    "Release changelog markdown is not readable");
            Console.WriteLine("PASS About, Update Center and inline changelog layout");
            VerifyWindowGeometry();
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("UI layout regression: " + ex);
            return 1;
        }
    }
}
