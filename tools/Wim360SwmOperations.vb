Imports System
Imports System.Collections.Generic
Imports System.Diagnostics
Imports System.Globalization
Imports System.IO
Imports System.Linq
Imports System.Security.Cryptography
Imports System.Text
Imports System.Threading
Imports System.Threading.Tasks

' Split WIM containers are IMMUTABLE inputs. This module checks every part
' and can export exactly one selected index into a NEW standalone WIM.
' It never changes, mounts, commits or removes an existing SWM part.
Public NotInheritable Class Wim360SwmOperations
    Private Sub New()
    End Sub

    Public NotInheritable Class PartDigest
        Public Property FullPath As String = ""
        Public Property Length As Long
        Public Property ModifiedUtc As DateTime
        Public Property Sha256 As String = ""
    End Class

    Public NotInheritable Class AuditResult
        Public Property MainFile As String = ""
        Public Property Identity As Guid
        Public Property Parts As New List(Of PartDigest)()
        Public Property Images As New List(Of Integer)()
        Public Property CompletedUtc As DateTime
        Public Property Verified As Boolean
        Public Property CombinedSha256 As String = ""
    End Class

    Public NotInheritable Class ExportResult
        Public Property OutputFile As String = ""
        Public Property OutputIndex As Integer = 1
        Public Property OutputBytes As Long
        Public Property SourceIndex As Integer
        Public Property SourceFilesUnchanged As Boolean
    End Class

    Private Shared Function Sha256File(filename As String, token As CancellationToken) As String
        token.ThrowIfCancellationRequested()
        Using stream As New FileStream(filename, FileMode.Open, FileAccess.Read,
                                      FileShare.Read, 4 * 1024 * 1024,
                                      FileOptions.SequentialScan)
            Using hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256)
                Dim buffer(4 * 1024 * 1024 - 1) As Byte
                While True
                    token.ThrowIfCancellationRequested()
                    Dim count = stream.Read(buffer, 0, buffer.Length)
                    If count = 0 Then Exit While
                    hasher.AppendData(buffer, 0, count)
                End While
                token.ThrowIfCancellationRequested()
                Return Convert.ToHexString(hasher.GetHashAndReset())
            End Using
        End Using
    End Function

    Private Shared Function EqualManifest(left As Wim360SplitSet.Manifest,
                                           right As Wim360SplitSet.Manifest) As Boolean
        Return left.Identity = right.Identity AndAlso
            left.PartCount = right.PartCount AndAlso
            left.TotalBytes = right.TotalBytes AndAlso
            left.Parts.SequenceEqual(right.Parts, StringComparer.OrdinalIgnoreCase)
    End Function

    Private Shared Sub ValidateExactWildcard(manifest As Wim360SplitSet.Manifest)
        Dim filename = Path.GetFileNameWithoutExtension(manifest.MainFile)
        Dim dir = Path.GetDirectoryName(manifest.MainFile)
        ' DISM /SWMFile accepts a wildcard; reject every surprising match
        ' instead of silently including an unrelated split WIM in the export.
        Dim allowed As New HashSet(Of String)(manifest.Parts,
                                              StringComparer.OrdinalIgnoreCase)
        Dim actual = Directory.EnumerateFiles(dir, filename & "*.swm").ToList()
        If actual.Count <> allowed.Count OrElse
           actual.Any(Function(x) Not allowed.Contains(Path.GetFullPath(x))) Then
            Throw New InvalidDataException(
                "The SWM wildcard also matches unexpected files. " &
                "Move unrelated .swm files away before attempting export.")
        End If
    End Sub

    Private Shared Function Revalidate(audit As AuditResult) As Wim360SplitSet.Manifest
        If audit Is Nothing OrElse Not audit.Verified OrElse
           audit.Parts Is Nothing OrElse audit.Parts.Count < 2 Then
            Throw New InvalidOperationException(
                "Complete a successful full SWM SHA-256 audit before exporting.")
        End If
        Dim manifest = Wim360SplitSet.Discover(audit.MainFile)
        If manifest.Identity <> audit.Identity OrElse
           manifest.PartCount <> audit.Parts.Count OrElse
           Not manifest.Parts.SequenceEqual(audit.Parts.Select(Function(x) x.FullPath),
                                            StringComparer.OrdinalIgnoreCase) Then
            Throw New InvalidOperationException(
                "SWM set changed after the integrity audit; inspect and audit again.")
        End If
        ValidateExactWildcard(manifest)
        Return manifest
    End Function

    Private Shared Function ReadPart(filename As String,
                                    token As CancellationToken) As PartDigest
        Dim before = New FileInfo(filename)
        If Not before.Exists Then Throw New FileNotFoundException("SWM part missing.", filename)
        Dim priorLength = before.Length
        Dim priorTime = before.LastWriteTimeUtc
        If (before.Attributes And FileAttributes.ReparsePoint) = FileAttributes.ReparsePoint Then
            Throw New InvalidDataException("SWM symlinks or junctions are not supported.")
        End If
        Dim fingerprint = Sha256File(filename, token)
        before.Refresh()
        If before.Length <> priorLength OrElse before.LastWriteTimeUtc <> priorTime Then
            Throw New IOException("SWM part changed while calculating SHA-256: " &
                                  Path.GetFileName(filename))
        End If
        Return New PartDigest With {
            .FullPath = filename,
            .Length = priorLength,
            .ModifiedUtc = priorTime,
            .Sha256 = fingerprint
        }
    End Function

    Private Shared Function MatchDigest(actual As PartDigest,
                                        expected As PartDigest) As Boolean
        Return String.Equals(actual.FullPath, expected.FullPath,
                             StringComparison.OrdinalIgnoreCase) AndAlso
            actual.Length = expected.Length AndAlso
            actual.ModifiedUtc = expected.ModifiedUtc AndAlso
            String.Equals(actual.Sha256, expected.Sha256, StringComparison.Ordinal)
    End Function

    Private Shared Function CombinedFingerprint(parts As IEnumerable(Of PartDigest)) As String
        Dim canonical = String.Join(vbLf, parts.Select(Function(x)
            Return Path.GetFileName(x.FullPath).ToUpperInvariant() & "|" &
                x.Length.ToString(CultureInfo.InvariantCulture) & "|" & x.Sha256
        End Function))
        Return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)))
    End Function

    Public Shared Function Audit(firstPart As String,
                                 token As CancellationToken,
                                 Optional progress As IProgress(Of String) = Nothing
                                 ) As AuditResult
        token.ThrowIfCancellationRequested()
        Dim manifest = Wim360SplitSet.Discover(firstPart)
        Dim sources = manifest.Parts.ToArray()
        Using lease = WimImageOperationLease.Acquire(sources)
            Dim before = Wim360SplitSet.Discover(manifest.MainFile)
            If Not EqualManifest(manifest, before) Then
                Throw New IOException("SWM part headers changed during inspection.")
            End If
            ValidateExactWildcard(manifest)
            Dim result As New AuditResult With {
                .MainFile = manifest.MainFile, .Identity = manifest.Identity
            }
            Dim index As Integer = 0
            For Each filename In sources
                token.ThrowIfCancellationRequested()
                index += 1
                If progress IsNot Nothing Then
                    progress.Report("SHA-256 " & index.ToString() & "/" &
                                    sources.Length.ToString() & ": " &
                                    Path.GetFileName(filename))
                End If
                result.Parts.Add(ReadPart(filename, token))
            Next
            token.ThrowIfCancellationRequested()
            Dim after = Wim360SplitSet.Discover(manifest.MainFile)
            If Not EqualManifest(manifest, after) Then
                Throw New IOException("SWM headers/part sizes changed during verification.")
            End If
            For Each digest In result.Parts
                Dim info = New FileInfo(digest.FullPath)
                If Not info.Exists OrElse info.Length <> digest.Length OrElse
                   info.LastWriteTimeUtc <> digest.ModifiedUtc Then
                    Throw New IOException("SWM source changed during verification.")
                End If
            Next
            Dim image = Wim360Reader.Inspect(manifest.MainFile)
            If Not image.IsSplitSet OrElse image.SplitSet.Identity <> manifest.Identity Then
                Throw New InvalidDataException("SWM image metadata changed during audit.")
            End If
            result.Images = image.Images.Select(Function(x) x.Index).ToList()
            result.CombinedSha256 = CombinedFingerprint(result.Parts)
            result.CompletedUtc = DateTime.UtcNow
            result.Verified = True
            Return result
        End Using
    End Function

    Private Shared Sub VerifyAuditUnchanged(audit As AuditResult,
                                            token As CancellationToken,
                                            Optional progress As IProgress(Of String) = Nothing)
        Dim manifest = Revalidate(audit)
        For i As Integer = 0 To manifest.Parts.Count - 1
            token.ThrowIfCancellationRequested()
            If progress IsNot Nothing Then
                progress.Report("Rechecking SWM " & (i + 1).ToString() &
                                "/" & manifest.PartCount.ToString())
            End If
            If Not MatchDigest(ReadPart(manifest.Parts(i), token), audit.Parts(i)) Then
                Throw New IOException(
                    "SWM part contents changed after the audit; export canceled.")
            End If
        Next
        Dim after = Wim360SplitSet.Discover(audit.MainFile)
        If Not EqualManifest(manifest, after) Then
            Throw New IOException("SWM part membership changed during verification.")
        End If
    End Sub

    Public Shared Async Function ExportNewWimAsync(audit As AuditResult,
                                                    sourceIndex As Integer,
                                                    outputFile As String,
                                                    token As CancellationToken,
                                                    Optional progress As IProgress(Of String) = Nothing
                                                    ) As Task(Of ExportResult)
        token.ThrowIfCancellationRequested()
        Dim manifest = Revalidate(audit)
        If Not audit.Images.Contains(sourceIndex) Then
            Throw New ArgumentOutOfRangeException(NameOf(sourceIndex),
                "Choose an image index from the verified SWM source.")
        End If
        If String.IsNullOrWhiteSpace(outputFile) Then
            Throw New ArgumentException("Choose a destination for the NEW .wim.")
        End If
        Dim dest = Path.GetFullPath(outputFile.Trim().Trim(""""c))
        If Not String.Equals(Path.GetExtension(dest), ".wim",
                             StringComparison.OrdinalIgnoreCase) Then
            Throw New ArgumentException("Destination must be a standalone .wim file.")
        End If
        Dim directory = Path.GetDirectoryName(dest)
        If Not Directory.Exists(directory) Then
            Throw New DirectoryNotFoundException("Destination directory does not exist.")
        End If
        If File.Exists(dest) Then
            Throw New IOException("Destination already exists; refusing to overwrite a WIM.")
        End If
        Dim partial = Path.Combine(directory,
            "." & Path.GetFileNameWithoutExtension(dest) & "." &
            Guid.NewGuid().ToString("N") & ".partial.wim")
        If Path.GetFullPath(partial).Equals(dest, StringComparison.OrdinalIgnoreCase) Then
            Throw New InvalidOperationException("Temporary destination conflicts with output.")
        End If
        Dim resources = manifest.Parts.Concat(New String() {dest}).ToArray()
        Using lease = WimImageOperationLease.Acquire(resources)
            Revalidate(audit)
            If File.Exists(dest) Then Throw New IOException("Destination appeared during preflight.")
            VerifyAuditUnchanged(audit, token, progress)
            Dim start As New ProcessStartInfo("dism.exe") With {
                .UseShellExecute = False,
                .CreateNoWindow = True,
                .RedirectStandardOutput = True,
                .RedirectStandardError = True
            }
            start.ArgumentList.Add("/English")
            start.ArgumentList.Add("/Export-Image")
            start.ArgumentList.Add("/SourceImageFile:" & manifest.MainFile)
            start.ArgumentList.Add("/SWMFile:" & Path.Combine(
                Path.GetDirectoryName(manifest.MainFile),
                Path.GetFileNameWithoutExtension(manifest.MainFile) & "*.swm"))
            start.ArgumentList.Add("/SourceIndex:" &
                sourceIndex.ToString(CultureInfo.InvariantCulture))
            start.ArgumentList.Add("/DestinationImageFile:" & partial)
            start.ArgumentList.Add("/CheckIntegrity")
            Dim processExited As Boolean = False
            Try
                If progress IsNot Nothing Then progress.Report("DISM exporting into a new temporary WIM...")
                Using process As New Process With {.StartInfo = start}
                    If Not process.Start() Then
                        Throw New IOException("DISM export could not be started.")
                    End If
                    Dim stdout = process.StandardOutput.ReadToEndAsync()
                    Dim stderr = process.StandardError.ReadToEndAsync()
                    Try
                        Await process.WaitForExitAsync(token).ConfigureAwait(False)
                        processExited = True
                    Catch
                        If Not process.HasExited Then
                            Try
                                process.Kill(entireProcessTree:=True)
                                Await process.WaitForExitAsync().ConfigureAwait(False)
                                processExited = True
                            Catch
                                ' Keep the partial file when DISM termination was not confirmed.
                            End Try
                        Else
                            processExited = True
                        End If
                        Throw
                    End Try
                    Dim text = (Await stdout.ConfigureAwait(False)) & vbLf &
                               (Await stderr.ConfigureAwait(False))
                    If process.ExitCode <> 0 Then
                        Throw New InvalidOperationException(
                            "DISM export failed (exit " &
                            process.ExitCode.ToString(CultureInfo.InvariantCulture) &
                            "). Inspect DISM logs. " &
                            text.Substring(Math.Max(0, text.Length - 1200)))
                    End If
                End Using
                token.ThrowIfCancellationRequested()
                VerifyAuditUnchanged(audit, token, progress)
                Dim exported = Wim360Reader.Inspect(partial)
                If exported.IsSplitSet OrElse exported.ImageCount <> 1 OrElse
                   Not exported.Images.Any(Function(x) x.Index = 1) Then
                    Throw New InvalidDataException("DISM output is not a valid one-index WIM.")
                End If
                If File.Exists(dest) Then
                    Throw New IOException("Output filename was taken during export.")
                End If
                File.Move(partial, dest, False)
                Return New ExportResult With {
                    .OutputFile = dest, .OutputIndex = 1,
                    .OutputBytes = New FileInfo(dest).Length,
                    .SourceIndex = sourceIndex,
                    .SourceFilesUnchanged = True
                }
            Finally
                ' Delete ONLY a uniquely named file created by this invocation.
                ' Never remove an uncertain partial while DISM may still own it.
                If processExited AndAlso File.Exists(partial) Then
                    Try
                        File.Delete(partial)
                    Catch
                    End Try
                End If
            End Try
        End Using
    End Function
End Class
