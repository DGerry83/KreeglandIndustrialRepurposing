using System;
using System.Collections.Generic;
using System.Linq;
using USITools;
using LifeSupport; // Hard reference - requires USILifeSupport.dll
using UnityEngine;

namespace KreeglandIndustrialRepurposing
{
    /// <summary>
    /// LifeSupport Recycler converter. Requires USI-LS to be installed.
    /// This exists in a separate DLL to make USI-LS optional for the base mod.
    /// </summary>
    public class KIR_LifeSupportRecyclerSwapOption : KIR_LifeSupportConverterSwapOption
    {
        [KSPField]
        public float RecyclePercent = 0f;

        public override void ApplyLifeSupportChanges(USI_Converter converter)
        {
            UseEfficiencyBonus = false;
            converter.Addons.Add(new USILS_LifeSupportRecyclerConverterAddon(converter)
            {
                CrewCapacity = this.CrewCapacity,
                RecyclePercent = this.RecyclePercent
            });
        }
    }

    /// <summary>
    /// LifeSupport Extender converter. Requires USI-LS to be installed.
    /// </summary>
    public class KIR_LifeSupportExtenderSwapOption : KIR_LifeSupportConverterSwapOption
    {
        [KSPField]
        public float TimeMultiplier = 1f;

        [KSPField]
        public bool AffectsPartOnly = false;

        [KSPField]
        public string RestrictedToClass = "";

        [KSPField]
        public bool AffectsHomeTimer = true;

        [KSPField]
        public bool AffectsHabTimer = true;

        public override void ApplyLifeSupportChanges(USI_Converter converter)
        {
            UseEfficiencyBonus = false;
            converter.Addons.Add(new USILS_LifeSupportExtenderConverterAddon(converter)
            {
                TimeMultiplier = this.TimeMultiplier,
                AffectsPartOnly = this.AffectsPartOnly,
                RestrictedToClass = this.RestrictedToClass,
                AffectsHomeTimer = this.AffectsHomeTimer,
                AffectsHabTimer = this.AffectsHabTimer
            });
        }
    }

    /// <summary>
    /// LifeSupport Habitation converter. Requires USI-LS to be installed.
    /// </summary>
    public class KIR_HabitationSwapOption : KIR_LifeSupportConverterSwapOption
    {
        [KSPField]
        public double BaseKerbalMonths = 1;

        [KSPField]
        public double BaseHabMultiplier = 0;

        public override void ApplyLifeSupportChanges(USI_Converter converter)
        {
            UseEfficiencyBonus = false;
            converter.Addons.Add(new USILS_HabitationConverterAddon(converter)
            {
                BaseKerbalMonths = this.BaseKerbalMonths,
                CrewCapacity = this.CrewCapacity,
                BaseHabMultiplier = this.BaseHabMultiplier
            });
        }
    }
}