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
                var heading = form.Controls.Find("btnToolsMenu", true).First();
                Require(heading.RectangleToScreen(heading.ClientRectangle).Right <=
                        form.RectangleToScreen(form.ClientRectangle).Right,
                        "Tools button extends off-window");
                Console.WriteLine($"PASS {size}: header, Tools, Index, Mount and Dismount are non-overlapping");
                Snapshot(form, $"DISM-GUI-{form.ClientSize.Width}x{form.ClientSize.Height}");
            }

            form.Hide();
            form.Dispose();

            using (var about = new frmAbout())
            {
                about.Show();
                Application.DoEvents();
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
