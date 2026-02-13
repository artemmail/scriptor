using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using YandexSpeech.services.Options;

namespace YandexSpeech.services.Whisper
{
    public sealed class FasterWhisperQueueClient : IAsyncDisposable
    {
        private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
        private static readonly TimeSpan BufferedResponseRetention = TimeSpan.FromMinutes(30);

        private readonly ILogger<FasterWhisperQueueClient> _logger;
        private readonly EventBusOptions _options;
        private readonly ConcurrentDictionary<string, TaskCompletionSource<FasterWhisperQueueResponse>> _pending = new();
        private readonly ConcurrentDictionary<string, BufferedResponse> _bufferedResponses = new();
        private readonly SemaphoreSlim _channelSync = new(1, 1);

        private readonly IConnection _connection;
        private readonly IChannel _channel;

        private readonly CancellationTokenSource _receiverCts = new();
        private readonly Task _receiverTask;

        public FasterWhisperQueueClient(IOptions<EventBusOptions> options, ILogger<FasterWhisperQueueClient> logger)
        {
            _logger = logger;
            _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
            _options.Validate();

            var access = _options.BusAccess ?? throw new InvalidOperationException("Event bus access options are not configured.");

            var factory = CreateFactory(access, _options.Broker);
            _connection = CreateConnection(factory);
            _channel = _connection.CreateChannelAsync().GetAwaiter().GetResult();

            // Declare queues and QoS synchronously because the constructor cannot be async.
            _channel.QueueDeclareAsync(
                _options.CommandQueueName,
                durable: true,
                exclusive: false,
                autoDelete: false,
                arguments: CreateQueueArguments(_options.ConsumerTimeoutMs)).Wait();

            _channel.QueueDeclareAsync(
                _options.QueueName,
                durable: true,
                exclusive: false,
                autoDelete: false,
                arguments: CreateQueueArguments(_options.ConsumerTimeoutMs)).Wait();

            _channel.BasicQosAsync(prefetchSize: 0, prefetchCount: 1, global: false).Wait();

            _receiverTask = Task.Run(() => PollResponsesAsync(_receiverCts.Token));

            _logger.LogInformation(
                "Initialized FasterWhisper RabbitMQ client. CommandQueue={CommandQueue}, ResponseQueue={ResponseQueue}",
                _options.CommandQueueName,
                _options.QueueName);
        }

        public async Task<FasterWhisperQueueResponse> TranscribeAsync(FasterWhisperQueueRequest request, CancellationToken cancellationToken)
        {
            if (request is null)
                throw new ArgumentNullException(nameof(request));

            var correlationId = BuildCorrelationId(request);
            var body = JsonSerializer.SerializeToUtf8Bytes(request, JsonOptions);

            var props = new BasicProperties
            {
                Persistent = true,
                CorrelationId = correlationId,
                ReplyTo = _options.QueueName,
                ContentType = "application/json"
            };

            var tcs = new TaskCompletionSource<FasterWhisperQueueResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
            if (!_pending.TryAdd(correlationId, tcs))
            {
                if (_pending.TryGetValue(correlationId, out var existingPending))
                {
                    _logger.LogInformation(
                        "Joining existing pending FasterWhisper request with correlation id {CorrelationId}.",
                        correlationId);
                    return await existingPending.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
                }

                throw new InvalidOperationException("Failed to register pending transcription request.");
            }

            try
            {
                CleanupBufferedResponses();
                if (TryResolveFromBufferedResponse(correlationId))
                {
                    return await tcs.Task.ConfigureAwait(false);
                }

                await _channelSync.WaitAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    await _channel.BasicPublishAsync(
                        exchange: string.Empty,
                        routingKey: _options.CommandQueueName,
                        mandatory: false,
                        basicProperties: props,
                        body: body,
                        cancellationToken: cancellationToken
                    ).ConfigureAwait(false);
                }
                finally
                {
                    _channelSync.Release();
                }
            }
            catch
            {
                _pending.TryRemove(correlationId, out _);
                throw;
            }

            using var registration = cancellationToken.Register(() =>
            {
                if (_pending.TryRemove(correlationId, out var pending))
                    pending.TrySetCanceled(cancellationToken);
            });

            return await tcs.Task.ConfigureAwait(false);
        }

        private async Task PollResponsesAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                RabbitMqMessage? message = null;

                try
                {
                    await _channelSync.WaitAsync(cancellationToken).ConfigureAwait(false);
                    try
                    {
                        var result = await _channel.BasicGetAsync(_options.QueueName, autoAck: false).ConfigureAwait(false);
                        if (result is not null)
                        {
                            var body = result.Body.ToArray();
                            message = new RabbitMqMessage(body, result.BasicProperties, result.DeliveryTag);
                        }
                    }
                    finally
                    {
                        _channelSync.Release();
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to receive FasterWhisper response message from RabbitMQ.");
                    await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken).ConfigureAwait(false);
                    continue;
                }

