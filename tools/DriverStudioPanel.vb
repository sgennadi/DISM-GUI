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
    Private ReadOnly installButton As Button
    Private ReadOnly stopButton As Button
    Private ReadOnly auditButton As Button
    Private ReadOnly targetLabel As Label
    Private ReadOnly statusLabel As Label
    Private ReadOnly totalMetric As Label
    Private ReadOnly newMetric As Label
    Private ReadOnly sameMetric As Label
    Private ReadOnly grid As DataGridView
    Private ReadOnly detailBox As TextBox
    Private decisions As New List(Of DriverStudioEngine.Decision)()
    Private isWorking As Boolean
    Private isInstalling As Boolean
    Private comparisonVerified As Boolean
    Private stopAfterCurrent As Boolean
    Private comparedWim As String = ""
    Private comparedMount As String = ""
    Private comparedIndex As Integer
    Private comparedSource As String = ""
    Private comparedRecurse As Boolean
    Private comparedArchitecture As String = ""
    Private ReadOnly packageAudits As New Dictionary(Of String,
        DriverStudioPackageAudit.AuditResult)(StringComparer.OrdinalIgnoreCase)
    Private ReadOnly importHistory As New List(Of String)()
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
            .ColumnCount = 1, .RowCount = 9, .Dock = DockStyle.Top,
            .Width = 660, .Height = 800,
            .BackColor = Color.White, .Margin = Padding.Empty,
            .Padding = New Padding(15, 12, 15, 18)
        }
        rootLayout.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100))
        ' Show the driver decision grid earlier in short windows.
        ' Scale rows to the real UI font instead of clipping content at high DPI.
        Dim layoutScale As Single = Math.Max(1.0F, Me.Font.Height / 16.0F)
        Dim stackSource As Boolean = layoutScale > 1.15F
        ' Reserve TWO full rows for the source and Scan INF at higher DPI.
        For Each h As Integer In {60, If(stackSource, 116, 64), 52, 42, 235, 72, 155, 74, 80}
            rootLayout.RowStyles.Add(New RowStyle(SizeType.Absolute,
                CInt(Math.Ceiling(h * layoutScale))))
        Next
        rootLayout.Height = rootLayout.RowStyles.Cast(Of RowStyle)().
            Sum(Function(row) CInt(row.Height)) + rootLayout.Padding.Vertical + 8
        viewport.Controls.Add(rootLayout)
        Dim resizeStudio As Action = Sub()
            If viewport.IsDisposed OrElse rootLayout.IsDisposed Then Return
            Dim minimum = If(stackSource, 660, CInt(Math.Ceiling(800 * layoutScale)))
            Dim wanted = Math.Max(minimum, viewport.ClientSize.Width - 1)
            If rootLayout.Width <> wanted Then rootLayout.Width = wanted
        End Sub
        AddHandler viewport.ClientSizeChanged, Sub(sender, e) resizeStudio()
        AddHandler Me.VisibleChanged, Sub(sender, e)
            If Me.Visible Then resizeStudio()
        End Sub
        resizeStudio()

        Dim heading As New TableLayoutPanel With {
            .RowCount = 2, .ColumnCount = 1, .Dock = DockStyle.Fill,
            .Margin = Padding.Empty, .BackColor = Color.White
        }
        heading.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100))
        heading.RowStyles.Add(New RowStyle(SizeType.Percent, 58))
        heading.RowStyles.Add(New RowStyle(SizeType.Percent, 42))
        rootLayout.Controls.Add(heading, 0, 0)
        heading.Controls.Add(WriteLabel(If(stackSource,
                                         "DRIVER STUDIO  |  VERSION CHECK",
                                         "DRIVER STUDIO  |  SMART VERSION DETECTION"),
                                        12.4F, True, ModernTheme.TextInk), 0, 0)
        heading.Controls.Add(WriteLabel("Read-only driver version comparison",
                                        9.1F, False, ModernTheme.Muted), 0, 1)

        Dim sourceRow As New TableLayoutPanel With {
            .Name = "DriverStudioSourceRow",
            .Dock = DockStyle.Fill, .Margin = New Padding(0, 4, 0, 6),
            .ColumnCount = If(stackSource, 2, 3),
            .RowCount = If(stackSource, 2, 1), .BackColor = Color.White
        }
        If stackSource Then
            sourceRow.RowStyles.Add(New RowStyle(SizeType.Percent, 50))
            sourceRow.RowStyles.Add(New RowStyle(SizeType.Percent, 50))
            sourceRow.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 50))
            sourceRow.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 50))
        Else
            sourceRow.RowStyles.Add(New RowStyle(SizeType.Percent, 100))
            sourceRow.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100))
            sourceRow.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute, CInt(Math.Ceiling(176 * layoutScale))))
            sourceRow.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute, CInt(Math.Ceiling(128 * layoutScale))))
        End If
        rootLayout.Controls.Add(sourceRow, 0, 1)
        sourceBox = New TextBox With {
            .Name = "txtStudioSource", .Dock = DockStyle.Fill,
            .PlaceholderText = "Driver folder or .inf file",
            .BorderStyle = BorderStyle.FixedSingle,
            .Font = ModernTheme.UiFont(9.7F), .BackColor = Color.White,
            .Margin = New Padding(1, 13, 7, 11)
        }
        sourceRow.Controls.Add(sourceBox, 0, 0)
        If stackSource Then sourceRow.SetColumnSpan(sourceBox, 2)

        Dim browse As New Button With {
            .Name = "btnStudioBrowse", .Text = "Choose folder",
            .Dock = DockStyle.Fill, .Margin = New Padding(3, 6, 5, 7)
        }
        ModernTheme.StyleButton(browse, "browse")
        browse.AutoEllipsis = False
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
        sourceRow.Controls.Add(browse, If(stackSource, 0, 1), If(stackSource, 1, 0))

        scanButton = New Button With {
            .Name = "btnStudioScan", .Text = "Scan INF",
            .Dock = DockStyle.Fill, .Margin = New Padding(2, 6, 8, 7)
        }
        ModernTheme.StyleButton(scanButton, "primary")
        scanButton.AutoEllipsis = False
        AddHandler scanButton.Click, AddressOf ScanClicked
        sourceRow.Controls.Add(scanButton, If(stackSource, 1, 2), If(stackSource, 1, 0))

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
        compareButton.AutoEllipsis = False
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
        rootLayout.Controls.Add(metrics, 0, 5)
        totalMetric = AddMetric(metrics, 0, If(stackSource, "SOURCES", "SOURCE PACKAGES"))
        newMetric = AddMetric(metrics, 1, "NEW / NEWER")
        sameMetric = AddMetric(metrics, 2, "SAME VERSION")

        Dim textStatus As New TableLayoutPanel With {
            .Dock = DockStyle.Fill, .ColumnCount = 1, .RowCount = 2,
            .Margin = Padding.Empty, .BackColor = Color.White
        }
        textStatus.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100))
        textStatus.RowStyles.Add(New RowStyle(SizeType.Percent, 50))
        textStatus.RowStyles.Add(New RowStyle(SizeType.Percent, 50))
        rootLayout.Controls.Add(textStatus, 0, 3)
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
                UpdateInstallReady()
            End If
        End Sub
        rootLayout.Controls.Add(grid, 0, 4)

        Dim detailsLayout As New TableLayoutPanel With {
            .Dock = DockStyle.Fill, .RowCount = 2, .ColumnCount = 2,
            .Margin = Padding.Empty, .BackColor = Color.White
        }
        detailsLayout.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100))
        detailsLayout.ColumnStyles.Add(New ColumnStyle(
            SizeType.Absolute, CInt(Math.Ceiling(166 * layoutScale))))
        detailsLayout.RowStyles.Add(New RowStyle(SizeType.Absolute,
            CInt(Math.Ceiling(46 * layoutScale))))
        detailsLayout.RowStyles.Add(New RowStyle(SizeType.Percent, 100))
        rootLayout.Controls.Add(detailsLayout, 0, 6)
        detailsLayout.Controls.Add(WriteLabel(If(stackSource,
                                              "PACKAGE DETAILS", "DRIVER PACKAGE  |  DETAILS"),
                                              9.2F, True, ModernTheme.Blue), 0, 0)
        auditButton = New Button With {
            .Name = "btnStudioAuditSelected", .Text = "Audit selected",
            .Dock = DockStyle.Fill, .Margin = New Padding(2, 3, 3, 4),
            .Enabled = False
        }
        ModernTheme.StyleButton(auditButton, "secondary")
        auditButton.AutoEllipsis = False
        AddHandler auditButton.Click, AddressOf AuditClicked
        detailsLayout.Controls.Add(auditButton, 1, 0)
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
        detailsLayout.SetColumnSpan(detailBox, 2)

        Dim footer As New TableLayoutPanel With {
            .Dock = DockStyle.Fill, .ColumnCount = 3, .RowCount = 1,
            .Margin = New Padding(0, 11, 0, 0), .BackColor = Color.White
        }
        footer.RowStyles.Add(New RowStyle(SizeType.Percent, 100))
        footer.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute, CInt(Math.Ceiling(185 * layoutScale))))
        footer.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute, CInt(Math.Ceiling(235 * layoutScale))))
        footer.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100))
        rootLayout.Controls.Add(footer, 0, 7)
        copyButton = New Button With {
            .Name = "btnStudioCopyPlan", .Text = "Copy comparison",
            .Dock = DockStyle.Fill, .Enabled = False,
            .Margin = New Padding(0, 3, 10, 4)
        }
        ModernTheme.StyleButton(copyButton, "browse")
        copyButton.AutoEllipsis = False
        AddHandler copyButton.Click, Sub(sender, e)
            Try
                Clipboard.SetText(DriverStudioEngine.FormatReport(decisions, lastTarget) &
                If(importHistory.Count = 0, "",
                   Environment.NewLine & Environment.NewLine &
                   "SELECTIVE IMPORT RESULTS" & Environment.NewLine &
                   String.Join(Environment.NewLine, importHistory)))
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
        transferButton.AutoEllipsis = False
        AddHandler transferButton.Click, AddressOf OpenDriversClicked
        footer.Controls.Add(transferButton, 1, 0)
        Dim warning = WriteLabel("No automatic Commit / Discard",
                                  8.5F, False, ModernTheme.Muted)
        warning.TextAlign = ContentAlignment.MiddleRight
        footer.Controls.Add(warning, 2, 0)

        Dim importActions As New TableLayoutPanel With {
            .Name = "DriverStudioImportActions",
            .Dock = DockStyle.Fill, .ColumnCount = 3, .RowCount = 1,
            .Margin = New Padding(0, 6, 0, 0), .BackColor = Color.White
        }
        importActions.RowStyles.Add(New RowStyle(SizeType.Percent, 100))
        importActions.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute, CInt(Math.Ceiling(215 * layoutScale))))
        importActions.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute, CInt(Math.Ceiling(215 * layoutScale))))
        importActions.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100))
        rootLayout.Controls.Add(importActions, 0, 8)

        installButton = New Button With {
            .Name = "btnStudioInstallSelected",
            .Text = "Install selected INF",
            .Dock = DockStyle.Fill, .Enabled = False,
            .Margin = New Padding(0, 4, 10, 5)
        }
        ModernTheme.StyleButton(installButton, "primary")
        installButton.AutoEllipsis = False
        AddHandler installButton.Click, AddressOf InstallSelectedClicked
        importActions.Controls.Add(installButton, 0, 0)

        stopButton = New Button With {
            .Name = "btnStudioStopAfterCurrent",
            .Text = "Stop after current",
            .Dock = DockStyle.Fill, .Enabled = False,
            .Margin = New Padding(0, 4, 10, 5)
        }
        ModernTheme.StyleButton(stopButton, "neutral")
        stopButton.AutoEllipsis = False
        AddHandler stopButton.Click, Sub(sender, e)
            stopAfterCurrent = True
            stopButton.Enabled = False
            statusLabel.Text = "Stopping after the current DISM driver operation finishes."
        End Sub
        importActions.Controls.Add(stopButton, 1, 0)

        Dim importNote = WriteLabel("Requires mounted R/W image",
                                    8.4F, False, ModernTheme.Muted)
        importNote.TextAlign = ContentAlignment.MiddleRight
        importActions.Controls.Add(importNote, 2, 0)

        AddHandler sourceBox.TextChanged, Sub(sender, e)
            If Not isWorking Then InvalidateComparison()
        End Sub
        AddHandler recursive.CheckedChanged, Sub(sender, e)
            If Not isWorking Then InvalidateComparison()
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
        comparisonVerified = False
        packageAudits.Clear()
        comparedArchitecture = ""
        importHistory.Clear()
        installButton.Enabled = False
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
        auditButton.Enabled = Not busy AndAlso decisions.Count > 0
        recursive.Enabled = Not busy
        sourceBox.ReadOnly = busy
        grid.ReadOnly = busy
        transferButton.Enabled = Not busy AndAlso decisions.Count > 0
        stopButton.Enabled = busy AndAlso isInstalling AndAlso Not stopAfterCurrent
        UpdateInstallReady()
    End Sub

    Private Sub UpdateInstallReady()
        installButton.Enabled = Not isWorking AndAlso comparisonVerified AndAlso
            decisions.Any(Function(d) d.Selected AndAlso DriverStudioInstaller.IsAllowed(d))
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
            RuntimeDiagnostics.RecordException("Driver Studio scan", ex)
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
        comparisonVerified = False
        WorkState(True)
        statusLabel.Text = "Checking selected WIM index and reading driver versions..."
        Dim sourceItems As New List(Of DriverStudioEngine.DriverPackage)()
        Try
            sourceItems = Await Task.Run(
                Function() DriverStudioEngine.ScanSource(selectedPath, includeSubfolders))
            If IsDisposed Then Return
            Dim installed = Await DriverStudioInventory.ReadMountedAsync(
                imagePath, index, mount, CancellationToken.None)
            If IsDisposed Then Return
            Dim architecture = Await Task.Run(Function()
                Dim report = Wim360Reader.Inspect(imagePath)
                Dim selected = report.Images.FirstOrDefault(Function(x) x.Index = index)
                If selected Is Nothing Then Throw New InvalidOperationException(
                    "Selected image index is missing from inspected WIM.")
                Return selected.Architecture
            End Function)
            Dim results = DriverStudioEngine.CompareCandidates(
                sourceItems, installed, True, architecture)
            comparedArchitecture = architecture
            comparedWim = imagePath
            comparedIndex = index
            comparedMount = mount
            comparedSource = selectedPath
            comparedRecurse = includeSubfolders
            ShowDecisions(results, Path.GetFileName(imagePath) & " / Index " &
                          index.ToString() & " / " & mount, True)
            statusLabel.Text = "Compared " & results.Count.ToString() &
                               " INF(s) against " & installed.Count.ToString() &
                               " installed driver entries. No changes made."
        Catch ex As Exception
            If IsDisposed Then Return
            RuntimeDiagnostics.RecordException("Driver Studio compare", ex)
            ' Never turn an unavailable inventory into "New" decisions.
            ' VB.NET forbids Await inside Catch; use the source INF scan already
            ' completed before reading the installed-driver inventory.
            Dim results = DriverStudioEngine.CompareCandidates(
                sourceItems, New List(Of DriverStudioEngine.DriverPackage)(), False)
            ShowDecisions(results, "Inventory NOT verified")
            statusLabel.Text = "Not checked: " & ex.Message
        Finally
            If Not IsDisposed Then WorkState(False)
        End Try
    End Sub

    Public Sub PreviewForTest(items As IEnumerable(Of DriverStudioEngine.Decision))
        ShowDecisions(items.ToList(), "CI fixture - mounted index verified", True)
    End Sub

    Private Sub ShowDecisions(results As List(Of DriverStudioEngine.Decision),
                              comparisonTarget As String,
                              Optional verified As Boolean = False)
        comparisonVerified = verified
        packageAudits.Clear()
        lastTarget = comparisonTarget
        decisions = results
        grid.SuspendLayout()
        Try
            grid.Rows.Clear()
            For Each d In results
                Dim number = grid.Rows.Add(d.Selected, d.Candidate.InfName,
                    d.Candidate.Version, d.InstalledVersion, d.Status)
                grid.Rows(number).Cells(0).ReadOnly =
                    Not DriverStudioInstaller.IsAllowed(d)
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
        Dim newCount = results.Where(
            Function(d) (d.Status = "New" OrElse d.Status = "Newer")).Count()
        Dim sameCount = results.Where(Function(d) d.Status = "Same version").Count()
        newMetric.Text = newCount.ToString()
        sameMetric.Text = sameCount.ToString()
        statusLabel.Text = If(results.Count = 0,
            "No INF files found. Select a folder with driver INF packages.",
            "Preview ready. Choose an INF to see the comparison explanation.")
        copyButton.Enabled = results.Count > 0
        transferButton.Enabled = results.Count > 0
        auditButton.Enabled = results.Count > 0
        UpdateInstallReady()
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
            "INF architecture hints: " & If(src.Architectures.Count = 0, "generic / unknown",
                String.Join(", ", src.Architectures)) & Environment.NewLine &
            "Catalog declarations: " & If(src.CatalogFiles.Count = 0, "none",
                String.Join(", ", src.CatalogFiles.Values.Distinct())) & Environment.NewLine &
            "Hardware IDs from INF: " & If(src.HardwareIds.Count = 0, "not parsed",
                String.Join(", ", src.HardwareIds.Take(8))) &
            If(packageAudits.ContainsKey(src.FilePath),
                Environment.NewLine & Environment.NewLine &
                packageAudits(src.FilePath).Summary, "")
    End Sub

    ' On-demand (not every Scan INF) because a folder can contain many GiB
    ' of payloads. All marked New/Newer packages are audited as a group;
    ' otherwise the highlighted INF can be inspected individually.
    Private Async Sub AuditClicked(sender As Object, e As EventArgs)
        If isWorking OrElse decisions.Count = 0 Then Return
        Dim selected = decisions.Where(Function(d) d.Selected).
            Select(Function(d) d.Candidate).ToList()
        If selected.Count = 0 AndAlso grid.SelectedRows.Count > 0 Then
            Dim rowIndex = grid.SelectedRows(0).Index
            If rowIndex >= 0 AndAlso rowIndex < decisions.Count Then
                selected.Add(decisions(rowIndex).Candidate)
            End If
        End If
        If selected.Count = 0 Then Return
        If selected.Count > 30 Then
            MessageBox.Show(Me.FindForm(),
                "Audit up to 30 INF packages at a time. Narrow your selected plan.",
                "Driver Studio - Package audit", MessageBoxButtons.OK,
                MessageBoxIcon.Information)
            Return
        End If
        Dim targetArchitecture = If(comparisonVerified, comparedArchitecture, "")
        WorkState(True)
        Dim verifiedCount As Integer = 0
        Try
            For Each package In selected
                statusLabel.Text = "Auditing " & (verifiedCount + 1).ToString() &
                    "/" & selected.Count.ToString() & ": " & package.InfName
                Dim report = Await Task.Run(Function()
                    Return DriverStudioPackageAudit.Audit(
                        package, targetArchitecture, True)
                End Function)
                If IsDisposed Then Return
                packageAudits(package.FilePath) = report
                If report.CanInstall Then verifiedCount += 1
                detailBox.Text = report.Summary
            Next
            statusLabel.Text = "Package audit: " & verifiedCount.ToString() & "/" &
                selected.Count.ToString() & " trusted. " &
                "Unverified CAT files cannot be installed by Driver Studio."
            ShowSelected()
        Catch ex As Exception
            RuntimeDiagnostics.RecordException("Driver Studio package audit", ex)
            statusLabel.Text = "Package audit stopped: " & ex.Message
            detailBox.Text = "No DISM writes were performed." &
                             Environment.NewLine & ex.Message
        Finally
            If Not IsDisposed Then WorkState(False)
        End Try
    End Sub

    ' Explicit controlled write path. The preview is never an authorization to
    ' install a folder: only checked, still-verified New/Newer INF files can run.
    Private Async Sub InstallSelectedClicked(sender As Object, e As EventArgs)
        If isWorking OrElse Not comparisonVerified Then Return
        Dim selected = decisions.Where(Function(d) d.Selected).ToList()
        If selected.Count = 0 Then
            MessageBox.Show(Me.FindForm(), "Select at least one New or Newer INF.",
                            "Driver Studio", MessageBoxButtons.OK,
                            MessageBoxIcon.Information)
            Return
        End If
        If selected.Any(Function(d) Not DriverStudioInstaller.IsAllowed(d)) Then
            MessageBox.Show(Me.FindForm(),
                "Only New / Newer entries may be installed. Deselect other statuses.",
                "Driver Studio - Review selection", MessageBoxButtons.OK,
                MessageBoxIcon.Warning)
            Return
        End If
        If selected.Any(Function(d)
               Return Not packageAudits.ContainsKey(d.Candidate.FilePath) OrElse
                   Not packageAudits(d.Candidate.FilePath).CanInstall
           End Function) Then
            MessageBox.Show(Me.FindForm(),
                "Audit selected INF first. A complete source-package SHA-256 " &
                "manifest and a valid CAT Authenticode signature are required." &
                Environment.NewLine &
                "Select New/Newer packages and click Audit selected.",
                "Driver Studio - Safety preflight", MessageBoxButtons.OK,
                MessageBoxIcon.Warning)
            Return
        End If
        If Not DriverStudioInstaller.IsAdministrator() Then
            MessageBox.Show(Me.FindForm(),
                "Adding drivers to an offline image requires Administrator privileges. " &
                "Restart DISM GUI elevated and compare the WIM index again.",
                "Driver Studio - Administrator required", MessageBoxButtons.OK,
                MessageBoxIcon.Warning)
            Return
        End If
        If Not String.Equals(currentWim(), comparedWim, StringComparison.OrdinalIgnoreCase) OrElse
           currentIndex() <> comparedIndex OrElse
           Not String.Equals(currentMount(), comparedMount, StringComparison.OrdinalIgnoreCase) OrElse
           Not String.Equals(sourceBox.Text.Trim(), comparedSource,
                             StringComparison.OrdinalIgnoreCase) OrElse
           recursive.Checked <> comparedRecurse Then
            MessageBox.Show(Me.FindForm(),
                "The WIM, image index, mount folder or driver source changed. " &
                "Run Compare with mounted index again.",
                "Driver Studio - Comparison expired", MessageBoxButtons.OK,
                MessageBoxIcon.Warning)
            Return
        End If

        Dim prompt = "Install " & selected.Count.ToString() &
                     " selected INF driver package(s) into:" & Environment.NewLine &
                     comparedWim & Environment.NewLine &
                     "Index " & comparedIndex.ToString() &
                     " | Mount folder: " & comparedMount & Environment.NewLine &
                     Environment.NewLine &
                     "DISM will MODIFY the mounted image. Only New/Newer INF files " &
                     "will be added; existing, older and ambiguous packages are skipped." &
                     Environment.NewLine & Environment.NewLine &
                     "No automatic Mount, Commit, Discard, Recurse or ForceUnsigned." &
                     Environment.NewLine &
                     "Changes remain pending until you explicitly commit/unmount." &
                     Environment.NewLine &
                     "Do not close the application while an INF is being installed." &
                     Environment.NewLine & Environment.NewLine &
                     "Continue?"
        If MessageBox.Show(Me.FindForm(), prompt,
                           "Driver Studio - Confirm selective import",
                           MessageBoxButtons.YesNo, MessageBoxIcon.Warning,
                           MessageBoxDefaultButton.Button2) <> DialogResult.Yes Then Return

        isInstalling = True
        stopAfterCurrent = False
        WorkState(True)
        importHistory.Clear()
        Dim succeeded As Integer = 0
        Dim completed As Integer = 0
        Dim appliedPackages As New List(Of DriverStudioEngine.DriverPackage)()
        Dim imageLease As WimImageOperationLease = Nothing
        Try
            ' Hold one cross-process write lease for the COMPLETE batch,
            ' including inventory preflight and postflight, not per INF.
            imageLease = WimImageOperationLease.Acquire(
                WimImageOperationLease.WriterResource, comparedWim, comparedMount)
            statusLabel.Text = "Preflight: verifying read/write mount and fresh driver inventory..."
            Dim mountRoot = Await Task.Run(Function()
                Return DriverStudioInstaller.RequireWritableTarget(
                    comparedWim, comparedIndex, comparedMount)
            End Function)
            Dim sourceItems = Await Task.Run(Function()
                Return DriverStudioEngine.ScanSource(comparedSource, comparedRecurse)
            End Function)
            Dim installed = Await DriverStudioInventory.ReadMountedAsync(
                comparedWim, comparedIndex, comparedMount, CancellationToken.None)
            ' Re-inspect the image architecture before servicing. An image
            ' replaced in-place since comparison must not retain old approvals.
            Dim refreshedArchitecture = Await Task.Run(Function()
                Dim image = Wim360Reader.Inspect(comparedWim)
                Dim indexInfo = image.Images.FirstOrDefault(
                    Function(x) x.Index = comparedIndex)
                If indexInfo Is Nothing OrElse
                   Not String.Equals(indexInfo.Architecture, comparedArchitecture,
                                     StringComparison.OrdinalIgnoreCase) Then
                    Throw New InvalidOperationException(
                        "The image architecture changed since driver comparison. " &
                        "Compare again before importing.")
                End If
                Return indexInfo.Architecture
            End Function)
            Dim plan = DriverStudioInstaller.ValidatePlan(
                selected, sourceItems, installed, refreshedArchitecture)

            For Each package As DriverStudioEngine.DriverPackage In plan
                If stopAfterCurrent Then Exit For
                ' An external operation might unmount the image during a long
                ' batch. Re-check the index and read/write flag before each INF.
                Await Task.Run(Function()
                    Return DriverStudioInstaller.RequireWritableTarget(
                        comparedWim, comparedIndex, comparedMount)
                End Function)

                statusLabel.Text = "Installing " & (completed + 1).ToString() & "/" &
                    plan.Count.ToString() & ": " & package.InfName &
                    " (do not close DISM GUI)"
                Dim outcome = Await DriverStudioInstaller.AddOneAsync(
                    mountRoot, package, packageAudits(package.FilePath), comparedArchitecture)
                completed += 1
                If outcome.Succeeded Then
                    succeeded += 1
                    appliedPackages.Add(package)
                End If
                importHistory.Add(package.InfName & " | " &
                    If(outcome.Succeeded, "Installed", "FAILED") &
                    " | DISM exit: " & outcome.ExitCode.ToString() &
                    If(outcome.RebootRequired, " | Restart required", "") &
                    Environment.NewLine & outcome.Details)

                For Each row As DataGridViewRow In grid.Rows
                    If String.Equals(Convert.ToString(row.Cells(1).Value),
                                     package.InfName, StringComparison.OrdinalIgnoreCase) Then
                        row.Cells(4).Value = If(outcome.Succeeded,
                                                "Installed", "FAILED")
                        row.Cells(0).Value = False
                        Exit For
                    End If
                Next
                If Not outcome.Succeeded Then
                    RuntimeDiagnostics.RecordDismExit(
                        "Driver Studio install", outcome.ExitCode.ToString())
                End If
                If Not outcome.Succeeded OrElse outcome.RebootRequired Then
                    detailBox.Text = If(outcome.Succeeded,
                        "Restart required before further servicing.",
                        "DISM failed. Remaining drivers were NOT installed.") &
                        Environment.NewLine & outcome.Details
                    Exit For
                End If
            Next
            statusLabel.Text = "DISM finished: " & succeeded.ToString() &
                "/" & completed.ToString() & " successful. Checking mounted inventory..."
            If appliedPackages.Count > 0 Then
                Try
                    Dim afterInventory = Await DriverStudioInventory.ReadMountedAsync(
                        comparedWim, comparedIndex, comparedMount, CancellationToken.None)
                    Dim confirmed = appliedPackages.Where(Function(p)
                        Return DriverStudioInstaller.IsPresentInInventory(p, afterInventory)
                    End Function).Count()
                    importHistory.Add("Postflight: " & confirmed.ToString() &
                        "/" & appliedPackages.Count.ToString() &
                        " successful DISM packages found in mounted driver inventory.")
                    If confirmed <> appliedPackages.Count Then
                        importHistory.Add(
                            "WARNING: DISM reported success but one or more packages " &
                            "were not confirmed. Run Scan mounted index and review DISM logs.")
                    End If
                    statusLabel.Text = "DISM: " & succeeded.ToString() & "/" &
                        completed.ToString() & " successful | Inventory: " &
                        confirmed.ToString() & "/" & appliedPackages.Count.ToString() &
                        " confirmed staged | Commit/Discard NOT performed."
                Catch auditError As Exception
                    RuntimeDiagnostics.RecordException("Driver Studio postflight", auditError)
                    importHistory.Add("Postflight unavailable: " & auditError.Message)
                    statusLabel.Text = "DISM: " & succeeded.ToString() & "/" &
                        completed.ToString() &
                        " successful; inventory verification unavailable. " &
                        "Commit/Discard NOT performed."
                End Try
            Else
                statusLabel.Text = "DISM: " & succeeded.ToString() & "/" &
                    completed.ToString() & " successful, " &
                    (selected.Count - completed).ToString() &
                    " not attempted. Commit/Discard NOT performed."
            End If
        Catch ex As Exception
            RuntimeDiagnostics.RecordException("Driver Studio install", ex)
            importHistory.Add("STOPPED | " & ex.Message)
            statusLabel.Text = "Selective import stopped after " &
                completed.ToString() & " attempt(s): " & ex.Message
            detailBox.Text = "No automatic rollback or Discard was performed." &
                Environment.NewLine & ex.Message
        Finally
            imageLease?.Dispose()
            comparisonVerified = False
            isInstalling = False
            stopAfterCurrent = False
            If Not IsDisposed Then
                WorkState(False)
                installButton.Enabled = False
                stopButton.Enabled = False
            End If
        End Try
    End Sub

    Private Sub OpenDriversClicked(sender As Object, e As EventArgs)
        If decisions.Count = 0 Then Return
        Dim folderPath = sourceBox.Text.Trim()
        If File.Exists(folderPath) Then folderPath = Path.GetDirectoryName(folderPath)
        If Not Directory.Exists(folderPath) Then Return
        ' This command only navigates; it never imports drivers by itself.
        Dim prompt = "Open this source folder in the Drivers section?" &
                     Environment.NewLine & Environment.NewLine &
                     "The standard Add Drivers command imports the entire folder," &
                     Environment.NewLine &
                     "including same-version or older drivers." &
                     Environment.NewLine &
                     "Smart Version Detection does not filter that command."
        Using confirm As New Form With {
            .Text = "Open Drivers section",
            .StartPosition = FormStartPosition.CenterParent,
            .FormBorderStyle = FormBorderStyle.Sizable,
            .MaximizeBox = False, .MinimizeBox = False,
            .ShowInTaskbar = False, .AutoScaleMode = AutoScaleMode.Font,
            .Font = ModernTheme.UiFont(10.0F),
            .MinimumSize = New Size(590, 245),
            .ClientSize = New Size(650, 255)
        }
            Dim message As New Label With {
                .Dock = DockStyle.Fill, .Text = prompt,
                .Font = ModernTheme.UiFont(10.0F),
                .ForeColor = ModernTheme.TextInk,
                .Padding = New Padding(12), .AutoSize = False
            }
            Dim popupScale As Single = Math.Max(1.0F, confirm.Font.Height / 18.0F)
            Dim actions As New FlowLayoutPanel With {
                .Dock = DockStyle.Fill,
                .FlowDirection = FlowDirection.RightToLeft,
                .Padding = New Padding(8), .WrapContents = False
            }
            Dim cancel As New Button With {
                .Text = "Cancel", .DialogResult = DialogResult.Cancel,
                .Width = 112, .Height = 34
            }
            Dim proceed As New Button With {
                .Text = "Open Drivers", .DialogResult = DialogResult.OK,
                .Width = 145, .Height = 34
            }
            ModernTheme.StyleButton(cancel, "neutral")
            ModernTheme.StyleButton(proceed, "primary")
            cancel.Size = New Size(CInt(Math.Ceiling(120 * popupScale)),
                                   CInt(Math.Ceiling(36 * popupScale)))
            proceed.Size = New Size(CInt(Math.Ceiling(150 * popupScale)),
                                    CInt(Math.Ceiling(36 * popupScale)))
            actions.Controls.Add(cancel)
            actions.Controls.Add(proceed)
            Dim dialogLayout As New TableLayoutPanel With {
                .Dock = DockStyle.Fill, .ColumnCount = 1, .RowCount = 2,
                .Padding = New Padding(8, 8, 8, 8)
            }
            dialogLayout.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100))
            dialogLayout.RowStyles.Add(New RowStyle(SizeType.Percent, 100))
            dialogLayout.RowStyles.Add(New RowStyle(SizeType.Absolute,
                CInt(Math.Ceiling(60 * popupScale))))
            dialogLayout.Controls.Add(message, 0, 0)
            dialogLayout.Controls.Add(actions, 0, 1)
            confirm.Controls.Add(dialogLayout)
            confirm.AcceptButton = proceed
            confirm.CancelButton = cancel
            If confirm.ShowDialog(Me.FindForm()) = DialogResult.OK Then
                openDrivers(folderPath)
            End If
        End Using
    End Sub
End Class
