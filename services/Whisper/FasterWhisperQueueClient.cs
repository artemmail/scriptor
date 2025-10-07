using System;
using System.Collections.Concurrent;
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

        private readonly ILogger<FasterWhisperQueueClient> _logger;
        private readonly EventBusOptions _options;
        private readonly ConcurrentDictionary<string, TaskCompletionSource<FasterWhisperQueueResponse>> _pending = new();
        private readonly IConnection _connection;
        private readonly IChannel _channel;
        private readonly object _channelLock = new();
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
            _channel = CreateChannel(_connection);

            _channel.QueueDeclare(_options.CommandQueueName, durable: true, exclusive: false, autoDelete: false, arguments: null);
            _channel.QueueDeclare(_options.QueueName, durable: true, exclusive: false, autoDelete: false, arguments: null);
            _channel.BasicQos(prefetchSize: 0, prefetchCount: 1, global: false);

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

            var correlationId = Guid.NewGuid().ToString("N");
            var body = JsonSerializer.SerializeToUtf8Bytes(request, JsonOptions);
            var props = _channel.CreateBasicProperties();
            props.Persistent = true;
            props.DeliveryMode = 2;
            props.CorrelationId = correlationId;
            props.ReplyTo = _options.QueueName;

            var tcs = new TaskCompletionSource<FasterWhisperQueueResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
            if (!_pending.TryAdd(correlationId, tcs))
                throw new InvalidOperationException("Failed to register pending transcription request.");

            try
            {
                lock (_channelLock)
                {
                    _channel.BasicPublish(
                        exchange: string.Empty,
                        routingKey: _options.CommandQueueName,
                        basicProperties: props,
                        body: body);
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
                    var result = _channel.BasicGet(_options.QueueName, autoAck: false);
                    if (result is not null)
                    {
                        var body = result.Body.ToArray();
                        message = new RabbitMqMessage(body, result.BasicProperties, result.DeliveryTag);
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

                HandleResponse(message);
            }
        }

        private void HandleResponse(RabbitMqMessage delivery)
        {
            TaskCompletionSource<FasterWhisperQueueResponse>? pending = null;
            string? correlationId = null;
            try
            {
                correlationId = delivery.Properties?.CorrelationId;
                if (string.IsNullOrEmpty(correlationId) || !_pending.TryRemove(correlationId, out pending))
                {
                    _logger.LogWarning("Received unexpected FasterWhisper response with correlation id {CorrelationId}", correlationId);
                    return;
                }

                var json = Encoding.UTF8.GetString(delivery.Body);
                var response = JsonSerializer.Deserialize<FasterWhisperQueueResponse>(json, JsonOptions)
                               ?? throw new InvalidOperationException("Response payload was empty.");
                pending.TrySetResult(response);
            }
            catch (Exception ex)
            {
                pending?.TrySetException(ex);
                _logger.LogError(ex, "Failed to process FasterWhisper response message.");
            }
            finally
            {
                lock (_channelLock)
                {
                    _channel.BasicAck(delivery.DeliveryTag, multiple: false);
                }
            }
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
            }

            await DisposeSafelyAsync(_channel).ConfigureAwait(false);
            await DisposeSafelyAsync(_connection).ConfigureAwait(false);
        }

        private static ConnectionFactory CreateFactory(EventBusAccessOptions access, string? brokerName)
        {
            var factory = new RabbitMQ.Client.ConnectionFactory
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

        private static IChannel CreateChannel(IConnection connection)
        {
            return connection.CreateChannel();
        }

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

    internal sealed record RabbitMqMessage(byte[] Body, IBasicProperties? Properties, ulong DeliveryTag);

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
