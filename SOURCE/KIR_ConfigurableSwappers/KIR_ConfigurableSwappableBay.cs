using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Text;
using TMPro;
using UnityEngine;
using USITools;
using static KreeglandIndustrialRepurposing.KIR_ConfigurableSwapController;

namespace KreeglandIndustrialRepurposing
{
    public class KIR_ConfigurableSwappableBay : USI_SwappableBay
    {
        private List<AbstractSwapOption> _filteredLoadouts;
        private float _lastEff = -1f; // For converter efficiency calcs
        private bool _isDisabled = false;
        private bool _bayInitialized = false;
        private bool _cacheInitialized = false;
        private StringBuilder _convStatusSB = new StringBuilder(128);
        private string _lastStatus = string.Empty;
        private ModuleCoreHeat _cachedCoreHeat;

        /// <summary>
        /// Cached list of KIR_Converter modules to avoid repeated FindModulesImplementing calls
        /// </summary>
        private List<KIR_Converter> _cachedConverters;

        /// <summary>
        /// Tracks part module count to detect when cache needs refresh
        /// </summary>
        private int _cachedConverterCount = -1;

        /// <summary>
        /// Static cache for FieldInfo objects to avoid repeated reflection lookups.
        /// Key: "TypeFullName.FieldName" 
        /// </summary>
        private static readonly Dictionary<string, FieldInfo> _fieldInfoCache =
            new Dictionary<string, FieldInfo>();
        /// <summary>
        /// Gets cached FieldInfo or performs reflection lookup if not cached.
        /// </summary>
        private static FieldInfo GetCachedFieldInfo(Type type, string fieldName, BindingFlags flags)
        {
            string cacheKey = $"{type.FullName}.{fieldName}";

            if (!_fieldInfoCache.TryGetValue(cacheKey, out var fieldInfo))
            {
                fieldInfo = type.GetField(fieldName, flags);
                _fieldInfoCache[cacheKey] = fieldInfo; // Cache even if null

                KIR_DebugLogger.Log($"[KIR-REFLECT] Cached FieldInfo for {cacheKey}");
            }

            return fieldInfo;
        }

        [KSPField(guiActive = false, guiActiveEditor = false, guiName = "Status")]
        public string converterStatus = "Inactive";

        [KSPField(guiActive = false, guiActiveEditor = false, guiName = "Load")]
        public string converterLoad = "0%";

        [KSPEvent(guiActive = true, guiActiveEditor = false, guiName = "Start Converter", active = true)]
        public void StartConverter()
        {
            var converters = GetCachedConverters();
            if (moduleIndex < converters.Count && !_isDisabled)
            {
                var converter = converters[moduleIndex];
                converter.StartResourceConverter();
                UpdateConverterUI();
                KIR_DebugLogger.Log(string.Format("Bay {0}: Started converter '{1}'", bayName, converter.ConverterName));
            }
        }

        [KSPEvent(guiActive = true, guiActiveEditor = false, guiName = "Stop Converter", active = false)]
        public void StopConverter()
        {
            var converters = GetCachedConverters();
            if (moduleIndex < converters.Count && !_isDisabled)
            {
                var converter = converters[moduleIndex];
                converter.StopResourceConverter();
                UpdateConverterUI();
                KIR_DebugLogger.Log(string.Format("Bay {0}: Stopped converter '{1}'", bayName, converter.ConverterName));
            }
        }

        //UI SELECTOR FIELDS - EDITOR ONLY
        [KSPField(isPersistant = false, guiActive = false, guiActiveEditor = false, guiName = "Selection"),
         UI_ChooseOption(suppressEditorShipModified = true)]
        public string selectedConverterUI = string.Empty;

        [KSPEvent(guiActive = false, guiActiveEditor = false, guiName = "B1: Next Loadout", active = false, guiActiveUnfocused = true, externalToEVAOnly = true, unfocusedRange = 10f)]
        public void KIR_NextSetup()
        {
            // Only check skill for preview navigation, not resources
            if (USI_ConverterOptions.ConverterSwapRequiresRepairSkillEnabled)
            {
                var (skillOk, skillMsg) = KIR_ResourceValidator.ValidateRepairSkill(
                    FlightGlobals.ActiveVessel,
                    USI_ConverterOptions.ConverterSwapRequiresEVAEnabled);

                if (!skillOk)
                {
                    ScreenMessages.PostScreenMessage(skillMsg, 5f, ScreenMessageStyle.UPPER_CENTER);
                    return;
                }
            }
            if (_filteredLoadouts == null || _filteredLoadouts.Count < 2)
                return;

            // Get current index from PERSISTABLE field, not private field
            int currentDisplayIndex = 0;
            if (!string.IsNullOrEmpty(selectedConverterUI))
            {
                for (int i = 0; i < _filteredLoadouts.Count; i++)
                {
                    if (_filteredLoadouts[i].ConverterName == selectedConverterUI)
                    {
                        currentDisplayIndex = i;
                        break;
                    }
                }
            }
            else if (currentLoadout < _filteredLoadouts.Count)
            {
                currentDisplayIndex = currentLoadout; // Start from installed
            }

            int newIndex = currentDisplayIndex + 1;
            if (newIndex >= _filteredLoadouts.Count)
                newIndex = 0;

            // Skip the installed converter
            if (newIndex == currentLoadout && _filteredLoadouts.Count > 1)
            {
                newIndex++;
                if (newIndex >= _filteredLoadouts.Count)
                    newIndex = 0;
            }

            _baseDisplayLoadoutField.SetValue(this, newIndex);
            selectedConverterUI = _filteredLoadouts[newIndex].ConverterName == KIR_Constants.DISABLED_LOADOUT_NAME ? "Disabled" : _filteredLoadouts[newIndex].ConverterName;
            KIR_DebugLogger.Log(string.Format("{0} NextSetup: {1} -> {2}, selected='{3}'", KIR_Constants.DEBUG_EVA_PREFIX, currentDisplayIndex, newIndex, selectedConverterUI));

            ChangeMenu();
        }

