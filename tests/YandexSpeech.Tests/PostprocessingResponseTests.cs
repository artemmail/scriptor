using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Xunit;
using YandexSpeech.services;

namespace YandexSpeech.Tests;

public sealed class PostprocessingResponseTests
{
    [Theory]
    [InlineData("length", "truncated")]
    [InlineData("content_filter", "partial")]
    [InlineData("stop", " ")]
    public async Task RejectsIncompleteOrEmptyResponses(string reason, string text)
    {
        var client = CreateClient(reason, text);
        await Assert.ThrowsAsync<InvalidOperationException>(() => client.CompleteAsync(new { }));
    }

    [Fact]
    public async Task AcceptsCompletedText()
        => Assert.Equal("# Result", await CreateClient("stop", "# Result").CompleteAsync(new { }));

    private static OpenAiBatchClient CreateClient(string reason, string text)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["OpenAI:ApiKey"] = "test",
            ["OpenAI:UseBatch"] = "false"
        }).Build();
        var json = JsonSerializer.Serialize(new { choices = new[] { new { finish_reason = reason, message = new { content = text } } } });
        return new OpenAiBatchClient(new Factory(json), config);
    }

    private sealed class Factory(string json) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(new Handler(json));
    }

    private sealed class Handler(string json) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json) });
    }
}
