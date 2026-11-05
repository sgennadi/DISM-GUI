Imports System
Imports System.Drawing
Imports System.Windows.Forms
Imports Microsoft.Win32

' Restore the last usable main-window rectangle and maximize state.
' Handles missing/unplugged monitors and resolution/DPI changes safely.
Public NotInheritable Class WindowGeometry
    Private Sub New()
    End Sub

    Private Const SettingsKey As String = "Software\DISMGUI\UserInterface"

    Private Shared Function ReadInt(key As RegistryKey, name As String, fallback As Integer) As Integer
        Dim value = key.GetValue(name)
        If TypeOf value Is Integer Then Return CInt(value)
        Return fallback
    End Function

    ' Optional key isolates inventory and dialog placement from main-window geometry.
    Public Shared Sub Install(form As Form, Optional settingsKeyName As String = SettingsKey)
        Dim maximize As Boolean = False
        Try
            Using key = Registry.CurrentUser.OpenSubKey(settingsKeyName, False)
                If key IsNot Nothing Then
                    Dim left = ReadInt(key, "WindowLeft", Integer.MinValue)
                    Dim top = ReadInt(key, "WindowTop", Integer.MinValue)
                    Dim width = ReadInt(key, "WindowWidth", 0)
                    Dim height = ReadInt(key, "WindowHeight", 0)
                    maximize = ReadInt(key, "WindowMaximized", 0) <> 0

                    If left <> Integer.MinValue AndAlso top <> Integer.MinValue AndAlso
                       width >= form.MinimumSize.Width AndAlso
                       height >= form.MinimumSize.Height AndAlso
                       width < 12000 AndAlso height < 12000 Then

                        Dim saved As New Rectangle(left, top, width, height)
                        Dim working As Rectangle = Rectangle.Empty
                        Dim bestArea As Long = 0

                        For Each display As Screen In System.Windows.Forms.Screen.AllScreens
                            Dim intersection = Rectangle.Intersect(saved, display.WorkingArea)
                            Dim area As Long = CLng(intersection.Width) * CLng(intersection.Height)
                            If area > bestArea Then
                                bestArea = area
                                working = display.WorkingArea
                            End If
                        Next
                        ' An off-screen window is recovered onto the primary display.
                        If bestArea < 120L * 120L Then
                            working = Screen.PrimaryScreen.WorkingArea
                        End If

                        Dim safeWidth = Math.Min(saved.Width, Math.Max(500, working.Width))
                        Dim safeHeight = Math.Min(saved.Height, Math.Max(400, working.Height))
                        Dim x = Math.Max(working.Left, Math.Min(saved.X,
                                         working.Right - safeWidth))
                        Dim y = Math.Max(working.Top, Math.Min(saved.Y,
                                         working.Bottom - safeHeight))
                        form.StartPosition = FormStartPosition.Manual
                        form.Bounds = New Rectangle(x, y, safeWidth, safeHeight)
                    End If
                End If
            End Using
        Catch
            ' Corrupt/unavailable preferences must never stop DISM GUI starting.
        End Try

        If maximize Then
            AddHandler form.Shown, Sub(sender, e)
                If Not form.IsDisposed Then form.WindowState = FormWindowState.Maximized
            End Sub
        End If

        AddHandler form.FormClosed, Sub(sender, e)
            Try
                Dim bounds = If(form.WindowState = FormWindowState.Normal,
                                form.Bounds, form.RestoreBounds)
                If bounds.Width < 500 OrElse bounds.Height < 400 Then Return
                Using key = Registry.CurrentUser.CreateSubKey(settingsKeyName)
                    If key Is Nothing Then Return
                    key.SetValue("WindowLeft", bounds.X, RegistryValueKind.DWord)
                    key.SetValue("WindowTop", bounds.Y, RegistryValueKind.DWord)
                    key.SetValue("WindowWidth", bounds.Width, RegistryValueKind.DWord)
                    key.SetValue("WindowHeight", bounds.Height, RegistryValueKind.DWord)
                    key.SetValue("WindowMaximized",
                                 If(form.WindowState = FormWindowState.Maximized, 1, 0),
                                 RegistryValueKind.DWord)
                End Using
            Catch
                ' Preference saving is best-effort and never affects DISM jobs.
            End Try
        End Sub
    End Sub
End Class
