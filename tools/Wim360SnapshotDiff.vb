Imports System
Imports System.Collections.Generic
Imports System.Globalization
Imports System.IO
Imports System.Linq
Imports System.Text
Imports System.Text.Json

' Pure, read-only comparison of saved WIM 360 JSON inventory snapshots.
' No DISM, WIMGAPI, mount, network, or servicing action is performed here.
Public NotInheritable Class Wim360SnapshotDiff
    Private Sub New()
    End Sub

    Public NotInheritable Class SnapshotData
        Public Property SchemaVersion As Integer
        Public Property ImageFile As String = ""
        Public Property ImageIndex As Integer
        Public Property ScannedUtc As DateTime
        Public Property InventoryRowCount As Integer
        Public Property CoverageKnown As Boolean
        Public Property IsCompleteInventory As Boolean
        Public Property FeatureWarning As String = ""
        Public Property CapabilityWarning As String = ""
        Public Property DriverWarning As String = ""
        Public Property PackageWarning As String = ""
        Public Property Rows As New List(Of Wim360Catalog.CatalogRow)()
    End Class

    Public NotInheritable Class ChangeRow
        Public Property Change As String = ""
        Public Property Kind As String = ""
        Public Property Name As String = ""
        Public Property Before As Wim360Catalog.CatalogRow
        Public Property After As Wim360Catalog.CatalogRow
        Public Property Details As String = ""

        Public Function CellAt(index As Integer) As String
            Select Case index
                Case 0 : Return Change
                Case 1 : Return Kind
                Case 2 : Return Name
                Case 3 : Return If(Before Is Nothing, "", Before.State)
                Case 4 : Return If(After Is Nothing, "", After.State)
                Case 5 : Return If(Before Is Nothing, "", Before.Version)
                Case 6 : Return If(After Is Nothing, "", After.Version)
                Case 7 : Return If(Before Is Nothing, "", Before.ProviderOrType)
                Case 8 : Return If(After Is Nothing, "", After.ProviderOrType)
                Case 9 : Return Details
                Case Else : Return ""
            End Select
        End Function
    End Class

    Public NotInheritable Class DiffReport
        Public Property Baseline As SnapshotData
        Public Property Current As SnapshotData
        Public Property Changes As New List(Of ChangeRow)()
        Public Property AddedCount As Integer
        Public Property RemovedCount As Integer
        Public Property ChangedCount As Integer
        Public Property UnchangedCount As Integer
        Public Property AmbiguousCount As Integer
        Public Property SuppressedMembershipCount As Integer
        Public Property Warnings As New List(Of String)()
        Public Property CanDetectMembershipChanges As Boolean

        Public ReadOnly Property Summary As String
            Get
                Return "Added " & AddedCount.ToString() &
                    "  |  Removed " & RemovedCount.ToString() &
                    "  |  Changed " & ChangedCount.ToString() &
                    "  |  Unchanged " & UnchangedCount.ToString() &
                    If(SuppressedMembershipCount > 0,
                       "  |  Uncertain membership " & SuppressedMembershipCount.ToString(), "")
            End Get
        End Property
    End Class

    Public Shared ReadOnly ColumnHeaders As String() = {
        "Change", "Type", "Name / INF", "Before state", "After state",
        "Before version", "After version", "Before provider", "After provider", "Details"
    }

    Private Shared Function StringField(root As JsonElement,
                                        name As String,
                                        Optional required As Boolean = False) As String
        Dim value As JsonElement
        If root.ValueKind <> JsonValueKind.Object OrElse
           Not root.TryGetProperty(name, value) Then
            If required Then Throw New InvalidDataException("Missing JSON field: " & name)
            Return ""
        End If
        If value.ValueKind = JsonValueKind.Null AndAlso Not required Then Return ""
        If value.ValueKind <> JsonValueKind.String Then
            Throw New InvalidDataException("Field " & name & " must be a string.")
        End If
        Dim result = If(value.GetString(), "")
        If result.Length > 8192 Then
            Throw New InvalidDataException("Field " & name & " exceeds 8192 characters.")
        End If
        If required AndAlso String.IsNullOrWhiteSpace(result) Then
            Throw New InvalidDataException("Required field " & name & " is empty.")
        End If
        Return result
    End Function

    Private Shared Function IntegerField(root As JsonElement, name As String) As Integer
        Dim value As JsonElement
        Dim number As Integer
        If root.ValueKind <> JsonValueKind.Object OrElse
           Not root.TryGetProperty(name, value) OrElse
           value.ValueKind <> JsonValueKind.Number OrElse
           Not value.TryGetInt32(number) Then
            Throw New InvalidDataException("Invalid numeric JSON field: " & name)
        End If
        Return number
    End Function

    Public Shared Function ReadSnapshot(path As String) As SnapshotData
        If String.IsNullOrWhiteSpace(path) Then
            Throw New ArgumentException("Choose an exported WIM 360 JSON snapshot.")
        End If
        Dim fullPath = System.IO.Path.GetFullPath(path)
        If Not String.Equals(System.IO.Path.GetExtension(fullPath), ".json",
                             StringComparison.OrdinalIgnoreCase) Then
            Throw New InvalidDataException("Only WIM 360 .json snapshot files are supported.")
        End If
        Dim info As New FileInfo(fullPath)
        If Not info.Exists Then Throw New FileNotFoundException("Snapshot not found.", fullPath)
        If info.Length <= 0 OrElse info.Length > 48L * 1024L * 1024L Then
            Throw New InvalidDataException("JSON snapshot must be 1-48 MB or smaller.")
        End If
        Return ParseSnapshotJson(File.ReadAllText(fullPath, Encoding.UTF8))
    End Function

    Public Shared Function ParseSnapshotJson(json As String) As SnapshotData
        If String.IsNullOrWhiteSpace(json) OrElse json.Length > 48 * 1024 * 1024 Then
            Throw New InvalidDataException("WIM 360 snapshot JSON is empty or too large.")
        End If
        Dim result As New SnapshotData()
        Using parsed = JsonDocument.Parse(json, New JsonDocumentOptions With {
            .MaxDepth = 12, .AllowTrailingCommas = False,
            .CommentHandling = JsonCommentHandling.Disallow
        })
            Dim root = parsed.RootElement
            If root.ValueKind <> JsonValueKind.Object Then
                Throw New InvalidDataException("Expected a WIM 360 snapshot JSON object.")
            End If
            result.SchemaVersion = IntegerField(root, "SchemaVersion")
            If result.SchemaVersion <> 1 AndAlso result.SchemaVersion <> 2 Then
                Throw New InvalidDataException("Unsupported WIM 360 snapshot schema version.")
            End If
            Dim source = StringField(root, "Source", True)
            If Not source.StartsWith("DISM GUI WIM 360", StringComparison.OrdinalIgnoreCase) Then
                Throw New InvalidDataException("This JSON is not a WIM 360 inventory export.")
            End If
            result.ImageFile = StringField(root, "ImageFile", True)
            result.ImageIndex = IntegerField(root, "ImageIndex")
            If result.ImageIndex < 1 OrElse result.ImageIndex > 1024 Then
                Throw New InvalidDataException("Invalid image index in JSON snapshot.")
            End If
            Dim scanned = StringField(root, "ScannedUtc", True)
            Dim parsedDate As DateTime
            If Not DateTime.TryParse(scanned, CultureInfo.InvariantCulture,
                  DateTimeStyles.AssumeUniversal Or DateTimeStyles.AdjustToUniversal,
                  parsedDate) Then
                Throw New InvalidDataException("Snapshot scan timestamp is invalid.")
            End If
            result.ScannedUtc = parsedDate
            result.FeatureWarning = StringField(root, "FeatureWarning")
            result.CapabilityWarning = StringField(root, "CapabilityWarning")
            ' Added in 4.3.7; absent on legitimate older schema-2 exports.
            result.DriverWarning = StringField(root, "DriverWarning")
            result.PackageWarning = StringField(root, "PackageWarning")
            Dim entries As JsonElement
            If Not root.TryGetProperty("Rows", entries) OrElse
               entries.ValueKind <> JsonValueKind.Array Then
                Throw New InvalidDataException("Snapshot is missing the Rows array.")
            End If
            If entries.GetArrayLength() > 60000 Then
                Throw New InvalidDataException("Snapshot exceeds the 60000-row limit.")
            End If
            For Each item As JsonElement In entries.EnumerateArray()
                If item.ValueKind <> JsonValueKind.Object Then
                    Throw New InvalidDataException("Invalid snapshot inventory entry.")
                End If
                Dim kind = StringField(item, "Kind", True)
                Dim allowed As String() = {"Drivers", "Packages", "Features", "Capabilities"}
                If Not allowed.Contains(kind, StringComparer.OrdinalIgnoreCase) Then
                    Throw New InvalidDataException("Unknown snapshot inventory type: " & kind)
                End If
                result.Rows.Add(New Wim360Catalog.CatalogRow With {
                    .Kind = allowed.First(Function(x)
                        Return String.Equals(x, kind, StringComparison.OrdinalIgnoreCase)
                    End Function),
                    .Name = StringField(item, "Name", True),
                    .State = StringField(item, "State"),
                    .ProviderOrType = StringField(item, "ProviderOrType"),
                    .DriverClass = StringField(item, "DriverClass"),
                    .Version = StringField(item, "Version"),
                    .DriverDate = StringField(item, "DriverDate"),
                    .PublishedInf = StringField(item, "PublishedInf")
                })
            Next
            If result.SchemaVersion = 2 Then
                result.InventoryRowCount = IntegerField(root, "InventoryRowCount")
                Dim completeValue As JsonElement
                If Not root.TryGetProperty("IsCompleteInventory", completeValue) OrElse
                   (completeValue.ValueKind <> JsonValueKind.True AndAlso
                    completeValue.ValueKind <> JsonValueKind.False) Then
                    Throw New InvalidDataException("Schema 2 requires IsCompleteInventory.")
                End If
                If result.InventoryRowCount < result.Rows.Count OrElse
                   result.InventoryRowCount > 60000 Then
                    Throw New InvalidDataException("Snapshot total row count is inconsistent.")
                End If
                result.CoverageKnown = True
                result.IsCompleteInventory = completeValue.GetBoolean() AndAlso
                    result.InventoryRowCount = result.Rows.Count
                If completeValue.GetBoolean() AndAlso
                   result.InventoryRowCount <> result.Rows.Count Then
                    Throw New InvalidDataException("Snapshot claims complete inventory but rows are missing.")
                End If
            Else
                ' 4.3.4 JSON can represent a filtered export without recording that fact.
                ' Never infer Added/Removed from legacy schema-1 snapshots.
                result.InventoryRowCount = result.Rows.Count
                result.CoverageKnown = False
                result.IsCompleteInventory = False
            End If
        End Using
        Return result
    End Function

    Private Shared Function KeyFor(item As Wim360Catalog.CatalogRow) As String
        Dim separator = ChrW(0).ToString()
        Dim result = item.Kind.Trim().ToUpperInvariant() & separator &
                     item.Name.Trim().ToUpperInvariant()
        ' Multiple vendors can use identical INF names; a class mismatch is
        ' treated as a different driver, and duplicate identities are flagged.
        If String.Equals(item.Kind, "Drivers", StringComparison.OrdinalIgnoreCase) Then
            result &= separator & item.DriverClass.Trim().ToUpperInvariant()
        End If
        Return result
    End Function

    Private Shared Function GroupRows(rows As IEnumerable(Of Wim360Catalog.CatalogRow)
        ) As Dictionary(Of String, List(Of Wim360Catalog.CatalogRow))
        Dim groups As New Dictionary(Of String, List(Of Wim360Catalog.CatalogRow))(
            StringComparer.OrdinalIgnoreCase)
        For Each item As Wim360Catalog.CatalogRow In rows
            Dim key = KeyFor(item)
            Dim group As List(Of Wim360Catalog.CatalogRow) = Nothing
            If Not groups.TryGetValue(key, group) Then
                group = New List(Of Wim360Catalog.CatalogRow)()
                groups.Add(key, group)
            End If
            group.Add(item)
        Next
        Return groups
    End Function

    Private Shared Function MembershipReliable(baseline As SnapshotData,
                                               current As SnapshotData,
                                               kind As String) As Boolean
        If Not baseline.IsCompleteInventory OrElse
           Not current.IsCompleteInventory Then Return False
        If String.Equals(kind, "Drivers", StringComparison.OrdinalIgnoreCase) Then
            Return String.IsNullOrWhiteSpace(baseline.DriverWarning) AndAlso
                   String.IsNullOrWhiteSpace(current.DriverWarning)
        End If
        If String.Equals(kind, "Packages", StringComparison.OrdinalIgnoreCase) Then
            Return String.IsNullOrWhiteSpace(baseline.PackageWarning) AndAlso
                   String.IsNullOrWhiteSpace(current.PackageWarning)
        End If
        If String.Equals(kind, "Features", StringComparison.OrdinalIgnoreCase) Then
            Return String.IsNullOrWhiteSpace(baseline.FeatureWarning) AndAlso
                   String.IsNullOrWhiteSpace(current.FeatureWarning)
        End If
        If String.Equals(kind, "Capabilities", StringComparison.OrdinalIgnoreCase) Then
            Return String.IsNullOrWhiteSpace(baseline.CapabilityWarning) AndAlso
                   String.IsNullOrWhiteSpace(current.CapabilityWarning)
        End If
        Return True
    End Function

    Public Shared Function Compare(baseline As SnapshotData, current As SnapshotData
        ) As DiffReport
        If baseline Is Nothing Then Throw New ArgumentNullException(NameOf(baseline))
        If current Is Nothing Then Throw New ArgumentNullException(NameOf(current))
        Dim result As New DiffReport With {.Baseline = baseline, .Current = current}
        result.CanDetectMembershipChanges =
            baseline.IsCompleteInventory AndAlso current.IsCompleteInventory
        If Not result.CanDetectMembershipChanges Then
            result.Warnings.Add("At least one snapshot is filtered or uses the older schema 1. " &
                "Added/Removed entries are suppressed; only common items are compared.")
        End If
        If baseline.ImageIndex <> current.ImageIndex Then
            result.Warnings.Add("Different WIM indexes (" & baseline.ImageIndex.ToString() &
                " vs " & current.ImageIndex.ToString() & ") were selected.")
        End If
        If Not String.IsNullOrWhiteSpace(baseline.DriverWarning) OrElse
           Not String.IsNullOrWhiteSpace(current.DriverWarning) Then
            result.Warnings.Add("Driver inventory is incomplete in at least one snapshot.")
        End If
        If Not String.IsNullOrWhiteSpace(baseline.PackageWarning) OrElse
           Not String.IsNullOrWhiteSpace(current.PackageWarning) Then
            result.Warnings.Add("Windows package inventory is incomplete in at least one snapshot.")
        End If
        If Not String.IsNullOrWhiteSpace(baseline.FeatureWarning) OrElse
           Not String.IsNullOrWhiteSpace(current.FeatureWarning) Then
            result.Warnings.Add("Optional feature inventory is incomplete in at least one snapshot.")
        End If
        If Not String.IsNullOrWhiteSpace(baseline.CapabilityWarning) OrElse
           Not String.IsNullOrWhiteSpace(current.CapabilityWarning) Then
            result.Warnings.Add("Capability inventory is incomplete in at least one snapshot.")
        End If
        Dim oldGroups = GroupRows(baseline.Rows)
        Dim newGroups = GroupRows(current.Rows)
        Dim keys = oldGroups.Keys.Union(newGroups.Keys, StringComparer.OrdinalIgnoreCase).
            OrderBy(Function(k) k, StringComparer.OrdinalIgnoreCase)
        Dim suppressed As Integer = 0
        For Each key As String In keys
            Dim beforeList As List(Of Wim360Catalog.CatalogRow) = Nothing
            Dim afterList As List(Of Wim360Catalog.CatalogRow) = Nothing
            oldGroups.TryGetValue(key, beforeList)
            newGroups.TryGetValue(key, afterList)
            If (beforeList IsNot Nothing AndAlso beforeList.Count > 1) OrElse
               (afterList IsNot Nothing AndAlso afterList.Count > 1) Then
                result.AmbiguousCount += 1
                Continue For
            End If
            Dim beforeItem = If(beforeList Is Nothing, Nothing, beforeList(0))
            Dim afterItem = If(afterList Is Nothing, Nothing, afterList(0))
            Dim item = If(afterItem, beforeItem)
            If beforeItem Is Nothing OrElse afterItem Is Nothing Then
                If MembershipReliable(baseline, current, item.Kind) Then
                    Dim change = If(beforeItem Is Nothing, "Added", "Removed")
                    result.Changes.Add(New ChangeRow With {
                        .Change = change,
                        .Kind = item.Kind,
                        .Name = item.Name,
                        .Before = beforeItem,
                        .After = afterItem,
                        .Details = If(change = "Added", "Only in current snapshot",
                                                   "Only in baseline snapshot")
                    })
                    If change = "Added" Then
                        result.AddedCount += 1
                    Else
                        result.RemovedCount += 1
                    End If
                Else
                    suppressed += 1
                End If
                Continue For
            End If

            Dim changedFields As New List(Of String)()
            For column As Integer = 2 To Wim360Catalog.ColumnHeaders.Length - 1
                If Not String.Equals(beforeItem.CellAt(column), afterItem.CellAt(column),
                                     StringComparison.OrdinalIgnoreCase) Then
                    changedFields.Add(Wim360Catalog.ColumnHeaders(column))
                End If
            Next
            If changedFields.Count = 0 Then
                result.UnchangedCount += 1
            Else
                result.ChangedCount += 1
                result.Changes.Add(New ChangeRow With {
                    .Change = "Changed",
                    .Kind = item.Kind,
                    .Name = item.Name,
                    .Before = beforeItem,
                    .After = afterItem,
                    .Details = "Changed: " & String.Join(", ", changedFields)
                })
            End If
        Next
        result.SuppressedMembershipCount = suppressed
        If suppressed > 0 Then
            result.Warnings.Add(suppressed.ToString() & " potentially added/removed item(s) " &
                "were not classified because source coverage is incomplete.")
        End If
        If result.AmbiguousCount > 0 Then
            result.Warnings.Add(result.AmbiguousCount.ToString() &
                " repeated identity key(s) are ambiguous and were skipped.")
        End If
        result.Changes = result.Changes.
            OrderBy(Function(x) x.Kind, StringComparer.OrdinalIgnoreCase).
            ThenBy(Function(x) x.Name, StringComparer.OrdinalIgnoreCase).
            ThenBy(Function(x) x.Change, StringComparer.OrdinalIgnoreCase).ToList()
        Return result
    End Function

    Public Shared Function FilterChanges(items As IEnumerable(Of ChangeRow),
         change As String, kind As String, search As String) As List(Of ChangeRow)
        If items Is Nothing Then Throw New ArgumentNullException(NameOf(items))
        Dim wantedChange = If(change, "").Trim()
        Dim wantedKind = If(kind, "").Trim()
        Dim needle = If(search, "").Trim()
        Return items.Where(Function(x)
            If wantedChange.Length > 0 AndAlso
               Not String.Equals(wantedChange, "All changes", StringComparison.OrdinalIgnoreCase) AndAlso
               Not String.Equals(x.Change, wantedChange, StringComparison.OrdinalIgnoreCase) Then
                Return False
            End If
            If wantedKind.Length > 0 AndAlso
               Not String.Equals(wantedKind, "All types", StringComparison.OrdinalIgnoreCase) AndAlso
               Not String.Equals(x.Kind, wantedKind, StringComparison.OrdinalIgnoreCase) Then
                Return False
            End If
            If needle.Length > 0 Then
                For i As Integer = 0 To ColumnHeaders.Length - 1
                    If x.CellAt(i).IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0 Then
                        Return True
                    End If
                Next
                Return False
            End If
            Return True
        End Function).ToList()
    End Function

    Private Shared Function CsvCell(value As String) As String
        Dim raw = If(value, "")
        Dim leading = raw.TrimStart({" "c, ChrW(9), ChrW(10), ChrW(13)})
        If leading.Length > 0 AndAlso "=+-@".Contains(leading(0)) Then
            raw = "'" & raw
        End If
        Dim quote = ChrW(34).ToString()
        Return quote & raw.Replace(quote, quote & quote) & quote
    End Function

    Public Shared Function ToCsv(report As DiffReport,
                                 rows As IEnumerable(Of ChangeRow)) As String
        If report Is Nothing Then Throw New ArgumentNullException(NameOf(report))
        If rows Is Nothing Then Throw New ArgumentNullException(NameOf(rows))
        Dim lines As New List(Of String) From {
            String.Join(",", ColumnHeaders.Select(Function(h) CsvCell(h)))
        }
        For Each item As ChangeRow In rows
            lines.Add(String.Join(",", Enumerable.Range(0, ColumnHeaders.Length).
                Select(Function(i) CsvCell(item.CellAt(i)))))
        Next
        Return String.Join(vbCrLf, lines) & vbCrLf
    End Function

    Public Shared Function ToJson(report As DiffReport,
                                 rows As IEnumerable(Of ChangeRow)) As String
        If report Is Nothing Then Throw New ArgumentNullException(NameOf(report))
        If rows Is Nothing Then Throw New ArgumentNullException(NameOf(rows))
        Return JsonSerializer.Serialize(New With {
            .SchemaVersion = 1,
            .Source = "DISM GUI WIM 360 snapshot comparison",
            .Baseline = New With {
                .ImageFile = report.Baseline.ImageFile,
                .Index = report.Baseline.ImageIndex,
                .ScannedUtc = report.Baseline.ScannedUtc,
                .SchemaVersion = report.Baseline.SchemaVersion,
                .IsCompleteInventory = report.Baseline.IsCompleteInventory
            },
            .Current = New With {
                .ImageFile = report.Current.ImageFile,
                .Index = report.Current.ImageIndex,
                .ScannedUtc = report.Current.ScannedUtc,
                .SchemaVersion = report.Current.SchemaVersion,
                .IsCompleteInventory = report.Current.IsCompleteInventory
            },
            .Summary = report.Summary,
            .Warnings = report.Warnings,
            .Rows = rows.ToArray()
        }, New JsonSerializerOptions With {.WriteIndented = True})
    End Function
End Class
