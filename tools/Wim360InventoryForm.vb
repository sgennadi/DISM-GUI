Imports System
Imports System.Collections.Generic
Imports System.Drawing
Imports System.IO
Imports System.Linq
Imports System.Text
Imports System.Windows.Forms

' Read-only, scrollable details window for a completed mounted-image audit.
' This window never executes DISM; it only filters and exports an in-memory snapshot.
Public NotInheritable Class Wim360InventoryForm
    Inherits Form

    Private ReadOnly audit As Wim360Servicing.AuditResult
    Private ReadOnly allRows As List(Of Wim360Catalog.CatalogRow)
    Private displayedRows As New List(Of Wim360Catalog.CatalogRow)()
    Private ReadOnly typeFilter As ComboBox
    Private ReadOnly stateFilter As ComboBox
    Private ReadOnly searchBox As TextBox
    Private ReadOnly inventoryGrid As DataGridView
    Private ReadOnly resultLabel As Label
    Private ReadOnly warningLabel As Label
    Private ReadOnly exportCsvButton As Button
    Private ReadOnly exportJsonButton As Button
    Private sortColumn As Integer = 1
    Private sortDescending As Boolean

    Public Sub New(value As Wim360Servicing.AuditResult)
        If value Is Nothing Then Throw New ArgumentNullException(NameOf(value))
        audit = value
        allRows = Wim360Catalog.BuildRows(value)

        Me.Name = "Wim360InventoryForm"
        Me.Text = "DISM GUI " & Reflection.Assembly.GetExecutingAssembly().
            GetName().Version.ToString(3) & " - WIM 360 Inventory"
        Me.StartPosition = FormStartPosition.CenterParent
        Me.MinimumSize = New Size(820, 530)
        Me.Size = New Size(1110, 740)
        Me.Font = ModernTheme.UiFont(9.4F)
        Me.AutoScaleMode = AutoScaleMode.Font
        Me.BackColor = Color.White
        Me.ShowInTaskbar = False
        Try
            Dim main = Application.OpenForms.Cast(Of Form)().
                FirstOrDefault(Function(f) f.Name = "frmMain")
            If main IsNot Nothing Then Me.Icon = main.Icon
        Catch
            ' Icon is decorative; inventory must remain usable without it.
        End Try

        Dim root As New TableLayoutPanel With {
            .Name = "Wim360InventoryLayout", .Dock = DockStyle.Fill,
            .ColumnCount = 1, .RowCount = 4,
            .BackColor = Color.White, .Padding = New Padding(17, 12, 17, 14)
        }
        root.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100))
        root.RowStyles.Add(New RowStyle(SizeType.Absolute, 73))
        root.RowStyles.Add(New RowStyle(SizeType.Absolute, 56))
        root.RowStyles.Add(New RowStyle(SizeType.Percent, 100))
        root.RowStyles.Add(New RowStyle(SizeType.Absolute, 102))
        Controls.Add(root)

        Dim header As New TableLayoutPanel With {
            .Dock = DockStyle.Fill, .ColumnCount = 1, .RowCount = 2,
            .Margin = Padding.Empty
        }
        header.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100))
        header.RowStyles.Add(New RowStyle(SizeType.Absolute, 30))
        header.RowStyles.Add(New RowStyle(SizeType.Percent, 100))
        Dim heading As New Label With {
            .Name = "lbl360CatalogTitle", .Text = "WIM 360  |  DETAILED INVENTORY",
            .Font = ModernTheme.UiFont(13.0F, True),
            .ForeColor = ModernTheme.TextInk, .TextAlign = ContentAlignment.MiddleLeft,
            .AutoEllipsis = True, .Dock = DockStyle.Fill
        }
        header.Controls.Add(heading, 0, 0)
        Dim explanation As New Label With {
            .Name = "lbl360CatalogTarget",
            .Text = "Index " & audit.Index.ToString() & "  |  " &
                Path.GetFileName(audit.ImageFile) & "  |  Read-only snapshot: " &
                audit.ScannedUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss") &
                "  |  No servicing commands",
            .ForeColor = ModernTheme.Muted,
            .TextAlign = ContentAlignment.MiddleLeft,
            .AutoEllipsis = True, .Dock = DockStyle.Fill
        }
        Dim detailsTip As New ToolTip()
        detailsTip.SetToolTip(explanation, audit.ImageFile & Environment.NewLine &
            "Mount: " & audit.MountFolder)
        header.Controls.Add(explanation, 0, 1)
        root.Controls.Add(header, 0, 0)

        Dim filterBar As New TableLayoutPanel With {
            .Name = "Wim360CatalogFilters",
            .ColumnCount = 4, .RowCount = 1,
            .Dock = DockStyle.Fill, .Margin = New Padding(0, 1, 0, 9)
        }
        filterBar.RowStyles.Add(New RowStyle(SizeType.Percent, 100))
        filterBar.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute, 152))
        filterBar.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute, 181))
        filterBar.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100))
        filterBar.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute, 133))
        root.Controls.Add(filterBar, 0, 1)

        typeFilter = New ComboBox With {
            .Name = "cmb360CatalogType", .Dock = DockStyle.Fill,
            .DropDownStyle = ComboBoxStyle.DropDownList,
            .Margin = New Padding(0, 7, 9, 6),
            .Font = ModernTheme.UiFont(9.4F)
        }
        typeFilter.Items.AddRange({"All types", "Drivers", "Packages", "Features", "Capabilities"})
        filterBar.Controls.Add(typeFilter, 0, 0)

        stateFilter = New ComboBox With {
            .Name = "cmb360CatalogState", .Dock = DockStyle.Fill,
            .DropDownStyle = ComboBoxStyle.DropDownList,
            .Margin = New Padding(0, 7, 9, 6),
            .Font = ModernTheme.UiFont(9.4F)
        }
        filterBar.Controls.Add(stateFilter, 1, 0)
        searchBox = New TextBox With {
            .Name = "txt360CatalogSearch", .Dock = DockStyle.Fill,
            .PlaceholderText = "Search name, provider, version, state...",
            .Margin = New Padding(0, 9, 9, 7),
            .Font = ModernTheme.UiFont(9.4F)
        }
        filterBar.Controls.Add(searchBox, 2, 0)

        Dim resetButton As New Button With {
            .Name = "btn360CatalogClear", .Text = "Clear filters",
            .Dock = DockStyle.Fill, .Margin = New Padding(0, 3, 0, 4)
        }
        ModernTheme.StyleButton(resetButton, "browse")
        AddHandler resetButton.Click, Sub(sender, e)
            searchBox.Clear()
            typeFilter.SelectedIndex = 0
            stateFilter.SelectedIndex = 0
        End Sub
        filterBar.Controls.Add(resetButton, 3, 0)

        inventoryGrid = New DataGridView With {
            .Name = "grid360Catalog", .Dock = DockStyle.Fill,
            .Margin = New Padding(0, 2, 0, 8), .ReadOnly = True,
            .VirtualMode = True, .RowHeadersVisible = False,
            .AllowUserToAddRows = False, .AllowUserToDeleteRows = False,
            .AllowUserToResizeRows = False, .AllowUserToOrderColumns = False,
            .MultiSelect = True, .SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            .AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None,
            .BackgroundColor = Color.White, .GridColor = Color.FromArgb(222, 232, 244),
            .BorderStyle = BorderStyle.FixedSingle,
            .ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing,
            .ColumnHeadersHeight = 35,
            .EnableHeadersVisualStyles = False
        }
        inventoryGrid.RowTemplate.Height = 27
        inventoryGrid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(233, 242, 253)
        inventoryGrid.ColumnHeadersDefaultCellStyle.ForeColor = ModernTheme.TextInk
        inventoryGrid.ColumnHeadersDefaultCellStyle.Font = ModernTheme.UiFont(9.4F, True)
        inventoryGrid.DefaultCellStyle.Font = ModernTheme.UiFont(9.4F)
        inventoryGrid.DefaultCellStyle.ForeColor = ModernTheme.TextInk
        inventoryGrid.DefaultCellStyle.BackColor = Color.White
        inventoryGrid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(218, 235, 252)
        inventoryGrid.DefaultCellStyle.SelectionForeColor = ModernTheme.TextInk
        Dim widths As Integer() = {113, 360, 130, 210, 134, 140, 115, 146}
        For i As Integer = 0 To Wim360Catalog.ColumnHeaders.Length - 1
            Dim columnIndex = inventoryGrid.Columns.Add("col360Catalog" & i.ToString(),
                                                         Wim360Catalog.ColumnHeaders(i))
            inventoryGrid.Columns(columnIndex).Width = widths(i)
            inventoryGrid.Columns(columnIndex).SortMode = DataGridViewColumnSortMode.Programmatic
        Next
        AddHandler inventoryGrid.CellValueNeeded, AddressOf GridValueNeeded
        AddHandler inventoryGrid.CellToolTipTextNeeded, AddressOf GridTooltipNeeded
        AddHandler inventoryGrid.ColumnHeaderMouseClick, AddressOf ColumnClicked
        root.Controls.Add(inventoryGrid, 0, 2)

        Dim footer As New TableLayoutPanel With {
            .Dock = DockStyle.Fill, .RowCount = 3, .ColumnCount = 1,
            .Margin = Padding.Empty
        }
        footer.RowStyles.Add(New RowStyle(SizeType.Absolute, 22))
        footer.RowStyles.Add(New RowStyle(SizeType.Absolute, 28))
        footer.RowStyles.Add(New RowStyle(SizeType.Percent, 100))
        footer.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100))
        root.Controls.Add(footer, 0, 3)

        resultLabel = New Label With {
            .Name = "lbl360CatalogCount", .Dock = DockStyle.Fill,
            .ForeColor = ModernTheme.TextInk, .AutoEllipsis = True,
            .TextAlign = ContentAlignment.MiddleLeft
        }
        footer.Controls.Add(resultLabel, 0, 0)

        warningLabel = New Label With {
            .Name = "lbl360CatalogWarnings", .Dock = DockStyle.Fill,
            .ForeColor = ModernTheme.Muted, .AutoEllipsis = True,
            .TextAlign = ContentAlignment.MiddleLeft,
            .Text = If(audit.FeatureWarning.Length = 0 AndAlso
                audit.CapabilityWarning.Length = 0,
                "Inventory is a snapshot; refresh Scan mounted index after changing the image.",
                "Partial component inventory: " &
                    If(audit.FeatureWarning.Length > 0, "Features: " & audit.FeatureWarning & "  ", "") &
                    If(audit.CapabilityWarning.Length > 0, "Capabilities: " & audit.CapabilityWarning, ""))
        }
        detailsTip.SetToolTip(warningLabel, warningLabel.Text)
        footer.Controls.Add(warningLabel, 0, 1)

        Dim footerButtons As New TableLayoutPanel With {
            .Dock = DockStyle.Fill, .ColumnCount = 4, .RowCount = 1,
            .Margin = Padding.Empty
        }
        footerButtons.RowStyles.Add(New RowStyle(SizeType.Percent, 100))
        footerButtons.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100))
        footerButtons.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute, 139))
        footerButtons.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute, 139))
        footerButtons.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute, 114))
        footer.Controls.Add(footerButtons, 0, 2)

        exportCsvButton = New Button With {
            .Name = "btn360ExportCsv", .Text = "Export CSV",
            .Dock = DockStyle.Fill, .Margin = New Padding(4, 3, 4, 1)
        }
        ModernTheme.StyleButton(exportCsvButton, "browse")
        AddHandler exportCsvButton.Click, Sub(sender, e) ExportRows(False)
        footerButtons.Controls.Add(exportCsvButton, 1, 0)

        exportJsonButton = New Button With {
            .Name = "btn360ExportJson", .Text = "Export JSON",
            .Dock = DockStyle.Fill, .Margin = New Padding(4, 3, 4, 1)
        }
        ModernTheme.StyleButton(exportJsonButton, "browse")
        AddHandler exportJsonButton.Click, Sub(sender, e) ExportRows(True)
        footerButtons.Controls.Add(exportJsonButton, 2, 0)

        Dim closeButton As New Button With {
            .Name = "btn360CatalogClose", .Text = "Close",
            .Dock = DockStyle.Fill, .Margin = New Padding(4, 3, 0, 1)
        }
        ModernTheme.StyleButton(closeButton, "secondary")
        AddHandler closeButton.Click, Sub(sender, e) Me.Close()
        footerButtons.Controls.Add(closeButton, 3, 0)
        Me.CancelButton = closeButton

        AddHandler typeFilter.SelectedIndexChanged, Sub(sender, e)
            UpdateStates()
            ApplyFilter()
        End Sub
        AddHandler stateFilter.SelectedIndexChanged, Sub(sender, e) ApplyFilter()
        AddHandler searchBox.TextChanged, Sub(sender, e) ApplyFilter()

        typeFilter.SelectedIndex = 0
        WindowGeometry.Install(Me, "Software\DISMGUI\Wim360Inventory")
    End Sub

    Private Sub UpdateStates()
        Dim old = If(TryCast(stateFilter.SelectedItem, String), "All states")
        Dim wantedType = If(TryCast(typeFilter.SelectedItem, String), "All types")
        Dim states = allRows.Where(Function(x)
            Return wantedType = "All types" OrElse
                   String.Equals(x.Kind, wantedType, StringComparison.OrdinalIgnoreCase)
        End Function).Select(Function(x) x.State).
            Where(Function(x) Not String.IsNullOrWhiteSpace(x)).
            Distinct(StringComparer.OrdinalIgnoreCase).
            OrderBy(Function(x) x, StringComparer.OrdinalIgnoreCase).ToList()
        stateFilter.BeginUpdate()
        Try
            stateFilter.Items.Clear()
            stateFilter.Items.Add("All states")
            For Each stateValue As String In states
                stateFilter.Items.Add(stateValue)
            Next
            Dim existing = stateFilter.Items.IndexOf(old)
            stateFilter.SelectedIndex = If(existing >= 0, existing, 0)
        Finally
            stateFilter.EndUpdate()
        End Try
    End Sub

    Private Sub ApplyFilter()
        If inventoryGrid Is Nothing OrElse typeFilter.SelectedIndex < 0 OrElse
           stateFilter.SelectedIndex < 0 Then Return
        Dim found = Wim360Catalog.FilterRows(allRows,
            Convert.ToString(typeFilter.SelectedItem),
            Convert.ToString(stateFilter.SelectedItem),
            searchBox.Text)
        Dim ordered = found.OrderBy(Function(x) x.CellAt(sortColumn),
                                     StringComparer.OrdinalIgnoreCase).ToList()
        If sortDescending Then ordered.Reverse()
        ' VirtualMode avoids creating tens of thousands of full DataGridViewRow objects.
        inventoryGrid.RowCount = 0
        displayedRows = ordered
        inventoryGrid.RowCount = displayedRows.Count
        inventoryGrid.Invalidate()
        For Each column As DataGridViewColumn In inventoryGrid.Columns
            column.HeaderCell.SortGlyphDirection = SortOrder.None
        Next
        inventoryGrid.Columns(sortColumn).HeaderCell.SortGlyphDirection =
            If(sortDescending, SortOrder.Descending, SortOrder.Ascending)
        resultLabel.Text = "Showing " & displayedRows.Count.ToString() &
            " of " & allRows.Count.ToString() &
            " entries  |  " & audit.Drivers.Count.ToString() & " drivers  |  " &
            audit.Packages.Count.ToString() & " packages  |  " &
            audit.FeatureCount.ToString() & " features  |  " &
            audit.CapabilityCount.ToString() & " capabilities"
    End Sub

    Private Sub ColumnClicked(sender As Object, e As DataGridViewCellMouseEventArgs)
        If e.ColumnIndex < 0 Then Return
        If sortColumn = e.ColumnIndex Then
            sortDescending = Not sortDescending
        Else
            sortColumn = e.ColumnIndex
            sortDescending = False
        End If
        ApplyFilter()
    End Sub

    Private Sub GridValueNeeded(sender As Object, e As DataGridViewCellValueEventArgs)
        If e.RowIndex >= 0 AndAlso e.RowIndex < displayedRows.Count AndAlso
           e.ColumnIndex >= 0 AndAlso e.ColumnIndex < Wim360Catalog.ColumnHeaders.Length Then
            e.Value = displayedRows(e.RowIndex).CellAt(e.ColumnIndex)
        End If
    End Sub

    Private Sub GridTooltipNeeded(sender As Object, e As DataGridViewCellToolTipTextNeededEventArgs)
        If e.RowIndex >= 0 AndAlso e.RowIndex < displayedRows.Count AndAlso
           e.ColumnIndex >= 0 AndAlso e.ColumnIndex < Wim360Catalog.ColumnHeaders.Length Then
            e.ToolTipText = displayedRows(e.RowIndex).CellAt(e.ColumnIndex)
        End If
    End Sub

    Private Sub ExportRows(asJson As Boolean)
        Dim extension = If(asJson, "json", "csv")
        Dim outputName = Path.GetFileNameWithoutExtension(audit.ImageFile) &
                         "-index" & audit.Index.ToString() & "-inventory." & extension
        Using dialog As New SaveFileDialog With {
            .Title = "Export filtered WIM 360 inventory",
            .Filter = If(asJson, "JSON document (*.json)|*.json", "CSV table (*.csv)|*.csv"),
            .DefaultExt = extension, .AddExtension = True,
            .OverwritePrompt = True, .RestoreDirectory = True,
            .FileName = outputName
        }
            If dialog.ShowDialog(Me) <> DialogResult.OK Then Return
            Try
                Dim content = If(asJson,
                    Wim360Catalog.ToJson(audit, displayedRows),
                    Wim360Catalog.ToCsv(audit, displayedRows))
                File.WriteAllText(dialog.FileName, content, New UTF8Encoding(True))
                resultLabel.Text = "Exported " & displayedRows.Count.ToString() &
                    " filtered entries to " & dialog.FileName
            Catch ex As Exception
                MessageBox.Show(Me, "Could not export inventory: " & ex.Message,
                                "WIM 360 - Export", MessageBoxButtons.OK,
                                MessageBoxIcon.Warning)
            End Try
        End Using
    End Sub
End Class
