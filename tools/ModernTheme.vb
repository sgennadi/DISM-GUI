Imports System
Imports System.Collections.Generic
Imports System.Drawing
Imports System.Drawing.Drawing2D
Imports System.Runtime.InteropServices
Imports System.Windows.Forms

' Presentation-only shell. Existing DISM controls and event handlers are reused intact.
Public NotInheritable Class ModernTheme
    Private Sub New()
    End Sub

    Public Shared ReadOnly Navy As Color = Color.FromArgb(16, 26, 55)
    Public Shared ReadOnly Navy2 As Color = Color.FromArgb(35, 53, 103)
    Public Shared ReadOnly Backdrop As Color = Color.FromArgb(243, 247, 253)
    Public Shared ReadOnly TextInk As Color = Color.FromArgb(38, 55, 82)
    Public Shared ReadOnly Muted As Color = Color.FromArgb(103, 120, 144)
    Public Shared ReadOnly Blue As Color = Color.FromArgb(45, 110, 235)
    Public Shared ReadOnly Cyan As Color = Color.FromArgb(23, 182, 202)
    Public Shared ReadOnly Purple As Color = Color.FromArgb(125, 92, 224)
    Public Shared ReadOnly Green As Color = Color.FromArgb(19, 162, 122)
    Public Shared ReadOnly Red As Color = Color.FromArgb(228, 77, 105)

    Public Shared Function UiFont(size As Single, Optional bold As Boolean = False) As Font
        Return New Font("Segoe UI", size, If(bold, FontStyle.Bold, FontStyle.Regular), GraphicsUnit.Point)
    End Function

    Private Shared Function RoundPath(bounds As Rectangle, radius As Integer) As GraphicsPath
        Dim path As New GraphicsPath()
        If bounds.Width < 5 OrElse bounds.Height < 5 Then
            path.AddRectangle(bounds)
            Return path
        End If
        Dim d = Math.Min(radius * 2, Math.Min(bounds.Width, bounds.Height))
        path.AddArc(bounds.Left, bounds.Top, d, d, 180, 90)
        path.AddArc(bounds.Right - d, bounds.Top, d, d, 270, 90)
        path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90)
        path.AddArc(bounds.Left, bounds.Bottom - d, d, d, 90, 90)
        path.CloseFigure()
        Return path
    End Function

    <DllImport("user32.dll", SetLastError:=True)>
    Private Shared Function DestroyIcon(iconHandle As IntPtr) As Boolean
    End Function

    Public Shared Function MakeAppIcon() As Icon
        ' Use the executable's embedded multi-size ICO for title bar, Alt-Tab and taskbar.
        ' The fallback below is used only in a source-only designer or development preview.
        Try
            Dim exePath = Application.ExecutablePath
            If Not String.IsNullOrWhiteSpace(exePath) AndAlso
               System.IO.File.Exists(exePath) Then
                Using embedded As Icon = Icon.ExtractAssociatedIcon(exePath)
                    If embedded IsNot Nothing Then
                        Return DirectCast(embedded.Clone(), Icon)
                    End If
                End Using
            End If
        Catch
        End Try
        Using bmp As New Bitmap(64, 64)
            Using g = Graphics.FromImage(bmp)
                g.SmoothingMode = SmoothingMode.AntiAlias
                Using shape = RoundPath(New Rectangle(2, 2, 60, 60), 14),
                      bg As New LinearGradientBrush(New Rectangle(2, 2, 60, 60), Cyan, Blue,
                                                    LinearGradientMode.ForwardDiagonal)
                    g.FillPath(bg, shape)
                End Using
                TextRenderer.DrawText(g, "D", UiFont(31.0F, True),
                                      New Rectangle(3, 2, 57, 59), Color.White,
                                      TextFormatFlags.VerticalCenter Or
                                      TextFormatFlags.HorizontalCenter Or TextFormatFlags.NoPadding)
            End Using
            Dim handle = bmp.GetHicon()
            Try
                Using appImage = Icon.FromHandle(handle)
                    Return DirectCast(appImage.Clone(), Icon)
                End Using
            Finally
                DestroyIcon(handle)
            End Try
        End Using
    End Function

    Public Shared Sub StyleButton(button As Button, Optional role As String = Nothing)
        If role Is Nothing Then
            Dim n = button.Name.ToLowerInvariant()
            If n.Contains("dismount") OrElse n.Contains("remove") OrElse
               n.Contains("delete") OrElse n.Contains("disable") OrElse n.Contains("del") Then
                role = "danger"
            ElseIf n.Contains("mount") AndAlso Not n.Contains("open") Then
                role = "primary"
            ElseIf n.Contains("apply") OrElse n.Contains("add") OrElse
                   n.Contains("capture") OrElse n.Contains("create") OrElse
                   n.Contains("append") OrElse n.Contains("enable") OrElse n.Contains("setedition") Then
                role = "primary"
            ElseIf n.Contains("open") OrElse n.Contains("browse") OrElse n.Contains("choose") Then
                role = "browse"
            Else
                role = "neutral"
            End If
        End If
        Dim bg As Color = If(role = "primary", Blue,
                             If(role = "danger", Red,
                                If(role = "browse", Color.FromArgb(215, 237, 252),
                                   Color.FromArgb(225, 233, 247))))
        button.UseVisualStyleBackColor = False
        button.FlatStyle = FlatStyle.Flat
        button.FlatAppearance.BorderSize = 0
        button.FlatAppearance.MouseOverBackColor = ControlPaint.Light(bg, 0.12F)
        button.FlatAppearance.MouseDownBackColor = ControlPaint.Dark(bg, 0.1F)
        button.BackColor = bg
        button.ForeColor = If(role = "primary" OrElse role = "danger", Color.White, TextInk)
        button.Font = UiFont(8.5F, True)
        button.Cursor = Cursors.Hand
        button.AutoEllipsis = True
        AddHandler button.SizeChanged, Sub(sender, e)
            Dim b = DirectCast(sender, Button)
            If b.IsDisposed OrElse b.Width < 6 OrElse b.Height < 6 Then Return
            Using gp = RoundPath(New Rectangle(0, 0, b.Width, b.Height), 8)
                Dim prior = b.Region
                b.Region = New Region(gp)
                If prior IsNot Nothing Then prior.Dispose()
            End Using
        End Sub
        If button.Width > 6 AndAlso button.Height > 6 Then
            Using gp = RoundPath(New Rectangle(0, 0, button.Width, button.Height), 8)
                button.Region = New Region(gp)
            End Using
        End If
    End Sub

    Private Shared Sub StyleControls(parent As Control, tips As ToolTip)
        For Each c As Control In parent.Controls
            If TypeOf c Is Button Then
                Dim button = DirectCast(c, Button)
                StyleButton(button)
                tips.SetToolTip(button, button.Text.Replace("&", "") &
                               If(button.Name.ToLowerInvariant().Contains("browse") OrElse
                                  button.Name.ToLowerInvariant().Contains("open") OrElse
                                  button.Name.ToLowerInvariant().Contains("choose"),
                                  " - opens the system file or folder dialog.", ""))
            ElseIf TypeOf c Is TextBox Then
                c.ForeColor = TextInk
                c.BackColor = Color.White
                c.Font = UiFont(9)
                DirectCast(c, TextBox).BorderStyle = BorderStyle.FixedSingle
            ElseIf TypeOf c Is ComboBox Then
                c.ForeColor = TextInk
                c.BackColor = Color.White
                c.Font = UiFont(9)
                DirectCast(c, ComboBox).FlatStyle = FlatStyle.Flat
            ElseIf TypeOf c Is Label Then
                c.ForeColor = Muted
                c.Font = UiFont(9)
            ElseIf TypeOf c Is CheckBox Then
                c.ForeColor = TextInk
                c.Font = UiFont(9)
            ElseIf TypeOf c Is GroupBox Then
                c.BackColor = Color.White
                c.ForeColor = TextInk
                c.Font = UiFont(9, True)
            End If
            If c.HasChildren Then StyleControls(c, tips)
        Next
    End Sub

    Private Shared Function TextLabel(value As String, font As Font, color As Color) As Label
        Return New Label With {.Text = value, .Font = font, .ForeColor = color,
                               .BackColor = Color.Transparent, .AutoSize = False}
    End Function

    Public Shared Sub ApplyMain(frm As Form, tabs As TabControl, output As TextBox,
                                menu As MenuStrip, exitButton As Button, outputLabel As Label)
        frm.SuspendLayout()
        frm.Font = UiFont(9)
        frm.BackColor = Backdrop
        frm.StartPosition = FormStartPosition.CenterScreen
        frm.MinimumSize = New Size(760, 590)
        frm.Text = "DISM GUI  •  Modern Edition"
        Try
            frm.Icon = MakeAppIcon()
        Catch
            ' Icon drawing never blocks operation.
        End Try
        Dim workArea = Screen.FromControl(frm).WorkingArea
        frm.ClientSize = New Size(Math.Max(720, Math.Min(1190, workArea.Width - 48)),
                                  Math.Max(550, Math.Min(800, workArea.Height - 70)))

        frm.Controls.Remove(tabs)
        frm.Controls.Remove(output)
        frm.Controls.Remove(menu)
        frm.Controls.Remove(exitButton)
        frm.Controls.Remove(outputLabel)

        Dim root As New TableLayoutPanel With {
            .Dock = DockStyle.Fill, .Padding = Padding.Empty, .Margin = Padding.Empty,
            .RowCount = 3, .ColumnCount = 1, .BackColor = Backdrop
        }
        root.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100))
        root.RowStyles.Add(New RowStyle(SizeType.Absolute, 88))
        root.RowStyles.Add(New RowStyle(SizeType.Percent, 100))
        root.RowStyles.Add(New RowStyle(SizeType.Absolute, 198))
        frm.Controls.Add(root)

        Dim hero As New Panel With {.Dock = DockStyle.Fill, .Margin = Padding.Empty}
        AddHandler hero.Paint, Sub(sender, e)
            If hero.Width < 1 OrElse hero.Height < 1 Then Return
            Using gradient As New LinearGradientBrush(hero.ClientRectangle, Navy, Navy2,
                                                        LinearGradientMode.Horizontal)
                e.Graphics.FillRectangle(gradient, hero.ClientRectangle)
            End Using
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias
            Using brush As New SolidBrush(Color.FromArgb(25, 77, 199, 238))
                e.Graphics.FillEllipse(brush, hero.Width - 290, -128, 330, 260)
            End Using
            Using brush As New SolidBrush(Cyan)
                e.Graphics.FillRectangle(brush, 0, hero.Height - 4, 130, 4)
            End Using
            Using brush As New SolidBrush(Purple)
                e.Graphics.FillRectangle(brush, 130, hero.Height - 4, 92, 4)
            End Using
        End Sub
        root.Controls.Add(hero, 0, 0)

        Dim mark As New Label With {
            .Text = "D", .Font = UiFont(27, True), .ForeColor = Cyan,
            .BackColor = Color.Transparent, .Location = New Point(23, 14),
            .Size = New Size(52, 57), .TextAlign = ContentAlignment.MiddleCenter
        }
        hero.Controls.Add(mark)
        Dim title = TextLabel("DISM GUI", UiFont(21, True), Color.White)
        title.Location = New Point(83, 12)
        title.Size = New Size(315, 41)
        hero.Controls.Add(title)
        Dim subtitle = TextLabel("WINDOWS IMAGE TOOLKIT  /  VS 2026",
                                 UiFont(9), Color.FromArgb(175, 217, 247))
        subtitle.Location = New Point(85, 54)
        subtitle.Size = New Size(450, 24)
        hero.Controls.Add(subtitle)

        menu.Dock = DockStyle.None
        menu.AutoSize = False
        menu.BackColor = Navy2
        menu.ForeColor = Color.White
        menu.Font = UiFont(9, True)
        menu.Padding = New Padding(7, 8, 4, 0)
        menu.Size = New Size(224, 39)
        menu.Location = New Point(hero.Width - menu.Width - 16, 24)
        menu.Anchor = AnchorStyles.Top Or AnchorStyles.Right
        For Each item As ToolStripItem In menu.Items
            item.ForeColor = Color.White
            If TypeOf item Is ToolStripMenuItem Then
                For Each subItem As ToolStripItem In DirectCast(item, ToolStripMenuItem).DropDownItems
                    subItem.ForeColor = TextInk
                Next
            End If
        Next
        hero.Controls.Add(menu)
        Dim updateMenu As New ToolStripMenuItem("Check for Updates...")
        AddHandler updateMenu.Click, Sub(sender, e) UpdateManager.ShowUpdateDialog(frm)
        menu.Items.Add(updateMenu)

        Dim workspace As New TableLayoutPanel With {
            .Dock = DockStyle.Fill, .Margin = Padding.Empty, .ColumnCount = 2,
            .RowCount = 1, .BackColor = Backdrop
        }
        workspace.RowStyles.Add(New RowStyle(SizeType.Percent, 100))
        workspace.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute, 211))
        workspace.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100))
        root.Controls.Add(workspace, 0, 1)

        Dim side As New FlowLayoutPanel With {
            .Dock = DockStyle.Fill, .AutoScroll = True,
            .FlowDirection = FlowDirection.TopDown, .WrapContents = False,
            .Padding = New Padding(8, 15, 7, 12), .Margin = Padding.Empty,
            .BackColor = Navy
        }
        workspace.Controls.Add(side, 0, 0)
        Dim navTitle = TextLabel("  WORKSPACES", UiFont(8.5F, True),
                                 Color.FromArgb(143, 168, 204))
        navTitle.Height = 36
        navTitle.Width = 180
        side.Controls.Add(navTitle)

        Dim sectionNames As String() = {"Mount & Inspect", "Drivers", "Packages",
            "Windows Features", "Editions", "Unattend XML",
            "Applications", "Capture WIM", "Apply WIM"}
        Dim sectionColors As Color() = {Cyan, Blue, Purple, Green, Color.Orange,
                                       Purple, Cyan, Green, Red}
        Dim navButtons As New List(Of Button)()
        Dim pageHeading = TextLabel(sectionNames(0).ToUpperInvariant(), UiFont(10.0F, True), TextInk)
        pageHeading.Dock = DockStyle.Fill
        pageHeading.TextAlign = ContentAlignment.MiddleLeft
        Dim setActive As Action(Of Integer) = Sub(index)
            For n As Integer = 0 To navButtons.Count - 1
                Dim b = navButtons(n)
                b.BackColor = If(n = index, Navy2, Navy)
                b.ForeColor = If(n = index, Color.White, Color.FromArgb(196, 211, 234))
                b.FlatAppearance.BorderSize = If(n = index, 1, 0)
                b.FlatAppearance.BorderColor = sectionColors(n Mod sectionColors.Length)
            Next
            pageHeading.Text = sectionNames(index).ToUpperInvariant()
        End Sub

        For i As Integer = 0 To tabs.TabPages.Count - 1
            Dim index As Integer = i
            Dim navBtn As New Button With {
                .Name = "nav" & i.ToString(), .Text = "  " & (i + 1).ToString("00") &
                   "    " & sectionNames(Math.Min(i, sectionNames.Length - 1)),
                .Size = New Size(190, 40), .Margin = New Padding(2, 1, 2, 4),
                .FlatStyle = FlatStyle.Flat, .TextAlign = ContentAlignment.MiddleLeft,
                .Font = UiFont(9, True), .ForeColor = Color.White, .BackColor = Navy,
                .Cursor = Cursors.Hand, .UseVisualStyleBackColor = False
            }
            navBtn.FlatAppearance.BorderSize = 0
            AddHandler navBtn.Click, Sub(sender, e)
                tabs.SelectedIndex = index
            End Sub
            navButtons.Add(navBtn)
            side.Controls.Add(navBtn)
        Next
        AddHandler tabs.SelectedIndexChanged, Sub(sender, e) setActive(Math.Max(0, tabs.SelectedIndex))
        setActive(0)

        Dim content As New TableLayoutPanel With {
            .Dock = DockStyle.Fill, .Padding = New Padding(18, 13, 18, 8),
            .Margin = Padding.Empty, .ColumnCount = 1, .RowCount = 2,
            .BackColor = Backdrop
        }
        content.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100))
        content.RowStyles.Add(New RowStyle(SizeType.Absolute, 36))
        content.RowStyles.Add(New RowStyle(SizeType.Percent, 100))
        workspace.Controls.Add(content, 1, 0)
        content.Controls.Add(pageHeading, 0, 0)

        Dim tabHost As New Panel With {
            .Dock = DockStyle.Fill, .Margin = Padding.Empty,
            .BackColor = Color.White, .Padding = Padding.Empty
        }
        content.Controls.Add(tabHost, 0, 1)
        tabs.Appearance = TabAppearance.Normal
        tabs.SizeMode = TabSizeMode.Fixed
        tabs.ItemSize = New Size(110, 26)
        tabs.Dock = DockStyle.None
        tabs.Location = New Point(-3, -26)
        tabHost.Controls.Add(tabs)
        Dim layoutTabs As Action = Sub()
            If tabHost.ClientSize.Width <= 0 Then Return
            Dim tabHeight = Math.Max(26, tabs.ItemSize.Height)
            tabs.Bounds = New Rectangle(-4, -tabHeight,
                                        tabHost.ClientSize.Width + 8,
                                        tabHost.ClientSize.Height + tabHeight + 5)
        End Sub
        AddHandler tabHost.Resize, Sub(sender, e) layoutTabs()
        layoutTabs()
        tabs.BackColor = Color.White
        Dim tips As New ToolTip With {.AutoPopDelay = 14000, .InitialDelay = 600,
                                     .ReshowDelay = 200}
        For Each page As TabPage In tabs.TabPages
            page.BackColor = Color.White
            page.UseVisualStyleBackColor = False
            page.AutoScroll = True
            StyleControls(page, tips)
        Next

        Dim logArea As New TableLayoutPanel With {
            .Dock = DockStyle.Fill, .RowCount = 2, .ColumnCount = 1,
            .Margin = New Padding(15, 8, 15, 11), .BackColor = Backdrop
        }
        logArea.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100))
        logArea.RowStyles.Add(New RowStyle(SizeType.Absolute, 36))
        logArea.RowStyles.Add(New RowStyle(SizeType.Percent, 100))
        root.Controls.Add(logArea, 0, 2)
        Dim logHeader As New Panel With {.Dock = DockStyle.Fill}
        logArea.Controls.Add(logHeader, 0, 0)
        Dim logTitle = TextLabel("DISM OUTPUT", UiFont(9, True), TextInk)
        logTitle.Dock = DockStyle.Left
        logTitle.Width = 170
        logTitle.TextAlign = ContentAlignment.MiddleLeft
        logHeader.Controls.Add(logTitle)
        Dim toolButtons As New FlowLayoutPanel With {
            .Dock = DockStyle.Right, .FlowDirection = FlowDirection.RightToLeft,
            .WrapContents = False, .Width = 240
        }
        logHeader.Controls.Add(toolButtons)
        Dim clear As New Button With {.Name = "btnClearOutput", .Text = "Clear",
                                     .Size = New Size(86, 29), .Margin = New Padding(3)}
        StyleButton(clear)
        AddHandler clear.Click, Sub(sender, e) output.Clear()
        toolButtons.Controls.Add(clear)
        Dim copy As New Button With {.Name = "btnCopyOutput", .Text = "Copy Log",
                                    .Size = New Size(103, 29), .Margin = New Padding(3)}
        StyleButton(copy, "browse")
        AddHandler copy.Click, Sub(sender, e)
            If Not String.IsNullOrWhiteSpace(output.Text) Then Clipboard.SetText(output.Text)
        End Sub
        toolButtons.Controls.Add(copy)
        output.Dock = DockStyle.Fill
        output.BackColor = Color.FromArgb(20, 32, 58)
        output.ForeColor = Color.FromArgb(213, 234, 248)
        output.Font = New Font("Consolas", 9.0F, FontStyle.Regular)
        output.BorderStyle = BorderStyle.None
        output.ReadOnly = True
        output.Margin = Padding.Empty
        logArea.Controls.Add(output, 0, 1)
        exitButton.Text = "Exit"
        exitButton.Size = New Size(86, 29)
        StyleButton(exitButton, "neutral")
        exitButton.Dock = DockStyle.Right
        logHeader.Controls.Add(exitButton)
        exitButton.BringToFront()
        frm.ResumeLayout(True)
    End Sub

    Public Shared Sub ApplyProgress(frm As Form, label As Label, bar As ProgressBar)
        frm.SuspendLayout()
        frm.StartPosition = FormStartPosition.CenterParent
        frm.BackColor = Backdrop
        frm.ClientSize = New Size(510, 169)
        frm.Font = UiFont(9)
        frm.Text = "DISM GUI - Processing"
        Try
            frm.Icon = MakeAppIcon()
            frm.ShowIcon = True
        Catch
        End Try
        label.Text = "DISM is processing the image..."
        label.Font = UiFont(14, True)
        label.ForeColor = Navy2
        label.AutoSize = False
        label.Location = New Point(25, 24)
        label.Size = New Size(462, 58)
        bar.Location = New Point(25, 105)
        bar.Size = New Size(459, 17)
        bar.ForeColor = Blue
        bar.Style = ProgressBarStyle.Marquee
        frm.ResumeLayout(True)
    End Sub
End Class
