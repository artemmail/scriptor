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
using YandexSpeech.services.Telegram;
using YandexSpeech.services.Whisper;
using YandexSpeech.Services;
using YoutubeDownload.Managers;
using YoutubeDownload.Services;
using YoutubeExplode;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using Microsoft.Extensions.FileProviders;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;

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
builder.Services.AddControllers();

// Общий HttpClient для внешних запросов
builder.Services.AddHttpClient();
builder.Services.AddHttpClient(nameof(TelegramTranscriptionBot));

builder.Services.AddSingleton<IFfmpegService, FfmpegService>();

// 3. DbContext
var conn = builder.Configuration.GetConnectionString("DefaultConnection")
           ?? throw new InvalidOperationException("DefaultConnection not found");
builder.Services.AddDbContext<MyDbContext>(opts => opts.UseSqlServer(conn));

// 4. Identity
builder.Services.AddIdentity<ApplicationUser, IdentityRole>()
    .AddEntityFrameworkStores<MyDbContext>()
    .AddDefaultTokenProviders();

builder.Services.ConfigureExternalCookie(options =>
{
    options.Cookie.SameSite = SameSiteMode.None;
    options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
    options.Cookie.Name = CookieAuthenticationDefaults.CookiePrefix + "External";
});

builder.Services.ConfigureApplicationCookie(options =>
{
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
});

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

var googleClientId = builder.Configuration["Authentication:Google:ClientId"];
var googleClientSecret = builder.Configuration["Authentication:Google:ClientSecret"];
if (!string.IsNullOrWhiteSpace(googleClientId) && !string.IsNullOrWhiteSpace(googleClientSecret))
{
    authBuilder.AddGoogle(opts =>
    {
        opts.ClientId = googleClientId;
        opts.ClientSecret = googleClientSecret;
        opts.CallbackPath = "/signin-google";
        opts.SaveTokens = true;

        // Явно указываем конечные точки OAuth 2.0 Google, чтобы избежать
        // предварительного HTTP-запроса к discovery-документу. На некоторых
        // машинах (особенно при локальной отладке) этот запрос через HTTP/2
        // завершается с ошибкой "The remote party closed the WebSocket connection
        // without completing the close handshake", из-за чего окно выбора аккаунта
        // даже не открывается. Пробрасываем конфигурацию вручную и отключаем
        // ConfigurationManager, чтобы handler использовал статические URL.
        opts.AuthorizationEndpoint = "https://accounts.google.com/o/oauth2/v2/auth";
        // Fix for region-based block of oauth2.googleapis.com (Crimea). Using www.googleapis.com instead.
        opts.TokenEndpoint = "https://www.googleapis.com/oauth2/v4/token";
        opts.UserInformationEndpoint = "https://www.googleapis.com/oauth2/v3/userinfo";

        // В некоторых окружениях Google может обрывать HTTP/2-соединения,
        // что приводит к WebSocketException внутри обработчика входа.
        // Настраиваем собственный backchannel-клиент, принудительно работающий
        // по HTTP/1.1, чтобы избежать ошибок вида
        // "The remote party closed the WebSocket connection without completing the close handshake."
        var backchannelHandler = new HttpClientHandler
        {
            AllowAutoRedirect = false,
            UseCookies = false
        };

        if (backchannelHandler.SupportsAutomaticDecompression)
        {
            backchannelHandler.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
        }

        var backchannelClient = new HttpClient(backchannelHandler)
        {
            Timeout = opts.BackchannelTimeout,
            DefaultRequestVersion = HttpVersion.Version11,
#if NET8_0_OR_GREATER
            DefaultVersionPolicy = HttpVersionPolicy.RequestVersionOrLower,
#endif
            MaxResponseContentBufferSize = 1024 * 1024 * 10 // 10 MB аналогично настройкам OAuthHandler
        };

        backchannelClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        backchannelClient.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("Microsoft.AspNetCore.Authentication.OAuth", "1.0"));
        backchannelClient.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("Microsoft.AspNetCore.Authentication.Google", "1.0"));

        opts.BackchannelHttpHandler = backchannelHandler;
        opts.Backchannel = backchannelClient;

        // Перехватываем ошибку silent-входа и возвращаем корректный HTML с postMessage
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

            // Для обычного (не-silent) входа — редиректим на callback с ошибкой
            var redirect = $"/api/account/externallogincallback?remoteError={Uri.EscapeDataString(ctx.Failure?.Message ?? "auth_error")}";
            ctx.Response.Redirect(redirect);
            ctx.HandleResponse();
            return Task.CompletedTask;
        };
    });
}

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
/*
builder.Services.AddSingleton<TelegramTranscriptionBot>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<TelegramTranscriptionBot>());
*/

