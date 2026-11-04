Imports System
Imports System.Diagnostics
Imports System.Drawing
Imports System.Runtime.InteropServices
Imports System.Windows.Forms

' Deliberately replaces the legacy auto-generated About box and obsolete logo/blog link.
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
        Me.Font = ModernTheme.UiFont(9.0F)
        Me.ClientSize = New Size(510, 344)
        Me.MinimumSize = New Size(510, 344)
        Try
            Me.Icon = ModernTheme.MakeAppIcon()
        Catch
        End Try

        Dim banner As New Panel With {.Dock = DockStyle.Top, .Height = 90,
                                      .BackColor = ModernTheme.Navy}
        Me.Controls.Add(banner)
        Dim heading As New Label With {.Text = "DISM GUI", .Font = ModernTheme.UiFont(20, True),
                                       .ForeColor = Color.White, .BackColor = Color.Transparent,
                                       .Location = New Point(23, 14), .Size = New Size(320, 38)}
        banner.Controls.Add(heading)
        Dim subheading As New Label With {.Text = "Windows Image Toolkit · Modern Edition",
                                          .Font = ModernTheme.UiFont(9), .ForeColor = Color.LightCyan,
                                          .BackColor = Color.Transparent,
                                          .Location = New Point(25, 54), .Size = New Size(450, 23)}
        banner.Controls.Add(subheading)

        Dim versionValue = "Version " & GetType(frmAbout).Assembly.GetName().Version.ToString()
        Dim platformValue = RuntimeInformation.ProcessArchitecture.ToString()
        AddLine(versionValue, 22, 108, 462, ModernTheme.TextInk, 11.0F, True)
        AddLine(".NET 10 LTS  ·  " & platformValue & "  ·  Windows 10/11", 22, 142, 462,
                ModernTheme.Muted, 9.5F, False)
        AddLine("Mount, service, capture and apply Windows images with DISM.", 22, 174, 466,
                ModernTheme.TextInk, 9.0F, False)
        AddLine("Original DISM GUI by Mike Celone · Modernized project: sgennadi", 22, 205, 466,
                ModernTheme.Muted, 8.6F, False)

        Dim repo As New LinkLabel With {
            .Text = "github.com/sgennadi/DISM-GUI",
            .LinkColor = ModernTheme.Blue, .Font = ModernTheme.UiFont(9, True),
            .Location = New Point(22, 236), .Size = New Size(440, 25)
        }
        AddHandler repo.LinkClicked, Sub(sender, e)
            Process.Start(New ProcessStartInfo("https://github.com/sgennadi/DISM-GUI") With
                          {.UseShellExecute = True})
        End Sub
        Me.Controls.Add(repo)

        Dim updateButton As New Button With {.Text = "Check Updates", .Location = New Point(22, 282),
                                             .Size = New Size(172, 37)}
        ModernTheme.StyleButton(updateButton, "primary")
        AddHandler updateButton.Click, Sub(sender, e) UpdateManager.ShowUpdateDialog(Me)
        Me.Controls.Add(updateButton)

        Dim closeButton As New Button With {.Text = "Close", .Location = New Point(386, 282),
                                            .Size = New Size(102, 37)}
        ModernTheme.StyleButton(closeButton)
        AddHandler closeButton.Click, Sub(sender, e) Me.Close()
        Me.CancelButton = closeButton
        Me.Controls.Add(closeButton)
    End Sub

    Private Sub AddLine(value As String, x As Integer, y As Integer, width As Integer,
                        foreground As Color, size As Single, isBold As Boolean)
        Dim lbl As New Label With {.Text = value, .AutoEllipsis = True,
                                   .Font = ModernTheme.UiFont(size, isBold),
                                   .ForeColor = foreground, .Location = New Point(x, y),
                                   .Size = New Size(width, 27)}
        Me.Controls.Add(lbl)
    End Sub
End Class
