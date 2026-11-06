Imports System
Imports System.Collections.Generic
Imports System.Globalization
Imports System.IO
Imports System.Linq
Imports System.Threading

' Read-only comparison of ONE index from each of TWO DIFFERENT WIM/ESD files.
' Numeric slots 1 and 2 identify baseline and candidate in the diff matrix;
' original WIM index numbers are retained in source-column labels.
Public NotInheritable Class Wim360CrossImage
    Private Sub New()
    End Sub

    Private Shared Function ImageAt(report As Wim360Reader.WimReport,
                                    index As Integer) As Wim360Reader.ImageInfo
        If report Is Nothing Then Throw New ArgumentNullException(NameOf(report))
        Dim item = report.Images.FirstOrDefault(Function(x) x.Index = index)
        If item Is Nothing Then
            Throw New ArgumentOutOfRangeException(NameOf(index),
                "Index " & index.ToString() & " is not present in the selected image.")
        End If
        Return item
    End Function

    Public Shared Function SourceLabel(report As Wim360Reader.WimReport,
                                       index As Integer, prefix As String) As String
        Dim name = Path.GetFileName(report.ImageFile)
        If String.IsNullOrWhiteSpace(name) Then name = "image"
        Return prefix & ": " & name & " [" &
            index.ToString(CultureInfo.InvariantCulture) & "]"
    End Function

    Public Shared Sub ValidatePair(baseline As Wim360Reader.WimReport,
                                   baselineIndex As Integer,
                                   candidate As Wim360Reader.WimReport,
                                   candidateIndex As Integer)
        If baseline Is Nothing OrElse candidate Is Nothing Then
            Throw New ArgumentNullException(NameOf(baseline),
                "Inspect both WIM/ESD images before comparing.")
        End If
        If baseline.IsSplitSet OrElse candidate.IsSplitSet Then
            Throw New NotSupportedException(
                "Cross-image comparison currently accepts WIM/ESD only; " &
                "SWM split sets require a separately verified export.")
        End If
        Dim first = Path.GetFullPath(baseline.ImageFile)
        Dim second = Path.GetFullPath(candidate.ImageFile)
        If String.Equals(first, second, StringComparison.OrdinalIgnoreCase) Then
            Throw New ArgumentException(
                "Choose two different WIM/ESD containers. " &
                "Use Compare indexes for two indexes within one WIM.")
        End If
        If Not (first.EndsWith(".wim", StringComparison.OrdinalIgnoreCase) OrElse
                first.EndsWith(".esd", StringComparison.OrdinalIgnoreCase)) OrElse
           Not (second.EndsWith(".wim", StringComparison.OrdinalIgnoreCase) OrElse
                second.EndsWith(".esd", StringComparison.OrdinalIgnoreCase)) Then
            Throw New ArgumentException("Both files must be WIM or ESD containers.")
        End If
        ImageAt(baseline, baselineIndex)
        ImageAt(candidate, candidateIndex)
    End Sub

    Private Shared Sub ValidateSnapshot(report As Wim360Reader.WimReport,
                                        index As Integer,
                                        token As CancellationToken)
        token.ThrowIfCancellationRequested()
        Dim path = Path.GetFullPath(report.ImageFile)
        Dim info = New FileInfo(path)
        If Not info.Exists Then Throw New FileNotFoundException("Image file missing.", path)
        If info.Length <> report.FileSizeBytes OrElse
           info.LastWriteTimeUtc <> report.ModifiedUtc Then
            Throw New InvalidOperationException(
                Path.GetFileName(path) & " changed after Inspect. " &
                "Inspect both sources again before comparing.")
        End If
        Dim fresh = Wim360Reader.Inspect(path)
        If fresh.ImageCount <> report.ImageCount OrElse
           fresh.IsSplitSet <> report.IsSplitSet OrElse
           Not fresh.Images.Any(Function(x)
                Return x.Index = index AndAlso
                    String.Equals(x.Name, ImageAt(report, index).Name,
                                  StringComparison.Ordinal)
           End Function) Then
            Throw New InvalidOperationException(
                Path.GetFileName(path) & " index metadata changed. Inspect again.")
        End If
        info.Refresh()
        If info.Length <> report.FileSizeBytes OrElse
           info.LastWriteTimeUtc <> report.ModifiedUtc Then
            Throw New InvalidOperationException(
                Path.GetFileName(path) & " changed during catalog verification.")
        End If
    End Sub

    Private Shared Function Fields(image As Wim360Reader.ImageInfo) As Dictionary(Of String, String)
        Return New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase) From {
            {"Image name", image.Name},
            {"Description", image.Description},
            {"Edition", image.Edition},
            {"Flags", image.Flags},
            {"Architecture", image.Architecture},
            {"Product", image.Product},
            {"OS build", image.Version},
            {"Languages", image.Languages},
            {"Installation type", image.InstallationType},
            {"Uncompressed bytes", image.UncompressedBytes.ToString(CultureInfo.InvariantCulture)}
        }
    End Function

    ' Pure comparison seam used by CI: does not read filesystem or call DISM.
    Public Shared Function CompareMetadataPair(
                                   baseline As Wim360Reader.WimReport,
                                   baselineIndex As Integer,
                                   candidate As Wim360Reader.WimReport,
                                   candidateIndex As Integer
                                   ) As Wim360IndexComparer.Result
        ValidatePair(baseline, baselineIndex, candidate, candidateIndex)
        Dim entries As New List(Of Wim360IndexComparer.Item)()
        For Each entry In Fields(ImageAt(baseline, baselineIndex))
            entries.Add(New Wim360IndexComparer.Item With {
                .Index = 1, .Kind = "Metadata",
                .Key = entry.Key, .Value = If(entry.Value, "")
            })
        Next
        For Each entry In Fields(ImageAt(candidate, candidateIndex))
            entries.Add(New Wim360IndexComparer.Item With {
                .Index = 2, .Kind = "Metadata",
                .Key = entry.Key, .Value = If(entry.Value, "")
            })
        Next
        Dim result = Wim360IndexComparer.CompareRecords(
            baseline.ImageFile & " | " & candidate.ImageFile,
            "Two WIM/ESD containers - metadata", {1, 2}, entries)
        AttachSourceLabels(result, baseline, baselineIndex, candidate, candidateIndex)
        result.Notes = "Read-only WIM index metadata only; no file paths, " &
            "file sizes or bytes have been compared."
        Return result
    End Function

    Public Shared Function ComparePathsPair(
                                   baseline As Wim360Reader.WimReport,
                                   baselineIndex As Integer,
                                   candidate As Wim360Reader.WimReport,
                                   candidateIndex As Integer,
                                   baselineRecords As IEnumerable(Of Wim360IndexComparer.Item),
                                   candidateRecords As IEnumerable(Of Wim360IndexComparer.Item)
                                   ) As Wim360IndexComparer.Result
        ValidatePair(baseline, baselineIndex, candidate, candidateIndex)
        If baselineRecords Is Nothing OrElse candidateRecords Is Nothing Then
            Throw New ArgumentNullException(NameOf(baselineRecords))
        End If
        ' Always assign virtual slot numbers, not the original WIM indexes;
        ' otherwise index 1 versus index 1 would merge into a single column.
        Dim records = baselineRecords.Select(Function(x)
            Return New Wim360IndexComparer.Item With {
                .Index = 1, .Kind = x.Kind, .Key = x.Key, .Value = x.Value
            }
        End Function).Concat(candidateRecords.Select(Function(x)
            Return New Wim360IndexComparer.Item With {
                .Index = 2, .Kind = x.Kind, .Key = x.Key, .Value = x.Value
            }
        End Function))
        Dim result = Wim360IndexComparer.CompareRecords(
            baseline.ImageFile & " | " & candidate.ImageFile,
            "Two WIM/ESD containers - offline paths", {1, 2}, records)
        AttachSourceLabels(result, baseline, baselineIndex, candidate, candidateIndex)
        result.Notes = "Both source WIM/ESD files passed full-file SHA-256 checks before/after " &
            "their offline catalog read. Path presence and directory presence only; " &
            "same names do NOT prove equal file sizes or contents."
        Return result
    End Function

    Private Shared Sub AttachSourceLabels(
                             result As Wim360IndexComparer.Result,
                             baseline As Wim360Reader.WimReport, baselineIndex As Integer,
                             candidate As Wim360Reader.WimReport, candidateIndex As Integer)
        result.ColumnLabels(1) = SourceLabel(baseline, baselineIndex, "A")
        result.ColumnLabels(2) = SourceLabel(candidate, candidateIndex, "B")
    End Sub

    Public Shared Function Compare(baseline As Wim360Reader.WimReport,
                                   baselineIndex As Integer,
                                   candidate As Wim360Reader.WimReport,
                                   candidateIndex As Integer,
                                   comparePaths As Boolean,
                                   token As CancellationToken,
                                   Optional progress As IProgress(Of String) = Nothing
                                   ) As Wim360IndexComparer.Result
        ValidatePair(baseline, baselineIndex, candidate, candidateIndex)
        token.ThrowIfCancellationRequested()
        ValidateSnapshot(baseline, baselineIndex, token)
        ValidateSnapshot(candidate, candidateIndex, token)
        If Not comparePaths Then
            Return CompareMetadataPair(baseline, baselineIndex, candidate, candidateIndex)
        End If
        If progress IsNot Nothing Then
            progress.Report("A: reading index " & baselineIndex.ToString() &
                " (verified WIM/ESD, no mounting)")
        End If
        Dim first = Wim360OfflineList.ListOneVerified(
            baseline, baselineIndex, token, progress)
        token.ThrowIfCancellationRequested()
        If progress IsNot Nothing Then
            progress.Report("B: reading index " & candidateIndex.ToString() &
                " (verified WIM/ESD, no mounting)")
        End If
        Dim second = Wim360OfflineList.ListOneVerified(
            candidate, candidateIndex, token, progress)
        token.ThrowIfCancellationRequested()
        ' Do not claim an all-or-nothing result if either source was changed
        ' while the other source was enumerated. Revalidate both snapshots.
        ValidateSnapshot(baseline, baselineIndex, token)
        ValidateSnapshot(candidate, candidateIndex, token)
        Return ComparePathsPair(baseline, baselineIndex, candidate, candidateIndex,
                                first, second)
    End Function
End Class
