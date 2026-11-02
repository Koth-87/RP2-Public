using System;
using System.Threading;
using System.Threading.Tasks;

namespace MapModeFramework
{
    public static class WorldRegenHandler
    {
        public static Task currentRegenTask;

        /// <summary>The thread-pool mesh task inside the current regeneration. Safe to wait on from the main thread.</summary>
        public static Task currentWorkerTask;
        private static CancellationTokenSource cancelTokenSource;

        public static bool IsBusy => currentRegenTask?.IsCompleted == false;
        public static MapMode regeneratingMapMode;
        public static DateTime startTime;
        public static int tilesToPrepare;
        public static int tilesPrepared;

        public static void RequestAsyncWorldRegeneration(WorldLayer_MapMode worldLayer)
        {
            if (IsBusy)
            {
                Interrupt();
            }
            Reset(true);
            cancelTokenSource = new CancellationTokenSource();
            currentRegenTask = worldLayer.BuildSubMeshes(cancelTokenSource.Token);
            regeneratingMapMode = worldLayer.CurrentMapMode;
            startTime = DateTime.Now;
        }

        public static void Interrupt()
        {
            cancelTokenSource?.Cancel();
            DisposeCancellationTokenSource();
        }

        private static void DisposeCancellationTokenSource()
        {
            cancelTokenSource?.Dispose();
            cancelTokenSource = null;
        }

        public static async Task RequestMapModeSwitch(MapMode mapMode)
        {
            if (IsBusy)
            {
                try
                {
                    Interrupt();
                    await currentRegenTask;
                }
                catch (OperationCanceledException)
                {
                    Core.Message("Async regeneration interrupted");
                }
                catch (Exception ex)
                {
                    Core.Error($"Error while requesting map mode switch: {ex.Message}");
                }
            }
            MapModeComponent.Instance.SwitchMapMode(mapMode);
        }

        public static void Reset(bool fullReset)
        {
            tilesToPrepare = 0;
            tilesPrepared = 0;
            if (fullReset)
            {
                currentRegenTask = null;
                currentWorkerTask = null;
                DisposeCancellationTokenSource();
                regeneratingMapMode = null;
                startTime = default;
            }
        }

        public static void Notify_Finished()
        {
            Reset(true);
        }
    }
}
