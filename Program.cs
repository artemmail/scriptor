// Program.cs
using AspNet.Security.OAuth.Yandex;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using System.Linq;
using YandexSpeech;
using YandexSpeech.models.DB;
using YandexSpeech.services;
using YandexSpeech.services.Interface;
using YandexSpeech.services.Options;
using YandexSpeech.services.Authentication;
using YandexSpeech.services.Telegram;
using YandexSpeech.services.TelegramTranscriptionBot.State;
using YandexSpeech.services.Whisper;
using YandexSpeech.Services;
using YandexSpeech.Extensions;
using YoutubeDownload.Managers;
using YoutubeDownload.Services;
using YoutubeExplode;
using System.IO;
using Microsoft.Extensions.FileProviders;
using Microsoft.AspNetCore.Http.Features;
using YandexSpeech.services.Google;
using YandexSpeech.services.TelegramIntegration;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<FormOptions>(options =>
{
    options.MultipartBodyLengthLimit = long.MaxValue;
    options.ValueLengthLimit = int.MaxValue;
    options.MultipartHeadersLengthLimit = int.MaxValue;
});

builder.Services.Configure<IISServerOptions>(options =>
{
    options.MaxRequestBodySize = long.MaxValue;
});

builder.WebHost.ConfigureKestrel(options =>
{
    options.Limits.MaxRequestBodySize = long.MaxValue;
});

// 1. Настройка CORS
builder.Services.AddCors(opts =>
{
    opts.AddPolicy("AllowAngularApp", p =>
        p.WithOrigins("http://localhost:4200")
         .AllowAnyMethod()
         .AllowAnyHeader());
});

// 2. Контроллеры
builder.Services.AddControllersWithViews();

// Общий HttpClient для внешних запросов
builder.Services.AddHttpClient();
builder.Services.AddHttpClient(nameof(TelegramTranscriptionBot));
builder.Services.AddHttpClient(nameof(TelegramTranscriptionBot) + ".Integration");
builder.Services.AddHttpClient(nameof(TelegramIntegrationNotifier));

builder.Services.AddSingleton<IFfmpegService, FfmpegService>();

// 3. DbContext
var conn = builder.Configuration.GetConnectionString("DefaultConnection")
           ?? throw new InvalidOperationException("DefaultConnection not found");
builder.Services.AddDbContext<MyDbContext>(opts => opts.UseSqlServer(conn));

// 4. Identity
builder.Services.AddIdentity<ApplicationUser, IdentityRole>()
    .AddEntityFrameworkStores<MyDbContext>()
    .AddDefaultTokenProviders();

// 5. JWT-настройки
var jwtSection = builder.Configuration.GetSection("Jwt");
var keyBytes = Encoding.UTF8.GetBytes(jwtSection["Key"]!);
var jwtKeyId = jwtSection["KeyId"]!;
var jwtIssuer = jwtSection["Issuer"]!;
var jwtAudience = jwtSection["Audience"]!;

// 6. Аутентификация (JWT + Google + Yandex + VK)
var authBuilder = builder.Services.AddAuthentication(o =>
{
    o.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    o.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
});

authBuilder.AddJwtBearer(o =>
{
    o.RequireHttpsMetadata = false; // Dev only
    o.SaveToken = true;
    o.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = new SymmetricSecurityKey(keyBytes) { KeyId = jwtKeyId },
        ValidateIssuer = true,
        ValidIssuer = jwtIssuer,
        ValidateAudience = true,
        ValidAudience = jwtAudience,
        ClockSkew = TimeSpan.Zero
    };
});

authBuilder.AddScheme<AuthenticationSchemeOptions, IntegrationApiAuthenticationHandler>(
    IntegrationApiAuthenticationDefaults.AuthenticationScheme,
    _ => { });

authBuilder.AddGoogleOAuthConfigurations(builder.Configuration, opts =>
{
    opts.Events.OnRemoteFailure = ctx =>
    {
        var silent = ctx.Request.Query.TryGetValue("silent", out var s) && s == "true";
        if (silent)
        {
            var origin = $"{ctx.Request.Scheme}://{ctx.Request.Host}";
            var html = $@"<!doctype html>
<html><head><meta charset=""utf-8""></head><body>
<script>
  window.parent.postMessage(JSON.stringify({{ type:'silent_login', status:'failed' }}), '{origin}');
</script>
</body></html>";
            ctx.Response.ContentType = "text/html; charset=utf-8";
            ctx.Response.Headers["Cache-Control"] = "no-store";
            ctx.HandleResponse();
            return ctx.Response.WriteAsync(html);
        }

        var redirect = $"/api/account/externallogincallback?remoteError={Uri.EscapeDataString(ctx.Failure?.Message ?? "auth_error")}";
        ctx.Response.Redirect(redirect);
        ctx.HandleResponse();
        return Task.CompletedTask;
    };
});

