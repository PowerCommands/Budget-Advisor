using BudgetAdvisor.Domain.Models;
using System.Globalization;
using System.Text;

namespace BudgetAdvisor.App.Imports;

public sealed class SkandiabankenCsvTransactionImporter : ITransactionImporter
{
    private const string BookingDateColumn = "Bokforingsdag";
    private const string AmountColumn = "Belopp";
    private const string SenderColumn = "Avsandare";
    private const string RecipientColumn = "Mottagare";
    private const string NameColumn = "Namn";
    private const string SubjectColumn = "Rubrik";
    private const string BalanceColumn = "Saldo";
    private const string CurrencyColumn = "Valuta";

    public string ImporterKey => "nordea.csv";

    public string DisplayName => "Nordea";

    public string LogoPath => "images/import-nordea.svg";

    public bool CanImport(TransactionImportFile file)
    {
        if (!file.HasExtension(".csv"))
        {
            return false;
        }

        var fileContent = file.GetTextContent();
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

        var headerColumns = ParseDelimitedLine(headerLine, ';')
            .Select(NormalizeHeader)
            .ToList();

        return headerColumns.Contains(BookingDateColumn, StringComparer.OrdinalIgnoreCase)
               && headerColumns.Contains(AmountColumn, StringComparer.OrdinalIgnoreCase)
               && headerColumns.Contains(SenderColumn, StringComparer.OrdinalIgnoreCase)
               && headerColumns.Contains(RecipientColumn, StringComparer.OrdinalIgnoreCase)
               && headerColumns.Contains(NameColumn, StringComparer.OrdinalIgnoreCase)
               && headerColumns.Contains(SubjectColumn, StringComparer.OrdinalIgnoreCase)
               && headerColumns.Contains(BalanceColumn, StringComparer.OrdinalIgnoreCase)
               && headerColumns.Contains(CurrencyColumn, StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyList<ImportedTransactionCandidate> Parse(TransactionImportFile file)
    {
        var fileContent = file.GetTextContent();
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

        var rawHeaderColumns = ParseDelimitedLine(headerLine, ';');
        var normalizedHeaderColumns = rawHeaderColumns.Select(NormalizeHeader).ToList();

        var bookingDateIndex = GetRequiredColumnIndex(normalizedHeaderColumns, BookingDateColumn);
        var amountIndex = GetRequiredColumnIndex(normalizedHeaderColumns, AmountColumn);
        var senderIndex = GetRequiredColumnIndex(normalizedHeaderColumns, SenderColumn);
        var recipientIndex = GetRequiredColumnIndex(normalizedHeaderColumns, RecipientColumn);
        var nameIndex = GetRequiredColumnIndex(normalizedHeaderColumns, NameColumn);
        var subjectIndex = GetRequiredColumnIndex(normalizedHeaderColumns, SubjectColumn);
        var balanceIndex = GetRequiredColumnIndex(normalizedHeaderColumns, BalanceColumn);
        var currencyIndex = GetRequiredColumnIndex(normalizedHeaderColumns, CurrencyColumn);

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
            if (columns.Count < rawHeaderColumns.Count)
            {
                throw new TransactionImportException($"The Nordea CSV file could not be parsed on line {lineNumber}.");
            }

            try
            {
                var amount = ParseSkandiabankenDecimal(columns[amountIndex]);
                if (amount == 0m)
                {
                    continue;
                }

                var transactionDate = DateOnly.ParseExact(columns[bookingDateIndex], "yyyy/MM/dd", CultureInfo.InvariantCulture);
                var description = BuildDescription(
                    columns[senderIndex],
                    columns[recipientIndex],
                    columns[nameIndex],
                    columns[subjectIndex]);
                var balance = ParseSkandiabankenDecimal(columns[balanceIndex]);
                var currency = NormalizeValue(columns[currencyIndex]);

                candidates.Add(new ImportedTransactionCandidate
                {
                    TransactionDate = transactionDate,
                    Description = description,
                    Amount = amount,
                    Balance = balance,
                    Currency = string.IsNullOrWhiteSpace(currency) ? "SEK" : currency,
                    SourceBank = "Nordea",
                    SourceFormat = "Nordea CSV"
                });
            }
            catch (FormatException)
            {
                throw new TransactionImportException($"The Nordea CSV file contains invalid data on line {lineNumber}.");
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

        throw new TransactionImportException($"The Nordea CSV file is missing the required column \"{columnName}\".");
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
            throw new TransactionImportException("The Nordea CSV file contains an unterminated quoted value.");
        }

        values.Add(NormalizeValue(current.ToString()));
        return values;
    }

    private static string NormalizeValue(string value) => value.Trim().Trim('\uFEFF');

    private static string NormalizeHeader(string value)
    {
        var normalized = NormalizeValue(value);
        normalized = normalized.Replace('ö', 'o').Replace('Ö', 'O');
        normalized = normalized.Replace('ä', 'a').Replace('Ä', 'A');
        normalized = normalized.Replace('å', 'a').Replace('Å', 'A');
        return normalized;
    }

    private static decimal ParseSkandiabankenDecimal(string value)
    {
        var normalized = NormalizeValue(value)
            .Replace(" ", string.Empty, StringComparison.Ordinal)
            .Replace('\u00A0'.ToString(), string.Empty, StringComparison.Ordinal)
            .Replace(".", string.Empty, StringComparison.Ordinal)
            .Replace(',', '.');

        return decimal.Parse(normalized, NumberStyles.Number | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
    }

    private static string BuildDescription(string sender, string recipient, string name, string subject)
    {
        var parts = new[]
        {
            NormalizeValue(name),
            NormalizeValue(subject),
            NormalizeValue(recipient),
            NormalizeValue(sender)
        };

        var description = string.Join(" - ", parts.Where(static part => !string.IsNullOrWhiteSpace(part)).Distinct(StringComparer.OrdinalIgnoreCase));
        return string.IsNullOrWhiteSpace(description) ? "Transaction" : description;
    }
}
