// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Text.Json;

namespace Microsoft.AspNetCore.Components.Endpoints;

internal interface IStoredDataSerializer
{
    IDictionary<string, TempDataValue> DeserializeData(IDictionary<string, JsonElement> data);

    byte[] SerializeData(IDictionary<string, TempDataValue> data);

    bool CanSerialize(Type type);

    byte[] SerializeValue(object value, Type type);

    TempDataValue DeserializeValue(ReadOnlySpan<byte> utf8Json);
}
