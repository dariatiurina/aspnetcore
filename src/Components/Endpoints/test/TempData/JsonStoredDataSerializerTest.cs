// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections;
using System.Text.Json;

namespace Microsoft.AspNetCore.Components.Endpoints;

public class JsonStoredDataSerializerTest
{
    private static JsonStoredDataSerializer CreateSerializer() => new JsonStoredDataSerializer();

    public static TheoryData<Type> InvalidTypes
    {
        get
        {
            return new TheoryData<Type>
                {
                    { typeof(long) },
                    { typeof(long[]) },
                    { typeof(double) },
                    { typeof(double[]) },
                    { typeof(object) },
                    { typeof(TestItem) },
                    { typeof(List<TestItem>) },
                    { typeof(Dictionary<string, TestItem>) },
                    { typeof(LinkedList<int>) },
                    { typeof(int[,]) },
                };
        }
    }

    public static TheoryData<Type> InvalidDictionaryKeyTypes
    {
        get
        {
            return new TheoryData<Type>
                {
                    { typeof(Dictionary<int, string>) },
                    { typeof(Dictionary<Uri, Guid>) },
                    { typeof(Dictionary<object, string>) },
                    { typeof(Dictionary<TestItem, TestItem>) }
                };
        }
    }

    public static TheoryData<Type> ValidTypes
    {
        get
        {
            return new TheoryData<Type>
                {
                    { typeof(int) },
                    { typeof(int[]) },
                    { typeof(string) },
                    { typeof(string[]) },
                    { typeof(Guid) },
                    { typeof(Guid[]) },
                    { typeof(Dictionary<string, int>) },
                    { typeof(Dictionary<string, string>) },
                    { typeof(Dictionary<string, bool>) },
                    { typeof(Dictionary<string, Guid>) },
                    { typeof(Dictionary<string, DateTime>) },
                    { typeof(DateTime) },
                    { typeof(DateTime[]) },
                    { typeof(bool) },
                    { typeof(bool[]) },
                    { typeof(TestEnum) },
                    { typeof(TestEnum[]) },
                    { typeof(List<TestEnum>) },
                    { typeof(HashSet<TestEnum>) },
                    { typeof(SortedSet<TestEnum>) },
                    { typeof(System.Collections.ObjectModel.Collection<TestEnum>) },
                    { typeof(System.Collections.ObjectModel.ObservableCollection<TestEnum>) },
                    { typeof(Dictionary<string, TestEnum>) },
                    { typeof(List<int>) },
                    { typeof(List<string>) },
                    { typeof(List<bool>) },
                    { typeof(List<Guid>) },
                    { typeof(List<DateTime>) },
                    { typeof(HashSet<int>) },
                    { typeof(SortedSet<int>) },
                    { typeof(System.Collections.ObjectModel.Collection<int>) },
                    { typeof(System.Collections.ObjectModel.ObservableCollection<int>) },
                    { typeof(object[]) },
                    { typeof(int?) },
                    { typeof(bool?) },
                    { typeof(int?[]) },
                    { typeof(DateTime?[]) },
                    { typeof(List<int?>) },
                    { typeof(HashSet<int?>) },
                    { typeof(Dictionary<string, int?>) },
                };
        }
    }

    [Theory]
    [MemberData(nameof(InvalidTypes))]
    public void CanSerialize_ReturnsFalse_OnInvalidType(Type type)
    {
        var serializer = CreateSerializer();

        var result = serializer.CanSerialize(type);

        Assert.False(result);
    }

    [Theory]
    [MemberData(nameof(InvalidDictionaryKeyTypes))]
    public void CanSerialize_ReturnsFalse_OnInvalidDictionaryKeyType(Type type)
    {
        var serializer = CreateSerializer();

        var result = serializer.CanSerialize(type);

        Assert.False(result);
    }

    [Theory]
    [MemberData(nameof(ValidTypes))]
    public void CanSerialize_ReturnsTrue_OnValidType(Type type)
    {
        var serializer = CreateSerializer();

        var result = serializer.CanSerialize(type);

        Assert.True(result);
    }

