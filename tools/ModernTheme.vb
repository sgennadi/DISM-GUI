Imports System
Imports System.Collections.Generic
Imports System.Drawing
Imports System.Drawing.Drawing2D
Imports System.Linq
Imports System.Runtime.InteropServices
Imports System.Windows.Forms

' Presentation-only shell. Existing DISM controls and event handlers are reused intact.
Public NotInheritable Class ModernTheme
    Private Sub New()
    End Sub

    Public Shared ReadOnly Navy As Color = Color.FromArgb(11, 46, 90)
    Public Shared ReadOnly Navy2 As Color = Color.FromArgb(14, 106, 230)
    Public Shared ReadOnly Backdrop As Color = Color.FromArgb(244, 247, 253)
    Public Shared ReadOnly TextInk As Color = Color.FromArgb(20, 39, 68)
    Public Shared ReadOnly Muted As Color = Color.FromArgb(94, 112, 142)
    Public Shared ReadOnly Blue As Color = Color.FromArgb(14, 106, 230)
    Public Shared ReadOnly Cyan As Color = Color.FromArgb(30, 194, 255)
    Public Shared ReadOnly Purple As Color = Color.FromArgb(125, 92, 224)
    Public Shared ReadOnly Green As Color = Color.FromArgb(16, 170, 93)
    Public Shared ReadOnly Red As Color = Color.FromArgb(211, 70, 91)

    Public Shared Function UiFont(size As Single, Optional bold As Boolean = False) As Font
        ' LayoutSmoke runs at three simulated font scales in Windows CI.
        ' Normal installed builds ALWAYS use the unchanged system UI size.
        Dim factor As Single = 1.0F
        If String.Equals(Environment.GetEnvironmentVariable("DISMGUI_LAYOUT_ONLY"),
                         "1", StringComparison.Ordinal) Then
            Dim requested As Single
            If Single.TryParse(Environment.GetEnvironmentVariable("DISMGUI_LAYOUT_FONT_SCALE"),
                               Globalization.NumberStyles.Float,
                               Globalization.CultureInfo.InvariantCulture, requested) Then
                factor = Math.Max(1.0F, Math.Min(1.75F, requested))
            End If
        End If
        Return New Font("Segoe UI", size * factor,
                        If(bold, FontStyle.Bold, FontStyle.Regular), GraphicsUnit.Point)
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

    ' Common, DPI-aware action-button measurements; keep container Dock behavior.
    Public Shared Function RecommendedButtonSize(button As Button,
                                                   Optional minimumWidth As Integer = 100) As Size
        Dim caption = button.Text.Replace("&&", "&").Replace("&", "")
        Dim measured = TextRenderer.MeasureText(caption, button.Font)
        Return New Size(Math.Max(minimumWidth, measured.Width + 34),
                        Math.Max(36, measured.Height + 18))
    End Function

    Public Shared Sub ConfigureReadableButton(button As Button,
                                              Optional minimumWidth As Integer = 100)
        button.AutoEllipsis = False
        button.TextAlign = ContentAlignment.MiddleCenter
        button.Padding = New Padding(8, 3, 8, 3)
        If button.Dock = DockStyle.None Then
            Dim preferred = RecommendedButtonSize(button, minimumWidth)
            button.MinimumSize = preferred
            button.Size = New Size(Math.Max(button.Width, preferred.Width),
                                   Math.Max(button.Height, preferred.Height))
        End If
    End Sub

    ' Measure every button against its real font. This works for all shared
    ' dialogs and avoids unreadable fixed-width English action captions.
    Public Shared Sub FitTableButtonCells(table As TableLayoutPanel)
        If table Is Nothing Then Return
        For Each button In table.Controls.OfType(Of Button)()
            button.AutoEllipsis = False
            Dim expected = RecommendedButtonSize(button)
            Dim col = table.GetColumn(button)
            If col >= 0 AndAlso col < table.ColumnStyles.Count Then
                Dim cell = table.ColumnStyles(col)
                If cell.SizeType = SizeType.Absolute Then
                    cell.Width = Math.Max(cell.Width,
                        expected.Width + button.Margin.Horizontal)
                End If
            End If
            Dim row = table.GetRow(button)
            If row >= 0 AndAlso row < table.RowStyles.Count Then
                Dim cell = table.RowStyles(row)
                If cell.SizeType = SizeType.Absolute Then
                    cell.Height = Math.Max(cell.Height,
                        expected.Height + button.Margin.Vertical)
                End If
            End If
        Next
    End Sub

    Private Shared Sub ScaleDialogTables(control As Control, scale As Single)
        Dim table = TryCast(control, TableLayoutPanel)
        If table IsNot Nothing Then
            If scale > 1.01F Then
                For Each row As RowStyle In table.RowStyles
                    If row.SizeType = SizeType.Absolute AndAlso row.Height > 0 Then
                        row.Height = CSng(Math.Ceiling(row.Height * scale))
                    End If
                Next
                For Each col As ColumnStyle In table.ColumnStyles
                    If col.SizeType = SizeType.Absolute AndAlso col.Width > 0 Then
                        col.Width = CSng(Math.Ceiling(col.Width * scale))
                    End If
                Next
            End If
            FitTableButtonCells(table)
        End If
        Dim grid = TryCast(control, DataGridView)
        If grid IsNot Nothing Then
            grid.RowTemplate.Height = Math.Max(grid.RowTemplate.Height,
                                               grid.Font.Height + 12)
            Dim headerFont = If(grid.ColumnHeadersDefaultCellStyle.Font, grid.Font)
            grid.ColumnHeadersHeight = Math.Max(grid.ColumnHeadersHeight,
                                                headerFont.Height + 18)
        End If
        For Each child As Control In control.Controls
            ScaleDialogTables(child, scale)
        Next
    End Sub

    ' Large dialogs must scroll at 125-200% Windows scaling instead of clipping
    ' their buttons or falling off screen. Call after all dialog controls exist.
    Public Shared Sub MakeDialogScrollable(frm As Form, root As TableLayoutPanel,
                                           minimumWidth As Integer,
                                           minimumHeight As Integer)
        If frm Is Nothing OrElse root Is Nothing OrElse root.Parent IsNot frm Then
            Return
        End If
        Dim uiScale = Math.Max(1.0F, frm.Font.Height / 17.0F)
        Dim scaledMinWidth = CInt(Math.Ceiling(minimumWidth * uiScale))
        Dim scaledMinHeight = CInt(Math.Ceiling(minimumHeight * uiScale))
        ScaleDialogTables(root, uiScale)
        Dim viewport As New Panel With {
            .Name = root.Name & "Viewport",
            .Dock = DockStyle.Fill, .AutoScroll = True,
            .BackColor = frm.BackColor
        }
        frm.Controls.Remove(root)
        frm.Controls.Add(viewport)
        root.Dock = DockStyle.None
        root.Margin = Padding.Empty
        root.Location = Point.Empty
        viewport.Controls.Add(root)
        Dim syncSize As Action = Sub()
            If frm.IsDisposed OrElse root.IsDisposed OrElse viewport.IsDisposed Then Return
            Dim w = Math.Max(scaledMinWidth, viewport.ClientSize.Width - 1)
            Dim h = Math.Max(scaledMinHeight, viewport.ClientSize.Height - 1)
            If root.Width <> w OrElse root.Height <> h Then
                root.Size = New Size(w, h)
            End If
        End Sub
        AddHandler viewport.ClientSizeChanged, Sub(sender, e) syncSize()
        AddHandler frm.Shown, Sub(sender, e)
            syncSize()
            ' WinForms may auto-scroll to the last focused button during Show.
            ' Restore the top/left AFTER initial focus and layout settle.
            frm.BeginInvoke(New MethodInvoker(Sub()
                If Not viewport.IsDisposed Then
                    viewport.AutoScrollPosition = Point.Empty
                End If
            End Sub))
        End Sub
        syncSize()
    End Sub

    Public Shared Sub StyleButton(button As Button, Optional role As String = Nothing)
        If role Is Nothing Then
            Dim n = button.Name.ToLowerInvariant()
            If n.Contains("dismount") OrElse n.Contains("remove") OrElse
               n.Contains("delete") OrElse n.Contains("disable") OrElse n.Contains("del") Then
                role = "secondary"
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
                             If(role = "secondary", Color.FromArgb(232, 241, 251),
                                If(role = "browse", Color.FromArgb(233, 243, 254),
                                   Color.White)))
        button.UseVisualStyleBackColor = False
        button.FlatStyle = FlatStyle.Flat
        button.FlatAppearance.BorderSize = 1
        button.FlatAppearance.BorderColor = If(role = "primary", Blue,
                                                Color.FromArgb(195, 215, 238))
        button.FlatAppearance.MouseOverBackColor = ControlPaint.Light(bg, 0.12F)
        button.FlatAppearance.MouseDownBackColor = ControlPaint.Dark(bg, 0.1F)
        button.BackColor = bg
        button.ForeColor = If(role = "primary", Color.White, TextInk)
        button.Font = UiFont(9.3F, True)
        button.Cursor = Cursors.Hand
        ConfigureReadableButton(button)
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

    ' Keep the original editable WIM index control and its existing location.
    ' Native dropdown text selection can appear as an ugly blue block when the
    ' legacy 30px-wide box receives focus; clear selection without disabling typing.
    Private Shared Sub StyleIndexSelector(index As ComboBox, tips As ToolTip)
        index.FlatStyle = FlatStyle.Standard
        index.BackColor = Color.White
        index.ForeColor = TextInk
        index.Font = UiFont(11.0F, True)
        index.DropDownWidth = 104
        index.MaxDropDownItems = 10
        index.IntegralHeight = False
        tips.SetToolTip(index, "WIM image index. Choose from the list or type an index.")

        Dim clearHighlight As EventHandler = Sub(sender, e)
            If index.IsDisposed OrElse index.DropDownStyle = ComboBoxStyle.DropDownList Then Return
            If index.SelectionLength <> 0 Then
                index.SelectionStart = index.Text.Length
                index.SelectionLength = 0
            End If
        End Sub
        AddHandler index.Enter, clearHighlight
        AddHandler index.SelectionChangeCommitted, clearHighlight
        AddHandler index.DropDownClosed, clearHighlight
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
                c.Font = UiFont(10)
                DirectCast(c, TextBox).BorderStyle = BorderStyle.FixedSingle
            ElseIf TypeOf c Is ComboBox Then
                c.ForeColor = TextInk
                c.BackColor = Color.White
                c.Font = UiFont(10)
                DirectCast(c, ComboBox).FlatStyle = FlatStyle.Flat
                If String.Equals(c.Name, "cmbIndex", StringComparison.OrdinalIgnoreCase) Then
                    StyleIndexSelector(DirectCast(c, ComboBox), tips)
                End If
            ElseIf TypeOf c Is Label Then
                c.ForeColor = Muted
                c.Font = UiFont(9.7F)
                ' Tooltip preserves the full caption where a narrow legacy
                ' layout cannot display the entire dynamic status label.
                tips.SetToolTip(c, c.Text)
                AddHandler c.TextChanged, Sub(sender, e)
                    Dim label = DirectCast(sender, Control)
                    If Not label.IsDisposed Then tips.SetToolTip(label, label.Text)
                End Sub
            ElseIf TypeOf c Is CheckBox Then
                c.ForeColor = TextInk
                c.Font = UiFont(9.7F)
                tips.SetToolTip(c, c.Text)
            ElseIf TypeOf c Is GroupBox Then
                c.BackColor = Color.White
                c.ForeColor = TextInk
                c.Font = UiFont(9.8F, True)
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
        frm.Font = UiFont(10)
        frm.AutoScaleMode = AutoScaleMode.Font
        frm.BackColor = Backdrop
        frm.StartPosition = FormStartPosition.CenterScreen
        frm.MinimumSize = New Size(850, 585)
        frm.Text = "DISM GUI " & GetType(ModernTheme).Assembly.GetName().Version.ToString(3) & " - Modern Edition"
        Try
            frm.Icon = MakeAppIcon()
        Catch
            ' Icon drawing never blocks operation.
        End Try
        Dim workArea = Screen.FromControl(frm).WorkingArea
        frm.ClientSize = New Size(Math.Max(800, Math.Min(1260, workArea.Width - 56)),
                                  Math.Max(545, Math.Min(790, workArea.Height - 68)))

        frm.Controls.Remove(tabs)
        frm.Controls.Remove(output)
        frm.Controls.Remove(menu)
        frm.Controls.Remove(exitButton)
        frm.Controls.Remove(outputLabel)

        Dim root As New TableLayoutPanel With {
            .Dock = DockStyle.Fill, .Padding = Padding.Empty, .Margin = Padding.Empty,
            .RowCount = 2, .ColumnCount = 1, .BackColor = Backdrop
        }
        root.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100))
        root.RowStyles.Add(New RowStyle(SizeType.Absolute, 102))
        root.RowStyles.Add(New RowStyle(SizeType.Percent, 100))
        frm.Controls.Add(root)

        ' Native draggable divider: no fixed 172px console and no overlap.
        Dim split As New SplitContainer With {
            .Name = "mainOutputSplitter", .Orientation = Orientation.Horizontal,
            .Dock = DockStyle.Fill, .Margin = Padding.Empty, .Padding = Padding.Empty,
            .SplitterWidth = 9,
            .FixedPanel = FixedPanel.Panel2,
            .BackColor = Color.FromArgb(207, 225, 243),
            .BorderStyle = BorderStyle.None,
            .TabStop = True
        }
        root.Controls.Add(split, 0, 1)

        Dim savedOutputHeight As Integer = 194
        Try
            Using key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                "Software\DISMGUI\UserInterface", False)
                If key IsNot Nothing Then
                    Dim raw = key.GetValue("OutputHeight")
                    If TypeOf raw Is Integer Then savedOutputHeight = CInt(raw)
                End If
            End Using
        Catch
            ' Preference storage must not interrupt the application.
        End Try
        savedOutputHeight = Math.Max(155, Math.Min(520, savedOutputHeight))
        AddHandler frm.Shown, Sub(sender, e)
            If split.IsDisposed Then Return
            split.BeginInvoke(New MethodInvoker(Sub()
                If split.IsDisposed Then Return
                ' Set min sizes after Dock layout. Setting them in the constructor
                ' can fail because an unattached SplitContainer is initially tiny.
                If split.Height <= 385 Then Return
                split.Panel1MinSize = 215
                split.Panel2MinSize = 145
                Dim maxDistance = split.Height - split.SplitterWidth - split.Panel2MinSize
                If maxDistance <= split.Panel1MinSize Then Return
                Dim wantedDistance = split.Height - split.SplitterWidth - savedOutputHeight
                split.SplitterDistance = Math.Max(split.Panel1MinSize,
                                                  Math.Min(maxDistance, wantedDistance))
            End Sub))
        End Sub
        AddHandler split.SplitterMoved, Sub(sender, e)
            Try
                Using key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(
                    "Software\DISMGUI\UserInterface")
                    If key IsNot Nothing Then
                        key.SetValue("OutputHeight", split.Panel2.Height,
                                     Microsoft.Win32.RegistryValueKind.DWord)
                    End If
                End Using
            Catch
                ' A locked-down workstation may not permit writing preferences.
            End Try
        End Sub

        ' Header is a real responsive grid, not independently positioned controls.
        ' This prevents title/subtitle/menu overlap at 100-200% Windows DPI.
        Dim hero As New Panel With {.Dock = DockStyle.Fill, .Margin = Padding.Empty}
        AddHandler hero.Paint, Sub(sender, e)
            If hero.Width < 1 OrElse hero.Height < 1 Then Return
            Using gradient As New LinearGradientBrush(hero.ClientRectangle,
                     Color.FromArgb(36, 98, 174), Color.FromArgb(55, 150, 215),
                     LinearGradientMode.Horizontal)
                e.Graphics.FillRectangle(gradient, hero.ClientRectangle)
            End Using
            Using brush As New SolidBrush(Color.FromArgb(26, Color.White))
                e.Graphics.FillEllipse(brush, hero.Width - 275, -165, 305, 265)
            End Using
        End Sub
        root.Controls.Add(hero, 0, 0)

        Dim heroGrid As New TableLayoutPanel With {
            .Dock = DockStyle.Fill, .ColumnCount = 3, .RowCount = 1,
            .Margin = Padding.Empty, .Padding = New Padding(18, 13, 22, 13),
            .BackColor = Color.Transparent
        }
        heroGrid.RowStyles.Add(New RowStyle(SizeType.Percent, 100))
        heroGrid.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute, 64))
        heroGrid.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100))
        heroGrid.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute, 178))
        hero.Controls.Add(heroGrid)

        Dim mark As New PictureBox With {
            .Dock = DockStyle.Fill, .SizeMode = PictureBoxSizeMode.Zoom,
            .Margin = New Padding(2, 7, 11, 7), .BackColor = Color.Transparent
        }
        Try
            Using appMark As Icon = MakeAppIcon()
                mark.Image = appMark.ToBitmap()
            End Using
        Catch
        End Try
        heroGrid.Controls.Add(mark, 0, 0)

        Dim heading As New TableLayoutPanel With {
            .Dock = DockStyle.Fill, .ColumnCount = 1, .RowCount = 2,
            .Margin = Padding.Empty, .BackColor = Color.Transparent
        }
        heading.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100))
        heading.RowStyles.Add(New RowStyle(SizeType.Percent, 57))
        heading.RowStyles.Add(New RowStyle(SizeType.Percent, 43))
        heroGrid.Controls.Add(heading, 1, 0)
        Dim title = TextLabel("DISM GUI", UiFont(19, True), Color.White)
        title.TextAlign = ContentAlignment.MiddleLeft
        title.Dock = DockStyle.Fill
        title.AutoEllipsis = True
        heading.Controls.Add(title, 0, 0)
        Dim subtitle = TextLabel("Windows Image Toolkit", UiFont(10),
                                 Color.FromArgb(241, 250, 255))
        subtitle.Dock = DockStyle.Fill
        subtitle.TextAlign = ContentAlignment.MiddleLeft
        subtitle.AutoEllipsis = True
        heading.Controls.Add(subtitle, 0, 1)

        ' Reuse the *original* Tools menu items and their existing event handlers.
        ' A real bordered button opens a context menu instead of a barely visible
        ' menu label which looks like plain text.
        Dim toolsMenu As New ContextMenuStrip With {
            .Font = UiFont(10), .BackColor = Color.White,
            .ForeColor = TextInk, .ShowImageMargin = False
        }
        If menu.Items.Count > 0 Then
            Dim originalTools = TryCast(menu.Items(0), ToolStripMenuItem)
            If originalTools IsNot Nothing Then
                While originalTools.DropDownItems.Count > 0
                    Dim currentItem = originalTools.DropDownItems(0)
                    originalTools.DropDownItems.RemoveAt(0)
                    toolsMenu.Items.Add(currentItem)
                End While
            End If
        End If
        menu.Visible = False

        Dim toolsButton As New Button With {
            .Name = "btnToolsMenu", .Text = "Tools  " & ChrW(&H25BE),
            .Dock = DockStyle.Fill, .Margin = New Padding(10, 14, 0, 14)
        }
        StyleButton(toolsButton, "neutral")
        toolsButton.FlatAppearance.BorderColor = Color.FromArgb(202, 228, 247)
        toolsButton.BackColor = Color.White
        toolsButton.ForeColor = Color.FromArgb(27, 78, 132)
        toolsButton.ContextMenuStrip = toolsMenu
        AddHandler toolsButton.Click, Sub(sender, e)
            toolsMenu.Show(toolsButton, New Point(0, toolsButton.Height))
        End Sub
        heroGrid.Controls.Add(toolsButton, 2, 0)

        Dim updatesStatus As New ToolStripMenuItem("Updates: checking...") With {
            .Enabled = False, .ForeColor = Muted
        }
        Dim updatesMenuItem As New ToolStripMenuItem("Check for updates...")
        AddHandler updatesMenuItem.Click, Sub(sender, e) UpdateManager.ShowUpdateDialog(frm)
        toolsMenu.Items.Add(New ToolStripSeparator())
        Dim emailSupportMenu As New ToolStripMenuItem("Email Support...") With {
            .Name = "mnuEmailSupport",
            .ToolTipText = SupportContact.SupportEmail
        }
        AddHandler emailSupportMenu.Click, Sub(sender, e)
            SupportContact.OpenSupportEmail(frm)
        End Sub
        toolsMenu.Items.Add(emailSupportMenu)
        Dim reportProblemMenu As New ToolStripMenuItem("Report error to GitHub...") With {
            .Name = "mnuReportError",
            .ToolTipText = "Review and sanitize diagnostics before opening a GitHub Issue draft."
        }
        AddHandler reportProblemMenu.Click, Sub(sender, e)
            Using viewer As New ErrorReportForm(output.Text, "DISM output / Tools")
                viewer.ShowDialog(frm)
            End Using
        End Sub
        toolsMenu.Items.Add(reportProblemMenu)
        Dim testCenterMenu As New ToolStripMenuItem("Run complete safe tests...") With {
            .Name = "mnuIntegratedTestCenter",
            .ToolTipText = "Test DISM, WIM/SWM, environment, DPI and reporting without image changes."
        }
        AddHandler testCenterMenu.Click, Sub(sender, e)
            Using viewer As New IntegratedTestCenterForm()
                viewer.ShowDialog(frm)
            End Using
        End Sub
        toolsMenu.Items.Add(testCenterMenu)
        toolsMenu.Items.Add(New ToolStripSeparator())
        toolsMenu.Items.Add(updatesStatus)
        toolsMenu.Items.Add(updatesMenuItem)

        ' Quiet one-shot check: no popup, no repeated MFA or background polling.
        ' The menu signals newer stable versions without interrupting DISM work.
        AddHandler frm.Shown, Sub(sender, e)
            ' The legacy form sets cmbIndex.Text during Load. Clear the
            ' default blue text selection only after this initialization.
            Dim selector = TryCast(tabs.TabPages(0).Controls.Find("cmbIndex", True).
                                   FirstOrDefault(), ComboBox)
            If selector IsNot Nothing AndAlso
               selector.DropDownStyle <> ComboBoxStyle.DropDownList Then
                selector.SelectionStart = selector.Text.Length
                selector.SelectionLength = 0
            End If
            NotifyUpdateInMenuAsync(toolsButton, updatesMenuItem, updatesStatus)
        End Sub

        Dim workspace As New TableLayoutPanel With {
            .Dock = DockStyle.Fill, .Margin = Padding.Empty, .ColumnCount = 2,
            .RowCount = 1, .BackColor = Backdrop
        }
        workspace.RowStyles.Add(New RowStyle(SizeType.Percent, 100))
        workspace.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute, 242))
        workspace.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100))
        split.Panel1.Controls.Add(workspace)

        Dim side As New FlowLayoutPanel With {
            .Dock = DockStyle.Fill, .AutoScroll = True,
            .FlowDirection = FlowDirection.TopDown, .WrapContents = False,
            .Padding = New Padding(11, 17, 9, 14), .Margin = Padding.Empty,
            .BackColor = Color.FromArgb(226, 239, 255)
        }
        workspace.Controls.Add(side, 0, 0)
        Dim navTitle = TextLabel("  WORKSPACES", UiFont(10, True),
                                 Color.FromArgb(12, 70, 180))
        navTitle.Height = 42
        navTitle.Width = 210
        side.Controls.Add(navTitle)

        ' WIM 360 is a full embedded workspace. The original nine tabs retain
        ' their indexes and handlers. Selecting an image never mounts it.
        Dim wimPage As New TabPage("WIM 360") With {
            .Name = "TabPageWim360", .BackColor = Color.White,
            .UseVisualStyleBackColor = False
        }
        Dim wim360 = New Wim360Dashboard(
            Function()
                Dim source = TryCast(tabs.TabPages(0).Controls.Find(
                    "txtWIM", True).FirstOrDefault(), TextBox)
                Return If(source Is Nothing, "", source.Text)
            End Function,
            Sub(path, imageIndex)
                Dim mountPage = tabs.TabPages(0)
                Dim source = TryCast(mountPage.Controls.Find(
                    "txtWIM", True).FirstOrDefault(), TextBox)
                Dim indexSpinner = TryCast(mountPage.Controls.Find(
                    "nudImageIndex", True).FirstOrDefault(), NumericUpDown)
                If source IsNot Nothing Then source.Text = path
                If indexSpinner IsNot Nothing AndAlso imageIndex > 0 Then
                    ' The visible spinner synchronizes the original hidden
                    ' cmbIndex field through its ValueChanged handler.
                    Dim wantedIndex = CDec(imageIndex)
                    If wantedIndex > indexSpinner.Maximum Then
                        indexSpinner.Maximum = wantedIndex
                    End If
                    indexSpinner.Value = wantedIndex
                End If
                tabs.SelectedIndex = 0
            End Sub)
        wimPage.Controls.Add(wim360)
        tabs.TabPages.Add(wimPage)
        Dim wimPageIndex = tabs.TabPages.Count - 1

        ' Driver Studio previews source INF packages against a VERIFIED
        ' already-mounted index. It never adds or removes drivers automatically.
        Dim studioPage As New TabPage("Driver Studio") With {
            .Name = "TabPageDriverStudio", .BackColor = Color.White,
            .UseVisualStyleBackColor = False
        }
        Dim driverStudio = New DriverStudioPanel(
            Function()
                Dim source = TryCast(tabs.TabPages(1).Controls.Find(
                    "txtDriverLocation", True).FirstOrDefault(), TextBox)
                Return If(source Is Nothing, "", source.Text)
            End Function,
            Function()
                Dim wimPath = TryCast(tabs.TabPages(0).Controls.Find(
                    "txtWIM", True).FirstOrDefault(), TextBox)
                Return If(wimPath Is Nothing, "", wimPath.Text)
            End Function,
            Function()
                Dim spinner = TryCast(tabs.TabPages(0).Controls.Find(
                    "nudImageIndex", True).FirstOrDefault(), NumericUpDown)
                Return If(spinner Is Nothing, 1, CInt(spinner.Value))
            End Function,
            Function()
                Dim mount = TryCast(tabs.TabPages(0).Controls.Find(
                    "txtMount", True).FirstOrDefault(), TextBox)
                Return If(mount Is Nothing, "", mount.Text)
            End Function,
            Sub(folderPath)
                Dim source = TryCast(tabs.TabPages(1).Controls.Find(
                    "txtDriverLocation", True).FirstOrDefault(), TextBox)
                If source IsNot Nothing Then source.Text = folderPath
                tabs.SelectedIndex = 1
            End Sub)
        studioPage.Controls.Add(driverStudio)
        tabs.TabPages.Add(studioPage)
        Dim studioPageIndex = tabs.TabPages.Count - 1

        Dim wimNav As New Button With {
            .Name = "navWim360", .Text = "  WIM 360",
            .Size = New Size(211, 45), .Margin = New Padding(2, 0, 2, 13),
            .FlatStyle = FlatStyle.Flat,
            .TextAlign = ContentAlignment.MiddleLeft,
            .Font = UiFont(10, True), .BackColor = Color.White,
            .ForeColor = TextInk, .Cursor = Cursors.Hand,
            .UseVisualStyleBackColor = False
        }
        wimNav.FlatAppearance.BorderColor = Color.FromArgb(198, 215, 234)
        AddHandler wimNav.Click, Sub(sender, e) tabs.SelectedIndex = wimPageIndex
        side.Controls.Add(wimNav)

        Dim studioNav As New Button With {
            .Name = "navDriverStudio", .Text = "  Driver Studio",
            .Size = New Size(211, 45), .Margin = New Padding(2, 0, 2, 14),
            .FlatStyle = FlatStyle.Flat, .TextAlign = ContentAlignment.MiddleLeft,
            .Font = UiFont(9.7F, True), .BackColor = Color.White,
            .ForeColor = TextInk, .Cursor = Cursors.Hand,
            .UseVisualStyleBackColor = False
        }
        studioNav.FlatAppearance.BorderColor = Color.FromArgb(198, 215, 234)
        AddHandler studioNav.Click, Sub(sender, e)
            tabs.SelectedIndex = studioPageIndex
        End Sub
        side.Controls.Add(studioNav)

        Dim sectionNames As String() = {"Mount & Inspect", "Drivers", "Packages",
            "Windows Features", "Editions", "Unattend XML",
            "Applications", "Capture WIM", "Apply WIM"}
        Dim shortNavigationNames As String() = {"Mount", "Drivers", "Packages",
            "Features", "Editions", "Unattend", "Apps", "Capture", "Apply"}
        Dim sectionColors As Color() = {Blue, Green, Blue, Green, Blue,
                                       Green, Blue, Green, Blue}
        Dim navButtons As New List(Of Button)()
        Dim pageHeading = TextLabel(sectionNames(0).ToUpperInvariant(), UiFont(12.0F, True), TextInk)
        pageHeading.Dock = DockStyle.Fill
        pageHeading.TextAlign = ContentAlignment.MiddleLeft
        Dim setActive As Action(Of Integer) = Sub(index)
            For n As Integer = 0 To navButtons.Count - 1
                Dim b = navButtons(n)
                b.BackColor = If(n = index, Blue, Color.White)
                b.ForeColor = If(n = index, Color.White, TextInk)
                b.FlatAppearance.BorderSize = 1
                b.FlatAppearance.BorderColor = If(n = index, Blue, Color.FromArgb(211, 225, 243))
                b.FlatAppearance.MouseOverBackColor = If(n = index,
                       Color.FromArgb(12, 70, 180), Color.FromArgb(211, 230, 254))
            Next
            Dim onWim = index = wimPageIndex
            Dim onStudio = index = studioPageIndex
            wimNav.BackColor = If(onWim, Blue, Color.White)
            wimNav.ForeColor = If(onWim, Color.White, TextInk)
            wimNav.FlatAppearance.BorderColor = If(onWim, Blue,
                                                     Color.FromArgb(198, 215, 234))
            studioNav.BackColor = If(onStudio, Blue, Color.White)
            studioNav.ForeColor = If(onStudio, Color.White, TextInk)
            studioNav.FlatAppearance.BorderColor = If(onStudio, Blue,
                                                        Color.FromArgb(198, 215, 234))
            pageHeading.Text = If(onWim, "WIM 360",
                                  If(onStudio, "DRIVER STUDIO",
                                     sectionNames(Math.Min(index, 8)).ToUpperInvariant()))
        End Sub

        For i As Integer = 0 To sectionNames.Length - 1
            Dim index As Integer = i
            Dim navBtn As New Button With {
                .Name = "nav" & i.ToString(), .Text = (i + 1).ToString("00") &
                   "  " & shortNavigationNames(i),
                .Size = New Size(211, 43), .Margin = New Padding(2, 1, 2, 6),
                .FlatStyle = FlatStyle.Flat, .TextAlign = ContentAlignment.MiddleLeft,
                .Font = UiFont(9.5F, True), .ForeColor = TextInk, .BackColor = Color.White,
                .Cursor = Cursors.Hand, .UseVisualStyleBackColor = False
            }
            navBtn.FlatAppearance.BorderSize = 1
            navBtn.FlatAppearance.BorderColor = Color.FromArgb(211, 225, 243)
            AddHandler navBtn.Click, Sub(sender, e)
                tabs.SelectedIndex = index
            End Sub
            navButtons.Add(navBtn)
            side.Controls.Add(navBtn)
        Next
        AddHandler side.SizeChanged, Sub(sender, e)
            wimNav.Width = Math.Max(176, side.ClientSize.Width -
                                   side.Padding.Left - side.Padding.Right - 13)
            studioNav.Width = wimNav.Width
            For Each b In navButtons
                b.Width = Math.Max(176, side.ClientSize.Width - side.Padding.Left -
                                   side.Padding.Right - 13)
            Next
        End Sub
        AddHandler tabs.SelectedIndexChanged, Sub(sender, e)
            setActive(Math.Max(0, tabs.SelectedIndex))
            If tabs.SelectedIndex = wimPageIndex Then wim360.ActivateFromMount()
            If tabs.SelectedIndex = studioPageIndex Then
                driverStudio.ActivateFromDrivers()
            End If
        End Sub
        setActive(0)

        Dim content As New TableLayoutPanel With {
            .Dock = DockStyle.Fill, .Padding = New Padding(19, 15, 19, 10),
            .Margin = Padding.Empty, .ColumnCount = 1, .RowCount = 2,
            .BackColor = Backdrop
        }
        content.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100))
        content.RowStyles.Add(New RowStyle(SizeType.Absolute, 44))
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
            If page.Name = "TabPageWim360" OrElse
               page.Name = "TabPageDriverStudio" Then Continue For
            page.BackColor = Color.White
            page.UseVisualStyleBackColor = False
            page.AutoScroll = True
            StyleControls(page, tips)
        Next
        ResponsiveSectionLayout.Install(tabs)

        Dim logArea As New TableLayoutPanel With {
            .Dock = DockStyle.Fill, .RowCount = 2, .ColumnCount = 1,
            .Margin = Padding.Empty, .Padding = New Padding(13, 7, 13, 12),
            .BackColor = Backdrop
        }
        logArea.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100))
        logArea.RowStyles.Add(New RowStyle(SizeType.Absolute, 54))
        logArea.RowStyles.Add(New RowStyle(SizeType.Percent, 100))
        split.Panel2.Controls.Add(logArea)
        Dim logHeader As New TableLayoutPanel With {
            .Dock = DockStyle.Fill, .ColumnCount = 5, .RowCount = 1,
            .BackColor = Color.FromArgb(230, 240, 250),
            .Padding = New Padding(12, 5, 10, 5),
            .Margin = Padding.Empty
        }
        logHeader.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100))
        logHeader.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute, 113))
        logHeader.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute, 124))
        logHeader.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute, 87))
        logHeader.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute, 87))
        logHeader.RowStyles.Add(New RowStyle(SizeType.Percent, 100))
        logArea.Controls.Add(logHeader, 0, 0)
        Dim logTitle = TextLabel("DISM OUTPUT", UiFont(9.5F, True), TextInk)
        tips.SetToolTip(logTitle, "Drag the divider to resize DISM Output.")
        logTitle.Dock = DockStyle.Fill
        logTitle.AutoEllipsis = True
        logTitle.TextAlign = ContentAlignment.MiddleLeft
        logHeader.Controls.Add(logTitle, 0, 0)

        Dim restoreHeight As Integer = savedOutputHeight
        Dim expanded As Boolean = False
        Dim expand As New Button With {
            .Name = "btnExpandOutput", .Text = "Expand",
            .Dock = DockStyle.Fill, .Margin = New Padding(4, 3, 4, 3)
        }
        StyleButton(expand, "browse")
        AddHandler expand.Click, Sub(sender, e)
            If Not expanded Then
                restoreHeight = split.Panel2.Height
                split.SplitterDistance = split.Panel1MinSize
                expanded = True
                expand.Text = "Restore"
            Else
                Dim maxDistance = split.Height - split.SplitterWidth - split.Panel2MinSize
                Dim targetDistance = split.Height - split.SplitterWidth - restoreHeight
                If maxDistance > split.Panel1MinSize Then
                    split.SplitterDistance = Math.Max(split.Panel1MinSize,
                                                     Math.Min(maxDistance, targetDistance))
                End If
                expanded = False
                expand.Text = "Expand"
            End If
        End Sub
        logHeader.Controls.Add(expand, 1, 0)
        tips.SetToolTip(expand,
                        "Expand the DISM output panel or restore its previous height." &
                        Environment.NewLine &
                        "You can also drag the horizontal divider with the mouse.")

        Dim copy As New Button With {
            .Name = "btnCopyOutput", .Text = "Copy log",
            .Dock = DockStyle.Fill, .Margin = New Padding(4, 2, 4, 2)
        }
        StyleButton(copy, "browse")
        AddHandler copy.Click, Sub(sender, e)
            If Not String.IsNullOrWhiteSpace(output.Text) Then Clipboard.SetText(output.Text)
        End Sub
        logHeader.Controls.Add(copy, 2, 0)
        Dim clear As New Button With {
            .Name = "btnClearOutput", .Text = "Clear",
            .Dock = DockStyle.Fill, .Margin = New Padding(4, 2, 4, 2)
        }
        StyleButton(clear, "neutral")
        AddHandler clear.Click, Sub(sender, e) output.Clear()
        logHeader.Controls.Add(clear, 3, 0)
        exitButton.Text = "Exit"
        exitButton.Dock = DockStyle.Fill
        exitButton.Margin = New Padding(4, 2, 0, 2)
        StyleButton(exitButton, "neutral")
        logHeader.Controls.Add(exitButton, 4, 0)
        FitTableButtonCells(logHeader)

        output.Dock = DockStyle.Fill
        output.BackColor = Color.White
        output.ForeColor = TextInk
        output.Font = New Font("Consolas", 9.5F, FontStyle.Regular)
        output.BorderStyle = BorderStyle.FixedSingle
        output.ReadOnly = True
        output.Margin = Padding.Empty
        logArea.Controls.Add(output, 0, 1)
        ' Remember window size, position and maximized state safely.
        WindowGeometry.Install(frm)
        frm.ResumeLayout(True)
    End Sub

    Private Shared Async Sub NotifyUpdateInMenuAsync(toolsButton As Button,
                                                       menuItem As ToolStripMenuItem,
                                                       statusItem As ToolStripMenuItem)
        Try
            Dim available = Await UpdateManager.CheckForUpdatesAsync()
            If toolsButton.IsDisposed OrElse statusItem.IsDisposed Then Return
            If String.IsNullOrWhiteSpace(available) Then
                statusItem.Text = "No newer verified release detected"
                statusItem.ForeColor = Muted
                Return
            End If
            toolsButton.Text = "Tools  " & ChrW(&H2022) & " " & ChrW(&H25BE)
            toolsButton.ForeColor = Color.FromArgb(12, 110, 67)
            toolsButton.BackColor = Color.FromArgb(233, 250, 240)
            statusItem.Text = "New stable release: " & available
            statusItem.ForeColor = Color.FromArgb(12, 124, 75)
            menuItem.Text = "Update to " & available & "..."
            menuItem.ForeColor = Color.FromArgb(12, 124, 75)
            menuItem.Font = UiFont(10, True)
            Dim tips As New ToolTip()
            tips.SetToolTip(toolsButton, "DISM GUI " & available &
                                          " is available. Open Tools > Update.")
        Catch
            If Not statusItem.IsDisposed Then
                statusItem.Text = "Update status unavailable"
                statusItem.ForeColor = Muted
            End If
        End Try
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
