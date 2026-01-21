using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEngine;
using USITools;
using USITools.Helpers;

namespace KreeglandIndustrialRepurposing
{
    public class KIR_ConfigurableSwapController : USI_SwapController
    {
        [KSPField(isPersistant = true)]
        public string AvailableConvertersPerBay = "";

        // ModuleCoreHeat Properties - set by B9PartSwitch per subtype
        [KSPField]
        public float CoreTempGoal = 1000f;

        [KSPField]
        public float CoreToPartRatio = 0.1f;

        [KSPField]
        public float CoreTempGoalAdjustment = 0f;

        [KSPField]
        public float CoreEnergyMultiplier = 0.1f;

        [KSPField]
        public float HeatRadiantMultiplier = 0.05f;

        [KSPField]
        public float CoolingRadiantMultiplier = 0f;

        [KSPField]
        public float HeatTransferMultiplier = 0f;

        [KSPField]
        public float CoolantTransferMultiplier = 0.01f;

        [KSPField]
        public float radiatorCoolingFactor = 1f;

        [KSPField]
        public float radiatorHeatingFactor = 0.01f;

        [KSPField]
        public float MaxCalculationWarp = 1000f;

        [KSPField]
        public float CoreShutdownTemp = 4000f;

        [KSPField]
        public float MaxCoolant = 2000f;

        private Dictionary<int, List<string>> _bayConverterMap = new Dictionary<int, List<string>>();
        private string _lastConfig = "";
        private ModuleCoreHeat _cachedCoreHeat;

        private DisabledSwapOption CreateDisabledLoadout()
        {
            return new DisabledSwapOption();
        }

        public override void OnStart(StartState state)
        {
            base.OnStart(state);
            Loadouts = part.FindModulesImplementing<AbstractSwapOption>();

            if (!Loadouts.Any(l => l is DisabledSwapOption))
                Loadouts.Add(CreateDisabledLoadout());

            // Wait for B9PartSwitch to fully apply the subtype configuration
            StartCoroutine(InitializeAfterLoad());

            // Subscribe to events for dynamic changes
            if (HighLogic.LoadedSceneIsEditor)
                GameEvents.onEditorShipModified.Add(OnEditorShipModified);
            else if (HighLogic.LoadedSceneIsFlight)
                StartCoroutine(InitializeAfterLoad());
                GameEvents.onVesselWasModified.Add(OnVesselWasModified);
        }

        private System.Collections.IEnumerator InitializeAfterLoad()
        {
            // Wait for B9PartSwitch to finish
            yield return new WaitForEndOfFrame();
            yield return new WaitForEndOfFrame();

            ParseBayConfiguration();
            _lastConfig = AvailableConvertersPerBay ?? "";
            NotifyBaysToRefresh();

            // Apply heat properties AFTER B9PartSwitch is done
            ApplyModuleCoreHeatProperties();

            // Give ModuleCoreHeat one frame to stabilize
            yield return new WaitForEndOfFrame();
        }


        public override void OnLoad(ConfigNode node)
        {
            base.OnLoad(node);

            if (ParseBayConfiguration())
            {
                KIR_DebugLogger.Log($"Controller OnLoad - Parsed config: '{AvailableConvertersPerBay}'");
            }
        }

