using System;
using System.Collections.Generic;
using USITools;
using UnityEngine;

namespace KreeglandIndustrialRepurposing
{
    /// <summary>
    /// Abstract base for LifeSupport converters. Does NOT reference USILifeSupport.
    /// Concrete implementations go in separate DLL.
    /// </summary>
    public abstract class KIR_LifeSupportConverterSwapOption : KIR_ConverterSwapOption
    {
        [KSPField]
        public float CrewCapacity = 1f;

        [KSPField]
        public new string EfficiencyTag = "";

        // Debug field - shows whether LS addon is attached
        [KSPField(guiActive = true, guiName = "LS Active", guiActiveEditor = true)]
        public string debugLSState = "No LS";

        public abstract void ApplyLifeSupportChanges(USI_Converter converter);

        public override void ApplyConverterChanges(USI_Converter converter)
        {
            base.ApplyConverterChanges(converter);
            ApplyLifeSupportChanges(converter);

            // Update debug field
            debugLSState = converter.Addons.Count > 0 ? "LS Loaded" : "No LS";
        }
    }
}