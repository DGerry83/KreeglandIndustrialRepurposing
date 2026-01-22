using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using USITools;
using USITools.Helpers;

namespace KreeglandIndustrialRepurposing
{
    public static class KIR_ResourceValidator
    {
        /// <summary>
        /// Validates repair skill requirement and returns appropriate message
        /// </summary>
        public static (bool success, string message) ValidateRepairSkill(Vessel vessel, bool requireEva)
        {
            if (vessel == null)
                return (false, "No vessel available");

            bool hasSkill = requireEva
                ? vessel.rootPart?.protoModuleCrew?.FirstOrDefault()?.HasEffect("RepairSkill") == true
                : vessel.GetVesselCrew().Any(k => k.HasEffect("RepairSkill"));

            if (!hasSkill)
            {
                return (false, GetSkillFailMessage(requireEva));
            }

            return (true, string.Empty);
        }

        private static string GetSkillFailMessage(bool requireEva)
        {
            return requireEva
                ? "Only Kerbals with repair skills (e.g. engineers, mechanics) can reconfigure modules!"
                : "A Kerbal with repair skills (e.g. engineer, mechanic) must be on board to reconfigure modules!";
        }

        /// <summary>
        /// Validates resource requirements and returns detailed missing resources message
        /// </summary>
        public static (bool success, string message) ValidateResources(
            Vessel vessel,
            List<ResourceRatio> swapCosts,
            Part excludedPart)
        {
            if (vessel == null || swapCosts == null)
                return (true, string.Empty);

            float costMultiplier = USI_ConverterOptions.ConverterSwapCostMultiplierValue;
            if (costMultiplier <= ResourceUtilities.FLOAT_TOLERANCE)
                return (true, string.Empty);

            var missingResources = new List<string>();
            foreach (var resource in swapCosts)
            {
                double needed = resource.Ratio * costMultiplier;
                if (!HasSufficientResource(vessel, resource.ResourceName, needed, excludedPart))
                {
                    missingResources.Add($"\n{needed:F2} {resource.ResourceName}");
                }
            }

            if (missingResources.Any())
            {
                return (false, "Missing resources to change module:" + string.Join("", missingResources));
            }

            return (true, string.Empty);
        }

        /// <summary>
        /// Deducts resources after successful validation
        /// </summary>
        public static void DeductResources(
            Vessel vessel,
            List<ResourceRatio> swapCosts,
            Part excludedPart)
        {
            if (vessel == null || swapCosts == null)
                return;

            float costMultiplier = USI_ConverterOptions.ConverterSwapCostMultiplierValue;
            if (costMultiplier <= ResourceUtilities.FLOAT_TOLERANCE)
                return;

            foreach (var resource in swapCosts)
            {
                double needed = resource.Ratio * costMultiplier;
                DeductResourceFromVessel(vessel, resource.ResourceName, needed, excludedPart);
            }
        }

        private static bool HasSufficientResource(
            Vessel vessel,
            string resourceName,
            double needed,
            Part excludedPart)
        {
            var whpList = LogisticsTools.GetRegionalWarehouses(vessel, "USI_ModuleResourceWarehouse");
            if (resourceName == "ElectricCharge")
                whpList.AddRange(vessel.parts);

            return whpList
                .Where(whp => whp != excludedPart)
                .Where(whp => resourceName == "ElectricCharge" ||
                    whp.FindModuleImplementing<USITools.USI_ModuleResourceWarehouse>()?.localTransferEnabled != false)
                .Where(whp => whp.Resources.Contains(resourceName))
                .Sum(whp => whp.Resources[resourceName].amount) >= needed;
        }

        private static void DeductResourceFromVessel(
            Vessel vessel,
            string resourceName,
            double needed,
            Part excludedPart)
        {
            var whpList = LogisticsTools.GetRegionalWarehouses(vessel, "USI_ModuleResourceWarehouse");

            foreach (var whp in whpList)
            {
                if (whp == excludedPart) continue;

                var wh = whp.FindModuleImplementing<USITools.USI_ModuleResourceWarehouse>();
                if (wh != null && !wh.localTransferEnabled) continue;

                if (whp.Resources.Contains(resourceName))
                {
                    var res = whp.Resources[resourceName];
                    double taken = Mathf.Min((float)res.amount, (float)needed);
                    res.amount -= taken;
                    needed -= taken;
                    if (needed <= ResourceUtilities.FLOAT_TOLERANCE) return;
                }
            }
        }
    }
}