        private void ApplyModuleCoreHeatProperties()
        {
            _cachedCoreHeat = part.FindModuleImplementing<ModuleCoreHeat>();

            if (_cachedCoreHeat == null)
            {
                Debug.LogError($"{KIR_Constants.DEBUG_HEAT_PREFIX} CRITICAL: No ModuleCoreHeat on part {part.name}");
                return;
            }

            KIR_DebugLogger.Log($"Applying properties to ModuleCoreHeat on {part.name}");
            KIR_DebugLogger.Log($"BEFORE - CoreTempGoal: {_cachedCoreHeat.CoreTempGoal:F1}, CoreTemperature: {_cachedCoreHeat.CoreTemperature:F1} (NaN={double.IsNaN(_cachedCoreHeat.CoreTemperature)})");

            // Set all thermal properties
            SetCoreHeatField("CoreTempGoal", CoreTempGoal);
            SetCoreHeatField("CoreToPartRatio", CoreToPartRatio);
            SetCoreHeatField("CoreTempGoalAdjustment", CoreTempGoalAdjustment);
            SetCoreHeatField("CoreEnergyMultiplier", CoreEnergyMultiplier);
            SetCoreHeatField("HeatRadiantMultiplier", HeatRadiantMultiplier);
            SetCoreHeatField("CoolingRadiantMultiplier", CoolingRadiantMultiplier);
            SetCoreHeatField("HeatTransferMultiplier", HeatTransferMultiplier);
            SetCoreHeatField("CoolantTransferMultiplier", CoolantTransferMultiplier);
            SetCoreHeatField("radiatorCoolingFactor", radiatorCoolingFactor);
            SetCoreHeatField("radiatorHeatingFactor", radiatorHeatingFactor);
            SetCoreHeatField("MaxCalculationWarp", MaxCalculationWarp);
            SetCoreHeatField("CoreShutdownTemp", CoreShutdownTemp);
            SetCoreHeatField("MaxCoolant", MaxCoolant);

            // FIX #1: Prime the thermal system if needed
            if (double.IsNaN(_cachedCoreHeat.CoreTemperature))
            {
                var checkTempMethod = typeof(ModuleCoreHeat).GetMethod("CheckStartingTemperature",
                    BindingFlags.NonPublic | BindingFlags.Instance);

                if (checkTempMethod != null)
                {
                    checkTempMethod.Invoke(_cachedCoreHeat, null);
                    Debug.LogError($"{KIR_Constants.DEBUG_HEAT_PREFIX}FIXED: Primed CoreTemperature to {_cachedCoreHeat.CoreTempGoal:F1}K");
                }
            }

            // FIX #2: CRITICAL - Force ModuleCoreHeat to update its converter cache
            // This is why heat generation fails! The cache was built before our converters existed.
            var updateCacheMethod = typeof(ModuleCoreHeat).GetMethod("UpdateConverterModuleCache",
                BindingFlags.Public | BindingFlags.Instance);

            if (updateCacheMethod != null)
            {
                updateCacheMethod.Invoke(_cachedCoreHeat, null);
            }
            else
            {
                Debug.LogError($"{KIR_Constants.DEBUG_HEAT_PREFIX} CRITICAL: UpdateConverterModuleCache method not found!");
            }
        }

        private void SetCoreHeatField(string fieldName, float value)
        {
            try
            {
                var field = _cachedCoreHeat.Fields[fieldName];
                if (field != null)
                    field.SetValue(value, _cachedCoreHeat);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"{KIR_Constants.DEBUG_HEAT_PREFIX} Could not set {fieldName} on ModuleCoreHeat: {ex.Message}");
            }
        }

        private bool ParseBayConfiguration()
        {
            _bayConverterMap.Clear();

            if (string.IsNullOrEmpty(AvailableConvertersPerBay))
                return false;

            string config = AvailableConvertersPerBay.Trim('"');
            bool anyParsed = false;
            int lineNumber = 0;

            foreach (string entry in config.Split(';'))
            {
                lineNumber++;
                string trimmed = entry.Trim();
                if (string.IsNullOrEmpty(trimmed)) continue;

                string[] parts = trimmed.Split(':');
                if (parts.Length != 2)
                {
                    Debug.LogError($"{KIR_Constants.DEBUG_LOG_PREFIX} Config parse error at entry {lineNumber}: '{trimmed}' - invalid format (expected 'bay:conv1,conv2')");
                    continue;
                }

                if (!int.TryParse(parts[0].Trim(), out int bayIndex))
                {
                    Debug.LogError($"{KIR_Constants.DEBUG_LOG_PREFIX} Config parse error at entry {lineNumber}: '{parts[0]}' is not a valid bay number");
                    continue;
                }

                var converters = parts[1].Split(',')
                    .Select(c => c.Trim())
                    .Where(c => !string.IsNullOrEmpty(c))
                    .ToList();

                if (converters.Count == 0)
                {
                    Debug.LogWarning($"{KIR_Constants.DEBUG_LOG_PREFIX} Config warning at entry {lineNumber}: Bay {bayIndex} has no converter names");
                }

                _bayConverterMap[bayIndex] = converters;
                anyParsed = true;
            }

            return anyParsed;
        }

