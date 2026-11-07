Imports System
Imports System.Collections.Generic
Imports System.IO
Imports System.Linq
Imports System.Security.Cryptography
Imports System.Text

' Cooperative, machine-local cross-process lock for DISM GUI operations
' on a given image path or mounted directory. Locks are Windows file-share
' locks: the zero-byte files persist, while the OS releases the held handles
' automatically when a process dies. Never delete the lock files on exit;
' deleting a live lock inode would permit independent overlapping locks.
'
' Other programs calling dism.exe directly are NOT controlled by this lease.
' UNC aliases/remote hosts require their own administrative serialization.
Public NotInheritable Class WimImageOperationLease
    Implements IDisposable

    Private ReadOnly handles As New List(Of FileStream)()
    Private disposed As Boolean

    Private Sub New()
    End Sub

    Private Shared Function LockRoot(Optional overrideDirectory As String = Nothing) As String
        Dim baseDirectory = If(String.IsNullOrWhiteSpace(overrideDirectory),
            IO.Path.Combine(Environment.GetFolderPath(
                Environment.SpecialFolder.CommonApplicationData),
                "DISMGUI", "ImageOperationLocks"),
            IO.Path.GetFullPath(overrideDirectory))
        If String.IsNullOrWhiteSpace(baseDirectory) Then
            Throw New IOException("The machine-wide image lock location is unavailable.")
        End If
        Return baseDirectory
    End Function

    Private Shared Function LockName(resourcePath As String) As String
        If String.IsNullOrWhiteSpace(resourcePath) Then
            Throw New ArgumentException("Image/mount path is required for an exclusive operation.")
        End If
        Dim normalized = IO.Path.GetFullPath(resourcePath.Trim().Trim(""""c)).
            TrimEnd(IO.Path.DirectorySeparatorChar, IO.Path.AltDirectorySeparatorChar).
            ToUpperInvariant()
        Dim bytes = SHA256.HashData(Encoding.UTF8.GetBytes(normalized))
        Return Convert.ToHexString(bytes) & ".lock"
    End Function

    Public Shared Function Acquire(ParamArray resourcePaths() As String) As WimImageOperationLease
        Return AcquireAt(Nothing, resourcePaths)
    End Function

    ' The optional directory is used by isolated CI tests; production code
    ' always uses the shared ProgramData folder.
    Public Shared Function AcquireAt(testDirectory As String,
                                     ParamArray resourcePaths() As String
                                     ) As WimImageOperationLease
        If resourcePaths Is Nothing OrElse resourcePaths.Length = 0 Then
            Throw New ArgumentException("At least one image or mounted directory is required.")
        End If
        Dim names = resourcePaths.Select(Function(x) LockName(x)).
            Distinct(StringComparer.OrdinalIgnoreCase).
            OrderBy(Function(x) x, StringComparer.Ordinal).ToList()
        Dim root = LockRoot(testDirectory)
        Directory.CreateDirectory(root)
        Dim lease As New WimImageOperationLease()
        Try
            For Each lockName In names
                Dim lockFileName = IO.Path.Combine(root, lockName)
                Try
                    Dim held = New FileStream(lockFileName, FileMode.OpenOrCreate,
                        FileAccess.ReadWrite, FileShare.None, 1, FileOptions.None)
                    lease.handles.Add(held)
                Catch ex As IOException
                    Throw New InvalidOperationException(
                        "An image operation is already active, or the lock file " &
                        "is unavailable. No DISM operation was started.", ex)
                End Try
            Next
            Return lease
        Catch
            lease.Dispose()
            Throw
        End Try
    End Function

    Public Sub Dispose() Implements IDisposable.Dispose
        If disposed Then Return
        disposed = True
        For i As Integer = handles.Count - 1 To 0 Step -1
            handles(i).Dispose()
        Next
        handles.Clear()
    End Sub
End Class
