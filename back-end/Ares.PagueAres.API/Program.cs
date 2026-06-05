using Ares.PagueAres.API.Middlewares.Authentication;
using Ares.PagueAres.Application.Configuration;
using Ares.PagueAres.Application.Extensions;
using Ares.PagueAres.Infrastructure;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using Scalar.AspNetCore;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

// Options
builder.Services.Configure<JwtSettings>(builder.Configuration.GetSection("JwtSettings"));
builder.Services.Configure<ActiveDirectorySettings>(builder.Configuration.GetSection("ActiveDirectory"));
builder.Services.Configure<SmtpSettings>(builder.Configuration.GetSection("Smtp"));

// Controllers
builder.Services.AddControllers();
builder.Services.AddMemoryCache();

// CORS: se AllowedOrigins estiver configurado restringe às origens listadas; senão permite qualquer origem.
var allowedOrigins = builder.Configuration["AllowedOrigins"];
builder.Services.AddCors(options =>
{
    options.AddPolicy("CorsPolicy", policy =>
    {
        if (string.IsNullOrWhiteSpace(allowedOrigins))
            policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod();
        else
            policy.WithOrigins(allowedOrigins.Split(',', StringSplitOptions.RemoveEmptyEntries))
                  .AllowAnyHeader().AllowAnyMethod();
    });
});

// OpenAPI (Microsoft.AspNetCore.OpenApi) + Scalar UI
builder.Services.AddOpenApi(options =>
{
    options.AddDocumentTransformer((document, context, cancellationToken) =>
    {
        document.Info.Title = "API PagueAres";
        document.Info.Version = "v1";

        document.Components ??= new OpenApiComponents();
        document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
        document.Components.SecuritySchemes["Bearer"] = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.Http,
            Scheme = "bearer",
            BearerFormat = "JWT",
            Description = "JWT Authorization header. Insira seu token JWT abaixo."
        };

        // Aplica o requisito de segurança a todas as operações
        var requirement = new OpenApiSecurityRequirement
        {
            [new OpenApiSecuritySchemeReference("Bearer")] = []
        };

        foreach (var path in document.Paths.Values)
            foreach (var operation in path.Operations.Values)
            {
                operation.Security ??= [];
                operation.Security.Add(requirement);
            }

        return Task.CompletedTask;
    });
});

// Database
builder.Services.AddDbContext<PagueAresContext>(opt =>
{
    opt.UseSqlServer(builder.Configuration.GetConnectionString("ConexaoPadrao"));
});

// Application Services
builder.Services.RegisterApplicationServices();

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = builder.Configuration["JwtSettings:Issuer"],
            ValidAudience = builder.Configuration["JwtSettings:Audience"],
            IssuerSigningKey = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(builder.Configuration["JwtSettings:Secret"] ?? string.Empty))
        };
    });

builder.Services.AddAuthorization();

var app = builder.Build();

// Auto-migrate no startup — ativado por variável de ambiente (Docker/CI).
if (app.Configuration["RunMigrations"] == "true"
    || Environment.GetEnvironmentVariable("ASPNETCORE_RUN_MIGRATIONS") == "true")
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<PagueAresContext>();
    db.Database.Migrate();
}

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference(options => options.Title = "API PagueAres");
}

// Sem TLS no container Docker — redirecionamento condicional.
if (!app.Environment.IsEnvironment("Docker"))
    app.UseHttpsRedirection();

app.UseAuthorization();

app.UseCors("CorsPolicy");

// Authentication Middleware
app.UseMiddleware<JwtMiddleware>();

app.MapControllers();

app.Run();