        public List<AbstractSwapOption> GetLoadoutsForBay(int bayIndex)
        {
            if (_bayConverterMap.Count == 0 || !_bayConverterMap.ContainsKey(bayIndex))
                return new List<AbstractSwapOption> { CreateDisabledLoadout() };

            var allowedNames = new HashSet<string>(_bayConverterMap[bayIndex]);
            var filtered = Loadouts.Where(l => l != null && allowedNames.Contains(l.ConverterName)).ToList();

            if (filtered.Count == 0)
            {
                KIR_DebugLogger.Log($"Bay {bayIndex} has no matching loadouts, returning disabled option only.");
                return new List<AbstractSwapOption> { CreateDisabledLoadout() };
            }

            return filtered;
        }

        private int[] _lastAppliedLoadoutIndex = new int[8];
        public new void ApplyLoadout(int loadoutIndex, int converterIndex)
        {
            ApplyLoadout(loadoutIndex, converterIndex, null);
        }

        public void ApplyLoadout(int loadoutIndex, int converterIndex, List<USI_Converter> converters)
        {
            if (_lastAppliedLoadoutIndex[converterIndex] == loadoutIndex)
            {
                KIR_DebugLogger.Log(string.Format("Skipping duplicate ApplyLoadout for bay {0}, loadout {1}", converterIndex, loadoutIndex));
                return;
            }

            if (loadoutIndex < 0 || loadoutIndex >= Loadouts.Count)
            {
                Debug.LogError($"{KIR_Constants.DEBUG_LOG_PREFIX} Invalid loadoutIndex {loadoutIndex}");
                return;
            }

            var loadout = Loadouts[loadoutIndex];

            // Fetch converters ONCE if not provided
            if (converters == null)
                converters = part.FindModulesImplementing<USI_Converter>().ToList();

            if (loadout is DisabledSwapOption)
            {
                ApplyDisabledConverter(converterIndex, converters); // Pass cached list
                return;
            }

            try
            {
                base.ApplyLoadout(loadoutIndex, converterIndex);
            }
            catch (Exception ex)
            {
                Debug.LogError($"{KIR_Constants.DEBUG_LOG_PREFIX} USI ApplyLoadout failed: {ex}");
                return;
            }

            _lastAppliedLoadoutIndex[converterIndex] = loadoutIndex;

            // Use cached list
            if (converterIndex >= 0 && converterIndex < converters.Count)
            {
                var converter = converters[converterIndex];

                if (HighLogic.LoadedSceneIsEditor)
                {
                    var startEvent = converter.Events["StartResourceConverter"];
                    var stopEvent = converter.Events["StopResourceConverter"];

                    if (startEvent != null)
                        startEvent.guiName = loadout.StartActionName;
                    if (stopEvent != null)
                        stopEvent.guiName = loadout.StopActionName;
                }
            }
        }

        private void ApplyDisabledConverter(int converterIndex, List<USI_Converter> converters)
        {
            // Use passed-in list instead of fetching
            if (converterIndex < 0 || converterIndex >= converters.Count)
                return;

            var converter = converters[converterIndex];

            converter.inputList.Clear();
            converter.outputList.Clear();
            converter.reqList.Clear();
            converter.Recipe.Inputs.Clear();
            converter.Recipe.Outputs.Clear();
            converter.Recipe.Requirements.Clear();

            converter.ConverterName = KIR_Constants.DISABLED_LOADOUT_NAME;
            converter.StartActionName = "Disabled";
            converter.StopActionName = "Disabled";
            converter.status = "Disabled";

            converter.Addons.Clear();

            var startEvent = converter.Events["StartResourceConverter"];
            var stopEvent = converter.Events["StopResourceConverter"];
            if (startEvent != null) startEvent.active = false;
            if (stopEvent != null) stopEvent.active = false;
        }