        [KSPEvent(guiActive = false, guiActiveEditor = false, guiName = "B1: Prev. Loadout", active = false, guiActiveUnfocused = true, externalToEVAOnly = true, unfocusedRange = 10f)]
        public void KIR_PrevSetup()
        {
            // Only check skill for preview navigation, not resources
            if (USI_ConverterOptions.ConverterSwapRequiresRepairSkillEnabled)
            {
                var (skillOk, skillMsg) = KIR_ResourceValidator.ValidateRepairSkill(
                    FlightGlobals.ActiveVessel,
                    USI_ConverterOptions.ConverterSwapRequiresEVAEnabled);

                if (!skillOk)
                {
                    ScreenMessages.PostScreenMessage(skillMsg, 5f, ScreenMessageStyle.UPPER_CENTER);
                    return;
                }
            }
            if (_filteredLoadouts == null || _filteredLoadouts.Count < 2)
                return;

            // Get current index from PERSISTABLE field, not private field
            int currentDisplayIndex = 0;
            if (!string.IsNullOrEmpty(selectedConverterUI))
            {
                for (int i = 0; i < _filteredLoadouts.Count; i++)
                {
                    if (_filteredLoadouts[i].ConverterName == selectedConverterUI)
                    {
                        currentDisplayIndex = i;
                        break;
                    }
                }
            }
            else if (currentLoadout < _filteredLoadouts.Count)
            {
                currentDisplayIndex = currentLoadout; // Start from installed
            }

            int newIndex = currentDisplayIndex - 1;
            if (newIndex < 0)
                newIndex = _filteredLoadouts.Count - 1;

            // Skip the installed converter
            if (newIndex == currentLoadout && _filteredLoadouts.Count > 1)
            {
                newIndex--;
                if (newIndex < 0)
                    newIndex = _filteredLoadouts.Count - 1;
            }

            _baseDisplayLoadoutField.SetValue(this, newIndex);
            selectedConverterUI = _filteredLoadouts[newIndex].ConverterName == KIR_Constants.DISABLED_LOADOUT_NAME ? "Disabled" : _filteredLoadouts[newIndex].ConverterName;
            KIR_DebugLogger.Log(string.Format("{0} PrevSetup: {1} -> {2}, selected='{3}'", KIR_Constants.DEBUG_EVA_PREFIX, currentDisplayIndex, newIndex, selectedConverterUI));

            ChangeMenu();
        }

        [KSPEvent(guiActive = false, guiActiveEditor = false, guiName = "B1: Install", active = false, guiActiveUnfocused = true, externalToEVAOnly = true, unfocusedRange = 10f)]
        public void KIR_LoadSetup()
        {
            KIR_DebugLogger.Log(string.Format("{0} LoadSetup started", KIR_Constants.DEBUG_EVA_PREFIX));

            // Step 1: Validate all preconditions (abort if any fail)
            if (!ValidateLoadSetupPreconditions())
                return;

            var controller = GetKirController();
            var converters = part.FindModulesImplementing<USI_Converter>();

            // Step 2: Resolve which converter to load from persistent state
            int targetIndex = ResolveTargetLoadoutIndex();
            currentLoadout = targetIndex;

            // Step 3: Apply loadout and capture name changes
            string oldTemplate, newTemplate;
            ExecuteLoadoutChange(controller, converters, out oldTemplate, out newTemplate);

            // Step 4: Finalize UI updates and persistence
            CompleteLoadSetup(oldTemplate, newTemplate);
        }

