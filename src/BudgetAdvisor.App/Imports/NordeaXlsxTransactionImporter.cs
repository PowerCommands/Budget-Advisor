using BudgetAdvisor.Domain.Models;

namespace BudgetAdvisor.App.Imports;

public sealed class NordeaXlsxTransactionImporter : ITransactionImporter
{
    public string ImporterKey => "skandiabanken.xlsx";

    public string DisplayName => "Skandiabanken";

    public string LogoPath => "images/skandiabanken.png";

    public bool CanImport(TransactionImportFile file)
    {
        if (!file.HasExtension(".xlsx"))
        {
            return false;
        }

        try
        {
            var headerRow = FindHeaderRow(XlsxSpreadsheetReader.ReadRows(file.Content));
            return headerRow is not null;
        }
        catch (InvalidDataException)
        {
            return false;
        }
        catch (TransactionImportException)
        {
            return false;
        }
    }

    public IReadOnlyList<ImportedTransactionCandidate> Parse(TransactionImportFile file)
    {
        if (file.Content.Length == 0)
        {
            throw new TransactionImportException("The selected file is empty.");
        }

        if (!file.HasExtension(".xlsx"))
        {
            throw new TransactionImportException("The selected file is not a supported Skandiabanken Excel file.");
        }

        IReadOnlyList<IReadOnlyList<SpreadsheetCellValue>> rows;
        try
        {
            rows = XlsxSpreadsheetReader.ReadRows(file.Content);
        }
        catch (InvalidDataException)
        {
            throw new TransactionImportException("The selected Excel file is invalid.");
        }

        var headerRow = FindHeaderRow(rows)
            ?? throw new TransactionImportException("The Skandiabanken Excel file is missing the transaction header row.");

        var transactionDateIndex = GetRequiredColumnIndex(headerRow, IsTransactionDateHeader, "Bokf. datum");
        var descriptionIndex = GetRequiredColumnIndex(headerRow, IsDescriptionHeader, "Beskrivning");
        var amountIndex = GetRequiredColumnIndex(headerRow, IsAmountHeader, "Belopp");
        var balanceIndex = GetRequiredColumnIndex(headerRow, IsBalanceHeader, "Saldo");

        var headerRowIndex = rows
            .Select((row, index) => new { row, index })
            .First(item => ReferenceEquals(item.row, headerRow))
            .index;

        var candidates = new List<ImportedTransactionCandidate>();
        for (var rowIndex = headerRowIndex + 1; rowIndex < rows.Count; rowIndex++)
        {
            var row = rows[rowIndex];
            if (row.Count == 0)
            {
                continue;
            }

            var dateCell = TryGetCell(row, transactionDateIndex);
            var amountCell = TryGetCell(row, amountIndex);
            var balanceCell = TryGetCell(row, balanceIndex);

            if (dateCell is null && amountCell is null && balanceCell is null)
            {
                continue;
            }

            if (dateCell is null || amountCell is null || balanceCell is null)
            {
                throw new TransactionImportException($"The Skandiabanken Excel file contains incomplete data on row {rowIndex + 1}.");
            }

            try
            {
                var amount = XlsxSpreadsheetReader.ParseDecimal(amountCell);
                if (amount == 0m)
                {
                    continue;
                }

                var transactionDate = XlsxSpreadsheetReader.ParseDate(dateCell);
                var description = TryGetCell(row, descriptionIndex)?.Text?.Trim() ?? string.Empty;
                var balance = XlsxSpreadsheetReader.ParseDecimal(balanceCell);

                candidates.Add(new ImportedTransactionCandidate
                {
                    TransactionDate = transactionDate,
                    Description = string.IsNullOrWhiteSpace(description) ? "Transaction" : description,
                    Amount = amount,
                    Balance = balance,
                    Currency = "SEK",
                    SourceBank = "Skandiabanken",
                    SourceFormat = "Skandiabanken Excel"
                });
            }
            catch (FormatException)
            {
                throw new TransactionImportException($"The Skandiabanken Excel file contains invalid data on row {rowIndex + 1}.");
            }
        }

        return candidates
            .OrderByDescending(candidate => candidate.TransactionDate)
            .ThenBy(candidate => candidate.Description)
            .ToList();
    }

    private static IReadOnlyList<SpreadsheetCellValue>? FindHeaderRow(IReadOnlyList<IReadOnlyList<SpreadsheetCellValue>> rows) =>
        rows.FirstOrDefault(row =>
            row.Any(cell => IsTransactionDateHeader(cell.Text)) &&
            row.Any(cell => IsDescriptionHeader(cell.Text)) &&
            row.Any(cell => IsAmountHeader(cell.Text)) &&
            row.Any(cell => IsBalanceHeader(cell.Text)));

    private static int GetRequiredColumnIndex(
        IReadOnlyList<SpreadsheetCellValue> headerRow,
        Func<string, bool> predicate,
        string columnName)
    {
        var cell = headerRow.FirstOrDefault(candidate => predicate(candidate.Text));
        if (cell is null)
        {
            throw new TransactionImportException($"The Skandiabanken Excel file is missing the required column \"{columnName}\".");
        }

        return cell.ColumnIndex;
    }

    private static SpreadsheetCellValue? TryGetCell(IReadOnlyList<SpreadsheetCellValue> row, int columnIndex) =>
        row.FirstOrDefault(cell => cell.ColumnIndex == columnIndex && !string.IsNullOrWhiteSpace(cell.Text + cell.RawValue));

    private static bool IsTransactionDateHeader(string value) => NormalizeHeader(value).StartsWith("bokfdatum", StringComparison.Ordinal);

    private static bool IsDescriptionHeader(string value) => NormalizeHeader(value).StartsWith("beskriv", StringComparison.Ordinal);

    private static bool IsAmountHeader(string value) => NormalizeHeader(value) == "belopp";

    private static bool IsBalanceHeader(string value) => NormalizeHeader(value) == "saldo";

    private static string NormalizeHeader(string value) =>
        new string(value
            .Trim()
            .ToLowerInvariant()
            .Where(char.IsLetterOrDigit)
            .ToArray());
}
