using Xunit;
using Yukti.Cli;
using Yukti.Cli.Import;

namespace Yukti.Cli.Tests;

/// <summary>
/// API Testing Module Spec, API-T10: OpenApiImporter/PostmanImporter are pure transformations —
/// called directly here, not through a spawned CLI process (CliIntegrationTests' own convention,
/// which proves the CLI wiring rather than the transformation logic itself).
/// </summary>
public sealed class ApiImportTests
{
    [Fact]
    public void OpenApi_generates_one_step_per_operation_with_path_and_query_params_and_a_schema_assertion()
    {
        const string spec = """
        {
          "openapi": "3.0.0",
          "info": { "title": "Orders API" },
          "servers": [{ "url": "https://api.example.com" }],
          "paths": {
            "/orders/{orderId}": {
              "get": {
                "operationId": "getOrder",
                "parameters": [
                  { "name": "orderId", "in": "path", "required": true, "schema": { "type": "string" } },
                  { "name": "verbose", "in": "query", "schema": { "type": "boolean" } }
                ],
                "responses": {
                  "200": {
                    "content": { "application/json": { "schema": { "type": "object", "required": ["id"], "properties": { "id": { "type": "integer" } } } } }
                  }
                }
              }
            }
          }
        }
        """;

        var result = OpenApiImporter.Import(spec);

        Assert.NotNull(result.Flow);
        var step = Assert.Single(result.Flow!.Steps);
        Assert.Equal("getOrder", step.Name);
        Assert.Equal("api", step.Module);
        Assert.Equal("request", step.Action);
        Assert.Equal("https://api.example.com/orders/{orderId}", step.Params["url"]);
        Assert.Equal("GET", step.Params["method"]);

        var pathParams = Assert.IsType<Dictionary<string, object?>>(step.Params["pathParams"]);
        Assert.Equal("{{vars.orderId}}", pathParams["orderId"]);

        var queryParams = Assert.IsType<Dictionary<string, object?>>(step.Params["queryParams"]);
        Assert.Equal("{{vars.verbose}}", queryParams["verbose"]);

        var assertions = Assert.IsType<List<object?>>(step.Params["assert"]);
        var schemaAssertion = Assert.IsType<Dictionary<string, object?>>(Assert.Single(assertions));
        Assert.Equal("schema", schemaAssertion["type"]);
    }

    [Fact]
    public void OpenApi_requestBody_is_never_fabricated_and_is_reported_as_a_warning()
    {
        const string spec = """
        {
          "openapi": "3.0.0",
          "paths": {
            "/orders": {
              "post": {
                "operationId": "createOrder",
                "requestBody": { "content": { "application/json": { "schema": { "type": "object" } } } },
                "responses": { "201": {} }
              }
            }
          }
        }
        """;

        var result = OpenApiImporter.Import(spec);

        Assert.NotNull(result.Flow);
        var step = Assert.Single(result.Flow!.Steps);
        Assert.False(step.Params.ContainsKey("body"));
        Assert.Contains(result.Warnings, w => w.Contains("createOrder") && w.Contains("requestBody"));
    }

    [Fact]
    public void OpenApi_operation_filter_imports_only_the_requested_operationIds()
    {
        const string spec = """
        {
          "openapi": "3.0.0",
          "paths": {
            "/a": { "get": { "operationId": "getA", "responses": { "200": {} } } },
            "/b": { "get": { "operationId": "getB", "responses": { "200": {} } } }
          }
        }
        """;

        var result = OpenApiImporter.Import(spec, new[] { "getB" });

        Assert.NotNull(result.Flow);
        var step = Assert.Single(result.Flow!.Steps);
        Assert.Equal("getB", step.Name);
    }

    [Fact]
    public void OpenApi_rejects_a_non_3x_document_clearly()
    {
        var result = OpenApiImporter.Import("""{ "swagger": "2.0", "paths": {} }""");

        Assert.Null(result.Flow);
        Assert.Single(result.Warnings);
    }

    [Fact]
    public void Postman_converts_a_request_with_path_param_and_variable_to_vars_syntax()
    {
        const string collection = """
        {
          "info": { "name": "Orders", "schema": "https://schema.getpostman.com/json/collection/v2.1.0/collection.json" },
          "item": [
            {
              "name": "Get order",
              "request": {
                "method": "GET",
                "header": [{ "key": "X-Api-Key", "value": "{{apiKey}}" }],
                "url": { "raw": "{{baseUrl}}/orders/:id", "path": ["orders", ":id"] }
              }
            }
          ]
        }
        """;

        var result = PostmanImporter.Import(collection);

        Assert.NotNull(result.Flow);
        var step = Assert.Single(result.Flow!.Steps);
        Assert.Equal("Get order", step.Name);
        Assert.Equal("{{vars.baseUrl}}/orders/{id}", step.Params["url"]);

        var pathParams = Assert.IsType<Dictionary<string, object?>>(step.Params["pathParams"]);
        Assert.Equal("{{vars.id}}", pathParams["id"]);

        var headers = Assert.IsType<Dictionary<string, object?>>(step.Params["headers"]);
        Assert.Equal("{{vars.apiKey}}", headers["X-Api-Key"]);

        Assert.Contains("baseUrl", result.Flow.DeclaredVariables);
        Assert.Contains("apiKey", result.Flow.DeclaredVariables);
    }