        /// <summary>
        /// Validates all preconditions for a loadout change. Logs specific abort reasons.
        /// </summary>
        /// <returns>True if all preconditions pass, false otherwise.</returns>
        private bool ValidateLoadSetupPreconditions()
        {
            KIR_DebugLogger.Log(string.Format("{0} LoadSetup preflight check started", KIR_Constants.DEBUG_EVA_PREFIX));

            if (!_bayInitialized || _isDisabled)
            {
                KIR_DebugLogger.Log(string.Format("{0} LoadSetup aborted: _bayInitialized={1}, _isDisabled={2}",
                    KIR_Constants.DEBUG_EVA_PREFIX, _bayInitialized, _isDisabled));
                return false;
            }

            if (_filteredLoadouts == null || _filteredLoadouts.Count == 0)
            {
                KIR_DebugLogger.Log(string.Format("{0} LoadSetup aborted: no loadouts", KIR_Constants.DEBUG_EVA_PREFIX));
                return false;
            }

            var vessel = FlightGlobals.ActiveVessel;
            var controller = GetKirController();
            if (controller == null)
            {
                KIR_DebugLogger.Log(string.Format("{0} LoadSetup aborted: controller is null", KIR_Constants.DEBUG_EVA_PREFIX));
                return false;
            }

            // Check both skill and resources for actual installation
            if (USI_ConverterOptions.ConverterSwapRequiresRepairSkillEnabled)
            {
                var (skillOk, skillMsg) = KIR_ResourceValidator.ValidateRepairSkill(
                    vessel,
                    USI_ConverterOptions.ConverterSwapRequiresEVAEnabled);

                if (!skillOk)
                {
                    ScreenMessages.PostScreenMessage(skillMsg, 5f, ScreenMessageStyle.UPPER_CENTER);
                    KIR_DebugLogger.Log(string.Format("{0} LoadSetup aborted: skill check failed", KIR_Constants.DEBUG_EVA_PREFIX));
                    return false;
                }
            }

            var (resourcesOk, resourceMsg) = KIR_ResourceValidator.ValidateResources(
                vessel,
                controller.SwapCosts,
                part);

            if (!resourcesOk)
            {
                ScreenMessages.PostScreenMessage(resourceMsg, 5f, ScreenMessageStyle.UPPER_CENTER);
                KIR_DebugLogger.Log(string.Format("{0} LoadSetup aborted: resource check failed", KIR_Constants.DEBUG_EVA_PREFIX));
                return false;
            }

            // Deduct resources after successful validation
            KIR_ResourceValidator.DeductResources(
                FlightGlobals.ActiveVessel,
                controller.SwapCosts,
                part);

            KIR_DebugLogger.Log(string.Format("{0} LoadSetup preflight check PASSED", KIR_Constants.DEBUG_EVA_PREFIX));
            return true;
        }

        /// <summary>
        /// Resolves the target loadout index from persistable UI state (selectedConverterUI),
        /// falling back to currentLoadout if UI state is empty or invalid.
        /// </summary>
        /// <returns>The resolved target index.</returns>
        private int ResolveTargetLoadoutIndex()
        {
            int displayIndex = currentLoadout; // Default to currently installed

            if (!string.IsNullOrEmpty(selectedConverterUI))
            {
                for (int i = 0; i < _filteredLoadouts.Count; i++)
                {
                    if (_filteredLoadouts[i].ConverterName == selectedConverterUI)
                    {
                        displayIndex = i;
                        KIR_DebugLogger.Log(string.Format("{0} Resolved index from selectedConverterUI: {1}", KIR_Constants.DEBUG_EVA_PREFIX, i));
                        break;
                    }
                }
            }

            return Mathf.Clamp(displayIndex, 0, _filteredLoadouts.Count - 1);
        }

        /// <summary>
        /// Executes the loadout change by applying it to the converter and capturing old/new names.
        /// </summary>
        /// <param name="controller">The KIR controller instance</param>
        /// <param name="converters">List of converter modules</param>
        /// <param name="oldName">Output: previous converter name</param>
        /// <param name="newName">Output: new converter name</param>
        private void ExecuteLoadoutChange(KIR_ConfigurableSwapController controller, List<USI_Converter> converters, out string oldName, out string newName)
        {
            oldName = _kirPersistedConverterName;
            newName = _filteredLoadouts[currentLoadout].ConverterName;

            KIR_DebugLogger.Log(string.Format("{0} Executing change: '{1}' -> '{2}'", KIR_Constants.DEBUG_EVA_PREFIX, oldName, newName));

            ApplyLoadout(controller, converters);
        }

