using Microsoft.OpenApi.Models;
using MainHub.Api.Config;
using MainHub.Api.Repositories;
using MainHub.Api.Services;
using MainHub.Api.Endpoints;
using MainHub.Api.Validators;
using MainHub.Api.Authorization;
using FluentValidation;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using Arex388.NhtsaVpic.Extensions.Microsoft.DependencyInjection;
using MainHub.Api.DTOs;
using AspNetCore.Swagger.Themes;
using Microsoft.AspNetCore.Authorization;
using Npgsql;

var builder = WebApplication.CreateBuilder(args);

// 🟦 Load JWT settings from configuration file
builder.Services.Configure<JwtSettings>(
    builder.Configuration.GetSection("JwtSettings")
);
builder.Services.Configure<AdminJwtSettings>(
    builder.Configuration.GetSection("AdminJwtSettings")
);
builder.Services.Configure<AdminSettings>(
    builder.Configuration.GetSection("AdminSettings")
);

// 🟦 Load Telegram authentication settings from configuration file
builder.Services.Configure<TelegramSettings>(
    builder.Configuration.GetSection("TelegramSettings")
);

// 🟦 Register PostgreSQL data source as a singleton (built-in connection pooling)
// This replaces the old IMongoClient singleton registration and the
// BsonSerializer.RegisterSerializer(...) call that used to configure BSON
// mapping - Npgsql needs no such global serializer setup. Connection info now
// comes from the standard ASP.NET "ConnectionStrings:Main" config key instead
// of a custom MongoDbSettings section (host/port/db were previously separate
// settings; here they're all part of one Postgres connection string).
// NpgsqlDataSourceBuilder(...).Build() creates the pooled data source itself;
// repositories call _dataSource.CreateCommand(...) per operation and Npgsql
// borrows/returns a physical connection from the pool automatically - there's
// no manual "open a connection, remember to close it" step to manage.
var connectionString = builder.Configuration.GetConnectionString("Main");
if (string.IsNullOrEmpty(connectionString))
{
    throw new InvalidOperationException("ConnectionStrings:Main is not configured properly.");
}
builder.Services.AddSingleton(new NpgsqlDataSourceBuilder(connectionString).Build());

// 🟦 Register application services 
builder.Services.AddScoped<IUserRepository, UserRepository>();
builder.Services.AddScoped<IVehicleRepository, VehicleRepository>();
builder.Services.AddScoped<IServiceHistoryRepository, ServiceHistoryRepository>();
builder.Services.AddScoped<IRefreshTokenRepository, RefreshTokenRepository>();

// 🟦 Register application repositories
builder.Services.AddScoped<IUserService, UserService>();
builder.Services.AddScoped<ITokenService, TokenService>();
builder.Services.AddScoped<IVehicleService, VehicleService>();
builder.Services.AddScoped<IServiceHistoryService, ServiceHistoryService>();
builder.Services.AddScoped<IRefreshTokenService, RefreshTokenService>();

builder.Services.AddSingleton<IAuthorizationHandler, AllowedTelegramAdminAuthorizationHandler>();

builder.Services.AddNhtsaVpic();

// 🟦 Register FluentValidation validators
builder.Services.AddValidatorsFromAssemblyContaining<UpdateUserDtoValidator>();
builder.Services.AddValidatorsFromAssemblyContaining<CreateVehicleDto>();
builder.Services.AddValidatorsFromAssemblyContaining<UpdateVehicleDtoValidator>();
builder.Services.AddValidatorsFromAssemblyContaining<CreateServiceHistoryDetailsDtoValidator>();
builder.Services.AddValidatorsFromAssemblyContaining<CreateServiceHistoryRecordDtoValidator>();

// 🟦 Add controllers and Swagger
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "MainHub API", Version = "v1" });
    // Add the "Authorization" header input field
    c.AddSecurityDefinition("Bearer", new Microsoft.OpenApi.Models.OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = Microsoft.OpenApi.Models.SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = Microsoft.OpenApi.Models.ParameterLocation.Header,
        Description = "Enter JWT token like: Bearer {your token}"
    });

    // Apply it globally 
    c.AddSecurityRequirement(new Microsoft.OpenApi.Models.OpenApiSecurityRequirement
    {
        {
            new Microsoft.OpenApi.Models.OpenApiSecurityScheme
            {
                Reference = new Microsoft.OpenApi.Models.OpenApiReference
                {
                    Type = Microsoft.OpenApi.Models.ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            Array.Empty<string>()
        }
    });
});

builder.Services.AddCors(options =>
{
    options.AddPolicy("AdminWebUi", policy =>
    {
        policy
            .WithOrigins("http://localhost:5100", "http://localhost:5173")
            .AllowAnyHeader()
            .AllowAnyMethod();
    });
});

// 🟦 Configure JWT Settings
var jwtSettings = builder.Configuration.GetSection("JwtSettings").Get<JwtSettings>();
if (jwtSettings == null || string.IsNullOrEmpty(jwtSettings.SecretKey))
{
    throw new InvalidOperationException("JwtSettings or SecretKey is not configured properly.");
}

var adminJwtSettings = builder.Configuration.GetSection("AdminJwtSettings").Get<AdminJwtSettings>();
if (adminJwtSettings == null || string.IsNullOrEmpty(adminJwtSettings.SecretKey))
{
    throw new InvalidOperationException("AdminJwtSettings or SecretKey is not configured properly.");
}

var jwtSecretKey = Encoding.UTF8.GetBytes(jwtSettings.SecretKey);
var adminJwtSecretKey = Encoding.UTF8.GetBytes(adminJwtSettings.SecretKey);

// 🟦 Authentication Configuration
// Internal JWT authentication scheme (tokens generated after Telegram OAuth)
builder.Services
    .AddAuthentication(options =>
    {
        // Default to Internal JWT (tokens generated by Telegram OAuth flow)
        options.DefaultAuthenticateScheme = "InternalJwt";
        options.DefaultChallengeScheme = "InternalJwt";
    })
    // Internal JWT Authentication (for custom tokens)
    .AddJwtBearer("InternalJwt", options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwtSettings.Issuer,
            ValidAudience = jwtSettings.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(jwtSecretKey),
            ClockSkew = TimeSpan.Zero // Remove delay of expiration validation
        };
    })
    .AddJwtBearer("AdminJwt", options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = adminJwtSettings.Issuer,
            ValidAudience = adminJwtSettings.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(adminJwtSecretKey),
            ClockSkew = TimeSpan.Zero
        };
    });

// Authorization policies
builder.Services.AddAuthorization(options =>
{
    // Policy that only accepts Internal JWT tokens (for all API calls)
    options.AddPolicy("RequireInternalJwt", policy =>
    {
        policy.AuthenticationSchemes.Add("InternalJwt");
        policy.RequireAuthenticatedUser();
    });

    options.AddPolicy("RequireAdminJwt", policy =>
    {
        policy.AuthenticationSchemes.Add("AdminJwt");
        policy.RequireAuthenticatedUser();
        policy.RequireRole("admin");
        policy.Requirements.Add(new AllowedTelegramAdminRequirement());
    });
});

var app = builder.Build();

// 🟦 Enable Swagger for testing APIs
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(Theme.Dark);
}

app.UseHttpsRedirection();
app.UseCors("AdminWebUi");
app.UseAuthentication();
app.UseAuthorization();

// 🟦 Register application Endpoints
app.MapAuthEndpoints();
app.MapUserEndpoints();
app.MapVehicleEndpoints();
app.MapServiceHistoryEndpoints();
app.MapAdminUserEndpoints();
app.MapAdminVehicleEndpoints();

app.Run();
