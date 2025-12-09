using UnityEngine;

namespace KSPShaderTools
{
    public class TexturesUnlimitedB9Bridge : PartModule
    {
        private KSPTextureSwitch tuModule;
        private string lastTextureSet = "";
        private bool textureSetInitialized = false;

        public override void OnStart(StartState state)
        {
            base.OnStart(state);
            
            tuModule = part.GetComponent<KSPTextureSwitch>();
            if (tuModule == null) return;

            // Initialize with current texture set
            if (!string.IsNullOrEmpty(tuModule.currentTextureSet))
            {
                lastTextureSet = tuModule.currentTextureSet;
                textureSetInitialized = true;
            }
            
            // Subscribe to events based on scene
            if (HighLogic.LoadedSceneIsEditor)
            {
                GameEvents.onEditorPartEvent.Add(OnEditorPartEvent);
            }
            else if (HighLogic.LoadedSceneIsFlight)
            {
                GameEvents.onVesselWasModified.Add(OnVesselWasModified);
            }
        }

        // B9 fires this in editor when switching subtypes
        private void OnEditorPartEvent(ConstructionEventType type, Part p)
        {
            // Only respond to PartTweaked events on our part
            if (type == ConstructionEventType.PartTweaked && p == this.part && 
                tuModule != null && !string.IsNullOrEmpty(tuModule.currentTextureSet))
            {
                // Check if texture set actually changed
                if (textureSetInitialized && tuModule.currentTextureSet == lastTextureSet)
                {
                    return;
                }
                
                tuModule.enableTextureSet(tuModule.currentTextureSet, false, true); //Third parameter is "userInput" which flags colors for updating.

                lastTextureSet = tuModule.currentTextureSet;
                textureSetInitialized = true;
            }
        }

        // B9 fires this in flight when switching subtypes
        private void OnVesselWasModified(Vessel v)
        {
            // Check if this vessel contains our part
            if (v == this.vessel && tuModule != null && !string.IsNullOrEmpty(tuModule.currentTextureSet))
            {
                // Check if texture set actually changed
                if (textureSetInitialized && tuModule.currentTextureSet == lastTextureSet)
                {
                    return;
                }
                
                tuModule.enableTextureSet(tuModule.currentTextureSet, false, false);
                lastTextureSet = tuModule.currentTextureSet;
                textureSetInitialized = true;
            }
        }
		// Manual refresh button on PAW (Part Action Window)
        [KSPEvent(guiActive = true, guiActiveEditor = true, guiName = "Reset Texture")]
        public void RefreshTexturesManual()
        {
            if (tuModule != null && !string.IsNullOrEmpty(tuModule.currentTextureSet))
            {
                UnityEngine.Debug.Log(string.Format("[TU-B9-Bridge] Manual refresh on {0}, refreshing set: {1}", 
                    part.name, tuModule.currentTextureSet));
                tuModule.enableTextureSet(tuModule.currentTextureSet, false, true);
                UnityEngine.Debug.Log("[TU-B9-Bridge] Texture refresh triggered by: manual PAW button");
            }
            else
            {
                UnityEngine.Debug.LogWarning("[TU-B9-Bridge] Cannot refresh: No texture set selected");
            }
        }

        public void OnDestroy()
        {
            // Clean up event subscriptions
            GameEvents.onEditorPartEvent.Remove(OnEditorPartEvent);
            GameEvents.onVesselWasModified.Remove(OnVesselWasModified);
        }
    }
}