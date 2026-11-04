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
                var updatesTitle = FindLabel(updateForm, "UPDATES");
                var updatesSub = FindLabel(updateForm,
                    "Stable releases  ·  SHA-256 verified");
                Require(updatesTitle is not null && updatesSub is not null,
                        "Update Center heading labels missing");
                NoOverlap(updatesTitle!, updatesSub!, "Update Center title and subtitle");
                Snapshot(updateForm, "DISM-GUI-Update-Center");
                updateForm.Hide();
            }

            Console.WriteLine("PASS About and Update Center heading layout");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("UI layout regression: " + ex);
            return 1;
        }
    }
}
