using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using USITools;
using USITools.Helpers;

namespace KreeglandIndustrialRepurposing
{
    /// <summary>
    /// Static helper class for resource validation and management
    /// Centralizes EVA skill checking and resource deduction logic
    /// </summary>
    public static class KIR_ResourceValidator
    {
        /// <summary>
        /// Checks if the current vessel has a kerbal with Repair skill
        /// </summary>
        /// <param name="vessel">Target vessel</param>
        /// <param name="requireEva">If true, only checks active EVA kerbal</param>
        /// <returns>True if repair skill is present</returns>
        public static bool HasRepairSkill(Vessel vessel, bool requireEva = true)
        {
            if (vessel == null)
                return false;

            if (requireEva)
            {
                var evaKerbal = vessel.rootPart?.protoModuleCrew?.FirstOrDefault();
                return evaKerbal?.HasEffect("RepairSkill") == true;
            }
            else
            {
                return vessel.GetVesselCrew().Any(k => k.HasEffect("RepairSkill"));
            }
        }

        /// <summary>
        /// Validates that all required resources are available for converter swap
        /// </summary>
        /// <param name="vessel">Target vessel</param>
        /// <param name="swapCosts">List of resource costs</param>
        /// <param name="excludedPart">Part to exclude from resource search (typically the converter part itself)</param>
        /// <returns>Tuple of (bool success, string missingResourcesMessage)</returns>
        public static (bool success, string missingMessage) HasRequiredResources(
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
                if (!HasSufficientResource(vessel, resource, costMultiplier, excludedPart))
                {
                    double neededAmount = resource.Ratio * costMultiplier;
                    missingResources.Add($"\n{neededAmount:F2} {resource.ResourceName}");
                }
            }

            if (missingResources.Any())
            {
                string message = "Missing resources to change module:" + string.Join("", missingResources);
                return (false, message);
            }

            return (true, string.Empty);
        }

        /// <summary>
        /// Deducts resources from vessel storage after successful validation
        /// </summary>
        /// <param name="vessel">Target vessel</param>
        /// <param name="swapCosts">List of resource costs</param>
        /// <param name="excludedPart">Part to exclude from deduction</param>
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

        #region Private Helpers

        private static bool HasSufficientResource(
            Vessel vessel,
            ResourceRatio resInfo,
            float costMultiplier,
            Part excludedPart)
        {
            double needed = resInfo.Ratio * costMultiplier;
            var whpList = LogisticsTools.GetRegionalWarehouses(vessel, "USI_ModuleResourceWarehouse");

            // Include vessel parts for ElectricCharge
            if (resInfo.ResourceName == "ElectricCharge")
            {
                whpList.AddRange(vessel.parts);
            }

            foreach (var whp in whpList)
            {
                if (whp == excludedPart)
                    continue;

                // Skip non-warehouse parts for non-EC resources
                if (resInfo.ResourceName != "ElectricCharge")
                {
                    var wh = whp.FindModuleImplementing<USITools.USI_ModuleResourceWarehouse>();
                    if (wh != null && !wh.localTransferEnabled)
                        continue;
                }

                if (whp.Resources.Contains(resInfo.ResourceName))
                {
                    var res = whp.Resources[resInfo.ResourceName];
                    if (res.amount >= needed)
                        return true;

                    needed -= res.amount;
                }
            }

            return needed < ResourceUtilities.FLOAT_TOLERANCE;
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
                if (whp == excludedPart)
                    continue;

                var wh = whp.FindModuleImplementing<USITools.USI_ModuleResourceWarehouse>();
                if (wh != null && !wh.localTransferEnabled)
                    continue;

                if (whp.Resources.Contains(resourceName))
                {
                    var res = whp.Resources[resourceName];
                    if (res.amount >= needed)
                    {
                        res.amount -= needed;
                        return;
                    }
                    else
                    {
                        needed -= res.amount;
                        res.amount = 0;
                    }
                }
            }
        }

        #endregion
    }
}