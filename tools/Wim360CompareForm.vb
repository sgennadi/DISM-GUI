Imports System
Imports System.Collections.Generic
Imports System.Drawing
Imports System.IO
Imports System.Linq
Imports System.Text
Imports System.Threading.Tasks
Imports System.Windows.Forms

' Offline read-only difference viewer for two saved WIM 360 JSON snapshots.
Public NotInheritable Class Wim360CompareForm
    Inherits Form

    Private ReadOnly baselinePath As TextBox
    Private ReadOnly currentPath As TextBox
    Private ReadOnly compareButton As Button
    Private ReadOnly changeFilter As ComboBox
    Private ReadOnly typeFilter As ComboBox
    Private ReadOnly searchBox As TextBox
    Private ReadOnly changesGrid As DataGridView
    Private ReadOnly summaryLabel As Label
    Private ReadOnly warningLabel As Label
    Private ReadOnly exportCsvButton As Button
    Private ReadOnly exportJsonButton As Button
    Private snapshot As Wim360SnapshotDiff.DiffReport
    Private visibleRows As New List(Of Wim360SnapshotDiff.ChangeRow)()
    Private sortColumn As Integer = 1
    Private sortDescending As Boolean
    Private loading As Boolean

    Public Sub New(Optional baseline As String = "", Optional current As String = "")
        Me.Name = "Wim360CompareForm"
        Me.Text = "DISM GUI " & Reflection.Assembly.GetExecutingAssembly().
            GetName().Version.ToString(3) & " - WIM 360 Snapshot Compare"
        Me.Size = New Size(1130, 740)
        Me.MinimumSize = New Size(840, 560)
        Me.StartPosition = FormStartPosition.CenterParent
        Me.ShowInTaskbar = False
        Me.AutoScaleMode = AutoScaleMode.Font
        Me.Font = ModernTheme.UiFont(9.4F)
        Me.BackColor = Color.White
        Try
            Dim main = Application.OpenForms.Cast(Of Form)().
                FirstOrDefault(Function(f) f.Name = "frmMain")
            If main IsNot Nothing Then Me.Icon = main.Icon
        Catch
        End Try

        Dim root As New TableLayoutPanel With {
            .Name = "Wim360CompareLayout",
            .Dock = DockStyle.Fill, .ColumnCount = 1, .RowCount = 6,
            .Padding = New Padding(17, 12, 17, 14),
            .BackColor = Color.White
        }
        root.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100))
        For Each height As Integer In {65, 49, 49, 56}
            root.RowStyles.Add(New RowStyle(SizeType.Absolute, height))
        Next
        root.RowStyles.Add(New RowStyle(SizeType.Percent, 100))
        root.RowStyles.Add(New RowStyle(SizeType.Absolute, 105))
        Controls.Add(root)

        Dim header As New TableLayoutPanel With {
            .Dock = DockStyle.Fill, .ColumnCount = 1, .RowCount = 2,
            .Margin = Padding.Empty
        }
        header.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100))
        header.RowStyles.Add(New RowStyle(SizeType.Absolute, 31))
        header.RowStyles.Add(New RowStyle(SizeType.Percent, 100))
        header.Controls.Add(New Label With {
            .Name = "lbl360CompareTitle",
            .Text = "WIM 360  |  SNAPSHOT COMPARISON",
            .Dock = DockStyle.Fill, .TextAlign = ContentAlignment.MiddleLeft,
            .ForeColor = ModernTheme.TextInk,
            .Font = ModernTheme.UiFont(13.0F, True),
            .AutoEllipsis = True
        }, 0, 0)
        header.Controls.Add(New Label With {
            .Text = "Compare two saved JSON exports without mounting or changing any image.",
            .Dock = DockStyle.Fill, .TextAlign = ContentAlignment.MiddleLeft,
            .ForeColor = ModernTheme.Muted, .AutoEllipsis = True
        }, 0, 1)
        root.Controls.Add(header, 0, 0)

        baselinePath = AddFileRow(root, 1, "BASELINE", "txt360Baseline", baseline)
        currentPath = AddFileRow(root, 2, "CURRENT", "txt360Current", current)

        Dim filterBar As New TableLayoutPanel With {
            .Name = "Wim360CompareFilters", .Dock = DockStyle.Fill,
            .ColumnCount = 4, .RowCount = 1,
            .Margin = New Padding(0, 3, 0, 8)
        }
        filterBar.RowStyles.Add(New RowStyle(SizeType.Percent, 100))
        ' Percentage widths keep search usable and the Compare action
        ' visible when font sizes increase. No forced horizontal scrolling.
        For Each share As Single In {22.0F, 22.0F, 33.0F, 23.0F}
            filterBar.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, share))
        Next
        root.Controls.Add(filterBar, 0, 3)
        changeFilter = New ComboBox With {
            .Name = "cmb360CompareChange", .Dock = DockStyle.Fill,
            .DropDownStyle = ComboBoxStyle.DropDownList,
            .Margin = New Padding(0, 8, 9, 6)
        }
        changeFilter.Items.AddRange({"All changes", "Added", "Removed", "Changed"})
        filterBar.Controls.Add(changeFilter, 0, 0)
        typeFilter = New ComboBox With {
            .Name = "cmb360CompareType", .Dock = DockStyle.Fill,
            .DropDownStyle = ComboBoxStyle.DropDownList,
            .Margin = New Padding(0, 8, 9, 6)
        }
        typeFilter.Items.AddRange({"All types", "Drivers", "Packages", "Features", "Capabilities"})
        filterBar.Controls.Add(typeFilter, 1, 0)
        searchBox = New TextBox With {
            .Name = "txt360CompareSearch", .Dock = DockStyle.Fill,
            .PlaceholderText = "Search differences...",
            .Margin = New Padding(0, 9, 9, 7)
        }
        filterBar.Controls.Add(searchBox, 2, 0)
        compareButton = New Button With {
            .Name = "btn360CompareRun", .Text = "Compare JSON",
            .Dock = DockStyle.Fill, .Margin = New Padding(0, 3, 0, 4)
        }
        ModernTheme.StyleButton(compareButton, "primary")
        AddHandler compareButton.Click, AddressOf CompareClicked
        filterBar.Controls.Add(compareButton, 3, 0)

        changesGrid = New DataGridView With {
            .Name = "grid360Changes", .Dock = DockStyle.Fill,
            .Margin = New Padding(0, 2, 0, 9),
            .ReadOnly = True, .VirtualMode = True,
            .RowHeadersVisible = False, .MultiSelect = True,
            .AllowUserToAddRows = False, .AllowUserToDeleteRows = False,
            .AllowUserToResizeRows = False,
            .SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            .AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None,
            .BackgroundColor = Color.White, .GridColor = Color.FromArgb(222, 232, 244),
            .BorderStyle = BorderStyle.FixedSingle,
            .ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing,
            .ColumnHeadersHeight = 35, .EnableHeadersVisualStyles = False
        }
        changesGrid.RowTemplate.Height = 27
        changesGrid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(233, 242, 253)
        changesGrid.ColumnHeadersDefaultCellStyle.ForeColor = ModernTheme.TextInk
        changesGrid.ColumnHeadersDefaultCellStyle.Font = ModernTheme.UiFont(9.3F, True)
        changesGrid.DefaultCellStyle.Font = ModernTheme.UiFont(9.2F)
        changesGrid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(218, 235, 252)
        changesGrid.DefaultCellStyle.SelectionForeColor = ModernTheme.TextInk
        Dim widths As Integer() = {105, 115, 305, 115, 115, 115, 115, 190, 190, 245}
        For i As Integer = 0 To Wim360SnapshotDiff.ColumnHeaders.Length - 1
            Dim k = changesGrid.Columns.Add("col360Diff" & i.ToString(),
                                             Wim360SnapshotDiff.ColumnHeaders(i))
            changesGrid.Columns(k).Width = widths(i)
            changesGrid.Columns(k).SortMode = DataGridViewColumnSortMode.Programmatic
        Next
        AddHandler changesGrid.CellValueNeeded, AddressOf GridValueNeeded
        AddHandler changesGrid.CellToolTipTextNeeded, AddressOf GridTooltipNeeded
        AddHandler changesGrid.ColumnHeaderMouseClick, AddressOf SortClicked
        root.Controls.Add(changesGrid, 0, 4)

        Dim footer As New TableLayoutPanel With {
            .Dock = DockStyle.Fill, .ColumnCount = 1, .RowCount = 3,
            .Margin = Padding.Empty
        }
        footer.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100))
        footer.RowStyles.Add(New RowStyle(SizeType.Absolute, 27))
        footer.RowStyles.Add(New RowStyle(SizeType.Absolute, 32))
        footer.RowStyles.Add(New RowStyle(SizeType.Percent, 100))
        root.Controls.Add(footer, 0, 5)
        summaryLabel = New Label With {
            .Name = "lbl360CompareSummary", .Dock = DockStyle.Fill,
            .Text = "Choose baseline and current snapshot JSON files, then Compare.",
            .TextAlign = ContentAlignment.MiddleLeft,
            .ForeColor = ModernTheme.TextInk, .AutoEllipsis = True
        }
        footer.Controls.Add(summaryLabel, 0, 0)
        warningLabel = New Label With {
            .Name = "lbl360CompareWarnings", .Dock = DockStyle.Fill,
            .Text = "Schema 1 or filtered exports: Added / Removed is suppressed for safety.",
            .TextAlign = ContentAlignment.MiddleLeft,
            .ForeColor = ModernTheme.Muted, .AutoEllipsis = True
        }
        Dim tip As New ToolTip()
        tip.SetToolTip(warningLabel, warningLabel.Text)
        footer.Controls.Add(warningLabel, 0, 1)

        Dim buttons As New TableLayoutPanel With {
            .Dock = DockStyle.Fill, .ColumnCount = 4, .RowCount = 1,
            .Margin = Padding.Empty
        }
        buttons.RowStyles.Add(New RowStyle(SizeType.Percent, 100))
        buttons.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100))
        buttons.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute, 145))
        buttons.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute, 145))
        buttons.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute, 114))
        footer.Controls.Add(buttons, 0, 2)
        exportCsvButton = New Button With {
            .Name = "btn360CompareCsv", .Text = "Export CSV",
            .Enabled = False, .Dock = DockStyle.Fill,
            .Margin = New Padding(4, 3, 4, 1)
        }
        ModernTheme.StyleButton(exportCsvButton, "browse")
        AddHandler exportCsvButton.Click, Sub(sender, e) ExportResult(False)
        buttons.Controls.Add(exportCsvButton, 1, 0)
        exportJsonButton = New Button With {
            .Name = "btn360CompareJson", .Text = "Export JSON",
            .Enabled = False, .Dock = DockStyle.Fill,
            .Margin = New Padding(4, 3, 4, 1)
        }
        ModernTheme.StyleButton(exportJsonButton, "browse")
        AddHandler exportJsonButton.Click, Sub(sender, e) ExportResult(True)
        buttons.Controls.Add(exportJsonButton, 2, 0)
        Dim closeButton As New Button With {
            .Name = "btn360CompareClose", .Text = "Close",
            .Dock = DockStyle.Fill, .Margin = New Padding(4, 3, 0, 1)
        }
        ModernTheme.StyleButton(closeButton, "secondary")
        AddHandler closeButton.Click, Sub(sender, e) Me.Close()
        buttons.Controls.Add(closeButton, 3, 0)
        Me.CancelButton = closeButton

        AddHandler changeFilter.SelectedIndexChanged, Sub(sender, e) RefreshRows()
        AddHandler typeFilter.SelectedIndexChanged, Sub(sender, e) RefreshRows()
        AddHandler searchBox.TextChanged, Sub(sender, e) RefreshRows()
        changeFilter.SelectedIndex = 0
        typeFilter.SelectedIndex = 0
        AddHandler baselinePath.TextChanged, AddressOf SourcesChanged
        AddHandler currentPath.TextChanged, AddressOf SourcesChanged
        ' A narrow viewport must still show all filters and compare controls;
        ' only the data grid may scroll horizontally for detailed rows.
        Dim fontScale As Single = Math.Max(1.0F, Me.Font.Height / 17.0F)
        ModernTheme.MakeDialogScrollable(Me, root,
            CInt(Math.Ceiling(780.0F / fontScale)), 680)
        WindowGeometry.Install(Me, "Software\DISMGUI\Wim360Compare")
    End Sub

    Private Function AddFileRow(parent As TableLayoutPanel, position As Integer,
                                heading As String, fieldName As String,
                                initialValue As String) As TextBox
        Dim panel As New TableLayoutPanel With {
            .Dock = DockStyle.Fill, .RowCount = 1, .ColumnCount = 3,
            .Margin = New Padding(0, 1, 0, 5)
        }
        panel.RowStyles.Add(New RowStyle(SizeType.Percent, 100))
        panel.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute, 103))
        panel.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100))
        panel.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute, 125))
        panel.Controls.Add(New Label With {
            .Text = heading, .Dock = DockStyle.Fill,
            .Font = ModernTheme.UiFont(9.4F, True),
            .ForeColor = ModernTheme.TextInk,
            .TextAlign = ContentAlignment.MiddleLeft
        }, 0, 0)
        Dim field As New TextBox With {
            .Name = fieldName, .Dock = DockStyle.Fill,
            .Text = If(initialValue, ""), .Margin = New Padding(0, 8, 9, 5),
            .PlaceholderText = "Select WIM 360 inventory export (.json)"
        }
        panel.Controls.Add(field, 1, 0)
        Dim picker As New Button With {
            .Name = "btn" & fieldName.Substring(3) & "Browse",
            .Text = "Browse...", .Dock = DockStyle.Fill,
            .Margin = New Padding(0, 3, 0, 4)
        }
        ModernTheme.StyleButton(picker, "browse")
        AddHandler picker.Click, Sub(sender, e)
            Using dlg As New OpenFileDialog With {
                .Title = "Choose a WIM 360 JSON snapshot",
                .Filter = "WIM 360 JSON (*.json)|*.json|All files (*.*)|*.*",
                .CheckFileExists = True, .Multiselect = False,
                .RestoreDirectory = True
            }
                If File.Exists(field.Text.Trim()) Then dlg.FileName = field.Text.Trim()
                If dlg.ShowDialog(Me) = DialogResult.OK Then field.Text = dlg.FileName
            End Using
        End Sub
        panel.Controls.Add(picker, 2, 0)
        parent.Controls.Add(panel, 0, position)
        Return field
    End Function

    Private Sub SourcesChanged(sender As Object, e As EventArgs)
        snapshot = Nothing
        visibleRows.Clear()
        If changesGrid IsNot Nothing Then changesGrid.RowCount = 0
        If exportCsvButton IsNot Nothing Then exportCsvButton.Enabled = False
        If exportJsonButton IsNot Nothing Then exportJsonButton.Enabled = False
        If summaryLabel IsNot Nothing Then
            summaryLabel.Text = "Inputs changed. Run Compare JSON to refresh differences."
        End If
    End Sub

    Public Sub PreviewComparisonForTest(value As Wim360SnapshotDiff.DiffReport)
        DisplayComparison(value)
    End Sub

    Private Async Sub CompareClicked(sender As Object, e As EventArgs)
        If loading Then Return
        Dim baselineFile = baselinePath.Text.Trim()
        Dim currentFile = currentPath.Text.Trim()
        If String.IsNullOrWhiteSpace(baselineFile) OrElse
           String.IsNullOrWhiteSpace(currentFile) Then
            summaryLabel.Text = "Choose both baseline and current JSON files."
            Return
        End If
        loading = True
        compareButton.Enabled = False
        exportCsvButton.Enabled = False
        exportJsonButton.Enabled = False
        summaryLabel.Text = "Comparing offline snapshots..."
        Try
            Dim report = Await Task.Run(Function()
                Dim baseline = Wim360SnapshotDiff.ReadSnapshot(baselineFile)
                Dim current = Wim360SnapshotDiff.ReadSnapshot(currentFile)
                Return Wim360SnapshotDiff.Compare(baseline, current)
            End Function)
            If Me.IsDisposed Then Return
            If Not String.Equals(baselineFile, baselinePath.Text.Trim(),
                                 StringComparison.OrdinalIgnoreCase) OrElse
               Not String.Equals(currentFile, currentPath.Text.Trim(),
                                 StringComparison.OrdinalIgnoreCase) Then Return
            DisplayComparison(report)
        Catch ex As Exception
            If Not Me.IsDisposed Then
                snapshot = Nothing
                changesGrid.RowCount = 0
                summaryLabel.Text = "Cannot compare snapshots: " & ex.Message
                warningLabel.Text = "No image files were changed."
            End If
        Finally
            loading = False
            If Not Me.IsDisposed Then compareButton.Enabled = True
        End Try
    End Sub

    Private Sub DisplayComparison(value As Wim360SnapshotDiff.DiffReport)
        snapshot = value
        exportCsvButton.Enabled = value IsNot Nothing
        exportJsonButton.Enabled = value IsNot Nothing
        RefreshRows()
    End Sub

    Private Sub RefreshRows()
        If changesGrid Is Nothing OrElse changeFilter.SelectedIndex < 0 OrElse
           typeFilter.SelectedIndex < 0 Then Return
        Dim filtered As New List(Of Wim360SnapshotDiff.ChangeRow)()
        If snapshot IsNot Nothing Then
            filtered = Wim360SnapshotDiff.FilterChanges(snapshot.Changes,
                Convert.ToString(changeFilter.SelectedItem),
                Convert.ToString(typeFilter.SelectedItem), searchBox.Text)
        End If
        Dim ordered = filtered.OrderBy(Function(x) x.CellAt(sortColumn),
                                       StringComparer.OrdinalIgnoreCase).ToList()
        If sortDescending Then ordered.Reverse()
        changesGrid.RowCount = 0
        visibleRows = ordered
        changesGrid.RowCount = visibleRows.Count
        changesGrid.Invalidate()
        For Each column As DataGridViewColumn In changesGrid.Columns
            column.HeaderCell.SortGlyphDirection = SortOrder.None
        Next
        changesGrid.Columns(sortColumn).HeaderCell.SortGlyphDirection =
            If(sortDescending, SortOrder.Descending, SortOrder.Ascending)
        If snapshot IsNot Nothing Then
            summaryLabel.Text = snapshot.Summary &
                "  |  Showing " & visibleRows.Count.ToString() &
                " of " & snapshot.Changes.Count.ToString()
            warningLabel.Text = If(snapshot.Warnings.Count = 0,
                "Full inventories: membership comparison enabled.",
                String.Join("  |  ", snapshot.Warnings))
        End If
    End Sub

    Private Sub SortClicked(sender As Object, e As DataGridViewCellMouseEventArgs)
        If e.ColumnIndex < 0 Then Return
        If sortColumn = e.ColumnIndex Then
            sortDescending = Not sortDescending
        Else
            sortColumn = e.ColumnIndex
            sortDescending = False
        End If
        RefreshRows()
    End Sub

    Private Sub GridValueNeeded(sender As Object, e As DataGridViewCellValueEventArgs)
        If e.RowIndex >= 0 AndAlso e.RowIndex < visibleRows.Count AndAlso
           e.ColumnIndex >= 0 AndAlso e.ColumnIndex < Wim360SnapshotDiff.ColumnHeaders.Length Then
            e.Value = visibleRows(e.RowIndex).CellAt(e.ColumnIndex)
        End If
    End Sub

    Private Sub GridTooltipNeeded(sender As Object,
                                  e As DataGridViewCellToolTipTextNeededEventArgs)
        If e.RowIndex >= 0 AndAlso e.RowIndex < visibleRows.Count AndAlso
           e.ColumnIndex >= 0 AndAlso e.ColumnIndex < Wim360SnapshotDiff.ColumnHeaders.Length Then
            e.ToolTipText = visibleRows(e.RowIndex).CellAt(e.ColumnIndex)
        End If
    End Sub

    Private Sub ExportResult(asJson As Boolean)
        If snapshot Is Nothing Then Return
        Dim ext = If(asJson, "json", "csv")
        Using dialog As New SaveFileDialog With {
            .Title = "Export displayed WIM 360 changes",
            .Filter = If(asJson, "JSON document (*.json)|*.json", "CSV table (*.csv)|*.csv"),
            .DefaultExt = ext, .AddExtension = True,
            .OverwritePrompt = True, .RestoreDirectory = True,
            .FileName = "WIM360-Snapshot-Diff." & ext
        }
            If dialog.ShowDialog(Me) <> DialogResult.OK Then Return
            Try
                Dim content = If(asJson,
                    Wim360SnapshotDiff.ToJson(snapshot, visibleRows),
                    Wim360SnapshotDiff.ToCsv(snapshot, visibleRows))
                File.WriteAllText(dialog.FileName, content, New UTF8Encoding(True))
                summaryLabel.Text = "Exported " & visibleRows.Count.ToString() &
                    " displayed differences to " & dialog.FileName
            Catch ex As Exception
                MessageBox.Show(Me, "Cannot export comparison: " & ex.Message,
                                "WIM 360 comparison", MessageBoxButtons.OK,
                                MessageBoxIcon.Warning)
            End Try
        End Using
    End Sub
End Class
