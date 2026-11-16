Imports System
Imports System.Diagnostics
Imports System.Globalization
Imports System.IO
Imports System.Linq
Imports System.Security.Cryptography
Imports System.Threading
Imports System.Threading.Tasks

' The Operation Center's ONLY unattended DISM writer exports one selected
' index to a unique NEW WIM file. Never mount, Commit, Discard, append,
' replace a WIM, inject a driver or service an online image here.
Public NotInheritable Class OperationExport
    Private Sub New()
    End Sub

    Public NotInheritable Class ExportPreflight
        Public Property Source As String = ""
        Public Property Output As String = ""
        Public Property Index As Integer
        Public Property RequiredBytes As Long
        Public Property AvailableBytes As Long
        Public Property SourceBytes As Long
    End Class

    Private Shared Function SafeDestination(destination As String) As String
        If String.IsNullOrWhiteSpace(destination) OrElse
           Not Path.IsPathFullyQualified(destination) Then
            Throw New ArgumentException("Choose an absolute NEW .wim output file.")
        End If
        Dim full = Path.GetFullPath(destination.Trim().Trim(""""c))
        If Not String.Equals(Path.GetExtension(full), ".wim",
                             StringComparison.OrdinalIgnoreCase) Then
            Throw New ArgumentException("Export destination must end in .wim.")
        End If
        If full.StartsWith("\\", StringComparison.Ordinal) Then
            Throw New IOException(
                "Use a local fixed disk for export output; UNC destinations are not supported.")
        End If
        Dim destinationFolder = Path.GetDirectoryName(full)
        If Not IO.Directory.Exists(destinationFolder) Then
            Throw New DirectoryNotFoundException("Create the output directory first.")
        End If
        Dim volume As New DriveInfo(Path.GetPathRoot(full))
        If Not volume.IsReady OrElse volume.DriveType <> DriveType.Fixed Then
            Throw New IOException("Export output requires an available fixed disk.")
        End If
        Dim parent = destinationFolder
        While Not String.IsNullOrWhiteSpace(parent)
            If (File.GetAttributes(parent) And FileAttributes.ReparsePoint) <>
                CType(0, FileAttributes) Then
                Throw New IOException(
                    "Linked or redirected export destination folders are not allowed.")
            End If
            Dim nextParent = Path.GetDirectoryName(parent.TrimEnd(
                Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
            If String.IsNullOrWhiteSpace(nextParent) OrElse
               String.Equals(nextParent, parent, StringComparison.OrdinalIgnoreCase) Then
                Exit While
            End If
            parent = nextParent
        End While
        If File.Exists(full) OrElse IO.Directory.Exists(full) Then
            Throw New IOException("Destination already exists; no WIM will be overwritten.")
        End If
        Return full
    End Function

    Public Shared Function Check(source As String, index As Integer,
                                 destination As String) As ExportPreflight
        If String.IsNullOrWhiteSpace(source) OrElse
           Not Path.IsPathFullyQualified(source) OrElse index <= 0 Then
            Throw New ArgumentException("A valid absolute WIM/ESD and index are required.")
        End If
        Dim imagePath = Path.GetFullPath(source.Trim().Trim(""""c))
        If Not {".wim", ".esd"}.Contains(Path.GetExtension(imagePath),
                                        StringComparer.OrdinalIgnoreCase) Then
            Throw New ArgumentException("Only normal WIM/ESD containers can be exported.")
        End If
        Dim output = SafeDestination(destination)
        If String.Equals(output, imagePath, StringComparison.OrdinalIgnoreCase) Then
            Throw New IOException("Source and destination cannot be the same file.")
        End If
        Dim metadata = Wim360Reader.Inspect(imagePath)
        If metadata.IsSplitSet OrElse
           Not String.IsNullOrWhiteSpace(metadata.MountScanWarning) Then
            Throw New InvalidOperationException(
                "Source mount status cannot be independently verified.")
        End If
        If metadata.Images.Any(Function(x)
                Return Not String.Equals(x.MountStatus, "Not mounted",
                                         StringComparison.Ordinal)
            End Function) Then
            Throw New InvalidOperationException(
                "Source is mounted, invalid or needs remount. Resolve it before exporting.")
        End If
        Dim image = metadata.Images.FirstOrDefault(Function(x) x.Index = index)
        If image Is Nothing Then
            Throw New InvalidOperationException("The selected source index was not found.")
        End If
        If metadata.FileSizeBytes <= 0 OrElse image.UncompressedBytes <= 0 Then
            Throw New InvalidDataException("Unverified source image size.")
        End If
        Dim capacity = Wim360ExportCapacity.CheckDestination(
            Path.GetDirectoryName(output), metadata.FileSizeBytes,
            image.UncompressedBytes)
        Return New ExportPreflight With {
            .Source = imagePath,
            .Output = output,
            .Index = index,
            .RequiredBytes = capacity.RequiredBytes,
            .AvailableBytes = capacity.AvailableBytes,
            .SourceBytes = metadata.FileSizeBytes
        }
    End Function

    Private Shared Function SourceDigest(file As String, token As CancellationToken) As String
        Using input As New FileStream(file, FileMode.Open, FileAccess.Read,
                                      FileShare.Read, 4 * 1024 * 1024,
                                      FileOptions.SequentialScan)
            Using hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256)
                Dim buffer(4 * 1024 * 1024 - 1) As Byte
                While True
                    token.ThrowIfCancellationRequested()
                    Dim read = input.Read(buffer, 0, buffer.Length)
                    If read <= 0 Then Exit While
                    hash.AppendData(buffer, 0, read)
                End While
                Return Convert.ToHexString(hash.GetHashAndReset())
            End Using
        End Using
    End Function

    Public Shared Async Function ExportAsync(source As String, index As Integer,
                                            destination As String, token As CancellationToken
                                            ) As Task(Of String)
        token.ThrowIfCancellationRequested()
        Dim plan = Check(source, index, destination)
        Using writer = WimImageOperationLease.Acquire(
            plan.Source, plan.Output, WimImageOperationLease.WriterResource)
            plan = Check(source, index, destination)
            Dim digestBefore = Await Task.Run(
                Function() SourceDigest(plan.Source, token), token).ConfigureAwait(False)
            token.ThrowIfCancellationRequested()
            plan = Check(source, index, destination)
            Dim folder = Path.GetDirectoryName(plan.Output)
            Dim work = Path.Combine(folder,
                "." & Path.GetFileNameWithoutExtension(plan.Output) & "." &
                Guid.NewGuid().ToString("N") & ".partial.wim")
            Dim arguments As New ProcessStartInfo("dism.exe") With {
                .UseShellExecute = False,
                .CreateNoWindow = True,
                .RedirectStandardOutput = True,
                .RedirectStandardError = True
            }
            arguments.ArgumentList.Add("/English")
            arguments.ArgumentList.Add("/Export-Image")
            arguments.ArgumentList.Add("/SourceImageFile:" & plan.Source)
            arguments.ArgumentList.Add("/SourceIndex:" &
                                       plan.Index.ToString(CultureInfo.InvariantCulture))
            arguments.ArgumentList.Add("/DestinationImageFile:" & work)
            arguments.ArgumentList.Add("/CheckIntegrity")
            Dim exitOk = False
            Try
                Using process As New Process With {.StartInfo = arguments}
                    token.ThrowIfCancellationRequested()
                    If File.Exists(work) OrElse File.Exists(plan.Output) Then
                        Throw New IOException("An output file appeared before DISM started.")
                    End If
                    If Not process.Start() Then
                        Throw New IOException("DISM export did not start.")
                    End If
                    Dim stdout = process.StandardOutput.ReadToEndAsync()
                    Dim stderr = process.StandardError.ReadToEndAsync()
                    Try
                        Await process.WaitForExitAsync(token).ConfigureAwait(False)
                    Catch
                        If Not process.HasExited Then
                            Try
                                process.Kill(entireProcessTree:=True)
                                process.WaitForExit(10000)
                            Catch
                                ' The partial output is retained for administrator review.
                            End Try
                        End If
                        Throw
                    End Try
                    Dim outputText = (Await stdout.ConfigureAwait(False)) & vbLf &
                                     (Await stderr.ConfigureAwait(False))
                    exitOk = process.ExitCode = 0
                    If Not exitOk Then
                        Throw New InvalidOperationException(
                            "DISM export returned exit " &
                            process.ExitCode.ToString(CultureInfo.InvariantCulture) &
                            ". Temporary WIM retained for review: " & work & ". " &
                            outputText.Substring(Math.Max(0, outputText.Length - 700)))
                    End If
                End Using

                token.ThrowIfCancellationRequested()
                Dim digestAfter = Await Task.Run(
                    Function() SourceDigest(plan.Source, token), token).ConfigureAwait(False)
                If Not String.Equals(digestBefore, digestAfter, StringComparison.Ordinal) Then
                    Throw New InvalidDataException(
                        "Source WIM SHA-256 changed during export; output is not published.")
                End If
                Dim verified = Wim360Reader.Inspect(work)
                If verified.IsSplitSet OrElse verified.ImageCount <> 1 OrElse
                   verified.Images(0).Index <> 1 Then
                    Throw New InvalidDataException(
                        "Exported image did not pass WIMGAPI one-index postflight.")
                End If
                If File.Exists(plan.Output) Then
                    Throw New IOException("Destination appeared during DISM; refusing overwrite.")
                End If
                File.Move(work, plan.Output, False)
                Return "New WIM: " & plan.Output & "; source SHA-256 unchanged; " &
                       "size " & New FileInfo(plan.Output).Length.ToString(
                           CultureInfo.InvariantCulture) & " bytes."
            Catch
                ' Never delete an uncertain .partial.wim or overwrite any image.
                ' An interrupted DISM requires manual review of the temporary WIM.
                Throw
            End Try
        End Using
    End Function
End Class