var yandexClientId = builder.Configuration["Authentication:Yandex:ClientId"];
var yandexClientSecret = builder.Configuration["Authentication:Yandex:ClientSecret"];
if (!string.IsNullOrWhiteSpace(yandexClientId) && !string.IsNullOrWhiteSpace(yandexClientSecret))
{
    authBuilder.AddYandex(opts =>
    {
        opts.ClientId = yandexClientId;
        opts.ClientSecret = yandexClientSecret;
        opts.Scope.Add("login:email");
        opts.SaveTokens = true;

        opts.Events.OnRemoteFailure = ctx =>
        {
            var redirect = $"/api/account/externallogincallback?remoteError={Uri.EscapeDataString(ctx.Failure?.Message ?? "auth_error")}";
            ctx.Response.Redirect(redirect);
            ctx.HandleResponse();
            return Task.CompletedTask;
        };
    });
}

var vkClientId = builder.Configuration["Authentication:Vkontakte:ClientId"];
var vkClientSecret = builder.Configuration["Authentication:Vkontakte:ClientSecret"];
if (!string.IsNullOrWhiteSpace(vkClientId) && !string.IsNullOrWhiteSpace(vkClientSecret))
{
    authBuilder.AddVkontakte(opts =>
    {
        opts.ClientId = vkClientId;
        opts.ClientSecret = vkClientSecret;
        opts.Scope.Add("email");
        opts.Fields.Add("photo_100");
        opts.SaveTokens = true;
    });
}



builder.Services.AddScoped<IAudioFileService, AudioFileService>();


builder.Services.Configure<EventBusOptions>(builder.Configuration.GetSection("EventBus"));
builder.Services.Configure<TelegramBotOptions>(builder.Configuration.GetSection("Telegram"));
builder.Services.Configure<TelegramIntegrationOptions>(builder.Configuration.GetSection("TelegramIntegration"));
builder.Services.AddSingleton<FasterWhisperQueueClient>();

var whisperProvider = builder.Configuration.GetValue<string>("Whisper:Provider");
if (string.Equals(whisperProvider, "faster", StringComparison.OrdinalIgnoreCase)
    || string.Equals(whisperProvider, "faster-whisper", StringComparison.OrdinalIgnoreCase))
{
    builder.Services.AddScoped<IWhisperTranscriptionService, FasterWhisperTranscriptionService>();
}
else
{
    builder.Services.AddScoped<IWhisperTranscriptionService, WhisperCliTranscriptionService>();
}

builder.Services.AddScoped<IOpenAiTranscriptionService, IntegratedFormattingOpenAiTranscriptionService>();


// 7. Сервисы приложения
builder.Services.AddSingleton<YoutubeClient>();
builder.Services.AddSingleton<CaptionService>();
builder.Services.AddScoped<IYSpeechService, YSpeechService>();
builder.Services.AddScoped<IPunctuationService, PunctuationService>();

builder.Services.AddScoped<ISubscriptionService, SubscriptionService>();
builder.Services.AddScoped<IWalletService, WalletService>();
builder.Services.AddScoped<IYandexDiskDownloadService, YandexDiskDownloadService>();
builder.Services.AddScoped<IPaymentGatewayService, PaymentGatewayService>();
builder.Services.AddScoped<ISubscriptionAccessService, SubscriptionAccessService>();
builder.Services.AddScoped<IGoogleTokenService, GoogleTokenService>();

builder.Services.Configure<YooMoneyOptions>(builder.Configuration.GetSection("YooMoney"));
builder.Services.Configure<SubscriptionLimitsOptions>(builder.Configuration.GetSection("SubscriptionLimits"));
builder.Services.AddHttpClient<IYooMoneyRepository, YooMoneyRepository>();

builder.Services.AddAudioTaskManager();  // ← вот эта строка

builder.Services.AddScoped<YoutubeWorkflowService>();

