Imports System
Imports System.Diagnostics
Imports System.Runtime.InteropServices
Imports System.Windows.Forms

' Central support contact used by About and the Tools menu.
' Only opens a draft in the user's default mail client; nothing is sent automatically.
Public NotInheritable Class SupportContact
    Private Sub New()
    End Sub

    Public Const SupportEmail As String = "sgennadi@hotmail.com"

    Public Shared Function BuildSupportMailto() As String
        Dim version = GetType(SupportContact).Assembly.GetName().Version.ToString(3)
        Dim subject = "DISM GUI Support - " & version
        Dim body = "DISM GUI version: " & version & vbCrLf &
                   "Architecture: " & RuntimeInformation.ProcessArchitecture.ToString() &
                   vbCrLf & vbCrLf &
                   "Issue description:" & vbCrLf & vbCrLf
        Return "mailto:" & SupportEmail &
               "?subject=" & Uri.EscapeDataString(subject) &
               "&body=" & Uri.EscapeDataString(body)
    End Function

    Public Shared Sub OpenSupportEmail(owner As IWin32Window)
        Try
            Process.Start(New ProcessStartInfo(BuildSupportMailto()) With {
                .UseShellExecute = True
            })
        Catch ex As Exception
            Dim message = "No default email application could be opened." &
                          Environment.NewLine & Environment.NewLine &
                          "Support email: " & SupportEmail &
                          Environment.NewLine & Environment.NewLine &
                          "Copy the address to the clipboard?"
            If MessageBox.Show(owner, message, "DISM GUI - Email Support",
                               MessageBoxButtons.YesNo, MessageBoxIcon.Information) =
                               DialogResult.Yes Then
                Try
                    Clipboard.SetText(SupportEmail)
                Catch
                    MessageBox.Show(owner, "Please copy this address manually: " &
                                    SupportEmail, "DISM GUI - Email Support",
                                    MessageBoxButtons.OK, MessageBoxIcon.Information)
                End Try
            End If
        End Try
    End Sub
End Class
