using System;
using UnityEngine;
using USITools;

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

        // Exposes curves for use by controller
        public FloatCurve ThermalEfficiencyCurve => _thermalEfficiencyCurve;
        public FloatCurve TemperatureModifierCurve => _temperatureModifierCurve;
        private bool _curvesLoaded = false;

        public override void OnStart(StartState state)
        {
            base.OnStart(state);
            // Curves now loaded on-demand via KIR_CurveManager
        }

        public void LoadCurvesFromPrototypeConfig()
        {
            // Delegate to centralized manager
            var (tempMod, thermalEff) = KIR_CurveManager.GetCurves(
                part,
                ConverterName,
                this.GetType().Name);

            _temperatureModifierCurve = tempMod;
            _thermalEfficiencyCurve = thermalEff;
            _curvesLoaded = tempMod != null || thermalEff != null;
        }

        public override void OnLoad(ConfigNode node)
        {
            base.OnLoad(node);

            if (HighLogic.LoadedSceneIsFlight)
            {
                LoadCurvesFromPrototypeConfig();
            }
        }

        public override void ApplyConverterChanges(USI_Converter converter)
        {
            if (converter == null)
            {
                UnityEngine.Debug.LogError("[KIR] ApplyConverterChanges called with null converter");
                return;
            }

            // Call parent to set up recipe
            base.ApplyConverterChanges(converter);

            if (TemperatureModifierCurve == null || ThermalEfficiencyCurve == null)
            {
                UnityEngine.Debug.LogWarning($"[KIR-CURVE] Curves missing for '{ConverterName}', reloading from prototype");
                LoadCurvesFromPrototypeConfig();
            }

            // Apply converter heat properties
            ApplyConverterHeatProperties(converter);

            if (TemperatureModifierCurve == null)
            {
                UnityEngine.Debug.LogWarning($"[KIR-CURVE] TemperatureModifierCurve failed to load for '{ConverterName}', check the part config.");
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

        // Prevent these modules from drawing their stats in the parts list info window
        public override string GetInfo()
        {
            return string.Empty;
        }
    }
}