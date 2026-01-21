using System;
using System.Reflection;
using UnityEngine;
using USITools;

namespace KreeglandIndustrialRepurposing
{
    /// <summary>
    /// Drop-in replacement for USI_Converter that eliminates double-UI by suppressing 
    /// native controls when managed by KIR_ConfigurableSwapController while exposing
    /// converter state for bay UI rendering. Centralizes heat curve management and
    /// provides debug capabilities.
    /// </summary>
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

        // Cached management state to avoid repeated part module lookups
        private bool? _isKirManaged;

        // Cached reference to KIR controller for child classes
        private KIR_ConfigurableSwapController _kirController;

        // UI state caching for bay consumption
        private string _lastStatus = "";
        private double _lastStatusPercent = 0d;
        private string _lastHeatStatus = "";

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
        /// Reduces reflection usage in the bay system.
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
            // Pre-load curves from the active swap option if available
            if (HighLogic.LoadedSceneIsFlight && GeneratesHeat)
            {
                var swapOption = CurrentSwapOption as KIR_ConverterSwapOption;
                if (swapOption != null)
                {
                    // Force the swap option to re-load its curves
                    if (swapOption.TemperatureModifierCurve == null)
                    {
                        swapOption.LoadCurvesFromPrototypeConfig();
                    }

                    ApplyHeatCurves(swapOption.TemperatureModifierCurve, swapOption.ThermalEfficiencyCurve);
                }
            }

            base.OnStart(state);

            if (GeneratesHeat)
            {
                var coreHeat = part.FindModuleImplementing<ModuleCoreHeat>();
            }

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

        /// <summary>
        /// Hides the converter's native Start/Stop buttons and status fields
        /// so only the KIR bay UI is visible. Called during OnStart.
        /// </summary>
        private void SuppressNativeUI()
        {
            try
            {
                // Hide Start/Stop events
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

                // Hide status and efficiency fields
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
                Debug.LogError(string.Format("[KIR] Failed to suppress native UI for converter '{0}' on part '{1}': {2}",
                    ConverterName, part.name, ex.Message));
            }
        }

        /// <summary>
        /// Prevents info display in part catalog for managed converters
        /// </summary>
        public override string GetInfo()
        {
            if (IsKirManaged && !IsStandaloneConverter)
                return string.Empty;

            return base.GetInfo();
        }

        #endregion

        #region Curve Management
        /// <summary>
        /// Apply curves provided by the SwapOption
        /// </summary>
        public void ApplyHeatCurves(FloatCurve temperatureModifier, FloatCurve thermalEfficiency)
        {
            if (temperatureModifier != null)
            {
                TemperatureModifier = temperatureModifier;
                KIR_DebugLogger.Log(string.Format("Applied TemperatureModifier to '{0}'", ConverterName));
            }

            if (thermalEfficiency != null)
            {
                ThermalEfficiency = thermalEfficiency;
                KIR_DebugLogger.Log(string.Format("Applied ThermalEfficiency to '{0}'", ConverterName));
            }
        }

        #endregion

        #region Public API for Bay UI

        /// <summary>
        /// Gets the current operational status for display in the bay UI
        /// Mirrors the logic from USI_Converter.PostProcess
        /// </summary>
        public string GetCurrentStatus()
        {
            if (string.IsNullOrEmpty(status))
            {
                _lastStatus = "Inactive";
                return _lastStatus;
            }

            // Cache the status to avoid string operations every frame
            if (status != _lastStatus)
            {
                _lastStatus = status;
            }

            return _lastStatus;
        }

        /// <summary>
        /// Gets the current load/efficiency percentage for display
        /// </summary>
        public double GetCurrentLoadPercentage()
        {
            _lastStatusPercent = statusPercent;
            return _lastStatusPercent;
        }

        /// <summary>
        /// Gets a formatted heat status string for UI display
        /// </summary>
        public string GetHeatStatus()
        {
            if (!GeneratesHeat)
            {
                _lastHeatStatus = "";
                return _lastHeatStatus;
            }

            ModuleCoreHeat coreHeat = part.FindModuleImplementing<ModuleCoreHeat>();
            if (coreHeat == null)
            {
                _lastHeatStatus = "";
                return _lastHeatStatus;
            }

            // Get CoreTempGoal from the current swap option (not from converter)
            // Default fallback value matches USI's standard reactor core temp
            float goalTemp = 1000f;

            var controller = part.FindModuleImplementing<KIR_ConfigurableSwapController>();
            if (controller != null)
            {
                goalTemp = controller.CoreTempGoal;
                KIR_DebugLogger.Log($"[KIR-HEAT-UI] Got CoreTempGoal={goalTemp} from controller");
            }
            else
            {
                Debug.LogWarning("[KIR-HEAT-UI] No controller found for CoreTempGoal");
            }

            // Get current temperature from ModuleCoreHeat
            float currentTemp = 0f;
            var currentTempField = coreHeat.Fields["CoreTemperature"];
            if (currentTempField != null)
            {
                currentTemp = currentTempField.GetValue<float>(coreHeat);
            }

            _lastHeatStatus = string.Format("Core: {0:F0}K / {1:F0}K", currentTemp, goalTemp);
            return _lastHeatStatus;
        }

        #endregion
    }
}