    public static TheoryData<object, Type> RoundTripData => new()
    {
        { 42, typeof(int) },
        { true, typeof(bool) },
        { "hello", typeof(string) },
        { Guid.Parse("5fa6e1de-d0b4-4272-a629-2e1382af8b51"), typeof(Guid) },
        { new DateTime(2007, 1, 1, 0, 0, 0, DateTimeKind.Utc), typeof(DateTime) },
        { new int[] { 1, 2, 3 }, typeof(int[]) },
        { new string[] { "foo", "bar" }, typeof(string[]) },
        { new bool[] { true, false }, typeof(bool[]) },
        { new DateTime[] { new DateTime(2007, 1, 1, 0, 0, 0, DateTimeKind.Utc), new DateTime(2008, 1, 1, 0, 0, 0, DateTimeKind.Utc) }, typeof(DateTime[]) },
        { new Guid[] { Guid.Parse("5fa6e1de-d0b4-4272-a629-2e1382af8b51"), Guid.Parse("a1b2c3d4-e5f6-7890-abcd-ef1234567890") }, typeof(Guid[]) },
        { new Dictionary<string, int> { { "key1", 1 }, { "key2", 2 } }, typeof(Dictionary<string, int>) },
        { new Dictionary<string, string> { { "key1", "value1" }, { "key2", "value2" } }, typeof(Dictionary<string, string>) },
        { new Dictionary<string, bool> { { "key1", true }, { "key2", false } }, typeof(Dictionary<string, bool>) },
        { new Dictionary<string, Guid> { { "key1", Guid.Parse("5fa6e1de-d0b4-4272-a629-2e1382af8b51") } }, typeof(Dictionary<string, Guid>) },
        { new Dictionary<string, DateTime> { { "key1", new DateTime(2007, 1, 1, 0, 0, 0, DateTimeKind.Utc) } }, typeof(Dictionary<string, DateTime>) },
        { Array.Empty<int>(), typeof(int[]) },
    };

    [Theory]
    [MemberData(nameof(RoundTripData))]
    public void RoundTrip(object value, Type type)
    {
        var serializer = CreateSerializer();
        var serialized = serializer.SerializeData(new Dictionary<string, TempDataValue>
        {
            { "key", (value, type) }
        });

        var jsonDocument = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(serialized);
        var result = serializer.DeserializeData(jsonDocument!);

        Assert.Equal(value, result["key"].Value);
    }

    [Fact]
    public void RoundTrip_Null()
    {
        var serializer = CreateSerializer();
        var serialized = serializer.SerializeData(new Dictionary<string, TempDataValue>
        {
            { "key", (null, null) }
        });

        var jsonDocument = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(serialized);
        var result = serializer.DeserializeData(jsonDocument!);

        Assert.Null(result["key"].Value);
    }

    [Fact]
    public void RoundTrip_Enum()
    {
        var serializer = CreateSerializer();
        var serialized = serializer.SerializeData(new Dictionary<string, TempDataValue>
        {
            { "key", (TestEnum.Value2, typeof(TestEnum)) }
        });

        var jsonDocument = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(serialized);
        var result = serializer.DeserializeData(jsonDocument!);

        // The value is deserialized back into its real enum type, not its underlying int.
        Assert.Equal(TestEnum.Value2, result["key"].Value);
    }

    [Fact]
    public void RoundTrip_EnumArray()
    {
        var serializer = CreateSerializer();
        var serialized = serializer.SerializeData(new Dictionary<string, TempDataValue>
        {
            { "key", (new TestEnum[] { TestEnum.Value1, TestEnum.Value2 }, typeof(TestEnum[])) }
        });

        var jsonDocument = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(serialized);
        var result = serializer.DeserializeData(jsonDocument!);

        var array = Assert.IsType<TestEnum[]>(result["key"].Value);
        Assert.Equal(TestEnum.Value1, array[0]);
        Assert.Equal(TestEnum.Value2, array[1]);
    }

