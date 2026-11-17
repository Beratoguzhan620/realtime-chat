using System.Text.Encodings.Web;
using System.Text.Json.Serialization;
using System.Text.Unicode;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using RealtimeChat.Api.Common;
using RealtimeChat.Api.Features.Auth;
using RealtimeChat.Api.Features.Conversations;
using RealtimeChat.Api.Features.Demo;
using RealtimeChat.Api.Features.Hub;
using RealtimeChat.Api.Features.Messages;
using RealtimeChat.Api.Features.Users;
using RealtimeChat.Api.Logging;
using RealtimeChat.Infrastructure;
using RealtimeChat.Infrastructure.Persistence;
using RealtimeChat.Infrastructure.Presence;
using Scalar.AspNetCore;
using Serilog;
using Serilog.Core;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton<ILogEventEnricher, SensitiveDataEnricher>();
builder.Services.AddSerilog((services, logger) => logger
    .ReadFrom.Configuration(builder.Configuration)
    .ReadFrom.Services(services)
    .Enrich.FromLogContext());

var connectionString = ConnectionStrings.FromUrl(builder.Configuration.GetConnectionString("Chat"));
if (string.IsNullOrWhiteSpace(connectionString))
{
    throw new InvalidOperationException(
        "ConnectionStrings:Chat is not configured. Use 'dotnet user-secrets' or the ConnectionStrings__Chat environment variable.");
}

var redisConnectionString = builder.Configuration.GetConnectionString("Redis");
if (string.IsNullOrWhiteSpace(redisConnectionString))
{
    throw new InvalidOperationException(
        "ConnectionStrings:Redis is not configured. Use 'dotnet user-secrets' or the ConnectionStrings__Redis environment variable.");
}

// One connection for presence and the SignalR backplane.
var redis = await ConnectionMultiplexer.ConnectAsync(redisConnectionString);

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddChatPersistence(connectionString);
builder.Services.AddChatPresence(builder.Configuration, redis);
builder.Services.AddChatAuth(builder.Configuration);

builder.Services.Configure<ChatLimits>(builder.Configuration.GetSection(ChatLimits.Section));
builder.Services.AddSingleton<SendRateLimiter>();
builder.Services.AddSingleton<ChatEvents>();
builder.Services.AddScoped<MessageService>();
builder.Services.AddSingleton<PresenceNotifier>();
builder.Services.AddSingleton<IPresenceListener>(provider => provider.GetRequiredService<PresenceNotifier>());
builder.Services.AddSingleton<IUserIdProvider, SubjectUserIdProvider>();
builder.Services.AddSignalR(options =>
    {
        options.AddFilter<HubErrorFilter>();
        // A message is at most 4000 characters; nothing a client sends needs more than this.
        options.MaximumReceiveMessageSize = 32 * 1024;
    })
    .AddJsonProtocol(options => options.PayloadSerializerOptions.Converters.Add(new JsonStringEnumConverter()))
    // Every instance publishes hub messages through Redis, so a message reaches a member connected anywhere.
    .AddStackExchangeRedis(options =>
    {
        options.Configuration.ChannelPrefix = RedisChannel.Literal("realtime-chat");
        options.ConnectionFactory = _ => Task.FromResult<IConnectionMultiplexer>(redis);
    });

// The public demo's people; off unless Demo:Enabled.
builder.Services.Configure<DemoOptions>(builder.Configuration.GetSection(DemoOptions.Section));
var demo = builder.Configuration.GetSection(DemoOptions.Section).Get<DemoOptions>() ?? new DemoOptions();
if (demo.Enabled)
{
    builder.Services.AddHttpClient(DemoWorker.HttpClientName, client => client.BaseAddress = new Uri(demo.BaseUrl));
    builder.Services.AddHostedService<DemoWorker>();
}

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Encoder = JavaScriptEncoder.Create(UnicodeRanges.All);
    options.SerializerOptions.NumberHandling = JsonNumberHandling.Strict;
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
});

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    // Sign-in and registration per address: plenty for a person, slow for guessing.
    var perMinute = builder.Configuration.GetValue("Auth:AttemptsPerMinute", 10);
    // The demo's own people sign in from inside the container and are not held to it.
    options.AddPolicy(AuthEndpoints.LoginRateLimit, context => demo.Enabled && context.Connection.RemoteIpAddress is { } address && System.Net.IPAddress.IsLoopback(address)
        ? RateLimitPartition.GetNoLimiter("demo")
        : RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions { PermitLimit = perMinute, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
    options.OnRejected = (context, _) =>
    {
        context.HttpContext.Response.Headers.RetryAfter = "60";
        return ValueTask.CompletedTask;
    };
});

