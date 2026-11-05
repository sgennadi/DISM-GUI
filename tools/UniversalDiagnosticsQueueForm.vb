Imports System
Imports System.Collections.Generic
Imports System.Drawing
Imports System.IO
Imports System.Linq
Imports System.Text
Imports System.Text.Json
Imports System.Windows.Forms

' Read-only review and explicit local management of the universal-diagnostics/v1
' queue embedded in DISM GUI. Never contacts GitHub or collects raw DISM logs.
Public NotInheritable Class UniversalDiagnosticsQueueForm
    Inherits Form

    Private NotInheritable Class SavedEntry
        Public Property Id As String = ""
        Public Property WhenUtc As String = ""
        Public Property Component As String = ""
        Public Property ErrorType As String = ""
        Public Property Code As String = ""
        Public Property Version As String = ""
        Public Property Level As String = ""
        Public Property Summary As String = ""
        Public Property Fingerprint As String = ""
        Public Property DeliveryStatus As String = ""
        Public Property FilePath As String = ""
    End Class

    Private Const Project As String = "sgennadi/DISM-GUI"
    Private Const MaxDisplayRecords As Integer = 1500
    Private ReadOnly records As New List(Of SavedEntry)()
    Private ReadOnly searchBox As TextBox
    Private ReadOnly statusBox As ComboBox
    Private ReadOnly grid As DataGridView
    Private ReadOnly preview As TextBox
    Private ReadOnly footnote As Label
    Private ReadOnly exportButton As Button
    Private ReadOnly copyButton As Button
    Private ReadOnly deleteButton As Button

    Public Sub New()
        Me.Name = "UniversalDiagnosticsQueueForm"
        Me.Text = "DISM GUI - Saved Diagnostics"
        Me.MinimumSize = New Size(860, 620)
        Me.Size = New Size(1120, 800)
        Me.Font = ModernTheme.UiFont(9.4F)
        Me.AutoScaleMode = AutoScaleMode.Font
        Me.StartPosition = FormStartPosition.CenterParent
        Me.ShowInTaskbar = False
        Me.BackColor = Color.White
        Try
            Me.Icon = ModernTheme.MakeAppIcon()
        Catch
        End Try

        Dim root As New TableLayoutPanel With {
            .Name = "UniversalQueueLayout",
            .Dock = DockStyle.Fill, .ColumnCount = 1, .RowCount = 5,
            .Padding = New Padding(16, 12, 16, 14),
            .BackColor = Color.White
        }
        root.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100))
        root.RowStyles.Add(New RowStyle(SizeType.Absolute, 55))
        root.RowStyles.Add(New RowStyle(SizeType.Absolute, 53))
        root.RowStyles.Add(New RowStyle(SizeType.Percent, 53))
        root.RowStyles.Add(New RowStyle(SizeType.Percent, 47))
        root.RowStyles.Add(New RowStyle(SizeType.Absolute, 112))
        Me.Controls.Add(root)

        Dim title As New Label With {
            .Text = "SAVED DIAGNOSTICS  |  LOCAL QUEUE",
            .Dock = DockStyle.Fill,
            .Font = ModernTheme.UiFont(13, True),
            .TextAlign = ContentAlignment.MiddleLeft,
            .ForeColor = ModernTheme.TextInk
        }
        root.Controls.Add(title, 0, 0)

        Dim filters As New TableLayoutPanel With {
            .Dock = DockStyle.Fill, .ColumnCount = 3, .RowCount = 1
        }
        filters.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100))
        filters.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute, 168))
        filters.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute, 135))
        filters.RowStyles.Add(New RowStyle(SizeType.Percent, 100))
        root.Controls.Add(filters, 0, 1)

        searchBox = New TextBox With {
            .Name = "txtUniversalQueueSearch",
            .Dock = DockStyle.Fill, .Margin = New Padding(0, 5, 10, 9),
            .PlaceholderText = "Search title, error type, fingerprint, ID..."
        }
        AddHandler searchBox.TextChanged, Sub(sender, e) ApplyFilters()
        filters.Controls.Add(searchBox, 0, 0)

        statusBox = New ComboBox With {
            .Name = "cmbUniversalQueueStatus",
            .Dock = DockStyle.Fill, .DropDownStyle = ComboBoxStyle.DropDownList,
            .Margin = New Padding(0, 5, 8, 9)
        }
        statusBox.Items.AddRange(New Object() {"All statuses", "Pending", "Submitted"})
        statusBox.SelectedIndex = 0
        AddHandler statusBox.SelectedIndexChanged, Sub(sender, e) ApplyFilters()
        filters.Controls.Add(statusBox, 1, 0)

        Dim refresh As New Button With {
            .Name = "btnUniversalQueueRefresh", .Text = "Refresh",
            .Dock = DockStyle.Fill, .Margin = New Padding(0, 3, 0, 8)
        }
        ModernTheme.StyleButton(refresh, "browse")
        AddHandler refresh.Click, Sub(sender, e) RefreshRecords()
        filters.Controls.Add(refresh, 2, 0)

        grid = New DataGridView With {
            .Name = "gridUniversalQueue",
            .Dock = DockStyle.Fill, .ReadOnly = True,
            .AllowUserToAddRows = False, .AllowUserToDeleteRows = False,
            .AllowUserToResizeRows = False,
            .SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            .MultiSelect = False, .RowHeadersVisible = False,
            .AutoGenerateColumns = False,
            .AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            .Margin = New Padding(0, 4, 0, 5),
            .BackgroundColor = Color.White
        }
        AddColumn("Date (UTC)", 130)
        AddColumn("Area", 150)
        AddColumn("Code", 75)
        AddColumn("Summary", 325)
        AddColumn("Status", 85)
        AddHandler grid.SelectionChanged, Sub(sender, e) ShowSelected()
        root.Controls.Add(grid, 0, 2)

        preview = New TextBox With {
            .Name = "txtUniversalQueuePreview",
            .Dock = DockStyle.Fill, .Multiline = True, .ReadOnly = True,
            .ScrollBars = ScrollBars.Vertical, .WordWrap = True,
            .Font = ModernTheme.UiFont(9.4F),
            .Margin = New Padding(0, 2, 0, 6)
        }
        root.Controls.Add(preview, 0, 3)

        Dim footer As New TableLayoutPanel With {
            .Dock = DockStyle.Fill, .ColumnCount = 1, .RowCount = 3
        }
        footer.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100))
        footer.RowStyles.Add(New RowStyle(SizeType.Absolute, 30))
        footer.RowStyles.Add(New RowStyle(SizeType.Percent, 50))
        footer.RowStyles.Add(New RowStyle(SizeType.Percent, 50))
        root.Controls.Add(footer, 0, 4)

        footnote = New Label With {
            .Name = "lblUniversalQueueStatus",
            .Text = "These reports are stored on this computer. Nothing is uploaded.",
            .Dock = DockStyle.Fill, .AutoEllipsis = True,
            .ForeColor = ModernTheme.Muted,
            .TextAlign = ContentAlignment.MiddleLeft
        }
        footer.Controls.Add(footnote, 0, 0)

        Dim buttons As New TableLayoutPanel With {
            .Dock = DockStyle.Fill, .ColumnCount = 3, .RowCount = 2
        }
        For i As Integer = 1 To 3
            buttons.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100.0F / 3.0F))
        Next
        buttons.RowStyles.Add(New RowStyle(SizeType.Percent, 50))
        buttons.RowStyles.Add(New RowStyle(SizeType.Percent, 50))
        footer.SetRowSpan(buttons, 2)
        footer.Controls.Add(buttons, 0, 1)

        copyButton = New Button With {
            .Name = "btnUniversalQueueCopy", .Text = "Copy reviewed details",
            .Dock = DockStyle.Fill, .Enabled = False,
            .Margin = New Padding(3, 3, 7, 2)
        }
        ModernTheme.StyleButton(copyButton, "browse")
        AddHandler copyButton.Click, Sub(sender, e)
            Try
                If preview.TextLength = 0 Then Return
                Clipboard.SetText(preview.Text)
                footnote.Text = "Reviewed details copied. No upload."
            Catch ex As Exception
                footnote.Text = "Copy failed (" & ex.GetType().Name & ")."
            End Try
        End Sub
        buttons.Controls.Add(copyButton, 0, 0)

        exportButton = New Button With {
            .Name = "btnUniversalQueueExport", .Text = "Export reviewed JSON...",
            .Dock = DockStyle.Fill, .Enabled = False,
            .Margin = New Padding(3, 3, 7, 2)
        }
        ModernTheme.StyleButton(exportButton, "browse")
        AddHandler exportButton.Click, AddressOf ExportSelected
        buttons.Controls.Add(exportButton, 1, 0)

        deleteButton = New Button With {
            .Name = "btnUniversalQueueDelete", .Text = "Delete selected...",
            .Dock = DockStyle.Fill, .Enabled = False,
            .Margin = New Padding(3, 3, 1, 2)
        }
        ModernTheme.StyleButton(deleteButton, "neutral")
        AddHandler deleteButton.Click, AddressOf DeleteSelected
        buttons.Controls.Add(deleteButton, 2, 0)

        Dim openFolder As New Button With {
            .Name = "btnUniversalQueueFolder", .Text = "Open queue folder",
            .Dock = DockStyle.Fill, .Margin = New Padding(3, 2, 7, 2)
        }
        ModernTheme.StyleButton(openFolder, "browse")
        AddHandler openFolder.Click, Sub(sender, e)
            Try
                Dim folder = UniversalDiagnosticsEmbedded.QueueDirectory(Project)
                Directory.CreateDirectory(folder)
                ProcessStart(folder)
            Catch ex As Exception
                footnote.Text = "Cannot open queue folder (" & ex.GetType().Name & ")."
            End Try
        End Sub
        buttons.Controls.Add(openFolder, 0, 1)

        Dim info As New Label With {
            .Text = "Preview and export only. No GitHub submission.",
            .Dock = DockStyle.Fill, .TextAlign = ContentAlignment.MiddleCenter,
            .ForeColor = ModernTheme.Muted, .AutoEllipsis = True,
            .Margin = New Padding(0, 0, 0, 0)
        }
        buttons.Controls.Add(info, 1, 1)

        Dim close As New Button With {
            .Name = "btnUniversalQueueClose", .Text = "Close",
            .Dock = DockStyle.Fill, .Margin = New Padding(3, 2, 1, 2)
        }
        ModernTheme.StyleButton(close, "neutral")
        AddHandler close.Click, Sub(sender, e) Me.Close()
        Me.CancelButton = close
        buttons.Controls.Add(close, 2, 1)

        WindowGeometry.Install(Me, "Software\DISMGUI\UniversalQueue")
        AddHandler Me.Shown, Sub(sender, e) RefreshRecords()
    End Sub

    Private Shared Sub ProcessStart(folder As String)
        System.Diagnostics.Process.Start(
            New System.Diagnostics.ProcessStartInfo(folder) With {
                .UseShellExecute = True
            })
    End Sub

    Private Sub AddColumn(name As String, weight As Single)
        Dim column As New DataGridViewTextBoxColumn With {
            .HeaderText = name, .FillWeight = weight,
            .MinimumWidth = If(name = "Summary", 190, 78),
            .SortMode = DataGridViewColumnSortMode.NotSortable
        }
        grid.Columns.Add(column)
    End Sub

    Private Shared Function Field(obj As JsonElement, propertyName As String,
                                  maxLength As Integer) As String
        Dim item As JsonElement
        If Not obj.TryGetProperty(propertyName, item) OrElse
           item.ValueKind <> JsonValueKind.String Then Return ""
        Dim value = If(item.GetString(), "")
        If value.Length > maxLength * 3 Then value = value.Substring(0, maxLength * 3)
        Dim cleaned = ErrorDiagnostics.Redact(value).Replace(vbCr, " ").
            Replace(vbLf, " ").Replace(vbTab, " ").Trim()
        Return cleaned.Substring(0, Math.Min(cleaned.Length, maxLength))
    End Function

    Private Shared Function ReadEntry(path As String) As SavedEntry
        Try
            Dim file As New FileInfo(path)
            If Not file.Exists OrElse file.Length > 65536 OrElse
               file.Length < 40 Then Return Nothing
            Dim fileId As Guid
            If Not Guid.TryParse(System.IO.Path.GetFileNameWithoutExtension(path),
                                 fileId) Then Return Nothing
            Using document = JsonDocument.Parse(File.ReadAllText(path, Encoding.UTF8))
                Dim root = document.RootElement
                Dim eventJson As JsonElement
                Dim delivery As JsonElement
                If Not root.TryGetProperty("event", eventJson) OrElse
                   eventJson.ValueKind <> JsonValueKind.Object OrElse
                   Not root.TryGetProperty("delivery", delivery) OrElse
                   delivery.ValueKind <> JsonValueKind.Object Then Return Nothing
                Dim declared As Guid
                Dim rawId As JsonElement
                If Not eventJson.TryGetProperty("id", rawId) OrElse
                   rawId.ValueKind <> JsonValueKind.String OrElse
                   Not Guid.TryParse(rawId.GetString(), declared) OrElse
                   declared <> fileId Then Return Nothing
                If Field(eventJson, "schema", 60) <> UniversalDiagnosticsEmbedded.Schema OrElse
                   Field(eventJson, "project", 100) <> Project Then Return Nothing
                Dim state = Field(delivery, "status", 20).ToLowerInvariant()
                If state <> "pending" AndAlso state <> "submitted" Then Return Nothing
                Return New SavedEntry With {
                    .Id = fileId.ToString("D"),
                    .WhenUtc = Field(eventJson, "timestamp_utc", 32),
                    .Component = Field(eventJson, "component", 90),
                    .ErrorType = Field(eventJson, "error_type", 90),
                    .Code = Field(eventJson, "code", 60),
                    .Version = Field(eventJson, "version", 70),
                    .Level = Field(eventJson, "level", 12),
                    .Summary = Field(eventJson, "summary", 450),
                    .Fingerprint = Field(eventJson, "fingerprint", 64),
                    .DeliveryStatus = state,
                    .FilePath = path
                }
            End Using
        Catch ex As Exception When TypeOf ex Is IOException OrElse
                                   TypeOf ex Is UnauthorizedAccessException OrElse
                                   TypeOf ex Is JsonException OrElse
                                   TypeOf ex Is ArgumentException
            Return Nothing
        End Try
    End Function

    Private Shared Function SanitizeExport(item As SavedEntry) As String
        Dim eventValue As New Dictionary(Of String, Object) From {
            {"schema", UniversalDiagnosticsEmbedded.Schema},
            {"id", item.Id},
            {"timestamp_utc", item.WhenUtc},
            {"project", Project},
            {"component", item.Component},
            {"error_type", item.ErrorType},
            {"code", item.Code},
            {"level", item.Level},
            {"version", item.Version},
            {"summary", item.Summary},
            {"fingerprint", item.Fingerprint}
        }
        Dim record As New Dictionary(Of String, Object) From {
            {"event", eventValue},
            {"delivery", New Dictionary(Of String, String) From {
                {"status", item.DeliveryStatus}, {"url", ""}
            }}
        }
        Return JsonSerializer.Serialize(record,
            New JsonSerializerOptions With {.WriteIndented = True})
    End Function

    Private Function Selected() As SavedEntry
        If grid.SelectedRows.Count = 0 Then Return Nothing
        Return TryCast(grid.SelectedRows(0).Tag, SavedEntry)
    End Function

    Private Sub ShowSelected()
        Dim item = Selected()
        preview.Text = If(item Is Nothing, "", SanitizeExport(item))
        Dim available = item IsNot Nothing
        copyButton.Enabled = available
        exportButton.Enabled = available
        deleteButton.Enabled = available
    End Sub

    Public Sub RefreshRecords()
        Dim preserved = If(Selected() Is Nothing, "", Selected().Id)
        records.Clear()
        Try
            Dim folder = UniversalDiagnosticsEmbedded.QueueDirectory(Project)
            If Directory.Exists(folder) Then
                Dim files = New DirectoryInfo(folder).EnumerateFiles("*.json").
                    OrderByDescending(Function(x) x.LastWriteTimeUtc).
                    Take(MaxDisplayRecords)
                For Each file In files
                    Dim item = ReadEntry(file.FullName)
                    If item IsNot Nothing Then records.Add(item)
                Next
            End If
            ApplyFilters(preserved)
        Catch ex As Exception When TypeOf ex Is IOException OrElse
                                   TypeOf ex Is UnauthorizedAccessException
            footnote.Text = "Cannot read local queue (" & ex.GetType().Name & ")."
            grid.Rows.Clear()
            ShowSelected()
        End Try
    End Sub

    Private Sub ApplyFilters(Optional preserved As String = "")
        Dim query = searchBox.Text.Trim()
        Dim chosen = statusBox.SelectedItem.ToString()
        grid.SuspendLayout()
        Try
            grid.Rows.Clear()
            Dim candidates = records.Where(Function(x)
                If chosen <> "All statuses" AndAlso
                   Not x.DeliveryStatus.Equals(chosen, StringComparison.OrdinalIgnoreCase) Then
                    Return False
                End If
                If query.Length = 0 Then Return True
                Dim searchable As String = String.Join(" ", New String() {
                    x.Id, x.Component, x.Code, x.ErrorType, x.Summary, x.Fingerprint
                })
                Return searchable.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0
            End Function)
            For Each entry In candidates
                Dim position = grid.Rows.Add(entry.WhenUtc, entry.Component,
                                             entry.Code, entry.Summary,
                                             entry.DeliveryStatus)
                grid.Rows(position).Tag = entry
                If entry.Id = preserved Then
                    grid.Rows(position).Selected = True
                End If
            Next
            footnote.Text = grid.Rows.Count.ToString() & " shown; " &
                records.Count.ToString() & " local entries loaded. No upload."
        Finally
            grid.ResumeLayout(True)
        End Try
        ShowSelected()
    End Sub

    Private Sub ExportSelected(sender As Object, e As EventArgs)
        Dim item = Selected()
        If item Is Nothing Then Return
        Using dialog As New SaveFileDialog With {
            .Title = "Export reviewed local diagnostic record",
            .Filter = "JSON report (*.json)|*.json",
            .FileName = "DISMGUI-diagnostic-" & item.Id & ".json",
            .AddExtension = True, .DefaultExt = "json"
        }
            If dialog.ShowDialog(Me) <> DialogResult.OK Then Return
            Try
                File.WriteAllText(dialog.FileName, SanitizeExport(item),
                                  New UTF8Encoding(False))
                footnote.Text = "Sanitized JSON exported locally. Nothing was uploaded."
            Catch ex As Exception
                footnote.Text = "Export failed (" & ex.GetType().Name & ")."
            End Try
        End Using
    End Sub

    Private Sub DeleteSelected(sender As Object, e As EventArgs)
        Dim item = Selected()
        If item Is Nothing Then Return
        If MessageBox.Show(Me,
            "Permanently delete local report " & item.Id & "?" & Environment.NewLine &
            "Only this saved diagnostic will be deleted. No GitHub issue is affected.",
            "Delete selected diagnostic", MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning) <> DialogResult.Yes Then Return
        Try
            Dim expected = System.IO.Path.Combine(
                UniversalDiagnosticsEmbedded.QueueDirectory(Project), item.Id & ".json")
            If Not String.Equals(System.IO.Path.GetFullPath(expected),
                                 System.IO.Path.GetFullPath(item.FilePath),
                                 StringComparison.OrdinalIgnoreCase) Then
                Throw New InvalidOperationException("Unexpected record location.")
            End If
            File.Delete(expected)
            RefreshRecords()
            footnote.Text = "Selected diagnostic deleted locally."
        Catch ex As Exception
            footnote.Text = "Delete failed (" & ex.GetType().Name & ")."
        End Try
    End Sub
End Class
