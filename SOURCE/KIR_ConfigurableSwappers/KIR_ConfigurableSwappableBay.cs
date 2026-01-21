using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using USITools;
using static KreeglandIndustrialRepurposing.KIR_ConfigurableSwapController;

namespace KreeglandIndustrialRepurposing
{
    public class KIR_ConfigurableSwappableBay : USI_SwappableBay
    {
        private List<AbstractSwapOption> _filteredLoadouts;
        private bool _isDisabled = false;
        private bool _bayInitialized = false;

        [KSPField(guiActive = false, guiActiveEditor = false, guiName = "Status")]
        public string converterStatus = "Inactive";

        [KSPField(guiActive = false, guiActiveEditor = false, guiName = "Load")]
        public string converterLoad = "0%";

        [KSPEvent(guiActive = true, guiActiveEditor = false, guiName = "Start Converter", active = true)]
        public void StartConverter()
        {
            var converters = part.FindModulesImplementing<KIR_Converter>();
            if (moduleIndex < converters.Count && !_isDisabled)
            {
                var converter = converters[moduleIndex];
                converter.StartResourceConverter();
                UpdateConverterUI();
                KIR_DebugLogger.Log(string.Format("[KIR] Bay {0}: Started converter '{1}'", bayName, converter.ConverterName));
            }
        }

        [KSPEvent(guiActive = true, guiActiveEditor = false, guiName = "Stop Converter", active = false)]
        public void StopConverter()
        {
            var converters = part.FindModulesImplementing<KIR_Converter>();
            if (moduleIndex < converters.Count && !_isDisabled)
            {
                var converter = converters[moduleIndex];
                converter.StopResourceConverter();
                UpdateConverterUI();
                KIR_DebugLogger.Log(string.Format("[KIR] Bay {0}: Stopped converter '{1}'", bayName, converter.ConverterName));
            }
        }

        //UI SELECTOR FIELDS - EDITOR ONLY
        [KSPField(isPersistant = false, guiActive = false, guiActiveEditor = false, guiName = "Selection"),
         UI_ChooseOption(suppressEditorShipModified = true)]
        public string selectedConverterUI = string.Empty;

        [KSPEvent(guiActive = false, guiActiveEditor = false, guiName = "B1: Next Loadout", active = false, guiActiveUnfocused = true, externalToEVAOnly = true, unfocusedRange = 10f)]
        public void KIR_NextSetup()
        {
            if (!CheckResourcesCustom())
                return;
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
            selectedConverterUI = _filteredLoadouts[newIndex].ConverterName;
            KIR_DebugLogger.Log(string.Format("[KIR-EVA] NextSetup: {0} -> {1}, selected='{2}'",
                currentDisplayIndex, newIndex, selectedConverterUI));

            ChangeMenu();
        }

        [KSPEvent(guiActive = false, guiActiveEditor = false, guiName = "B1: Prev. Loadout", active = false, guiActiveUnfocused = true, externalToEVAOnly = true, unfocusedRange = 10f)]
        public void KIR_PrevSetup()
        {
            if (!CheckResourcesCustom())
                return;
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
            selectedConverterUI = _filteredLoadouts[newIndex].ConverterName;
            KIR_DebugLogger.Log(string.Format("[KIR-EVA] PrevSetup: {0} -> {1}, selected='{2}'",
                currentDisplayIndex, newIndex, selectedConverterUI));

            ChangeMenu();
        }

