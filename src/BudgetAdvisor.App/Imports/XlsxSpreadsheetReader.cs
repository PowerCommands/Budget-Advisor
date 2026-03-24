using System.Globalization;
using System.IO.Compression;
using System.Xml.Linq;

namespace BudgetAdvisor.App.Imports;

internal static class XlsxSpreadsheetReader
{
    private static readonly XNamespace SpreadsheetNamespace = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly XNamespace RelationshipsNamespace = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private static readonly HashSet<int> BuiltInDateFormatIds =
    [
        14, 15, 16, 17, 18, 19, 20, 21, 22,
        27, 28, 29, 30, 31, 32, 33, 34, 35, 36,
        45, 46, 47, 50, 51, 52, 53, 54, 55, 56, 57, 58
    ];

    public static IReadOnlyList<IReadOnlyList<SpreadsheetCellValue>> ReadRows(byte[] content)
    {
        using var stream = new MemoryStream(content, writable: false);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: false);

        var sharedStrings = ReadSharedStrings(archive);
        var dateStyleIndexes = ReadDateStyleIndexes(archive);
        var worksheetDocument = LoadWorksheetDocument(archive);
        var sheetData = worksheetDocument.Root?.Element(SpreadsheetNamespace + "sheetData")
            ?? throw new TransactionImportException("The Excel file is missing worksheet data.");

        var rows = new List<IReadOnlyList<SpreadsheetCellValue>>();
        foreach (var rowElement in sheetData.Elements(SpreadsheetNamespace + "row"))
        {
            var cells = new List<SpreadsheetCellValue>();
            foreach (var cellElement in rowElement.Elements(SpreadsheetNamespace + "c"))
            {
                var reference = (string?)cellElement.Attribute("r") ?? string.Empty;
                var columnIndex = GetColumnIndex(reference);
                var rawValue = GetRawCellValue(cellElement);
                var textValue = ResolveCellText(cellElement, rawValue, sharedStrings);
                var styleIndex = ParseOptionalInt((string?)cellElement.Attribute("s"));
                var isDate = styleIndex.HasValue && dateStyleIndexes.Contains(styleIndex.Value);

                cells.Add(new SpreadsheetCellValue(columnIndex, textValue, rawValue, isDate));
            }

            rows.Add(cells);
        }

