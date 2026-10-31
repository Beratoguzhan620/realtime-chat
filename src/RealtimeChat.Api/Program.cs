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
using RealtimeChat.Api.Features.Hub;
using RealtimeChat.Api.Features.Messages;
using RealtimeChat.Api.Features.Users;
using RealtimeChat.Api.Logging;
using RealtimeChat.Infrastructure;
using RealtimeChat.Infrastructure.Persistence;
using Scalar.AspNetCore;
using Serilog;
using Serilog.Core;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton<ILogEventEnricher, SensitiveDataEnricher>();
builder.Services.AddSerilog((services, logger) => logger
    .ReadFrom.Configuration(builder.Configuration)
    .ReadFrom.Services(services)
    .Enrich.FromLogContext());

var connectionString = builder.Configuration.GetConnectionString("Chat");
if (string.IsNullOrWhiteSpace(connectionString))
{
    throw new InvalidOperationException(
        "ConnectionStrings:Chat is not configured. Use 'dotnet user-secrets' or the ConnectionStrings__Chat environment variable.");
}

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddChatPersistence(connectionString);
builder.Services.AddChatAuth(builder.Configuration);

builder.Services.Configure<ChatLimits>(builder.Configuration.GetSection(ChatLimits.Section));
builder.Services.AddSingleton<SendRateLimiter>();
builder.Services.AddSingleton<ChatEvents>();
builder.Services.AddScoped<MessageService>();
builder.Services.AddSingleton<IUserIdProvider, SubjectUserIdProvider>();
builder.Services.AddSignalR(options =>
    {
        options.AddFilter<HubErrorFilter>();
        // A message is at most 4000 characters; nothing a client sends needs more than this.
        options.MaximumReceiveMessageSize = 32 * 1024;
    })
    .AddJsonProtocol(options => options.PayloadSerializerOptions.Converters.Add(new JsonStringEnumConverter()));

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
    options.AddPolicy(AuthEndpoints.LoginRateLimit, context => RateLimitPartition.GetFixedWindowLimiter(
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
    .AddDbContextCheck<ChatDbContext>("database", tags: ["ready"]);

var app = builder.Build();

app.UseMiddleware<CorrelationIdMiddleware>();
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
app.MapGet("/", () => Results.Redirect("/scalar")).ExcludeFromDescription();

app.MapAuthEndpoints();
app.MapUserEndpoints();
app.MapConversationEndpoints();
app.MapMessageEndpoints();
app.MapHub<ChatHub>(AuthSetup.HubPath);

app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains("ready") });

if (app.Configuration.GetValue("Database:MigrateOnStartup", false))
{
    await using var scope = app.Services.CreateAsyncScope();
    await scope.ServiceProvider.GetRequiredService<ChatDbContext>().Database.MigrateAsync();
}

await app.RunAsync();

public partial class Program;
