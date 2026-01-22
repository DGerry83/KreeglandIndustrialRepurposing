using System.Collections.Generic;
using System.Diagnostics; // Required for [Conditional("DEBUG")]
using USITools;
using UnityEngine;

namespace KreeglandIndustrialRepurposing
{
    /// <summary>
    /// Singleton manager for loading and caching FloatCurves from prototype configs.
    /// Eliminates duplicate disk I/O and parsing across all converter instances.
    /// </summary>
    public static class KIR_CurveManager
    {
        private static readonly Dictionary<string, CachedCurves> _curveCache =
            new Dictionary<string, CachedCurves>();

        private static readonly object _lock = new object();

        private class CachedCurves
        {
            public FloatCurve TemperatureModifier { get; set; }
            public FloatCurve ThermalEfficiency { get; set; }
            public bool SuccessfullyLoaded { get; set; }
        }

        /// <summary>
        /// Gets cached curves for a converter. Loads from config if not cached.
        /// Thread-safe for KSP's single-threaded part loading.
        /// </summary>
        public static (FloatCurve tempMod, FloatCurve thermalEff) GetCurves(
            Part part,
            string converterName,
            string moduleClassName)
        {
            string cacheKey = $"{part.name}_{converterName}_{moduleClassName}";

            lock (_lock)
            {
                if (_curveCache.TryGetValue(cacheKey, out var cached))
                {
                    KIR_DebugLogger.Log($"[KIR-CURVE] Cache hit for '{converterName}' on '{part.name}'");
                    return (cached.TemperatureModifier, cached.ThermalEfficiency);
                }

                var curves = LoadAndCacheCurves(part, converterName, moduleClassName, cacheKey);
                return (curves.TemperatureModifier, curves.ThermalEfficiency);
            }
        }

        private static CachedCurves LoadAndCacheCurves(
            Part part,
            string converterName,
            string moduleClassName,
            string cacheKey)
        {
            KIR_DebugLogger.Log($"[KIR-CURVE] Loading curves for '{converterName}' on '{part.name}'");

            var cached = new CachedCurves();

            if (part?.partInfo?.partConfig == null)
            {
                UnityEngine.Debug.LogWarning($"[KIR-CURVE] Cannot load: part config unavailable for '{converterName}'");
                cached.SuccessfullyLoaded = false;
                _curveCache[cacheKey] = cached;
                return cached;
            }

            ConfigNode moduleNode = FindPrototypeModuleNode(
                part.partInfo.partConfig,
                converterName,
                moduleClassName);

            if (moduleNode == null)
            {
                UnityEngine.Debug.LogWarning($"[KIR-CURVE] No prototype node found: {converterName} ({moduleClassName})");
                cached.SuccessfullyLoaded = false;
                _curveCache[cacheKey] = cached;
                return cached;
            }

            // Load TemperatureModifier (critical for heat generation)
            if (moduleNode.HasNode("TemperatureModifier"))
            {
                cached.TemperatureModifier = new FloatCurve();
                cached.TemperatureModifier.Load(moduleNode.GetNode("TemperatureModifier"));
                KIR_DebugLogger.Log($"[KIR-CURVE] Loaded TemperatureModifier for '{converterName}'");
            }

            // Load ThermalEfficiency
            if (moduleNode.HasNode("ThermalEfficiency"))
            {
                cached.ThermalEfficiency = new FloatCurve();
                cached.ThermalEfficiency.Load(moduleNode.GetNode("ThermalEfficiency"));
                KIR_DebugLogger.Log($"[KIR-CURVE] Loaded ThermalEfficiency for '{converterName}'");
            }

            cached.SuccessfullyLoaded = true;
            _curveCache[cacheKey] = cached;

            return cached;
        }

        private static ConfigNode FindPrototypeModuleNode(
            ConfigNode partConfig,
            string converterName,
            string moduleClassName)
        {
            foreach (ConfigNode node in partConfig.GetNodes("MODULE"))
            {
                string nodeConverterName = node.GetValue("ConverterName");
                string nodeModuleName = node.GetValue("name");

                // Use reliable name-based matching
                if (nodeConverterName == converterName && nodeModuleName == moduleClassName)
                {
                    return node;
                }
            }
            return null;
        }

        /// <summary>
        /// Clears cache (useful for debugging or config reloads)
        /// </summary>
        [Conditional("DEBUG")]
        public static void ClearCache()
        {
            lock (_lock)
            {
                KIR_DebugLogger.Log($"[KIR-CURVE] Clearing {_curveCache.Count} cached entries");
                _curveCache.Clear();
            }
        }

        /// <summary>
        /// Gets cache statistics for debugging
        /// </summary>
        [Conditional("DEBUG")]
        public static void LogCacheStats()
        {
            lock (_lock)
            {
                KIR_DebugLogger.Log($"[KIR-CURVE] Cache contains {_curveCache.Count} entries");
                foreach (var key in _curveCache.Keys)
                {
                    KIR_DebugLogger.Log($"[KIR-CURVE]   - {key}");
                }
            }
        }
    }
}