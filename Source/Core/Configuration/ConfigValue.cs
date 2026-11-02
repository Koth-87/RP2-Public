using System;
using System.Collections.Generic;
using Verse;

namespace Planets.Core.Configuration
{
    /// <summary>
    /// Non-generic contract for a configuration entry so configuration containers can iterate
    /// their fields for Scribe persistence, reset, modification checks, and deep copying.
    /// </summary>
    public interface IConfigValue
    {
        string Name { get; }
        bool IsDebugOnly { get; }
        bool IsFeatureToggle { get; }
        bool IsModified { get; }
        void Look(bool forceSave = false);
        void Reset();
        void CopyValueFrom(IConfigValue other);
    }

    /// <summary>
    /// Configuration value holder that tracks current and default values, supports Scribe serialization,
    /// and optionally registers itself with a parent configuration list.
    /// </summary>
    public class ConfigValue<T> : IConfigValue
    {
        public T CurrentValue;
        public T DefaultValue;
        public string Name { get; }
        public bool IsDebugOnly { get; }
        public bool IsFeatureToggle { get; }

        public ConfigValue(
            T defaultValue,
            string name,
            List<IConfigValue> registry = null,
            bool isDebugOnly = false,
            bool isFeatureToggle = false)
        {
            CurrentValue = defaultValue;
            DefaultValue = defaultValue;
            Name = name ?? throw new ArgumentNullException(nameof(name));
            IsDebugOnly = isDebugOnly;
            IsFeatureToggle = isFeatureToggle;
            registry?.Add(this);
        }

        public bool IsModified => !EqualityComparer<T>.Default.Equals(CurrentValue, DefaultValue);

        public void Reset()
        {
            CurrentValue = DefaultValue;
        }

        public void Look(bool forceSave = false)
        {
            Scribe_Values.Look(ref CurrentValue, Name, DefaultValue, forceSave);
        }

        public void CopyValueFrom(IConfigValue other)
        {
            if (other is ConfigValue<T> typed)
                CurrentValue = typed.CurrentValue;
        }
    }
}