        /// <summary>
        /// Completes the loadout setup by updating UI, persistence, and showing user message.
        /// </summary>
        /// <param name="oldName">Previous converter name for message</param>
        /// <param name="newName">New converter name for message</param>
        private void CompleteLoadSetup(string oldName, string newName)
        {
            // Update UI (calls UpdateConverterUI, may overwrite curTemplate)
            ChangeMenu();

            // Update persistent state
            _kirPersistedConverterName = newName;

            // Show user confirmation
            ScreenMessages.PostScreenMessage(
                string.Format("Reconfiguration from {0} to {1} completed.", oldName, newName),
                5f, ScreenMessageStyle.UPPER_CENTER);

            KIR_DebugLogger.Log(string.Format("{0} LoadSetup completed successfully", KIR_Constants.DEBUG_EVA_PREFIX));
        }
        /// <summary>
        /// Gets cached converter list or rebuilds if part modules changed
        /// </summary>
        private List<KIR_Converter> GetCachedConverters()
        {
            if (!_cacheInitialized || _cachedConverters == null || (_cachedConverterCount != part.Modules.Count && _cachedConverterCount > 0))
            {
                _cachedConverters = part.FindModulesImplementing<KIR_Converter>();
                _cachedConverterCount = part.Modules.Count;
                _cacheInitialized = true;

                KIR_DebugLogger.Log($"[KIR-PERF] Re-cached {_cachedConverters.Count} converters for bay {bayName}");
            }
            return _cachedConverters;
        }

        [KSPField(isPersistant = true)]
        private string _kirPersistedConverterName = "";

        private static readonly FieldInfo _baseDisplayLoadoutField =
            GetCachedFieldInfo(typeof(USI_SwappableBay), "displayLoadout", BindingFlags.NonPublic | BindingFlags.Instance);

        private KIR_ConfigurableSwapController GetKirController()
        {
            var controller = part.FindModuleImplementing<KIR_ConfigurableSwapController>();
            KIR_DebugLogger.Log(string.Format("{0} GetKirController: {1}", KIR_Constants.DEBUG_EVA_PREFIX, controller != null ? "found" : "null"));
            return controller;
        }

        public override void OnStart(StartState state)
        {
            base.OnStart(state);
            DisableUSIBaseEvents();

            _cacheInitialized = false;
            _cachedConverters = null;
            _cachedConverterCount = -1;
            _cachedCoreHeat = part.FindModuleImplementing<ModuleCoreHeat>();

            // DECLARE postLoadField HERE - use cached reflection
            var postLoadField = GetCachedFieldInfo(typeof(USI_SwappableBay), "_postLoad",
                BindingFlags.NonPublic | BindingFlags.Instance);
            postLoadField?.SetValue(this, true);

            _bayInitialized = true;

            // Add the coroutine for ModuleCoreHeat timing fix
            StartCoroutine(EnsureCacheRebuildAfterConverters());

            // Simple restoration - happens after controller loads naturally
            var controller = GetKirController();
            if (!string.IsNullOrEmpty(_kirPersistedConverterName) && controller != null)
            {
                RefreshFilteredLoadouts(controller);
            }

            if (!hasPermanentLoadout)
            {
                Callback<BaseField, object> selectionChanged = (BaseField field, object oldValue) =>
                {
                    HandleSelectionChange();
                };

                BaseField uiField = Fields["selectedConverterUI"];
                uiField.uiControlEditor.onFieldChanged = selectionChanged;
                uiField.uiControlFlight.onFieldChanged = selectionChanged;
            }

            if (HighLogic.LoadedSceneIsFlight)
                InvokeRepeating(nameof(UpdateConverterUI), 0f, 0.5f);
        }

        //private System.Collections.IEnumerator RestoreAfterLoad()
        //{
        //    yield return null; // Wait for KSP field loading

        //    var controller = GetKirController();
        //    if (!string.IsNullOrEmpty(_kirPersistedConverterName) && controller != null)
        //    {
        //        KIR_DebugLogger.Log($"RestoreAfterLoad: Found persisted '{_kirPersistedConverterName}'");
        //        RefreshFilteredLoadouts(controller);
        //    }
        //}
        private System.Collections.IEnumerator EnsureCacheRebuildAfterConverters()
        {
            yield return new WaitForEndOfFrame();
            yield return new WaitForEndOfFrame();

            var controller = GetKirController();
            if (controller?._cachedCoreHeat != null)
            {
                var updateCacheMethod = typeof(ModuleCoreHeat).GetMethod("UpdateConverterModuleCache",
                    BindingFlags.Public | BindingFlags.Instance);
                updateCacheMethod?.Invoke(controller._cachedCoreHeat, null);

                KIR_DebugLogger.Log("[KIR-HEAT] Final cache rebuild after converter init");
            }
        }

        private void DisableUSIBaseEvents()
        {
            string[] baseEventNames = { "NextSetup", "PrevSetup", "LoadSetup" }; // Original USI names
            foreach (var eventName in baseEventNames)
            {
                var baseEvent = Events[eventName];
                if (baseEvent != null)
                {
                    baseEvent.active = false;
                    baseEvent.guiActive = false;
                    baseEvent.guiActiveEditor = false;
                    baseEvent.guiActiveUnfocused = false;
                    baseEvent.externalToEVAOnly = false;
                }
            }
        }

        public new void Update()
        {
            // Empty - all work done by InvokeRepeating
        }

        public override void OnLoad(ConfigNode node)
        {
            base.OnLoad(node);
        }

