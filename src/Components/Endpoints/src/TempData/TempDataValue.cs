// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Microsoft.AspNetCore.Components;

internal readonly record struct TempDataValue(object? Value, Type? Type)
{
    public static implicit operator TempDataValue((object? Value, Type? Type) value) => new(value.Value, value.Type);
}
