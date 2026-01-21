using System.Linq;
using UnityEngine;

public class KIR_CoreHeatVisualizer : ModuleColorChanger
{
    [KSPField]
    public string targetTransformName = "";

    private ModuleCoreHeat _coreHeatModule;

    public override void OnStart(StartState state)
    {
        base.OnStart(state);

        // Parse transform names so they're ready for base.Start() filtering
        if (!string.IsNullOrEmpty(targetTransformName))
        {
            includedRenderers = targetTransformName.Split(',')
                                    .Select(t => t.Trim())
                                    .Where(t => !string.IsNullOrEmpty(t))
                                    .ToList();
        }
        else
        {
            // Critical error: no target specified
            enabled = false;
            return;
        }

        // Find the heat module
        _coreHeatModule = part.FindModuleImplementing<ModuleCoreHeat>();
        if (_coreHeatModule == null)
        {
            Debug.LogWarning($"[KIR] No ModuleCoreHeat found for '{part.name}'. Visualizer disabled.");
            enabled = false;
            return;
        }

        // Configure base module animation system for automatic heat-driven control
        useRate = true;
        animState = false;
        // UI toggles are controlled by config fields: toggleInEditor, toggleInFlight, etc.
    }

    public override void Start()
    {
        // Let the base class initialize renderers using our includedRenderers list
        base.Start();

        // If the base class failed to set up valid renderers, disable
        if (!isValid)
        {
            enabled = false;
        }
    }

    public override void FixedUpdate()
    {
        // Let the base class handle its animation logic first
        base.FixedUpdate();

        // Only update visuals in flight when we have a valid heat module
        if (!HighLogic.LoadedSceneIsFlight || _coreHeatModule == null)
            return;

        float thermalScalar = CalculateThermalScalar();
        SetScalar(thermalScalar);
    }

    private float CalculateThermalScalar()
    {
        if (_coreHeatModule == null || !_coreHeatModule.isEnabled)
            return 0f;

        // Direct access to ModuleCoreHeat's temperature data
        double currentTemp = _coreHeatModule.CoreTemperature;
        double shutdownTemp = _coreHeatModule.CoreShutdownTemp;

        if (shutdownTemp > 0.0)
            return Mathf.Clamp01((float)(currentTemp / shutdownTemp));

        return 0f;
    }
}