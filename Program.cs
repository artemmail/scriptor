// Program.cs
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using YandexSpeech;
using YandexSpeech.models.DB;
using YandexSpeech.services;
using YandexSpeech.Services;
using YoutubeDownload.Managers;
using YoutubeDownload.Services;
using YoutubeExplode;
using System.IO;
using Microsoft.Extensions.FileProviders;

var builder = WebApplication.CreateBuilder(args);

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

// 6. Аутентификация (JWT + Google + VK)
builder.Services.AddAuthentication(o =>
{
    o.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    o.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(o =>
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
})
.AddGoogle(opts =>
{
    opts.ClientId = builder.Configuration["Authentication:Google:ClientId"];
    opts.ClientSecret = builder.Configuration["Authentication:Google:ClientSecret"];

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
})
.AddVkontakte(opts =>
{
    opts.ClientId = builder.Configuration["Authentication:Vkontakte:ClientId"];
    opts.ClientSecret = builder.Configuration["Authentication:Vkontakte:ClientSecret"];
    opts.Scope.Add("email");
    opts.Fields.Add("photo_100");
    opts.SaveTokens = true;
});



builder.Services.AddScoped<IAudioFileService, AudioFileService>();
builder.Services.AddScoped<ISpeechWorkflowService, SpeechWorkflowService>();


// 7. Сервисы приложения
builder.Services.AddSingleton<YoutubeClient>();
builder.Services.AddSingleton<CaptionService>();
builder.Services.AddScoped<IYSpeechService, YSpeechService>();
builder.Services.AddSingleton<IPunctuationService, PunctuationService>();
builder.Services.AddScoped<ISpeechWorkflowService, SpeechWorkflowService>();

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



// 8. SPA static files
builder.Services.AddSpaStaticFiles(opts => opts.RootPath = "wwwroot");

var app = builder.Build();

await EnsureRolesAsync(app.Services);

// (пропущена инициализация ролей и IndexNow для краткости)

app.UseRouting();
app.UseCors("AllowAngularApp");
app.UseAuthentication();
app.UseAuthorization();

app.UseEndpoints(endpoints => endpoints.MapControllers());

if (app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
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

    var rolesToEnsure = new[] { "Free", "Moderator" };

    foreach (var roleName in rolesToEnsure)
    {
        if (!await roleManager.RoleExistsAsync(roleName))
        {
            await roleManager.CreateAsync(new IdentityRole(roleName));
        }
    }
}
