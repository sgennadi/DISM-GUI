Imports System
Imports System.Collections.Generic
Imports System.Globalization
Imports System.Linq
Imports System.Text
Imports System.Text.Json

' A pure, read-only view model for the verified mounted-image inventory.
' Filtering/export never invokes DISM and never touches a mounted image.
Public NotInheritable Class Wim360Catalog
    Private Sub New()
    End Sub

    Public NotInheritable Class CatalogRow
        Public Property Kind As String = ""
        Public Property Name As String = ""
        Public Property State As String = ""
        Public Property ProviderOrType As String = ""
        Public Property DriverClass As String = ""
        Public Property Version As String = ""
        Public Property DriverDate As String = ""
        Public Property PublishedInf As String = ""

        Public Function CellAt(index As Integer) As String
            Select Case index
                Case 0 : Return Kind
                Case 1 : Return Name
                Case 2 : Return State
                Case 3 : Return ProviderOrType
                Case 4 : Return DriverClass
                Case 5 : Return Version
                Case 6 : Return DriverDate
                Case 7 : Return PublishedInf
                Case Else : Return ""
            End Select
        End Function
    End Class

    Public Shared ReadOnly ColumnHeaders As String() = {
        "Type", "Name / INF", "State", "Provider / Release type",
        "Class", "Version", "Date", "Published INF"
    }

    Public Shared Function BuildRows(audit As Wim360Servicing.AuditResult) As List(Of CatalogRow)
        If audit Is Nothing Then Throw New ArgumentNullException(NameOf(audit))
        Dim result As New List(Of CatalogRow)()

        For Each item As DriverStudioEngine.DriverPackage In audit.Drivers
            result.Add(New CatalogRow With {
                .Kind = "Drivers",
                .Name = item.InfName,
                .State = If(item.IsInbox, "Inbox", "Third-party"),
                .ProviderOrType = item.Provider,
                .DriverClass = item.ClassName,
                .Version = item.Version,
                .DriverDate = item.DriverDate,
                .PublishedInf = item.PublishedName
            })
        Next
        For Each item As Wim360Servicing.PackageEntry In audit.Packages
            result.Add(New CatalogRow With {
                .Kind = "Packages",
                .Name = item.Name,
                .State = item.State,
                .ProviderOrType = item.ReleaseType
            })
        Next
        For Each item As Wim360Servicing.ComponentEntry In audit.Components
            result.Add(New CatalogRow With {
                .Kind = item.Kind,
                .Name = item.Name,
                .State = item.State
            })
        Next
        Return result
    End Function

    Public Shared Function FilterRows(items As IEnumerable(Of CatalogRow),
               kind As String, state As String, search As String) As List(Of CatalogRow)
        If items Is Nothing Then Throw New ArgumentNullException(NameOf(items))
        Dim chosenKind = If(kind, "").Trim()
        Dim chosenState = If(state, "").Trim()
        Dim needle = If(search, "").Trim()
        Dim query = items
        If chosenKind.Length > 0 AndAlso
           Not String.Equals(chosenKind, "All types", StringComparison.OrdinalIgnoreCase) Then
            query = query.Where(Function(x)
                Return String.Equals(x.Kind, chosenKind, StringComparison.OrdinalIgnoreCase)
            End Function)
        End If
        If chosenState.Length > 0 AndAlso
           Not String.Equals(chosenState, "All states", StringComparison.OrdinalIgnoreCase) Then
            query = query.Where(Function(x)
                Return String.Equals(x.State, chosenState, StringComparison.OrdinalIgnoreCase)
            End Function)
        End If
        If needle.Length > 0 Then
            query = query.Where(Function(x)
                For i As Integer = 0 To ColumnHeaders.Length - 1
                    If x.CellAt(i).IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0 Then
                        Return True
                    End If
                Next
                Return False
            End Function)
        End If
        Return query.ToList()
    End Function

    Private Shared Function CsvCell(value As String) As String
        Dim safe = If(value, "")
        ' Prevent Excel/Calc formula execution from untrusted INF/package names.
        Dim trimmed = safe.TrimStart(" "c, ControlChars.Tab, ControlChars.Cr, ControlChars.Lf)
        If trimmed.Length > 0 AndAlso "=+-@".Contains(trimmed(0)) Then
            safe = "'" & safe
        End If
        Dim quote = ChrW(34).ToString()
        Return quote & safe.Replace(quote, quote & quote) & quote
    End Function

    Public Shared Function ToCsv(audit As Wim360Servicing.AuditResult,
                    rows As IEnumerable(Of CatalogRow)) As String
        If audit Is Nothing Then Throw New ArgumentNullException(NameOf(audit))
        If rows Is Nothing Then Throw New ArgumentNullException(NameOf(rows))
        Dim headers As String() = {"Image", "Index", "Scanned UTC"}.
            Concat(ColumnHeaders).ToArray()
        Dim lines As New List(Of String) From {
            String.Join(",", headers.Select(Function(h) CsvCell(h)))
        }
        For Each item As CatalogRow In rows
            Dim fields As String() = {
                audit.ImageFile,
                audit.Index.ToString(CultureInfo.InvariantCulture),
                audit.ScannedUtc.ToString("u", CultureInfo.InvariantCulture)
            }
            lines.Add(String.Join(",", fields.Concat(
                Enumerable.Range(0, ColumnHeaders.Length).Select(
                    Function(i) item.CellAt(i))).Select(Function(s) CsvCell(s))))
        Next
        Return String.Join(vbCrLf, lines) & vbCrLf
    End Function

    Public Shared Function ToJson(audit As Wim360Servicing.AuditResult,
                     rows As IEnumerable(Of CatalogRow)) As String
        If audit Is Nothing Then Throw New ArgumentNullException(NameOf(audit))
        If rows Is Nothing Then Throw New ArgumentNullException(NameOf(rows))
        Dim snapshot = New With {
            .SchemaVersion = 1,
            .Source = "DISM GUI WIM 360 (read-only export)",
            .ImageFile = audit.ImageFile,
            .ImageIndex = audit.Index,
            .MountFolder = audit.MountFolder,
            .ScannedUtc = audit.ScannedUtc,
            .FeatureWarning = audit.FeatureWarning,
            .CapabilityWarning = audit.CapabilityWarning,
            .Rows = rows.ToArray()
        }
        Return JsonSerializer.Serialize(snapshot, New JsonSerializerOptions With {
            .WriteIndented = True
        })
    End Function
End Class