builder.Services.AddProblemDetails(options => options.CustomizeProblemDetails = context =>
    context.ProblemDetails.Extensions["traceId"] = context.HttpContext.TraceIdentifier);
builder.Services.AddExceptionHandler<DomainExceptionHandler>();

builder.Services.AddOpenApi(options => options
    .AddDocumentTransformer<DocumentInfoTransformer>()
    .AddDocumentTransformer<SecuritySchemesTransformer>()
    .AddOperationTransformer<SecuritySchemesTransformer>());
builder.Services.AddHealthChecks()
    .AddDbContextCheck<ChatDbContext>("database", tags: ["ready"])
    .AddCheck<RedisHealthCheck>("redis", tags: ["ready"]);

var app = builder.Build();

// One container for the public demo: the built web app is copied into wwwroot and served from here, with the same
// headers nginx sets in Compose. Without a wwwroot (tests, Compose), nothing changes.
var indexPage = Path.Combine(app.Environment.ContentRootPath, "wwwroot", "index.html");
var serveWeb = File.Exists(indexPage);
app.UseMiddleware<CorrelationIdMiddleware>();
if (serveWeb)
{
    app.UseMiddleware<WebHeadersMiddleware>();
    app.UseDefaultFiles();
    app.UseStaticFiles(new StaticFileOptions
    {
        OnPrepareResponse = context => context.Context.Response.Headers.CacheControl =
            context.Context.Request.Path.StartsWithSegments("/assets") ? "public, max-age=31536000, immutable" : "no-cache",
    });
}

app.UseSerilogRequestLogging();
app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

app.MapOpenApi();
app.MapScalarApiReference(options => options
    .WithTitle("Realtime Chat")
    .WithTheme(ScalarTheme.Default)
    .ForceLightMode()
    .HideDarkModeToggle()
    .DisableDefaultFonts()
    .WithCustomCss(ScalarStyle.Css)
    .AddPreferredSecuritySchemes(SecuritySchemesTransformer.Scheme)
    .HideDeveloperTools()
    .HideClientButton()
    .DisableAgent()
    .DisableMcp()
    .DisableTelemetry());
if (!serveWeb)
{
    app.MapGet("/", () => Results.Redirect("/scalar")).ExcludeFromDescription();
}

app.MapDemoEndpoints();

app.MapAuthEndpoints();
app.MapUserEndpoints();
app.MapConversationEndpoints();
app.MapMessageEndpoints();
app.MapPresenceEndpoints();
app.MapHub<ChatHub>(AuthSetup.HubPath);

app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains("ready") });
if (serveWeb)
{
    // Every other path is a page of the web app; unknown API paths stay 404s.
    app.MapFallback(async context =>
    {
        if (WebHeadersMiddleware.IsApi(context.Request.Path))
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        context.Response.ContentType = "text/html; charset=utf-8";
        context.Response.Headers.CacheControl = "no-cache";
        await context.Response.SendFileAsync(indexPage, context.RequestAborted);
    });
}

if (app.Configuration.GetValue("Database:MigrateOnStartup", false))
{
    await using var scope = app.Services.CreateAsyncScope();
    await scope.ServiceProvider.GetRequiredService<ChatDbContext>().Database.MigrateAsync();
}

await app.RunAsync();

public partial class Program;
