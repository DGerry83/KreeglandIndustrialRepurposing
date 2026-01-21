using HarmonyLib;
using LifeSupport;
using UnityEngine;

namespace KreeglandIndustrialRepurposing
{
    /// <summary>
    /// Simple Harmony patch to disable USI's original VAB monitor
    /// </summary>
    [KSPAddon(KSPAddon.Startup.MainMenu, false)]
    public class KIR_MonitorDisabler : MonoBehaviour
    {
        void Awake()
        {
            new Harmony("KIR.Monitor.Disable").PatchAll();
            Debug.Log("[KIR-LS] Applied monitor disable patch");
        }
    }

    [HarmonyPatch(typeof(LifeSupportMonitor_Editor), "Awake")]
    static class DisableUSIMonitor_Patch
    {
        static bool Prefix()
        {
            // Prevent USI monitor from initializing
            return false;
        }
    }
}