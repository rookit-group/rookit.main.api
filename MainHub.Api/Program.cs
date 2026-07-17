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
using AspNetCore.Swagger.Themes;
using Microsoft.AspNetCore.Authorization;
using Npgsql;
using MainHub.Api.Enums;
using Minio;
using DbUp;
using System.Reflection;

var builder = WebApplication.CreateBuilder(args);

// 🟦 Load JWT settings from configuration file
builder.Services.Configure<JwtSettings>(
    builder.Configuration.GetSection("JwtSettings")
);
builder.Services.Configure<WebJwtSettings>(
    builder.Configuration.GetSection("WebJwtSettings")
);
builder.Services.Configure<AdminJwtSettings>(
    builder.Configuration.GetSection("AdminJwtSettings")
);

// 🟦 Load Admin (non-JWT) settings from configuration file
builder.Services.Configure<AdminSettings>(
    builder.Configuration.GetSection("AdminSettings")
);

// 🟦 Load Telegram authentication settings from configuration file
builder.Services.Configure<TelegramSettings>(
    builder.Configuration.GetSection("TelegramSettings")
);

// 🟦 Load Minio (S3-compatible object storage) settings from configuration file
builder.Services.Configure<MinioSettings>(
    builder.Configuration.GetSection("MinioSettings")
);

// 🟦 Register PostgreSQL
var connectionString = builder.Configuration.GetConnectionString("Main");
if (string.IsNullOrEmpty(connectionString))
{
    throw new InvalidOperationException("ConnectionStrings:Main is not configured properly.");
}
// if the mainhub database doesn't exist yet on the Postgres server, create it. Safe to run when it already exists.
EnsureDatabase.For.PostgresqlDatabase(connectionString);
// configures DbUp to look for SQL scripts embedded in the running assembly (i.e. the .dll).
var upgrader = DeployChanges.To
    .PostgresqlDatabase(connectionString)
    .WithScriptsEmbeddedInAssembly(Assembly.GetExecutingAssembly())
    .LogToConsole()
    .Build();
// connects, checks the schemaversions table (creates it if missing), runs any scripts not yet applied.
var result = upgrader.PerformUpgrade();
// If anything fails, print the error and exit the process — no point letting the app run against a broken schema.
if (!result.Successful)
{
    Console.Error.WriteLine(result.Error);
    Environment.Exit(1);
}

// 🟦 Register Minio client as a singleton. The client is thread-safe and holds
// an HttpClient internally, so a single instance is reused across requests.
var minioSettings = builder.Configuration.GetSection("MinioSettings").Get<MinioSettings>();
if (minioSettings == null || string.IsNullOrEmpty(minioSettings.Endpoint))
{
    throw new InvalidOperationException("MinioSettings is not configured properly.");
}
builder.Services.AddSingleton<IMinioClient>(_ =>
{
    var minioBuilder = new MinioClient()
        .WithEndpoint(minioSettings.Endpoint)
        .WithCredentials(minioSettings.AccessKey, minioSettings.SecretKey);
    if (minioSettings.UseSsl) minioBuilder = minioBuilder.WithSSL();
    if (!string.IsNullOrEmpty(minioSettings.Region)) minioBuilder = minioBuilder.WithRegion(minioSettings.Region);
    return minioBuilder.Build();
});

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
builder.Services.AddSingleton<IMinioService, MinioService>();

builder.Services.AddSingleton<IAuthorizationHandler, AllowedTelegramAdminAuthorizationHandler>();

// 🟦 Register NHTSA Vehicle API client
builder.Services.AddNhtsaVpic();

// 🟦 Register FluentValidation validators
builder.Services.AddValidatorsFromAssemblyContaining<UpdateUserDtoValidator>();
builder.Services.AddValidatorsFromAssemblyContaining<CreateVehicleDtoValidator>();
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
    options.AddPolicy("WebUi", policy =>
    {
        policy
            .WithOrigins("http://localhost:5100", "http://localhost:5173")
            .AllowAnyHeader()
            .AllowAnyMethod();
    });
});