        public override void OnSave(ConfigNode node)
        {
            base.OnSave(node);
            node.SetValue("_kirPersistedConverterName", _kirPersistedConverterName, true);
        }

        public new void OnDestroy()
        {
            CancelInvoke(nameof(UpdateConverterUI));
            base.OnDestroy();
        }



        public void UpdateConverterUI()
        {
            var converters = GetCachedConverters();

            // 1. Safety & Disable Check
            if (moduleIndex >= converters.Count || _isDisabled)
            {
                Fields["converterStatus"].guiActive = false;
                Fields["converterLoad"].guiActive = false;
                Events["StartConverter"].active = false;
                Events["StopConverter"].active = false;
                return;
            }

            float currentEff = GetConverterEfficiency();
            var converter = converters[moduleIndex];

            // 2. Get the raw base status from the converter
            string baseStatus = converter.GetCurrentStatus();

            // 3. Logic: Only append efficiency if it is Active AND produces heat
            if (baseStatus != _lastStatus || Mathf.Abs(currentEff - _lastEff) > 0.001f)
            {
                // Update trackers
                _lastStatus = baseStatus;
                _lastEff = currentEff;

                if (baseStatus != "Inactive" && converter.GeneratesHeat)
                {
                    _convStatusSB.Clear();
                    _convStatusSB.Append(baseStatus);
                    _convStatusSB.Append(" Eff. ");
                    _convStatusSB.Append(currentEff.ToString("P1"));
                    curTemplate = _convStatusSB.ToString();
                }
                else
                {
                    // Otherwise, the template is just the base status (Inactive or Non-Thermal)
                    curTemplate = baseStatus;
                }
            }

            // 4. Only update the actual KSPField if the text changed
            if (converterStatus != curTemplate)
            {
                converterStatus = curTemplate;
            }

            UpdateConverterButtons();
        }

        private float GetConverterEfficiency()
        {
            // 1. Use the cached module instead of searching every frame
            if (_cachedCoreHeat == null) return 1f;

            var converters = GetCachedConverters();
            var converter = converters[moduleIndex];

            // 2. Direct null/logic checks (Faster than try-catch)
            if (converter?.ThermalEfficiency?.Curve == null) return 1f;
            if (converter.ThermalEfficiency.Curve.keys.Length == 0) return 1f;

            // 3. Evaluate the curve
            return converter.ThermalEfficiency.Evaluate((float)_cachedCoreHeat.CoreTemperature);
        }

        private void UpdateConverterButtons()
        {
            var converters = GetCachedConverters();
            if (moduleIndex >= converters.Count || _isDisabled)
            {
                Events["StartConverter"].active = false;
                Events["StopConverter"].active = false;
                return;
            }

            var converter = converters[moduleIndex];
            bool isActive = converter.IsActivated;
            bool showButtons = HighLogic.LoadedSceneIsFlight && !_isDisabled;

            Events["StartConverter"].guiName = converter.StartActionName;
            Events["StopConverter"].guiName = converter.StopActionName;
            Events["StartConverter"].active = showButtons && !isActive;
            Events["StopConverter"].active = showButtons && isActive;
        }

        private void InitializeSelectionUI()
        {
            if (_filteredLoadouts == null || _filteredLoadouts.Count < 2 || hasPermanentLoadout)
            {
                Fields["selectedConverterUI"].guiActive = false;
                Fields["selectedConverterUI"].guiActiveEditor = false;
                return;
            }

            string[] optionValues = new string[_filteredLoadouts.Count];
            string[] displayNames = new string[_filteredLoadouts.Count];

            for (int i = 0; i < _filteredLoadouts.Count; i++)
            {
                string name = _filteredLoadouts[i].ConverterName;
                optionValues[i] = name;
                displayNames[i] = name == KIR_Constants.DISABLED_LOADOUT_NAME ? "Disabled" : name;
            }

            UI_ChooseOption widget = null;
            if (HighLogic.LoadedSceneIsEditor)
                widget = (UI_ChooseOption)Fields["selectedConverterUI"].uiControlEditor;
            else if (HighLogic.LoadedSceneIsFlight)
                widget = (UI_ChooseOption)Fields["selectedConverterUI"].uiControlFlight;

            if (widget == null) return;

            widget.options = optionValues;
            widget.display = displayNames;

            if (widget.partActionItem != null)
            {
                UIPartActionChooseOption control = widget.partActionItem as UIPartActionChooseOption;
                if (control?.slider != null)
                {
                    int index = 0;
                    for (int i = 0; i < optionValues.Length; i++)
                    {
                        if (optionValues[i] == selectedConverterUI)
                        {
                            index = i;
                            break;
                        }
                    }

                    Callback<BaseField, object> tempCallback = widget.onFieldChanged;
                    widget.onFieldChanged = null;

                    control.slider.minValue = 0;
                    control.slider.maxValue = optionValues.Length - 1;
                    control.slider.value = index;
                    control.OnValueChanged(0);

                    widget.onFieldChanged = tempCallback;
                }
            }
        }

