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
builder.Services.Configure<InternalIdentityJwtSettings>(
    builder.Configuration.GetSection("InternalIdentityJwtSettings")
);
builder.Services.Configure<GarageJwtSettings>(
    builder.Configuration.GetSection("GarageJwtSettings")
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
// Register a single NpgsqlDataSource for the app. Repositories inject this
// and open short-lived connections from it. Singleton because the data
// source is thread-safe and owns the connection pool.
builder.Services.AddSingleton<NpgsqlDataSource>(_ =>
    new NpgsqlDataSourceBuilder(connectionString).Build());

// 🟦 Register Minio client as a singleton. The client is thread-safe and holds
// an HttpClient internally, so a single instance is reused across requests.
var minioSettings = builder.Configuration.GetSection("MinioSettings").Get<MinioSettings>();
if (minioSettings == null || string.IsNullOrEmpty(minioSettings.Endpoint))
{
    throw new InvalidOperationException("MinioSettings is not configured properly.");
}
static IMinioClient BuildMinioClient(MinioSettings settings, string endpoint, bool useSsl)
{
    var b = new MinioClient()
        .WithEndpoint(endpoint)
        .WithCredentials(settings.AccessKey, settings.SecretKey);
    if (useSsl) b = b.WithSSL();
    if (!string.IsNullOrEmpty(settings.Region)) b = b.WithRegion(settings.Region);
    return b.Build();
}

// Internal client: talks to Minio over the private network for uploads, downloads, bucket ops.
builder.Services.AddSingleton<IMinioClient>(_ =>
    BuildMinioClient(minioSettings, minioSettings.Endpoint, minioSettings.UseSsl));

// Presign client: used only to generate presigned URLs. Bound to the publicly reachable
// hostname so the signature is valid against a host clients can actually resolve.
// Falls back to the internal endpoint when PublicEndpoint isn't configured.
builder.Services.AddKeyedSingleton<IMinioClient>(MinioClientKeys.Presign, (_, _) =>
{
    var endpoint = string.IsNullOrWhiteSpace(minioSettings.PublicEndpoint)
        ? minioSettings.Endpoint
        : minioSettings.PublicEndpoint;
    var useSsl = minioSettings.PublicUseSsl ?? minioSettings.UseSsl;
    return BuildMinioClient(minioSettings, endpoint, useSsl);
});

// 🟦 Register application services 
builder.Services.AddScoped<IUserRepository, UserRepository>();
builder.Services.AddScoped<IInternalUserProfileRepository, InternalUserProfileRepository>();
builder.Services.AddScoped<IExternalUserProfileRepository, ExternalUserProfileRepository>();
builder.Services.AddScoped<IVehicleRepository, VehicleRepository>();
builder.Services.AddScoped<IServiceHistoryRepository, ServiceHistoryRepository>();
builder.Services.AddScoped<IRefreshTokenRepository, RefreshTokenRepository>();
builder.Services.AddScoped<IGarageMembershipRepository, GarageMembershipRepository>();
builder.Services.AddScoped<IRoleRepository, RoleRepository>();
builder.Services.AddScoped<IGarageRepository, GarageRepository>();

// 🟦 Register application repositories
builder.Services.AddScoped<IUserService, UserService>();
builder.Services.AddScoped<ITokenService, TokenService>();
builder.Services.AddScoped<IVehicleService, VehicleService>();
builder.Services.AddScoped<IServiceHistoryService, ServiceHistoryService>();
builder.Services.AddScoped<IRefreshTokenService, RefreshTokenService>();
builder.Services.AddScoped<IPermissionService, PermissionService>();
builder.Services.AddScoped<IRoleService, RoleService>();
builder.Services.AddScoped<IMembershipService, MembershipService>();
builder.Services.AddScoped<IGarageService, GarageService>();
builder.Services.AddSingleton<IMinioService, MinioService>();

builder.Services.AddSingleton<IAuthorizationHandler, AllowedTelegramAdminAuthorizationHandler>();

// Enforces per-garage scope checks on garage-scoped endpoints (garage-context + scope claim).
// The policy provider mints a policy on demand for each RequireScope(...) call.
builder.Services.AddSingleton<IAuthorizationHandler, ScopeAuthorizationHandler>();
builder.Services.AddSingleton<IAuthorizationPolicyProvider, ScopePolicyProvider>();

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

var internalIdentityJwtSettings = builder.Configuration.GetSection("InternalIdentityJwtSettings").Get<InternalIdentityJwtSettings>();
if (internalIdentityJwtSettings == null || string.IsNullOrEmpty(internalIdentityJwtSettings.SecretKey))
{
    throw new InvalidOperationException("InternalIdentityJwtSettings or SecretKey is not configured properly.");
}

var garageJwtSettings = builder.Configuration.GetSection("GarageJwtSettings").Get<GarageJwtSettings>();
if (garageJwtSettings == null || string.IsNullOrEmpty(garageJwtSettings.SecretKey))
{
    throw new InvalidOperationException("GarageJwtSettings or SecretKey is not configured properly.");
}

var adminJwtSettings = builder.Configuration.GetSection("AdminJwtSettings").Get<AdminJwtSettings>();
if (adminJwtSettings == null || string.IsNullOrEmpty(adminJwtSettings.SecretKey))
{
    throw new InvalidOperationException("AdminJwtSettings or SecretKey is not configured properly.");
}

var mobileJwtSecretKey = Encoding.UTF8.GetBytes(mobileJwtSettings.SecretKey);
var internalIdentityJwtSecretKey = Encoding.UTF8.GetBytes(internalIdentityJwtSettings.SecretKey);
var garageJwtSecretKey = Encoding.UTF8.GetBytes(garageJwtSettings.SecretKey);
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
    .AddJwtBearer(nameof(AuthScheme.InternalIdentityJwt), options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = internalIdentityJwtSettings.Issuer,
            ValidAudience = internalIdentityJwtSettings.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(internalIdentityJwtSecretKey),
            ClockSkew = TimeSpan.Zero // Remove delay of expiration validation
        };
    })
    .AddJwtBearer(nameof(AuthScheme.GarageJwt), options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = garageJwtSettings.Issuer,
            ValidAudience = garageJwtSettings.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(garageJwtSecretKey),
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

    options.AddPolicy(nameof(AuthPolicy.RequireInternalIdentityJwt), policy =>
    {
        policy.AuthenticationSchemes.Add(nameof(AuthScheme.InternalIdentityJwt));
        policy.RequireAuthenticatedUser();
    });

    options.AddPolicy(nameof(AuthPolicy.RequireGarageJwt), policy =>
    {
        policy.AuthenticationSchemes.Add(nameof(AuthScheme.GarageJwt));
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
app.MapGarageEndpoints();
app.MapRoleEndpoints();
app.MapStaffEndpoints();
app.MapUserEndpoints();
app.MapVehicleEndpoints();
app.MapServiceHistoryEndpoints();
app.MapAdminUserEndpoints();
app.MapAdminVehicleEndpoints();
app.MapAdminServiceHistoryEndpoints();

app.Run();
