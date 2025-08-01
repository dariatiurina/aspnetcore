// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Net.Http;
using System.Text.Json.Serialization.Metadata;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.InternalTesting;
using Microsoft.AspNetCore.OpenApi;

public class CreateSchemaReferenceIdTests : OpenApiDocumentServiceTestBase
{
    [Fact]
    public async Task HandlesPolymorphicTypeWithCustomReferenceIds()
    {
        // Arrange
        var builder = CreateBuilder();

        // Act
        builder.MapPost("/api", (Shape shape) => { });
        string createReferenceId(JsonTypeInfo jsonTypeInfo)
        {
            return jsonTypeInfo.Type.Name switch
            {
                "Shape" => "MyShape",
                "Triangle" => "MyTriangle",
                "Square" => "MySquare",
                _ => jsonTypeInfo.Type.Name,
            };
        }
        var options = new OpenApiOptions { CreateSchemaReferenceId = createReferenceId };

        // Assert
        await VerifyOpenApiDocument(builder, options, document =>
        {
            var operation = document.Paths["/api"].Operations[HttpMethod.Post];
            Assert.NotNull(operation.RequestBody);
            var requestBody = operation.RequestBody.Content;
            Assert.True(requestBody.TryGetValue("application/json", out var mediaType));
            var schema = mediaType.Schema;
            // Assert discriminator mappings have been configured correctly
            Assert.Equal("$type", schema.Discriminator.PropertyName);
            Assert.Contains(schema.Discriminator.PropertyName, schema.Required);
            Assert.Collection(schema.Discriminator.Mapping,
                item => Assert.Equal("triangle", item.Key),
                item => Assert.Equal("square", item.Key)
            );
            Assert.Collection(schema.Discriminator.Mapping,
                item => Assert.Equal("#/components/schemas/MyShapeMyTriangle", item.Value.Reference.ReferenceV3),
                item => Assert.Equal("#/components/schemas/MyShapeMySquare", item.Value.Reference.ReferenceV3)
            );
            // Assert the schemas with the discriminator have been inserted into the components
            Assert.True(document.Components.Schemas.TryGetValue("MyShapeMyTriangle", out var triangleSchema));
            Assert.Contains(schema.Discriminator.PropertyName, triangleSchema.Properties.Keys);
            Assert.Equal("triangle", triangleSchema.Properties[schema.Discriminator.PropertyName].Enum.First().GetValue<string>());
            Assert.True(document.Components.Schemas.TryGetValue("MyShapeMySquare", out var squareSchema));
            Assert.Equal("square", squareSchema.Properties[schema.Discriminator.PropertyName].Enum.First().GetValue<string>());
        });
    }

    [Fact]
    public async Task GeneratesSchemaForPoco_WithSchemaReferenceIdCustomization()
    {
        // Arrange
        var builder = CreateBuilder();

        // Act
        builder.MapPost("/", (Todo todo) => { });
        var options = new OpenApiOptions { CreateSchemaReferenceId = (type) => type.Type.Name == "Todo" ? $"{type.Type.Name}Schema" : OpenApiOptions.CreateDefaultSchemaReferenceId(type) };

        // Assert
        await VerifyOpenApiDocument(builder, options, document =>
        {
            var operation = document.Paths["/"].Operations[HttpMethod.Post];
            var requestBody = operation.RequestBody;

            Assert.NotNull(requestBody);
            var content = Assert.Single(requestBody.Content);
            Assert.Equal("application/json", content.Key);
            Assert.NotNull(content.Value.Schema);
            Assert.Equal("TodoSchema", ((OpenApiSchemaReference)content.Value.Schema).Reference.Id);
            var schema = content.Value.Schema;
            Assert.Equal(JsonSchemaType.Object, schema.Type);
            Assert.Collection(schema.Properties,
                property =>
                {
                    Assert.Equal("id", property.Key);
                    Assert.Equal(JsonSchemaType.Integer, property.Value.Type);
                },
                property =>
                {
                    Assert.Equal("title", property.Key);
                    Assert.Equal(JsonSchemaType.String | JsonSchemaType.Null, property.Value.Type);
                },
                property =>
                {
                    Assert.Equal("completed", property.Key);
                    Assert.Equal(JsonSchemaType.Boolean, property.Value.Type);
                },
                property =>
                {
                    Assert.Equal("createdAt", property.Key);
                    Assert.Equal(JsonSchemaType.String, property.Value.Type);
                    Assert.Equal("date-time", property.Value.Format);
                });

        });
    }

