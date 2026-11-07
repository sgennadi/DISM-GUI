Imports System
Imports System.Collections.Generic
Imports System.Diagnostics
Imports System.Globalization
Imports System.IO
Imports System.Linq
Imports System.Security.Cryptography
Imports System.Text
Imports System.Threading

' Read-only source-package audit. DISM remains responsible for validating
' catalog membership / image servicing; a valid catalog signature alone does
' NOT prove every payload is signed or compatible with every device.
Public NotInheritable Class DriverStudioPackageAudit
    Private Sub New()
    End Sub

    Private Const MaxFiles As Integer = 1600
    Private Const MaxBytes As Long = 2147483648L

    Public NotInheritable Class AuditResult
        Public Property InfPath As String = ""
        Public Property Root As String = ""
        Public Property SourceInfHash As String = ""
        Public Property ManifestSha256 As String = ""
        Public Property FileCount As Integer
        Public Property TotalBytes As Long
        Public Property TargetArchitecture As String = ""
        Public Property CatalogName As String = ""
        Public Property CatalogStatus As String = "Not checked"
        Public Property Warnings As New List(Of String)()

        Public ReadOnly Property CanInstall As Boolean
            Get
                Return Warnings.Count = 0 AndAlso CatalogStatus = "Valid" AndAlso
                    ManifestSha256.Length = 64 AndAlso FileCount > 0
            End Get
        End Property

        Public ReadOnly Property Summary As String
            Get
                Return "Package audit: " & FileCount.ToString("N0") &
                    " files / " & TotalBytes.ToString("N0") &
                    " bytes" & Environment.NewLine &
                    "Manifest SHA-256: " & ManifestSha256 & Environment.NewLine &
                    "Target image architecture: " & If(TargetArchitecture = "", "unknown", TargetArchitecture) &
                    Environment.NewLine &
                    "Catalog: " & If(CatalogName = "", "not declared", CatalogName) &
                    " | Authenticode: " & CatalogStatus &
                    If(Warnings.Count = 0, "", Environment.NewLine &
                       "Review: " & String.Join("; ", Warnings)) &
                    Environment.NewLine &
                    "Authenticode validates the catalog signature, not every payload " &
                    "or PnP ranking. Windows DISM validates the actual driver package."
            End Get
        End Property
    End Class

    Private Shared Function ArchitectureKey(value As String) As String
        Dim v = If(value, "").Trim().ToUpperInvariant()
        If v.Contains("ARM64") Then Return "ARM64"
        If v.Contains("AMD64") OrElse v.Contains("X64") OrElse v = "9" Then Return "AMD64"
        If v.Contains("X86") OrElse v = "0" Then Return "X86"
        Return ""
    End Function

    Public Shared Function ChooseCatalog(package As DriverStudioEngine.DriverPackage,
                                         targetArchitecture As String) As String
        If package Is Nothing Then Throw New ArgumentNullException(NameOf(package))
        Dim arch = ArchitectureKey(targetArchitecture)
        Dim matches = package.CatalogFiles.OrderBy(Function(e) e.Key,
                                       StringComparer.OrdinalIgnoreCase).ToList()
        Dim choice As String = Nothing
        If arch <> "" Then
            Dim decorated = matches.FirstOrDefault(Function(e)
                Return e.Key.StartsWith("CatalogFile.NT" & arch,
                                        StringComparison.OrdinalIgnoreCase) AndAlso
                       (e.Key.Length = ("CatalogFile.NT" & arch).Length OrElse
                        e.Key(("CatalogFile.NT" & arch).Length) = "."c)
            End Function)
            If Not String.IsNullOrWhiteSpace(decorated.Key) Then choice = decorated.Value
        End If
        If String.IsNullOrWhiteSpace(choice) Then
            Dim generic = matches.FirstOrDefault(Function(e)
                Return e.Key.Equals("CatalogFile", StringComparison.OrdinalIgnoreCase)
            End Function)
            If Not String.IsNullOrWhiteSpace(generic.Key) Then choice = generic.Value
        End If
        ' Do not take an ARM64 catalog as a fallback for an AMD64 image.
        If String.IsNullOrWhiteSpace(choice) AndAlso arch = "" AndAlso matches.Count = 1 Then
            choice = matches(0).Value
        End If
        If String.IsNullOrWhiteSpace(choice) Then Return ""
        choice = choice.Trim().Trim(""""c)
        If Path.IsPathRooted(choice) OrElse
           choice.Contains("\"c) OrElse choice.Contains("/"c) OrElse
           choice = "." OrElse choice = ".." OrElse
           Not choice.EndsWith(".cat", StringComparison.OrdinalIgnoreCase) OrElse
           choice.IndexOf(ChrW(0)) >= 0 Then
            Return ""
        End If
        Return choice
    End Function

    ' CAT status is checked only on explicit audit, using a native installed
    ' PowerShell executable. On some Windows Server images the legacy Windows
    ' PowerShell Security module cannot load even though PowerShell 7 verifies
    ' the exact same CAT correctly. Prefer PS7 if installed, then legacy.
    ' A non-Valid signature is NEVER promoted to Valid by a fallback.
    Private Shared Function CatalogSignature(catalogPath As String) As String
        Dim programFiles = Environment.GetEnvironmentVariable("ProgramW6432")
        If String.IsNullOrWhiteSpace(programFiles) Then
            programFiles = Environment.GetFolderPath(
                Environment.SpecialFolder.ProgramFiles)
        End If
        Dim core = Path.Combine(programFiles, "PowerShell", "7", "pwsh.exe")
        Dim legacy = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            If(Environment.Is64BitOperatingSystem AndAlso Not Environment.Is64BitProcess,
               "Sysnative", "System32"),
            "WindowsPowerShell", "v1.0", "powershell.exe")
        Dim executables As New List(Of String)()
        If File.Exists(core) Then executables.Add(core)
        If File.Exists(legacy) Then executables.Add(legacy)
        If executables.Count = 0 Then Return "Unavailable (PowerShell not installed)"

        Dim script = "$ErrorActionPreference='Stop'; " &
                     "(Get-AuthenticodeSignature -LiteralPath '" &
                     catalogPath.Replace("'", "''") & "').Status.ToString()"
        Dim encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(script))
        For Each executable In executables
            Dim psi As New ProcessStartInfo With {
                .FileName = executable, .CreateNoWindow = True,
                .UseShellExecute = False,
                .RedirectStandardOutput = True,
                .RedirectStandardError = True
            }
            For Each arg As String In {"-NoProfile", "-NonInteractive",
                                      "-EncodedCommand", encoded}
                psi.ArgumentList.Add(arg)
            Next
            Try
                Using process As New Process With {.StartInfo = psi}
                    If Not process.Start() Then Continue For
                    Dim output = process.StandardOutput.ReadToEndAsync()
                    Dim errorTask = process.StandardError.ReadToEndAsync()
                    If Not process.WaitForExit(20000) Then
                        process.Kill(True)
                        process.WaitForExit(5000)
                        Continue For
                    End If
                    Dim value = output.GetAwaiter().GetResult().Trim()
                    Dim ignoredError = errorTask.GetAwaiter().GetResult()
                    If process.ExitCode <> 0 OrElse value.Length = 0 Then Continue For
                    If String.Equals(value, "Valid", StringComparison.OrdinalIgnoreCase) Then
                        Return "Valid"
                    End If
                    ' Return a real failure status without asking another
                    ' PowerShell engine to override a cryptographic rejection.
                    Return value.Substring(0, Math.Min(100, value.Length))
                End Using
            Catch ex As Exception When TypeOf ex Is IOException OrElse
                                       TypeOf ex Is InvalidOperationException OrElse
                                       TypeOf ex Is System.ComponentModel.Win32Exception
                ' A missing/broken PowerShell module is not an invalid CAT.
                ' Try the next installed engine; fail closed if none work.
            End Try
        Next
        Return "Unavailable (signature check failed in installed PowerShell engines)"
    End Function

    Public Shared Function Audit(package As DriverStudioEngine.DriverPackage,
                                 targetArchitecture As String,
                                 Optional checkSignature As Boolean = True,
                                 Optional token As CancellationToken = Nothing) As AuditResult
        If package Is Nothing Then Throw New ArgumentNullException(NameOf(package))
        If String.IsNullOrWhiteSpace(package.FilePath) OrElse
           Not package.FilePath.EndsWith(".inf", StringComparison.OrdinalIgnoreCase) Then
            Throw New InvalidDataException("Choose a real INF source for auditing.")
        End If
        Dim inf = Path.GetFullPath(package.FilePath)
        Dim root = Path.GetDirectoryName(inf)
        If String.IsNullOrWhiteSpace(root) OrElse Not File.Exists(inf) Then
            Throw New FileNotFoundException("INF source is no longer available.", inf)
        End If
        If String.IsNullOrWhiteSpace(package.Sha256) OrElse
           Not String.IsNullOrWhiteSpace(package.ValidationError) Then
            Throw New InvalidDataException("Run Scan INF and resolve metadata warnings first.")
        End If
        Dim result As New AuditResult With {
            .InfPath = inf, .Root = root, .SourceInfHash = package.Sha256,
            .TargetArchitecture = If(targetArchitecture, ""),
            .CatalogName = ChooseCatalog(package, targetArchitecture)
        }
        If DriverStudioEngine.ArchitectureMismatch(package, targetArchitecture) Then
            result.Warnings.Add("INF architecture differs from selected image")
        End If
        If String.IsNullOrEmpty(result.CatalogName) Then
            result.Warnings.Add("No applicable CatalogFile declared for target architecture")
        ElseIf Not File.Exists(Path.Combine(root, result.CatalogName)) Then
            result.Warnings.Add("Declared catalog file is missing")
        End If
        If (File.GetAttributes(root) And FileAttributes.ReparsePoint) = FileAttributes.ReparsePoint Then
            Throw New InvalidDataException("A reparse-point package root is not permitted.")
        End If
        Dim parts As New List(Of String)()
        Dim seen As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)
        Dim pending As New Stack(Of String)()
        pending.Push(root)
        While pending.Count > 0
            token.ThrowIfCancellationRequested()
            Dim folder = pending.Pop()
            For Each entry In Directory.EnumerateFileSystemEntries(folder)
                token.ThrowIfCancellationRequested()
                Dim attributes = File.GetAttributes(entry)
                If (attributes And FileAttributes.ReparsePoint) = FileAttributes.ReparsePoint Then
                    Throw New InvalidDataException("Package includes a junction/symlink; audit aborted.")
                End If
                If (attributes And FileAttributes.Directory) = FileAttributes.Directory Then
                    pending.Push(entry)
                    Continue For
                End If
                Dim relative = Path.GetRelativePath(root, entry).Replace("/"c, "\"c)
                If Path.IsPathRooted(relative) OrElse relative = ".." OrElse
                   relative.StartsWith("..\", StringComparison.Ordinal) OrElse
                   Not seen.Add(relative) Then
                    Throw New InvalidDataException("Package path is ambiguous or leaves its source root.")
                End If
                Dim info As New FileInfo(entry)
                Dim bytes = info.Length
                If result.FileCount >= MaxFiles OrElse
                   bytes > MaxBytes - result.TotalBytes Then
                    Throw New InvalidDataException(
                        "Driver package exceeds 1,600 files / 2 GiB; select a smaller package folder.")
                End If
                Dim startTime = info.LastWriteTimeUtc
                Dim dataHash As Byte()
                Dim readTotal As Long = 0
                Using stream As New FileStream(entry, FileMode.Open, FileAccess.Read, FileShare.Read)
                    Using hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256)
                        Dim buffer(131071) As Byte
                        While True
                            token.ThrowIfCancellationRequested()
                            Dim count = stream.Read(buffer, 0, buffer.Length)
                            If count = 0 Then Exit While
                            hash.AppendData(buffer, 0, count)
                            readTotal += count
                        End While
                        dataHash = hash.GetHashAndReset()
                    End Using
                End Using
                info.Refresh()
                If readTotal <> bytes OrElse info.Length <> bytes OrElse
                   info.LastWriteTimeUtc <> startTime Then
                    Throw New InvalidDataException("A package file changed during auditing.")
                End If
                result.FileCount += 1
                result.TotalBytes += bytes
                parts.Add(relative & ChrW(0) & bytes.ToString(CultureInfo.InvariantCulture) &
                          ChrW(0) & Convert.ToHexString(dataHash))
            Next
        End While
        ' Stable sorted manifest includes file paths, lengths and all content hashes.
        parts.Sort(StringComparer.OrdinalIgnoreCase)
        Dim manifest = String.Join(vbLf, parts)
        result.ManifestSha256 = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(manifest)))
        Dim freshInf = DriverStudioEngine.ReadInf(inf)
        If Not String.Equals(freshInf.Sha256, package.Sha256,
                             StringComparison.OrdinalIgnoreCase) Then
            Throw New InvalidDataException("INF changed since Scan INF; rescan before importing.")
        End If
        If Not String.IsNullOrWhiteSpace(result.CatalogName) AndAlso
           File.Exists(Path.Combine(root, result.CatalogName)) Then
            result.CatalogStatus = If(checkSignature,
                CatalogSignature(Path.Combine(root, result.CatalogName)), "Not checked")
            If checkSignature AndAlso result.CatalogStatus <> "Valid" Then
                result.Warnings.Add("Catalog Authenticode signature is not verified")
            End If
        Else
            result.CatalogStatus = "Missing"
        End If
        Return result
    End Function

    ' A successful audit is a snapshot, not an ongoing lock on source files.
    ' Repeat immediately before every DISM invocation; abort on any change.
    Public Shared Function VerifyUnchanged(previous As AuditResult,
                           package As DriverStudioEngine.DriverPackage,
                           targetArchitecture As String,
                           Optional token As CancellationToken = Nothing) As AuditResult
        If previous Is Nothing OrElse Not previous.CanInstall Then
            Throw New InvalidOperationException(
                "No verified package audit is available. Audit selected INF first.")
        End If
        Dim current = Audit(package, targetArchitecture, True, token)
        If Not current.CanInstall OrElse
           Not String.Equals(previous.InfPath, current.InfPath,
                             StringComparison.OrdinalIgnoreCase) OrElse
           Not String.Equals(previous.CatalogName, current.CatalogName,
                             StringComparison.OrdinalIgnoreCase) OrElse
           Not String.Equals(previous.ManifestSha256, current.ManifestSha256,
                             StringComparison.OrdinalIgnoreCase) Then
            Throw New InvalidOperationException(
                "Driver package files, catalog signature or target changed. " &
                "Nothing was imported; rescan and audit the package again.")
        End If
        Return current
    End Function
End Class
