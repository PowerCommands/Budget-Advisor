using System.Text;

namespace BudgetAdvisor.App.Imports;

public sealed class TransactionImportFile
{
    private string? _textContent;

    public string FileName { get; init; } = string.Empty;

    public string ContentType { get; init; } = string.Empty;

    public byte[] Content { get; init; } = [];

    public bool HasExtension(string extension) =>
        !string.IsNullOrWhiteSpace(extension) &&
        FileName.EndsWith(extension, StringComparison.OrdinalIgnoreCase);

    public string GetTextContent()
    {
        _textContent ??= DecodeFileContent(Content);
        return _textContent;
    }

    private static string DecodeFileContent(byte[] bytes)
    {
        if (bytes.Length == 0)
        {
            return string.Empty;
        }

        var utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: false);
        var utf8Content = utf8.GetString(bytes);
        if (!utf8Content.Contains('\uFFFD'))
        {
            return utf8Content;
        }

        return Encoding.Latin1.GetString(bytes);
    }
}