        private void NotifyBaysToRefresh()
        {
            var bays = part.FindModulesImplementing<KIR_ConfigurableSwappableBay>();
            foreach (var bay in bays)
            {
                bay.RefreshFilteredLoadouts();
            }
        }

        private void OnEditorShipModified(ShipConstruct ship)
        {
            if (ship?.Parts == null || !ship.Parts.Contains(part)) return;
            if (AvailableConvertersPerBay != _lastConfig)
            {
                _lastConfig = AvailableConvertersPerBay;
                ParseBayConfiguration();
                NotifyBaysToRefresh();
            }
        }

        private void OnVesselWasModified(Vessel vessel)
        {
            if (vessel == null || part?.vessel != vessel) return;
            if (AvailableConvertersPerBay != _lastConfig)
            {
                _lastConfig = AvailableConvertersPerBay;
                ParseBayConfiguration();
                NotifyBaysToRefresh();
            }
        }

        public void OnDestroy()
        {
            GameEvents.onEditorShipModified.Remove(OnEditorShipModified);
            GameEvents.onVesselWasModified.Remove(OnVesselWasModified);
        }

        public class DisabledSwapOption : KIR_ConverterSwapOption
        {
            public DisabledSwapOption()
            {
                ConverterName = KIR_Constants.DISABLED_LOADOUT_NAME;
                inputList = new List<ResourceRatio>();
                outputList = new List<ResourceRatio>();
                reqList = new List<ResourceRatio>();
            }

            public override void ApplyConverterChanges(USI_Converter converter)
            {
                // Intentionally empty
            }
        }

        public override string GetModuleDisplayName()
        {
            return "KIR Configurable Bays";
        }


        #region Draw Parts List Info
        public override string GetInfo()
        {
            StringBuilder sb = new StringBuilder();

            ConfigNode partConfig = part?.partInfo?.partConfig;
            if (partConfig == null)
            {
                Debug.LogWarning($"{KIR_Constants.DEBUG_LOG_PREFIX} GetInfo(): No partConfig available");
                return "No converter data available";
            }

            var converterConfigs = BuildConverterConfigLookup(partConfig);
            ConfigNode b9Node = FindB9PartSwitchNode(partConfig);

            if (b9Node == null)
            {
                sb.Append(BuildNonB9InfoString(partConfig, converterConfigs));
                return sb.ToString();
            }

            sb.Append(BuildB9InfoString(b9Node, converterConfigs));
            return sb.ToString();
        }

        private Dictionary<string, ConfigNode> BuildConverterConfigLookup(ConfigNode partConfig)
        {
            var lookup = new Dictionary<string, ConfigNode>();
            foreach (ConfigNode moduleNode in partConfig.GetNodes("MODULE"))
            {
                string moduleName = moduleNode.GetValue("name");
                if (moduleName != null && moduleName.Contains("SwapOption"))
                {
                    string convName = moduleNode.GetValue("ConverterName");
                    if (!string.IsNullOrEmpty(convName))
                        lookup[convName] = moduleNode;
                }
            }
            return lookup;
        }

        private ConfigNode FindB9PartSwitchNode(ConfigNode partConfig)
        {
            foreach (ConfigNode moduleNode in partConfig.GetNodes("MODULE"))
            {
                if (moduleNode.GetValue("name") == "ModuleB9PartSwitch")
                    return moduleNode;
            }
            return null;
        }

        private void ParseSubtypeForConverters(ConfigNode subtypeNode, Dictionary<string, ConfigNode> converterConfigs, StringBuilder sb)
        {
            ConfigNode controllerData = FindControllerDataInSubtype(subtypeNode);
            if (controllerData == null) return;

            string bayConfig = controllerData.GetValue("AvailableConvertersPerBay");
            if (string.IsNullOrEmpty(bayConfig)) return;

            ParseBayConfig(bayConfig, converterConfigs, sb);
        }