        return rows;
    }

    public static DateOnly ParseDate(SpreadsheetCellValue cellValue)
    {
        if (cellValue.IsDate && decimal.TryParse(cellValue.RawValue, NumberStyles.Float, CultureInfo.InvariantCulture, out var numericDate))
        {
            return DateOnly.FromDateTime(DateTime.FromOADate((double)numericDate));
        }

        var candidates = new[]
        {
            cellValue.Text,
            cellValue.RawValue
        };

        foreach (var candidate in candidates.Where(value => !string.IsNullOrWhiteSpace(value)))
        {
            if (DateOnly.TryParseExact(candidate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var isoDate))
            {
                return isoDate;
            }

            if (DateOnly.TryParseExact(candidate, "yyyy-M-d", CultureInfo.InvariantCulture, DateTimeStyles.None, out var shortIsoDate))
            {
                return shortIsoDate;
            }

            if (DateOnly.TryParse(candidate, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedDate))
            {
                return parsedDate;
            }

            if (DateOnly.TryParse(candidate, CultureInfo.GetCultureInfo("sv-SE"), DateTimeStyles.None, out var swedishDate))
            {
                return swedishDate;
            }
        }

        throw new FormatException("The Excel cell does not contain a valid date.");
    }

    public static decimal ParseDecimal(SpreadsheetCellValue cellValue)
    {
        if (decimal.TryParse(cellValue.RawValue, NumberStyles.Float, CultureInfo.InvariantCulture, out var numericValue))
        {
            return numericValue;
        }

        var normalized = cellValue.Text
            .Trim()
            .Replace(" ", string.Empty, StringComparison.Ordinal)
            .Replace('\u00A0'.ToString(), string.Empty, StringComparison.Ordinal)
            .Replace(".", string.Empty, StringComparison.Ordinal)
            .Replace(',', '.');

        return decimal.Parse(normalized, NumberStyles.Number | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
    }

    private static XDocument LoadWorksheetDocument(ZipArchive archive)
    {
        var workbook = LoadRequiredXml(archive, "xl/workbook.xml", "The Excel file is missing the workbook definition.");
        var firstSheet = workbook.Root?
            .Elements(SpreadsheetNamespace + "sheets")
            .Elements(SpreadsheetNamespace + "sheet")
            .FirstOrDefault()
            ?? throw new TransactionImportException("The Excel file does not contain any worksheets.");

        var relationshipId = (string?)firstSheet.Attribute(RelationshipsNamespace + "id")
            ?? throw new TransactionImportException("The Excel worksheet relationship is missing.");

        var workbookRelationships = LoadRequiredXml(archive, "xl/_rels/workbook.xml.rels", "The Excel file is missing workbook relationships.");
        var relationship = workbookRelationships.Root?
            .Elements()
            .FirstOrDefault(element => string.Equals((string?)element.Attribute("Id"), relationshipId, StringComparison.Ordinal))
            ?? throw new TransactionImportException("The Excel worksheet relationship could not be resolved.");

        var target = (string?)relationship.Attribute("Target")
            ?? throw new TransactionImportException("The Excel worksheet target is missing.");

        var normalizedTarget = target.Replace('\\', '/');
        var worksheetPath = normalizedTarget.StartsWith("/", StringComparison.Ordinal)
            ? normalizedTarget.TrimStart('/')
            : $"xl/{normalizedTarget.TrimStart('/')}";

        return LoadRequiredXml(archive, worksheetPath, "The Excel worksheet could not be loaded.");
    }

    private static IReadOnlyList<string> ReadSharedStrings(ZipArchive archive)
    {
        var entry = archive.GetEntry("xl/sharedStrings.xml");
        if (entry is null)
        {
            return [];
        }

        using var stream = entry.Open();
        var document = XDocument.Load(stream);
        return document.Root?
            .Elements(SpreadsheetNamespace + "si")
            .Select(ReadSharedString)
            .ToList()
            ?? [];
    }

    private static HashSet<int> ReadDateStyleIndexes(ZipArchive archive)
    {
        var entry = archive.GetEntry("xl/styles.xml");
        if (entry is null)
        {
            return [];
        }

        using var stream = entry.Open();
        var document = XDocument.Load(stream);
        var customDateFormatIds = document.Root?
            .Element(SpreadsheetNamespace + "numFmts")?
            .Elements(SpreadsheetNamespace + "numFmt")
            .Where(element => IsDateFormatCode((string?)element.Attribute("formatCode")))
            .Select(element => ParseOptionalInt((string?)element.Attribute("numFmtId")))
            .OfType<int>()
            .ToHashSet()
            ?? [];

        var dateStyleIndexes = new HashSet<int>();
        var cellFormats = document.Root?
            .Element(SpreadsheetNamespace + "cellXfs")?
            .Elements(SpreadsheetNamespace + "xf")
            .ToList();

        if (cellFormats is null)
        {
            return dateStyleIndexes;
        }

        for (var styleIndex = 0; styleIndex < cellFormats.Count; styleIndex++)
        {
            var numFmtId = ParseOptionalInt((string?)cellFormats[styleIndex].Attribute("numFmtId"));
            if (numFmtId.HasValue && (BuiltInDateFormatIds.Contains(numFmtId.Value) || customDateFormatIds.Contains(numFmtId.Value)))
            {
                dateStyleIndexes.Add(styleIndex);
            }
        }

        return dateStyleIndexes;
    }

    private static XDocument LoadRequiredXml(ZipArchive archive, string path, string errorMessage)
    {
        var entry = archive.GetEntry(path);
        if (entry is null)
        {
            throw new TransactionImportException(errorMessage);
        }

        using var stream = entry.Open();
        return XDocument.Load(stream);
    }

    private static string ReadSharedString(XElement sharedStringItem)
    {
        var textValues = sharedStringItem
            .Descendants(SpreadsheetNamespace + "t")
            .Select(element => element.Value)
            .ToList();

        return string.Concat(textValues);
    }

    private static string GetRawCellValue(XElement cellElement) =>
        cellElement.Element(SpreadsheetNamespace + "v")?.Value
        ?? cellElement.Element(SpreadsheetNamespace + "is")?.Element(SpreadsheetNamespace + "t")?.Value
        ?? string.Empty;

    private static string ResolveCellText(XElement cellElement, string rawValue, IReadOnlyList<string> sharedStrings)
    {
        var cellType = (string?)cellElement.Attribute("t");
        return cellType switch
        {
            "s" when int.TryParse(rawValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var sharedStringIndex)
                     && sharedStringIndex >= 0
                     && sharedStringIndex < sharedStrings.Count => sharedStrings[sharedStringIndex],
            "inlineStr" => cellElement.Element(SpreadsheetNamespace + "is")?.Value ?? string.Empty,
            _ => rawValue
        };
    }

    private static bool IsDateFormatCode(string? formatCode)
    {
        if (string.IsNullOrWhiteSpace(formatCode))
        {
            return false;
        }

        var normalized = formatCode
            .Replace("\\", string.Empty, StringComparison.Ordinal)
            .Replace("\"", string.Empty, StringComparison.Ordinal)
            .Replace("_", string.Empty, StringComparison.Ordinal)
            .Replace("*", string.Empty, StringComparison.Ordinal)
            .ToLowerInvariant();

        return normalized.Contains('y') ||
               normalized.Contains('m') ||
               normalized.Contains('d') ||
               normalized.Contains('h') ||
               normalized.Contains('s');
    }

    private static int GetColumnIndex(string cellReference)
    {
        var columnIndex = 0;
        foreach (var character in cellReference)
        {
            if (!char.IsLetter(character))
            {
                break;
            }

            columnIndex *= 26;
            columnIndex += char.ToUpperInvariant(character) - 'A' + 1;
        }

        return Math.Max(0, columnIndex - 1);
    }

    private static int? ParseOptionalInt(string? value) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedValue)
            ? parsedValue
            : null;
}

internal sealed record SpreadsheetCellValue(int ColumnIndex, string Text, string RawValue, bool IsDate);