        private void HandleSelectionChange()
        {
            if (_filteredLoadouts == null || _filteredLoadouts.Count <= 1)
                return;

            int newIndex = GetCurrentSelectionIndex();
            if (_baseDisplayLoadoutField != null)
                _baseDisplayLoadoutField.SetValue(this, newIndex);

            ChangeMenu();

            bool evaRequired = USI_ConverterOptions.ConverterSwapRequiresEVAEnabled;
            bool isEditor = HighLogic.LoadedSceneIsEditor;

            if (!evaRequired || isEditor)
            {
                LoadSetupCustom();
            }
        }

        private int GetCurrentSelectionIndex()
        {
            if (_filteredLoadouts == null || string.IsNullOrEmpty(selectedConverterUI))
                return currentLoadout;

            for (int i = 0; i < _filteredLoadouts.Count; i++)
            {
                if (_filteredLoadouts[i].ConverterName == selectedConverterUI)
                    return i;
            }
            return currentLoadout;
        }

        private void LoadSetupCustom()
        {
            // EDITOR MODE: Skip validation, apply immediately and persist
            if (HighLogic.LoadedSceneIsEditor)
            {
                string oldTemplate = curTemplate;
                int newIndex = GetCurrentSelectionIndex();

                if (_baseDisplayLoadoutField != null)
                    _baseDisplayLoadoutField.SetValue(this, newIndex);
                currentLoadout = newIndex;

                // Update persistence immediately
                if (_filteredLoadouts != null && newIndex >= 0 && newIndex < _filteredLoadouts.Count)
                {
                    _kirPersistedConverterName = _filteredLoadouts[newIndex].ConverterName;
                    selectedConverterUI = _kirPersistedConverterName;
                    KIR_DebugLogger.Log(string.Format("{0} LoadSetupCustom: Persisted '{1}' (editor)",
                        KIR_Constants.DEBUG_EVA_PREFIX, _kirPersistedConverterName));
                }

                ApplyLoadout();

                ScreenMessages.PostScreenMessage(
                    string.Format("Reconfigured from {0} to {1}", oldTemplate, curTemplate),
                    5f, ScreenMessageStyle.UPPER_CENTER);

                SyncSelectionUI();
                ChangeMenu();
                return;
            }

            // FLIGHT MODE: Full validation required
            if (!ValidateLoadSetupPreconditions())
                return;

            string oldTemplateFlight = curTemplate;
            int newIndexFlight = GetCurrentSelectionIndex();

            if (_baseDisplayLoadoutField != null)
                _baseDisplayLoadoutField.SetValue(this, newIndexFlight);
            currentLoadout = newIndexFlight;

            ApplyLoadout();

            ScreenMessages.PostScreenMessage(
                string.Format("Reconfiguration from {0} to {1} completed.", oldTemplateFlight, curTemplate),
                5f, ScreenMessageStyle.UPPER_CENTER);

            SyncSelectionUI();
            ChangeMenu();
        }

        private void SyncSelectionUI()
        {
            if (_filteredLoadouts != null && currentLoadout < _filteredLoadouts.Count)
            {
                selectedConverterUI = _filteredLoadouts[currentLoadout].ConverterName;
            }
        }

        public void RefreshFilteredLoadouts(KIR_ConfigurableSwapController controller = null)
        {
            if (controller == null)
                controller = GetKirController();

            if (controller == null)
            {
                SetupDisabledBay();
                return;
            }

            var newFilteredLoadouts = controller.GetLoadoutsForBay(moduleIndex);
            _filteredLoadouts = newFilteredLoadouts ?? new List<AbstractSwapOption>();
            _isDisabled = _filteredLoadouts.Count == 1 && _filteredLoadouts[0] is DisabledSwapOption;

            // Just restore the UI state - don't call ApplyLoadout here
            if (!string.IsNullOrEmpty(_kirPersistedConverterName))
            {
                int restoredIndex = -1;
                for (int i = 0; i < _filteredLoadouts.Count; i++)
                {
                    if (_filteredLoadouts[i].ConverterName == _kirPersistedConverterName)
                    {
                        restoredIndex = i;
                        break;
                    }
                }

                if (restoredIndex >= 0)
                {
                    currentLoadout = restoredIndex;
                    selectedConverterUI = _kirPersistedConverterName;
                    KIR_DebugLogger.Log($"RefreshFilteredLoadouts: Restored '{_kirPersistedConverterName}'");
                }
                else
                {
                    _kirPersistedConverterName = "";
                    currentLoadout = 0;
                    selectedConverterUI = "";
                }
            }

            ChangeMenu();
            SetupEnabledBay();
        }

