using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace SharedKernel;

public static class OpenApiEndpointExtensions
{
    public static IEndpointRouteBuilder MapSwaggerUi(this IEndpointRouteBuilder endpoints, string serviceName)
    {
        var encodedServiceName = JavaScriptEncoder.Default.Encode(serviceName);

        endpoints.MapGet("/swagger", () => Results.Content(
            $$"""
            <!doctype html>
            <html lang="en">
            <head>
              <meta charset="utf-8" />
              <meta name="viewport" content="width=device-width, initial-scale=1" />
              <title>{{encodedServiceName}} - Swagger UI</title>
              <link rel="stylesheet" href="https://cdn.jsdelivr.net/npm/swagger-ui-dist@5/swagger-ui.css" />
              <style>
                body { margin: 0; background: #faf7f2; }
                .topbar { display: none; }
              </style>
            </head>
            <body>
              <div id="swagger-ui"></div>
              <script src="https://cdn.jsdelivr.net/npm/swagger-ui-dist@5/swagger-ui-bundle.js"></script>
              <script>
                window.ui = SwaggerUIBundle({
                  url: '/openapi/v1.json',
                  dom_id: '#swagger-ui',
                  docExpansion: 'list',
                  defaultModelsExpandDepth: 1,
                  displayRequestDuration: true,
                  persistAuthorization: true
                });
              </script>
            </body>
            </html>
            """,
            "text/html"))
            .ExcludeFromDescription();

        endpoints.MapGet("/swagger/v1/swagger.json", () => Results.Redirect("/openapi/v1.json", permanent: false))
            .ExcludeFromDescription();

        return endpoints;
    }
}
