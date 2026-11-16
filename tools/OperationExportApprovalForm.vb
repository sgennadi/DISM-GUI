Imports System
Imports System.Drawing
Imports System.Windows.Forms

' Approval is deliberately NOT stored in profiles. Each new-WIM export needs
' fresh, interactive consent after preflight and before the DISM process runs.
Public NotInheritable Class OperationExportApprovalForm
    Inherits Form

    Private ReadOnly phrase As TextBox
    Private ReadOnly consent As CheckBox
    Private ReadOnly proceed As Button

    Public Sub New(sourcePath As String, index As Integer, outputPath As String)
        Me.Name = "OperationExportApprovalForm"
        Me.Text = "Confirm NEW WIM export"
        Me.MinimumSize = New Size(680, 425)
        Me.Size = New Size(860, 490)
        Me.StartPosition = FormStartPosition.CenterParent
        Me.ShowInTaskbar = False
        Me.BackColor = Color.White
        Me.Font = ModernTheme.UiFont(9.5F)
        Me.AutoScaleMode = AutoScaleMode.Font

        Dim root As New TableLayoutPanel With {
            .Name = "ExportApprovalLayout", .Dock = DockStyle.Fill,
            .ColumnCount = 1, .RowCount = 5,
            .Padding = New Padding(20, 16, 20, 14)
        }
        root.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100))
        root.RowStyles.Add(New RowStyle(SizeType.Absolute, 54))
        root.RowStyles.Add(New RowStyle(SizeType.Percent, 100))
        root.RowStyles.Add(New RowStyle(SizeType.Absolute, 52))
        root.RowStyles.Add(New RowStyle(SizeType.Absolute, 46))
        root.RowStyles.Add(New RowStyle(SizeType.Absolute, 62))
        Controls.Add(root)
        root.Controls.Add(New Label With {
            .Name = "lblExportApprovalHeading",
            .Text = "CONFIRM EXPORT TO A NEW WIM",
            .Font = ModernTheme.UiFont(12.5F, True),
            .ForeColor = ModernTheme.TextInk, .Dock = DockStyle.Fill,
            .TextAlign = ContentAlignment.MiddleLeft
        }, 0, 0)
        root.Controls.Add(New TextBox With {
            .Name = "txtExportApprovalDetail",
            .Text = "Source (read-only): " & sourcePath &
                    Environment.NewLine &
                    "Index: " & index.ToString() & Environment.NewLine &
                    "NEW WIM destination: " & outputPath &
                    Environment.NewLine & Environment.NewLine &
                    "DISM will export ONE index to a unique temporary file, " &
                    "verify its integrity and check source SHA-256 before publishing." &
                    Environment.NewLine &
                    "No existing WIM is overwritten. If interrupted, a partial " &
                    "file may remain and must be reviewed manually.",
            .Dock = DockStyle.Fill, .Multiline = True, .ReadOnly = True,
            .ScrollBars = ScrollBars.Vertical,
            .BackColor = Color.FromArgb(249, 251, 254)
        }, 0, 1)
        consent = New CheckBox With {
            .Name = "chkExportApproved",
            .Text = "I approve writing a NEW destination WIM, not modifying the source.",
            .Checked = False, .Dock = DockStyle.Fill,
            .AutoSize = False
        }
        root.Controls.Add(consent, 0, 2)
        Dim typedRow As New TableLayoutPanel With {
            .Dock = DockStyle.Fill, .ColumnCount = 2, .RowCount = 1
        }
        typedRow.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 42))
        typedRow.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 58))
        typedRow.Controls.Add(New Label With {
            .Text = "Type EXPORT to confirm", .Dock = DockStyle.Fill,
            .TextAlign = ContentAlignment.MiddleLeft
        }, 0, 0)
        phrase = New TextBox With {
            .Name = "txtExportConfirmation",
            .Dock = DockStyle.Fill
        }
        typedRow.Controls.Add(phrase, 1, 0)
        root.Controls.Add(typedRow, 0, 3)
        Dim buttons As New TableLayoutPanel With {
            .Dock = DockStyle.Fill, .ColumnCount = 2, .RowCount = 1
        }
        buttons.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 50))
        buttons.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 50))
        Dim cancel = New Button With {
            .Name = "btnExportDecline", .Text = "Cancel", .Dock = DockStyle.Fill,
            .DialogResult = DialogResult.Cancel, .Margin = New Padding(4, 9, 10, 9)
        }
        ModernTheme.StyleButton(cancel, "secondary")
        proceed = New Button With {
            .Name = "btnExportApprove", .Text = "Export NEW WIM", .Dock = DockStyle.Fill,
            .DialogResult = DialogResult.OK, .Margin = New Padding(4, 9, 2, 9),
            .Enabled = False
        }
        ModernTheme.StyleButton(proceed, "primary")
        buttons.Controls.Add(cancel, 0, 0)
        buttons.Controls.Add(proceed, 1, 0)
        root.Controls.Add(buttons, 0, 4)
        AddHandler phrase.TextChanged, Sub(sender, e) UpdateApproval()
        AddHandler consent.CheckedChanged, Sub(sender, e) UpdateApproval()
        Me.AcceptButton = proceed
        Me.CancelButton = cancel
        ModernTheme.MakeDialogScrollable(Me, root, 720, 510)
    End Sub

    Private Sub UpdateApproval()
        proceed.Enabled = consent.Checked AndAlso
            String.Equals(phrase.Text.Trim(), "EXPORT", StringComparison.Ordinal)
    End Sub
End Class