    [Fact]
    public void RoundTrip_LongBackedEnum()
    {
        var serializer = CreateSerializer();
        var serialized = serializer.SerializeData(new Dictionary<string, TempDataValue>
        {
            { "key", (LongEnum.Big, typeof(LongEnum)) }
        });

        var jsonDocument = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(serialized);
        var result = serializer.DeserializeData(jsonDocument!);

        // Any enum is supported now, not just Int32-backed ones.
        Assert.Equal(LongEnum.Big, result["key"].Value);
    }

    [Fact]
    public void RoundTrip_EnumList()
    {
        var serializer = CreateSerializer();
        var serialized = serializer.SerializeData(new Dictionary<string, TempDataValue>
        {
            { "key", (new List<TestEnum> { TestEnum.Value1, TestEnum.Value2 }, typeof(List<TestEnum>)) }
        });

        var jsonDocument = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(serialized);
        var result = serializer.DeserializeData(jsonDocument!);

        var list = Assert.IsType<List<TestEnum>>(result["key"].Value);
        Assert.Equal(new List<TestEnum> { TestEnum.Value1, TestEnum.Value2 }, list);
    }

    [Fact]
    public void RoundTrip_EnumHashSet()
    {
        var serializer = CreateSerializer();
        var serialized = serializer.SerializeData(new Dictionary<string, TempDataValue>
        {
            { "key", (new HashSet<TestEnum> { TestEnum.Value1, TestEnum.Value2 }, typeof(HashSet<TestEnum>)) }
        });

        var jsonDocument = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(serialized);
        var result = serializer.DeserializeData(jsonDocument!);

        var set = Assert.IsType<HashSet<TestEnum>>(result["key"].Value);
        Assert.Equal(new HashSet<TestEnum> { TestEnum.Value1, TestEnum.Value2 }, set);
    }

    [Fact]
    public void RoundTrip_EnumDictionary()
    {
        var serializer = CreateSerializer();
        var serialized = serializer.SerializeData(new Dictionary<string, TempDataValue>
        {
            { "key", (new Dictionary<string, TestEnum> { ["a"] = TestEnum.Value1, ["b"] = TestEnum.Value2 }, typeof(Dictionary<string, TestEnum>)) }
        });

        var jsonDocument = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(serialized);
        var result = serializer.DeserializeData(jsonDocument!);

        var dictionary = Assert.IsType<Dictionary<string, TestEnum>>(result["key"].Value);
        Assert.Equal(TestEnum.Value1, dictionary["a"]);
        Assert.Equal(TestEnum.Value2, dictionary["b"]);
    }

    [Fact]
    public void RoundTrip_List()
    {
        var serializer = CreateSerializer();
        var serialized = serializer.SerializeData(new Dictionary<string, TempDataValue>
        {
            { "key", (new List<int> { 1, 2, 3 }, typeof(List<int>)) }
        });

        var jsonDocument = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(serialized);
        var result = serializer.DeserializeData(jsonDocument!);

        var list = Assert.IsType<List<int>>(result["key"].Value);
        Assert.Equal(new List<int> { 1, 2, 3 }, list);
    }

    [Fact]
    public void RoundTrip_HashSet()
    {
        var serializer = CreateSerializer();
        var serialized = serializer.SerializeData(new Dictionary<string, TempDataValue>
        {
            { "key", (new HashSet<int> { 1, 2, 3 }, typeof(HashSet<int>)) }
        });

        var jsonDocument = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(serialized);
        var result = serializer.DeserializeData(jsonDocument!);

        var set = Assert.IsType<HashSet<int>>(result["key"].Value);
        Assert.Equal(new HashSet<int> { 1, 2, 3 }, set);
    }

    [Fact]
    public void RoundTrip_SortedSet()
    {
        var serializer = CreateSerializer();
        var serialized = serializer.SerializeData(new Dictionary<string, TempDataValue>
        {
            { "key", (new SortedSet<int> { 3, 1, 2 }, typeof(SortedSet<int>)) }
        });

        var jsonDocument = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(serialized);
        var result = serializer.DeserializeData(jsonDocument!);

        var set = Assert.IsType<SortedSet<int>>(result["key"].Value);
        Assert.Equal(new SortedSet<int> { 1, 2, 3 }, set);
    }

