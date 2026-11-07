Imports System
Imports System.Drawing
Imports System.IO
Imports System.Linq
Imports System.Text
Imports System.Threading
Imports System.Threading.Tasks
Imports System.Windows.Forms

' SWM is always an immutable source. Export builds a NEW WIM, never changes SWM.
Public NotInheritable Class Wim360SwmToolsForm
    Inherits Form

    Private ReadOnly sourceBox As TextBox
    Private ReadOnly indexBox As ComboBox
    Private ReadOnly targetBox As TextBox
    Private ReadOnly summary As TextBox
    Private ReadOnly statusLabel As Label
    Private ReadOnly auditButton As Button
    Private ReadOnly exportButton As Button
    Private ReadOnly closeButton As Button
    Private activeJob As CancellationTokenSource
    Private currentAudit As Wim360SwmOperations.AuditResult
    Private loading As Boolean

    Public Sub New(Optional selectedSwm As String = "")
        Me.Name = "Wim360SwmToolsForm"
        Me.Text = "DISM GUI - WIM 360 - SWM audit and safe export"
        Me.MinimumSize = New Size(800, 550)
        Me.Size = New Size(1130, 730)
        Me.StartPosition = FormStartPosition.CenterParent
        Me.ShowInTaskbar = False
        Me.AutoScaleMode = AutoScaleMode.Font
        Me.Font = ModernTheme.UiFont(9.5F)
        Me.BackColor = Color.White

        Dim scroll As New Panel With {
            .Name = "Wim360SwmViewport", .Dock = DockStyle.Fill,
            .AutoScroll = True, .BackColor = Color.White
        }
        Controls.Add(scroll)
        Dim factor As Single = Math.Max(1.0F, Me.Font.Height / 16.0F)
        Dim formLayout As New TableLayoutPanel With {
            .Name = "Wim360SwmLayout", .ColumnCount = 1, .RowCount = 7,
            .Padding = New Padding(16, 9, 16, 12),
            .Margin = Padding.Empty, .BackColor = Color.White,
            .Dock = DockStyle.None
        }
        formLayout.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100))
        Dim sizes = New Integer() {86, 71, 68, 71, 0, 78, 75}
        For Each n In sizes
            formLayout.RowStyles.Add(New RowStyle(
                If(n = 0, SizeType.Percent, SizeType.Absolute),
                If(n = 0, 100, CSng(Math.Ceiling(n * factor)))))
        Next
        scroll.Controls.Add(formLayout)
        Dim fit As Action = Sub()
            If scroll.IsDisposed OrElse formLayout.IsDisposed Then Return
            Dim minHeight = CInt(Math.Ceiling(690 * factor))
            formLayout.Size = New Size(Math.Max(CInt(710 * factor),
                                               scroll.ClientSize.Width - 1),
                                      Math.Max(minHeight, scroll.ClientSize.Height - 1))
        End Sub
        AddHandler scroll.ClientSizeChanged, Sub(sender, e) fit()
        AddHandler Me.Shown, Sub(sender, e)
            fit()
            scroll.AutoScrollPosition = Point.Empty
        End Sub
        fit()

        Dim heading As New TableLayoutPanel With {.ColumnCount = 1,
            .RowCount = 2, .Dock = DockStyle.Fill}
        heading.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100))
        heading.RowStyles.Add(New RowStyle(SizeType.Percent, 54))
        heading.RowStyles.Add(New RowStyle(SizeType.Percent, 46))
        heading.Controls.Add(MakeLabel("WIM 360  |  SWM INTEGRITY & EXPORT",
                                      12.8F, True, ModernTheme.TextInk), 0, 0)
        heading.Controls.Add(MakeLabel("Verify EVERY split part before exporting one index to a new WIM.",
                                      9.0F, False, ModernTheme.Muted), 0, 1)
        formLayout.Controls.Add(heading, 0, 0)

        Dim sourceRow = NewRow()
        sourceBox = New TextBox With {
            .Name = "txtSwmSource", .Text = If(selectedSwm, "").Trim(),
            .Dock = DockStyle.Fill, .PlaceholderText = "First or any .swm part",
            .Margin = New Padding(0, 8, 7, 6)
        }
        sourceRow.Controls.Add(sourceBox, 0, 0)
        Dim browseSource As New Button With {
            .Name = "btnSwmBrowse", .Text = "Browse SWM...",
            .Dock = DockStyle.Fill, .Margin = New Padding(0, 4, 0, 4)
        }
        ModernTheme.StyleButton(browseSource, "browse")
        AddHandler browseSource.Click, Sub(sender, e) BrowseSwm()
        sourceRow.Controls.Add(browseSource, 1, 0)
        formLayout.Controls.Add(sourceRow, 0, 1)

        Dim indexRow = NewRow()
        indexBox = New ComboBox With {
            .Name = "cmbSwmImageIndex", .Dock = DockStyle.Fill,
            .DropDownStyle = ComboBoxStyle.DropDownList,
            .Margin = New Padding(0, 8, 7, 6)
        }
        indexRow.Controls.Add(indexBox, 0, 0)
        Dim inspectButton As New Button With {
            .Name = "btnSwmInspect", .Text = "Inspect indexes",
            .Dock = DockStyle.Fill, .Margin = New Padding(0, 4, 0, 4)
        }
        ModernTheme.StyleButton(inspectButton, "secondary")
        AddHandler inspectButton.Click, Sub(sender, e) InspectIndexes()
        indexRow.Controls.Add(inspectButton, 1, 0)
        formLayout.Controls.Add(indexRow, 0, 2)

        Dim targetRow = NewRow()
        targetBox = New TextBox With {
            .Name = "txtSwmDestination", .Dock = DockStyle.Fill,
            .PlaceholderText = "New destination file, for example C:\Images\recovered.wim",
            .Margin = New Padding(0, 8, 7, 6)
        }
        targetRow.Controls.Add(targetBox, 0, 0)
        Dim browseTarget As New Button With {
            .Name = "btnSwmDestination", .Text = "Choose WIM...",
            .Dock = DockStyle.Fill, .Margin = New Padding(0, 4, 0, 4)
        }
        ModernTheme.StyleButton(browseTarget, "browse")
        AddHandler browseTarget.Click, Sub(sender, e) BrowseTarget()
        targetRow.Controls.Add(browseTarget, 1, 0)
        formLayout.Controls.Add(targetRow, 0, 3)

        summary = New TextBox With {
            .Name = "txtSwmAuditSummary", .Dock = DockStyle.Fill,
            .Multiline = True, .ReadOnly = True, .WordWrap = False,
            .ScrollBars = ScrollBars.Both, .BackColor = Color.FromArgb(249, 251, 254),
            .Font = New Font("Consolas", 9.0F),
            .Text = "SWM sources are immutable. Audit all parts before export." &
                Environment.NewLine &
                "A successful header check alone is NOT proof of intact content.",
            .Margin = New Padding(0, 6, 0, 5)
        }
        formLayout.Controls.Add(summary, 0, 4)

        statusLabel = MakeLabel(
            "Only exports a new WIM. Existing SWM parts will not be overwritten.",
            9.0F, False, ModernTheme.Muted)
        statusLabel.Name = "lblSwmProgress"
        statusLabel.Margin = New Padding(0, 4, 0, 4)
        formLayout.Controls.Add(statusLabel, 0, 5)

        Dim footer As New TableLayoutPanel With {
            .Name = "Wim360SwmStickyActions", .Dock = DockStyle.Bottom,
            .ColumnCount = 3, .RowCount = 1,
            .Height = CInt(Math.Ceiling(71 * factor)),
            .Padding = New Padding(16, 4, 16, 4), .BackColor = Color.White
        }
        For Each width As Single In {34.0F, 42.0F, 24.0F}
            footer.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, width))
        Next
        auditButton = New Button With {
            .Name = "btnSwmAudit", .Text = "Audit all SWM parts",
            .Dock = DockStyle.Fill, .Margin = New Padding(0, 5, 9, 5)
        }
        ModernTheme.StyleButton(auditButton, "primary")
        AddHandler auditButton.Click, AddressOf AuditClicked
        footer.Controls.Add(auditButton, 0, 0)
        exportButton = New Button With {
            .Name = "btnSwmExport", .Text = "Export selected index to NEW WIM",
            .Dock = DockStyle.Fill, .Enabled = False,
            .Margin = New Padding(0, 5, 9, 5)
        }
        ModernTheme.StyleButton(exportButton, "secondary")
        AddHandler exportButton.Click, AddressOf ExportClicked
        footer.Controls.Add(exportButton, 1, 0)
        closeButton = New Button With {
            .Name = "btnSwmCancelClose", .Text = "Close",
            .Dock = DockStyle.Fill, .Margin = New Padding(0, 5, 0, 5)
        }
        ModernTheme.StyleButton(closeButton, "neutral")
        AddHandler closeButton.Click, Sub(sender, e)
            If loading Then
                activeJob?.Cancel()
            Else
                Close()
            End If
        End Sub
        footer.Controls.Add(closeButton, 2, 0)
        Controls.Add(footer)
        footer.BringToFront()
        AddHandler sourceBox.TextChanged, Sub(sender, e)
            InvalidateAudit()
            indexBox.Items.Clear()
        End Sub
        AddHandler indexBox.SelectedIndexChanged, Sub(sender, e) UpdateButtons()
        AddHandler targetBox.TextChanged, Sub(sender, e) UpdateButtons()
        AddHandler Me.FormClosing, Sub(sender, e)
            If loading Then
                activeJob?.Cancel()
                e.Cancel = True
            End If
        End Sub
        WindowGeometry.Install(Me, "Software\DISMGUI\Wim360SwmTools")
        UpdateButtons()
        If Not String.IsNullOrWhiteSpace(sourceBox.Text) Then
            AddHandler Me.Shown, Sub(sender, e) InspectIndexes()
        End If
    End Sub

    Private Shared Function MakeLabel(value As String, size As Single,
                                       bold As Boolean, foreground As Color) As Label
        Return New Label With {
            .Text = value, .Font = ModernTheme.UiFont(size, bold),
            .ForeColor = foreground, .Dock = DockStyle.Fill,
            .TextAlign = ContentAlignment.MiddleLeft,
            .AutoEllipsis = False
        }
    End Function

    Private Shared Function NewRow() As TableLayoutPanel
        Dim grid As New TableLayoutPanel With {
            .Dock = DockStyle.Fill, .ColumnCount = 2, .RowCount = 1,
            .Margin = Padding.Empty
        }
        grid.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 76))
        grid.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 24))
        grid.RowStyles.Add(New RowStyle(SizeType.Percent, 100))
        Return grid
    End Function

    Private Sub InvalidateAudit()
        currentAudit = Nothing
        UpdateButtons()
    End Sub

    Private Sub UpdateButtons()
        If auditButton Is Nothing Then Return
        auditButton.Enabled = Not loading AndAlso sourceBox.Text.Trim().Length > 0
        exportButton.Enabled = Not loading AndAlso
            currentAudit IsNot Nothing AndAlso currentAudit.Verified AndAlso
            indexBox.SelectedItem IsNot Nothing AndAlso
            targetBox.Text.Trim().Length > 0
        closeButton.Text = If(loading, "Cancel scan", "Close")
    End Sub

    Private Sub BrowseSwm()
        If loading Then Return
        Using picker As New OpenFileDialog With {
            .Filter = "Split Windows image (*.swm)|*.swm",
            .Title = "Choose any member of a complete SWM set",
            .CheckFileExists = True, .RestoreDirectory = True
        }
            If picker.ShowDialog(Me) = DialogResult.OK Then
                sourceBox.Text = picker.FileName
                InspectIndexes()
            End If
        End Using
    End Sub

    Private Sub BrowseTarget()
        If loading Then Return
        Using picker As New SaveFileDialog With {
            .Title = "Choose a NEW WIM filename - existing images are not overwritten",
            .Filter = "Windows image (*.wim)|*.wim",
            .DefaultExt = "wim", .AddExtension = True, .OverwritePrompt = False,
            .RestoreDirectory = True
        }
            If picker.ShowDialog(Me) <> DialogResult.OK Then Return
            If File.Exists(picker.FileName) Then
                MessageBox.Show(Me, "Choose a filename that does NOT exist.",
                    "SWM export", MessageBoxButtons.OK, MessageBoxIcon.Warning)
                Return
            End If
            targetBox.Text = picker.FileName
        End Using
    End Sub

    Private Sub InspectIndexes()
        If loading OrElse sourceBox.Text.Trim().Length = 0 Then Return
        InvalidateAudit()
        indexBox.Items.Clear()
        Try
            Dim report = Wim360Reader.Inspect(sourceBox.Text.Trim())
            If Not report.IsSplitSet OrElse report.SplitSet Is Nothing Then
                Throw New InvalidDataException("The selected source is not a complete SWM set.")
            End If
            For Each item In report.Images.OrderBy(Function(x) x.Index)
                indexBox.Items.Add(item)
            Next
            If indexBox.Items.Count > 0 Then indexBox.SelectedIndex = 0
            summary.Text = Wim360SplitSet.Describe(report.SplitSet) &
                Environment.NewLine & "Image indexes: " &
                String.Join(", ", report.Images.Select(Function(x) x.Index.ToString())) &
                Environment.NewLine & "Not yet SHA-256 verified."
            statusLabel.Text = "Click Audit all SWM parts before exporting."
        Catch ex As Exception
            ErrorDiagnostics.RememberFailure("WIM 360 SWM Inspect", ex)
            statusLabel.Text = "SWM Inspect failed: " & ex.Message
        End Try
    End Sub

    Private Async Sub AuditClicked(sender As Object, e As EventArgs)
        If loading Then Return
        Dim source = sourceBox.Text.Trim()
        If source.Length = 0 Then Return
        Dim stopSource As New CancellationTokenSource()
        activeJob = stopSource
        loading = True
        UpdateButtons()
        statusLabel.Text = "Checking headers and calculating SHA-256 for all parts..."
        Dim progress As IProgress(Of String) = New Progress(Of String)(Sub(s)
            If Not IsDisposed AndAlso Not stopSource.IsCancellationRequested Then
                statusLabel.Text = s
            End If
        End Sub)
        Try
            Dim audited = Await Task.Run(Function()
                Return Wim360SwmOperations.Audit(source, stopSource.Token, progress)
            End Function)
            If IsDisposed OrElse stopSource.IsCancellationRequested Then Return
            currentAudit = audited
            Dim report = Wim360Reader.Inspect(audited.MainFile)
            indexBox.Items.Clear()
            For Each item In report.Images
                indexBox.Items.Add(item)
            Next
            If indexBox.Items.Count > 0 Then indexBox.SelectedIndex = 0
            Dim output = New StringBuilder()
            output.AppendLine("VERIFIED: " & audited.Parts.Count.ToString() &
                              " complete SWM parts")
            output.AppendLine("Set SHA-256: " & audited.CombinedSha256)
            output.AppendLine("Image indexes: " &
                              String.Join(", ", audited.Images))
            output.AppendLine("")
            For Each p In audited.Parts
                output.AppendLine(Path.GetFileName(p.FullPath) & "  " &
                    p.Length.ToString("N0") & " bytes  " & p.Sha256)
            Next
            summary.Text = output.ToString()
            statusLabel.Text = "All parts verified. Choose index and a NEW WIM path."
        Catch ex As OperationCanceledException
            If Not IsDisposed Then statusLabel.Text = "SWM audit canceled. No image was modified."
        Catch ex As Exception
            ErrorDiagnostics.RememberFailure("WIM 360 SWM integrity audit", ex)
            If Not IsDisposed Then statusLabel.Text = "SWM audit failed: " & ex.Message
        Finally
            loading = False
            activeJob = Nothing
            stopSource.Dispose()
            If Not IsDisposed Then UpdateButtons()
        End Try
    End Sub

    Private Async Sub ExportClicked(sender As Object, e As EventArgs)
        If loading OrElse currentAudit Is Nothing OrElse Not currentAudit.Verified Then Return
        Dim selected = TryCast(indexBox.SelectedItem, Wim360Reader.ImageInfo)
        If selected Is Nothing Then Return
        Dim target = targetBox.Text.Trim()
        If target.Length = 0 Then Return
        If File.Exists(target) Then
            MessageBox.Show(Me, "This file already exists. No overwrite is allowed.",
                "SWM export", MessageBoxButtons.OK, MessageBoxIcon.Warning)
            Return
        End If
        If MessageBox.Show(Me, "Export verified SWM index " &
            selected.Index.ToString() & " to a NEW WIM?" &
            Environment.NewLine & target & Environment.NewLine &
            "Both the audit and DISM export recheck all SWM bytes. " &
            "This may take significant disk space and processing time.",
            "SWM export - confirm", MessageBoxButtons.YesNo,
            MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) <>
            DialogResult.Yes Then Return
        Dim stopSource As New CancellationTokenSource()
        activeJob = stopSource
        loading = True
        UpdateButtons()
        Dim progress As IProgress(Of String) = New Progress(Of String)(Sub(s)
            If Not IsDisposed AndAlso Not stopSource.IsCancellationRequested Then
                statusLabel.Text = s
            End If
        End Sub)
        Try
            Dim audit = currentAudit
            Dim output = Await Task.Run(Function()
                Return Wim360SwmOperations.ExportNewWimAsync(
                    audit, selected.Index, target, stopSource.Token, progress)
            End Function)
            If IsDisposed OrElse stopSource.IsCancellationRequested Then Return
            statusLabel.Text = "Export completed. New WIM index 1: " & output.OutputFile
            summary.AppendText(Environment.NewLine & Environment.NewLine &
                "NEW WIM CREATED: " & output.OutputFile & Environment.NewLine &
                "Bytes: " & output.OutputBytes.ToString("N0") &
                " | Original SWM parts unchanged (full SHA-256).")
        Catch ex As OperationCanceledException
            If Not IsDisposed Then statusLabel.Text = "Export canceled; no destination WIM published."
        Catch ex As Exception
            ErrorDiagnostics.RememberFailure("WIM 360 SWM export", ex)
            If Not IsDisposed Then
                statusLabel.Text = "SWM export failed; no destination WIM published."
                summary.AppendText(Environment.NewLine & "Error: " & ex.Message)
            End If
        Finally
            loading = False
            activeJob = Nothing
            stopSource.Dispose()
            If Not IsDisposed Then UpdateButtons()
        End Try
    End Sub

    ' A non-running, synthetic preview used only by UI smoke tests.
    Public Sub PreviewAuditForTest(audited As Wim360SwmOperations.AuditResult)
        currentAudit = audited
        If audited Is Nothing Then Return
        summary.Text = "VERIFIED: " & audited.Parts.Count.ToString() &
            " SWM parts" & Environment.NewLine &
            "Set SHA-256: " & audited.CombinedSha256
        statusLabel.Text = "Preview of a completed SWM audit."
        UpdateButtons()
    End Sub
End Class
