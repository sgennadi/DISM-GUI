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
            var wim = Find(page, "txtWIM")!;
            var browseWim = Find(page, "btnOpenWIM")!;
            var mount = Find(page, "txtMount")!;
            var browseMount = Find(page, "btnOpenMount")!;
            var index = Find(page, "cmbIndex") as ComboBox;
            var mountAction = Find(page, "btnMount")!;
            var dismountAction = Find(page, "btnDismount")!;
            var info = Find(page, "btnDisplayWIMInfo")!;
            Require(index is not null, "Image index selector missing");
            Require(dismountAction.BackColor != ModernTheme.Red,
                "Dismount action must not be red like an error state");

            foreach (var size in new[] { new Size(900, 650), new Size(1180, 760) })
            {
                form.ClientSize = size;
                form.PerformLayout();
                Application.DoEvents();
                Require(index!.Width >= 75, $"Image index too narrow at {size}");
                Require(browseWim.Width >= 110 && browseMount.Width >= 110,
                    $"Browse actions too narrow at {size}");
                NoOverlap(wim, browseWim, "WIM text / Browse WIM");
                NoOverlap(mount, browseMount, "Mount path / Browse folder");
                NoOverlap(mountAction, dismountAction, "Mount / Dismount");
                NoOverlap(dismountAction, info, "Dismount / Image info");
                NoOverlap(index, browseWim, "Image index / Choose WIM");
                var heading = form.Controls.Find("btnToolsMenu", true).First();
                Require(heading.RectangleToScreen(heading.ClientRectangle).Right <=
                        form.RectangleToScreen(form.ClientRectangle).Right,
                        "Tools button extends off-window");
                Console.WriteLine($"PASS {size}: header, Tools, Index, Mount and Dismount are non-overlapping");
                try
                {
                    var folder = System.IO.Path.Combine(System.Environment.CurrentDirectory, "ui-screenshots");
                    System.IO.Directory.CreateDirectory(folder);
                    using var snapshot = new Bitmap(form.Width, form.Height);
                    form.DrawToBitmap(snapshot, new Rectangle(Point.Empty, form.Size));
                    snapshot.Save(System.IO.Path.Combine(folder,
                        $"DISM-GUI-{size.Width}x{size.Height}.png"),
                        System.Drawing.Imaging.ImageFormat.Png);
                }
                catch (Exception imageError)
                {
                    // Some hosted Windows sessions restrict GUI screenshotting.
                    Console.WriteLine("Screenshot unavailable: " + imageError.Message);
                }
            }

            form.Hide();
            form.Dispose();
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("UI layout regression: " + ex);
            return 1;
        }
    }
}