        [KSPEvent(guiActive = false, guiActiveEditor = false, guiName = "B1: Install", active = false, guiActiveUnfocused = true, externalToEVAOnly = true, unfocusedRange = 10f)]
        public void KIR_LoadSetup()
        {
            KIR_DebugLogger.Log("[KIR-EVA] LoadSetup started");

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
            KIR_DebugLogger.Log("[KIR-EVA] LoadSetup preflight check started");

            if (!_bayInitialized || _isDisabled)
            {
                KIR_DebugLogger.Log($"[KIR-EVA] LoadSetup aborted: _bayInitialized={_bayInitialized}, _isDisabled={_isDisabled}");
                return false;
            }

            if (_filteredLoadouts == null || _filteredLoadouts.Count == 0)
            {
                KIR_DebugLogger.Log("[KIR-EVA] LoadSetup aborted: no loadouts");
                return false;
            }

            if (!CheckResourcesCustom())
            {
                KIR_DebugLogger.Log("[KIR-EVA] LoadSetup aborted: resource check failed");
                return false;
            }

            var controller = GetKirController();
            if (controller == null)
            {
                KIR_DebugLogger.Log("[KIR-EVA] LoadSetup aborted: controller is null");
                return false;
            }

            var converters = part.FindModulesImplementing<USI_Converter>();
            if (converters.Count == 0)
            {
                KIR_DebugLogger.Log("[KIR-EVA] LoadSetup aborted: no converters");
                return false;
            }

            KIR_DebugLogger.Log("[KIR-EVA] LoadSetup preflight check PASSED");
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
                        KIR_DebugLogger.Log($"[KIR-EVA] Resolved index from selectedConverterUI: {i}");
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

            KIR_DebugLogger.Log($"[KIR-EVA] Executing change: '{oldName}' -> '{newName}'");

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

            KIR_DebugLogger.Log("[KIR-EVA] LoadSetup completed successfully");
        }

        [KSPField(isPersistant = true)]
        private string _kirPersistedConverterName = "";

        private static readonly FieldInfo _baseDisplayLoadoutField =
            typeof(USI_SwappableBay).GetField("displayLoadout",
                BindingFlags.NonPublic | BindingFlags.Instance);

        private KIR_ConfigurableSwapController GetKirController()
        {
            var controller = part.FindModuleImplementing<KIR_ConfigurableSwapController>();
            KIR_DebugLogger.Log(string.Format("[KIR-EVA] GetKirController: {0}", controller != null ? "found" : "null"));
            return controller;
        }

        public override void OnStart(StartState state)
        {
            base.OnStart(state);
            DisableUSIBaseEvents();

            var postLoadField = typeof(USI_SwappableBay).GetField("_postLoad",
                BindingFlags.NonPublic | BindingFlags.Instance);
            postLoadField?.SetValue(this, true);
            _bayInitialized = true;

            // Fetch controller fresh for persistence restore
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

            if (node.HasValue("_kirPersistedConverterName"))
            {
                _kirPersistedConverterName = node.GetValue("_kirPersistedConverterName");
            }
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
            var converters = part.FindModulesImplementing<KIR_Converter>();
            if (moduleIndex >= converters.Count || _isDisabled)
            {
                Fields["converterStatus"].guiActive = false;
                Fields["converterLoad"].guiActive = false;
                Events["StartConverter"].active = false;
                Events["StopConverter"].active = false;
                return;
            }

            var converter = converters[moduleIndex];
            string status = converter.GetCurrentStatus();

            // Update curTemplate content but NOT guiName
            curTemplate = status;

            UpdateConverterButtons();
        }