        private void SetupDisabledBay()
        {
            Events["KIR_NextSetup"].active = false;
            Events["KIR_PrevSetup"].active = false;
            Events["KIR_LoadSetup"].active = false;
            Fields["curTemplate"].guiActive = false;
            Fields["curTemplate"].guiActiveEditor = false;
            Fields["selectedConverterUI"].guiActive = false;
            Fields["selectedConverterUI"].guiActiveEditor = false;
            Events["StartConverter"].active = false;
            Events["StopConverter"].active = false;
            Fields["converterStatus"].guiActive = false;
            Fields["converterLoad"].guiActive = false;
            curTemplate = KIR_Constants.DISABLED_LOADOUT_NAME;
            MonoUtilities.RefreshContextWindows(part);
        }

        private void SetupEnabledBay()
        {
            if (_filteredLoadouts == null || _filteredLoadouts.Count == 0)
            {
                SetupDisabledBay();
                return;
            }

            currentLoadout = Mathf.Clamp(currentLoadout, 0, _filteredLoadouts.Count - 1);
            var loadout = _filteredLoadouts[currentLoadout];
            if (_baseDisplayLoadoutField != null)
                _baseDisplayLoadoutField.SetValue(this, currentLoadout);

            if (loadout == null || loadout.ConverterName == KIR_Constants.DISABLED_LOADOUT_NAME)
            {
                SetupDisabledBay();
                return;
            }

            ApplyLoadout();

            Events["KIR_NextSetup"].active = true;
            Events["KIR_PrevSetup"].active = true;
            Events["KIR_LoadSetup"].active = true;
            Fields["curTemplate"].guiActive = true;
            Fields["curTemplate"].guiActiveEditor = true;

            InitializeSelectionUI();
            SyncSelectionUI();
            ChangeMenu();
        }

        public new void ChangeMenu()
        {
            // Early exit for uninitialized/disabled bays
            if (ShouldSkipChangeMenu())
                return;

            // Validate and clamp persisted state
            currentLoadout = Mathf.Clamp(currentLoadout, 0, _filteredLoadouts.Count - 1);
            SyncPersistentDisplayField();

            // Update display field name
            Fields["curTemplate"].guiName = _filteredLoadouts[currentLoadout].ConverterName;

            // Update button names based on preview
            UpdateButtonNames();

            // Log current preview state for debugging
            KIR_DebugLogger.Log(string.Format("{0} ChangeMenu: previewConverter='{1}', selectedConverterUI='{2}'", KIR_Constants.DEBUG_EVA_PREFIX, GetPreviewConverterName(), selectedConverterUI));

            // Apply visibility rules and refresh
            UpdateAllUIVisibility();
            MonoUtilities.RefreshContextWindows(part);
        }
        /// <summary>
        /// Determines whether ChangeMenu should exit early or call base implementation
        /// </summary>
        private bool ShouldSkipChangeMenu()
        {
            if (!_bayInitialized || _filteredLoadouts == null || _filteredLoadouts.Count == 0)
            {
                if (!_bayInitialized)
                    base.ChangeMenu();
                return true;
            }
            return false;
        }

        /// <summary>
        /// Resolves the display index from selectedConverterUI, falling back to currentLoadout
        /// </summary>
        private int ResolveDisplayIndex()
        {
            int displayIndex = currentLoadout; // Default fallback

            if (!string.IsNullOrEmpty(selectedConverterUI))
            {
                for (int i = 0; i < _filteredLoadouts.Count; i++)
                {
                    if (_filteredLoadouts[i].ConverterName == selectedConverterUI)
                    {
                        displayIndex = i;
                        break;
                    }
                }
            }

            return Mathf.Clamp(displayIndex, 0, _filteredLoadouts.Count - 1);
        }

        /// <summary>
        /// Gets the preview converter name based on persistent UI state
        /// </summary>
        private string GetPreviewConverterName()
        {
            if (_filteredLoadouts == null || _filteredLoadouts.Count == 0)
                return string.Empty;

            int displayIndex = ResolveDisplayIndex();
            return _filteredLoadouts[displayIndex].ConverterName;
        }

        /// <summary>
        /// Updates Next/Prev/Install button names based on preview state
        /// </summary>
        private void UpdateButtonNames()
        {
            string previewConverter = GetPreviewConverterName();

            Events["KIR_NextSetup"].guiName = string.Format("{0} Next", bayName).Trim();
            Events["KIR_PrevSetup"].guiName = string.Format("{0} Prev.", bayName).Trim();
            Events["KIR_LoadSetup"].guiName = string.Format("{0} Install {1}", bayName, previewConverter).Trim();
        }

        /// <summary>
        /// Syncs the base class's private displayLoadout field with currentLoadout
        /// </summary>
        private void SyncPersistentDisplayField()
        {
            if (_baseDisplayLoadoutField == null) return;

            int currentValue = (int)_baseDisplayLoadoutField.GetValue(this);
            int clampedValue = Mathf.Clamp(currentValue, 0, _filteredLoadouts?.Count - 1 ?? 0);

            if (currentValue != clampedValue)
                _baseDisplayLoadoutField.SetValue(this, clampedValue);

            if (clampedValue != currentLoadout)
                _baseDisplayLoadoutField.SetValue(this, currentLoadout);
        }