    [Fact]
    public void Postman_flattens_nested_folders_with_a_prefixed_step_name()
    {
        const string collection = """
        {
          "info": { "name": "Orders", "schema": "https://schema.getpostman.com/json/collection/v2.1.0/collection.json" },
          "item": [
            {
              "name": "Orders folder",
              "item": [
                { "name": "List orders", "request": { "method": "GET", "url": "https://api.example.com/orders" } }
              ]
            }
          ]
        }
        """;

        var result = PostmanImporter.Import(collection);

        Assert.NotNull(result.Flow);
        var step = Assert.Single(result.Flow!.Steps);
        Assert.Equal("Orders folder / List orders", step.Name);
    }

    [Fact]
    public void Postman_raw_json_body_is_converted_literally()
    {
        const string collection = """
        {
          "info": { "name": "Orders", "schema": "https://schema.getpostman.com/json/collection/v2.1.0/collection.json" },
          "item": [
            {
              "name": "Create order",
              "request": {
                "method": "POST",
                "url": "https://api.example.com/orders",
                "body": { "mode": "raw", "raw": "{\"sku\":\"ABC\",\"qty\":2}", "options": { "raw": { "language": "json" } } }
              }
            }
          ]
        }
        """;

        var result = PostmanImporter.Import(collection);

        Assert.NotNull(result.Flow);
        var step = Assert.Single(result.Flow!.Steps);
        var body = Assert.IsType<Dictionary<string, object?>>(step.Params["body"]);
        Assert.Equal("ABC", body["sku"]);
    }

    [Fact]
    public void Postman_bearer_auth_becomes_an_authorization_header()
    {
        const string collection = """
        {
          "info": { "name": "Orders", "schema": "https://schema.getpostman.com/json/collection/v2.1.0/collection.json" },
          "item": [
            {
              "name": "Get order",
              "request": {
                "method": "GET",
                "url": "https://api.example.com/orders",
                "auth": { "type": "bearer", "bearer": [{ "key": "token", "value": "{{authToken}}" }] }
              }
            }
          ]
        }
        """;

        var result = PostmanImporter.Import(collection);

        Assert.NotNull(result.Flow);
        var step = Assert.Single(result.Flow!.Steps);
        var headers = Assert.IsType<Dictionary<string, object?>>(step.Params["headers"]);
        Assert.Equal("Bearer {{vars.authToken}}", headers["Authorization"]);
    }

    [Fact]
    public void Postman_unconvertible_body_mode_and_dynamic_variable_are_reported_not_dropped()
    {
        const string collection = """
        {
          "info": { "name": "Orders", "schema": "https://schema.getpostman.com/json/collection/v2.1.0/collection.json" },
          "item": [
            {
              "name": "Upload",
              "request": {
                "method": "POST",
                "url": "https://api.example.com/upload?token={{$guid}}",
                "body": { "mode": "formdata", "formdata": [] }
              }
            }
          ]
        }
        """;

        var result = PostmanImporter.Import(collection);

        Assert.NotNull(result.Flow);
        Assert.Contains(result.Warnings, w => w.Contains("formdata"));
        Assert.Contains(result.Warnings, w => w.Contains("$guid"));
    }

    [Fact]
    public void Postman_rejects_a_non_v2_1_collection_clearly()
    {
        var result = PostmanImporter.Import("""{ "info": { "name": "Old", "schema": "v2.0.0" }, "item": [] }""");

        Assert.Null(result.Flow);
        Assert.Single(result.Warnings);
    }

    [Fact]
    public void Generated_flow_file_round_trips_through_FlowFileJson_deserialization()
    {
        // Proves the importer's output is byte-for-byte the same contract FlowLoader already
        // consumes -- no parallel flow-file format, per ImportModels' own doc comment.
        const string spec = """
        {
          "openapi": "3.0.0",
          "paths": { "/health": { "get": { "operationId": "health", "responses": { "200": {} } } } }
        }
        """;
        var result = OpenApiImporter.Import(spec);
        var json = System.Text.Json.JsonSerializer.Serialize(result.Flow, FlowFileJson.Options);

        var roundTripped = System.Text.Json.JsonSerializer.Deserialize<FlowFileDto>(json, FlowFileJson.Options);
        Assert.NotNull(roundTripped);
        Assert.Equal("health", Assert.Single(roundTripped!.Steps).Name);
    }
}
