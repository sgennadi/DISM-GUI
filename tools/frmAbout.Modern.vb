Imports System
Imports System.Diagnostics
Imports System.Drawing
Imports System.Runtime.InteropServices
Imports System.Windows.Forms

' Compact responsive About form styled after Nipkg Creator.
Public NotInheritable Class frmAbout
    Inherits Form

    Public Sub New()
        Me.Text = "About DISM GUI"
        Me.StartPosition = FormStartPosition.CenterParent
        Me.FormBorderStyle = FormBorderStyle.FixedDialog
        Me.MaximizeBox = False
        Me.MinimizeBox = False
        Me.ShowInTaskbar = False
        Me.BackColor = ModernTheme.Backdrop
        Me.Font = ModernTheme.UiFont(10)
        Me.AutoScaleMode = AutoScaleMode.Font
        Me.ClientSize = New Size(680, 420)
        Me.MinimumSize = New Size(640, 410)
        Try
            Me.Icon = ModernTheme.MakeAppIcon()
        Catch
        End Try

        Dim root As New TableLayoutPanel With {
            .Dock = DockStyle.Fill, .ColumnCount = 1, .RowCount = 3,
            .BackColor = ModernTheme.Backdrop, .Margin = Padding.Empty,
            .Padding = New Padding(18, 15, 18, 13)
        }
        root.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100))
        root.RowStyles.Add(New RowStyle(SizeType.Absolute, 105))
        root.RowStyles.Add(New RowStyle(SizeType.Percent, 100))
        root.RowStyles.Add(New RowStyle(SizeType.Absolute, 62))
        Me.Controls.Add(root)

        Dim header As New TableLayoutPanel With {
            .Dock = DockStyle.Fill, .ColumnCount = 2,
            .RowCount = 1, .BackColor = ModernTheme.Blue,
            .Padding = New Padding(14, 10, 14, 10),
            .Margin = New Padding(0, 0, 0, 10)
        }
        header.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute, 83))
        header.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100))
        root.Controls.Add(header, 0, 0)

        Dim mark As New PictureBox With {
            .Dock = DockStyle.Fill, .SizeMode = PictureBoxSizeMode.Zoom,
            .Margin = New Padding(4, 3, 15, 3),
            .BackColor = Color.Transparent
        }
        Try
            Using ico = ModernTheme.MakeAppIcon()
                mark.Image = ico.ToBitmap()
            End Using
        Catch
        End Try
        header.Controls.Add(mark, 0, 0)

        Dim textHeader As New TableLayoutPanel With {
            .Dock = DockStyle.Fill, .ColumnCount = 1, .RowCount = 2,
            .BackColor = Color.Transparent, .Margin = Padding.Empty
        }
        textHeader.RowStyles.Add(New RowStyle(SizeType.Percent, 55))
        textHeader.RowStyles.Add(New RowStyle(SizeType.Percent, 45))
        header.Controls.Add(textHeader, 1, 0)
        textHeader.Controls.Add(CreateLabel("DISM GUI", 20, True, Color.White), 0, 0)
        textHeader.Controls.Add(CreateLabel("Windows Image Toolkit  ·  Modern Edition",
                                           10, False, Color.FromArgb(231, 246, 255)), 0, 1)

        Dim body As New TableLayoutPanel With {
            .Dock = DockStyle.Fill, .ColumnCount = 1, .RowCount = 5,
            .Margin = Padding.Empty, .Padding = New Padding(18, 14, 18, 7),
            .BackColor = Color.White
        }
        body.RowStyles.Add(New RowStyle(SizeType.Absolute, 38))
        body.RowStyles.Add(New RowStyle(SizeType.Absolute, 35))
        body.RowStyles.Add(New RowStyle(SizeType.Absolute, 40))
        body.RowStyles.Add(New RowStyle(SizeType.Absolute, 40))
        body.RowStyles.Add(New RowStyle(SizeType.Percent, 100))
        root.Controls.Add(body, 0, 1)

        Dim versionText = "Version " & GetType(frmAbout).Assembly.GetName().Version.ToString()
        body.Controls.Add(CreateLabel(versionText, 13, True, ModernTheme.TextInk), 0, 0)
        Dim platform = RuntimeInformation.ProcessArchitecture.ToString()
        body.Controls.Add(CreateLabel(".NET 10 LTS   |   " & platform &
                                     "   |   Windows 10 / 11", 10, False, ModernTheme.Muted), 0, 1)
        body.Controls.Add(CreateLabel("Mount, service, capture and apply Windows images.", 10,
                                     False, ModernTheme.TextInk), 0, 2)
        body.Controls.Add(CreateLabel("Original author: Mike Celone    ·    Modernized by sgennadi",
                                     9.5F, False, ModernTheme.Muted), 0, 3)
        Dim link As New LinkLabel With {
            .Text = "github.com/sgennadi/DISM-GUI",
            .Dock = DockStyle.Fill, .LinkColor = ModernTheme.Blue,
            .ActiveLinkColor = ModernTheme.Green,
            .Font = ModernTheme.UiFont(10, True),
            .TextAlign = ContentAlignment.MiddleLeft, .Margin = Padding.Empty
        }
        AddHandler link.LinkClicked, Sub(sender, e)
            Process.Start(New ProcessStartInfo("https://github.com/sgennadi/DISM-GUI") With
                          {.UseShellExecute = True})
        End Sub
        body.Controls.Add(link, 0, 4)

        Dim footer As New TableLayoutPanel With {
            .Dock = DockStyle.Fill, .ColumnCount = 3, .RowCount = 1,
            .BackColor = ModernTheme.Backdrop, .Padding = New Padding(0, 11, 0, 0),
            .Margin = Padding.Empty
        }
        footer.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute, 185))
        footer.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100))
        footer.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute, 110))
        root.Controls.Add(footer, 0, 2)

        Dim updateButton As New Button With {.Text = "Check Updates",
                                             .Dock = DockStyle.Fill,
                                             .Margin = New Padding(0, 0, 12, 2)}
        ModernTheme.StyleButton(updateButton, "primary")
        AddHandler updateButton.Click, Sub(sender, e) UpdateManager.ShowUpdateDialog(Me)
        footer.Controls.Add(updateButton, 0, 0)
        Dim closeButton As New Button With {.Text = "Close",
                                            .Dock = DockStyle.Fill,
                                            .Margin = New Padding(0, 0, 0, 2)}
        ModernTheme.StyleButton(closeButton, "neutral")
        AddHandler closeButton.Click, Sub(sender, e) Me.Close()
        Me.CancelButton = closeButton
        footer.Controls.Add(closeButton, 2, 0)
    End Sub

    Private Shared Function CreateLabel(caption As String, size As Single,
                                         bold As Boolean, color As Color) As Label
        Return New Label With {
            .Text = caption, .Font = ModernTheme.UiFont(size, bold),
            .ForeColor = color, .BackColor = Color.Transparent,
            .TextAlign = ContentAlignment.MiddleLeft, .Dock = DockStyle.Fill,
            .AutoEllipsis = False, .Margin = Padding.Empty
        }
    End Function
End Class