        /// <summary>
        /// Updates visibility states for all UI fields and controls
        /// </summary>
        private void UpdateAllUIVisibility()
        {
            bool isEditor = HighLogic.LoadedSceneIsEditor;
            bool shouldShowUI = !_isDisabled && !hasPermanentLoadout;
            bool isMultiOption = shouldShowUI && _filteredLoadouts.Count >= 2;

            Fields["selectedConverterUI"].guiActiveEditor = isMultiOption;
            Fields["selectedConverterUI"].guiActive = false; // Never in flight

            Fields["curTemplate"].guiActiveEditor = !isMultiOption;
            Fields["curTemplate"].guiActive = !_isDisabled;

            if (shouldShowUI && !isEditor)
            {
                UpdateConverterUI();
            }
        }


        private void ApplyLoadout()
        {
            if (_filteredLoadouts == null || _filteredLoadouts.Count == 0) return;

            var controller = GetKirController();
            if (controller == null) return;

            var converters = part.FindModulesImplementing<USI_Converter>();
            if (converters.Count == 0) return;

            ApplyLoadout(controller, converters);
        }

        private void ApplyLoadout(KIR_ConfigurableSwapController controller, List<USI_Converter> converters)
        {
            if (_filteredLoadouts == null || currentLoadout < 0 || currentLoadout >= _filteredLoadouts.Count)
            {
                KIR_DebugLogger.Log(string.Format("{0}Early Exit due to _filteredLoadouts == null || currentLoadout < 0 || currentLoadout >= _filteredLoadouts.Count", KIR_Constants.DEBUG_EVA_PREFIX));
                return;
            }

            var loadout = _filteredLoadouts[currentLoadout];
            KIR_DebugLogger.Log(string.Format("{0} loadout set to {1}", KIR_Constants.DEBUG_EVA_PREFIX, loadout));
            if (loadout == null || loadout.ConverterName == "_DISABLED_")
            {
                KIR_DebugLogger.Log(string.Format("{0} loadout was null OR {1}, running SetupDisabledBay() and then exiting", KIR_Constants.DEBUG_EVA_PREFIX, KIR_Constants.DISABLED_LOADOUT_NAME));
                SetupDisabledBay();
                return;
            }

            int fullIndex = FindLoadoutIndex(loadout.ConverterName);
            KIR_DebugLogger.Log(string.Format("{0} fullIndex set to {1}", KIR_Constants.DEBUG_EVA_PREFIX, fullIndex));
            if (fullIndex >= 0)
            {
                controller.ApplyLoadout(fullIndex, moduleIndex, converters);
                curTemplate = loadout.ConverterName;
                KIR_DebugLogger.Log(string.Format("{0} curTemplate set to {1}", KIR_Constants.DEBUG_EVA_PREFIX, curTemplate));
                _kirPersistedConverterName = curTemplate;
                if (_baseDisplayLoadoutField != null)
                    _baseDisplayLoadoutField.SetValue(this, currentLoadout);
            }
        }

        private int FindLoadoutIndex(string converterName)
        {
            if (converterName == KIR_Constants.DISABLED_LOADOUT_NAME)
            {
                KIR_DebugLogger.Log(string.Format("{0} converterName was {1}, returning -1", KIR_Constants.DEBUG_EVA_PREFIX, KIR_Constants.DISABLED_LOADOUT_NAME));
                return -1;
            }

            var controller = GetKirController();
            if (controller == null)
            {
                KIR_DebugLogger.Log(string.Format("{0} controller was NULL, returning -1", KIR_Constants.DEBUG_EVA_PREFIX));
                return -1;
            }

            for (int i = 0; i < controller.Loadouts.Count; i++)
            {
                if (controller.Loadouts[i].ConverterName == converterName)
                {
                    KIR_DebugLogger.Log(string.Format("{0} Loadout found ({1}) returning converterName {2}", KIR_Constants.DEBUG_EVA_PREFIX, controller.Loadouts[i].ConverterName, converterName));
                    return i;
                }
            }
            KIR_DebugLogger.Log(string.Format("{0} for loop failed, returning -1", KIR_Constants.DEBUG_EVA_PREFIX));
            return -1;
        }
        /// <summary>
        /// Profiles method execution time in DEBUG builds only.
        /// Logs if execution exceeds 1ms.
        /// </summary>
        [Conditional("DEBUG")]
        private void ProfileMethod(string methodName, System.Action action)
        {
            var sw = Stopwatch.StartNew();
            action();
            sw.Stop();
            if (sw.ElapsedMilliseconds > 1)
            {
                KIR_DebugLogger.Log($"[KIR-PERF] {methodName} took {sw.ElapsedMilliseconds}ms");
            }
        }
    }
}