    [Fact]
    public void RoundTrip_Collection()
    {
        var serializer = CreateSerializer();
        var serialized = serializer.SerializeData(new Dictionary<string, TempDataValue>
        {
            { "key", (new System.Collections.ObjectModel.Collection<int> { 1, 2, 3 }, typeof(System.Collections.ObjectModel.Collection<int>)) }
        });

        var jsonDocument = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(serialized);
        var result = serializer.DeserializeData(jsonDocument!);

        var collection = Assert.IsType<System.Collections.ObjectModel.Collection<int>>(result["key"].Value);
        Assert.Equal(new[] { 1, 2, 3 }, collection);
    }

    [Fact]
    public void RoundTrip_ObservableCollection()
    {
        var serializer = CreateSerializer();
        var serialized = serializer.SerializeData(new Dictionary<string, TempDataValue>
        {
            { "key", (new System.Collections.ObjectModel.ObservableCollection<int> { 1, 2, 3 }, typeof(System.Collections.ObjectModel.ObservableCollection<int>)) }
        });

        var jsonDocument = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(serialized);
        var result = serializer.DeserializeData(jsonDocument!);

        var collection = Assert.IsType<System.Collections.ObjectModel.ObservableCollection<int>>(result["key"].Value);
        Assert.Equal(new[] { 1, 2, 3 }, collection);
    }

    [Fact]
    public void RoundTrip_ListOfNullableElements()
    {
        var serializer = CreateSerializer();
        var serialized = serializer.SerializeData(new Dictionary<string, TempDataValue>
        {
            { "key", (new List<int?> { 1, null, 3 }, typeof(List<int?>)) }
        });

        var jsonDocument = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(serialized);
        var result = serializer.DeserializeData(jsonDocument!);

        var list = Assert.IsType<List<int?>>(result["key"].Value);
        Assert.Equal(new List<int?> { 1, null, 3 }, list);
    }

    [Fact]
    public void RoundTrip_DictionaryOfNullableElements()
    {
        var serializer = CreateSerializer();
        var serialized = serializer.SerializeData(new Dictionary<string, TempDataValue>
        {
            { "key", (new Dictionary<string, int?> { ["a"] = 1, ["b"] = null }, typeof(Dictionary<string, int?>)) }
        });

        var jsonDocument = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(serialized);
        var result = serializer.DeserializeData(jsonDocument!);

        var dictionary = Assert.IsType<Dictionary<string, int?>>(result["key"].Value);
        Assert.Equal(1, dictionary["a"]);
        Assert.Null(dictionary["b"]);
    }

    [Fact]
    public void SerializeData_UsesTypeNameToken_WithoutAssemblyMetadata()
    {
        var serializer = CreateSerializer();
        var serialized = serializer.SerializeData(new Dictionary<string, TempDataValue>
        {
            { "key", (new Dictionary<string, int> { ["a"] = 1 }, typeof(Dictionary<string, int>)) }
        });

        var json = System.Text.Encoding.UTF8.GetString(serialized);

        // The stored token is the assembly-qualified name, resolved back with Type.GetType on read.
        Assert.Contains("System.Collections.Generic.Dictionary", json);
        Assert.Contains("Version=", json);
    }

    [Fact]
    public void CanSerialize_DoesNotThrow_ForTypeImplementingMultipleCollectionInterfaces()
    {
        var serializer = CreateSerializer();

        var exception = Record.Exception(() => serializer.CanSerialize(typeof(DualCollection)));

        Assert.Null(exception);
    }

    [Fact]
    public void RoundTrip_NullableValue_SerializesAsUnderlyingType()
    {
        var serializer = CreateSerializer();
        int? value = 42;
        var serialized = serializer.SerializeData(new Dictionary<string, TempDataValue>
        {
            { "key", (value, value?.GetType()) }
        });

        var jsonDocument = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(serialized);
        var result = serializer.DeserializeData(jsonDocument!);

        Assert.IsType<int>(result["key"].Value);
        Assert.Equal(42, result["key"].Value);
    }

