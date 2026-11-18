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
    Private ReadOnly viewport As Panel
    Private ReadOnly rootLayout As TableLayoutPanel
    Private ReadOnly sourceBox As TextBox
    Private ReadOnly recursive As CheckBox
    Private ReadOnly scanButton As Button
    Private ReadOnly copyButton As Button
    Private ReadOnly installButton As Button
    Private ReadOnly stopButton As Button
    Private ReadOnly importActions As Panel
    Private ReadOnly auditButton As Button
    Private ReadOnly selectEligibleButton As Button
    Private ReadOnly clearSelectionButton As Button
    Private ReadOnly auditAllButton As Button
    Private ReadOnly selectionLabel As Label
    Private bulkSelectionUpdate As Boolean
    Private lastAutoCompareSignature As String = ""
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
    Private comparedInventoryFingerprint As String = ""
    Private ReadOnly packageAudits As New Dictionary(Of String,
        DriverStudioPackageAudit.AuditResult)(StringComparer.OrdinalIgnoreCase)
    Private ReadOnly importHistory As New List(Of String)()
    Private lastTarget As String = "Not checked"

    Public Sub New(getSource As Func(Of String),
                   getWim As Func(Of String),
                   getIndex As Func(Of Integer),
                   getMount As Func(Of String))
        currentSource = getSource
        currentWim = getWim
        currentIndex = getIndex
        currentMount = getMount

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
            .ColumnCount = 1, .RowCount = 7, .Dock = DockStyle.Top,
            .Width = 660, .Height = 800,
            .BackColor = Color.White, .Margin = Padding.Empty,
            .Padding = New Padding(15, 12, 15, 18)
        }
        rootLayout.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100))
        ' Show the driver decision grid earlier in short windows.
        ' Scale rows to the real UI font instead of clipping content at high DPI.
        Dim layoutScale As Single = Math.Max(1.0F, Me.Font.Height / 16.0F)
        Dim stackSource As Boolean = layoutScale > 1.15F
        ' Keep actions next to the results instead of separate detached footers.
        ' Grid/details expand with the viewport; narrow/high-DPI windows scroll.
        For Each h As Integer In {58, If(stackSource, 94, 62), 44, 46, 260, 64, 154}
            rootLayout.RowStyles.Add(New RowStyle(SizeType.Absolute,
                CInt(Math.Ceiling(h * layoutScale))))
        Next
        rootLayout.Height = rootLayout.RowStyles.Cast(Of RowStyle)().
            Sum(Function(row) CInt(row.Height)) + rootLayout.Padding.Vertical + 8
        viewport.Controls.Add(rootLayout)
        Dim resizeStudio As Action = Sub()
            If viewport.IsDisposed OrElse rootLayout.IsDisposed Then Return
            ' Wide windows get one compact action row. At small widths or
            ' high DPI, distribute selection/audits and the WIM write across
            ' three readable rows rather than clipping Audit selected.
            Dim compactActions = viewport.ClientSize.Width <
                CInt(Math.Ceiling(920 * layoutScale))
            Dim actionBar = TryCast(rootLayout.Controls.Find(
                "DriverStudioSelectionBar", True).FirstOrDefault(), TableLayoutPanel)
            Dim gridArea = TryCast(rootLayout.Controls.Find(
                "DriverStudioGridArea", True).FirstOrDefault(), TableLayoutPanel)
            If actionBar IsNot Nothing AndAlso gridArea IsNot Nothing Then
                ArrangeSelectionToolbar(actionBar, gridArea, compactActions, layoutScale)
            End If

            Dim minimum = If(compactActions,
                CInt(Math.Ceiling(300 * layoutScale)) + rootLayout.Padding.Horizontal,
                CInt(Math.Ceiling((198 + 88 + 145 + 122 + 225 + 100) * layoutScale)) +
                    rootLayout.Padding.Horizontal)
            Dim wanted = Math.Max(minimum, viewport.ClientSize.Width - 1)
            If rootLayout.Width <> wanted Then rootLayout.Width = wanted

            ' Reuse the vertical space released by the old Copy and Add
            ' footer rows. The comparison grid gets two thirds of any extra
            ' height and the INF details receive the remaining third.
            Dim fixedHeight As Integer = rootLayout.Padding.Vertical + 8
            For Each rowIndex As Integer In {0, 1, 2, 3, 5}
                fixedHeight += CInt(Math.Ceiling(rootLayout.RowStyles(rowIndex).Height))
            Next
            Dim minGrid = CInt(Math.Ceiling(
                If(compactActions, 330, 260) * layoutScale))
            Dim minDetails = CInt(Math.Ceiling(154 * layoutScale))
            Dim extra = Math.Max(0, viewport.ClientSize.Height -
                                  fixedHeight - minGrid - minDetails - 1)
            Dim gridHeight = minGrid + CInt(Math.Floor(extra * 0.67))
            Dim detailHeight = minDetails + extra - CInt(Math.Floor(extra * 0.67))
            If CInt(rootLayout.RowStyles(4).Height) <> gridHeight Then
                rootLayout.RowStyles(4).Height = gridHeight
            End If
            If CInt(rootLayout.RowStyles(6).Height) <> detailHeight Then
                rootLayout.RowStyles(6).Height = detailHeight
            End If
            Dim newHeight = fixedHeight + gridHeight + detailHeight
            If rootLayout.Height <> newHeight Then rootLayout.Height = newHeight
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
                                         "DRIVER COMPARISON",
                                         "OFFLINE DRIVER COMPARISON"),
                                        12.4F, True, ModernTheme.TextInk), 0, 0)
        heading.Controls.Add(WriteLabel("Compare versions safely. Adding drivers to WIM is optional.",
                                        9.1F, False, ModernTheme.Muted), 0, 1)

        Dim sourceRow As New TableLayoutPanel With {
            .Name = "DriverStudioSourceRow",
            .Dock = DockStyle.Fill, .Margin = New Padding(0, 4, 0, 6),
            .ColumnCount = 2,
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
        sourceRow.Controls.Add(browse, 1, If(stackSource, 1, 0))

        ' One Scan / Refresh button is hosted in the always-visible workspace header.
        ' Do not duplicate it in the scrollable source row at 175-200% DPI.
        scanButton = New Button With {
            .Name = "btnStudioScanQuick", .Text = "Scan / Refresh",
            .Dock = DockStyle.Fill, .Margin = New Padding(5, 3, 0, 3),
            .AccessibleDescription =
                "Read INF files and automatically compare them with the mounted WIM " &
                "when that exact image and index are available. Read-only."
        }
        ModernTheme.StyleButton(scanButton, "primary")
        scanButton.AutoEllipsis = False
        AddHandler scanButton.Click, AddressOf ScanClicked

        Dim settings As New TableLayoutPanel With {
            .Dock = DockStyle.Fill, .ColumnCount = 1, .RowCount = 1,
            .Margin = Padding.Empty, .BackColor = Color.White
        }
        settings.RowStyles.Add(New RowStyle(SizeType.Percent, 100))
        settings.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100))
        rootLayout.Controls.Add(settings, 0, 2)
        recursive = New CheckBox With {
            .Name = "chkStudioRecursive", .Text = "Include subfolders",
            .Checked = True, .AutoSize = False,
            .Dock = DockStyle.Fill, .Font = ModernTheme.UiFont(9.4F),
            .Margin = New Padding(3, 3, 6, 3)
        }
        settings.Controls.Add(recursive, 0, 0)
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
             .Name = "colStudioInclude", .HeaderText = "Add?", .FillWeight = 48
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
            If bulkSelectionUpdate OrElse e.ColumnIndex <> 0 OrElse
               e.RowIndex < 0 OrElse e.RowIndex >= decisions.Count Then Return
            Dim value = grid.Rows(e.RowIndex).Cells(0).Value
            Dim checked = TypeOf value Is Boolean AndAlso CBool(value)
            Dim eligible = comparisonVerified AndAlso
                DriverStudioInstaller.IsAllowed(decisions(e.RowIndex))
            decisions(e.RowIndex).Selected = checked AndAlso eligible
            If checked AndAlso Not eligible Then
                bulkSelectionUpdate = True
                Try
                    grid.Rows(e.RowIndex).Cells(0).Value = False
                Finally
                    bulkSelectionUpdate = False
                End Try
            End If
            UpdateInstallReady()
        End Sub

        Dim driverGridArea As New TableLayoutPanel With {
            .Name = "DriverStudioGridArea", .Dock = DockStyle.Fill,
            .RowCount = 2, .ColumnCount = 1,
            .BackColor = Color.White, .Margin = Padding.Empty
        }
        driverGridArea.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100))
        driverGridArea.RowStyles.Add(New RowStyle(SizeType.Absolute,
            CInt(Math.Ceiling(45 * layoutScale))))
        driverGridArea.RowStyles.Add(New RowStyle(SizeType.Percent, 100))
        rootLayout.Controls.Add(driverGridArea, 0, 4)

        Dim selectionBar As New TableLayoutPanel With {
            .Name = "DriverStudioSelectionBar", .Dock = DockStyle.Fill,
            .RowCount = 3, .ColumnCount = 6,
            .Margin = New Padding(0, 0, 0, 3), .BackColor = Color.White
        }
        selectionBar.RowStyles.Add(New RowStyle(SizeType.Percent, 100))
        selectionBar.RowStyles.Add(New RowStyle(SizeType.Absolute, 0))
        selectionBar.RowStyles.Add(New RowStyle(SizeType.Absolute, 0))
        For Each columnWidth As Integer In {198, 88, 145, 122}
            selectionBar.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute,
                CInt(Math.Ceiling(columnWidth * layoutScale))))
        Next
        selectionBar.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100))
        selectionBar.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute,
            CInt(Math.Ceiling(225 * layoutScale))))
        driverGridArea.Controls.Add(selectionBar, 0, 0)

        selectEligibleButton = New Button With {
            .Name = "btnStudioSelectAllEligible", .Text = "Select New / Newer",
            .Dock = DockStyle.Fill, .Enabled = False,
            .Margin = New Padding(0, 3, 7, 4)
        }
        ModernTheme.StyleButton(selectEligibleButton, "secondary")
        AddHandler selectEligibleButton.Click, AddressOf SelectAllEligibleClicked
        selectEligibleButton.AccessibleDescription =
            "Opt-in: mark verified New/Newer packages for an optional WIM import; never installs by itself."
        selectionBar.Controls.Add(selectEligibleButton, 0, 0)

        clearSelectionButton = New Button With {
            .Name = "btnStudioClearAll", .Text = "Clear",
            .Dock = DockStyle.Fill, .Enabled = False,
            .Margin = New Padding(0, 3, 7, 4)
        }
        ModernTheme.StyleButton(clearSelectionButton, "neutral")
        AddHandler clearSelectionButton.Click, Sub(sender, e)
            ApplyBulkSelection(False)
            statusLabel.Text = "Selection cleared. WIM unchanged."
        End Sub
        selectionBar.Controls.Add(clearSelectionButton, 1, 0)

        auditButton = New Button With {
            .Name = "btnStudioAuditSelected", .Text = "Audit selected",
            .Dock = DockStyle.Fill, .Enabled = False,
            .Margin = New Padding(0, 3, 7, 4)
        }
        ModernTheme.StyleButton(auditButton, "secondary")
        auditButton.AutoEllipsis = False
        AddHandler auditButton.Click, AddressOf AuditClicked
        auditButton.AccessibleDescription =
            "Read-only: audit all checked INF packages, or the highlighted INF if none are checked."
        selectionBar.Controls.Add(auditButton, 2, 0)

        auditAllButton = New Button With {
            .Name = "btnStudioAuditAll", .Text = "Audit all INF",
            .Dock = DockStyle.Fill, .Enabled = False,
            .Margin = New Padding(0, 3, 7, 4)
        }
        ModernTheme.StyleButton(auditAllButton, "browse")
        AddHandler auditAllButton.Click, AddressOf AuditAllClicked
        auditAllButton.AccessibleDescription =
            "Read-only source package and CAT signature audit, maximum 30 INF per run."
        selectionBar.Controls.Add(auditAllButton, 3, 0)

        selectionLabel = WriteLabel("0 selected", 8.8F, False, ModernTheme.Muted)
        selectionLabel.Name = "lblStudioSelection"
        selectionLabel.TextAlign = ContentAlignment.MiddleRight
        selectionLabel.Margin = New Padding(5, 0, 10, 0)
        selectionBar.Controls.Add(selectionLabel, 4, 0)

        ' The primary write action lives next to selection/audit, never as a
        ' detached bottom-of-page button. Stop replaces it during a batch.
        importActions = New Panel With {
            .Name = "DriverStudioImportActions", .Dock = DockStyle.Fill,
            .Padding = New Padding(5, 3, 0, 4),
            .BackColor = Color.White, .Margin = Padding.Empty
        }
        selectionBar.Controls.Add(importActions, 5, 0)
        installButton = New Button With {
            .Name = "btnStudioInstallSelected",
            .Text = "Add selected to WIM...",
            .Dock = DockStyle.Fill, .Enabled = False,
            .Margin = Padding.Empty
        }
        ModernTheme.StyleButton(installButton, "primary")
        installButton.AutoEllipsis = False
        installButton.AccessibleDescription =
            "Optional offline WIM write. Requires verified New/Newer INF, signed package audit, " &
            "read/write mount and explicit confirmation. No automatic Commit or old-driver removal."
        AddHandler installButton.Click, AddressOf InstallSelectedClicked
        importActions.Controls.Add(installButton)

        stopButton = New Button With {
            .Name = "btnStudioStopAfterCurrent",
            .Text = "Stop after current",
            .Dock = DockStyle.Fill, .Enabled = False, .Visible = False,
            .Margin = Padding.Empty
        }
        ModernTheme.StyleButton(stopButton, "neutral")
        stopButton.AutoEllipsis = False
        AddHandler stopButton.Click, Sub(sender, e)
            stopAfterCurrent = True
            stopButton.Enabled = False
            statusLabel.Text = "Stopping after the current DISM driver operation finishes."
        End Sub
        importActions.Controls.Add(stopButton)

        driverGridArea.Controls.Add(grid, 0, 1)

        Dim detailsLayout As New TableLayoutPanel With {
            .Name = "DriverStudioDetailsArea",
            .Dock = DockStyle.Fill, .RowCount = 2, .ColumnCount = 2,
            .Margin = Padding.Empty, .BackColor = Color.White
        }
        detailsLayout.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100))
        detailsLayout.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute,
            CInt(Math.Ceiling(162 * layoutScale))))
        detailsLayout.RowStyles.Add(New RowStyle(SizeType.Absolute,
            CInt(Math.Ceiling(46 * layoutScale))))
        detailsLayout.RowStyles.Add(New RowStyle(SizeType.Percent, 100))
        rootLayout.Controls.Add(detailsLayout, 0, 6)
        ' Keep this header short at 175-200% scaling; Copy comparison is
        ' placed in its own right column, not over the heading.
        detailsLayout.Controls.Add(WriteLabel(If(stackSource,
                                              "INF DETAILS", "PACKAGE DETAILS"),
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
        detailsLayout.SetColumnSpan(detailBox, 2)

        ' Export belongs with the detail/report view, not in a separate row.
        copyButton = New Button With {
            .Name = "btnStudioCopyPlan", .Text = "Copy comparison",
            .Dock = DockStyle.Fill, .Enabled = False,
            .Margin = New Padding(2, 3, 0, 4)
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
        detailsLayout.Controls.Add(copyButton, 1, 0)

        AddHandler sourceBox.TextChanged, Sub(sender, e)
            If Not isWorking Then InvalidateComparison()
        End Sub
        AddHandler recursive.CheckedChanged, Sub(sender, e)
            If Not isWorking Then InvalidateComparison()
        End Sub
        ' Initial children are now available for responsive toolbar layout.
        resizeStudio()
    End Sub

    Private Sub ArrangeSelectionToolbar(bar As TableLayoutPanel,
                                        gridArea As TableLayoutPanel,
                                        compact As Boolean,
                                        layoutScale As Single)
        If TypeOf bar.Tag Is Boolean AndAlso CBool(bar.Tag) = compact Then Return
        ' Mark early to avoid recursive ClientSizeChanged reflow.
        bar.Tag = compact
        bar.SuspendLayout()
        gridArea.SuspendLayout()
        Try
            Dim positions As (Ctl As Control, Column As Integer, Row As Integer)()
            If compact Then
                For i As Integer = 0 To 5
                    bar.ColumnStyles(i).SizeType = If(i < 2,
                        SizeType.Percent, SizeType.Absolute)
                    bar.ColumnStyles(i).Width = If(i < 2, 50.0F, 0.0F)
                Next
                For i As Integer = 0 To 2
                    bar.RowStyles(i).SizeType = SizeType.Percent
                    bar.RowStyles(i).Height = 100.0F / 3.0F
                Next
                positions = {
                    (selectEligibleButton, 0, 0), (clearSelectionButton, 1, 0),
                    (auditButton, 0, 1), (auditAllButton, 1, 1),
                    (selectionLabel, 0, 2), (importActions, 1, 2)
                }
            Else
                Dim actionWidths As Integer() = {198, 88, 145, 122}
                For i As Integer = 0 To 3
                    bar.ColumnStyles(i).SizeType = SizeType.Absolute
                    bar.ColumnStyles(i).Width = CSng(Math.Ceiling(
                        actionWidths(i) * layoutScale))
                Next
                bar.ColumnStyles(4).SizeType = SizeType.Percent
                bar.ColumnStyles(4).Width = 100
                bar.ColumnStyles(5).SizeType = SizeType.Absolute
                bar.ColumnStyles(5).Width = CSng(Math.Ceiling(225 * layoutScale))
                bar.RowStyles(0).SizeType = SizeType.Percent
                bar.RowStyles(0).Height = 100
                For i As Integer = 1 To 2
                    bar.RowStyles(i).SizeType = SizeType.Absolute
                    bar.RowStyles(i).Height = 0
                Next
                positions = {
                    (selectEligibleButton, 0, 0), (clearSelectionButton, 1, 0),
                    (auditButton, 2, 0), (auditAllButton, 3, 0),
                    (selectionLabel, 4, 0), (importActions, 5, 0)
                }
            End If
            For Each placement In positions
                bar.SetCellPosition(placement.Ctl,
                    New TableLayoutPanelCellPosition(
                        placement.Column, placement.Row))
            Next
            gridArea.RowStyles(0).Height = CSng(Math.Ceiling(
                If(compact, 147, 45) * layoutScale))
        Finally
            gridArea.ResumeLayout(True)
            bar.ResumeLayout(True)
        End Try
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

    ' Transfer only the selected source folder to legacy Driver tools.
    ' A single INF selection maps to its parent folder. This never authorizes
    ' adding drivers or enables the legacy "Add drivers" action.
    Public ReadOnly Property ScanAction As Button
        Get
            Return scanButton
        End Get
    End Property

    Public Function SelectedSourceFolderForDriverTools() As String
        Dim selected = sourceBox.Text.Trim().Trim(""""c)
        If selected.Length = 0 Then Return ""
        Try
            If Directory.Exists(selected) Then Return Path.GetFullPath(selected)
            If File.Exists(selected) AndAlso
               selected.EndsWith(".inf", StringComparison.OrdinalIgnoreCase) Then
                Return Path.GetDirectoryName(Path.GetFullPath(selected))
            End If
        Catch ex As ArgumentException
        Catch ex As NotSupportedException
        Catch ex As PathTooLongException
        End Try
        Return ""
    End Function

    ' The operator may unmount/remount the SAME WIM into the SAME folder.
    ' The file/index/folder string is then unchanged, but the inventory is not
    ' guaranteed to be the same. Force a fresh read-only scan on returning.
    Public Sub NotifyMountWorkspaceVisited()
        lastAutoCompareSignature = ""
    End Sub

    Public Sub ActivateFromDrivers()
        If Not isWorking AndAlso String.IsNullOrWhiteSpace(sourceBox.Text) Then
            Dim path = currentSource()
            If Not String.IsNullOrWhiteSpace(path) Then sourceBox.Text = path.Trim()
        End If
        If isWorking Then Return

        Dim wim = If(currentWim(), "").Trim()
        Dim mount = If(currentMount(), "").Trim()
        Dim target = "Target: " & If(wim.Length = 0, "No WIM selected",
            Path.GetFileName(wim)) & " | Index " & currentIndex().ToString()
        targetLabel.AccessibleDescription = "Selected mount folder: " & mount
        Dim mountedWindowsAvailable As Boolean = False
        Try
            mountedWindowsAvailable = wim.Length > 0 AndAlso File.Exists(wim) AndAlso
                mount.Length > 0 AndAlso Directory.Exists(Path.Combine(mount, "Windows"))
        Catch ex As ArgumentException
            mountedWindowsAvailable = False
        Catch ex As NotSupportedException
            mountedWindowsAvailable = False
        End Try

        If Not mountedWindowsAvailable Then
            ' A previously verified WIM might have been unmounted or discarded
            ' while the operator was using Mount. Never keep a stale "verified"
            ' label, eligible checkboxes, or installed-driver version results.
            If comparisonVerified AndAlso decisions.Count > 0 Then
                Dim sourceOnly = DriverStudioEngine.CompareCandidates(
                    decisions.Select(Function(d) d.Candidate).ToList(),
                    New List(Of DriverStudioEngine.DriverPackage)(), False)
                ShowDecisions(sourceOnly, "WIM inventory NOT checked")
                statusLabel.Text = "Image is no longer mounted. INF scan retained; " &
                    "driver versions NOT checked. No WIM changes."
            End If
            targetLabel.Text = target & " | Not mounted / not verified"
            lastAutoCompareSignature = ""
            Return
        End If

        Dim signature = CurrentTargetSignature()
        If decisions.Count = 0 Then
            targetLabel.Text = target & " | WIM available; click Scan / Refresh"
            Return
        End If
        If String.Equals(signature, lastAutoCompareSignature,
                         StringComparison.Ordinal) Then Return
        lastAutoCompareSignature = signature
        targetLabel.Text = target & " | Comparing automatically..."
        statusLabel.Text = "Mounted Windows folder detected. Checking WIM index..."
        ScanClicked(Me, EventArgs.Empty)
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
        totalMetric.Text = "—"
        newMetric.Text = "—"
        sameMetric.Text = "—"
        lastTarget = "Not checked"
        lastAutoCompareSignature = ""
        targetLabel.Text = "WIM comparison pending. Use Scan / Refresh."
        statusLabel.Text = "Source changed. Scan / Refresh to read INF and compare automatically."
        ' Expired comparisons must clear ALL action states immediately, not
        ' only the Add button; never rely on a later click in the INF grid.
        auditButton.Enabled = False
        UpdateInstallReady()
    End Sub

    Private Sub WorkState(busy As Boolean)
        isWorking = busy
        scanButton.Enabled = Not busy
        auditButton.Enabled = Not busy AndAlso decisions.Count > 0
        recursive.Enabled = Not busy
        sourceBox.ReadOnly = busy
        grid.ReadOnly = busy
        Dim showStop = busy AndAlso isInstalling
        installButton.Visible = Not showStop
        stopButton.Visible = showStop
        If showStop Then stopButton.BringToFront()
        stopButton.Enabled = showStop AndAlso Not stopAfterCurrent
        UpdateInstallReady()
    End Sub

    Private Sub UpdateInstallReady()
        Dim eligibleCount = decisions.Where(
            Function(d) DriverStudioInstaller.IsAllowed(d)).Count()
        Dim selectedCount = decisions.Where(Function(d) d.Selected).Count()
        Dim allEligibleSelected = comparisonVerified AndAlso eligibleCount > 0 AndAlso
            decisions.All(Function(d)
                Return Not DriverStudioInstaller.IsAllowed(d) OrElse d.Selected
            End Function)
        Dim eligible = comparisonVerified AndAlso eligibleCount > 0
        installButton.Enabled = Not isWorking AndAlso eligible AndAlso
            decisions.Any(Function(d) d.Selected AndAlso DriverStudioInstaller.IsAllowed(d))
        selectEligibleButton.Text = If(allEligibleSelected,
            "Deselect New / Newer", "Select New / Newer")
        selectEligibleButton.AccessibleDescription = If(allEligibleSelected,
            "All eligible INF packages are already selected. Click to deselect them.",
            "Choose all verified New/Newer INF packages; no WIM changes are made.")
        selectEligibleButton.Enabled = Not isWorking AndAlso eligible
        clearSelectionButton.Enabled = Not isWorking AndAlso selectedCount > 0
        auditAllButton.Enabled = Not isWorking AndAlso decisions.Count > 0
        selectionLabel.Text = selectedCount.ToString() & " selected"
    End Sub

    Private Sub ApplyBulkSelection(selectEligible As Boolean)
        If isWorking OrElse decisions.Count = 0 Then Return
        Dim eligibleCount = decisions.Where(Function(d)
            Return comparisonVerified AndAlso DriverStudioInstaller.IsAllowed(d)
        End Function).Count()
        If selectEligible AndAlso eligibleCount > 200 Then
            statusLabel.Text = "Import limit: 200 INF per batch. Narrow the source first."
            Return
        End If
        bulkSelectionUpdate = True
        grid.SuspendLayout()
        Try
            For i As Integer = 0 To decisions.Count - 1
                Dim selected = selectEligible AndAlso comparisonVerified AndAlso
                    DriverStudioInstaller.IsAllowed(decisions(i))
                decisions(i).Selected = selected
                grid.Rows(i).Cells(0).Value = selected
            Next
        Finally
            grid.ResumeLayout(False)
            bulkSelectionUpdate = False
        End Try
        UpdateInstallReady()
    End Sub

    Private Sub SelectAllEligibleClicked(sender As Object, e As EventArgs)
        If isWorking Then Return
        If Not comparisonVerified Then
            statusLabel.Text = "Selected WIM index is not verified. Mount it, " &
                "then return here or click Scan / Refresh. Audit all is read-only."
            Return
        End If
        Dim eligibleCount = decisions.Where(Function(d) DriverStudioInstaller.IsAllowed(d)).Count()
        If eligibleCount = 0 Then Return
        Dim allSelected = decisions.All(Function(d)
            Return Not DriverStudioInstaller.IsAllowed(d) OrElse d.Selected
        End Function)
        ApplyBulkSelection(Not allSelected)
        If allSelected Then
            statusLabel.Text = "Deselected New/Newer INF packages. WIM unchanged."
        Else
            statusLabel.Text = "Marked " &
                decisions.Where(Function(d) d.Selected).Count().ToString() &
                " New/Newer INF(s). Nothing written; audit before Add to WIM."
        End If
    End Sub

    ' One read-only action for source inspection and (when already mounted)
    ' image comparison. No action here mounts a WIM or invokes DISM Add-Driver.
    ' An absent/invalid target never turns INF packages into "New".
    Private Async Sub ScanClicked(sender As Object, e As EventArgs)
        If isWorking Then Return
        Dim selectedPath = sourceBox.Text.Trim()
        Dim includeSubfolders = recursive.Checked
        Dim imagePath = If(currentWim(), "").Trim()
        Dim index = currentIndex()
        Dim mount = If(currentMount(), "").Trim()
        comparisonVerified = False
        comparedInventoryFingerprint = ""
        WorkState(True)
        statusLabel.Text = "Scanning INF packages (read-only)..."
        Dim sourceItems As List(Of DriverStudioEngine.DriverPackage) = Nothing
        Try
            sourceItems = Await Task.Run(
                Function() DriverStudioEngine.ScanSource(selectedPath, includeSubfolders))
            If IsDisposed Then Return
        Catch ex As Exception
            If IsDisposed Then Return
            RuntimeDiagnostics.RecordException("Driver Studio scan", ex)
            InvalidateComparison()
            statusLabel.Text = "INF scan failed: " & ex.Message
            WorkState(False)
            Return
        End Try

        Try
            ' Check whether any mounted Windows is present before invoking
            ' Get-WindowsDriver. The inventory bridge always verifies that
            ' WIMGAPI registered the EXACT WIM file, index and mount directory.
            Dim mountMayExist = imagePath.Length > 0 AndAlso File.Exists(imagePath) AndAlso
                index > 0 AndAlso mount.Length > 0 AndAlso
                Directory.Exists(Path.Combine(mount, "Windows"))
            If Not mountMayExist Then
                Dim sourceOnly = DriverStudioEngine.CompareCandidates(
                    sourceItems, New List(Of DriverStudioEngine.DriverPackage)(), False)
                ShowDecisions(sourceOnly, "WIM inventory NOT checked")
                targetLabel.Text = "Target: " & If(imagePath.Length = 0, "No WIM selected",
                    Path.GetFileName(imagePath)) & " | Index " & index.ToString() &
                    " | Not mounted / not verified"
                targetLabel.AccessibleDescription = "Selected mount folder: " & mount
                statusLabel.Text = sourceOnly.Count.ToString() &
                    " INF scanned. WIM not mounted; versions NOT checked. " &
                    "Mount the index; comparison runs on returning here."
                lastAutoCompareSignature = ""
                Return
            End If

            statusLabel.Text = "INF scan complete. Verifying WIM index and driver inventory..."
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
            If IsDisposed Then Return
            Dim results = DriverStudioEngine.CompareCandidates(
                sourceItems, installed, True, architecture)
            comparedInventoryFingerprint =
                DriverStudioInstaller.SnapshotInstalledInventory(installed)
            comparedArchitecture = architecture
            comparedWim = imagePath
            comparedIndex = index
            comparedMount = mount
            comparedSource = selectedPath
            comparedRecurse = includeSubfolders
            ShowDecisions(results, Path.GetFileName(imagePath) & " / Index " &
                          index.ToString() & " / " & mount, True)
            targetLabel.Text = "Target: " & Path.GetFileName(imagePath) &
                " | Index " & index.ToString() & " | WIM inventory verified"
            targetLabel.AccessibleDescription = "Verified mount folder: " & mount
            lastAutoCompareSignature = CurrentTargetSignature()
            statusLabel.Text = "Compared " & results.Count.ToString() &
                " INF(s) against " & installed.Count.ToString() &
                " WIM driver entries. Read-only; 0 selected by default."
        Catch ex As Exception
            If IsDisposed Then Return
            RuntimeDiagnostics.RecordException("Driver Studio automatic WIM comparison", ex)
            ' An INF-only result is still useful, but may NOT authorize import.
            Dim unverified = DriverStudioEngine.CompareCandidates(
                sourceItems, New List(Of DriverStudioEngine.DriverPackage)(), False)
            ShowDecisions(unverified, "WIM inventory NOT verified")
            targetLabel.Text = "Target: " & If(imagePath.Length = 0, "No WIM selected",
                Path.GetFileName(imagePath)) & " | Index " & index.ToString() &
                " | WIM NOT verified"
            targetLabel.AccessibleDescription = "Selected mount: " & mount
            statusLabel.Text = "INF scanned; WIM comparison unavailable. " &
                "No WIM changes. Use Scan / Refresh after checking Mount health."
            statusLabel.AccessibleDescription = ex.Message
            ' Suppress repeated automatic retries of the same broken target
            ' during tab navigation; Scan / Refresh explicitly retries.
            lastAutoCompareSignature = CurrentTargetSignature()
        Finally
            If Not IsDisposed Then WorkState(False)
        End Try
    End Sub

    Private Function CurrentTargetSignature() As String
        Return If(currentWim(), "").Trim().ToUpperInvariant() & "|" &
            currentIndex().ToString(Globalization.CultureInfo.InvariantCulture) & "|" &
            If(currentMount(), "").Trim().ToUpperInvariant() & "|" &
            sourceBox.Text.Trim().ToUpperInvariant() & "|" &
            recursive.Checked.ToString()
    End Function

    Public Sub PreviewForTest(items As IEnumerable(Of DriverStudioEngine.Decision),
                              Optional verified As Boolean = True)
        ShowDecisions(items.ToList(),
                      If(verified, "CI fixture - mounted index verified",
                         "CI fixture - INF-only scan; WIM not verified"), verified)
    End Sub

    Private Sub ShowDecisions(results As List(Of DriverStudioEngine.Decision),
                              comparisonTarget As String,
                              Optional verified As Boolean = False)
        comparisonVerified = verified
        If Not verified Then comparedInventoryFingerprint = ""
        packageAudits.Clear()
        lastTarget = comparisonTarget
        decisions = results
        grid.ShowCellToolTips = True
        grid.Columns(4).HeaderCell.ToolTipText =
            "Review = source INF metadata/conflict. Not checked = mounted WIM inventory unavailable."
        grid.SuspendLayout()
        Try
            grid.Rows.Clear()
            For Each d In results
                Dim canSelect = verified AndAlso DriverStudioInstaller.IsAllowed(d)
                ' Version comparison must never silently create a write plan.
                ' New/Newer rows are visible, but require explicit user selection.
                d.Selected = False
                Dim number = grid.Rows.Add(d.Selected, d.Candidate.InfName,
                    d.Candidate.Version, d.InstalledVersion, d.Status)
                grid.Rows(number).Cells(0).ReadOnly = Not canSelect
                grid.Rows(number).Cells(0).ToolTipText = If(canSelect,
                    "Include this New/Newer INF in the installation plan.",
                    "Not eligible for import; review or compare WIM first.")
                Dim cell = grid.Rows(number).Cells(4)
                cell.ToolTipText = If(Not verified AndAlso d.Status = "Review",
                    "Review SOURCE INF (no WIM comparison). " & d.Detail,
                    If(Not verified AndAlso d.Status = "Not checked",
                       "WIM inventory NOT checked. " & d.Detail,
                       d.Detail))
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
            "Read-only preview: no drivers selected, no WIM changes.")
        copyButton.Enabled = results.Count > 0
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
        ' Keep bulk-selection state synchronized after DataGridView focus moves.
        ' In particular, a completed asynchronous Scan must never require
        ' clicking an INF row just to activate Select New / Newer.
        UpdateInstallReady()
    End Sub

    Private Sub ShowSelected()
        If grid.SelectedRows.Count = 0 OrElse
           grid.SelectedRows(0).Index >= decisions.Count Then Return
        Dim d = decisions(grid.SelectedRows(0).Index)
        Dim src = d.Candidate
        detailBox.Text = "WIM inventory: " &
            If(comparisonVerified, "verified", "NOT verified (source INF scan only)") &
            Environment.NewLine &
            d.Status & " — " & d.Detail & Environment.NewLine &
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
        Dim chosen = decisions.Where(Function(d) d.Selected).
            Select(Function(d) d.Candidate).ToList()
        If chosen.Count = 0 AndAlso grid.SelectedRows.Count > 0 Then
            Dim rowIndex = grid.SelectedRows(0).Index
            If rowIndex >= 0 AndAlso rowIndex < decisions.Count Then
                chosen.Add(decisions(rowIndex).Candidate)
            End If
        End If
        Await AuditPackagesAsync(chosen)
    End Sub

    Private Async Sub AuditAllClicked(sender As Object, e As EventArgs)
        If isWorking OrElse decisions.Count = 0 Then Return
        ' Audit is READ-ONLY and can inspect INF/CAT even when the WIM
        ' inventory was not verified. It never selects drivers for install.
        Await AuditPackagesAsync(decisions.Select(Function(d) d.Candidate).ToList())
    End Sub

    Private Async Function AuditPackagesAsync(
        packages As List(Of DriverStudioEngine.DriverPackage)) As Task
        If isWorking OrElse packages.Count = 0 Then Return
        If packages.Count > 30 Then
            MessageBox.Show(Me.FindForm(),
                "Audit up to 30 INF packages at a time. Narrow the source folder.",
                "Driver Studio - Package audit", MessageBoxButtons.OK,
                MessageBoxIcon.Information)
            Return
        End If
        Dim targetArchitecture = If(comparisonVerified, comparedArchitecture, "")
        WorkState(True)
        Dim trusted As Integer = 0
        Try
            For i As Integer = 0 To packages.Count - 1
                Dim package = packages(i)
                statusLabel.Text = "Auditing " & (i + 1).ToString() &
                    "/" & packages.Count.ToString() & ": " & package.InfName
                Dim report = Await Task.Run(Function()
                    Return DriverStudioPackageAudit.Audit(
                        package, targetArchitecture, True)
                End Function)
                If IsDisposed Then Return
                packageAudits(package.FilePath) = report
                If report.CanInstall Then trusted += 1
                detailBox.Text = report.Summary
            Next
            statusLabel.Text = "Package audit: " & trusted.ToString() & "/" &
                packages.Count.ToString() & " trusted. " &
                If(comparisonVerified,
                   "WIM compared; install still requires R/W mount and approval.",
                   "WIM not compared. Mount the image; Scan / Refresh will compare automatically.")
            ShowSelected()
        Catch ex As Exception
            RuntimeDiagnostics.RecordException("Driver Studio package audit", ex)
            statusLabel.Text = "Package audit stopped: " & ex.Message
            detailBox.Text = "No DISM writes were performed." &
                             Environment.NewLine & ex.Message
        Finally
            If Not IsDisposed Then WorkState(False)
        End Try
    End Function

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
                "Use Audit selected first. A complete source-package SHA-256 " &
                "manifest and a valid CAT Authenticode signature are required." &
                Environment.NewLine &
                "Select New / Newer, then Audit selected; this does not install.",
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
                "Click Scan / Refresh to verify the mounted WIM again.",
                "Driver Studio - Comparison expired", MessageBoxButtons.OK,
                MessageBoxIcon.Warning)
            Return
        End If

        Dim prompt = "Add " & selected.Count.ToString() &
                     " selected INF package(s) to the MOUNTED OFFLINE WIM:" & Environment.NewLine &
                     comparedWim & Environment.NewLine &
                     "Index " & comparedIndex.ToString() &
                     " | Mount folder: " & comparedMount & Environment.NewLine &
                     Environment.NewLine &
                     "DISM will MODIFY this offline WIM, NOT the running Windows PC. Only New/Newer INF files " &
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
                        "Scan / Refresh before importing.")
                End If
                Return indexInfo.Architecture
            End Function)
            Dim plan = DriverStudioInstaller.ValidatePlan(
                selected, sourceItems, installed, refreshedArchitecture,
                comparedInventoryFingerprint)
            Dim expectedInventoryFingerprint =
                DriverStudioInstaller.SnapshotInstalledInventory(installed)

            For Each package As DriverStudioEngine.DriverPackage In plan
                If stopAfterCurrent Then Exit For
                ' An external operation might unmount the image during a long
                ' batch. Re-check the index and read/write flag before each INF.
                Await Task.Run(Function()
                    Return DriverStudioInstaller.RequireWritableTarget(
                        comparedWim, comparedIndex, comparedMount)
                End Function)
                ' Even while we hold the cooperative writer lease, an external
                ' DISM or administrator can change the mount. Do not trust
                ' the batch's initial inventory after the previous INF.
                Dim beforeStep = Await DriverStudioInventory.ReadMountedAsync(
                    comparedWim, comparedIndex, comparedMount, CancellationToken.None)
                DriverStudioInstaller.RequireInventorySnapshot(
                    expectedInventoryFingerprint, beforeStep)

                statusLabel.Text = "Installing " & (completed + 1).ToString() & "/" &
                    plan.Count.ToString() & ": " & package.InfName &
                    " (do not close DISM GUI)"
                Dim outcome = Await DriverStudioInstaller.AddOneAsync(
                    mountRoot, package, packageAudits(package.FilePath), comparedArchitecture)
                completed += 1
                Dim checkpointError As Exception = Nothing

                If Not outcome.Succeeded Then
                    RuntimeDiagnostics.RecordDismExit(
                        "Driver Studio install", outcome.ExitCode.ToString())
                Else
                    ' A successful DISM process exit is NOT an inventory
                    ' confirmation. Do not label the item as installed, or
                    ' include it in successful counts, until the checkpoint
                    ' verifies the mounted image after this specific INF.
                    Try
                        statusLabel.Text = "Verifying driver " & completed.ToString() &
                            "/" & plan.Count.ToString() & " in mounted inventory..."
                        Dim afterStep = Await DriverStudioInventory.ReadMountedAsync(
                            comparedWim, comparedIndex, comparedMount,
                            CancellationToken.None)
                        expectedInventoryFingerprint =
                            DriverStudioInstaller.ValidateStagedBatchCheckpoint(
                                beforeStep, afterStep, package)
                        succeeded += 1
                        appliedPackages.Add(package)
                        importHistory.Add("Checkpoint confirmed: " & package.InfName &
                            " staged; unrelated driver inventory unchanged.")
                    Catch verificationError As Exception
                        checkpointError = verificationError
                    End Try
                End If

                Dim stageStatus = If(Not outcome.Succeeded, "FAILED",
                    If(checkpointError Is Nothing, "Installed (verified)", "UNVERIFIED"))
                importHistory.Add(package.InfName & " | " & stageStatus &
                    " | DISM exit: " & outcome.ExitCode.ToString() &
                    If(outcome.RebootRequired, " | Restart required", "") &
                    Environment.NewLine & outcome.Details)

                For Each row As DataGridViewRow In grid.Rows
                    If String.Equals(Convert.ToString(row.Cells(1).Value),
                                     package.InfName, StringComparison.OrdinalIgnoreCase) Then
                        row.Cells(4).Value = stageStatus
                        row.Cells(0).Value = False
                        Exit For
                    End If
                Next

                If checkpointError IsNot Nothing Then
                    importHistory.Add("UNVERIFIED: " & checkpointError.Message &
                        " Remaining drivers were not installed.")
                    Throw New InvalidOperationException(
                        "DISM returned success for " & package.InfName &
                        " but the mounted inventory checkpoint failed. " &
                        "Remaining drivers were blocked. Review the image before " &
                        "any Commit or Discard.", checkpointError)
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
            comparedInventoryFingerprint = ""
            isInstalling = False
            stopAfterCurrent = False
            If Not IsDisposed Then
                WorkState(False)
                installButton.Enabled = False
                stopButton.Enabled = False
            End If
        End Try
    End Sub

End Class