                if (message is null)
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(250), cancellationToken).ConfigureAwait(false);
                    continue;
                }

                await HandleResponse(message).ConfigureAwait(false);
            }
        }

        private async Task HandleResponse(RabbitMqMessage delivery)
        {
            TaskCompletionSource<FasterWhisperQueueResponse>? pending = null;
            string? correlationId = null;
            try
            {
                correlationId = delivery.Properties?.CorrelationId;
                var json = Encoding.UTF8.GetString(delivery.Body);
                var response = JsonSerializer.Deserialize<FasterWhisperQueueResponse>(json, JsonOptions)
                               ?? throw new InvalidOperationException("Response payload was empty.");

                if (string.IsNullOrEmpty(correlationId))
                {
                    _logger.LogWarning("Received FasterWhisper response without correlation id.");
                    return;
                }

                if (_pending.TryRemove(correlationId, out pending))
                {
                    pending.TrySetResult(response);
                    return;
                }

                CleanupBufferedResponses();
                _bufferedResponses[correlationId] = new BufferedResponse(response, DateTime.UtcNow);
                _logger.LogInformation(
                    "Buffered unexpected FasterWhisper response with correlation id {CorrelationId} for later recovery.",
                    correlationId);
            }
            catch (Exception ex)
            {
                pending?.TrySetException(ex);
                _logger.LogError(ex, "Failed to process FasterWhisper response message.");
            }
            finally
            {
                await _channelSync.WaitAsync().ConfigureAwait(false);
                try
                {
                    await _channel.BasicAckAsync(delivery.DeliveryTag, multiple: false).ConfigureAwait(false);
                }
                finally
                {
                    _channelSync.Release();
                }
            }
        }

        private bool TryResolveFromBufferedResponse(string correlationId)
        {
            if (!_bufferedResponses.TryRemove(correlationId, out var buffered))
                return false;

            if (!_pending.TryRemove(correlationId, out var pending))
                return false;

            pending.TrySetResult(buffered.Response);
            _logger.LogInformation(
                "Resolved FasterWhisper request from buffered response with correlation id {CorrelationId}.",
                correlationId);
            return true;
        }

        private void CleanupBufferedResponses()
        {
            if (_bufferedResponses.IsEmpty)
                return;

            var now = DateTime.UtcNow;
            foreach (var pair in _bufferedResponses)
            {
                if (now - pair.Value.ReceivedAtUtc > BufferedResponseRetention)
                {
                    _bufferedResponses.TryRemove(pair.Key, out _);
                }
            }
        }

        private static string BuildCorrelationId(FasterWhisperQueueRequest request)
        {
            var payload = JsonSerializer.Serialize(request, JsonOptions);
            var hash = SHA256.HashData(Encoding.UTF8.GetBytes(payload));
            return "fw-" + Convert.ToHexString(hash).ToLowerInvariant();
        }

        public async ValueTask DisposeAsync()
        {
            try
            {
                _receiverCts.Cancel();
                if (_receiverTask is not null)
                {
                    await _receiverTask.ConfigureAwait(false);
                }
            }
            catch
            {
                // ignore cancellation exceptions during shutdown
            }
            finally
            {
                _receiverCts.Dispose();
                _channelSync.Dispose();
            }

            await DisposeSafelyAsync(_channel).ConfigureAwait(false);
            await DisposeSafelyAsync(_connection).ConfigureAwait(false);
        }

        private static Dictionary<string, object>? CreateQueueArguments(int? consumerTimeoutMs)
        {
            if (consumerTimeoutMs is null || consumerTimeoutMs <= 0)
            {
                return null;
            }

            return new Dictionary<string, object>
            {
                ["x-consumer-timeout"] = consumerTimeoutMs.Value
            };
        }

        private static ConnectionFactory CreateFactory(EventBusAccessOptions access, string? brokerName)
        {
            var factory = new ConnectionFactory
            {
                HostName = access.Host,
                UserName = access.UserName,
                Password = access.Password,
                AutomaticRecoveryEnabled = true,
                NetworkRecoveryInterval = TimeSpan.FromSeconds(5)
            };

            var clientName = string.IsNullOrWhiteSpace(brokerName) ? "faster-whisper" : brokerName;
            factory.ClientProvidedName = clientName;

            return factory;
        }

        private static IConnection CreateConnection(ConnectionFactory factory)
            => factory.CreateConnectionAsync().GetAwaiter().GetResult();

        private static ValueTask DisposeSafelyAsync(IDisposable? disposable)
        {
            if (disposable is null)
            {
                return ValueTask.CompletedTask;
            }

            try
            {
                disposable.Dispose();
            }
            catch
            {
                // ignore dispose exceptions
            }

            return ValueTask.CompletedTask;
        }
    }

    internal sealed record RabbitMqMessage(byte[] Body, IReadOnlyBasicProperties? Properties, ulong DeliveryTag);
    internal sealed record BufferedResponse(FasterWhisperQueueResponse Response, DateTime ReceivedAtUtc);

    public sealed record FasterWhisperQueueRequest
    {
        public string Audio { get; init; } = string.Empty;
        public string Model { get; init; } = string.Empty;
        public string Device { get; init; } = string.Empty;
        public string ComputeType { get; init; } = string.Empty;
        public string Language { get; init; } = string.Empty;
        public string Temperature { get; init; } = string.Empty;
        public string CompressionRatioThreshold { get; init; } = string.Empty;
        public string LogProbThreshold { get; init; } = string.Empty;
        public string NoSpeechThreshold { get; init; } = string.Empty;
        public string ConditionOnPreviousText { get; init; } = string.Empty;
        public string? FfmpegExecutable { get; init; }
    }

    public sealed class FasterWhisperQueueResponse
    {
        public bool Success { get; set; }
        public string? Error { get; set; }
        public string? TranscriptJson { get; set; }
        public string? Diagnostics { get; set; }
    }
}
