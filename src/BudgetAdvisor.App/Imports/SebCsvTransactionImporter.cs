using BudgetAdvisor.Domain.Models;
using System.Globalization;
using System.Text;

namespace BudgetAdvisor.App.Imports;

public sealed class SebCsvTransactionImporter : ITransactionImporter
{
    private const string BookingDateColumn = "Bokföringsdatum";
    private const string TransactionDateColumn = "Transaktionsdatum";
    private const string DescriptionColumn = "Text";
    private const string AmountColumn = "Belopp";
    private const string BalanceColumn = "Saldo";

    public string ImporterKey => "seb.csv";

    public string DisplayName => "SEB";

    public string LogoPath => "images/import-seb.svg";

    public bool CanImport(string fileContent)
    {
        if (string.IsNullOrWhiteSpace(fileContent))
        {
            return false;
        }

        using var reader = new StringReader(fileContent);
        var headerLine = ReadNextContentLine(reader);
        if (headerLine is null)
        {
            return false;
        }

        var headerColumns = ParseDelimitedLine(headerLine, ';');
        return headerColumns.Contains(BookingDateColumn, StringComparer.OrdinalIgnoreCase)
               && headerColumns.Contains(DescriptionColumn, StringComparer.OrdinalIgnoreCase)
               && headerColumns.Contains(AmountColumn, StringComparer.OrdinalIgnoreCase)
               && headerColumns.Contains(BalanceColumn, StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyList<ImportedTransactionCandidate> Parse(string fileContent)
    {
        if (string.IsNullOrWhiteSpace(fileContent))
        {
            throw new TransactionImportException("The selected file is empty.");
        }

        using var reader = new StringReader(fileContent);
        var headerLine = ReadNextContentLine(reader);
        if (headerLine is null)
        {
            throw new TransactionImportException("The selected file is empty.");
        }

        var headerColumns = ParseDelimitedLine(headerLine, ';');
        var bookingDateIndex = GetRequiredColumnIndex(headerColumns, BookingDateColumn);
        var transactionDateIndex = GetOptionalColumnIndex(headerColumns, TransactionDateColumn);
        var descriptionIndex = GetRequiredColumnIndex(headerColumns, DescriptionColumn);
        var amountIndex = GetRequiredColumnIndex(headerColumns, AmountColumn);
        var balanceIndex = GetRequiredColumnIndex(headerColumns, BalanceColumn);

        var candidates = new List<ImportedTransactionCandidate>();
        var lineNumber = 1;

        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            lineNumber++;

            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var columns = ParseDelimitedLine(line, ';');
            if (columns.Count < headerColumns.Count)
            {
                throw new TransactionImportException($"The SEB CSV file could not be parsed on line {lineNumber}.");
            }

            try
            {
                var amount = ParseSebDecimal(columns[amountIndex]);
                if (amount >= 0m)
                {
                    continue;
                }

                var transactionDateValue = transactionDateIndex >= 0
                    ? columns[transactionDateIndex]
                    : columns[bookingDateIndex];

                var transactionDate = DateOnly.ParseExact(transactionDateValue, "yyyy-MM-dd", CultureInfo.InvariantCulture);
                var description = columns[descriptionIndex].Trim();
                var balance = ParseSebDecimal(columns[balanceIndex]);

                candidates.Add(new ImportedTransactionCandidate
                {
                    TransactionDate = transactionDate,
                    Description = string.IsNullOrWhiteSpace(description) ? "Transaction" : description,
                    Amount = Math.Abs(amount),
                    Balance = balance,
                    Currency = "SEK",
                    SourceBank = "SEB",
                    SourceFormat = "SEB CSV"
                });
            }
            catch (FormatException)
            {
                throw new TransactionImportException($"The SEB CSV file contains invalid data on line {lineNumber}.");
            }
        }

        return candidates
            .OrderByDescending(candidate => candidate.TransactionDate)
            .ThenBy(candidate => candidate.Description)
            .ToList();
    }

    private static string? ReadNextContentLine(StringReader reader)
    {
        while (reader.ReadLine() is { } line)
        {
            if (!string.IsNullOrWhiteSpace(line))
            {
                return line;
            }
        }

        return null;
    }

    private static int GetRequiredColumnIndex(IReadOnlyList<string> headerColumns, string columnName)
    {
        for (var index = 0; index < headerColumns.Count; index++)
        {
            if (string.Equals(headerColumns[index], columnName, StringComparison.OrdinalIgnoreCase))
            {
                return index;
            }
        }

        throw new TransactionImportException($"The SEB CSV file is missing the required column \"{columnName}\".");
    }

    private static int GetOptionalColumnIndex(IReadOnlyList<string> headerColumns, string columnName)
    {
        for (var index = 0; index < headerColumns.Count; index++)
        {
            if (string.Equals(headerColumns[index], columnName, StringComparison.OrdinalIgnoreCase))
            {
                return index;
            }
        }

        return -1;
    }

    private static IReadOnlyList<string> ParseDelimitedLine(string line, char separator)
    {
        var values = new List<string>();
        var current = new StringBuilder();
        var insideQuotes = false;

        for (var index = 0; index < line.Length; index++)
        {
            var character = line[index];
            if (character == '"')
            {
                if (insideQuotes && index + 1 < line.Length && line[index + 1] == '"')
                {
                    current.Append('"');
                    index++;
                    continue;
                }

                insideQuotes = !insideQuotes;
                continue;
            }

            if (character == separator && !insideQuotes)
            {
                values.Add(NormalizeValue(current.ToString()));
                current.Clear();
                continue;
            }

            current.Append(character);
        }

        if (insideQuotes)
        {
            throw new TransactionImportException("The SEB CSV file contains an unterminated quoted value.");
        }

        values.Add(NormalizeValue(current.ToString()));
        return values;
    }

    private static string NormalizeValue(string value) => value.Trim().Trim('\uFEFF');

    private static decimal ParseSebDecimal(string value)
    {
        var normalized = NormalizeValue(value)
            .Replace(" ", string.Empty, StringComparison.Ordinal)
            .Replace('\u00A0'.ToString(), string.Empty, StringComparison.Ordinal)
            .Replace(".", string.Empty, StringComparison.Ordinal)
            .Replace(',', '.');

        return decimal.Parse(normalized, NumberStyles.Number | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
    }
}
