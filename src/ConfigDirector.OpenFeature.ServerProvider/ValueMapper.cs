using System.Globalization;
using System.Text.Json;
using OpenFeature.Model;
using OpenFeatureValue = OpenFeature.Model.Value;

namespace ConfigDirector.OpenFeature;

internal static class ValueMapper
{
    private const double LargestExactWholeNumber = 9007199254740992d;

    internal static OpenFeatureValue ToValue(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                var members = new Dictionary<string, OpenFeatureValue>(StringComparer.Ordinal);
                foreach (var member in element.EnumerateObject())
                {
                    members[member.Name] = ToValue(member.Value);
                }

                return new OpenFeatureValue(new Structure(members));

            case JsonValueKind.Array:
                var items = new List<OpenFeatureValue>(element.GetArrayLength());
                foreach (var item in element.EnumerateArray())
                {
                    items.Add(ToValue(item));
                }

                return new OpenFeatureValue(items);

            case JsonValueKind.String:
                return new OpenFeatureValue(element.GetString()!);

            case JsonValueKind.Number:
                return new OpenFeatureValue(element.GetDouble());

            case JsonValueKind.True:
                return new OpenFeatureValue(true);

            case JsonValueKind.False:
                return new OpenFeatureValue(false);

            default:
                return new OpenFeatureValue();
        }
    }

    internal static JsonElement ToJsonElement(OpenFeatureValue value)
    {
        using var buffer = new MemoryStream();
        using (var json = new Utf8JsonWriter(buffer))
        {
            Write(json, value);
        }

        buffer.Position = 0;
        using var document = JsonDocument.Parse(buffer);
        return document.RootElement.Clone();
    }

    internal static TraitCollection ToTraits(Structure structure)
    {
        var traits = new TraitCollection();
        foreach (var member in structure)
        {
            traits[member.Key] = ToTrait(member.Value);
        }

        return traits;
    }

    internal static TraitValue ToTrait(OpenFeatureValue value)
    {
        if (value.IsBoolean)
        {
            return value.AsBoolean == true;
        }

        if (value.IsString)
        {
            return value.AsString;
        }

        if (value.IsNumber)
        {
            return ToTrait(value.AsDouble!.Value);
        }

        if (value.IsDateTime)
        {
            return value.AsDateTime!.Value.ToString("o", CultureInfo.InvariantCulture);
        }

        if (value.IsStructure)
        {
            return TraitValue.FromObject(ToTraits(value.AsStructure!));
        }

        if (value.IsList)
        {
            return TraitValue.FromArray(value.AsList!.Select(ToTrait));
        }

        return TraitValue.Null;
    }

    private static TraitValue ToTrait(double number) =>
        number == Math.Floor(number) && Math.Abs(number) <= LargestExactWholeNumber
            ? (long)number
            : number;

    private static void Write(Utf8JsonWriter json, OpenFeatureValue value)
    {
        if (value.IsBoolean)
        {
            json.WriteBooleanValue(value.AsBoolean == true);
        }
        else if (value.IsString)
        {
            json.WriteStringValue(value.AsString);
        }
        else if (value.IsNumber)
        {
            json.WriteNumberValue(value.AsDouble!.Value);
        }
        else if (value.IsDateTime)
        {
            json.WriteStringValue(value.AsDateTime!.Value);
        }
        else if (value.IsStructure)
        {
            json.WriteStartObject();
            foreach (var member in value.AsStructure!)
            {
                json.WritePropertyName(member.Key);
                Write(json, member.Value);
            }

            json.WriteEndObject();
        }
        else if (value.IsList)
        {
            json.WriteStartArray();
            foreach (var item in value.AsList!)
            {
                Write(json, item);
            }

            json.WriteEndArray();
        }
        else
        {
            json.WriteNullValue();
        }
    }
}
