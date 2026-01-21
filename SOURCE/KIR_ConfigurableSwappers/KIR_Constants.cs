using System;

namespace KreeglandIndustrialRepurposing
{
    /// <summary>
    /// Centralized constants for Kreegland Industrial Repurposing mod
    /// </summary>
    public static class KIR_Constants
    {
        // Loadout & Bay Management
        public const string DISABLED_LOADOUT_NAME = "_DISABLED_";
        public const int MAX_BAY_COUNT = 8; // Initial capacity, will expand dynamically

        // Debug Logging Prefixes
        public const string DEBUG_LOG_PREFIX = "[KIR]";
        public const string DEBUG_EVA_PREFIX = "[KIR-EVA]";
        public const string DEBUG_HEAT_PREFIX = "[KIR-HEAT]";
        public const string DEBUG_CURVE_PREFIX = "[KIR-CURVE]";
        public const string DEBUG_LS_PREFIX = "[KIR-LS]";
    }
}