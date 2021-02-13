using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CoreNgine.Models;
using CoreNgine.Shared;
using HellBrick.Collections;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace CoreNgine
{

    public static class CoreExtensions
    {
        public static async Task<TResult> TimeoutAfter<TResult>(this Task<TResult> task, TimeSpan timeout) {

            using (var timeoutCancellationTokenSource = new CancellationTokenSource()) {

                var completedTask = await Task.WhenAny(task, Task.Delay(timeout, timeoutCancellationTokenSource.Token));
                if (completedTask == task) {
                    timeoutCancellationTokenSource.Cancel();
                    return await task;  // Very important in order to propagate exceptions
                } else {
                    throw new TimeoutException("The operation has timed out.");
                }
            }
        }

        public static Task StartLongRunningTask(this TaskFactory factory, Action taskFunc, CancellationToken token)
        {
            return Task.Factory
                .StartNew(taskFunc, token,
                    TaskCreationOptions.LongRunning, 
                    TaskScheduler.Default);
        }

        public static void Apply<T>(this IEnumerable<T> enumerable, Action<T> action)
        {
            foreach (var item in enumerable)
            {
                action(item);
            }
        }

        public static void Clear<T>(this ConcurrentQueue<T> queue)
        {
            while (queue.TryDequeue(out _))
            {
                // just go next
            }
        }

        public static async Task Clear<T>(this AsyncQueue<T> queue)
        {
            while (queue.Count > 0)
            {
                if (queue.Count > 0)
                    await queue.TakeAsync();
            }
        }

        public static void UseStocksManager(this IApplicationBuilder app)
        {
            app.ApplicationServices.GetRequiredService<IMainModel>().Start();
        }

        public static void AddStocksManager(this IServiceCollection services)
        {
            services.AddSingleton<StocksManager>();
        }
    }
}
