using System;
using System.IO;
using System.Net.Mail;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;
using YandexSpeech.Controllers;
using YandexSpeech.models.DB;
using YandexSpeech.models.DTO;
using YandexSpeech.services.Interface;
using YandexSpeech.services.Options;

namespace YandexSpeech.Tests;

public sealed class SupportControllerTests
{
    [Fact]
    public async Task SendsPlainTextWithAccountReplyToAndAttachment()
    {
        using var provider = CreateProvider();
        using var scope = provider.CreateScope();
        var controller = await CreateController(scope.ServiceProvider);
        var sender = scope.ServiceProvider.GetRequiredService<RecordingSender>();
        using var stream = new MemoryStream(new byte[] { 1, 2, 3 });
        var model = ValidModel();
        model.Text = "  <script>alert('test')</script>\nОписание  ";
        model.UploadedFile = new FormFile(stream, 0, stream.Length, "UploadedFile", "../screen.png");
        sender.Inspect = mail =>
        {
            Assert.Equal("support@example.com", mail.To[0].Address);
            Assert.Equal("sender@example.com", mail.From!.Address);
            Assert.Equal("alice@example.com", mail.ReplyToList[0].Address);
            Assert.False(mail.IsBodyHtml);
            Assert.Contains("<script>alert('test')</script>\nОписание", mail.Body);
            Assert.Contains("ID: alice", mail.Body);
            Assert.Contains("[YouScriptor]", mail.Subject);
            var attachment = Assert.Single(mail.Attachments);
            Assert.Equal("screen.png", attachment.Name);
            Assert.Equal(1, attachment.ContentStream.ReadByte());
        };

        Assert.IsType<OkObjectResult>(await controller.PostAsync(model, default));
        Assert.Equal(1, sender.Calls);
    }

    [Theory]
    [InlineData(" ", "Текст", "Вопрос")]
    [InlineData("Тема", " \n ", "Вопрос")]
    [InlineData("Тема", "Текст", " ")]
    [InlineData("Тема\r\nBcc: attacker@example.com", "Текст", "Вопрос")]
    public async Task RejectsInvalidFields(string header, string text, string type)
    {
        using var provider = CreateProvider();
        using var scope = provider.CreateScope();
        var controller = await CreateController(scope.ServiceProvider);
        Assert.IsType<BadRequestObjectResult>(await controller.PostAsync(
            new SupportFormModel { Header = header, Text = text, MessageType = type }, default));
        Assert.Equal(0, scope.ServiceProvider.GetRequiredService<RecordingSender>().Calls);
    }

    [Fact]
    public async Task RejectsOversizedFileAndTextBeforeSending()
    {
        using var provider = CreateProvider();
        using var scope = provider.CreateScope();
        var controller = await CreateController(scope.ServiceProvider);
        var model = ValidModel();
        model.Text = new string('x', 10001);
        Assert.IsType<BadRequestObjectResult>(await controller.PostAsync(model, default));
        model = ValidModel();
        model.UploadedFile = new FormFile(Stream.Null, 0, SupportFormModel.MaxFileSize + 1, "UploadedFile", "large.zip");
        Assert.IsType<BadRequestObjectResult>(await controller.PostAsync(model, default));
        Assert.Equal(0, scope.ServiceProvider.GetRequiredService<RecordingSender>().Calls);
    }

    [Fact]
    public async Task DeletedOrUnknownAccountCannotSend()
    {
        using var provider = CreateProvider();
        using var scope = provider.CreateScope();
        var controller = await CreateController(scope.ServiceProvider, "missing");
        Assert.IsType<UnauthorizedResult>(await controller.PostAsync(ValidModel(), default));
        Assert.Equal(0, scope.ServiceProvider.GetRequiredService<RecordingSender>().Calls);
    }

    [Fact]
    public async Task MissingConfigurationDoesNotReportSuccess()
    {
        using var provider = CreateProvider(new SmtpOptions());
        using var scope = provider.CreateScope();
        var controller = await CreateController(scope.ServiceProvider);
        var result = Assert.IsType<ObjectResult>(await controller.PostAsync(ValidModel(), default));
        Assert.Equal(503, result.StatusCode);
        Assert.Equal(0, scope.ServiceProvider.GetRequiredService<RecordingSender>().Calls);
    }

    [Fact]
    public async Task SmtpFailureDoesNotExposeServerDetails()
    {
        using var provider = CreateProvider();
        using var scope = provider.CreateScope();
        var controller = await CreateController(scope.ServiceProvider);
        scope.ServiceProvider.GetRequiredService<RecordingSender>().Inspect = _ => throw new SmtpException("private SMTP details");
        var result = Assert.IsType<ObjectResult>(await controller.PostAsync(ValidModel(), default));
        Assert.Equal(503, result.StatusCode);
        Assert.DoesNotContain("private SMTP details", System.Text.Json.JsonSerializer.Serialize(result.Value));
    }

    private static SupportFormModel ValidModel() => new() { Header = "Тема", Text = "Текст", MessageType = "Вопрос" };

    private static ServiceProvider CreateProvider(SmtpOptions? smtp = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<MyDbContext>(options => options.UseInMemoryDatabase(Guid.NewGuid().ToString()));
        services.AddIdentityCore<ApplicationUser>().AddEntityFrameworkStores<MyDbContext>();
        services.AddSingleton(Options.Create(smtp ?? new SmtpOptions
        {
            Host = "smtp.example.com", UserName = "sender@example.com", Password = "test",
            SupportEmail = "support@example.com"
        }));
        services.AddSingleton<RecordingSender>();
        return services.BuildServiceProvider();
    }

    private static async Task<SupportController> CreateController(IServiceProvider services, string userId = "alice")
    {
        var db = services.GetRequiredService<MyDbContext>();
        db.Users.Add(new ApplicationUser { Id = "alice", UserName = "alice", Email = "alice@example.com", DisplayName = "Alice" });
        await db.SaveChangesAsync();
        return new SupportController(services.GetRequiredService<UserManager<ApplicationUser>>(),
            services.GetRequiredService<IOptions<SmtpOptions>>(), services.GetRequiredService<RecordingSender>(),
            NullLogger<SupportController>.Instance)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, userId) }, "Test"))
                }
            }
        };
    }

    private sealed class RecordingSender : ISupportEmailSender
    {
        public int Calls { get; private set; }
        public Action<MailMessage>? Inspect { get; set; }
        public Task SendAsync(MailMessage message, CancellationToken cancellationToken)
        {
            Calls++;
            Inspect?.Invoke(message);
            return Task.CompletedTask;
        }
    }
}