    [Fact]
    public async Task GeneratesInlineSchemaForPoco_WithCustomNullId()
    {
        // Arrange
        var builder = CreateBuilder();

        // Act
        builder.MapPost("/", (Todo todo) => { });
        var options = new OpenApiOptions { CreateSchemaReferenceId = (type) => type.Type.Name == "Todo" ? null : OpenApiOptions.CreateDefaultSchemaReferenceId(type) };

        // Assert
        await VerifyOpenApiDocument(builder, options, document =>
        {
            var operation = document.Paths["/"].Operations[HttpMethod.Post];
            var requestBody = operation.RequestBody;

            Assert.NotNull(requestBody);
            var content = Assert.Single(requestBody.Content);
            Assert.Equal("application/json", content.Key);
            Assert.NotNull(content.Value.Schema);
            // Assert that no reference was created and the schema is inlined
            var schema = content.Value.Schema;
            Assert.IsType<OpenApiSchema>(schema);
            Assert.Equal(JsonSchemaType.Object, schema.Type);
            Assert.Collection(schema.Properties,
                property =>
                {
                    Assert.Equal("id", property.Key);
                    Assert.Equal(JsonSchemaType.Integer, property.Value.Type);
                },
                property =>
                {
                    Assert.Equal("title", property.Key);
                    Assert.Equal(JsonSchemaType.String | JsonSchemaType.Null, property.Value.Type);
                },
                property =>
                {
                    Assert.Equal("completed", property.Key);
                    Assert.Equal(JsonSchemaType.Boolean, property.Value.Type);
                },
                property =>
                {
                    Assert.Equal("createdAt", property.Key);
                    Assert.Equal(JsonSchemaType.String, property.Value.Type);
                    Assert.Equal("date-time", property.Value.Format);
                });

        });
    }

    [Fact]
    public async Task CanCallDefaultImplementationFromCustomOne()
    {
        var builder = CreateBuilder();

        builder.MapPost("/", (Todo todo) => new TodoWithDueDate(todo.Id, todo.Title, todo.Completed, todo.CreatedAt, DateTime.UtcNow));
        var options = new OpenApiOptions
        {
            CreateSchemaReferenceId = (type) =>
            {
                if (type.Type.Name == "Todo")
                {
                    return null;
                }
                return OpenApiOptions.CreateDefaultSchemaReferenceId(type);
            }
        };

        await VerifyOpenApiDocument(builder, options, document =>
        {
            var operation = document.Paths["/"].Operations[HttpMethod.Post];
            var requestBody = operation.RequestBody;
            var response = operation.Responses["200"];

            // Assert that no reference was created for the Todo type
            Assert.NotNull(requestBody);
            var content = Assert.Single(requestBody.Content);
            Assert.Equal("application/json", content.Key);
            Assert.NotNull(content.Value.Schema);
            var schema = content.Value.Schema;
            Assert.IsNotType<OpenApiSchemaReference>(schema);

            // Assert that a reference was created for the TodoWithDueDate type
            Assert.NotNull(response);
            var responseContent = Assert.Single(response.Content);
            Assert.Equal("application/json", responseContent.Key);
            Assert.NotNull(responseContent.Value.Schema);
            var responseSchema = responseContent.Value.Schema;
            Assert.IsType<OpenApiSchemaReference>(responseSchema);
            Assert.Equal("TodoWithDueDate", ((OpenApiSchemaReference)responseSchema).Reference.Id);
        });
    }

