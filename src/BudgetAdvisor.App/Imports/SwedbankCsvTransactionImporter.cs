using BudgetAdvisor.Domain.Models;
using System.Globalization;
using System.Text;

namespace BudgetAdvisor.App.Imports;

public sealed class SwedbankCsvTransactionImporter : ITransactionImporter
{
    private const string MetadataPrefix = "Transaktioner Period ";
    private const string DescriptionColumn = "Beskrivning";
    private const string TransactionDateColumn = "Transaktionsdag";
    private const string AmountColumn = "Belopp";
    private const string BalanceColumn = "Bokfört saldo";
    private const string CurrencyColumn = "Valuta";

    public string ImporterKey => "swedbank.csv";

    public string DisplayName => "Swedbank";

    public string LogoPath => "images/import-swedbank.svg";

    public bool CanImport(string fileContent)
    {
        if (string.IsNullOrWhiteSpace(fileContent))
        {
            return false;
        }

        using var reader = new StringReader(fileContent);
        var metadataLine = ReadNextContentLine(reader);
        var headerLine = ReadNextContentLine(reader);
        if (metadataLine is null || headerLine is null)
        {
            return false;
        }

        var normalizedMetadataLine = NormalizeMetadataLine(metadataLine);
        var headerColumns = ParseCsvLine(headerLine);
        var hasRequiredColumns =
            headerColumns.Contains(DescriptionColumn, StringComparer.OrdinalIgnoreCase) &&
            headerColumns.Contains(TransactionDateColumn, StringComparer.OrdinalIgnoreCase) &&
            headerColumns.Contains(AmountColumn, StringComparer.OrdinalIgnoreCase) &&
            headerColumns.Contains(BalanceColumn, StringComparer.OrdinalIgnoreCase) &&
            headerColumns.Contains(CurrencyColumn, StringComparer.OrdinalIgnoreCase);

        if (!hasRequiredColumns)
        {
            return false;
        }

        return normalizedMetadataLine.StartsWith(MetadataPrefix, StringComparison.OrdinalIgnoreCase)
               || headerColumns.Contains("Radnummer", StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyList<ImportedTransactionCandidate> Parse(string fileContent)
    {
        if (string.IsNullOrWhiteSpace(fileContent))
        {
            throw new TransactionImportException("The selected file is empty.");
        }

        using var reader = new StringReader(fileContent);
        var metadataLine = ReadNextContentLine(reader);
        if (metadataLine is null)
        {
            throw new TransactionImportException("The selected file is empty.");
        }

        var headerLine = ReadNextContentLine(reader);
        if (headerLine is null)
        {
            throw new TransactionImportException("The Swedbank CSV file is missing the header row.");
        }

        var headerColumns = ParseCsvLine(headerLine);
        var descriptionIndex = GetRequiredColumnIndex(headerColumns, DescriptionColumn);
        var transactionDateIndex = GetRequiredColumnIndex(headerColumns, TransactionDateColumn);
        var amountIndex = GetRequiredColumnIndex(headerColumns, AmountColumn);
        var balanceIndex = GetRequiredColumnIndex(headerColumns, BalanceColumn);
        var currencyIndex = GetRequiredColumnIndex(headerColumns, CurrencyColumn);

        var transactions = new List<ImportedTransactionCandidate>();
        var lineNumber = 2;

        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            lineNumber++;

            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var columns = ParseCsvLine(line);
            if (columns.Count < headerColumns.Count)
            {
                throw new TransactionImportException($"The Swedbank CSV file could not be parsed on line {lineNumber}.");
            }

            try
            {
                var amount = decimal.Parse(columns[amountIndex], NumberStyles.Number | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
                if (amount == 0m)
                {
                    continue;
                }

                var transactionDate = DateOnly.ParseExact(columns[transactionDateIndex], "yyyy-MM-dd", CultureInfo.InvariantCulture);
                var description = columns[descriptionIndex].Trim();
                var balance = decimal.Parse(columns[balanceIndex], NumberStyles.Number | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
                var currency = columns[currencyIndex].Trim();

                transactions.Add(new ImportedTransactionCandidate
                {
                    Description = string.IsNullOrWhiteSpace(description) ? "Transaction" : description,
                    TransactionDate = transactionDate,
                    Amount = amount,
                    Balance = balance,
                    Currency = string.IsNullOrWhiteSpace(currency) ? null : currency,
                    SourceBank = "Swedbank",
                    SourceFormat = "Swedbank CSV"
                });
            }
            catch (FormatException)
            {
                throw new TransactionImportException($"The Swedbank CSV file contains invalid data on line {lineNumber}.");
            }
        }

        return transactions
            .OrderByDescending(transaction => transaction.TransactionDate)
            .ThenBy(transaction => transaction.Description)
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

        throw new TransactionImportException($"The Swedbank CSV file is missing the required column \"{columnName}\".");
    }

    private static IReadOnlyList<string> ParseCsvLine(string line)
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

            if (character == ',' && !insideQuotes)
            {
                values.Add(NormalizeCsvValue(current.ToString()));
                current.Clear();
                continue;
            }

            current.Append(character);
        }

        if (insideQuotes)
        {
            throw new TransactionImportException("The Swedbank CSV file contains an unterminated quoted value.");
        }

        values.Add(NormalizeCsvValue(current.ToString()));
        return values;
    }

    private static string NormalizeCsvValue(string value) => value.Trim().Trim('\uFEFF');

    private static string NormalizeMetadataLine(string value)
    {
        var normalized = NormalizeCsvValue(value);

        while (normalized.Length > 0 &&
               (char.IsWhiteSpace(normalized[0]) || normalized[0] is '*' or '-' or '\u2022'))
        {
            normalized = normalized[1..].TrimStart();
        }

        return normalized;
    }
}
