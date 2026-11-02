using System;
using System.Collections.Generic;
using System.IO;
using Verse;
using Planets.Core;
using Planets.Utilities.Logging;

namespace Planets.Utilities.Serialization
{
    /// <summary>
    /// Shared XML file I/O and caching for preset and profile managers.
    /// Guarantees Scribe.ForceStop() on save/load exceptions and case-insensitive existence checks.
    /// </summary>
    public sealed class XmlPresetStore<T> where T : class, IExposable, new()
    {
        private readonly string folderPath;
        private readonly string rootElementName;
        private readonly Func<T, string> getName;
        private readonly Func<T, long> getSavedTicks;
        private readonly Action<T> onPostLoad;
        private List<T> cachedItems;

        public XmlPresetStore(
            string folderName,
            string rootElementName,
            Func<T, string> getName,
            Func<T, long> getSavedTicks,
            Action<T> onPostLoad = null)
            : this(Path.Combine(GenFilePaths.ConfigFolderPath, folderName), rootElementName, getName, getSavedTicks, onPostLoad, true)
        {
        }

        public XmlPresetStore(
            string fullFolderPath,
            string rootElementName,
            Func<T, string> getName,
            Func<T, long> getSavedTicks,
            Action<T> onPostLoad,
            bool isFullPath)
        {
            folderPath = isFullPath ? fullFolderPath : Path.Combine(GenFilePaths.ConfigFolderPath, fullFolderPath);
            this.rootElementName = rootElementName ?? typeof(T).Name;
            this.getName = getName ?? throw new ArgumentNullException(nameof(getName));
            this.getSavedTicks = getSavedTicks ?? (_ => 0L);
            this.onPostLoad = onPostLoad;
            EnsureFolderExists();
        }

        public string FolderPath => folderPath;

        public void EnsureFolderExists()
        {
            DirectoryInfo dir = new DirectoryInfo(folderPath);
            if (!dir.Exists)
            {
                dir.Create();
                if (Controller.DebugEnabled)
                    RPLog.D($"[Presets] Created preset folder at {folderPath}");
            }
        }

        public List<T> GetAll(bool forceRefresh = false)
        {
            if (!forceRefresh && cachedItems != null)
                return cachedItems;

            cachedItems = new List<T>();
            DirectoryInfo dir = new DirectoryInfo(folderPath);
            if (!dir.Exists)
                return cachedItems;

            foreach (FileInfo file in dir.GetFiles("*.xml"))
            {
                try
                {
                    T item = Load(Path.GetFileNameWithoutExtension(file.Name));
                    if (item != null)
                        cachedItems.Add(item);
                }
                catch (Exception e)
                {
                    RPLog.E($"[Presets] Failed to load {rootElementName} from {file.Name}: {e}");
                }
            }

            cachedItems.SortBy(item => -getSavedTicks(item));
            return cachedItems;
        }

        public bool Save(T item)
        {
            string name = item != null ? getName(item) : null;
            if (item == null || string.IsNullOrEmpty(name))
            {
                RPLog.E($"[Presets] Cannot save {rootElementName} without a name.");
                return false;
            }

            EnsureFolderExists();
            string safeName = SanitizeFileName(name);
            string filePath = Path.Combine(folderPath, $"{safeName}.xml");

            try
            {
                Scribe.saver.InitSaving(filePath, rootElementName);
                item.ExposeData();
                Scribe.saver.FinalizeSaving();
                InvalidateCache();
                if (Controller.DebugEnabled)
                    RPLog.D($"[Presets] Saved {rootElementName} '{name}'.");
                return true;
            }
            catch (Exception e)
            {
                Scribe.ForceStop();
                RPLog.E($"[Presets] Failed to save {rootElementName} '{name}': {e}");
                return false;
            }
        }

        public T Load(string name)
        {
            if (string.IsNullOrEmpty(name))
                return null;

            string safeName = SanitizeFileName(name);
            string filePath = Path.Combine(folderPath, $"{safeName}.xml");
            if (!File.Exists(filePath))
            {
                if (Controller.DebugEnabled)
                    RPLog.D($"[Presets] {rootElementName} file not found: {filePath}");
                return null;
            }

            try
            {
                Scribe.loader.InitLoading(filePath);
                T item = new T();
                item.ExposeData();
                Scribe.loader.FinalizeLoading();
                onPostLoad?.Invoke(item);
                if (Controller.DebugEnabled)
                    RPLog.D($"[Presets] Loaded {rootElementName} '{getName(item)}'.");
                return item;
            }
            catch (Exception e)
            {
                Scribe.ForceStop();
                RPLog.E($"[Presets] Failed to load {rootElementName} from '{filePath}': {e}");
                return null;
            }
        }

        public bool Delete(string name, Action onDefaultCleared = null)
        {
            if (string.IsNullOrEmpty(name))
                return false;

            string safeName = SanitizeFileName(name);
            string filePath = Path.Combine(folderPath, $"{safeName}.xml");
            if (!File.Exists(filePath))
                return false;

            try
            {
                File.Delete(filePath);
                InvalidateCache();
                onDefaultCleared?.Invoke();
                if (Controller.DebugEnabled)
                    RPLog.D($"[Presets] Deleted {rootElementName} '{name}'.");
                return true;
            }
            catch (Exception e)
            {
                RPLog.E($"[Presets] Failed to delete {rootElementName} '{name}': {e}");
                return false;
            }
        }

        public bool Exists(string name)
        {
            if (string.IsNullOrEmpty(name) || !Directory.Exists(folderPath))
                return false;

            string safeName = SanitizeFileName(name);
            foreach (string filePath in Directory.EnumerateFiles(folderPath, "*.xml"))
            {
                if (string.Equals(Path.GetFileNameWithoutExtension(filePath), safeName, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        public void InvalidateCache()
        {
            cachedItems = null;
        }

        public static string SanitizeFileName(string fileName)
        {
            if (string.IsNullOrEmpty(fileName))
                return "_";
            foreach (char c in Path.GetInvalidFileNameChars())
                fileName = fileName.Replace(c, '_');
            return fileName;
        }
    }
}
