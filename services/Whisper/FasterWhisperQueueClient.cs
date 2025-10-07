using System;
using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
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
        private readonly IModel _channel;
        private readonly object _channelLock = new();

        public FasterWhisperQueueClient(IOptions<EventBusOptions> options, ILogger<FasterWhisperQueueClient> logger)
        {
            _logger = logger;
            _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
            _options.Validate();

            var access = _options.BusAccess ?? throw new InvalidOperationException("Event bus access options are not configured.");

            var factory = new ConnectionFactory
            {
                HostName = access.Host,
                UserName = access.UserName,
                Password = access.Password,
                AutomaticRecoveryEnabled = true,
                NetworkRecoveryInterval = TimeSpan.FromSeconds(5)
            };

            var clientName = string.IsNullOrWhiteSpace(_options.Broker) ? "faster-whisper" : _options.Broker;
            TrySetClientProvidedName(factory, clientName);
            _connection = factory.CreateConnection();
            _channel = _connection.CreateModel();

            _channel.QueueDeclare(queue: _options.CommandQueueName, durable: true, exclusive: false, autoDelete: false, arguments: null);
            _channel.QueueDeclare(queue: _options.QueueName, durable: true, exclusive: false, autoDelete: false, arguments: null);
            _channel.BasicQos(0, 1, false);

            var consumer = new EventingBasicConsumer(_channel);
            consumer.Received += HandleResponse;
            _channel.BasicConsume(queue: _options.QueueName, autoAck: false, consumer: consumer);

            _logger.LogInformation("Initialized FasterWhisper RabbitMQ client. CommandQueue={CommandQueue}, ResponseQueue={ResponseQueue}",
                _options.CommandQueueName, _options.QueueName);
        }

        public async Task<FasterWhisperQueueResponse> TranscribeAsync(FasterWhisperQueueRequest request, CancellationToken cancellationToken)
        {
            if (request is null)
                throw new ArgumentNullException(nameof(request));

            var correlationId = Guid.NewGuid().ToString("N");
            var body = JsonSerializer.SerializeToUtf8Bytes(request, JsonOptions);
            var bodyMemory = new ReadOnlyMemory<byte>(body);
            var props = _channel.CreateBasicProperties();
            props.Persistent = true;
            props.CorrelationId = correlationId;
            props.ReplyTo = _options.QueueName;

            var tcs = new TaskCompletionSource<FasterWhisperQueueResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
            if (!_pending.TryAdd(correlationId, tcs))
                throw new InvalidOperationException("Failed to register pending transcription request.");

            try
            {
                lock (_channelLock)
                {
                    _channel.BasicPublish(exchange: string.Empty, routingKey: _options.CommandQueueName, mandatory: false, basicProperties: props, body: bodyMemory);
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

        private void HandleResponse(object? sender, BasicDeliverEventArgs args)
        {
            TaskCompletionSource<FasterWhisperQueueResponse>? pending = null;
            string? correlationId = null;
            try
            {
                correlationId = args.BasicProperties?.CorrelationId;
                if (string.IsNullOrEmpty(correlationId) || !_pending.TryRemove(correlationId, out pending))
                {
                    _logger.LogWarning("Received unexpected FasterWhisper response with correlation id {CorrelationId}", correlationId);
                    return;
                }

                var json = Encoding.UTF8.GetString(args.Body.Span);
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
                    _channel.BasicAck(args.DeliveryTag, false);
                }
            }
        }

        public async ValueTask DisposeAsync()
        {
            await DisposeSafelyAsync(_channel).ConfigureAwait(false);
            await DisposeSafelyAsync(_connection).ConfigureAwait(false);
        }

        private static void TrySetClientProvidedName(ConnectionFactory factory, string clientName)
        {
            var property = typeof(ConnectionFactory).GetProperty("ClientProvidedName");
            if (property is not null && property.CanWrite)
            {
                property.SetValue(factory, clientName);
            }
        }

        private static async ValueTask DisposeSafelyAsync(object? disposable)
        {
            switch (disposable)
            {
                case null:
                    return;
                case IAsyncDisposable asyncDisposable:
                    try
                    {
                        await asyncDisposable.DisposeAsync().ConfigureAwait(false);
                    }
                    catch
                    {
                        // ignore dispose exceptions
                    }
                    break;
                case IDisposable syncDisposable:
                    try
                    {
                        syncDisposable.Dispose();
                    }
                    catch
                    {
                        // ignore dispose exceptions
                    }
                    break;
            }
        }
    }

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