        private ConfigNode FindControllerDataInSubtype(ConfigNode subtypeNode)
        {
            foreach (ConfigNode moduleNode in subtypeNode.GetNodes("MODULE"))
            {
                ConfigNode idNode = moduleNode.GetNode("IDENTIFIER");
                if (idNode?.GetValue("name") == "KIR_ConfigurableSwapController")
                {
                    return moduleNode.GetNode("DATA");
                }
            }
            return null;
        }

        private void ParseBayConfig(string config, Dictionary<string, ConfigNode> converterConfigs, StringBuilder sb)
        {
            // Parse bay configurations into groups
            var bayGroups = new Dictionary<string, List<int>>();
            string[] bayEntries = config.Split(';');

            // Group bays by their converter configuration
            for (int i = 0; i < bayEntries.Length; i++)
            {
                string entry = bayEntries[i].Trim();
                if (string.IsNullOrEmpty(entry)) continue;

                string[] parts = entry.Split(':');
                if (parts.Length != 2) continue;

                int bayIndex;
                if (!int.TryParse(parts[0].Trim(), out bayIndex)) continue;

                string converterList = parts[1].Trim();

                // Use the raw converter list string as the group key
                if (!bayGroups.ContainsKey(converterList))
                    bayGroups[converterList] = new List<int>();

                bayGroups[converterList].Add(bayIndex);
            }

            // Display each unique configuration once, with grouped bay names
            foreach (var group in bayGroups.OrderBy(g => g.Value.Min())) // Maintain bay order
            {
                string converterList = group.Key;
                List<int> bayIndices = group.Value;

                // Skip if all bays in this group are empty/_DISABLED_
                var enabledConverters = GetEnabledConverterList(converterList, converterConfigs);
                if (enabledConverters.Count == 0) continue;

                // Build comma-separated bay names (e.g., "Bay 1, Bay 2")
                string[] bayNames = bayIndices.Select(b => $"Bay {b + 1}").ToArray();
                string bayHeader = string.Join(", ", bayNames);

                sb.AppendFormat("\n<b>{0}:</b>\n", bayHeader);

                // List each converter for this configuration once
                foreach (var swapCfg in enabledConverters)
                {
                    FormatConverterInfo(swapCfg, sb);
                }
            }
        }

        private List<ConfigNode> GetEnabledConverterList(string converterList, Dictionary<string, ConfigNode> configs)
        {
            var result = new List<ConfigNode>();
            string[] names = converterList.Split(',');

            foreach (string name in names)
            {
                string trimmed = name.Trim();
                if (trimmed == KIR_Constants.DISABLED_LOADOUT_NAME) continue;
                if (configs.TryGetValue(trimmed, out ConfigNode cfg))
                    result.Add(cfg);
            }
            return result;
        }

