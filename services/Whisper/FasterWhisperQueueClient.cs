using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using YandexSpeech.services.Options;
using System.Reflection;
using System.Runtime.InteropServices;

namespace YandexSpeech.services.Whisper
{
    public sealed class FasterWhisperQueueClient : IAsyncDisposable
    {
        private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

        private readonly ILogger<FasterWhisperQueueClient> _logger;
        private readonly EventBusOptions _options;
        private readonly ConcurrentDictionary<string, TaskCompletionSource<FasterWhisperQueueResponse>> _pending = new();
        private readonly object _connection;
        private readonly object _channel;
        private readonly object _channelLock = new();
        private readonly CancellationTokenSource _receiverCts = new();
        private readonly Task _receiverTask;

        public FasterWhisperQueueClient(IOptions<EventBusOptions> options, ILogger<FasterWhisperQueueClient> logger)
        {
            _logger = logger;
            _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
            _options.Validate();

            var access = _options.BusAccess ?? throw new InvalidOperationException("Event bus access options are not configured.");

            var factory = RabbitMqCompat.CreateFactory(access, _options.Broker);
            _connection = RabbitMqCompat.CreateConnection(factory);
            _channel = RabbitMqCompat.CreateChannel(_connection);

            RabbitMqCompat.QueueDeclare(_channel, _options.CommandQueueName, durable: true);
            RabbitMqCompat.QueueDeclare(_channel, _options.QueueName, durable: true);
            RabbitMqCompat.BasicQos(_channel, prefetchCount: 1);

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
            var props = RabbitMqCompat.CreateBasicProperties(_channel);
            RabbitMqCompat.SetPersistent(props, true);
            RabbitMqCompat.SetCorrelationId(props, correlationId);
            RabbitMqCompat.SetReplyTo(props, _options.QueueName);

            var tcs = new TaskCompletionSource<FasterWhisperQueueResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
            if (!_pending.TryAdd(correlationId, tcs))
                throw new InvalidOperationException("Failed to register pending transcription request.");

            try
            {
                lock (_channelLock)
                {
                    RabbitMqCompat.BasicPublish(
                        _channel,
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
                    message = await RabbitMqCompat.BasicGetAsync(_channel, _options.QueueName, cancellationToken).ConfigureAwait(false);
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
                correlationId = RabbitMqCompat.GetCorrelationId(delivery.Properties);
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
                    RabbitMqCompat.BasicAck(_channel, delivery.DeliveryTag);
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

    internal sealed record RabbitMqMessage(byte[] Body, object? Properties, ulong DeliveryTag);

    internal static class RabbitMqCompat
    {
        private const string ClientAssemblyName = "RabbitMQ.Client";

        public static object CreateFactory(EventBusAccessOptions access, string? brokerName)
        {
            if (access is null)
                throw new ArgumentNullException(nameof(access));

            var factoryType = GetRequiredType("RabbitMQ.Client.ConnectionFactory");
            var factory = Activator.CreateInstance(factoryType)
                          ?? throw new InvalidOperationException("Failed to create RabbitMQ connection factory instance.");

            SetProperty(factoryType, factory, "HostName", access.Host);
            SetProperty(factoryType, factory, "UserName", access.UserName);
            SetProperty(factoryType, factory, "Password", access.Password);
            SetProperty(factoryType, factory, "AutomaticRecoveryEnabled", true);
            SetProperty(factoryType, factory, "NetworkRecoveryInterval", TimeSpan.FromSeconds(5));

            var clientName = string.IsNullOrWhiteSpace(brokerName) ? "faster-whisper" : brokerName;
            SetProperty(factoryType, factory, "ClientProvidedName", clientName, optional: true);

            return factory;
        }

        public static object CreateConnection(object factory)
        {
            if (factory is null)
                throw new ArgumentNullException(nameof(factory));

            var factoryType = factory.GetType();

            foreach (var methodName in new[] { "CreateConnectionAsync", "CreateAutorecoveringConnectionAsync" })
            {
                foreach (var asyncMethod in factoryType.GetMethods().Where(m => m.Name == methodName))
                {
                    if (!TryInvokeWithOptionalParameters(factory, asyncMethod, out var result))
                    {
                        continue;
                    }

                    var task = result ?? throw new InvalidOperationException($"RabbitMQ factory method {methodName} returned null.");
                    return Await(task);
                }
            }

            foreach (var method in factoryType.GetMethods().Where(m => m.Name == "CreateConnection"))
            {
                if (!TryInvokeWithOptionalParameters(factory, method, out var result))
                {
                    continue;
                }

                if (result is not null)
                {
                    return result;
                }
            }

            throw new InvalidOperationException("RabbitMQ connection factory does not expose a supported CreateConnection method.");
        }

        public static object CreateChannel(object connection)
        {
            if (connection is null)
                throw new ArgumentNullException(nameof(connection));

            var connectionType = connection.GetType();

            foreach (var methodName in new[] { "CreateChannelAsync", "CreateModelAsync" })
            {
                foreach (var asyncMethod in connectionType.GetMethods().Where(m => m.Name == methodName))
                {
                    if (!TryInvokeWithOptionalParameters(connection, asyncMethod, out var result))
                    {
                        continue;
                    }

                    var task = result ?? throw new InvalidOperationException($"RabbitMQ connection method {methodName} returned null.");
                    return Await(task);
                }
            }

            foreach (var methodName in new[] { "CreateChannel", "CreateModel" })
            {
                foreach (var method in connectionType.GetMethods().Where(m => m.Name == methodName))
                {
                    if (!TryInvokeWithOptionalParameters(connection, method, out var result))
                    {
                        continue;
                    }

                    if (result is not null)
                    {
                        return result;
                    }
                }
            }

            throw new InvalidOperationException("RabbitMQ connection does not expose a supported channel creation method.");
        }

        public static void QueueDeclare(object channel, string queueName, bool durable)
        {
            if (channel is null)
                throw new ArgumentNullException(nameof(channel));
            if (string.IsNullOrEmpty(queueName))
                throw new ArgumentException("Queue name is required.", nameof(queueName));

            var channelType = channel.GetType();
            var declareArgs = new object?[] { queueName, durable, false, false, null };

            var method = GetQueueDeclareMethod(channelType, async: false);
            if (method is not null)
            {
                method.Invoke(channel, declareArgs);
                return;
            }

            var asyncMethod = GetQueueDeclareMethod(channelType, async: true);
            if (asyncMethod is not null)
            {
                var args = declareArgs.Concat(new object?[] { CancellationToken.None }).ToArray();
                var task = asyncMethod.Invoke(channel, args)
                           ?? throw new InvalidOperationException("RabbitMQ channel returned null from QueueDeclareAsync.");
                Await(task);
                return;
            }

            var argsType = Type.GetType("RabbitMQ.Client.QueueDeclareArgs, " + ClientAssemblyName);
            if (argsType is not null)
            {
                var argsInstance = Activator.CreateInstance(argsType)
                                   ?? throw new InvalidOperationException("Failed to create QueueDeclareArgs instance.");
                SetProperty(argsType, argsInstance, "Queue", queueName, optional: true);
                SetProperty(argsType, argsInstance, "Durable", durable, optional: true);
                SetProperty(argsType, argsInstance, "Exclusive", false, optional: true);
                SetProperty(argsType, argsInstance, "AutoDelete", false, optional: true);

                var methodWithArgs = channelType.GetMethod("QueueDeclare", new[] { argsType });
                if (methodWithArgs is not null)
                {
                    methodWithArgs.Invoke(channel, new[] { argsInstance });
                    return;
                }

                var asyncMethodWithArgs = channelType.GetMethod("QueueDeclareAsync", new[] { argsType, typeof(CancellationToken) });
                if (asyncMethodWithArgs is not null)
                {
                    var task = asyncMethodWithArgs.Invoke(channel, new object?[] { argsInstance, CancellationToken.None })
                               ?? throw new InvalidOperationException("RabbitMQ channel returned null from QueueDeclareAsync.");
                    Await(task);
                    return;
                }
            }

            throw new InvalidOperationException("RabbitMQ channel does not expose a supported QueueDeclare method.");
        }

        private static MethodInfo? GetQueueDeclareMethod(Type channelType, bool async)
        {
            var candidates = channelType.GetMethods().Where(m => m.Name == (async ? "QueueDeclareAsync" : "QueueDeclare"));

            foreach (var candidate in candidates)
            {
                var parameters = candidate.GetParameters();

                if (!TryMatchQueueDeclareParameters(parameters, async))
                {
                    continue;
                }

                return candidate;
            }

            return null;
        }

        private static bool TryMatchQueueDeclareParameters(IReadOnlyList<ParameterInfo> parameters, bool async)
        {
            if (async)
            {
                if (parameters.Count != 6)
                {
                    return false;
                }

                if (parameters[^1].ParameterType != typeof(CancellationToken))
                {
                    return false;
                }

                parameters = parameters.Take(parameters.Count - 1).ToArray();
            }
            else if (parameters.Count != 5)
            {
                return false;
            }

            return parameters[0].ParameterType == typeof(string)
                   && parameters[1].ParameterType == typeof(bool)
                   && parameters[2].ParameterType == typeof(bool)
                   && parameters[3].ParameterType == typeof(bool)
                   && IsDictionaryParameter(parameters[4].ParameterType);
        }

        private static bool IsDictionaryParameter(Type parameterType)
        {
            if (typeof(IDictionary).IsAssignableFrom(parameterType))
            {
                return true;
            }

            if (parameterType.IsGenericType)
            {
                var genericType = parameterType.GetGenericTypeDefinition();

                if (genericType == typeof(IDictionary<,>) || genericType == typeof(IReadOnlyDictionary<,>))
                {
                    var genericArguments = parameterType.GetGenericArguments();
                    return genericArguments[0] == typeof(string);
                }
            }

            return false;
        }

        public static void BasicQos(object channel, ushort prefetchCount)
        {
            if (channel is null)
                throw new ArgumentNullException(nameof(channel));

            var channelType = channel.GetType();

            var method = channelType.GetMethod("BasicQos", new[] { typeof(uint), typeof(ushort), typeof(bool) });
            if (method is not null)
            {
                method.Invoke(channel, new object?[] { 0u, prefetchCount, false });
                return;
            }

            var alternative = channelType.GetMethod("BasicQos", new[] { typeof(ushort), typeof(bool) });
            if (alternative is not null)
            {
                alternative.Invoke(channel, new object?[] { prefetchCount, false });
                return;
            }

            var asyncMethod = channelType.GetMethod("BasicQosAsync", new[] { typeof(uint), typeof(ushort), typeof(bool), typeof(CancellationToken) });
            if (asyncMethod is not null)
            {
                var task = asyncMethod.Invoke(channel, new object?[] { 0u, prefetchCount, false, CancellationToken.None })
                           ?? throw new InvalidOperationException("RabbitMQ channel returned null from BasicQosAsync.");
                Await(task);
            }
        }

        public static object CreateBasicProperties(object channel)
        {
            if (channel is null)
                throw new ArgumentNullException(nameof(channel));

            var channelType = channel.GetType();
            var method = channelType.GetMethod("CreateBasicProperties", Type.EmptyTypes)
                         ?? throw new InvalidOperationException("RabbitMQ channel does not expose CreateBasicProperties.");
            var props = method.Invoke(channel, Array.Empty<object?>());
            if (props is null)
                throw new InvalidOperationException("RabbitMQ channel returned null basic properties.");
            return props;
        }

        public static void SetPersistent(object basicProperties, bool persistent)
        {
            if (basicProperties is null)
                return;

            var type = basicProperties.GetType();
            SetProperty(type, basicProperties, "Persistent", persistent, optional: true);
            var deliveryMode = persistent ? (byte)2 : (byte)1;
            SetProperty(type, basicProperties, "DeliveryMode", deliveryMode, optional: true);
        }

        public static void SetCorrelationId(object basicProperties, string? correlationId)
        {
            if (basicProperties is null)
                return;

            var type = basicProperties.GetType();
            SetProperty(type, basicProperties, "CorrelationId", correlationId, optional: true);
        }

        public static void SetReplyTo(object basicProperties, string? replyTo)
        {
            if (basicProperties is null)
                return;

            var type = basicProperties.GetType();
            SetProperty(type, basicProperties, "ReplyTo", replyTo, optional: true);
        }

        public static void BasicPublish(object channel, string exchange, string routingKey, object basicProperties, byte[] body)
        {
            if (channel is null)
                throw new ArgumentNullException(nameof(channel));
            if (body is null)
                throw new ArgumentNullException(nameof(body));

            var channelType = channel.GetType();
            var bodyMemory = new ReadOnlyMemory<byte>(body);

            foreach (var method in channelType.GetMethods().Where(m => m.Name == "BasicPublish"))
            {
                var parameters = method.GetParameters();
                object?[] args;

                switch (parameters.Length)
                {
                    case 4:
                        args = new object?[]
                        {
                            exchange,
                            routingKey,
                            basicProperties,
                            ConvertBodyArgument(parameters[3].ParameterType, body, bodyMemory)
                        };
                        break;
                    case 5:
                        args = new object?[]
                        {
                            exchange,
                            routingKey,
                            false,
                            basicProperties,
                            ConvertBodyArgument(parameters[4].ParameterType, body, bodyMemory)
                        };
                        break;
                    default:
                        continue;
                }

                method.Invoke(channel, args);
                return;
            }

            foreach (var method in channelType.GetMethods().Where(m => m.Name == "BasicPublishAsync"))
            {
                var parameters = method.GetParameters();
                object?[] args;

                switch (parameters.Length)
                {
                    case 5:
                        args = new object?[]
                        {
                            exchange,
                            routingKey,
                            false,
                            basicProperties,
                            ConvertBodyArgument(parameters[4].ParameterType, body, bodyMemory)
                        };
                        break;
                    case 6:
                        args = new object?[]
                        {
                            exchange,
                            routingKey,
                            false,
                            basicProperties,
                            ConvertBodyArgument(parameters[4].ParameterType, body, bodyMemory),
                            CancellationToken.None
                        };
                        break;
                    default:
                        continue;
                }

                var task = method.Invoke(channel, args)
                           ?? throw new InvalidOperationException("RabbitMQ channel returned null from BasicPublishAsync.");
                Await(task);
                return;
            }

            throw new InvalidOperationException("RabbitMQ channel does not expose a supported BasicPublish method.");
        }

        public static void BasicAck(object channel, ulong deliveryTag)
        {
            if (channel is null)
                throw new ArgumentNullException(nameof(channel));

            var channelType = channel.GetType();

            var method = channelType.GetMethod("BasicAck", new[] { typeof(ulong), typeof(bool) });
            if (method is not null)
            {
                method.Invoke(channel, new object?[] { deliveryTag, false });
                return;
            }

            var asyncMethod = channelType.GetMethod("BasicAckAsync", new[] { typeof(ulong), typeof(bool), typeof(CancellationToken) });
            if (asyncMethod is not null)
            {
                var task = asyncMethod.Invoke(channel, new object?[] { deliveryTag, false, CancellationToken.None })
                           ?? throw new InvalidOperationException("RabbitMQ channel returned null from BasicAckAsync.");
                Await(task);
            }
        }

        public static string? GetCorrelationId(object? basicProperties)
        {
            if (basicProperties is null)
                return null;

            var type = basicProperties.GetType();
            return type.GetProperty("CorrelationId")?.GetValue(basicProperties) as string;
        }

        public static async Task<RabbitMqMessage?> BasicGetAsync(object channel, string queueName, CancellationToken cancellationToken)
        {
            if (channel is null)
                throw new ArgumentNullException(nameof(channel));

            var channelType = channel.GetType();

            var asyncWithToken = channelType.GetMethod("BasicGetAsync", new[] { typeof(string), typeof(bool), typeof(CancellationToken) });
            if (asyncWithToken is not null)
            {
                var task = asyncWithToken.Invoke(channel, new object?[] { queueName, false, cancellationToken })
                           ?? throw new InvalidOperationException("RabbitMQ channel returned null from BasicGetAsync.");
                var result = await AwaitAsync(task, cancellationToken).ConfigureAwait(false);
                return ConvertBasicGetResult(result);
            }

            var asyncMethod = channelType.GetMethod("BasicGetAsync", new[] { typeof(string), typeof(bool) });
            if (asyncMethod is not null)
            {
                var task = asyncMethod.Invoke(channel, new object?[] { queueName, false })
                           ?? throw new InvalidOperationException("RabbitMQ channel returned null from BasicGetAsync.");
                var result = await AwaitAsync(task, cancellationToken).ConfigureAwait(false);
                return ConvertBasicGetResult(result);
            }

            var method = channelType.GetMethod("BasicGet", new[] { typeof(string), typeof(bool) });
            if (method is not null)
            {
                var result = method.Invoke(channel, new object?[] { queueName, false });
                return ConvertBasicGetResult(result);
            }

            throw new InvalidOperationException("RabbitMQ channel does not expose a supported BasicGet method.");
        }

        private static RabbitMqMessage? ConvertBasicGetResult(object? result)
        {
            if (result is null)
            {
                return null;
            }

            var resultType = result.GetType();
            var body = ExtractBodyBytes(resultType, result);
            var deliveryTag = ExtractDeliveryTag(resultType, result);
            var properties = resultType.GetProperty("BasicProperties")?.GetValue(result);

            return new RabbitMqMessage(body, properties, deliveryTag);
        }

        private static byte[] ExtractBodyBytes(Type resultType, object result)
        {
            var bodyProperty = resultType.GetProperty("Body") ?? resultType.GetProperty("Memory") ?? resultType.GetProperty("BodyBytes");
            if (bodyProperty is null)
            {
                throw new InvalidOperationException("RabbitMQ BasicGet result does not contain a Body property.");
            }

            var value = bodyProperty.GetValue(result);
            return ConvertToBytes(value);
        }

        private static ulong ExtractDeliveryTag(Type resultType, object result)
        {
            var deliveryTagProperty = resultType.GetProperty("DeliveryTag");
            if (deliveryTagProperty is not null)
            {
                var value = deliveryTagProperty.GetValue(result);
                if (value is ulong ulongValue)
                {
                    return ulongValue;
                }

                if (value is long longValue)
                {
                    return unchecked((ulong)longValue);
                }
            }

            var envelopeProperty = resultType.GetProperty("Envelope");
            if (envelopeProperty is not null)
            {
                var envelope = envelopeProperty.GetValue(result);
                if (envelope is not null)
                {
                    var envelopeType = envelope.GetType();
                    var value = envelopeType.GetProperty("DeliveryTag")?.GetValue(envelope);
                    if (value is ulong ulongEnvelope)
                    {
                        return ulongEnvelope;
                    }

                    if (value is long longEnvelope)
                    {
                        return unchecked((ulong)longEnvelope);
                    }
                }
            }

            throw new InvalidOperationException("RabbitMQ BasicGet result does not expose a DeliveryTag property.");
        }

        private static byte[] ConvertToBytes(object? body)
        {
            switch (body)
            {
                case null:
                    return Array.Empty<byte>();
                case byte[] bytes:
                    return bytes;
                case ReadOnlyMemory<byte> readOnlyMemory:
                    return readOnlyMemory.ToArray();
                case Memory<byte> memory:
                    return memory.ToArray();
            }

            var type = body?.GetType();
            if (type is null)
            {
                return Array.Empty<byte>();
            }

            if (type.FullName == "System.ReadOnlyMemory`1[System.Byte]")
            {
                var toArray = type.GetMethod("ToArray", Type.EmptyTypes);
                if (toArray is not null)
                {
                    var result = toArray.Invoke(body, Array.Empty<object?>()) as byte[];
                    if (result is not null)
                    {
                        return result;
                    }
                }
            }

            var toArrayMethod = type.GetMethod("ToArray", Type.EmptyTypes);
            if (toArrayMethod is not null)
            {
                if (toArrayMethod.Invoke(body, Array.Empty<object?>()) is byte[] result)
                {
                    return result;
                }
            }

            var spanProperty = type.GetProperty("Span");
            if (spanProperty is not null)
            {
                var spanType = spanProperty.PropertyType;
                if (spanType.FullName == "System.ReadOnlySpan`1[System.Byte]" || spanType.FullName == "System.Span`1[System.Byte]")
                {
                    var spanValue = spanProperty.GetValue(body);
                    var toArray = typeof(MemoryMarshal).GetMethods(BindingFlags.Public | BindingFlags.Static)
                        .FirstOrDefault(m => m.Name == "ToArray" && m.IsGenericMethodDefinition && m.GetParameters().Length == 1)
                        ?.MakeGenericMethod(typeof(byte));
                    if (toArray is not null && toArray.Invoke(null, new[] { spanValue }) is byte[] spanResult)
                    {
                        return spanResult;
                    }
                }
            }

            throw new InvalidOperationException($"Unsupported RabbitMQ body type {type.FullName}.");
        }

        private static object ConvertBodyArgument(Type parameterType, byte[] body, ReadOnlyMemory<byte> bodyMemory)
        {
            if (parameterType == typeof(byte[]))
            {
                return body;
            }

            if (parameterType == typeof(ReadOnlyMemory<byte>) ||
                parameterType.FullName == "System.ReadOnlyMemory`1[System.Byte]")
            {
                return bodyMemory;
            }

            return body;
        }

        private static bool TryInvokeWithOptionalParameters(object instance, MethodInfo method, out object? result)
        {
            if (instance is null)
                throw new ArgumentNullException(nameof(instance));
            if (method is null)
                throw new ArgumentNullException(nameof(method));

            var parameters = method.GetParameters();
            if (parameters.Length == 0)
            {
                result = method.Invoke(instance, Array.Empty<object?>());
                return true;
            }

            var args = new object?[parameters.Length];
            for (var i = 0; i < parameters.Length; i++)
            {
                var parameter = parameters[i];

                if (parameter.HasDefaultValue)
                {
                    args[i] = parameter.DefaultValue;
                }
                else if (parameter.ParameterType == typeof(string))
                {
                    args[i] = null;
                }
                else if (parameter.ParameterType == typeof(CancellationToken))
                {
                    args[i] = CancellationToken.None;
                }
                else if (parameter.IsOptional)
                {
                    args[i] = parameter.ParameterType.IsValueType
                        ? Activator.CreateInstance(parameter.ParameterType)
                        : null;
                }
                else
                {
                    result = null;
                    return false;
                }
            }

            result = method.Invoke(instance, args);
            return true;
        }

        private static object Await(object task)
        {
            var awaiter = task.GetType().GetMethod("GetAwaiter", Type.EmptyTypes)?.Invoke(task, Array.Empty<object?>())
                           ?? throw new InvalidOperationException("Unable to obtain awaiter from asynchronous RabbitMQ method.");
            return awaiter.GetType().GetMethod("GetResult", Type.EmptyTypes)?.Invoke(awaiter, Array.Empty<object?>())
                   ?? throw new InvalidOperationException("RabbitMQ asynchronous method returned null result.");
        }

        private static async Task<object?> AwaitAsync(object task, CancellationToken cancellationToken)
        {
            switch (task)
            {
                case Task t:
                    await t.WaitAsync(cancellationToken).ConfigureAwait(false);
                    return GetTaskResult(t);
                case ValueTask vt:
                    await vt.AsTask().WaitAsync(cancellationToken).ConfigureAwait(false);
                    return null;
            }

            var type = task.GetType();
            if (type.FullName?.StartsWith("System.Threading.Tasks.ValueTask`1", StringComparison.Ordinal) == true)
            {
                var asTaskMethod = type.GetMethod("AsTask", Type.EmptyTypes);
                if (asTaskMethod is not null)
                {
                    var resultTask = asTaskMethod.Invoke(task, Array.Empty<object?>()) as Task;
                    if (resultTask is not null)
                    {
                        await resultTask.WaitAsync(cancellationToken).ConfigureAwait(false);
                        return GetTaskResult(resultTask);
                    }
                }
            }

            var awaiter = type.GetMethod("GetAwaiter", Type.EmptyTypes)?.Invoke(task, Array.Empty<object?>())
                           ?? throw new InvalidOperationException("Unable to obtain awaiter from asynchronous RabbitMQ method.");
            var awaiterType = awaiter.GetType();
            var getResult = awaiterType.GetMethod("GetResult", Type.EmptyTypes)
                            ?? throw new InvalidOperationException("RabbitMQ awaiter does not provide GetResult.");

            var isCompletedProperty = awaiterType.GetProperty("IsCompleted");
            if (isCompletedProperty is not null && isCompletedProperty.GetValue(awaiter) is bool completed && !completed)
            {
                var waitHandleProperty = awaiterType.GetProperty("WaitHandle");
                if (waitHandleProperty?.GetValue(awaiter) is WaitHandle waitHandle)
                {
                    waitHandle.WaitOne();
                }
            }

            return getResult.Invoke(awaiter, Array.Empty<object?>());
        }

        private static object? GetTaskResult(Task task)
        {
            if (task.GetType().IsGenericType)
            {
                return task.GetType().GetProperty("Result")?.GetValue(task);
            }

            return null;
        }

        private static Type GetRequiredType(string fullName)
        {
            var type = Type.GetType($"{fullName}, {ClientAssemblyName}", throwOnError: false, ignoreCase: false);
            if (type is null)
            {
                throw new InvalidOperationException($"Required RabbitMQ type {fullName} could not be resolved.");
            }

            return type;
        }

        private static void SetProperty(Type type, object instance, string propertyName, object? value, bool optional = false)
        {
            var property = type.GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
            if (property is null)
            {
                if (!optional)
                {
                    throw new InvalidOperationException($"Property {propertyName} was not found on type {type.FullName}.");
                }

                return;
            }

            if (property.CanWrite)
            {
                property.SetValue(instance, value);
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