// 8. SPA static files
builder.Services.AddSpaStaticFiles(opts => opts.RootPath = "wwwroot");

var app = builder.Build();

await EnsureRolesAsync(app.Services);
await SubscriptionPlanSeeder.EnsureDefaultPlansAsync(app.Services);
await ResumeIncompleteOpenAiTasksAsync(app.Services);

// (пропущена инициализация ролей и IndexNow для краткости)

app.UseForwardedHeaders(new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost
});

app.UseHttpsRedirection();

if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
}

app.UseRouting();

app.Use(async (context, next) =>
{
    if ((HttpMethods.IsGet(context.Request.Method) || HttpMethods.IsHead(context.Request.Method)) &&
        context.Request.Path.StartsWithSegments("/recognized", out var remaining))
    {
        var remainder = remaining.Value;
        if (!string.IsNullOrEmpty(remainder) && !remainder.Contains('/'))
        {
            var candidate = remainder.Trim('/');
            if (!string.IsNullOrEmpty(candidate))
            {
                await using var scope = context.RequestServices.CreateAsyncScope();
                var dbContext = scope.ServiceProvider.GetRequiredService<MyDbContext>();
                var slug = await dbContext.YoutubeCaptionTasks
                    .Where(t => t.Id == candidate && !string.IsNullOrEmpty(t.Slug))
                    .Select(t => t.Slug)
                    .FirstOrDefaultAsync();

                if (!string.IsNullOrEmpty(slug) &&
                    !string.Equals(slug, candidate, StringComparison.OrdinalIgnoreCase))
                {
                    var destination = context.Request.QueryString.HasValue
                        ? $"/recognized/{slug}{context.Request.QueryString}"
                        : $"/recognized/{slug}";

                    context.Response.Redirect(destination, permanent: true);
                    return;
                }
            }
        }
    }

    await next();
});
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

static async Task ResumeIncompleteOpenAiTasksAsync(IServiceProvider services)
{
    using var scope = services.CreateScope();
    var dbContext = scope.ServiceProvider.GetRequiredService<MyDbContext>();
    var loggerFactory = scope.ServiceProvider.GetRequiredService<ILoggerFactory>();
    var logger = loggerFactory.CreateLogger("OpenAiTranscriptionStartupRecovery");
    var pendingTaskIds = await dbContext.OpenAiTranscriptionTasks
        .AsNoTracking()
        .Where(t => t.Status != OpenAiTranscriptionStatus.Done)
        .OrderBy(t => t.CreatedAt)
        .Select(t => t.Id)
        .ToListAsync();

    if (pendingTaskIds.Count == 0)
    {
        return;
    }

    logger.LogInformation(
        "Scheduling {Count} OpenAI transcription task(s) for startup recovery.",
        pendingTaskIds.Count);

    _ = Task.Run(async () =>
    {
        const int maxConcurrentRecoveryTasks = 3;
        using var semaphore = new SemaphoreSlim(maxConcurrentRecoveryTasks, maxConcurrentRecoveryTasks);

        var workers = pendingTaskIds.Select(async taskId =>
        {
            await semaphore.WaitAsync();
            try
            {
                await using var taskScope = services.CreateAsyncScope();
                var transcriptionService = taskScope.ServiceProvider.GetRequiredService<IOpenAiTranscriptionService>();
                await transcriptionService.ContinueTranscriptionAsync(taskId);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to recover OpenAI transcription task {TaskId} on startup.", taskId);
            }
            finally
            {
                semaphore.Release();
            }
        }).ToList();

        await Task.WhenAll(workers);
    });
}
