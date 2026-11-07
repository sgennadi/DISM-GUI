Imports System
Imports System.Collections.Generic
Imports System.ComponentModel
Imports System.Diagnostics
Imports System.IO
Imports System.Linq
Imports System.Security.Cryptography
Imports System.Security.Principal
Imports System.Text
Imports System.Threading.Tasks

' Explicit, per-INF import into an already-mounted read/write Windows image.
' Import never mounts, unmounts, commits, discards, recurses, or forces unsigned drivers.
Public NotInheritable Class DriverStudioInstaller
    Private Sub New()
    End Sub

    Public NotInheritable Class InstallResult
        Public Property InfName As String = ""
        Public Property ExitCode As Integer
        Public Property Succeeded As Boolean
        Public Property RebootRequired As Boolean
        Public Property Details As String = ""
    End Class

    Public Shared Function IsAllowed(d As DriverStudioEngine.Decision) As Boolean
        Return d IsNot Nothing AndAlso d.Candidate IsNot Nothing AndAlso
            String.IsNullOrWhiteSpace(d.Candidate.ValidationError) AndAlso
            (String.Equals(d.Status, "New", StringComparison.Ordinal) OrElse
             String.Equals(d.Status, "Newer", StringComparison.Ordinal))
    End Function

    ' Capture the ENTIRE mounted driver inventory as a stable, order-independent
    ' fingerprint. Per-INF status alone cannot detect a second driver being
    ' staged by another administrator between Compare and Install. Include
    ' duplicate entries; do not confuse matching count with matching content.
    Private Shared Function SnapshotField(value As String) As String
        Dim normalized = If(value, "").Trim().ToUpperInvariant()
        Return normalized.Length.ToString(Globalization.CultureInfo.InvariantCulture) &
            ":" & normalized
    End Function

    Public Shared Function SnapshotInstalledInventory(
        installed As IEnumerable(Of DriverStudioEngine.DriverPackage)) As String
        If installed Is Nothing Then Throw New ArgumentNullException(NameOf(installed))
        Dim entries As New List(Of String)()
        For Each p As DriverStudioEngine.DriverPackage In installed
            If p Is Nothing Then
                Throw New InvalidDataException("The installed-driver inventory contains an invalid entry.")
            End If
            entries.Add(String.Join("|", New String() {
                SnapshotField(p.InfName), SnapshotField(p.PublishedName),
                SnapshotField(p.Provider), SnapshotField(p.ClassName),
                SnapshotField(p.Version), SnapshotField(p.DriverDate),
                If(p.IsInbox, "1", "0")
            }))
        Next
        entries.Sort(StringComparer.Ordinal)
        Return Convert.ToHexString(SHA256.HashData(
            Encoding.UTF8.GetBytes(String.Join(vbLf, entries))))
    End Function

    Public Shared Sub RequireInventorySnapshot(expected As String,
        fresh As IEnumerable(Of DriverStudioEngine.DriverPackage))
        If String.IsNullOrWhiteSpace(expected) OrElse expected.Length <> 64 OrElse
           Not String.Equals(expected, SnapshotInstalledInventory(fresh),
                             StringComparison.OrdinalIgnoreCase) Then
            Throw New InvalidOperationException(
                "The mounted image driver inventory changed since comparison. " &
                "No drivers were imported. Compare and audit again before installing.")
        End If
    End Sub

    ' A rechecked decision can remain "Newer" even if the mounted image's
    ' existing driver has changed in the meantime (e.g. 1.0 -> 1.5 while
    ' incoming is 2.0). Compare the precise reviewed baseline as well.
    Private Shared Function SameInstalledSnapshot(
        reviewed As DriverStudioEngine.DriverPackage,
        refreshed As DriverStudioEngine.DriverPackage) As Boolean
        If reviewed Is Nothing OrElse refreshed Is Nothing Then
            Return reviewed Is Nothing AndAlso refreshed Is Nothing
        End If
        Return String.Equals(reviewed.InfName, refreshed.InfName,
                             StringComparison.OrdinalIgnoreCase) AndAlso
            String.Equals(reviewed.PublishedName, refreshed.PublishedName,
                          StringComparison.OrdinalIgnoreCase) AndAlso
            String.Equals(reviewed.Provider, refreshed.Provider,
                          StringComparison.OrdinalIgnoreCase) AndAlso
            String.Equals(reviewed.ClassName, refreshed.ClassName,
                          StringComparison.OrdinalIgnoreCase) AndAlso
            String.Equals(reviewed.Version, refreshed.Version,
                          StringComparison.OrdinalIgnoreCase) AndAlso
            String.Equals(reviewed.DriverDate, refreshed.DriverDate,
                          StringComparison.OrdinalIgnoreCase) AndAlso
            reviewed.IsInbox = refreshed.IsInbox
    End Function

    ' Pure preflight: the candidate list and inventory are freshly re-read by
    ' the caller. Source INF SHA-256, identity, date, version, installed-driver
    ' snapshot, target architecture and decision must all match the selection.
    Public Shared Function ValidatePlan(
        reviewed As IEnumerable(Of DriverStudioEngine.Decision),
        freshSource As IEnumerable(Of DriverStudioEngine.DriverPackage),
        freshInstalled As IEnumerable(Of DriverStudioEngine.DriverPackage),
        Optional targetArchitecture As String = "",
        Optional expectedInventorySnapshot As String = Nothing
    ) As List(Of DriverStudioEngine.DriverPackage)
        If reviewed Is Nothing OrElse freshSource Is Nothing OrElse
           freshInstalled Is Nothing Then
            Throw New ArgumentNullException(NameOf(reviewed))
        End If
        Dim selected = reviewed.Where(Function(d) d.Selected).ToList()
        If selected.Count = 0 Then Throw New InvalidOperationException(
            "Choose at least one New or Newer driver before importing.")
        If selected.Count > 200 Then Throw New InvalidOperationException(
            "A single import is limited to 200 INF packages; use smaller batches.")
        If selected.Any(Function(d) Not IsAllowed(d)) Then
            Throw New InvalidOperationException(
                "Only verified New / Newer drivers are eligible. Deselect Same, Older and Review entries.")
        End If

        Dim installedList = freshInstalled.ToList()
        If expectedInventorySnapshot IsNot Nothing Then
            RequireInventorySnapshot(expectedInventorySnapshot, installedList)
        End If
        Dim current = DriverStudioEngine.CompareCandidates(freshSource,
                                                            installedList, True,
                                                            targetArchitecture)
        Dim approved As New List(Of DriverStudioEngine.DriverPackage)()
        Dim seen As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)
        For Each reviewedDecision In selected
            Dim oldPkg = reviewedDecision.Candidate
            If String.IsNullOrWhiteSpace(oldPkg.FilePath) OrElse
               String.IsNullOrWhiteSpace(oldPkg.Sha256) Then
                Throw New InvalidOperationException("Unverified INF metadata; run comparison again.")
            End If
            Dim full = Path.GetFullPath(oldPkg.FilePath)
            If Not full.EndsWith(".inf", StringComparison.OrdinalIgnoreCase) OrElse
               Not seen.Add(full) Then
                Throw New InvalidOperationException("Duplicate or invalid INF path in selected plan.")
            End If
            Dim matching = current.Where(
                Function(d) String.Equals(Path.GetFullPath(d.Candidate.FilePath),
                                          full, StringComparison.OrdinalIgnoreCase)).ToList()
            If matching.Count <> 1 OrElse Not IsAllowed(matching(0)) OrElse
               Not String.Equals(matching(0).Status, reviewedDecision.Status,
                                 StringComparison.Ordinal) Then
                Throw New InvalidOperationException(
                    "Image inventory or driver decision changed for " &
                    oldPkg.InfName & ". Compare again before importing.")
            End If
            If Not SameInstalledSnapshot(reviewedDecision.Existing,
                                         matching(0).Existing) Then
                Throw New InvalidOperationException(
                    "The installed driver baseline changed since comparison: " &
                    oldPkg.InfName & ". Compare again before importing.")
            End If
            Dim freshPkg = matching(0).Candidate
            If Not String.Equals(oldPkg.Sha256, freshPkg.Sha256,
                                 StringComparison.OrdinalIgnoreCase) OrElse
               Not String.Equals(oldPkg.Provider, freshPkg.Provider,
                                 StringComparison.OrdinalIgnoreCase) OrElse
               Not String.Equals(oldPkg.ClassName, freshPkg.ClassName,
                                 StringComparison.OrdinalIgnoreCase) OrElse
               Not String.Equals(oldPkg.Version, freshPkg.Version,
                                 StringComparison.OrdinalIgnoreCase) OrElse
               Not String.Equals(oldPkg.DriverDate, freshPkg.DriverDate,
                                 StringComparison.OrdinalIgnoreCase) Then
                Throw New InvalidOperationException(
                    "INF source changed after comparison: " &
                    oldPkg.InfName & ". No drivers were imported.")
            End If
            approved.Add(freshPkg)
        Next
        Return approved
    End Function

    Public Shared Function RequireWritableTarget(wimFile As String,
                    index As Integer, mountFolder As String) As String
        Dim root = DriverStudioInventory.ValidateTarget(wimFile, index, mountFolder)
        Dim report = Wim360Reader.Inspect(wimFile)
        Dim chosen = report.Images.FirstOrDefault(Function(x) x.Index = index)
        If chosen Is Nothing OrElse
           Not String.Equals(chosen.MountStatus, "Mounted (R/W)",
                             StringComparison.OrdinalIgnoreCase) OrElse
           Not String.Equals(Path.GetFullPath(chosen.MountFolder).TrimEnd(
                 Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                 root, StringComparison.OrdinalIgnoreCase) Then
            Throw New InvalidOperationException(
                "The selected WIM index must be mounted in read/write mode. " &
                "Read-only or invalid mounts cannot receive driver packages.")
        End If
        Return root
    End Function

    Public Shared Function IsAdministrator() As Boolean
        Try
            Using identity = WindowsIdentity.GetCurrent()
                Return New WindowsPrincipal(identity).IsInRole(
                    WindowsBuiltInRole.Administrator)
            End Using
        Catch
            Return False
        End Try
    End Function

    Public Shared Function BuildArguments(mountFolder As String,
                                          driverInf As String) As List(Of String)
        If String.IsNullOrWhiteSpace(mountFolder) OrElse
           String.IsNullOrWhiteSpace(driverInf) Then
            Throw New ArgumentException("Mounted Windows folder and INF path are required.")
        End If
        Dim root = Path.GetFullPath(mountFolder).TrimEnd(
            Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
        Dim inf = Path.GetFullPath(driverInf)
        If Not inf.EndsWith(".inf", StringComparison.OrdinalIgnoreCase) Then
            Throw New ArgumentException("Only individual .inf files can be imported.")
        End If
        Return New List(Of String) From {
            "/Image:" & root,
            "/Add-Driver",
            "/Driver:" & inf
        }
    End Function

    ' Postflight is an inventory check, not proof that PnP has selected
    ' this driver for a physical device. DISM may stage unused drivers.
    Public Shared Function IsPresentInInventory(
                package As DriverStudioEngine.DriverPackage,
                inventory As IEnumerable(Of DriverStudioEngine.DriverPackage)) As Boolean
        If package Is Nothing OrElse inventory Is Nothing Then Return False
        Return inventory.Any(Function(p)
            Return String.Equals(Path.GetFileName(p.InfName), package.InfName,
                                 StringComparison.OrdinalIgnoreCase) AndAlso
                String.Equals(p.Provider, package.Provider, StringComparison.OrdinalIgnoreCase) AndAlso
                String.Equals(p.ClassName, package.ClassName, StringComparison.OrdinalIgnoreCase) AndAlso
                String.Equals(p.Version, package.Version, StringComparison.OrdinalIgnoreCase) AndAlso
                SameDate(p.DriverDate, package.DriverDate)
        End Function)
    End Function

    Private Shared Function SameDate(a As String, b As String) As Boolean
        Dim parsedA As DateTime
        Dim parsedB As DateTime
        Dim patterns = {"M/d/yyyy", "MM/dd/yyyy", "yyyy-MM-dd"}
        If Not DateTime.TryParseExact(If(a, "").Trim(), patterns,
                      Globalization.CultureInfo.InvariantCulture,
                      Globalization.DateTimeStyles.None, parsedA) OrElse
           Not DateTime.TryParseExact(If(b, "").Trim(), patterns,
                      Globalization.CultureInfo.InvariantCulture,
                      Globalization.DateTimeStyles.None, parsedB) Then Return False
        Return parsedA.Date = parsedB.Date
    End Function

    Private Shared Function DismExecutable() As String
        Dim systemName = If(Environment.Is64BitOperatingSystem AndAlso
                            Not Environment.Is64BitProcess, "Sysnative", "System32")
        Dim osDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Windows)
        Dim filename = Path.Combine(osDirectory, systemName, "dism.exe")
        If Not File.Exists(filename) Then Throw New FileNotFoundException(
            "System DISM.exe was not found.", filename)
        Return filename
    End Function

    Public Shared Async Function AddOneAsync(mountFolder As String,
                  package As DriverStudioEngine.DriverPackage,
                  approvedAudit As DriverStudioPackageAudit.AuditResult,
                  targetArchitecture As String) As Task(Of InstallResult)
        If package Is Nothing OrElse String.IsNullOrWhiteSpace(package.FilePath) Then
            Throw New ArgumentException("Missing INF source.")
        End If
        ' Re-hash the actual INF immediately before starting each DISM action.
        ' This does not certify all accompanying driver payloads; DISM enforces
        ' package validity and signature checks.
        Dim fresh = Await Task.Run(Function() DriverStudioEngine.ReadInf(package.FilePath))
        If approvedAudit Is Nothing OrElse Not approvedAudit.CanInstall Then
            Throw New InvalidOperationException(
                "An intact package with a trusted catalog must be audited before DISM.")
        End If
        If DriverStudioEngine.ArchitectureMismatch(fresh, targetArchitecture) Then
            Throw New InvalidOperationException(
                "INF architecture is incompatible with the mounted image.")
        End If
        ' Re-hash the entire package and re-check the selected CAT immediately
        ' before DISM reads the source. Never allow an unchecked source in.
        Await Task.Run(Function()
            Return DriverStudioPackageAudit.VerifyUnchanged(
                approvedAudit, fresh, targetArchitecture)
        End Function)
        If Not String.Equals(fresh.Sha256, package.Sha256,
                             StringComparison.OrdinalIgnoreCase) Then
            Throw New InvalidOperationException(
                "Source INF changed during import: " & package.InfName &
                ". Stop, re-scan and compare again.")
        End If
        Dim psi As New ProcessStartInfo With {
            .FileName = DismExecutable(),
            .UseShellExecute = False, .CreateNoWindow = True,
            .RedirectStandardOutput = True, .RedirectStandardError = True
        }
        For Each argument As String In BuildArguments(mountFolder, fresh.FilePath)
            psi.ArgumentList.Add(argument)
        Next

        Using command As New Process With {.StartInfo = psi}
            If Not command.Start() Then Throw New Win32Exception(
                "Could not start DISM for the selected INF.")
            ' No kill/timeout mid-operation: interrupting an active DISM write
            ' can leave the mounted WIM in an inconsistent state.
            Dim outputTask = command.StandardOutput.ReadToEndAsync()
            Dim errorTask = command.StandardError.ReadToEndAsync()
            Await command.WaitForExitAsync()
            Dim combined = (Await outputTask) & Environment.NewLine & (Await errorTask)
            Dim shortOutput = If(combined.Length <= 3600,
                                 combined, combined.Substring(combined.Length - 3600))
            Return New InstallResult With {
                .InfName = fresh.InfName,
                .ExitCode = command.ExitCode,
                .Succeeded = command.ExitCode = 0 OrElse command.ExitCode = 3010,
                .RebootRequired = command.ExitCode = 3010,
                .Details = shortOutput.Trim()
            }
        End Using
    End Function
End Class
