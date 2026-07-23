// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.Json;

namespace Microsoft.AspNetCore.Components.Endpoints;

internal sealed class JsonStoredDataSerializer : IStoredDataSerializer
{
    private static readonly JsonSerializerOptions _options = new(JsonSerializerDefaults.Web);

    // These are taken from the set of types supported by TempDataDictionary in ASP.NET Core MVC
    private static readonly Type[] _scalarTypes =[typeof(int), typeof(bool), typeof(string), typeof(Guid), typeof(DateTime)];

    // Enums are stored as their Int32 value, so only enums whose underlying type always fits in an Int32 are supported.
    private static readonly HashSet<Type> _int32EnumUnderlyingTypes =
        [typeof(byte), typeof(sbyte), typeof(short), typeof(ushort), typeof(int)];

    private static readonly Dictionary<string, Type> _nameToType = BuildNameToType();
    private static readonly HashSet<Type> _supportedTypes = [.. _nameToType.Values];

    private static string GetTypeName(Type type) => type.ToString();

    private static Dictionary<string, Type> BuildNameToType()
    {
        var map = new Dictionary<string, Type>(StringComparer.Ordinal);

        foreach (var scalar in _scalarTypes)
        {
            Add(map, scalar);
            AddCollectionTypes(map, scalar);

            if (scalar.IsValueType)
            {
                var nullable = typeof(Nullable<>).MakeGenericType(scalar);
                Add(map, nullable);
                AddCollectionTypes(map, nullable);
            }
        }

        Add(map, typeof(object[]));
        return map;
    }

    private static void AddCollectionTypes(Dictionary<string, Type> map, Type element)
    {
        Add(map, element.MakeArrayType());
        Add(map, typeof(List<>).MakeGenericType(element));
        Add(map, typeof(HashSet<>).MakeGenericType(element));
        Add(map, typeof(SortedSet<>).MakeGenericType(element));
        Add(map, typeof(Collection<>).MakeGenericType(element));
        Add(map, typeof(ObservableCollection<>).MakeGenericType(element));
        Add(map, typeof(Dictionary<,>).MakeGenericType(typeof(string), element));
    }

    private static void Add(Dictionary<string, Type> map, Type type) => map[GetTypeName(type)] = type;

    public IDictionary<string, TempDataValue> DeserializeData(IDictionary<string, JsonElement> data)
    {
        var result = new Dictionary<string, TempDataValue>(data.Count);

        foreach (var (key, element) in data)
        {
            result[key] = DeserializeEntry(element);
        }
        return result;
    }

    private static TempDataValue DeserializeEntry(JsonElement element)
    {
        if (element.ValueKind is JsonValueKind.Null)
        {
            return default;
        }

        var typeName = element.GetProperty("type").GetString()!;
        var valueElement = element.GetProperty("value");

        if (!_nameToType.TryGetValue(typeName, out var type))
        {
            throw new InvalidOperationException($"Cannot deserialize type '{typeName}'.");
        }

        // object[] is the only kind stored recursively (each element carries its own type token),
        // so it is rebuilt element-by-element rather than delegated to System.Text.Json.
        if (type == typeof(object[]))
        {
            var array = new object?[valueElement.GetArrayLength()];
            var index = 0;
            foreach (var item in valueElement.EnumerateArray())
            {
                array[index++] = DeserializeEntry(item).Value;
            }
            return new TempDataValue(array, type);
        }

        var value = JsonSerializer.Deserialize(valueElement, type, _options);
        return new TempDataValue(value, type);
    }

    public bool CanSerialize(Type type) => TryGetStorageType(type, out _);

    private static bool TryGetStorageType(Type type, out Type storageType)
    {
        if (_supportedTypes.Contains(type))
        {
            storageType = type;
            return true;
        }

        if (IsInt32Enum(type))
        {
            storageType = typeof(int);
            return true;
        }

        if (type.IsArray && IsInt32Enum(type.GetElementType()!))
        {
            storageType = typeof(int[]);
            return true;
        }

        storageType = type;
        return false;
    }

    private static bool IsInt32Enum(Type type)
        => type.IsEnum && _int32EnumUnderlyingTypes.Contains(type.GetEnumUnderlyingType());

    public byte[] SerializeData(IDictionary<string, TempDataValue> data)
    {
        using var buffer = new MemoryStream();
        using var writer = new Utf8JsonWriter(buffer);

        writer.WriteStartObject();

        foreach (var (key, (value, type)) in data)
        {
            writer.WritePropertyName(key);
            WriteEntry(writer, value, type);
        }

        writer.WriteEndObject();
        writer.Flush();

        return buffer.ToArray();
    }

    private static void WriteEntry(Utf8JsonWriter writer, object? value, Type? type)
    {
        if (value is null)
        {
            writer.WriteNullValue();
            return;
        }

        var valueType = type ?? value.GetType();
        if (!TryGetStorageType(valueType, out var storageType))
        {
            throw new InvalidOperationException($"Cannot serialize type '{valueType}'.");
        }

        var writeValue = NormalizeEnums(value, valueType, storageType);

        writer.WriteStartObject();
        writer.WriteString("type", GetTypeName(storageType));
        writer.WritePropertyName("value");

        // object[] is the only kind stored recursively (each element carries its own type token),
        // so it is written element-by-element rather than delegated to System.Text.Json.
        if (storageType == typeof(object[]))
        {
            writer.WriteStartArray();
            foreach (var item in (object?[])writeValue)
            {
                WriteEntry(writer, item, item?.GetType());
            }
            writer.WriteEndArray();
        }
        else
        {
            JsonSerializer.Serialize(writer, writeValue, storageType, _options);
        }

        writer.WriteEndObject();
    }

    public byte[] SerializeValue(object value, Type type)
    {
        using var buffer = new MemoryStream();
        using var writer = new Utf8JsonWriter(buffer);

        WriteEntry(writer, value, type);
        writer.Flush();

        return buffer.ToArray();
    }

    public TempDataValue DeserializeValue(ReadOnlySpan<byte> utf8Json)
    {
        var element = JsonSerializer.Deserialize<JsonElement>(utf8Json, _options);
        return DeserializeEntry(element);
    }

    // Enums have no direct JSON representation, so they are converted to their Int32 form to match
    // the "int"/"int[]" storage type resolved by TryGetStorageType. All other values pass through.
    private static object NormalizeEnums(object value, Type valueType, Type storageType)
    {
        if (storageType == typeof(int) && valueType.IsEnum)
        {
            return Convert.ToInt32(value, CultureInfo.InvariantCulture);
        }

        if (storageType == typeof(int[]) && valueType != typeof(int[]))
        {
            return ConvertEnumsToInts((IEnumerable)value);
        }

        return value;
    }

    private static int[] ConvertEnumsToInts(IEnumerable values)
    {
        var result = new List<int>();
        foreach (var item in values)
        {
            result.Add(Convert.ToInt32(item, CultureInfo.InvariantCulture));
        }
        return result.ToArray();
    }
}
