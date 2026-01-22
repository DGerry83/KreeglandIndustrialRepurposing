using System;
using System.Reflection;
using UnityEngine;
using USITools;

namespace KreeglandIndustrialRepurposing
{
    public class KIR_Converter : USI_Converter
    {
        // Events for controller
        public event Action<KIR_Converter> OnStartConverter;
        public event Action<KIR_Converter> OnStopConverter;

        public override void StartResourceConverter()
        {
            base.StartResourceConverter();
            OnStartConverter?.Invoke(this);
        }

        public override void StopResourceConverter()
        {
            base.StopResourceConverter();
            OnStopConverter?.Invoke(this);
        }

        #region Fields and Properties
        private bool? _isKirManaged;
        private KIR_ConfigurableSwapController _kirController;

        /// <summary>
        /// Returns true if this converter is managed by a KIR controller on the same part.
        /// Cached after first check for performance.
        /// </summary>
        public bool IsKirManaged
        {
            get
            {
                if (!_isKirManaged.HasValue)
                {
                    _kirController = part.FindModuleImplementing<KIR_ConfigurableSwapController>();
                    _isKirManaged = _kirController != null;
                }
                return _isKirManaged.Value;
            }
        }

        /// <summary>
        /// Provides direct access to the KIR controller for derived classes.
        /// </summary>
        protected KIR_ConfigurableSwapController KirController
        {
            get
            {
                if (_kirController == null && IsKirManaged)
                {
                    _kirController = part.FindModuleImplementing<KIR_ConfigurableSwapController>();
                }
                return _kirController;
            }
        }

        // Protected accessor for the private _swapOption field from base class
        protected AbstractSwapOption<USI_Converter> CurrentSwapOption
        {
            get
            {
                var field = typeof(USI_Converter).GetField("_swapOption",
                    BindingFlags.NonPublic | BindingFlags.Instance);
                return field?.GetValue(this) as AbstractSwapOption<USI_Converter>;
            }
        }
        #endregion

        #region KSP Lifecycle
        public override void OnStart(StartState state)
        {
            // REMOVE manual curve loading - now handled by SwapOption
            base.OnStart(state);

            if (IsKirManaged && !IsStandaloneConverter)
            {
                SuppressNativeUI();
            }
        }

        public override void OnInactive()
        {
            base.OnInactive();
            // Clear cached state when part becomes inactive
            _isKirManaged = null;
            _kirController = null;
        }
        #endregion

        #region UI Suppression
        private void SuppressNativeUI()
        {
            try
            {
                var startEvent = Events["StartResourceConverter"];
                var stopEvent = Events["StopResourceConverter"];

                if (startEvent != null)
                {
                    startEvent.guiActive = false;
                    startEvent.guiActiveEditor = false;
                    startEvent.guiActiveUnfocused = false;
                    startEvent.externalToEVAOnly = false;
                }

                if (stopEvent != null)
                {
                    stopEvent.guiActive = false;
                    stopEvent.guiActiveEditor = false;
                    stopEvent.guiActiveUnfocused = false;
                    stopEvent.externalToEVAOnly = false;
                }

                Fields["status"].guiActive = false;
                Fields["status"].guiActiveEditor = false;

                if (Fields["statusPercent"] != null)
                {
                    Fields["statusPercent"].guiActive = false;
                    Fields["statusPercent"].guiActiveEditor = false;
                }
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogError($"[KIR] Failed to suppress native UI for converter '{ConverterName}' on part '{part.name}': {ex.Message}");
            }
        }

        public override string GetInfo()
        {
            if (IsKirManaged && !IsStandaloneConverter)
                return string.Empty;

            return base.GetInfo();
        }
        #endregion

        #region Curve Management
        public void ApplyHeatCurves(FloatCurve temperatureModifier, FloatCurve thermalEfficiency)
        {
            if (temperatureModifier != null)
            {
                TemperatureModifier = temperatureModifier;
                KIR_DebugLogger.Log($"Applied TemperatureModifier to '{ConverterName}'");
            }

            if (thermalEfficiency != null)
            {
                ThermalEfficiency = thermalEfficiency;
                KIR_DebugLogger.Log($"Applied ThermalEfficiency to '{ConverterName}'");
            }
        }
        #endregion

        #region Public API for Bay UI
        public string GetCurrentStatus()
        {
            if (string.IsNullOrEmpty(status))
            {
                return "Inactive";
            }
            return status;
        }

        public double GetCurrentLoadPercentage()
        {
            return statusPercent;
        }

        public string GetHeatStatus()
        {
            if (!GeneratesHeat)
            {
                return "";
            }

            ModuleCoreHeat coreHeat = part.FindModuleImplementing<ModuleCoreHeat>();
            if (coreHeat == null)
            {
                return "";
            }

            float goalTemp = 1000f;
            var controller = part.FindModuleImplementing<KIR_ConfigurableSwapController>();
            if (controller != null)
            {
                goalTemp = controller.CoreTempGoal;
                KIR_DebugLogger.Log($"[KIR-HEAT-UI] Got CoreTempGoal={goalTemp} from controller");
            }

            float currentTemp = coreHeat.Fields["CoreTemperature"].GetValue<float>(coreHeat);
            return $"Core: {currentTemp:F0}K / {goalTemp:F0}K";
        }
        #endregion
    }
}