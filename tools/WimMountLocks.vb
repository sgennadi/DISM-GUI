Imports System
Imports System.Collections.Generic
Imports System.IO
Imports System.Linq
Imports System.Runtime.InteropServices
Imports System.Text

' Windows Restart Manager best-effort *read-only* file-lock inspection.
' Restart Manager accepts files, not directories (ERROR_ACCESS_DENIED for
' directories). This deliberately never calls RmShutdown / RmRestart.
Public NotInheritable Class WimMountLocks
    Private Sub New()
    End Sub

    Private Const MoreData As Integer = 234
    Private Const MaxFiles As Integer = 96

    <StructLayout(LayoutKind.Sequential)>
    Private Structure NativeFileTime
        Public Low As Integer
        Public High As Integer
    End Structure

    <StructLayout(LayoutKind.Sequential)>
    Private Structure UniqueProcess
        Public ProcessId As Integer
        Public StartTime As NativeFileTime
    End Structure

    <StructLayout(LayoutKind.Sequential, CharSet:=CharSet.Unicode)>
    Private Structure ProcessInfo
        Public Process As UniqueProcess
        <MarshalAs(UnmanagedType.ByValTStr, SizeConst:=256)>
        Public AppName As String
        <MarshalAs(UnmanagedType.ByValTStr, SizeConst:=64)>
        Public ServiceName As String
        Public ApplicationType As Integer
        Public AppStatus As UInteger
        Public SessionId As UInteger
        <MarshalAs(UnmanagedType.Bool)>
        Public Restartable As Boolean
    End Structure

    <DllImport("rstrtmgr.dll", CharSet:=CharSet.Unicode, ExactSpelling:=True)>
    Private Shared Function RmStartSession(ByRef session As UInteger,
                                           flags As UInteger,
                                           key As StringBuilder) As Integer
    End Function

    <DllImport("rstrtmgr.dll", CharSet:=CharSet.Unicode, ExactSpelling:=True)>
    Private Shared Function RmRegisterResources(
        session As UInteger, fileCount As UInteger,
        <MarshalAs(UnmanagedType.LPArray, ArraySubType:=UnmanagedType.LPWStr)>
        files() As String, applicationCount As UInteger,
        applications As IntPtr, serviceCount As UInteger,
        services As IntPtr) As Integer
    End Function

    <DllImport("rstrtmgr.dll", CharSet:=CharSet.Unicode, ExactSpelling:=True)>
    Private Shared Function RmGetList(
        session As UInteger, ByRef required As UInteger,
        ByRef count As UInteger, <Out> affected() As ProcessInfo,
        ByRef rebootReasons As UInteger) As Integer
    End Function

    <DllImport("rstrtmgr.dll", CharSet:=CharSet.Unicode, ExactSpelling:=True)>
    Private Shared Function RmEndSession(session As UInteger) As Integer
    End Function

    Public NotInheritable Class LockCandidate
        Public Property Pid As Integer
        Public Property Name As String = ""
        Public Property Service As String = ""
    End Class

    Public NotInheritable Class ScanResult
        Public Property FilesSampled As Integer
        Public Property Limited As Boolean
        Public Property Warning As String = ""
        Public Property Candidates As New List(Of LockCandidate)()
    End Class

    Public Shared Function ValidateFolder(folder As String) As String
        If String.IsNullOrWhiteSpace(folder) Then
            Throw New ArgumentException("Select a registered mount folder.")
        End If
        Dim full = Path.GetFullPath(folder.Trim().Trim(""""c)).
                       TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
        If full.Length = 0 OrElse
           String.Equals(full, Path.GetPathRoot(full).TrimEnd(
                Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                StringComparison.OrdinalIgnoreCase) Then
            Throw New ArgumentException("A volume root is not a mount folder.")
        End If
        If Not Directory.Exists(full) Then
            Throw New DirectoryNotFoundException("Mount folder is not accessible.")
        End If
        If (File.GetAttributes(full) And FileAttributes.ReparsePoint) <>
            FileAttributes.Normal Then
            Throw New IOException("Mount folder itself is a reparse point; inspect manually.")
        End If
        Return full
    End Function

    Public Shared Function Scan(mountFolder As String,
                                Optional wimPath As String = "") As ScanResult
        Dim folder = ValidateFolder(mountFolder)
        Dim result As New ScanResult()
        Dim files As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)
        ' The WIM source is only an extra candidate resource. A process shown
        ' for this source alone is not proof of a mount-folder blocker.
        Try
            If Not String.IsNullOrWhiteSpace(wimPath) AndAlso
               File.Exists(wimPath) Then files.Add(Path.GetFullPath(wimPath))
        Catch ex As Exception When TypeOf ex Is ArgumentException OrElse
                                  TypeOf ex Is IOException OrElse
                                  TypeOf ex Is UnauthorizedAccessException
            result.Warning = "Source WIM path is not readable; checking mounted files only."
        End Try
        Try
            Dim settings As New EnumerationOptions With {
                .RecurseSubdirectories = True,
                .IgnoreInaccessible = True,
                .AttributesToSkip = FileAttributes.ReparsePoint,
                .ReturnSpecialDirectories = False
            }
            ' Sampling is intentional. A complete handle inventory is NOT
            ' available through Restart Manager; do not scan millions of paths.
            Dim walked As Integer = 0
            For Each file In Directory.EnumerateFiles(folder, "*", settings)
                walked += 1
                If files.Count < MaxFiles Then files.Add(file)
                If walked >= 512 Then
                    result.Limited = True
                    Exit For
                End If
            Next
            If files.Count >= MaxFiles Then result.Limited = True
        Catch ex As Exception When TypeOf ex Is IOException OrElse
                                  TypeOf ex Is UnauthorizedAccessException OrElse
                                  TypeOf ex Is System.Security.SecurityException
            result.Warning &= " Some mounted files cannot be enumerated (" &
                              ex.GetType().Name & ")."
            result.Limited = True
        End Try
        result.FilesSampled = files.Count
        If files.Count = 0 Then
            result.Warning &= " No readable regular files sampled."
            Return result
        End If

        Dim session As UInteger = 0UI
        Dim started = RmStartSession(session, 0UI, New StringBuilder(33))
        If started <> 0 Then
            result.Warning &= " Restart Manager session unavailable (" &
                              started.ToString() & ")."
            Return result
        End If
        Try
            Dim paths = files.ToArray()
            Dim code = RmRegisterResources(session, CUInt(paths.Length), paths,
                                           0UI, IntPtr.Zero, 0UI, IntPtr.Zero)
            If code <> 0 Then
                result.Warning &= " Restart Manager could not register sampled files (" &
                                  code.ToString() & ")."
                Return result
            End If
            Dim required As UInteger = 0UI
            Dim count As UInteger = 0UI
            Dim reasons As UInteger = 0UI
            code = RmGetList(session, required, count, Nothing, reasons)
            If code = 0 Then Return result
            If code <> MoreData Then
                result.Warning &= " Restart Manager process list unavailable (" &
                                  code.ToString() & ")."
                Return result
            End If
            If required > 256UI OrElse required = 0UI Then
                result.Warning &= " Restart Manager list exceeds the 256-process limit."
                result.Limited = True
                Return result
            End If
            Dim entries(CInt(required) - 1) As ProcessInfo
            count = required
            code = RmGetList(session, required, count, entries, reasons)
            If code <> 0 Then
                result.Warning &= " Restart Manager process list changed (" &
                                  code.ToString() & ")."
                result.Limited = True
                Return result
            End If
            For Each app In entries.Take(CInt(count)).
                                      GroupBy(Function(x) x.Process.ProcessId).
                                      Select(Function(g) g.First())
                If app.Process.ProcessId <= 0 Then Continue For
                result.Candidates.Add(New LockCandidate With {
                    .Pid = app.Process.ProcessId,
                    .Name = If(app.AppName, "").Trim(),
                    .Service = If(app.ServiceName, "").Trim()
                })
            Next
            Return result
        Finally
            RmEndSession(session)
        End Try
    End Function

    Public Shared Function FormatResult(result As ScanResult) As String
        If result Is Nothing Then Throw New ArgumentNullException(NameOf(result))
        Dim lines As New List(Of String) From {
            "MOUNT FILE LOCKS | Windows Restart Manager (read-only)",
            "Sampled files: " & result.FilesSampled.ToString() &
                " (bounded to " & MaxFiles.ToString() & " resources)",
            "Directory handles, unmapped files and kernel handles may be missed.",
            "No processes were closed or restarted."
        }
        If result.Limited Then
            lines.Add("PARTIAL: scan was bounded; the result cannot exclude other locks.")
        End If
        If result.Warning.Length > 0 Then lines.Add("WARNING: " & result.Warning.Trim())
        If result.Candidates.Count = 0 Then
            lines.Add("No process identified for the sampled files; this does NOT prove " &
                      "the folder is unlocked.")
        Else
            For Each candidate In result.Candidates.OrderBy(Function(p) p.Pid)
                lines.Add("PID " & candidate.Pid.ToString() & " | " &
                          If(candidate.Name, "") &
                          If(String.IsNullOrWhiteSpace(candidate.Service),
                             "", " | service: " & candidate.Service))
            Next
        End If
        lines.Add("Close confirmed applications normally, then refresh DISM mount state.")
        Return String.Join(Environment.NewLine, lines)
    End Function
End Class
