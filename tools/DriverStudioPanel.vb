Imports System
Imports System.Collections.Generic
Imports System.Drawing
Imports System.IO
Imports System.Linq
Imports System.Threading
Imports System.Threading.Tasks
Imports System.Windows.Forms

' Embedded Driver Studio — non-destructive INF + mounted-index comparison.
' The legacy "Add drivers" button is NOT invoked by this view: importing a
' whole folder would ignore per-package skip decisions.
Public NotInheritable Class DriverStudioPanel
    Inherits UserControl

    Private ReadOnly currentSource As Func(Of String)
    Private ReadOnly currentWim As Func(Of String)
    Private ReadOnly currentIndex As Func(Of Integer)
    Private ReadOnly currentMount As Func(Of String)
    Private ReadOnly openDrivers As Action(Of String)
    Private ReadOnly viewport As Panel
    Private ReadOnly rootLayout As TableLayoutPanel
    Private ReadOnly sourceBox As TextBox
    Private ReadOnly recursive As CheckBox
    Private ReadOnly scanButton As Button
    Private ReadOnly compareButton As Button
    Private ReadOnly copyButton As Button
    Private ReadOnly transferButton As Button
    Private ReadOnly targetLabel As Label
    Private ReadOnly statusLabel As Label
    Private ReadOnly totalMetric As Label
    Private ReadOnly newMetric As Label
    Private ReadOnly sameMetric As Label
    Private ReadOnly grid As DataGridView
    Private ReadOnly detailBox As TextBox
    Private decisions As New List(Of DriverStudioEngine.Decision)()
    Private isWorking As Boolean
    Private lastTarget As String = "Not checked"

    Public Sub New(getSource As Func(Of String),
                   getWim As Func(Of String),
                   getIndex As Func(Of Integer),
                   getMount As Func(Of String),
                   useSourceInDrivers As Action(Of String))
        currentSource = getSource
        currentWim = getWim
        currentIndex = getIndex
        currentMount = getMount
        openDrivers = useSourceInDrivers

        Me.Name = "DriverStudioPanel"
        Me.Dock = DockStyle.Fill
        Me.Margin = Padding.Empty
        Me.BackColor = Color.White
        Me.Font = ModernTheme.UiFont(9.5F)
        Me.AutoScaleMode = AutoScaleMode.Font

        viewport = New Panel With {
            .Name = "DriverStudioViewport", .Dock = DockStyle.Fill,
            .AutoScroll = True, .BackColor = Color.White
        }
        Controls.Add(viewport)
        rootLayout = New TableLayoutPanel With {
            .Name = "DriverStudioLayout",
            .ColumnCount = 1, .RowCount = 8, .Dock = DockStyle.Top,
            .Width = 660, .Height = 760,
            .BackColor = Color.White, .Margin = Padding.Empty,
            .Padding = New Padding(15, 12, 15, 18)
        }
        rootLayout.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100))
        For Each h As Integer In {59, 68, 50, 80, 38, 270, 130, 65}
            rootLayout.RowStyles.Add(New RowStyle(SizeType.Absolute, h))
        Next
        viewport.Controls.Add(rootLayout)
        AddHandler viewport.ClientSizeChanged, Sub(sender, e)
            Dim wanted = Math.Max(400, viewport.ClientSize.Width - 1)
            If rootLayout.Width <> wanted Then rootLayout.Width = wanted
        End Sub

        Dim heading As New TableLayoutPanel With {
            .RowCount = 2, .ColumnCount = 1, .Dock = DockStyle.Fill,
            .Margin = Padding.Empty, .BackColor = Color.White
        }
        heading.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100))
        heading.RowStyles.Add(New RowStyle(SizeType.Percent, 58))
        heading.RowStyles.Add(New RowStyle(SizeType.Percent, 42))
        rootLayout.Controls.Add(heading, 0, 0)
        heading.Controls.Add(WriteLabel("DRIVER STUDIO  |  SMART VERSION DETECTION",
                                        12.4F, True, ModernTheme.TextInk), 0, 0)
        heading.Controls.Add(WriteLabel("Read-only scan. Compares with the selected mounted WIM index.",
                                        9.1F, False, ModernTheme.Muted), 0, 1)

        Dim sourceRow As New TableLayoutPanel With {
            .Dock = DockStyle.Fill, .Margin = New Padding(0, 4, 0, 6),
            .ColumnCount = 3, .RowCount = 1, .BackColor = Color.White
        }
        sourceRow.RowStyles.Add(New RowStyle(SizeType.Percent, 100))
        sourceRow.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100))
        sourceRow.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute, 144))
        sourceRow.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute, 116))
        rootLayout.Controls.Add(sourceRow, 0, 1)
        sourceBox = New TextBox With {
            .Name = "txtStudioSource", .Dock = DockStyle.Fill,
            .PlaceholderText = "Driver folder or .inf file",
            .BorderStyle = BorderStyle.FixedSingle,
            .Font = ModernTheme.UiFont(9.7F), .BackColor = Color.White,
            .Margin = New Padding(1, 13, 7, 11)
        }
        sourceRow.Controls.Add(sourceBox, 0, 0)

        Dim browse As New Button With {
            .Name = "btnStudioBrowse", .Text = "Choose folder",
            .Dock = DockStyle.Fill, .Margin = New Padding(3, 6, 5, 7)
        }
        ModernTheme.StyleButton(browse, "browse")
        AddHandler browse.Click, Sub(sender, e)
            Using picker As New FolderBrowserDialog With {
                .Description = "Select a folder containing INF driver packages",
                .UseDescriptionForTitle = True,
                .ShowNewFolderButton = False
            }
                If Directory.Exists(sourceBox.Text.Trim()) Then
                    picker.InitialDirectory = sourceBox.Text.Trim()
                End If
                If picker.ShowDialog(Me.FindForm()) = DialogResult.OK Then
                    sourceBox.Text = picker.SelectedPath
                End If
            End Using
        End Sub
        sourceRow.Controls.Add(browse, 1, 0)

        scanButton = New Button With {
            .Name = "btnStudioScan", .Text = "Scan INF",
            .Dock = DockStyle.Fill, .Margin = New Padding(2, 6, 0, 7)
        }
        ModernTheme.StyleButton(scanButton, "primary")
        AddHandler scanButton.Click, AddressOf ScanClicked
        sourceRow.Controls.Add(scanButton, 2, 0)

        Dim settings As New TableLayoutPanel With {
            .Dock = DockStyle.Fill, .ColumnCount = 2, .RowCount = 1,
            .Margin = Padding.Empty, .BackColor = Color.White
        }
        settings.RowStyles.Add(New RowStyle(SizeType.Percent, 100))
        settings.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute, 195))
        settings.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100))
        rootLayout.Controls.Add(settings, 0, 2)
        recursive = New CheckBox With {
            .Name = "chkStudioRecursive", .Text = "Include subfolders",
            .Checked = True, .AutoSize = False,
            .Dock = DockStyle.Fill, .Font = ModernTheme.UiFont(9.4F),
            .Margin = New Padding(3, 3, 6, 3)
        }
        settings.Controls.Add(recursive, 0, 0)
        compareButton = New Button With {
            .Name = "btnStudioCompare", .Text = "Compare with mounted index",
            .Dock = DockStyle.Fill, .Margin = New Padding(1, 3, 0, 4)
        }
        ModernTheme.StyleButton(compareButton, "secondary")
        AddHandler compareButton.Click, AddressOf CompareClicked
        settings.Controls.Add(compareButton, 1, 0)

        Dim metrics As New TableLayoutPanel With {
            .Name = "DriverStudioMetrics", .Dock = DockStyle.Fill,
            .ColumnCount = 3, .RowCount = 1, .Margin = New Padding(0, 7, 0, 7),
            .BackColor = Color.White
        }
        metrics.RowStyles.Add(New RowStyle(SizeType.Percent, 100))
        For i As Integer = 1 To 3
            metrics.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, CSng(100.0 / 3.0)))
        Next
        rootLayout.Controls.Add(metrics, 0, 3)
        totalMetric = AddMetric(metrics, 0, "SOURCE PACKAGES")
        newMetric = AddMetric(metrics, 1, "NEW / NEWER")
        sameMetric = AddMetric(metrics, 2, "SAME VERSION")

        Dim textStatus As New TableLayoutPanel With {
            .Dock = DockStyle.Fill, .ColumnCount = 1, .RowCount = 2,
            .Margin = Padding.Empty, .BackColor = Color.White
        }
        textStatus.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100))
        textStatus.RowStyles.Add(New RowStyle(SizeType.Percent, 50))
        textStatus.RowStyles.Add(New RowStyle(SizeType.Percent, 50))
        rootLayout.Controls.Add(textStatus, 0, 4)
        targetLabel = WriteLabel("Target WIM index: not selected",
                                 8.9F, False, ModernTheme.Muted)
        targetLabel.Name = "lblStudioTarget"
        textStatus.Controls.Add(targetLabel, 0, 0)
        statusLabel = WriteLabel("Select a driver folder to inspect INF files.",
                                 8.9F, False, ModernTheme.Muted)
        statusLabel.Name = "lblStudioStatus"
        textStatus.Controls.Add(statusLabel, 0, 1)

        grid = New DataGridView With {
            .Name = "gridStudioDrivers", .Dock = DockStyle.Fill,
            .ReadOnly = False, .MultiSelect = False,
            .AllowUserToAddRows = False, .AllowUserToDeleteRows = False,
            .AllowUserToResizeRows = False, .RowHeadersVisible = False,
            .SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            .BackgroundColor = Color.White,
            .GridColor = Color.FromArgb(225, 233, 246),
            .BorderStyle = BorderStyle.FixedSingle,
            .AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            .ColumnHeadersHeight = 33, .RowTemplate = New DataGridViewRow() With {.Height = 30},
            .EnableHeadersVisualStyles = False,
            .Margin = New Padding(0, 5, 0, 8)
        }
        grid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(235, 244, 253)
        grid.ColumnHeadersDefaultCellStyle.Font = ModernTheme.UiFont(9.1F, True)
        grid.DefaultCellStyle.Font = ModernTheme.UiFont(9.1F)
        grid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(216, 235, 253)
        grid.DefaultCellStyle.SelectionForeColor = ModernTheme.TextInk
        Dim checkedCol As New DataGridViewCheckBoxColumn With {
            .Name = "colStudioInclude", .HeaderText = "Plan", .FillWeight = 44
        }
        grid.Columns.Add(checkedCol)
        grid.Columns.Add("colStudioInf", "INF file")
        grid.Columns.Add("colStudioIncoming", "Incoming")
        grid.Columns.Add("colStudioExisting", "In WIM")
        grid.Columns.Add("colStudioStatus", "Decision")
        grid.Columns(1).FillWeight = 145
        grid.Columns(2).FillWeight = 98
        grid.Columns(3).FillWeight = 98
        grid.Columns(4).FillWeight = 105
        For i As Integer = 1 To 4
            grid.Columns(i).ReadOnly = True
        Next
        AddHandler grid.SelectionChanged, AddressOf GridSelected
        AddHandler grid.CurrentCellDirtyStateChanged, Sub(sender, e)
            If grid.IsCurrentCellDirty Then grid.CommitEdit(
                DataGridViewDataErrorContexts.Commit)
        End Sub
        AddHandler grid.CellValueChanged, Sub(sender, e)
            If e.RowIndex >= 0 AndAlso e.RowIndex < decisions.Count AndAlso
               e.ColumnIndex = 0 Then
                Dim value = grid.Rows(e.RowIndex).Cells(0).Value
                decisions(e.RowIndex).Selected = TypeOf value Is Boolean AndAlso CBool(value)
            End If
        End Sub
        rootLayout.Controls.Add(grid, 0, 5)

        Dim detailsLayout As New TableLayoutPanel With {
            .Dock = DockStyle.Fill, .RowCount = 2, .ColumnCount = 1,
            .Margin = Padding.Empty, .BackColor = Color.White
        }
        detailsLayout.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100))
        detailsLayout.RowStyles.Add(New RowStyle(SizeType.Absolute, 28))
        detailsLayout.RowStyles.Add(New RowStyle(SizeType.Percent, 100))
        rootLayout.Controls.Add(detailsLayout, 0, 6)
        detailsLayout.Controls.Add(WriteLabel("DRIVER PACKAGE  |  DETAILS",
                                              9.2F, True, ModernTheme.Blue), 0, 0)
        detailBox = New TextBox With {
            .Name = "txtStudioDetails", .Multiline = True, .ReadOnly = True,
            .ScrollBars = ScrollBars.Vertical, .Dock = DockStyle.Fill,
            .Font = ModernTheme.UiFont(9.1F),
            .BackColor = Color.FromArgb(249, 252, 255),
            .BorderStyle = BorderStyle.FixedSingle,
            .Text = "Driver rankings also depend on device matching, signature and date.",
            .Margin = New Padding(0, 0, 0, 6)
        }
        detailsLayout.Controls.Add(detailBox, 0, 1)

        Dim footer As New TableLayoutPanel With {
            .Dock = DockStyle.Fill, .ColumnCount = 3, .RowCount = 1,
            .Margin = New Padding(0, 11, 0, 0), .BackColor = Color.White
        }
        footer.RowStyles.Add(New RowStyle(SizeType.Percent, 100))
        footer.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute, 155))
        footer.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute, 206))
        footer.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100))
        rootLayout.Controls.Add(footer, 0, 7)
        copyButton = New Button With {
            .Name = "btnStudioCopyPlan", .Text = "Copy comparison",
            .Dock = DockStyle.Fill, .Enabled = False,
            .Margin = New Padding(0, 3, 10, 4)
        }
        ModernTheme.StyleButton(copyButton, "browse")
        AddHandler copyButton.Click, Sub(sender, e)
            Try
                Clipboard.SetText(DriverStudioEngine.FormatReport(decisions, lastTarget))
                statusLabel.Text = "Read-only comparison copied to clipboard."
            Catch ex As Exception
                statusLabel.Text = "Copy failed: " & ex.Message
            End Try
        End Sub
        footer.Controls.Add(copyButton, 0, 0)
        transferButton = New Button With {
            .Name = "btnStudioGoDrivers", .Text = "Open Drivers section",
            .Dock = DockStyle.Fill, .Enabled = False,
            .Margin = New Padding(0, 3, 10, 4)
        }
        ModernTheme.StyleButton(transferButton, "neutral")
        AddHandler transferButton.Click, AddressOf OpenDriversClicked
        footer.Controls.Add(transferButton, 1, 0)
        Dim warning = WriteLabel("Preview only • no drivers added",
                                  8.5F, False, ModernTheme.Muted)
        warning.TextAlign = ContentAlignment.MiddleRight
        footer.Controls.Add(warning, 2, 0)
        AddHandler sourceBox.TextChanged, Sub(sender, e)
            InvalidateComparison()
        End Sub
    End Sub

    Private Shared Function WriteLabel(value As String, size As Single,
                                       isBold As Boolean, color As Color) As Label
        Return New Label With {
            .Text = value, .Font = ModernTheme.UiFont(size, isBold),
            .ForeColor = color, .BackColor = Color.Transparent,
            .Dock = DockStyle.Fill, .TextAlign = ContentAlignment.MiddleLeft,
            .AutoEllipsis = True, .Margin = Padding.Empty
        }
    End Function

    Private Shared Function AddMetric(parent As TableLayoutPanel, col As Integer,
                                       name As String) As Label
        Dim tile As New TableLayoutPanel With {
            .Dock = DockStyle.Fill, .ColumnCount = 1, .RowCount = 2,
            .BackColor = Color.FromArgb(236, 245, 254),
            .Padding = New Padding(11, 5, 7, 5),
            .Margin = New Padding(0, 0, If(col = 2, 0, 9), 0)
        }
        tile.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100))
        tile.RowStyles.Add(New RowStyle(SizeType.Percent, 44))
        tile.RowStyles.Add(New RowStyle(SizeType.Percent, 56))
        parent.Controls.Add(tile, col, 0)
        tile.Controls.Add(WriteLabel(name, 8.1F, True, ModernTheme.Muted), 0, 0)
        Dim value = WriteLabel("—", 12, True, ModernTheme.TextInk)
        value.Name = "metricStudio" & col.ToString()
        tile.Controls.Add(value, 0, 1)
        Return value
    End Function

    Public Sub ActivateFromDrivers()
        If Not isWorking AndAlso String.IsNullOrWhiteSpace(sourceBox.Text) Then
            Dim path = currentSource()
            If Not String.IsNullOrWhiteSpace(path) Then sourceBox.Text = path.Trim()
        End If
        targetLabel.Text = "Target: " & If(String.IsNullOrWhiteSpace(currentWim()),
            "No WIM selected", Path.GetFileName(currentWim())) &
            "  |  Index " & currentIndex().ToString() &
            "  |  Mount: " & If(String.IsNullOrWhiteSpace(currentMount()),
                "Not set", currentMount())
    End Sub

    Private Sub InvalidateComparison()
        decisions.Clear()
        grid.Rows.Clear()
        copyButton.Enabled = False
        transferButton.Enabled = False
        totalMetric.Text = "—"
        newMetric.Text = "—"
        sameMetric.Text = "—"
        lastTarget = "Not checked"
        statusLabel.Text = "Folder changed. Scan INF before comparing installed versions."
    End Sub

    Private Sub WorkState(busy As Boolean)
        isWorking = busy
        scanButton.Enabled = Not busy
        compareButton.Enabled = Not busy
        recursive.Enabled = Not busy
        sourceBox.ReadOnly = busy
    End Sub

    Private Async Sub ScanClicked(sender As Object, e As EventArgs)
        If isWorking Then Return
        Dim selectedPath = sourceBox.Text.Trim()
        Dim includeSubfolders = recursive.Checked
        WorkState(True)
        statusLabel.Text = "Scanning INF packages (read-only)..."
        Try
            Dim sourceItems = Await Task.Run(
                Function() DriverStudioEngine.ScanSource(selectedPath, includeSubfolders))
            If IsDisposed Then Return
            Dim results = DriverStudioEngine.CompareCandidates(
                sourceItems, New List(Of DriverStudioEngine.DriverPackage)(), False)
            ShowDecisions(results, "Image inventory not checked")
        Catch ex As Exception
            If IsDisposed Then Return
            statusLabel.Text = "INF scan failed: " & ex.Message
            InvalidateComparison()
            statusLabel.Text = "INF scan failed: " & ex.Message
        Finally
            If Not IsDisposed Then WorkState(False)
        End Try
    End Sub

    Private Async Sub CompareClicked(sender As Object, e As EventArgs)
        If isWorking Then Return
        Dim selectedPath = sourceBox.Text.Trim()
        Dim includeSubfolders = recursive.Checked
        Dim imagePath = currentWim()
        Dim index = currentIndex()
        Dim mount = currentMount()
        WorkState(True)
        statusLabel.Text = "Checking selected WIM index and reading driver versions..."
        Try
            Dim sourceItems = Await Task.Run(
                Function() DriverStudioEngine.ScanSource(selectedPath, includeSubfolders))
            If IsDisposed Then Return
            Dim installed = Await DriverStudioInventory.ReadMountedAsync(
                imagePath, index, mount, CancellationToken.None)
            If IsDisposed Then Return
            Dim results = DriverStudioEngine.CompareCandidates(sourceItems, installed, True)
            ShowDecisions(results, Path.GetFileName(imagePath) & " / Index " &
                          index.ToString() & " / " & mount)
            statusLabel.Text = "Compared " & results.Count.ToString() &
                               " INF(s) against " & installed.Count.ToString() &
                               " installed driver entries. No changes made."
        Catch ex As Exception
            If IsDisposed Then Return
            ' Never turn an unavailable inventory into "New" decisions.
            Dim results As New List(Of DriverStudioEngine.Decision)()
            Try
                Dim sourceItems = Await Task.Run(
                    Function() DriverStudioEngine.ScanSource(selectedPath, includeSubfolders))
                results = DriverStudioEngine.CompareCandidates(
                    sourceItems, New List(Of DriverStudioEngine.DriverPackage)(), False)
            Catch
            End Try
            If IsDisposed Then Return
            ShowDecisions(results, "Inventory NOT verified")
            statusLabel.Text = "Not checked: " & ex.Message
        Finally
            If Not IsDisposed Then WorkState(False)
        End Try
    End Sub

    Public Sub PreviewForTest(items As IEnumerable(Of DriverStudioEngine.Decision))
        ShowDecisions(items.ToList(), "CI fixture - mounted index verified")
    End Sub

    Private Sub ShowDecisions(results As List(Of DriverStudioEngine.Decision),
                              comparisonTarget As String)
        lastTarget = comparisonTarget
        decisions = results
        grid.SuspendLayout()
        Try
            grid.Rows.Clear()
            For Each d In results
                Dim number = grid.Rows.Add(d.Selected, d.Candidate.InfName,
                    d.Candidate.Version, d.InstalledVersion, d.Status)
                Dim cell = grid.Rows(number).Cells(4)
                If d.Status = "Same version" OrElse d.Status = "Duplicate source" Then
                    cell.Style.ForeColor = ModernTheme.Muted
                ElseIf d.Status = "New" OrElse d.Status = "Newer" Then
                    cell.Style.ForeColor = ModernTheme.Green
                End If
            Next
        Finally
            grid.ResumeLayout(True)
        End Try
        totalMetric.Text = results.Count.ToString()
        newMetric.Text = results.Count(Function(d)
            d.Status = "New" OrElse d.Status = "Newer").ToString()
        sameMetric.Text = results.Count(Function(d)
            d.Status = "Same version").ToString()
        statusLabel.Text = If(results.Count = 0,
            "No INF files found. Select a folder with driver INF packages.",
            "Preview ready. Choose an INF to see the comparison explanation.")
        copyButton.Enabled = results.Count > 0
        transferButton.Enabled = results.Count > 0
        If results.Count > 0 Then
            grid.ClearSelection()
            grid.Rows(0).Selected = True
            ShowSelected()
        Else
            detailBox.Text = "No INF packages found."
        End If
    End Sub

    Private Sub GridSelected(sender As Object, e As EventArgs)
        ShowSelected()
    End Sub

    Private Sub ShowSelected()
        If grid.SelectedRows.Count = 0 OrElse
           grid.SelectedRows(0).Index >= decisions.Count Then Return
        Dim d = decisions(grid.SelectedRows(0).Index)
        Dim src = d.Candidate
        detailBox.Text = d.Status & " — " & d.Detail & Environment.NewLine &
            "INF: " & src.FilePath & Environment.NewLine &
            "Provider: " & src.Provider & "   Class: " & src.ClassName &
            "   Version: " & src.Version & "   Date: " & src.DriverDate & Environment.NewLine &
            "Installed: " & If(d.Existing Is Nothing, "not verified or not matched",
                d.Existing.InfName & " / " & d.Existing.Version & " / " & d.Existing.DriverDate) &
            Environment.NewLine &
            "Source INF SHA-256: " & src.Sha256 & Environment.NewLine &
            "Hardware IDs from INF: " & If(src.HardwareIds.Count = 0, "not parsed",
                String.Join(", ", src.HardwareIds.Take(8)))
    End Sub

    Private Sub OpenDriversClicked(sender As Object, e As EventArgs)
        If decisions.Count = 0 Then Return
        Dim folderPath = sourceBox.Text.Trim()
        If File.Exists(folderPath) Then folderPath = Path.GetDirectoryName(folderPath)
        If Not Directory.Exists(folderPath) Then Return
        Dim prompt = "Open the existing Drivers section with this source folder?" &
                     Environment.NewLine & Environment.NewLine &
                     "Important: the legacy Add drivers action adds the ENTIRE folder, " &
                     "including drivers marked Same / Older / Review here." &
                     Environment.NewLine &
                     "This comparison is a preview and does not filter legacy imports."
        If MessageBox.Show(Me.FindForm(), prompt, "Driver Studio - Safe handoff",
                           MessageBoxButtons.YesNo, MessageBoxIcon.Warning) =
                           DialogResult.Yes Then
            openDrivers(folderPath)
        End If
    End Sub
End Class