// 🟦 Configure JWT Settings
var mobileJwtSettings = builder.Configuration.GetSection("JwtSettings").Get<JwtSettings>();
if (mobileJwtSettings == null || string.IsNullOrEmpty(mobileJwtSettings.SecretKey))
{
    throw new InvalidOperationException("JwtSettings or SecretKey is not configured properly.");
}

var webJwtSettings = builder.Configuration.GetSection("WebJwtSettings").Get<WebJwtSettings>();
if (webJwtSettings == null || string.IsNullOrEmpty(webJwtSettings.SecretKey))
{
    throw new InvalidOperationException("WebJwtSettings or SecretKey is not configured properly.");
}

var adminJwtSettings = builder.Configuration.GetSection("AdminJwtSettings").Get<AdminJwtSettings>();
if (adminJwtSettings == null || string.IsNullOrEmpty(adminJwtSettings.SecretKey))
{
    throw new InvalidOperationException("AdminJwtSettings or SecretKey is not configured properly.");
}

var mobileJwtSecretKey = Encoding.UTF8.GetBytes(mobileJwtSettings.SecretKey);
var webJwtSecretKey = Encoding.UTF8.GetBytes(webJwtSettings.SecretKey);
var adminJwtSecretKey = Encoding.UTF8.GetBytes(adminJwtSettings.SecretKey);

// 🟦 Authentication Configuration
// Internal JWT authentication scheme (tokens generated after Telegram OAuth)
builder.Services
    .AddAuthentication(options =>
    {
        options.DefaultAuthenticateScheme = nameof(AuthScheme.MobileJwt);
        options.DefaultChallengeScheme = nameof(AuthScheme.MobileJwt);
    })
    .AddJwtBearer(nameof(AuthScheme.MobileJwt), options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = mobileJwtSettings.Issuer,
            ValidAudience = mobileJwtSettings.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(mobileJwtSecretKey),
            ClockSkew = TimeSpan.Zero // Remove delay of expiration validation
        };
    })
    .AddJwtBearer(nameof(AuthScheme.WebJwt), options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = webJwtSettings.Issuer,
            ValidAudience = webJwtSettings.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(webJwtSecretKey),
            ClockSkew = TimeSpan.Zero // Remove delay of expiration validation
        };
    })
    .AddJwtBearer(nameof(AuthScheme.AdminJwt), options =>
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
            ClockSkew = TimeSpan.Zero // Remove delay of expiration validation
        };
    });

// Authorization policies
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy(nameof(AuthPolicy.RequireMobileJwt), policy =>
    {
        policy.AuthenticationSchemes.Add(nameof(AuthScheme.MobileJwt));
        policy.RequireAuthenticatedUser();
    });

    options.AddPolicy(nameof(AuthPolicy.RequireWebJwt), policy =>
    {
        policy.AuthenticationSchemes.Add(nameof(AuthScheme.WebJwt));
        policy.RequireAuthenticatedUser();
    });

    options.AddPolicy(nameof(AuthPolicy.RequireAdminJwt), policy =>
    {
        policy.AuthenticationSchemes.Add(nameof(AuthScheme.AdminJwt));
        policy.RequireAuthenticatedUser();
        policy.RequireRole("admin");
        policy.Requirements.Add(new AllowedTelegramAdminRequirement());
    });
});

var app = builder.Build();

// 🟦 Ensure the default Minio bucket exists on startup. This creates it lazily
// on first run so no manual bucket provisioning is required in dev environments.
using (var scope = app.Services.CreateScope())
{
    var minio = scope.ServiceProvider.GetRequiredService<IMinioService>();
    await minio.EnsureBucketAsync();
}

// 🟦 Enable Swagger for testing APIs
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(Theme.Dark);
}

app.UseHttpsRedirection();
app.UseCors("WebUi");
app.UseAuthentication();
app.UseAuthorization();

// 🟦 Register application Endpoints
app.MapAuthEndpoints();
app.MapUserEndpoints();
app.MapVehicleEndpoints();
app.MapServiceHistoryEndpoints();
app.MapAdminUserEndpoints();
app.MapAdminVehicleEndpoints();
app.MapAdminServiceHistoryEndpoints();

app.Run();
