using System;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Telegram.Bot;
using Telegram.Bot.Requests;
using Telegram.Bot.Requests.Abstractions;

namespace YandexSpeech.services.Telegram
{
    internal static class TelegramBotClientExtensions
    {
        private static readonly string[] CandidateMethodNames =
        {
            "MakeRequestAsync",
            "MakeRequest",
            "Send",
            "SendAsync",
            "Execute",
            "ExecuteAsync",
            "Call",
            "CallAsync"
        };

        public static Task<TResponse> MakeRequestAsync<TResponse>(this ITelegramBotClient client, IRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            if (client is null)
                throw new ArgumentNullException(nameof(client));
            if (request is null)
                throw new ArgumentNullException(nameof(request));

            var clientType = client.GetType();
            var bindingFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            foreach (var methodName in CandidateMethodNames)
            {
                foreach (var method in clientType
                             .GetMethods(bindingFlags)
                             .Where(m => string.Equals(m.Name, methodName, StringComparison.OrdinalIgnoreCase)
                                         || m.Name.EndsWith('.' + methodName, StringComparison.OrdinalIgnoreCase)))
                {
                    var targetMethod = method;
                    if (method.IsGenericMethodDefinition)
                    {
                        try
                        {
                            targetMethod = method.MakeGenericMethod(typeof(TResponse));
                        }
                        catch
                        {
                            continue;
                        }
                    }

                    var parameters = targetMethod.GetParameters();
                    object?[] arguments;
                    if (parameters.Length == 1 && parameters[0].ParameterType.IsInstanceOfType(request))
                    {
                        arguments = new object?[] { request };
                    }
                    else if (parameters.Length == 2 && parameters[0].ParameterType.IsInstanceOfType(request) && parameters[1].ParameterType == typeof(CancellationToken))
                    {
                        arguments = new object?[] { request, cancellationToken };
                    }
                    else
                    {
                        continue;
                    }

                    var result = targetMethod.Invoke(client, arguments);
                    if (result is null)
                    {
                        continue;
                    }

                    if (result is Task<TResponse> typedTask)
                    {
                        return typedTask;
                    }

                    if (result is Task task)
                    {
                        return ConvertTask<TResponse>(task, cancellationToken);
                    }

                    var resultType = result.GetType();
                    if (resultType.FullName?.StartsWith("System.Threading.Tasks.ValueTask`1", StringComparison.Ordinal) == true)
                    {
                        var asTask = resultType.GetMethod("AsTask", Type.EmptyTypes);
                        if (asTask?.Invoke(result, Array.Empty<object?>()) is Task<TResponse> valueTask)
                        {
                            return valueTask;
                        }
                    }

                    if (result is ValueTask<TResponse> typedValueTask)
                    {
                        return typedValueTask.AsTask();
                    }

                    if (result is ValueTask valueTaskNoResult)
                    {
                        return valueTaskNoResult.AsTask().ContinueWith(_ => default(TResponse)!, cancellationToken, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
                    }
                }
            }

            throw new InvalidOperationException("Unable to locate a compatible request execution method on the Telegram bot client instance.");
        }

        private static Task<TResponse> ConvertTask<TResponse>(Task task, CancellationToken cancellationToken)
        {
            if (task is Task<TResponse> typed)
            {
                return typed;
            }

            if (task.GetType().IsGenericType)
            {
                return task.ContinueWith(t =>
                {
                    if (t.GetType().IsGenericType)
                    {
                        var resultProperty = t.GetType().GetProperty("Result");
                        if (resultProperty?.GetValue(t) is TResponse value)
                        {
                            return value;
                        }
                    }

                    throw new InvalidOperationException("Telegram bot client returned a task with an unexpected result type.");
                }, cancellationToken, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            }

            return task.ContinueWith(_ => default(TResponse)!, cancellationToken, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        }
    }
}
