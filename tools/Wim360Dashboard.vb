Imports System
Imports System.Drawing
Imports System.IO
Imports System.Text
Imports System.Linq
Imports System.Threading
Imports System.Threading.Tasks
Imports System.Windows.Forms

' Embedded, read-only WIM 360 workspace. Only the explicit "Use in Mount"
' button transfers a chosen file/index to the existing Mount controls.
Public NotInheritable Class Wim360Dashboard
    Inherits UserControl

    Private ReadOnly mountPath As Func(Of String)
    Private ReadOnly useInMount As Action(Of String, Integer)
    Private ReadOnly canvas As Panel
    Private ReadOnly dashboardLayout As TableLayoutPanel
    Private ReadOnly fileBox As TextBox
    Private ReadOnly browse As Button
    Private ReadOnly scan As Button
    Private ReadOnly imageCount As Label
    Private ReadOnly fileSize As Label
    Private ReadOnly mounts As Label
    Private ReadOnly status As Label
    Private ReadOnly indexTable As DataGridView
    Private ReadOnly details As TextBox
    Private ReadOnly useSelected As Button
    Private ReadOnly copyReport As Button
    Private ReadOnly auditMounted As Button
    Private ReadOnly inventoryButton As Button
    Private ReadOnly compareSnapshotsButton As Button
    Private ReadOnly exportIndexesCsv As Button
    Private ReadOnly exportIndexesJson As Button
    Private ReadOnly compareIndexesButton As Button
    Private report As Wim360Reader.WimReport
    Private scanGeneration As Integer
    Private isAuditing As Boolean
    Private auditCancellation As CancellationTokenSource

    Public Sub New(getMountPath As Func(Of String),
                   applyIndex As Action(Of String, Integer))
        If getMountPath Is Nothing Then Throw New ArgumentNullException(NameOf(getMountPath))
        If applyIndex Is Nothing Then Throw New ArgumentNullException(NameOf(applyIndex))
        mountPath = getMountPath
        useInMount = applyIndex

        Me.Name = "Wim360Dashboard"
        Me.Dock = DockStyle.Fill
        Me.Margin = Padding.Empty
        Me.Padding = Padding.Empty
        Me.BackColor = Color.White
        Me.Font = ModernTheme.UiFont(10)
        Me.AutoScaleMode = AutoScaleMode.Font

        canvas = New Panel With {.Name = "Wim360Viewport",
                                 .Dock = DockStyle.Fill,
                                 .AutoScroll = True,
                                 .BackColor = Color.White}
        Controls.Add(canvas)

        dashboardLayout = New TableLayoutPanel With {
            .Name = "Wim360Layout",
            .ColumnCount = 1, .RowCount = 7,
            .Dock = DockStyle.Top, .Height = 731, .Width = 710,
            .BackColor = Color.White,
            .Margin = Padding.Empty, .Padding = New Padding(17, 13, 17, 18)
        }
        dashboardLayout.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100))
        ' Scroll rather than truncate content on high-DPI or short viewports.
        Dim factor As Single = Math.Max(1.0F, Me.Font.Height / 16.0F)
        For Each height As Integer In {62, 66, 85, 36, 215, 163, 314}
            dashboardLayout.RowStyles.Add(New RowStyle(SizeType.Absolute,
                CInt(Math.Ceiling(height * factor))))
        Next
        dashboardLayout.Height = dashboardLayout.RowStyles.Cast(Of RowStyle)().
            Sum(Function(row) CInt(row.Height)) + dashboardLayout.Padding.Vertical + 8
        canvas.Controls.Add(dashboardLayout)
        AddHandler canvas.ClientSizeChanged, Sub(sender, e)
            Dim newWidth = Math.Max(710, canvas.ClientSize.Width - 1)
            If dashboardLayout.Width <> newWidth Then dashboardLayout.Width = newWidth
        End Sub

        Dim header As New TableLayoutPanel With {
            .Dock = DockStyle.Fill, .RowCount = 2, .ColumnCount = 1,
            .Margin = Padding.Empty, .BackColor = Color.White
        }
        header.RowStyles.Add(New RowStyle(SizeType.Percent, 55))
        header.RowStyles.Add(New RowStyle(SizeType.Percent, 45))
        header.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100))
        dashboardLayout.Controls.Add(header, 0, 0)
        header.Controls.Add(MakeText("WIM 360  |  IMAGE DASHBOARD",
                                    13, True, ModernTheme.TextInk), 0, 0)
        header.Controls.Add(MakeText("Read-only preview  •  No mounting or changes",
                                    9.4F, False, ModernTheme.Muted), 0, 1)

        Dim pathRow As New TableLayoutPanel With {
            .Dock = DockStyle.Fill, .RowCount = 1, .ColumnCount = 3,
            .Margin = New Padding(0, 4, 0, 7), .BackColor = Color.White
        }
        pathRow.RowStyles.Add(New RowStyle(SizeType.Percent, 100))
        pathRow.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100))
        pathRow.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute, 165))
        pathRow.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute, 130))
        dashboardLayout.Controls.Add(pathRow, 0, 1)

        fileBox = New TextBox With {
            .Name = "txt360Path", .Dock = DockStyle.Fill,
            .PlaceholderText = "Choose boot.wim, install.wim or install.swm",
            .Font = ModernTheme.UiFont(10), .BackColor = Color.White,
            .ForeColor = ModernTheme.TextInk,
            .BorderStyle = BorderStyle.FixedSingle,
            .Margin = New Padding(1, 13, 7, 9)
        }
        pathRow.Controls.Add(fileBox, 0, 0)

        browse = New Button With {
            .Name = "btn360Browse", .Text = "Choose WIM",
            .Dock = DockStyle.Fill, .Margin = New Padding(3, 7, 5, 5)
        }
        ModernTheme.StyleButton(browse, "browse")
        AddHandler browse.Click, AddressOf BrowseClicked
        pathRow.Controls.Add(browse, 1, 0)

        scan = New Button With {
            .Name = "btn360Scan", .Text = "Inspect",
            .Dock = DockStyle.Fill, .Margin = New Padding(3, 7, 0, 5)
        }
        ModernTheme.StyleButton(scan, "primary")
        AddHandler scan.Click, AddressOf ScanClicked
        pathRow.Controls.Add(scan, 2, 0)

        Dim metrics As New TableLayoutPanel With {
            .Name = "Wim360Metrics",
            .Dock = DockStyle.Fill, .RowCount = 1, .ColumnCount = 3,
            .Margin = New Padding(0, 5, 0, 7),
            .BackColor = Color.White
        }
        metrics.RowStyles.Add(New RowStyle(SizeType.Percent, 100))
        For i As Integer = 1 To 3
            metrics.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, CSng(100.0 / 3.0)))
        Next
        dashboardLayout.Controls.Add(metrics, 0, 2)
        imageCount = AddMetric(metrics, 0, "INDEXES", "—")
        fileSize = AddMetric(metrics, 1, "FILE SIZE", "—")
        mounts = AddMetric(metrics, 2, "MOUNT STATUS", "Not checked")

        status = MakeText("Select a WIM file to inspect its available indexes.",
                          9.5F, False, ModernTheme.Muted)
        status.Name = "lbl360Status"
        status.Margin = New Padding(0, 1, 0, 2)
        dashboardLayout.Controls.Add(status, 0, 3)

        indexTable = New DataGridView With {
            .Name = "grid360Indexes",
            .Dock = DockStyle.Fill, .Margin = New Padding(0, 3, 0, 11),
            .ReadOnly = True, .AllowUserToAddRows = False,
            .AllowUserToDeleteRows = False, .AllowUserToResizeRows = False,
            .RowHeadersVisible = False, .MultiSelect = False,
            .SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            .AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            .BackgroundColor = Color.White, .GridColor = Color.FromArgb(222, 232, 244),
            .BorderStyle = BorderStyle.FixedSingle,
            .ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing,
            .ColumnHeadersHeight = 33, .RowTemplate = New DataGridViewRow() With {.Height = 29},
            .EnableHeadersVisualStyles = False
        }
        indexTable.RowTemplate.Height = 29
        indexTable.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(233, 242, 253)
        indexTable.ColumnHeadersDefaultCellStyle.ForeColor = ModernTheme.TextInk
        indexTable.ColumnHeadersDefaultCellStyle.Font = ModernTheme.UiFont(9.5F, True)
        indexTable.DefaultCellStyle.Font = ModernTheme.UiFont(9.4F)
        indexTable.DefaultCellStyle.ForeColor = ModernTheme.TextInk
        indexTable.DefaultCellStyle.BackColor = Color.White
        indexTable.DefaultCellStyle.SelectionBackColor = Color.FromArgb(218, 235, 252)
        indexTable.DefaultCellStyle.SelectionForeColor = ModernTheme.TextInk
        indexTable.Columns.Add("col360Index", "Index")
        indexTable.Columns.Add("col360Name", "Image name")
        indexTable.Columns.Add("col360Build", "OS build")
        indexTable.Columns.Add("col360Mount", "Mount state")
        indexTable.Columns(0).FillWeight = 47
        indexTable.Columns(1).FillWeight = 210
        indexTable.Columns(2).FillWeight = 90
        indexTable.Columns(3).FillWeight = 145
        AddHandler indexTable.SelectionChanged, AddressOf IndexSelectionChanged
        dashboardLayout.Controls.Add(indexTable, 0, 4)

        Dim info As New TableLayoutPanel With {
            .Dock = DockStyle.Fill, .ColumnCount = 1, .RowCount = 2,
            .Margin = Padding.Empty, .BackColor = Color.White
        }
        info.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100))
        info.RowStyles.Add(New RowStyle(SizeType.Absolute, 29))
        info.RowStyles.Add(New RowStyle(SizeType.Percent, 100))
        dashboardLayout.Controls.Add(info, 0, 5)
        info.Controls.Add(MakeText("SELECTED INDEX  |  METADATA",
                                  9.5F, True, ModernTheme.Blue), 0, 0)

        details = New TextBox With {
            .Name = "txt360Details", .Dock = DockStyle.Fill,
            .Multiline = True, .ReadOnly = True, .WordWrap = True,
            .ScrollBars = ScrollBars.Vertical,
            .Font = ModernTheme.UiFont(9.5F),
            .BackColor = Color.FromArgb(249, 251, 254),
            .ForeColor = ModernTheme.TextInk,
            .BorderStyle = BorderStyle.FixedSingle,
            .Text = "Choose an index to view architecture, edition, languages, version and mount state.",
            .Margin = New Padding(0, 0, 0, 4)
        }
        info.Controls.Add(details, 0, 1)

        Dim actions As New TableLayoutPanel With {
            .Dock = DockStyle.Fill, .ColumnCount = 2, .RowCount = 5,
            .Margin = New Padding(0, 5, 0, 0),
            .BackColor = Color.White
        }
        For i As Integer = 1 To 5
            actions.RowStyles.Add(New RowStyle(SizeType.Percent, 20.0F))
        Next
        actions.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 50))
        actions.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 50))
        dashboardLayout.Controls.Add(actions, 0, 6)

        useSelected = New Button With {
            .Name = "btn360UseInMount", .Text = "Use index in Mount",
            .Dock = DockStyle.Fill, .Margin = New Padding(0, 4, 9, 4),
            .Enabled = False
        }
        ModernTheme.StyleButton(useSelected, "secondary")
        AddHandler useSelected.Click, AddressOf UseClicked
        actions.Controls.Add(useSelected, 0, 0)

        copyReport = New Button With {
            .Name = "btn360CopyReport", .Text = "Copy report",
            .Dock = DockStyle.Fill, .Margin = New Padding(0, 4, 8, 4),
            .Enabled = False
        }
        ModernTheme.StyleButton(copyReport, "browse")
        AddHandler copyReport.Click, AddressOf CopyClicked
        actions.Controls.Add(copyReport, 1, 0)

        auditMounted = New Button With {
            .Name = "btn360AuditMounted", .Text = "Scan mounted index",
            .Dock = DockStyle.Fill, .Margin = New Padding(0, 4, 9, 4),
            .Enabled = False
        }
        ModernTheme.StyleButton(auditMounted, "secondary")
        AddHandler auditMounted.Click, AddressOf AuditMountedClicked
        actions.Controls.Add(auditMounted, 0, 1)

        inventoryButton = New Button With {
            .Name = "btn360Inventory", .Text = "Inventory details",
            .Dock = DockStyle.Fill, .Margin = New Padding(0, 4, 8, 4),
            .Enabled = False
        }
        ModernTheme.StyleButton(inventoryButton, "browse")
        AddHandler inventoryButton.Click, AddressOf InventoryClicked
        actions.Controls.Add(inventoryButton, 1, 1)

        compareSnapshotsButton = New Button With {
            .Name = "btn360CompareSnapshots", .Text = "Compare JSON snapshots",
            .Dock = DockStyle.Fill, .Margin = New Padding(0, 4, 9, 4)
        }
        ModernTheme.StyleButton(compareSnapshotsButton, "secondary")
        AddHandler compareSnapshotsButton.Click, AddressOf CompareSnapshotsClicked
        actions.Controls.Add(compareSnapshotsButton, 0, 2)

        Dim testCenter As New Button With {
            .Name = "btn360TestCenter", .Text = "Run complete safe tests",
            .Dock = DockStyle.Fill, .Margin = New Padding(0, 4, 8, 4)
        }
        ModernTheme.StyleButton(testCenter, "browse")
        AddHandler testCenter.Click, Sub(sender, e)
            Using viewer As New IntegratedTestCenterForm(fileBox.Text.Trim())
                viewer.ShowDialog(Me.FindForm())
            End Using
        End Sub
        actions.Controls.Add(testCenter, 1, 2)

        exportIndexesCsv = New Button With {
            .Name = "btn360ExportIndexCsv", .Text = "Export indexes CSV",
            .Dock = DockStyle.Fill, .Margin = New Padding(0, 4, 9, 4),
            .Enabled = False
        }
        ModernTheme.StyleButton(exportIndexesCsv, "browse")
        AddHandler exportIndexesCsv.Click, Sub(sender, e) ExportIndexMetadata(False)
        actions.Controls.Add(exportIndexesCsv, 0, 3)

        exportIndexesJson = New Button With {
            .Name = "btn360ExportIndexJson", .Text = "Export indexes JSON",
            .Dock = DockStyle.Fill, .Margin = New Padding(0, 4, 8, 4),
            .Enabled = False
        }
        ModernTheme.StyleButton(exportIndexesJson, "browse")
        AddHandler exportIndexesJson.Click, Sub(sender, e) ExportIndexMetadata(True)
        actions.Controls.Add(exportIndexesJson, 1, 3)

        compareIndexesButton = New Button With {
            .Name = "btn360CompareIndexes", .Text = "Compare indexes",
            .Dock = DockStyle.Fill, .Margin = New Padding(0, 4, 9, 4),
            .Enabled = False
        }
        ModernTheme.StyleButton(compareIndexesButton, "primary")
        AddHandler compareIndexesButton.Click, Sub(sender, e)
            If report Is Nothing OrElse report.ImageCount < 2 Then Return
            Using viewer As New Wim360CompareIndexesForm(report)
                viewer.ShowDialog(Me.FindForm())
            End Using
        End Sub
        actions.Controls.Add(compareIndexesButton, 0, 4)
        Dim compareHelp = MakeText("2–8 indexes: present / missing / different",
                                   9.1F, False, ModernTheme.Muted)
        compareHelp.Margin = New Padding(4, 3, 0, 3)
        actions.Controls.Add(compareHelp, 1, 4)

        AddHandler fileBox.TextChanged, Sub(sender, e)
            CancelMountedAudit()
            useSelected.Enabled = False
            copyReport.Enabled = False
            auditMounted.Enabled = False
            inventoryButton.Enabled = False
            exportIndexesCsv.Enabled = False
            exportIndexesJson.Enabled = False
            compareIndexesButton.Enabled = False
        End Sub
        AddHandler Me.Disposed, Sub(sender, e) CancelMountedAudit()
    End Sub

    Private Shared Function MakeText(value As String, size As Single, bold As Boolean,
                                     color As Color) As Label
        Return New Label With {
            .Text = value, .Font = ModernTheme.UiFont(size, bold),
            .BackColor = Color.Transparent, .ForeColor = color,
            .Dock = DockStyle.Fill, .AutoEllipsis = True,
            .TextAlign = ContentAlignment.MiddleLeft,
            .Margin = Padding.Empty
        }
    End Function

    Private Shared Function AddMetric(parent As TableLayoutPanel, position As Integer,
                                       name As String, current As String) As Label
        Dim tile As New TableLayoutPanel With {
            .Dock = DockStyle.Fill, .RowCount = 2, .ColumnCount = 1,
            .BackColor = Color.FromArgb(236, 245, 254),
            .Margin = New Padding(0, 0, If(position = 2, 0, 9), 0),
            .Padding = New Padding(13, 6, 8, 6)
        }
        tile.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100))
        tile.RowStyles.Add(New RowStyle(SizeType.Percent, 44))
        tile.RowStyles.Add(New RowStyle(SizeType.Percent, 56))
        parent.Controls.Add(tile, position, 0)
        tile.Controls.Add(MakeText(name, 8.5F, True, ModernTheme.Muted), 0, 0)
        Dim value = MakeText(current, 12.2F, True, ModernTheme.TextInk)
        value.Name = "metric360" & position.ToString()
        tile.Controls.Add(value, 0, 1)
        Return value
    End Function

    Public Sub ActivateFromMount()
        ' Treat the current Mount path as a suggestion only. Never override the
        ' user's WIM 360 choice when returning to the dashboard.
        If String.IsNullOrWhiteSpace(fileBox.Text) Then
            Dim suggestedPath = mountPath()
            If Not String.IsNullOrWhiteSpace(suggestedPath) Then
                fileBox.Text = suggestedPath.Trim()
                StartScan()
                Return
            End If
        End If

        ' WinForms DataGridView may clear selection while its TabPage is hidden.
        ' Re-evaluate the usable index after the page becomes visible.
        If report IsNot Nothing AndAlso indexTable.Rows.Count > 0 Then
            If indexTable.SelectedRows.Count = 0 Then
                indexTable.Rows(0).Selected = True
            End If
            UpdateSelection()
        End If
    End Sub

    Private Sub BrowseClicked(sender As Object, e As EventArgs)
        Using picker As New OpenFileDialog With {
            .Title = "Choose WIM/ESD or split SWM for read-only inventory",
            .Filter = "Windows images (*.wim;*.esd;*.swm)|*.wim;*.esd;*.swm|All files (*.*)|*.*",
            .CheckFileExists = True, .Multiselect = False,
            .RestoreDirectory = True
        }
            If File.Exists(fileBox.Text.Trim()) Then
                picker.FileName = fileBox.Text.Trim()
            End If
            If picker.ShowDialog(Me.FindForm()) = DialogResult.OK Then
                fileBox.Text = picker.FileName
                StartScan()
            End If
        End Using
    End Sub

    Private Sub ScanClicked(sender As Object, e As EventArgs)
        StartScan()
    End Sub

    Public Sub ScanPathForTest(path As String)
        fileBox.Text = path
        StartScan()
    End Sub

    Private Async Sub StartScan()
        CancelMountedAudit()
        Dim path = fileBox.Text.Trim()
        scanGeneration += 1
        Dim generation = scanGeneration
        report = Nothing
        indexTable.Rows.Clear()
        details.Text = "Inspecting image metadata..."
        imageCount.Text = "—"
        fileSize.Text = "—"
        mounts.Text = "Checking"
        useSelected.Enabled = False
        copyReport.Enabled = False
        auditMounted.Enabled = False
        inventoryButton.Enabled = False
        exportIndexesCsv.Enabled = False
        exportIndexesJson.Enabled = False
        compareIndexesButton.Enabled = False
        If String.IsNullOrWhiteSpace(path) Then
            status.Text = "Choose a .wim, .esd or .swm file first."
            details.Text = "WIM 360 only reads image metadata."
            mounts.Text = "Not checked"
            Return
        End If
        scan.Enabled = False
        browse.Enabled = False
        status.Text = "Reading WIM metadata (read-only)..."

        Try
            Dim loaded = Await Task.Run(Function() Wim360Reader.Inspect(path))
            If Me.IsDisposed OrElse generation <> scanGeneration OrElse
               Not String.Equals(path, fileBox.Text.Trim(), StringComparison.OrdinalIgnoreCase) Then
                Return
            End If
            ' Normalize relative paths so Use in Mount uses the inspected file.
            If Not String.Equals(fileBox.Text.Trim(), loaded.ImageFile,
                                 StringComparison.OrdinalIgnoreCase) Then
                fileBox.Text = loaded.ImageFile
            End If
            ShowReport(loaded)
        Catch ex As Exception
            ErrorDiagnostics.RememberFailure("WIM 360 metadata inspection", ex)
            If Me.IsDisposed OrElse generation <> scanGeneration Then Return
            mounts.Text = "Unavailable"
            status.Text = "Cannot inspect image; open Tools > Report error to GitHub."
            details.Text = "No files were modified." & Environment.NewLine &
                           ErrorDiagnostics.Redact(ex.GetType().Name & ": " & ex.Message)
        Finally
            If Not Me.IsDisposed AndAlso generation = scanGeneration Then
                scan.Enabled = True
                browse.Enabled = True
            End If
        End Try
    End Sub

    Public Sub PreviewMetadataForTest(xml As String)
        ShowReport(Wim360Reader.ParseMetadataXml(xml, "C:\test\boot.wim", 1048576))
    End Sub

    Private Sub ShowReport(value As Wim360Reader.WimReport)
        report = value
        imageCount.Text = value.ImageCount.ToString()
        fileSize.Text = Wim360Reader.HumanSize(value.FileSizeBytes)
        Dim mounted = value.Images.Where(Function(x) x.MountStatus.StartsWith("Mounted",
                                                 StringComparison.OrdinalIgnoreCase)).Count()
        mounts.Text = If(value.MountScanWarning.Length > 0, "Not checked",
                         If(mounted = 0, "No mounts", mounted.ToString() & " mounted"))
        status.Text = If(value.MountScanWarning.Length > 0,
                         value.MountScanWarning,
                         If(value.IsSplitSet,
                            "Split WIM: " & Wim360SplitSet.Describe(value.SplitSet) &
                                "  Read-only; no mounting or servicing.",
                            "Metadata loaded. Select an index to inspect its details."))
        indexTable.SuspendLayout()
        Try
            indexTable.Rows.Clear()
            For Each image As Wim360Reader.ImageInfo In value.Images
                indexTable.Rows.Add(image.Index, image.Name, image.Version, image.MountStatus)
            Next
        Finally
            indexTable.ResumeLayout(True)
        End Try
        copyReport.Enabled = True
        exportIndexesCsv.Enabled = value.ImageCount > 0
        exportIndexesJson.Enabled = value.ImageCount > 0
        compareIndexesButton.Enabled = value.ImageCount >= 2
        If indexTable.Rows.Count > 0 Then
            indexTable.ClearSelection()
            indexTable.Rows(0).Selected = True
            UpdateSelection()
        End If
    End Sub

    Private Function SelectedImage() As Wim360Reader.ImageInfo
        If report Is Nothing OrElse indexTable.Rows.Count = 0 Then Return Nothing
        ' The selected row is authoritative; CurrentRow (and then the first
        ' displayed image) keeps the action available across tab activation.
        Dim selectedRow As DataGridViewRow = Nothing
        If indexTable.SelectedRows.Count > 0 Then
            selectedRow = indexTable.SelectedRows(0)
        ElseIf indexTable.CurrentRow IsNot Nothing Then
            selectedRow = indexTable.CurrentRow
        Else
            selectedRow = indexTable.Rows(0)
        End If
        Dim wanted As Integer = 0
        If Not Integer.TryParse(Convert.ToString(selectedRow.Cells(0).Value),
                                wanted) Then Return Nothing
        Return report.Images.FirstOrDefault(Function(x) x.Index = wanted)
    End Function

    Private Sub IndexSelectionChanged(sender As Object, e As EventArgs)
        UpdateSelection()
    End Sub

    Private Sub UpdateSelection()
        Dim selected = SelectedImage()
        useSelected.Enabled = selected IsNot Nothing AndAlso
                               report IsNot Nothing AndAlso Not report.IsSplitSet AndAlso
                              Not String.IsNullOrWhiteSpace(fileBox.Text) AndAlso
                              report IsNot Nothing AndAlso
                              String.Equals(fileBox.Text.Trim(), report.ImageFile,
                                            StringComparison.OrdinalIgnoreCase)
        auditMounted.Enabled = Not isAuditing AndAlso selected IsNot Nothing AndAlso
            report IsNot Nothing AndAlso Not report.IsSplitSet AndAlso
            report IsNot Nothing AndAlso
            String.Equals(fileBox.Text.Trim(), report.ImageFile,
                          StringComparison.OrdinalIgnoreCase) AndAlso
            selected.MountStatus.StartsWith("Mounted", StringComparison.OrdinalIgnoreCase) AndAlso
            Not String.IsNullOrWhiteSpace(selected.MountFolder)
        inventoryButton.Enabled = selected IsNot Nothing AndAlso
            selected.ServicingAudit IsNot Nothing AndAlso report IsNot Nothing AndAlso
            String.Equals(fileBox.Text.Trim(), report.ImageFile,
                          StringComparison.OrdinalIgnoreCase)
        If selected Is Nothing Then Return
        details.Text = If(report IsNot Nothing AndAlso report.IsSplitSet,
                          "SPLIT WIM: " & report.SplitSet.PartCount.ToString() &
                          " verified parts (headers). Viewing only." &
                          Environment.NewLine & Environment.NewLine, "") &
                       "Index " & selected.Index.ToString() & "  |  " & selected.Name &
                       Environment.NewLine &
                       "Description: " & If(selected.Description.Length > 0,
                                             selected.Description, "Not listed") &
                       Environment.NewLine &
                       "Edition: " & If(selected.Edition.Length > 0, selected.Edition, "Not listed") &
                       "     Architecture: " & selected.Architecture &
                       "     Build: " & selected.Version &
                       Environment.NewLine &
                       "Product: " & selected.Product &
                       "     Type: " & selected.InstallationType &
                       Environment.NewLine &
                       "Uncompressed: " & Wim360Reader.HumanSize(selected.UncompressedBytes) &
                       "     Languages: " & If(selected.Languages.Length > 0,
                                                selected.Languages, "Not listed") &
                       Environment.NewLine &
                       "Mount: " & selected.MountStatus &
                       If(selected.MountFolder.Length > 0,
                          "  |  " & selected.MountFolder, "")
        If selected.ServicingAudit IsNot Nothing Then
            details.Text &= Environment.NewLine & Environment.NewLine &
                            Wim360Servicing.FormatAudit(selected.ServicingAudit)
        ElseIf selected.MountStatus.StartsWith("Mounted", StringComparison.OrdinalIgnoreCase) Then
            details.Text &= Environment.NewLine & Environment.NewLine &
                "Click Scan mounted index for read-only driver and Windows package counts."
        End If
    End Sub

    Private Sub CancelMountedAudit()
        If auditCancellation Is Nothing Then Return
        Try
            auditCancellation.Cancel()
        Catch ex As ObjectDisposedException
        End Try
    End Sub

    Private Async Sub AuditMountedClicked(sender As Object, e As EventArgs)
        If isAuditing Then Return
        Dim selected = SelectedImage()
        Dim imageReport = report
        If selected Is Nothing OrElse imageReport Is Nothing OrElse
           Not selected.MountStatus.StartsWith("Mounted", StringComparison.OrdinalIgnoreCase) OrElse
           String.IsNullOrWhiteSpace(selected.MountFolder) Then Return

        Dim generation = scanGeneration
        Dim path = imageReport.ImageFile
        Dim auditToken As New CancellationTokenSource()
        auditCancellation = auditToken
        isAuditing = True
        auditMounted.Enabled = False
        status.Text = "Scanning mounted index " & selected.Index.ToString() &
                      ": drivers and Windows packages (read-only)..."
        Dim progress As IProgress(Of String) = New Progress(Of String)(Sub(message)
            If Not Me.IsDisposed AndAlso Not auditToken.IsCancellationRequested AndAlso
               generation = scanGeneration AndAlso
               Object.ReferenceEquals(report, imageReport) Then
                status.Text = message
            End If
        End Sub)
        Dim errorMessage As String = Nothing
        Try
            Dim audit = Await Wim360Servicing.InspectMountedAsync(
                path, selected.Index, selected.MountFolder, auditToken.Token, progress)
            If Me.IsDisposed OrElse auditToken.IsCancellationRequested OrElse
               generation <> scanGeneration OrElse
               Not Object.ReferenceEquals(report, imageReport) OrElse
               Not String.Equals(fileBox.Text.Trim(), path, StringComparison.OrdinalIgnoreCase) Then Return
            selected.ServicingAudit = audit
            status.Text = "Mounted index " & selected.Index.ToString() &
                If(audit.HasWarnings,
                   " PARTIAL (some categories unavailable) — open Inventory details.",
                   " audited: " & audit.DriverCount.ToString() & " drivers, " &
                   audit.PackageCount.ToString() & " packages, " &
                   audit.Components.Count.ToString() & " components. No changes.")
        Catch ex As OperationCanceledException
            If Not Me.IsDisposed AndAlso Object.ReferenceEquals(report, imageReport) Then
                status.Text = "Mounted audit canceled; no image changes were made."
            End If
        Catch ex As Exception
            ErrorDiagnostics.RememberFailure("WIM 360 mounted audit", ex)
            errorMessage = ErrorDiagnostics.Redact(ex.Message)
        Finally
            If Object.ReferenceEquals(auditCancellation, auditToken) Then
                auditCancellation = Nothing
            End If
            auditToken.Dispose()
            isAuditing = False
            If Not Me.IsDisposed Then
                UpdateSelection()
                If errorMessage IsNot Nothing AndAlso
                   Object.ReferenceEquals(report, imageReport) Then
                    status.Text = "Mounted audit unavailable; see details."
                    details.Text &= Environment.NewLine & Environment.NewLine &
                        "Read-only audit failed: " & errorMessage
                End If
            End If
        End Try
    End Sub

    Private Sub CompareSnapshotsClicked(sender As Object, e As EventArgs)
        Using viewer As New Wim360CompareForm()
            viewer.ShowDialog(Me.FindForm())
        End Using
    End Sub

    Private Sub InventoryClicked(sender As Object, e As EventArgs)
        Dim selected = SelectedImage()
        If selected Is Nothing OrElse selected.ServicingAudit Is Nothing Then Return
        Using popup As New Wim360InventoryForm(selected.ServicingAudit)
            popup.ShowDialog(Me.FindForm())
        End Using
    End Sub

    Private Sub UseClicked(sender As Object, e As EventArgs)
        Dim selected = SelectedImage()
        If selected Is Nothing OrElse report Is Nothing OrElse report.IsSplitSet Then Return
        ' Only fills existing selectors. It does NOT mount or modify the WIM.
        useInMount(report.ImageFile, selected.Index)
    End Sub

    Private Sub ExportIndexMetadata(asJson As Boolean)
        Dim inspected = report
        If inspected Is Nothing OrElse inspected.ImageCount = 0 OrElse
           Not String.Equals(fileBox.Text.Trim(), inspected.ImageFile,
                             StringComparison.OrdinalIgnoreCase) Then Return
        Dim extension = If(asJson, "json", "csv")
        Dim name = Path.GetFileNameWithoutExtension(inspected.ImageFile) &
                   "-indexes." & extension
        Using picker As New SaveFileDialog With {
            .Title = "Export WIM 360 index metadata (no mounting)",
            .Filter = If(asJson,
                         "JSON index metadata (*.json)|*.json",
                         "CSV index metadata (*.csv)|*.csv"),
            .DefaultExt = extension, .AddExtension = True,
            .OverwritePrompt = True, .RestoreDirectory = True,
            .FileName = name
        }
            If picker.ShowDialog(Me.FindForm()) <> DialogResult.OK Then Return
            Try
                Dim content = If(asJson,
                    Wim360MetadataExport.ToJson(inspected),
                    Wim360MetadataExport.ToCsv(inspected))
                File.WriteAllText(picker.FileName, content, New UTF8Encoding(True))
                status.Text = "Exported " & inspected.ImageCount.ToString() &
                    " indexes to " & picker.FileName & " (metadata only; no WIM changes)."
            Catch ex As Exception
                ErrorDiagnostics.RememberFailure("WIM 360 metadata export", ex)
                MessageBox.Show(Me.FindForm(),
                    "Could not export index metadata: " & ex.Message,
                    "WIM 360 - Export", MessageBoxButtons.OK, MessageBoxIcon.Warning)
            End Try
        End Using
    End Sub

    Private Sub CopyClicked(sender As Object, e As EventArgs)
        If report Is Nothing Then Return
        Try
            Clipboard.SetText(Wim360Reader.FormatReport(report))
            status.Text = "WIM 360 report copied. No changes were made."
        Catch ex As Exception
            status.Text = "Could not copy report: " & ex.Message
        End Try
    End Sub
End Class
