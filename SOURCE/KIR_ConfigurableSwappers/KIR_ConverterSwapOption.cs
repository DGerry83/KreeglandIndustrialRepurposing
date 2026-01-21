using System;
using System.Text;
using USITools;
using UnityEngine;

namespace KreeglandIndustrialRepurposing
{
    public class KIR_ConverterSwapOption : USI_ConverterSwapOption
    {
        // ===== CONVERTER HEAT PROPERTIES =====
        [KSPField]
        public bool GeneratesHeat = false;

        [KSPField]
        public float DefaultShutoffTemp = 0.8f;

        [KSPField]
        public bool AutoShutdown = true;

        // FloatCurves - loaded from prototype config
        private FloatCurve _thermalEfficiencyCurve;
        private FloatCurve _temperatureModifierCurve;

        //Exposes curves for use by controller
        public FloatCurve ThermalEfficiencyCurve => _thermalEfficiencyCurve;
        public FloatCurve TemperatureModifierCurve => _temperatureModifierCurve;
        private bool _curvesLoaded = false;

        public override void OnStart(StartState state)
        {
            base.OnStart(state);

            // Load curves from prototype config to survive serialization
            if (!_curvesLoaded)
            {
                LoadCurvesFromPrototypeConfig();
                _curvesLoaded = true;
            }
        }

        public void LoadCurvesFromPrototypeConfig()
        {
            if (part == null || part.partInfo == null || part.partInfo.partConfig == null)
            {
                Debug.LogWarning(string.Format("[KIR] Cannot load curves for {0}: part config unavailable", ConverterName));
                return;
            }

            ConfigNode moduleNode = FindPrototypeModuleNode();
            if (moduleNode == null)
            {
                Debug.LogWarning(string.Format("[KIR] Could not find prototype node for {0} ({1})",
                    this.GetType().Name, ConverterName));
                return;
            }

            LoadCurvesFromNode(moduleNode);
        }

        public override void OnLoad(ConfigNode node)
        {
            base.OnLoad(node);

            // CRITICAL: Re-load curves in flight (they're not persisted)
            if (HighLogic.LoadedSceneIsFlight)
            {
                LoadCurvesFromPrototypeConfig();
            }
        }

        private ConfigNode FindPrototypeModuleNode()
        {
            if (part?.partInfo?.partConfig == null)
            {
                Debug.LogWarning($"[KIR-CURVE] {ConverterName}: partConfig unavailable");
                return null;
            }

            ConfigNode[] moduleNodes = part.partInfo.partConfig.GetNodes("MODULE");

            // Only use reliable name-based lookup
            foreach (ConfigNode node in moduleNodes)
            {
                string nodeConverterName = node.GetValue("ConverterName");
                string nodeModuleName = node.GetValue("name");

                if (nodeConverterName == this.ConverterName &&
                    nodeModuleName == this.GetType().Name)
                {
                    return node;
                }
            }

            Debug.LogError($"[KIR-CURVE] {ConverterName}: No matching ConfigNode found!");
            return null;
        }


        private void LoadCurvesFromNode(ConfigNode node)
        {
            // TemperatureModifier (critical for heat production)
            if (node.HasNode("TemperatureModifier"))
            {
                _temperatureModifierCurve = new FloatCurve();
                _temperatureModifierCurve.Load(node.GetNode("TemperatureModifier"));
                KIR_DebugLogger.Log(string.Format("[KIR] Loaded TemperatureModifier curve for {0}", ConverterName));
            }
            else
            {
                _temperatureModifierCurve = null;
            }

            // ThermalEfficiency
            if (node.HasNode("ThermalEfficiency"))
            {
                _thermalEfficiencyCurve = new FloatCurve();
                _thermalEfficiencyCurve.Load(node.GetNode("ThermalEfficiency"));
                KIR_DebugLogger.Log(string.Format("[KIR] Loaded ThermalEfficiency curve for {0}", ConverterName));
            }
            else
            {
                _thermalEfficiencyCurve = null;
            }
        }

        public override void ApplyConverterChanges(USI_Converter converter)
        {
            if (converter == null)
            {
                Debug.LogError("[KIR] ApplyConverterChanges called with null converter");
                return;
            }

            // Force initialization of converter's internal lists
            try
            {
                var recipe = converter.Recipe;
                var inputList = converter.inputList;
                var outputList = converter.outputList;
                var reqList = converter.reqList;

                if (recipe == null || inputList == null || outputList == null || reqList == null)
                {
                    Debug.LogWarning($"[KIR-LS] Converter {converter.ConverterName} not fully initialized, attempting recovery");
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[KIR-LS] Error accessing converter properties for {converter.ConverterName}: {ex.Message}");
            }

            // Call parent to set up recipe
            base.ApplyConverterChanges(converter);

            // CRITICAL: Re-load curves from config before applying (they may be null in flight)
            if (TemperatureModifierCurve == null || ThermalEfficiencyCurve == null)
            {
                Debug.LogWarning($"[KIR-CURVE] Curves missing for '{ConverterName}', reloading from prototype");
                LoadCurvesFromPrototypeConfig();
            }

            // Apply converter heat properties
            ApplyConverterHeatProperties(converter);

            if (TemperatureModifierCurve == null)
            {
                Debug.LogError($"[KIR-CURVE] CRITICAL: TemperatureModifierCurve failed to load for '{ConverterName}'!");
            }
        }


        private void ApplyConverterHeatProperties(USI_Converter converter)
        {
            converter.GeneratesHeat = GeneratesHeat;
            converter.DefaultShutoffTemp = DefaultShutoffTemp;
            converter.AutoShutdown = AutoShutdown;

            // Apply curves
            var kirConverter = converter as KIR_Converter;
            if (kirConverter != null)
            {
                kirConverter.ApplyHeatCurves(_temperatureModifierCurve, _thermalEfficiencyCurve);
            }
        }

        //Prevent these modules from drawing their stats in the parts list info window - the controller will handle it for the whole part.
        public override string GetInfo()
        {
            return string.Empty;
        }
    }
}