        private void UpdateConverterButtons()
        {
            var converters = part.FindModulesImplementing<KIR_Converter>();
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
                displayNames[i] = name == "_DISABLED_" ? "Disabled" : name;
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
            if (!CheckResourcesCustom())
                return;

            string oldTemplate = curTemplate;
            int newIndex = GetCurrentSelectionIndex();

            if (_baseDisplayLoadoutField != null)
                _baseDisplayLoadoutField.SetValue(this, newIndex);
            currentLoadout = newIndex;

            ApplyLoadout();

            ScreenMessages.PostScreenMessage(
                string.Format("Reconfiguration from {0} to {1} completed.", oldTemplate, curTemplate),
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

            KIR_DebugLogger.Log(string.Format("[KIR-EVA] RefreshFilteredLoadouts controller={0}", controller != null));

            if (controller == null)
            {
                Debug.LogWarning(string.Format("[KIR] Bay{0} cannot find controller", moduleIndex));
                SetupDisabledBay();
                return;
            }

            var newFilteredLoadouts = controller.GetLoadoutsForBay(moduleIndex);
            _filteredLoadouts = newFilteredLoadouts ?? new List<AbstractSwapOption>();
            _isDisabled = _filteredLoadouts.Count == 1 && _filteredLoadouts[0] is DisabledSwapOption;

            bool isPersistedValid = false;
            if (!string.IsNullOrEmpty(_kirPersistedConverterName))
            {
                foreach (var loadout in _filteredLoadouts)
                {
                    if (loadout.ConverterName == _kirPersistedConverterName)
                    {
                        isPersistedValid = true;
                        break;
                    }
                }
            }

            if (!isPersistedValid)
            {
                _kirPersistedConverterName = "";
                currentLoadout = 0;
                if (_baseDisplayLoadoutField != null)
                    _baseDisplayLoadoutField.SetValue(this, 0);
                selectedConverterUI = "";
            }

            ChangeMenu();

            if (_isDisabled || _filteredLoadouts.Count == 0)
            {
                SetupDisabledBay();
                return;
            }

            InitializeSelectionUI();
            SyncSelectionUI();
            SetupEnabledBay();
            KIR_DebugLogger.Log(string.Format("[KIR-EVA] RefreshFilteredLoadouts complete: _isDisabled={0}, count={1}", _isDisabled, _filteredLoadouts.Count));
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
            curTemplate = "_DISABLED_";
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

            if (loadout == null || loadout.ConverterName == "_DISABLED_")
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
            KIR_DebugLogger.Log(string.Format("[KIR-EVA] ChangeMenu: previewConverter='{0}', selectedConverterUI='{1}'",
                GetPreviewConverterName(), selectedConverterUI));

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
            KIR_DebugLogger.Log("[KIR-EVA] Starting Parameterized ApplyLoadout()");
            if (_filteredLoadouts == null || currentLoadout < 0 || currentLoadout >= _filteredLoadouts.Count)
            {
                KIR_DebugLogger.Log("[KIR-EVA] Early Exit due to _filteredLoadouts == null || currentLoadout < 0 || currentLoadout >= _filteredLoadouts.Count");
                return;
            }

            var loadout = _filteredLoadouts[currentLoadout];
            KIR_DebugLogger.Log(string.Format("[KIR-EVA] loadout set to {0}", loadout));
            if (loadout == null || loadout.ConverterName == "_DISABLED_")
            {
                KIR_DebugLogger.Log("[KIR-EVA] loadout was null OR _DISABLED_, running SetupDisabledBay() and then exiting");
                SetupDisabledBay();
                return;
            }

            int fullIndex = FindLoadoutIndex(loadout.ConverterName);
            KIR_DebugLogger.Log(string.Format("[KIR-EVA] fullIndex set to {0}", fullIndex));
            if (fullIndex >= 0)
            {
                controller.ApplyLoadout(fullIndex, moduleIndex, converters);
                curTemplate = loadout.ConverterName;
                KIR_DebugLogger.Log(string.Format("[KIR-EVA] curTemplate set to {0}", curTemplate));
                _kirPersistedConverterName = curTemplate;
                if (_baseDisplayLoadoutField != null)
                    _baseDisplayLoadoutField.SetValue(this, currentLoadout);
            }
        }

        private int FindLoadoutIndex(string converterName)
        {
            if (converterName == "_DISABLED_")
            {
                KIR_DebugLogger.Log("[KIR-EVA] converterName was _DISABLED_, returning -1");
                return -1;
            }

            var controller = GetKirController();
            if (controller == null)
            {
                KIR_DebugLogger.Log("[KIR-EVA] controller was NULL, returning -1");
                return -1;
            }

            for (int i = 0; i < controller.Loadouts.Count; i++)
            {
                if (controller.Loadouts[i].ConverterName == converterName)
                {
                    KIR_DebugLogger.Log(string.Format("[KIR-EVA] Loadout found ({0}) returning converterName {1}", controller.Loadouts[i].ConverterName, converterName));
                    return i;
                }
            }
            KIR_DebugLogger.Log("[KIR-EVA] for loop failed, returning -1");
            return -1;
        }

        private bool CheckResourcesCustom()
        {
            KIR_DebugLogger.Log("[KIR-EVA] CheckResourcesCustom started");

            if (HighLogic.LoadedSceneIsEditor) return true;

            // Repair skill check
            if (USI_ConverterOptions.ConverterSwapRequiresRepairSkillEnabled)
            {
                bool foundRepairSkill = false;

                if (USI_ConverterOptions.ConverterSwapRequiresEVAEnabled)
                {
                    var kerbal = FlightGlobals.ActiveVessel.rootPart.protoModuleCrew[0];
                    if (kerbal?.HasEffect("RepairSkill") == true)
                        foundRepairSkill = true;
                }
                else
                {
                    var crew = FlightGlobals.ActiveVessel.GetVesselCrew();
                    foreach (var kerbal in crew)
                    {
                        if (kerbal.HasEffect("RepairSkill"))
                        {
                            foundRepairSkill = true;
                            break;
                        }
                    }
                }

                KIR_DebugLogger.Log(string.Format("[KIR-EVA] CheckResourcesCustom skill check result: {0}", foundRepairSkill));

                if (!foundRepairSkill)
                {
                    ScreenMessages.PostScreenMessage("Repair skill required!", 5f, ScreenMessageStyle.UPPER_CENTER);
                    return false;
                }
            }

            float costMultiplier = USI_ConverterOptions.ConverterSwapCostMultiplierValue;
            KIR_DebugLogger.Log(string.Format("[KIR-EVA] CheckResourcesCustom costMultiplier: {0}", costMultiplier));

            if (costMultiplier > ResourceUtilities.FLOAT_TOLERANCE)
            {
                var controller = GetKirController();
                if (controller == null)
                {
                    KIR_DebugLogger.Log("[KIR-EVA] CheckResourcesCustom early exit: no controller");
                    return true;
                }

                foreach (var resource in controller.SwapCosts)
                {
                    if (!HasResourceCustom(resource))
                    {
                        KIR_DebugLogger.Log(string.Format("[KIR-EVA] CheckResourcesCustom failed: missing {0}", resource.ResourceName));
                        return false;
                    }
                }
                KIR_DebugLogger.Log("[KIR-EVA] CheckResourcesCustom all resources present");
            }
            return true;
        }

        private bool HasResourceCustom(ResourceRatio resInfo)
        {
            var costMultiplier = USI_ConverterOptions.ConverterSwapCostMultiplierValue;
            if (costMultiplier <= ResourceUtilities.FLOAT_TOLERANCE) return true;

            var needed = resInfo.Ratio * costMultiplier;
            var whpList = LogisticsTools.GetRegionalWarehouses(vessel, "USI_ModuleResourceWarehouse");

            if (resInfo.ResourceName == "ElectricCharge")
                whpList.AddRange(part.vessel.parts);

            foreach (var whp in whpList)
            {
                if (whp == part) continue;

                if (resInfo.ResourceName != "ElectricCharge")
                {
                    var wh = whp.FindModuleImplementing<USI_ModuleResourceWarehouse>();
                    if (wh != null && !wh.localTransferEnabled) continue;
                }

                if (whp.Resources.Contains(resInfo.ResourceName))
                {
                    var res = whp.Resources[resInfo.ResourceName];
                    if (res.amount >= needed) return true;
                    needed -= res.amount;
                }
            }
            return needed < ResourceUtilities.FLOAT_TOLERANCE;
        }
    }
}