        private void FormatConverterInfo(ConfigNode swapCfg, StringBuilder sb)
        {
            string name = swapCfg.GetValue("ConverterName");
            string crewCap = swapCfg.GetValue("CrewCapacity");
            string baseMonths = swapCfg.GetValue("BaseKerbalMonths");
            string baseHabMultiplier = swapCfg.GetValue("BaseHabMultiplier");
            string recycleRatio = swapCfg.GetValue("RecyclePercent");

            sb.AppendFormat("<color=#99FF00>{0}</color>\n", name);

            var inputNodes = swapCfg.GetNodes("INPUT_RESOURCE");
            if (inputNodes.Length > 0)
            {
                sb.AppendLine("<color=#FF5C00>Requires:</color>");
                FormatResourceList(inputNodes, "  ", sb);
                sb.AppendLine();
            }

            var outputNodes = swapCfg.GetNodes("OUTPUT_RESOURCE");
            if (outputNodes.Length > 0)
            {
                sb.AppendLine("<color=#FF5C00>Produces:</color>");
                FormatResourceList(outputNodes, "  ", sb);
                sb.AppendLine();
            }

            // Group LifeSupport info under its own header
            bool hasLifeSupport = !string.IsNullOrEmpty(baseMonths) || !string.IsNullOrEmpty(baseHabMultiplier) ||
                                 !string.IsNullOrEmpty(recycleRatio) || !string.IsNullOrEmpty(crewCap);
            if (hasLifeSupport)
            {
                sb.AppendLine("<color=#FF5C00>LifeSupport:</color>");
                if (!string.IsNullOrEmpty(baseMonths))
                    sb.AppendFormat("  Added Hab. Time:  {0} Months\n", baseMonths);
                if (!string.IsNullOrEmpty(baseHabMultiplier))
                    sb.AppendFormat("  Hab. Time Multiplier:  {0}x\n", baseHabMultiplier);
                if (!string.IsNullOrEmpty(recycleRatio) && float.TryParse(recycleRatio, out float ratio))
                {
                    sb.AppendFormat("  Recycled: {0:P0}\n", ratio);
                }
                if (!string.IsNullOrEmpty(crewCap))
                    sb.AppendFormat("  Crew Affected:  {0}\n", crewCap);
            }
        }

        // Added null check for resName
        private bool FormatResourceList(ConfigNode[] nodes, string type, StringBuilder sb)
        {
            if (nodes.Length == 0) return false;

            foreach (ConfigNode node in nodes)
            {
                string resName = node.GetValue("ResourceName");
                string ratioStr = node.GetValue("Ratio");

                // Skip if essential data is missing
                if (string.IsNullOrEmpty(resName) || string.IsNullOrEmpty(ratioStr))
                    continue;

                if (double.TryParse(ratioStr, out double ratio))
                {
                    sb.AppendFormat("{0}{1}    {2}\n", type, resName, ParseResourceRatio(ratio, resName));
                }
            }
            return true;
        }

        //Get the correct calendar day length in seconds
        double GetCalendarDayLength()
        {
            return GameSettings.KERBIN_TIME ? 21600.0 : 86400.0;
        }

        private string ParseResourceRatio(double ratio, string resourceName)
        {
            // Handle ElectricCharge differently - show per second
            if (resourceName == "ElectricCharge")
            {
                return string.Format("{0:F2}/s", ratio);
            }

            // For all other resources, show per day using correct day length if value is short enough
            double secondsPerDay = GetCalendarDayLength();
            if (ratio * secondsPerDay < 999)
            {
                return string.Format("{0:F2}/day", ratio * secondsPerDay);
            }

            return string.Format("{0:F2}/s", ratio);
        }

        #region Method Extraction - Stage 1

        private string BuildNonB9InfoString(ConfigNode partConfig, Dictionary<string, ConfigNode> converterConfigs)
        {
            StringBuilder sb = new StringBuilder();
            string baseBayConfig = AvailableConvertersPerBay;

            if (string.IsNullOrEmpty(baseBayConfig))
            {
                sb.AppendLine("<color=#FF8000>No bay configuration defined</color>");
            }
            else
            {
                sb.AppendLine("<color=#FFFF00>Available Converters:</color>");
                ParseBayConfig(baseBayConfig, converterConfigs, sb);
            }

            return sb.ToString();
        }

        private string BuildB9InfoString(ConfigNode b9Node, Dictionary<string, ConfigNode> converterConfigs)
        {
            StringBuilder sb = new StringBuilder();

            foreach (ConfigNode subtypeNode in b9Node.GetNodes("SUBTYPE"))
            {
                string title = subtypeNode.GetValue("title");
                if (string.IsNullOrEmpty(title)) continue;

                sb.Append("<color=#FFFF00>");
                sb.AppendLine(title + "</color>");

                ParseSubtypeForConverters(subtypeNode, converterConfigs, sb);
                sb.AppendLine();
            }

            return sb.ToString();
        }

        #endregion
        #endregion
    }
}