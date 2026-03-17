using BudgetAdvisor.Domain.Enums;
using BudgetAdvisor.Domain.Models;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BudgetAdvisor.Domain.Serialization;

public sealed class IncomeEntryJsonConverter : JsonConverter<IncomeEntry>
{
    public override IncomeEntry Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        var root = document.RootElement;

        var entry = new IncomeEntry
        {
            Id = TryGetGuid(root, nameof(IncomeEntry.Id)) ?? Guid.NewGuid(),
            MemberId = TryGetGuid(root, nameof(IncomeEntry.MemberId)) ?? Guid.Empty,
            Amount = TryGetDecimal(root, nameof(IncomeEntry.Amount)),
            Year = TryGetInt(root, nameof(IncomeEntry.Year)),
            Month = TryGetInt(root, nameof(IncomeEntry.Month)),
            Type = ReadType(root),
            Metadata = TryGetString(root, nameof(IncomeEntry.Metadata)),
            SavingsAccountId = TryGetGuid(root, nameof(IncomeEntry.SavingsAccountId)),
            AssetId = TryGetGuid(root, nameof(IncomeEntry.AssetId)),
            SeriesId = TryGetGuid(root, nameof(IncomeEntry.SeriesId))
        };

        return entry;
    }

    public override void Write(Utf8JsonWriter writer, IncomeEntry value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteString(nameof(IncomeEntry.Id), value.Id);
        writer.WriteString(nameof(IncomeEntry.MemberId), value.MemberId);
        writer.WriteNumber(nameof(IncomeEntry.Amount), value.Amount);
        writer.WriteNumber(nameof(IncomeEntry.Year), value.Year);
        writer.WriteNumber(nameof(IncomeEntry.Month), value.Month);
        writer.WriteString(nameof(IncomeEntry.Type), value.Type);
        writer.WriteString(nameof(IncomeEntry.Metadata), value.Metadata);

        if (value.SavingsAccountId.HasValue)
        {
            writer.WriteString(nameof(IncomeEntry.SavingsAccountId), value.SavingsAccountId.Value);
        }
        else
        {
            writer.WriteNull(nameof(IncomeEntry.SavingsAccountId));
        }

        if (value.AssetId.HasValue)
        {
            writer.WriteString(nameof(IncomeEntry.AssetId), value.AssetId.Value);
        }
        else
        {
            writer.WriteNull(nameof(IncomeEntry.AssetId));
        }

        if (value.SeriesId.HasValue)
        {
            writer.WriteString(nameof(IncomeEntry.SeriesId), value.SeriesId.Value);
        }
        else
        {
            writer.WriteNull(nameof(IncomeEntry.SeriesId));
        }

        writer.WriteEndObject();
    }

    private static string ReadType(JsonElement root)
    {
        if (!root.TryGetProperty(nameof(IncomeEntry.Type), out var typeElement))
        {
            return "Other";
        }

        return typeElement.ValueKind switch
        {
            JsonValueKind.String => typeElement.GetString()?.Trim() ?? "Other",
            JsonValueKind.Number when typeElement.TryGetInt32(out var legacyValue) => MapLegacyType(legacyValue),
            _ => "Other"
        };
    }

    private static string MapLegacyType(int legacyValue) =>
        Enum.IsDefined(typeof(IncomeType), legacyValue)
            ? ((IncomeType)legacyValue) switch
            {
                IncomeType.Salary => "Salary",
                IncomeType.TaxRefund => "Tax refund",
                IncomeType.Inheritance => "Inheritance",
                IncomeType.Gift => "Gift",
                _ => "Other"
            }
            : "Other";

    private static Guid? TryGetGuid(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var property) || property.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        return property.TryGetGuid(out var value) ? value : null;
    }

    private static decimal TryGetDecimal(JsonElement root, string propertyName) =>
        root.TryGetProperty(propertyName, out var property) && property.TryGetDecimal(out var value) ? value : 0m;

    private static int TryGetInt(JsonElement root, string propertyName) =>
        root.TryGetProperty(propertyName, out var property) && property.TryGetInt32(out var value) ? value : 0;

    private static string TryGetString(JsonElement root, string propertyName) =>
        root.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()?.Trim() ?? string.Empty
            : string.Empty;
}