    [Fact]
    public void RoundTrip_NullNullableValue_SerializesAsNull()
    {
        var serializer = CreateSerializer();
        bool? value = null;
        var serialized = serializer.SerializeData(new Dictionary<string, TempDataValue>
        {
            { "key", (value, value?.GetType()) }
        });

        var jsonDocument = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(serialized);
        var result = serializer.DeserializeData(jsonDocument!);

        Assert.Null(result["key"].Value);
    }

    [Fact]
    public void RoundTrip_NonNullValueWithNullType_UsesRuntimeType()
    {
        var serializer = CreateSerializer();
        var serialized = serializer.SerializeData(new Dictionary<string, TempDataValue>
        {
            { "key", ("hello", null) }
        });

        var jsonDocument = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(serialized);
        var result = serializer.DeserializeData(jsonDocument!);

        Assert.Equal("hello", result["key"].Value);
    }

    [Fact]
    public void RoundTrip_NestedArrays()
    {
        var serializer = CreateSerializer();
        var serialized = serializer.SerializeData(new Dictionary<string, TempDataValue>
        {
            { "key", (new object[] { new int[] { 1 }, new int[] { 2, 3, 4 } }, typeof(object[])) }
        });

        var jsonDocument = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(serialized);
        var result = serializer.DeserializeData(jsonDocument!);

        var array = Assert.IsType<object[]>(result["key"].Value);
        Assert.Equal(new int[] { 1 }, array[0]);
        Assert.Equal(new int[] { 2, 3, 4 }, array[1]);
    }

    [Fact]
    public void SerializeValue_RoundTrips_ToDeclaredType()
    {
        var serializer = CreateSerializer();

        var bytes = serializer.SerializeValue("hello", typeof(string));
        var (value, type) = serializer.DeserializeValue(bytes);

        Assert.Equal("hello", value);
        Assert.Equal(typeof(string), type);
    }

    [Fact]
    public void SerializeValue_RoundTripsEnumToItsOwnType()
    {
        var serializer = CreateSerializer();

        var bytes = serializer.SerializeValue(TestEnum.Value2, typeof(TestEnum));
        var (value, type) = serializer.DeserializeValue(bytes);

        Assert.Equal(TestEnum.Value2, value);
        Assert.Equal(typeof(TestEnum), type);
    }

    [Fact]
    public void SerializeValue_Throws_ForUnsupportedType()
    {
        var serializer = CreateSerializer();

        Assert.Throws<InvalidOperationException>(() => serializer.SerializeValue(new TestItem(), typeof(TestItem)));
    }

    [Fact]
    public void SerializeValue_RoundTripsCollection_ToSourceType()
    {
        var serializer = CreateSerializer();

        var bytes = serializer.SerializeValue(new List<int> { 1, 2, 3 }, typeof(List<int>));
        var (value, type) = serializer.DeserializeValue(bytes);

        var list = Assert.IsType<List<int>>(value);
        Assert.Equal(new List<int> { 1, 2, 3 }, list);
        Assert.Equal(typeof(List<int>), type);
    }

    private class TestItem
    {
        public int DummyInt { get; set; }
    }

    private enum TestEnum
    {
        Value1,
        Value2
    }

    private enum LongEnum : long
    {
        Big = 5_000_000_000
    }

    private sealed class DualCollection : ICollection<int>, ICollection<string>
    {
        public int Count => 0;
        public bool IsReadOnly => true;
        public void Add(int item) => throw new NotSupportedException();
        public void Add(string item) => throw new NotSupportedException();
        public void Clear() => throw new NotSupportedException();
        public bool Contains(int item) => throw new NotSupportedException();
        public bool Contains(string item) => throw new NotSupportedException();
        public void CopyTo(int[] array, int arrayIndex) => throw new NotSupportedException();
        public void CopyTo(string[] array, int arrayIndex) => throw new NotSupportedException();
        public bool Remove(int item) => throw new NotSupportedException();
        public bool Remove(string item) => throw new NotSupportedException();
        IEnumerator<int> IEnumerable<int>.GetEnumerator() => throw new NotSupportedException();
        IEnumerator<string> IEnumerable<string>.GetEnumerator() => throw new NotSupportedException();
        IEnumerator IEnumerable.GetEnumerator() => throw new NotSupportedException();
    }
}