    [ConditionalFact(Skip = "https://github.com/dotnet/aspnetcore/issues/58619")]
    public async Task HandlesDuplicateSchemaReferenceIdsGeneratedByOverload()
    {
        var builder = CreateBuilder();

        builder.MapPost("/", (Todo todo) => new TodoWithDueDate(todo.Id, todo.Title, todo.Completed, todo.CreatedAt, DateTime.UtcNow));
        var options = new OpenApiOptions
        {
            CreateSchemaReferenceId = (type) =>
            {
                if (type.Type.Name == "TodoWithDueDate" || type.Type.Name == "Todo")
                {
                    return "Todo";
                }
                return OpenApiOptions.CreateDefaultSchemaReferenceId(type);
            }
        };

        await VerifyOpenApiDocument(builder, options, document =>
        {
            var operation = document.Paths["/"].Operations[HttpMethod.Post];
            var requestBody = operation.RequestBody;
            var response = operation.Responses["200"];

            // Assert that a reference was created for the Todo type
            Assert.NotNull(requestBody);
            var content = Assert.Single(requestBody.Content);
            Assert.Equal("application/json", content.Key);
            Assert.NotNull(content.Value.Schema);
            var schema = content.Value.Schema;
            Assert.IsType<OpenApiSchemaReference>(schema);

            // Assert that a reference was created for the TodoWithDueDate type
            Assert.NotNull(response);
            var responseContent = Assert.Single(response.Content);
            Assert.Equal("application/json", responseContent.Key);
            Assert.NotNull(responseContent.Value.Schema);
            var responseSchema = responseContent.Value.Schema;
            Assert.IsType<OpenApiSchemaReference>(responseSchema);

            // Assert that the reference IDs are not the same (have been deduped)
            Assert.NotEqual(((OpenApiSchemaReference)schema).Reference.Id, ((OpenApiSchemaReference)responseSchema).Reference.Id);

            // Assert that the referenced schemas are correct
            var effectiveResponseSchema = responseSchema;
            Assert.Equal(JsonSchemaType.Object, effectiveResponseSchema.Type);
            Assert.Collection(effectiveResponseSchema.Properties,
                property =>
                {
                    Assert.Equal("dueDate", property.Key);
                    Assert.Equal(JsonSchemaType.String | JsonSchemaType.Null, property.Value.Type);
                    Assert.Equal("date-time", property.Value.Format);
                },
                property =>
                {
                    Assert.Equal("id", property.Key);
                    Assert.Equal(JsonSchemaType.Integer, property.Value.Type);
                },
                property =>
                {
                    Assert.Equal("title", property.Key);
                    Assert.Equal(JsonSchemaType.String | JsonSchemaType.Null, property.Value.Type);
                },
                property =>
                {
                    Assert.Equal("completed", property.Key);
                    Assert.Equal(JsonSchemaType.Boolean, property.Value.Type);
                },
                property =>
                {
                    Assert.Equal("createdAt", property.Key);
                    Assert.Equal(JsonSchemaType.String | JsonSchemaType.Null, property.Value.Type);
                    Assert.Equal("date-time", property.Value.Format);
                });

            var effectiveRequestSchema = schema;
            Assert.Equal(JsonSchemaType.Object, effectiveRequestSchema.Type);
            Assert.Collection(effectiveRequestSchema.Properties,
                property =>
                {
                    Assert.Equal("id", property.Key);
                    Assert.Equal(JsonSchemaType.Integer, property.Value.Type);
                },
                property =>
                {
                    Assert.Equal("title", property.Key);
                    Assert.Equal(JsonSchemaType.String | JsonSchemaType.Null, property.Value.Type);
                },
                property =>
                {
                    Assert.Equal("completed", property.Key);
                    Assert.Equal(JsonSchemaType.Boolean, property.Value.Type);
                },
                property =>
                {
                    Assert.Equal("createdAt", property.Key);
                    Assert.Equal(JsonSchemaType.String | JsonSchemaType.Null, property.Value.Type);
                    Assert.Equal("date-time", property.Value.Format);
                });
        });
    }

}
