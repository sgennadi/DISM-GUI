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
            var scale = Environment.GetEnvironmentVariable("DISMGUI_LAYOUT_FONT_SCALE");
            var prefix = Environment.GetEnvironmentVariable("DISMGUI_LAYOUT_ONLY") == "1" &&
                         !string.IsNullOrWhiteSpace(scale)
                ? "FontScale-" + scale!.Replace(".", "_") + "-" : "";
            snapshot.Save(System.IO.Path.Combine(folder, prefix + name + ".png"),
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

    private static void VerifyReadableButton(Button button, string context)
    {
        if (button.Width < 1 || button.Height < 1 || !button.Visible) return;
        var text = button.Text.Replace("&&", "&").Replace("&", "");
        var measured = TextRenderer.MeasureText(text, button.Font,
            new Size(Math.Max(48, button.ClientSize.Width - 16), 1000),
            TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl);
        Require(measured.Height + 4 <= button.ClientSize.Height,
            $"{context}: button '{button.Text}' is vertically clipped");
    }

    private static void VerifyScrollableDialog(Form form, string context)
    {
        var scrollHost = AllControls(form).OfType<Panel>()
            .FirstOrDefault(p => p.AutoScroll &&
                p.Controls.OfType<TableLayoutPanel>().Any());
        Require(scrollHost is not null,
            context + ": dialog must scroll at high DPI rather than clip controls");
        foreach (var button in AllControls(form).OfType<Button>()
                     .Where(b => b.Visible && b.Width > 0 && b.Height > 0))
            VerifyReadableButton(button, context);
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

    private static void VerifyThreeIndexComparison(Form owner)
    {
        var xml = WIM_METADATA_FIXTURE.Replace("</WIM>",
            "<IMAGE INDEX=\"3\"><NAME>Windows 11 Enterprise</NAME>" +
            "<WINDOWS><ARCH>9</ARCH><EDITIONID>Enterprise</EDITIONID>" +
            "<VERSION><MAJOR>10</MAJOR><MINOR>0</MINOR><BUILD>26100</BUILD></VERSION>" +
            "</WINDOWS></IMAGE></WIM>");
        var report = Wim360Reader.ParseMetadataXml(
            xml, @"C:\Images\install.wim", 128000000);
        report.Images[0].Edition = "Home";
        report.Images[1].Edition = "Pro";
        report.Images[2].Edition = "Enterprise";
        report.Images[0].Architecture = "x64";
        report.Images[1].Architecture = "x64";
        report.Images[2].Architecture = "x64";
        // The offline DISM parser preserves folders (trailing slash) and file
        // paths and must never accept a truncated/failed command as complete.
        var rawList = string.Join("\r\n", new[] {
            "Deployment Image Servicing and Management tool",
            @"\",
            @"\Windows\",
            @"\Windows\System32\",
            @"\Windows\System32\hello.txt",
            "The operation completed successfully.",
            ""
        });
        var parsedPaths = Wim360OfflineList.ParseEnglishListing(rawList, 1);
        Require(parsedPaths.Count == 3 &&
                parsedPaths.Count(x => x.Kind == "Folders") == 2 &&
                parsedPaths.Single(x => x.Kind == "Files").Key ==
                    @"Windows\System32\hello.txt",
                "DISM offline /List-Image parser lost file/folder path information");
        bool partialRejected = false;
        try { Wim360OfflineList.ParseEnglishListing(
            string.Join("\r\n", new[] { @"\Windows\missing.txt", "Error: 2" }), 1); }
        catch (System.IO.InvalidDataException) { partialRejected = true; }
        Require(partialRejected, "Offline WIM compare must refuse incomplete listings");
        var metadata = Wim360IndexComparer.Metadata(report, new[] { 1, 2, 3 });
        Require(metadata.Indexes.Count == 3 && metadata.Rows.Count == 10,
                "Index compare must expose metadata for all three selected indexes");
        Require(metadata.Rows.Single(x => x.Name == "Edition").State == "Different" &&
                metadata.Rows.Single(x => x.Name == "Architecture").State == "Present in all",
                "Index compare must distinguish different and shared metadata");

        var records = new[] {
            new Wim360IndexComparer.Item { Index=1, Kind="Files", Key=@"Windows\System32\common.dll", Value="100 bytes" },
            new Wim360IndexComparer.Item { Index=2, Kind="Files", Key=@"Windows\System32\common.dll", Value="100 bytes" },
            new Wim360IndexComparer.Item { Index=3, Kind="Files", Key=@"Windows\System32\common.dll", Value="100 bytes" },
            new Wim360IndexComparer.Item { Index=3, Kind="Files", Key=@"Windows\System32\unique.dll", Value="300 bytes" },
            new Wim360IndexComparer.Item { Index=1, Kind="Files", Key=@"Windows\System32\missing.dll", Value="200 bytes" },
            new Wim360IndexComparer.Item { Index=2, Kind="Files", Key=@"Windows\System32\missing.dll", Value="200 bytes" },
            new Wim360IndexComparer.Item { Index=1, Kind="Files", Key=@"Windows\System32\changed.dll", Value="42 bytes" },
            new Wim360IndexComparer.Item { Index=2, Kind="Files", Key=@"Windows\System32\changed.dll", Value="46 bytes" },
            new Wim360IndexComparer.Item { Index=3, Kind="Files", Key=@"Windows\System32\changed.dll", Value="42 bytes" },
            new Wim360IndexComparer.Item { Index=1, Kind="Files", Key="=HYPERLINK()", Value="1 bytes" }
        };
        var comparison = Wim360IndexComparer.CompareRecords(report.ImageFile,
            "Mounted file paths / sizes", new[] { 1, 2, 3 }, records);
        Require(comparison.Rows.Count == 5 &&
                comparison.Rows.Single(x => x.Name.EndsWith("unique.dll")).State == "Only in Index 3" &&
                comparison.Rows.Single(x => x.Name.EndsWith("missing.dll")).State == "Missing in some" &&
                comparison.Rows.Single(x => x.Name.EndsWith("changed.dll")).State == "Different" &&
                comparison.Rows.Single(x => x.Name.EndsWith("common.dll")).State == "Present in all",
                "Presence matrix has incorrect missing/unique/different classifications");
        Require(Wim360IndexComparer.Filter(comparison.Rows, "All types",
                    "Missing / only in some", "").Count == 3 &&
                Wim360IndexComparer.Filter(comparison.Rows, "All types",
                    "Only in Index 3", "").Count == 1 &&
                Wim360IndexComparer.Filter(comparison.Rows, "All types",
                    "Missing from Index 3", "").Count == 2 &&
                Wim360IndexComparer.Filter(comparison.Rows, "All types",
                    "Present in Index 3", "").Count == 3 &&
                Wim360IndexComparer.Filter(comparison.Rows, "Files",
                    "Different", "changed.dll").Count == 1,
                "Index comparison filters must combine state, type and search");
        var allPresent = Wim360IndexComparer.Filter(comparison.Rows, "All types",
            "Exists in all", "", 3);
        Require(allPresent.Count == 2 &&
                allPresent.Any(x => x.Name.EndsWith("changed.dll")) &&
                allPresent.Any(x => x.Name.EndsWith("common.dll")),
                "Exists in all must include different-size files still present everywhere");

        // A realistic multi-index catalog must be merged in a single pass.
        // This deliberately yields records lazily instead of materializing
        // a cross-product of all index paths in the test harness.
        var generated = Enumerable.Range(0, 30000).SelectMany(n =>
            new[] { 1, 2, 3 }.Select(index => new Wim360IndexComparer.Item {
                Index = index, Kind = "Files",
                Key = @"Windows\System32\file-" + n.ToString("D6") + ".dll",
                Value = index == 3 && n % 31 == 0 ? "123 bytes" : "120 bytes"
            }));
        var large = Wim360IndexComparer.CompareRecords(
            report.ImageFile, "large synthetic fixture", new[] { 1, 2, 3 }, generated);
        Require(large.Rows.Count == 30000 &&
                large.Rows.Count(x => x.State == "Different") == 968 &&
                large.Rows.Count(x => x.State == "Present in all") == 29032,
                "Large streamed multi-index catalog was not merged correctly");
        var repeated = Wim360IndexComparer.CompareRecords(
            report.ImageFile, "duplicate fixture", new[] { 1, 2, 3 }, new[] {
                new Wim360IndexComparer.Item { Index = 1, Kind="Files", Key="same.inf", Value="Present" },
                new Wim360IndexComparer.Item { Index = 1, Kind="Files", Key="same.inf", Value="Present" },
                new Wim360IndexComparer.Item { Index = 2, Kind="Files", Key="same.inf", Value="Present" },
                new Wim360IndexComparer.Item { Index = 3, Kind="Files", Key="same.inf", Value="Present" }
            });
        Require(repeated.Rows.Single().State == "Ambiguous duplicates",
                "Streamed comparer must not hide ambiguous duplicate identities");

        var folderComparison = Wim360IndexComparer.CompareRecords(report.ImageFile,
            "Mounted file paths / sizes", new[] { 1, 2, 3 }, new[] {
                new Wim360IndexComparer.Item { Index=1, Kind="Folders", Key=@"Windows\Policy", Value="Present" },
                new Wim360IndexComparer.Item { Index=2, Kind="Folders", Key=@"Windows\Policy", Value="Present" },
                new Wim360IndexComparer.Item { Index=3, Kind="Folders", Key=@"Windows\Policy", Value="Present" },
                new Wim360IndexComparer.Item { Index=1, Kind="Folders", Key=@"Windows\Extra", Value="Present" }
            });
        Require(folderComparison.Rows.Count == 2 &&
                folderComparison.Rows.Single(x => x.Name.EndsWith("Extra")).State == "Only in Index 1" &&
                folderComparison.Rows.Single(x => x.Name.EndsWith("Policy")).State == "Present in all" &&
                Wim360IndexComparer.Filter(folderComparison.Rows, "Folders",
                    "Missing from Index 3", "").Count == 1,
                "Folder presence must compare independently of files");

        var csv = Wim360IndexComparer.ToCsv(comparison, comparison.Rows);
        Require(csv.Contains("\"Index 1\",\"Index 2\",\"Index 3\"") &&
                csv.Contains("\"'=HYPERLINK()\"") &&
                csv.Contains("\"—\""),
                "Index comparison CSV missing headers, absent-item markers or formula protection");
        report.Images[0].ServicingAudit = Wim360Servicing.Summarize(
            report.ImageFile, 1, @"C:\Mounted1",
            Array.Empty<DriverStudioEngine.DriverPackage>(),
            new[] { new Wim360Servicing.PackageEntry {
                Name="A-Package", State="Installed", ReleaseType="Update"
            } });
        report.Images[1].ServicingAudit = Wim360Servicing.Summarize(
            report.ImageFile, 2, @"C:\Mounted2",
            Array.Empty<DriverStudioEngine.DriverPackage>(),
            new[] { new Wim360Servicing.PackageEntry {
                Name="A-Package", State="Staged", ReleaseType="Update"
            } });
        report.Images[2].ServicingAudit = Wim360Servicing.Summarize(
            report.ImageFile, 3, @"C:\Mounted3",
            Array.Empty<DriverStudioEngine.DriverPackage>(),
            Array.Empty<Wim360Servicing.PackageEntry>());
        var audited = Wim360IndexComparer.AuditedInventory(report, new[] { 1, 2, 3 });
        Require(audited.Rows.Count == 1 &&
                audited.Rows[0].State == "Missing in some" &&
                audited.Rows[0].At(3) == "—",
                "Audited three-index inventory must show absent packages");
        report.Images[1].ServicingAudit.PackageWarning = "Partial query";
        bool blockedPartial = false;
        try { Wim360IndexComparer.AuditedInventory(report, new[] { 1, 2, 3 }); }
        catch (InvalidOperationException) { blockedPartial = true; }
        Require(blockedPartial, "Partial audits must not produce false missing results");

        using var dialog = new Wim360CompareIndexesForm(report);
        dialog.Show(owner);
        Application.DoEvents();
        VerifyScrollableDialog(dialog, "WIM 360 index comparison");
        var offlineMode = Find(dialog, "cmb360IndexCompareMode") as ComboBox;
        Require(offlineMode is not null && offlineMode.Items.Count == 4 &&
                offlineMode.Items[1]!.ToString()!.Contains("no mounting") &&
                offlineMode.Items[2]!.ToString()!.Contains("mounted"),
                "WIM 360 needs a separate direct offline file listing mode");
        if (Environment.GetEnvironmentVariable("DISMGUI_LAYOUT_ONLY") == "1" &&
            float.TryParse(Environment.GetEnvironmentVariable("DISMGUI_LAYOUT_FONT_SCALE"),
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var scale) &&
            scale > 1.15f)
        {
            var optionsViewport = Find(dialog, "Wim360CompareIndexesViewport") as Panel;
            var optionBounds = offlineMode!.RectangleToScreen(offlineMode.ClientRectangle);
            var viewportBounds = optionsViewport!.RectangleToScreen(optionsViewport.ClientRectangle);
            Require(optionBounds.Left >= viewportBounds.Left - 5 &&
                    optionBounds.Right <= viewportBounds.Right + 5,
                    "Compare Indexes mode must be horizontally visible at high font scaling");
        }
        dialog.PreviewForTest(comparison);
        Application.DoEvents();
        var matrix = Find(dialog, "grid360IndexComparison") as DataGridView;
        var state = Find(dialog, "cmb360IndexCompareState") as ComboBox;
        var action = Find(dialog, "btn360RunIndexCompare") as Button;
        Require(matrix is not null && matrix.VirtualMode &&
                matrix.Columns.Count == 6 && matrix.RowCount == 5 &&
                action is not null && action.Enabled, "Three-index comparison UI not initialized");
        // A scrollable dialog is not sufficient if the actual comparison grid
        // starts below the viewport at 125-175% UI font scaling.
        var matrixViewport = Find(dialog, "Wim360CompareIndexesViewport") as Panel;
        Require(matrixViewport is not null, "Comparison viewport missing");
        var visibleMatrix = Rectangle.Intersect(
            matrix.RectangleToScreen(matrix.ClientRectangle),
            matrixViewport!.RectangleToScreen(matrixViewport.ClientRectangle));
        Require(visibleMatrix.Height >= 118,
            $"WIM 360 comparison grid hidden below the fold: " +
            $"visible={visibleMatrix.Height}px, " +
            $"fontScale={Environment.GetEnvironmentVariable("DISMGUI_LAYOUT_FONT_SCALE")}");
        foreach (var name in new[] { "btn360RunIndexCompare",
                                     "btn360IndexCompareHash",
                                     "btn360IndexCompareCsv",
                                     "btn360IndexCompareClose" })
        {
            var footerButton = Find(dialog, name) as Button;
            Require(footerButton is not null, "WIM comparison footer button missing: " + name);
            var full = footerButton!.RectangleToScreen(footerButton.ClientRectangle);
            var clipped = Rectangle.Intersect(full,
                matrixViewport.RectangleToScreen(matrixViewport.ClientRectangle));
            Require(clipped.Height >= footerButton.Height - 4 &&
                    clipped.Width >= footerButton.Width - 4,
                    $"WIM comparison footer button hidden at simulated DPI: {name}, " +
                    $"visible={clipped.Height}x{clipped.Width}, " +
                    $"required={footerButton.Height}x{footerButton.Width}");
        }
        var thirdIndexRect = matrix.GetColumnDisplayRectangle(5, false);
        Require(thirdIndexRect.Width > 80 &&
                thirdIndexRect.Right <= matrix.ClientSize.Width,
            $"Third WIM index is offscreen in the three-column comparison: " +
            $"right={thirdIndexRect.Right}, width={matrix.ClientSize.Width}, " +
            $"fontScale={Environment.GetEnvironmentVariable("DISMGUI_LAYOUT_FONT_SCALE")}");
        state!.SelectedItem = "Missing / only in some";
        Application.DoEvents();
        Require(matrix.RowCount == 3,
                "Missing filter must include unique and partially absent paths");
        state.SelectedItem = "Missing from Index 3";
        Application.DoEvents();
        Require(matrix.RowCount == 2,
                "Missing-from-index filter must show paths absent in index 3");
        state.SelectedItem = "Present in Index 3";
        Application.DoEvents();
        Require(matrix.RowCount == 3,
                "Present-in-index filter must show only paths found in index 3");
        state.SelectedItem = "All results";
        Application.DoEvents();
        Snapshot(dialog, "DISM-GUI-WIM360-Compare-Three-Indexes");
        dialog.Close();
        Console.WriteLine("PASS WIM 360 three-index metadata, files, inventory filters and CSV");
    }

    private static void VerifyInspectOverview()
    {
        // Three editions from one Windows build, as in install.wim.
        var catalog = new Wim360Reader.WimReport { ImageFile = @"C:\Images\install.wim" };
        catalog.Images.Add(new Wim360Reader.ImageInfo {
            Index = 1, Name = "Windows 11 Home", Edition = "Core",
            Architecture = "x64", Version = "10.0.26100",
            Languages = "en-US, he-IL" });
        catalog.Images.Add(new Wim360Reader.ImageInfo {
            Index = 2, Name = "Windows 11 Home Single Language",
            Edition = "CoreSingleLanguage", Architecture = "x64",
            Version = "10.0.26100", Languages = "he-IL, en-US" });
        catalog.Images.Add(new Wim360Reader.ImageInfo {
            Index = 3, Name = "Windows 11 Pro", Edition = "Professional",
            Architecture = "x64", Version = "10.0.26100",
            Languages = "en-US" });
        var overview = Wim360InspectOverview.Analyze(catalog);
        Require(overview.Text.Contains("3 indexes") &&
                overview.Text.Contains("OS builds: 10.0.26100 in all 3 indexes") &&
                overview.Text.Contains("Core [1]") &&
                overview.Text.Contains("CoreSingleLanguage [2]") &&
                overview.Text.Contains("Professional [3]") &&
                overview.Text.Contains("Languages:") &&
                overview.Text.Contains("Matching metadata does NOT prove identical files") &&
                overview.SuggestedPairs.Count == 3 &&
                overview.SuggestedPairs.Any(x =>
                    x.FirstIndex == 1 && x.SecondIndex == 3 &&
                    x.Reason.Contains("different editions")),
                "Three-index metadata overview or suggested compare pairs are wrong");
        Require(Wim360Reader.FormatReport(catalog).Contains("AUTOMATIC INDEX OVERVIEW") &&
                Wim360Reader.FormatReport(catalog).Contains("Compare 1 vs 3"),
                "Copy report must include the automatic metadata overview");

        // Recommended pair launches the existing multi-index viewer with precisely
        // the suggested two indexes selected, rather than the first three.
        using (var suggestedViewer = new Wim360CompareIndexesForm(catalog, new[] { 1, 3 }))
        {
            var selected = Find(suggestedViewer, "lst360CompareIndexes") as CheckedListBox;
            var checkedIndexes = selected?.CheckedItems.Cast<Wim360Reader.ImageInfo>()
                .Select(x => x.Index).OrderBy(x => x).ToArray();
            Require(checkedIndexes is not null &&
                    checkedIndexes.SequenceEqual(new[] { 1, 3 }),
                "Suggested index pair must preselect exactly the requested indexes");
        }

        // Different build, same edition and architecture: a useful upgrade
        // comparison, without pretending that same-build file bytes match.
        catalog.Images[0].Edition = "Professional";
        catalog.Images[0].Version = "10.0.22621";
        var builds = Wim360InspectOverview.Analyze(catalog);
        Require(builds.Text.Contains("OS builds: 10.0.22621 [1]; 10.0.26100 [2, 3]") &&
                builds.SuggestedPairs.Any(x =>
                    x.FirstIndex == 1 && x.SecondIndex == 3 &&
                    x.Reason.Contains("different builds")) &&
                builds.SuggestedPairs.Any(x =>
                    x.FirstIndex == 2 && x.SecondIndex == 3 &&
                    x.Reason.Contains("different editions")),
                "Overview lost build-versus-edition comparison guidance");

        // Missing metadata must NOT be classified as identical builds.
        catalog.Images[0].Version = "";
        catalog.Images[0].Architecture = "";
        catalog.Images[0].Edition = "";
        var incomplete = Wim360InspectOverview.Analyze(catalog);
        Require(incomplete.Text.Contains("Some metadata is missing") &&
                incomplete.Text.Contains("Not listed") &&
                !incomplete.Text.Contains("OS builds: 10.0.26100 in all"),
                "Overview falsely treated missing metadata as matching");

        // A one-index WIM has no meaningful cross-index suggestion.
        var single = new Wim360Reader.WimReport();
        single.Images.Add(new Wim360Reader.ImageInfo {
            Index = 5, Edition = "WindowsPE", Version = "10.0.26100",
            Architecture = "ARM64" });
        var only = Wim360InspectOverview.Analyze(single);
        Require(only.SuggestedPairs.Count == 0 &&
                only.Text.Contains("Only one index"),
                "Single-index WIM must not invent compare recommendations");

        // Do not mutate source metadata: Inspect is read-only.
        Require(catalog.Images[1].Languages == "he-IL, en-US" &&
                catalog.Images[2].Version == "10.0.26100",
                "Metadata overview unexpectedly modified the WIM report");
        Console.WriteLine("PASS WIM 360 Inspect automatic metadata overview and compare guidance");
    }

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

        // Export index-level metadata directly from unmounted WIM/ESD/SWM.
        var indexCsv = Wim360MetadataExport.ToCsv(report);
        Require(indexCsv.Contains("\"Image file\"") &&
                indexCsv.Contains("\"C:\\Images\\boot.wim\"") &&
                indexCsv.Contains("\"WindowsPE\"") &&
                indexCsv.Contains("\"ARM64\"") &&
                indexCsv.Split('\n').Length == 4,
                "WIM 360 index CSV must include both indexes and headers");
        var metadataJson = Wim360MetadataExport.ToJson(report);
        using (var indexDocument = System.Text.Json.JsonDocument.Parse(metadataJson))
        {
            var root = indexDocument.RootElement;
            Require(root.GetProperty("ExportKind").GetString() == "Wim360IndexMetadata" &&
                    root.GetProperty("ImageCount").GetInt32() == 2 &&
                    root.GetProperty("Indexes").GetArrayLength() == 2 &&
                    root.GetProperty("Indexes")[1].GetProperty("Architecture").GetString() == "ARM64",
                    "Index metadata JSON must remain independent of servicing inventory snapshots");
        }
        report.Images[0].Name = "=1+1";
        Require(Wim360MetadataExport.ToCsv(report).Contains("\"'=1+1\""),
                "WIM index CSV must protect against spreadsheet formula injection");
        report.Images[0].Name = "Microsoft Windows PE";


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
        VerifyInspectOverview();
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

    private static void VerifyUniversalDiagnosticsBridge()
    {
        var payload = UniversalDiagnosticsBridge.BuildCapturePayload(
            "WIM 360", "4.3.10", "Mount failed (87)",
            "Password: PRIVATE_TEST_SECRET\nC:\\Users\\tester\\Private\\image.wim");
        using (var parsed = System.Text.Json.JsonDocument.Parse(payload))
        {
            var root = parsed.RootElement;
            Require(root.GetProperty("project").GetString() == "sgennadi/DISM-GUI" &&
                    root.GetProperty("component").GetString() == "WIM 360" &&
                    root.GetProperty("version").GetString() == "4.3.10" &&
                    root.GetProperty("code").GetString() == "MANUAL" &&
                    root.GetProperty("level").GetString() == "error" &&
                    !root.TryGetProperty("excerpt", out _) &&
                    !payload.Contains("PRIVATE_TEST_SECRET") &&
                    !payload.Contains("tester") &&
                    !payload.Contains("image.wim") &&
                    !payload.Contains("GH_TOKEN"),
                    "UniversalDiagnostics payload must be schema v1 and redact secrets/logs");
        }
        var previous = Environment.GetEnvironmentVariable("UDX_STATE_DIR");
        var temp = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
            "udx-embedded-" + Guid.NewGuid().ToString("N"));
        try
        {
            Environment.SetEnvironmentVariable("UDX_STATE_DIR", temp);
            var result = UniversalDiagnosticsBridge.CaptureAsync(payload)
                .GetAwaiter().GetResult();
            Require(result.Success && Guid.TryParse(result.ReportId, out _) &&
                    result.Message.Contains("local diagnostics queue") &&
                    result.Message.Contains("Nothing was sent"),
                "Embedded UniversalDiagnostics local capture failed");

            var projectFolder = UniversalDiagnosticsEmbedded.QueueDirectory(
                "sgennadi/DISM-GUI");
            var path = System.IO.Path.Combine(projectFolder,
                result.ReportId + ".json");
            Require(System.IO.File.Exists(path),
                "Embedded diagnostic event was not stored in the expected queue");
            var raw = System.IO.File.ReadAllText(path);
            using (var record = System.Text.Json.JsonDocument.Parse(raw))
            {
                var root = record.RootElement;
                var ev = root.GetProperty("event");
                Require(ev.GetProperty("schema").GetString() == "universal-diagnostics/v1" &&
                        ev.GetProperty("id").GetString() == result.ReportId &&
                        ev.GetProperty("project").GetString() == "sgennadi/DISM-GUI" &&
                        ev.GetProperty("version").GetString() == "4.3.10" &&
                        ev.GetProperty("fingerprint").GetString()!.Length == 64 &&
                        root.GetProperty("delivery").GetProperty("status").GetString() == "pending" &&
                        root.GetProperty("delivery").GetProperty("url").GetString() == "" &&
                        !ev.TryGetProperty("excerpt", out _),
                    "Embedded queue record does not match universal-diagnostics/v1");
            }
            Require(!raw.Contains("PRIVATE_TEST_SECRET") &&
                    !raw.Contains("tester") &&
                    !raw.Contains("image.wim") &&
                    !raw.Contains("GH_TOKEN"),
                "Embedded queue leaked a private identifier");
            var extra = """{"project":"sgennadi/DISM-GUI","component":"Diagnostics","summary":"Password: SUPER_SECRET_ABC\nC:\\Users\\tester\\private.txt"}""";
            var secondary = UniversalDiagnosticsEmbedded.Capture(extra);
            var secondaryRaw = System.IO.File.ReadAllText(secondary.Path);
            Require(!secondaryRaw.Contains("SUPER_SECRET_ABC") &&
                    !secondaryRaw.Contains("tester") &&
                    !secondaryRaw.Contains("private.txt"),
                "Canonical embedded core must redact untrusted summaries");
            Require(System.IO.Directory.GetFiles(projectFolder, "*.json").Length == 2,
                "Exactly two embedded test reports should be stored, without helper EXE");
        }
        finally
        {
            Environment.SetEnvironmentVariable("UDX_STATE_DIR", previous);
            try { System.IO.Directory.Delete(temp, recursive: true); }
            catch { /* Test files are disposable. */ }
        }
        Console.WriteLine("PASS embedded UniversalDiagnostics v1 queue, schema and redaction. No helper EXE.");

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
        VerifyScrollableDialog(dialog, "GitHub diagnostics");
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
        var queue = Find(dialog, "btnSupportUniversalQueue") as Button;
        var close = Find(dialog, "btnSupportClose") as Button;
        Require(include is not null && reviewed is not null && preview is not null &&
                history is not null && openHistory is not null && clearHistory is not null &&
                title is not null && description is not null &&
                copy is not null && draft is not null && queue is not null &&
                close is not null,
                "Error report UI is missing required preview, consent or export controls");
        Require(preview!.WordWrap && preview.ScrollBars == ScrollBars.Vertical,
                "Diagnostics preview must wrap long report lines without requiring horizontal scrolling");
        Require(!include!.Checked && !history!.Checked &&
                !reviewed!.Checked && !draft!.Enabled && !queue!.Enabled,
                "Diagnostic logs must opt-in and GitHub action must require review");
        NoOverlap(openHistory!, clearHistory!, "Open / Clear diagnostic logs");
        NoOverlap(clearHistory, copy!, "Clear diagnostic logs / Copy");
        NoOverlap(copy, draft, "Diagnostics Copy / GitHub draft actions");
        NoOverlap(queue, draft, "Universal queue / GitHub draft actions");
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
        VerifyScrollableDialog(window, "WIM 360 inventory");
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
        VerifyScrollableDialog(window, "WIM 360 snapshot comparison");
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
        VerifyScrollableDialog(window, "Integrated Test Center");
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
            WimMountIntegration.ProbeOfflineList(image);
            WimMountIntegration.VerifyOfflineThreeIndexes(root);
            WimMountIntegration.VerifyLargeThreeIndexWim(root);
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
            var testScale = Environment.GetEnvironmentVariable("DISMGUI_LAYOUT_FONT_SCALE");
            if (Environment.GetEnvironmentVariable("DISMGUI_LAYOUT_ONLY") == "1")
                Console.WriteLine("SIMULATED DPI FONT SCALE " + testScale +
                    " (not a substitute for a physical Windows DPI session)");
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
            VerifyThreeIndexComparison(form);
            VerifyMountedAudit();
            VerifyRuntimeDiagnostics();
            VerifyUniversalDiagnosticsBridge();
            VerifyErrorDiagnostics();
            VerifyIntegratedTestCenter(form);
            var detailFixture = VerifyDetailedInventory();
            var snapshotReport = VerifySnapshotDiff(detailFixture);
            if (Environment.GetEnvironmentVariable("DISMGUI_LAYOUT_ONLY") != "1")
                VerifyNativeWimReader();
            else
                Console.WriteLine("SIMULATED DPI LAYOUT: skipped real WIM capture/mount tests.");
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
            var overviewText = Find(dashboard, "txt360Overview") as TextBox;
            var recommendedButton = Find(dashboard, "btn360CompareSuggested") as Button;
            var recommendedLabel = Find(dashboard, "lbl360SuggestedPair") as Label;
            Require(recommendedButton is not null && recommendedButton.Enabled &&
                    recommendedLabel is not null &&
                    recommendedLabel.Text.StartsWith("Recommended: ") &&
                    recommendedLabel.AutoEllipsis,
                    "Inspect should expose a readable, enabled recommended-pair action");
            Require(overviewText is not null && overviewText.ReadOnly &&
                    overviewText.Multiline && overviewText.ScrollBars == ScrollBars.Vertical &&
                    overviewText.Text.Contains("2 indexes") &&
                    overviewText.Text.Contains("OS builds: 10.0.26100 in all 2 indexes") &&
                    overviewText.Text.Contains("Compare 1 vs 2"),
                    "Inspect must display a readable, immediate cross-index metadata overview");
            Require(indexGrid!.Rows.Count == 2,
                    "WIM 360 should display both indexes from the fixture");
            var auditWimButton = Find(dashboard, "btn360AuditMounted") as Button;
            Require(auditWimButton is not null && !auditWimButton.Enabled,
                    "Mounted audit must be disabled for an unmounted WIM index");
            var inventoryButton = Find(dashboard, "btn360Inventory") as Button;
            Require(inventoryButton is not null && !inventoryButton.Enabled,
                    "WIM 360 inventory detail action must require a finished audit");
            var indexCsvButton = Find(dashboard, "btn360ExportIndexCsv") as Button;
            var indexJsonButton = Find(dashboard, "btn360ExportIndexJson") as Button;
            Require(indexCsvButton is not null && indexCsvButton.Enabled &&
                    indexJsonButton is not null && indexJsonButton.Enabled,
                    "WIM 360 index metadata export should work without mounting");
            var multiCompare = Find(dashboard, "btn360CompareIndexes") as Button;
            tabs.SelectedIndex = 9;
            Application.DoEvents();
            foreach (var actionButton in new[] {
                indexCsvButton!, indexJsonButton!, multiCompare,
                Find(dashboard, "btn360UseInMount") as Button,
                Find(dashboard, "btn360CompareSnapshots") as Button
            })
            {
                Require(actionButton is not null, "WIM 360 required action missing");
                VerifyReadableButton(actionButton!, "WIM 360");
            }
            Require(multiCompare is not null && multiCompare.Enabled,
                    "WIM 360 must enable direct multi-index comparison when two indexes exist");
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
            if (Environment.GetEnvironmentVariable("DISMGUI_LAYOUT_ONLY") == "1")
                Require(studioSource.Width >= 200,
                    "Driver Studio source path was squeezed under high font scaling");
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
                foreach (var button in new[] { expandLog!, copyLog!, clearLog!, exitApp! }.OfType<Button>())
                    VerifyReadableButton(button, "DISM output");
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
            if (Environment.GetEnvironmentVariable("DISMGUI_LAYOUT_ONLY") == "1")
                Require(wimPathField.Width >= 200,
                    "WIM 360 image path field was squeezed under high font scaling");
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
                var installedCaption = Find(updateForm, "lblUpdateInstalledVersion") as Label;
                var latestCaption = Find(updateForm, "lblUpdateLatestVersion") as Label;
                var upToDateCaption = typeof(UpdateManager).GetMethod(
                    "UpToDateCaption", System.Reflection.BindingFlags.Static |
                                       System.Reflection.BindingFlags.NonPublic);
                var formattedReleaseVersion = typeof(UpdateManager).GetMethod(
                    "DisplayVersion", System.Reflection.BindingFlags.Static |
                                      System.Reflection.BindingFlags.NonPublic);
                Require(installedCaption is not null && latestCaption is not null &&
                        upToDateCaption is not null && formattedReleaseVersion is not null,
                    "Update Center numeric version labels/formatters are missing");
                Require(installedCaption!.Text.StartsWith("Installed: " + appVersion + " (") &&
                        !installedCaption.Text.Contains("Installed: v", StringComparison.OrdinalIgnoreCase) &&
                        !latestCaption!.Text.Contains("Latest stable: v", StringComparison.OrdinalIgnoreCase),
                    "Installed/latest UI captions must never display v-prefixed versions");
                var rawTagVersion = new Version(4, 3, 10);
                Require((string)formattedReleaseVersion!.Invoke(null, new object[] { rawTagVersion })! == "4.3.10" &&
                        (string)upToDateCaption!.Invoke(null, new object[] { rawTagVersion })! == "Up to date - 4.3.10",
                    "Up to date must display numeric version without v prefix");
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
                VerifyScrollableDialog(updateForm, "Update Center");
                var updateViewport = AllControls(updateForm).OfType<Panel>()
                    .FirstOrDefault(p => p.AutoScroll);
                Require(updateViewport is not null &&
                        updateViewport.HorizontalScroll.Value <= 5 &&
                        updateViewport.VerticalScroll.Value <= 5,
                    "Update Center should open at the top-left rather than focus-scroll to footer");
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
                "## Changes in v4.3.10\n- Fixed controls\nSee [Release](https://github.com/example)"
            })!;
            Require(formatted.Contains("Changes in 4.3.10") &&
                    !formatted.Contains("v4.3.10", StringComparison.OrdinalIgnoreCase) &&
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