builder.Services.AddScoped<YoutubeStreamService>();
builder.Services.AddSingleton<IRecognitionTaskManager, RecognitionTaskManager>();
builder.Services.AddScoped<IYoutubeCaptionService, YoutubeCaptionService>();
builder.Services.AddScoped<IDocumentGeneratorService, DocumentGeneratorService>();
builder.Services.AddSingleton<ICaptionTaskManager, CaptionTaskManager>();
builder.Services.AddSingleton<IYoutubeDownloadTaskManager, YoutubeDownloadTaskManager>();
builder.Services.AddScoped<IYSubtitlesService, YSubtitlesService>();
builder.Services.AddHostedService<RecognitionBackgroundService>();
builder.Services.AddHostedService<AudioRecognitionBackgroundService>();
builder.Services.AddHostedService<SubscriptionExpirationHostedService>();
builder.Services.AddSingleton<TelegramUserStateStore>();
builder.Services.AddSingleton<TelegramTranscriptionBot>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<TelegramTranscriptionBot>());
builder.Services.AddSingleton<ITelegramIntegrationNotifier, TelegramIntegrationNotifier>();
builder.Services.AddScoped<ITelegramLinkService, TelegramLinkService>();



// 8. SPA static files
builder.Services.AddSpaStaticFiles(opts => opts.RootPath = "wwwroot");

var app = builder.Build();

await EnsureRolesAsync(app.Services);
await SubscriptionPlanSeeder.EnsureDefaultPlansAsync(app.Services);
await MarkIncompleteOpenAiTasksAsErroredAsync(app.Services);

// (пропущена инициализация ролей и IndexNow для краткости)

app.UseHttpsRedirection();

if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
}

app.UseRouting();
app.UseCors("AllowAngularApp");
app.UseAuthentication();
app.UseAuthorization();

app.UseEndpoints(endpoints => endpoints.MapControllers());

if (app.Environment.IsDevelopment())
{
    app.UseSpa(spa =>
    {
        spa.Options.SourcePath = "ClientApp";
        spa.UseProxyToSpaDevelopmentServer("http://localhost:4200");
    });
}
else
{
    var staticFileContentTypeProvider = new FileExtensionContentTypeProvider();
    if (!staticFileContentTypeProvider.Mappings.ContainsKey(".webp"))
    {
        staticFileContentTypeProvider.Mappings[".webp"] = "image/webp";
    }

    var spaRoot = app.Environment.WebRootPath ?? Path.Combine(app.Environment.ContentRootPath, "wwwroot");

    var staticFileOptions = new StaticFileOptions
    {
        FileProvider = new PhysicalFileProvider(spaRoot),
        ContentTypeProvider = staticFileContentTypeProvider
    };

    var defaultFilesOptions = new DefaultFilesOptions
    {
        FileProvider = staticFileOptions.FileProvider
    };

    app.UseDefaultFiles(defaultFilesOptions);
    app.UseStaticFiles(staticFileOptions);
    app.UseSpaStaticFiles(staticFileOptions);
    app.UseSpa(spa =>
    {
        spa.Options.SourcePath = spaRoot;
        spa.Options.DefaultPage = "/index.html";
        spa.Options.DefaultPageStaticFileOptions = staticFileOptions;
    });
}

app.Run();

static async Task EnsureRolesAsync(IServiceProvider services)
{
    using var scope = services.CreateScope();
    var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();

    var rolesToEnsure = new[] { "Free", "Subscriber", "Lifetime", "Moderator", "Admin" };

    foreach (var roleName in rolesToEnsure)
    {
        if (!await roleManager.RoleExistsAsync(roleName))
        {
            await roleManager.CreateAsync(new IdentityRole(roleName));
        }
    }
}

static async Task MarkIncompleteOpenAiTasksAsErroredAsync(IServiceProvider services)
{
    using var scope = services.CreateScope();
    var dbContext = scope.ServiceProvider.GetRequiredService<MyDbContext>();

    var incompleteTasks = await dbContext.OpenAiTranscriptionTasks
        .Where(t => !t.Done)
        .Include(t => t.Steps)
        .Include(t => t.Segments)
        .ToListAsync();

    if (incompleteTasks.Count == 0)
    {
        return;
    }

    var now = DateTime.UtcNow;
    const string restartErrorMessage = "Задача была остановлена из-за перезапуска сервера. Перезапустите обработку вручную.";

    foreach (var task in incompleteTasks)
    {
        task.Status = OpenAiTranscriptionStatus.Error;
        task.Error = restartErrorMessage;
        task.Done = false;
        task.ModifiedAt = now;

        if (task.Steps != null)
        {
            foreach (var step in task.Steps.Where(s => s.Status == OpenAiTranscriptionStepStatus.InProgress))
            {
                step.Status = OpenAiTranscriptionStepStatus.Error;
                step.Error = restartErrorMessage;
                step.FinishedAt = now;
            }
        }

        if (task.Segments != null)
        {
            foreach (var segment in task.Segments.Where(s => s.IsProcessing))
            {
                segment.IsProcessing = false;
            }
        }
    }

    await dbContext.SaveChangesAsync();
}
