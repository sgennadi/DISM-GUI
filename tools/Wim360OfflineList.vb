Imports System
Imports System.Collections.Generic
Imports System.ComponentModel
Imports System.Diagnostics
Imports System.IO
Imports System.Linq
Imports System.Text
Imports System.Threading

' Read the real file/directory catalog directly from WIM/ESD using the Windows DISM API.
' This mode never mounts, applies, commits, extracts, or modifies an image.
' Unlike the mounted mode, DISM does not expose file sizes or byte hashes here.
Public NotInheritable Class Wim360OfflineList
    Private Sub New()
    End Sub

    Private Const MaxEntriesPerIndex As Integer = 250000
    Private Const MaxEntriesTotal As Integer = 650000

    ' A pure parser for the English DISM output; also used by regression tests.
    Public Shared Function ParseEnglishListing(output As String,
                                                index As Integer) As List(Of Wim360IndexComparer.Item)
        If String.IsNullOrWhiteSpace(output) OrElse
           output.IndexOf("The operation completed successfully.",
                          StringComparison.OrdinalIgnoreCase) < 0 Then
            Throw New InvalidDataException(
                "DISM /List-Image did not report successful completion. No partial results can be compared.")
        End If
        Dim rows As New List(Of Wim360IndexComparer.Item)()
        Using reader As New StringReader(output)
            Dim line = reader.ReadLine()
            While line IsNot Nothing
                Dim item = ParseEntryLine(line, index)
                If item IsNot Nothing Then
                    rows.Add(item)
                    If rows.Count > MaxEntriesPerIndex Then
                        Throw New InvalidDataException("Index exceeds the supported catalog size.")
                    End If
                End If
                line = reader.ReadLine()
            End While
        End Using
        Return rows
    End Function

    Private Shared Function ParseEntryLine(line As String,
                                            index As Integer) As Wim360IndexComparer.Item
        If String.IsNullOrEmpty(line) OrElse
           Not line.StartsWith("\", StringComparison.Ordinal) OrElse
           line = "\" Then Return Nothing
        Dim folder = line.EndsWith("\", StringComparison.Ordinal)
        Dim relative = line.Substring(1)
        If folder Then relative = relative.TrimEnd("\"c)
        If relative.Length = 0 OrElse
           relative.IndexOf(ChrW(0)) >= 0 OrElse
           relative.Contains(ChrW(13)) OrElse relative.Contains(ChrW(10)) Then
            Throw New InvalidDataException("Invalid path in DISM image content.")
        End If
        Return New Wim360IndexComparer.Item With {
            .Index = index,
            .Kind = If(folder, "Folders", "Files"),
            .Key = relative,
            .Value = "Present"
        }
    End Function

    ' DISM /List-Image writes to a redirected console stream with the active
    ' console codepage. Non-ASCII image paths can become '?' or replacement
    ' characters, causing FALSE missing-item reports. Use the Windows DISM
    ' PowerShell API and ASCII Base64 transport for lossless Unicode paths.
    ' Both APIs read the WIM catalog without mounting or modifying it.
    Private Shared Function ListIndex(imagePath As String, index As Integer,
                     token As CancellationToken,
                     progress As IProgress(Of String)) As List(Of Wim360IndexComparer.Item)
        Dim encodedPath = Convert.ToBase64String(Encoding.Unicode.GetBytes(imagePath))
        Dim script = "$ErrorActionPreference='Stop'; " &
            "Import-Module Dism -ErrorAction Stop; " &
            "$image=[Text.Encoding]::Unicode.GetString(" &
            "[Convert]::FromBase64String('" & encodedPath & "')); " &
            "Get-WindowsImageContent -ImagePath $image -Index " &
            index.ToString(Globalization.CultureInfo.InvariantCulture) &
            " -ErrorAction Stop | ForEach-Object { " &
            "[Console]::Out.WriteLine('W360:' + " &
            "[Convert]::ToBase64String(" &
            "[Text.Encoding]::Unicode.GetBytes([string]$_))) " &
            "}; [Console]::Out.WriteLine('W360:END')"
        Dim encodedScript = Convert.ToBase64String(Encoding.Unicode.GetBytes(script))

        Dim windowsPath = Environment.GetFolderPath(Environment.SpecialFolder.Windows)
        Dim shellDir = If(Environment.Is64BitOperatingSystem AndAlso
                          Not Environment.Is64BitProcess, "Sysnative", "System32")
        Dim shell = Path.Combine(windowsPath, shellDir,
                                 "WindowsPowerShell", "v1.0", "powershell.exe")
        If Not File.Exists(shell) Then shell = "powershell.exe"
        Dim start As New ProcessStartInfo(shell) With {
            .UseShellExecute = False,
            .CreateNoWindow = True,
            .RedirectStandardOutput = True,
            .RedirectStandardError = True,
            .StandardOutputEncoding = Encoding.ASCII,
            .StandardErrorEncoding = Encoding.UTF8
        }
        For Each arg In {"-NoLogo", "-NoProfile", "-NonInteractive",
                         "-EncodedCommand", encodedScript}
            start.ArgumentList.Add(arg)
        Next
        Using proc As New Process With {.StartInfo = start}
            If Not proc.Start() Then
                Throw New InvalidOperationException(
                    "Windows PowerShell could not start the DISM image-content reader.")
            End If
            Dim errors = proc.StandardError.ReadToEndAsync()
            Using timeout As New CancellationTokenSource(TimeSpan.FromMinutes(12))
                Using linked = CancellationTokenSource.CreateLinkedTokenSource(
                    token, timeout.Token)
                    Using registration = linked.Token.Register(Sub()
                        Try
                            If Not proc.HasExited Then proc.Kill(True)
                        Catch ex As InvalidOperationException
                            ' The process has already stopped.
                        Catch ex As Win32Exception
                            ' Report the cancellation or process exit below.
                        End Try
                    End Sub)
                        Dim entries As New List(Of Wim360IndexComparer.Item)()
                        Dim successful As Boolean = False
                        Dim line = proc.StandardOutput.ReadLine()
                        While line IsNot Nothing
                            If token.IsCancellationRequested Then
                                Throw New OperationCanceledException(token)
                            End If
                            If timeout.IsCancellationRequested Then
                                Throw New TimeoutException(
                                    "Reading WIM index " & index.ToString() &
                                    " exceeded 12 minutes. Incomplete results were discarded.")
                            End If
                            If line = "W360:END" Then
                                successful = True
                            ElseIf line.StartsWith("W360:", StringComparison.Ordinal) Then
                                Dim raw = Convert.FromBase64String(line.Substring(5))
                                Dim original = Encoding.Unicode.GetString(raw)
                                Dim entry = ParseEntryLine(original, index)
                                If entry IsNot Nothing Then
                                    entries.Add(entry)
                                    If entries.Count > MaxEntriesPerIndex Then
                                        Try
                                            If Not proc.HasExited Then proc.Kill(True)
                                        Catch ex As InvalidOperationException
                                        End Try
                                        Throw New InvalidDataException(
                                            "Index " & index.ToString() & " exceeds " &
                                            MaxEntriesPerIndex.ToString() &
                                            " paths; no incomplete comparison was shown.")
                                    End If
                                    If entries.Count Mod 25000 = 0 AndAlso
                                       progress IsNot Nothing Then
                                        progress.Report("Index " & index.ToString() & ": " &
                                            entries.Count.ToString("N0") &
                                            " paths read (no mount)")
                                    End If
                                End If
                            End If
                            line = proc.StandardOutput.ReadLine()
                        End While
                        proc.WaitForExit()
                        If token.IsCancellationRequested Then
                            Throw New OperationCanceledException(token)
                        End If
                        If timeout.IsCancellationRequested Then
                            Throw New TimeoutException(
                                "Reading index " & index.ToString() & " timed out.")
                        End If
                        Dim errorText = errors.GetAwaiter().GetResult()
                        If proc.ExitCode <> 0 OrElse Not successful Then
                            Throw New InvalidOperationException(
                                "DISM PowerShell could not list index " &
                                index.ToString() & " (exit " & proc.ExitCode.ToString() &
                                "). The WIM was not modified. " &
                                If(errorText.Length > 1100,
                                   errorText.Substring(0, 1100), errorText))
                        End If
                        Return entries
                    End Using
                End Using
            End Using
        End Using
    End Function

    Public Shared Function Compare(report As Wim360Reader.WimReport,
                          indexes As IEnumerable(Of Integer),
                          token As CancellationToken,
                          Optional progress As IProgress(Of String) = Nothing
                          ) As Wim360IndexComparer.Result
        Dim chosen = Wim360IndexComparer.SelectedIndexes(report, indexes)
        If report.IsSplitSet Then
            Throw New NotSupportedException(
                "Direct offline listing of split SWM is not enabled. Compare its metadata instead.")
        End If
        Dim imagePath = Path.GetFullPath(report.ImageFile)
        Dim original = New FileInfo(imagePath)
        If Not original.Exists Then Throw New FileNotFoundException("WIM file was not found.", imagePath)
        If original.Length <> report.FileSizeBytes OrElse
           original.LastWriteTimeUtc <> report.ModifiedUtc Then
            Throw New InvalidOperationException(
                "The image changed after Inspect. Inspect the image again before comparing.")
        End If
        ' Confirm that all selected indexes still exist before listing a potentially
        ' very large image; never compare stale or incomplete per-index data.
        Dim current = Wim360Reader.Inspect(imagePath)
        If current.ImageCount <> report.ImageCount OrElse
           current.Images.Any(Function(x) Not report.Images.Any(
               Function(old) old.Index = x.Index AndAlso
                              old.Name = x.Name)) Then
            Throw New InvalidOperationException("The WIM index catalog changed. Inspect again.")
        End If
        Dim records As New List(Of Wim360IndexComparer.Item)()
        For Each image In chosen
            token.ThrowIfCancellationRequested()
            If progress IsNot Nothing Then
                progress.Report("Listing index " & image.Index.ToString() &
                                " / " & chosen.Count.ToString() & " without mounting...")
            End If
            records.AddRange(ListIndex(imagePath, image.Index, token, progress))
            If records.Count > MaxEntriesTotal Then
                Throw New InvalidDataException(
                    "Combined catalog exceeds " & MaxEntriesTotal.ToString() &
                    " entries. No incomplete comparison was shown.")
            End If
        Next
        original.Refresh()
        If original.Length <> report.FileSizeBytes OrElse
           original.LastWriteTimeUtc <> report.ModifiedUtc Then
            Throw New IOException("The source WIM changed during enumeration. Results discarded.")
        End If
        Dim result = Wim360IndexComparer.CompareRecords(imagePath,
                    "Offline file/folder presence (no mount)",
                    chosen.Select(Function(x) x.Index), records)
        result.Notes = "Read from the source WIM without mounting. " &
            "Presence/absence only; no file sizes or SHA-256 hashes. " &
            "Files with identical names may have different contents."
        Return result
    End Function
End Class
