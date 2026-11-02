using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using RimWorld;
using Verse;

namespace MapModeFramework
{
    public static class TaskHandler
    {
        public static readonly Queue<Func<CancellationToken, Task>> taskQueue = new Queue<Func<CancellationToken, Task>>();
        public static Task currentTask;

        /// <summary>The thread-pool task inside the current cache job. Safe to wait on from the main thread.</summary>
        public static Task currentWorkerTask;

        public static bool IsBusy => currentTask?.IsCompleted == false;
        private static CancellationTokenSource cancelTokenSource;
        private static readonly object queueLock = new object();
        private static bool isProcessing;

        public static void StartQueue(Func<CancellationToken, Task> task)
        {
            bool start;
            lock (queueLock)
            {
                taskQueue.Enqueue(task);
                start = !isProcessing;
                if (start)
                    isProcessing = true;
            }
            if (start)
                ProcessQueuedTasksAsync().ConfigureAwait(false);
        }

        private static async Task ProcessQueuedTasksAsync()
        {
            while (true)
            {
                Func<CancellationToken, Task> taskFunc = null;
                lock (queueLock)
                {
                    if (taskQueue.Count == 0)
                    {
                        isProcessing = false;
                        currentTask = null;
                        currentWorkerTask = null;
                        break;
                    }
                    taskFunc = taskQueue.Dequeue();
                }
                CancellationTokenSource localSource = CancellationTokenSource.CreateLinkedTokenSource(GetOrGenerateToken());
                CancellationToken token = localSource.Token;
                lock (linkedSourceLock) { activeLinkedSource = localSource; }

                try
                {
                    currentTask = taskFunc(token);
                    await currentTask;
                }
                catch (OperationCanceledException) { }
                catch (Exception ex)
                {
                    Core.Error($"Error while running asynchronous task: {ex.Message}");
                }
                finally
                {
                    lock (linkedSourceLock)
                    {
                        if (activeLinkedSource == localSource) activeLinkedSource = null;
                    }
                    localSource.Dispose();
                }
            }
            DisposeCancellationTokenSource();
        }

        private static CancellationToken GetOrGenerateToken()
        {
            cancelTokenSource ??= new CancellationTokenSource();
            return cancelTokenSource.Token;
        }


        private static CancellationTokenSource activeLinkedSource;
        private static readonly object linkedSourceLock = new object();

        public static void KillQueue()
        {
            lock (queueLock)
            {
                cancelTokenSource?.Cancel();
                lock (linkedSourceLock)
                {
                    if (activeLinkedSource != null) activeLinkedSource.Cancel();
                }
                taskQueue.Clear();
            }
        }
        
        /// <summary>
        /// Dispose the cancellation token source. Should only be called when no tasks are running.
        /// </summary>
        public static void DisposeCancellationTokenSource()
        {
            lock (queueLock)
            {
                cancelTokenSource?.Dispose();
                cancelTokenSource = null;
            }
        }
    }
}
