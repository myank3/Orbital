using Microsoft.Extensions.Options;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace Orbital
{
    public class ConfigureSwaggerOptions
        : IConfigureOptions<SwaggerGenOptions>
    {
        public void Configure(SwaggerGenOptions options)
        {
            options.AddSecurityDefinition("Bearer",
                new OpenApiSecurityScheme
                {
                    Description =
                        "Enter JWT token like: Bearer {your token}",
                    Name = "Authorization",
                    In = ParameterLocation.Header,
                    Type = SecuritySchemeType.Http,
                    Scheme = "bearer",
                    BearerFormat = "JWT"
                });

            options.AddSecurityRequirement(
                new OpenApiSecurityRequirement
                {
                    {
                        new OpenApiSecurityScheme
                        {
                            Reference =
                                new OpenApiReference
                                {
                                    Type =
                                        ReferenceType.SecurityScheme,
                                    Id = "Bearer"
                                }
                        },
                        Array.Empty<string>()
                    }
                });
        }
    }
}