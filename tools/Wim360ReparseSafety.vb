Imports System
Imports System.Collections.Generic
Imports System.ComponentModel
Imports System.Globalization
Imports System.IO
Imports System.Runtime.InteropServices
Imports System.Threading

' Windows WIMMount exposes regular WIM-backed FILES as reparse points
' (IO_REPARSE_TAG_WIM). Rejecting every reparse file silently dropped
' virtually all files from real mounted WIMs. Distinguish the safe
' Microsoft WIM/WOF backing tags from symlinks, junctions and other tags.
'
' Directories with ANY reparse attribute are never recursed: this prevents
' traversal through a junction/symlink outside the mounted image.
Public NotInheritable Class Wim360ReparseSafety
    Private Sub New()
    End Sub

    Private Const WimTag As UInteger = &H80000008UI
    Private Const WofTag As UInteger = &H80000017UI

    <StructLayout(LayoutKind.Sequential, CharSet:=CharSet.Unicode)>
    Private Structure FindData
        Public Attributes As UInteger
        Public CreatedLow As UInteger
        Public CreatedHigh As UInteger
        Public AccessedLow As UInteger
        Public AccessedHigh As UInteger
        Public WrittenLow As UInteger
        Public WrittenHigh As UInteger
        Public SizeHigh As UInteger
        Public SizeLow As UInteger
        Public ReparseTag As UInteger
        Public Reserved As UInteger
        <MarshalAs(UnmanagedType.ByValTStr, SizeConst:=260)>
        Public FileName As String
        <MarshalAs(UnmanagedType.ByValTStr, SizeConst:=14)>
        Public AlternateName As String
    End Structure

    <DllImport("kernel32.dll", EntryPoint:="FindFirstFileW",
        CharSet:=CharSet.Unicode, SetLastError:=True)>
    Private Shared Function FindFirstFile(filename As String,
                                          ByRef data As FindData) As IntPtr
    End Function

    <DllImport("kernel32.dll", SetLastError:=True)>
    Private Shared Function FindClose(handle As IntPtr) As Boolean
    End Function

    Public Shared Function IsReadableImageFile(path As String) As Boolean
        Dim attributes = File.GetAttributes(path)
        If (attributes And FileAttributes.Directory) = FileAttributes.Directory Then
            Return False
        End If
        If (attributes And FileAttributes.ReparsePoint) <> FileAttributes.ReparsePoint Then
            Return True
        End If
        Dim found As New FindData()
        Dim handle = FindFirstFile(path, found)
        If handle = New IntPtr(-1) Then
            Throw New Win32Exception(Marshal.GetLastWin32Error(),
                "Cannot verify reparse type; the mounted image scan is incomplete.")
        End If
        Try
            ' WIN32_FIND_DATA.dwReserved0 contains the reparse tag when the
            ' REPARSE_POINT attribute is set. WIM/WOF are content backing,
            ' NOT pathname redirections (unlike SYMLINK or MOUNT_POINT).
            Return found.ReparseTag = WimTag OrElse found.ReparseTag = WofTag
        Finally
            FindClose(handle)
        End Try
    End Function

    Public Shared Function EnumerateMountTree(rootFolder As String, index As Integer,
                                               token As CancellationToken,
                                               Optional maxEntries As Integer = 250000
                                               ) As List(Of Wim360IndexComparer.Item)
        If index < 1 Then Throw New ArgumentOutOfRangeException(NameOf(index))
        If maxEntries < 1 Then Throw New ArgumentOutOfRangeException(NameOf(maxEntries))
        If String.IsNullOrWhiteSpace(rootFolder) Then
            Throw New ArgumentException("A mounted directory path is required.")
        End If
        Dim root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(rootFolder))
        If Not Directory.Exists(root) Then Throw New DirectoryNotFoundException(root)
        If (File.GetAttributes(root) And FileAttributes.ReparsePoint) = FileAttributes.ReparsePoint Then
            Throw New InvalidDataException("A junction/symlink mount root is not supported.")
        End If
        Dim folders As New Stack(Of String)()
        folders.Push(root)
        Dim total As Integer = 0
        Dim result As New List(Of Wim360IndexComparer.Item)()
        While folders.Count > 0
            token.ThrowIfCancellationRequested()
            Dim folder = folders.Pop()
            For Each entry In Directory.EnumerateFileSystemEntries(folder)
                token.ThrowIfCancellationRequested()
                total += 1
                If total > maxEntries Then
                    Throw New InvalidOperationException(
                        "Mounted image exceeds " & maxEntries.ToString(CultureInfo.InvariantCulture) &
                        " entries. No partial comparison was shown.")
                End If
                Dim attributes = File.GetAttributes(entry)
                Dim isReparse = (attributes And FileAttributes.ReparsePoint) = FileAttributes.ReparsePoint
                Dim isFolder = (attributes And FileAttributes.Directory) = FileAttributes.Directory
                Dim relative = Path.GetRelativePath(root, entry).Replace("/"c, "\"c)
                If relative = ".." OrElse relative.StartsWith("..\", StringComparison.Ordinal) OrElse
                   Path.IsPathRooted(relative) Then
                    Throw New InvalidDataException("A mounted path is outside the expected root.")
                End If
                If isFolder Then
                    If isReparse Then Continue For
                    result.Add(New Wim360IndexComparer.Item With {
                        .Index = index, .Kind = "Folders", .Key = relative, .Value = "Present"
                    })
                    folders.Push(entry)
                Else
                    If Not IsReadableImageFile(entry) Then Continue For
                    Dim bytes = New FileInfo(entry).Length
                    result.Add(New Wim360IndexComparer.Item With {
                        .Index = index, .Kind = "Files", .Key = relative,
                        .Value = bytes.ToString(CultureInfo.InvariantCulture) & " bytes"
                    })
                End If
            Next
        End While
        Return result
    End Function
End Class
