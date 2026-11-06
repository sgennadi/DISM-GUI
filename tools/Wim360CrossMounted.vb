Imports System
Imports System.Collections.Generic
Imports System.Globalization
Imports System.IO
Imports System.Linq
Imports System.Threading

' Compare two separate WIM/ESD indexes that are already mounted, without
' mounting or writing an image. A/B use virtual slots 1 and 2; physical
' image index numbers are retained in result headers.
Public NotInheritable Class Wim360CrossMounted
    Private Sub New()
    End Sub

    Private Const MaxEntriesPerSource As Integer = 250000

    Private Shared Function FreshMounts(first As Wim360Reader.WimReport,
                                        firstIndex As Integer,
                                        second As Wim360Reader.WimReport,
                                        secondIndex As Integer,
                                        token As CancellationToken
                                        ) As List(Of Wim360Reader.ImageInfo)
        Wim360CrossImage.ValidatePair(first, firstIndex, second, secondIndex)
        Wim360CrossImage.ValidateSnapshot(first, firstIndex, token)
        Wim360CrossImage.ValidateSnapshot(second, secondIndex, token)
        Dim originals = {
            (Report:=first, Index:=firstIndex),
            (Report:=second, Index:=secondIndex)
        }
        Dim mounted As New List(Of Wim360Reader.ImageInfo)()
        For side As Integer = 0 To 1
            token.ThrowIfCancellationRequested()
            Dim descriptor = originals(side)
            Dim fresh = Wim360Reader.Inspect(descriptor.Report.ImageFile)
            Dim image = fresh.Images.FirstOrDefault(Function(i) i.Index = descriptor.Index)
            Dim expected = descriptor.Report.Images.First(Function(i) i.Index = descriptor.Index)
            If image Is Nothing OrElse
               Not String.Equals(image.Name, expected.Name, StringComparison.Ordinal) OrElse
               Not image.MountStatus.StartsWith("Mounted", StringComparison.OrdinalIgnoreCase) OrElse
               String.IsNullOrWhiteSpace(image.MountFolder) OrElse
               Not Directory.Exists(image.MountFolder) Then
                Throw New InvalidOperationException(
                    If(side = 0, "A", "B") & " index is not currently mounted. " &
                    "Mount the selected index and inspect both sources again.")
            End If
            Dim root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(image.MountFolder))
            If (File.GetAttributes(root) And FileAttributes.ReparsePoint) = FileAttributes.ReparsePoint Then
                Throw New InvalidDataException("Mounted image root may not be a junction or symlink.")
            End If
            mounted.Add(New Wim360Reader.ImageInfo With {
                .Index = side + 1,
                .Name = image.Name,
                .MountFolder = root,
                .MountStatus = image.MountStatus
            })
        Next
        If String.Equals(mounted(0).MountFolder, mounted(1).MountFolder,
                         StringComparison.OrdinalIgnoreCase) Then
            Throw New InvalidOperationException(
                "A and B resolved to the same mount folder. Choose two independent mounts.")
        End If
        Return mounted
    End Function

    ' The enumerator is also used in the synthetic Windows regression suite.
    ' Skip reparse entries, reject incomplete/unreadable scans, and enforce
    ' the same 250,000 entry cap as same-container mounted comparison.
    ' This safety-aware walker keeps genuine WIM/WOF-backed reparse
    ' files (regular image content) while refusing symlinks and junctions.
    Public Shared Function ListMountedTree(rootFolder As String, slot As Integer,
                                           token As CancellationToken
                                           ) As List(Of Wim360IndexComparer.Item)
        If slot <> 1 AndAlso slot <> 2 Then Throw New ArgumentOutOfRangeException(NameOf(slot))
        Return Wim360ReparseSafety.EnumerateMountTree(rootFolder, slot,
                                                       token, MaxEntriesPerSource)
    End Function

    ' Pure merge seam: the inputs may originate from two different original
    ' WIM indexes even when both are numbered 1 in their source container.
    Public Shared Function CompareMountedCatalogs(first As Wim360Reader.WimReport,
                             firstIndex As Integer,
                             second As Wim360Reader.WimReport,
                             secondIndex As Integer,
                             firstCatalog As IEnumerable(Of Wim360IndexComparer.Item),
                             secondCatalog As IEnumerable(Of Wim360IndexComparer.Item)
                             ) As Wim360IndexComparer.Result
        Wim360CrossImage.ValidatePair(first, firstIndex, second, secondIndex)
        If firstCatalog Is Nothing OrElse secondCatalog Is Nothing Then
            Throw New ArgumentNullException(NameOf(firstCatalog))
        End If
        Dim records = firstCatalog.Select(Function(i)
            Return New Wim360IndexComparer.Item With {
                .Index = 1, .Kind = i.Kind, .Key = i.Key, .Value = i.Value
            }
        End Function).Concat(secondCatalog.Select(Function(i)
            Return New Wim360IndexComparer.Item With {
                .Index = 2, .Kind = i.Kind, .Key = i.Key, .Value = i.Value
            }
        End Function))
        Dim result = Wim360IndexComparer.CompareRecords(
            first.ImageFile & " | " & second.ImageFile,
            "Mounted file paths / sizes", {1, 2}, records)
        result.ColumnLabels(1) = Wim360CrossImage.SourceLabel(first, firstIndex, "A")
        result.ColumnLabels(2) = Wim360CrossImage.SourceLabel(second, secondIndex, "B")
        result.Notes = "Read-only directories and file lengths from two already-mounted indexes. " &
            "Reparse points skipped. Equal file lengths do NOT prove equal bytes. " &
            "Use SHA-256 verification for selected paths."
        Return result
    End Function

    Public Shared Function Compare(first As Wim360Reader.WimReport, firstIndex As Integer,
                                   second As Wim360Reader.WimReport, secondIndex As Integer,
                                   token As CancellationToken,
                                   Optional progress As IProgress(Of String) = Nothing
                                   ) As Wim360IndexComparer.Result
        Dim mounts = FreshMounts(first, firstIndex, second, secondIndex, token)
        If progress IsNot Nothing Then progress.Report("A: scanning mounted file lengths...")
        Dim a = ListMountedTree(mounts(0).MountFolder, 1, token)
        token.ThrowIfCancellationRequested()
        If progress IsNot Nothing Then progress.Report("B: scanning mounted file lengths...")
        Dim b = ListMountedTree(mounts(1).MountFolder, 2, token)
        token.ThrowIfCancellationRequested()
        Dim current = FreshMounts(first, firstIndex, second, secondIndex, token)
        If Not String.Equals(mounts(0).MountFolder, current(0).MountFolder,
                             StringComparison.OrdinalIgnoreCase) OrElse
           Not String.Equals(mounts(1).MountFolder, current(1).MountFolder,
                             StringComparison.OrdinalIgnoreCase) Then
            Throw New InvalidOperationException(
                "A mount folder changed while listing. No partial comparison was applied.")
        End If
        Return CompareMountedCatalogs(first, firstIndex, second, secondIndex, a, b)
    End Function

    Public Shared Function VerifyOne(first As Wim360Reader.WimReport, firstIndex As Integer,
                                     second As Wim360Reader.WimReport, secondIndex As Integer,
                                     original As Wim360IndexComparer.Row,
                                     token As CancellationToken) As Wim360IndexComparer.Row
        Dim mounts = FreshMounts(first, firstIndex, second, secondIndex, token)
        Return Wim360IndexComparer.VerifyAtMountRoots(
            first.ImageFile & " | " & second.ImageFile, mounts, original, token)
    End Function

    Public Shared Function VerifyBatch(first As Wim360Reader.WimReport, firstIndex As Integer,
                                       second As Wim360Reader.WimReport, secondIndex As Integer,
                                       comparison As Wim360IndexComparer.Result,
                                       candidates As IEnumerable(Of Wim360IndexComparer.Row),
                                       token As CancellationToken,
                                       Optional progress As IProgress(Of String) = Nothing
                                       ) As List(Of Wim360IndexComparer.Row)
        Dim planned = Wim360IndexComparer.PlanHashBatch(comparison, candidates)
        If planned.Count = 0 Then
            Throw New InvalidOperationException(
                "No same-length files remain under the active filter. " &
                "Select All results or Present in both.")
        End If
        Dim mounts = FreshMounts(first, firstIndex, second, secondIndex, token)
        Dim results As New List(Of Wim360IndexComparer.Row)()
        For Each item In planned
            token.ThrowIfCancellationRequested()
            If progress IsNot Nothing Then progress.Report(
                "SHA-256 " & (results.Count + 1).ToString() &
                "/" & planned.Count.ToString() & ": " & item.Name)
            results.Add(Wim360IndexComparer.VerifyAtMountRoots(
                first.ImageFile & " | " & second.ImageFile, mounts, item, token))
        Next
        token.ThrowIfCancellationRequested()
        ' Return all-or-nothing. The form applies the list atomically.
        Return results
    End Function
End Class
