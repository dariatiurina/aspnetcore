// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Globalization;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;

var builder = WebAssemblyHostBuilder.CreateDefault(args);

builder.Services.AddLocalization();

CultureInfo.DefaultThreadCurrentCulture = new CultureInfo("en-us");
CultureInfo.DefaultThreadCurrentUICulture = new CultureInfo("en-us");

await builder.Build().RunAsync();
