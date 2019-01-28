using System;
using UnityEngine;
using Verse;
using Harmony;

namespace BetterLoading
{
    public class LoadingScreenManager : MonoBehaviour
    {
        public bool shouldShow = true;

        public LoadingStage currentStage = LoadingStage.CreateClasses;

        public ModContentPack currentlyLoadingDefsFrom;
        public int totalLoadedContentPacks;
        public int numContentPacksLoaded;

        public int numPatchesToLoad;
        public int numPatchesLoaded;
        public ModContentPack currentlyPatching;

        public int numDefsToPreProcess;
        public int numDefsPreProcessed;
        
        public int numDefsToProcess;
        public int numDefsProcessed;

        public int numDefDatabases;
        public int numDatabasesReloaded;
        public Type currentDatabaseResolving;

        public int numStaticConstructorsToCall;
        public int numStaticConstructorsCalled;
        public Type currentStaticConstructor;

        public LoadingScreenManager()
        {
            Log.Message("LoadingScreenManager :: Init");
        }

        public void OnGUI()
        {
            //In terms of stages, we have:
            //
            //Initialize mods - this just verifies files exist etc and is pre-instantiation so cannot be shown
            //
            //Load mod content - this loads assemblies and schedules the load of audio clips, textures, and strings. Again, pre-instantiation.
            //
            //Create mod classes (that's where this gets setup, so it's unlikely that we'll be able to display this fully/at all)
            //
            //Loading of xml files in defs folder (LoadedModManager#LoadModXML) - can be displayed as a progress bar
            //
            //XML Unification (LoadedModManager#CombineIntoUnifiedXML) - may be doable as a progress bar, may just be easier to show it's being done
            //
            //Patch application. Loaded per mod in ModContentPack#LoadPatches and then applied in blocks (a mod at a time) by PatchOperation#Apply (but this is overridden, so... does harmony work?)
            //    This runs as Load Mod Patches -> Apply one at a time -> load next mod -> apply -> etc
            //
            //Parse + Process XML. Two time consuming stages:
            //    - Register all inheritence nodes (XMLInheritence#TryRegister for each xmlDoc.DocumentElement.ChildNodes in param for LoadedModManager#ParseAndProcessXML)
            //    - Addition of Defs - DirectXmlLoader#DefFromNode for each def followed OPTIONALLY by DefPackage#AddDef if it loads (which not all of even the vanilla ones do).
            //
            //Freeing of memory (probably don't need to show) via LoadedModManager#ClearCachedPatches and XmlInheritance#Clear

            if (!LongEventHandler.AnyEventNowOrWaiting)
            {
                shouldShow = false;
            }
            
            if(!shouldShow) return;

            //Draw window
            var rect = new Rect(100, 100, UI.screenWidth - 200, UI.screenHeight - 200);
            rect = rect.Rounded();
            UIMenuBackgroundManager.background.BackgroundOnGUI();
            Widgets.DrawShadowAround(rect);
            Widgets.DrawWindowBackground(rect);

            Text.Font = GameFont.Medium;
            Text.Anchor = TextAnchor.UpperCenter;

            rect.y += 20; //Nudge down for title

            Widgets.Label(rect, "BetterLoading :: Game Loading, Please Wait...");

            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.UpperLeft;

            rect.x += 20; //Indent
            rect.width -= 20;

            //------------------------Mod Construction------------------------
            rect.y += 50; //Move down a bit
            Widgets.Label(rect, currentStage == LoadingStage.CreateClasses ? "Constructing Mods" : "Mods Constructed");
            
            //Draw a bar
            var barRect = new Rect(rect.x, rect.y + 25, rect.width - 24, 20);
            Widgets.FillableBar(barRect, currentStage == LoadingStage.CreateClasses ? 0 : 1);

            //------------------------Def XML Reading------------------------
            rect.y += 50;
            Widgets.Label(rect,
                $"Reading Def XML ({numContentPacksLoaded}/{(totalLoadedContentPacks == 0 ? "<waiting>" : "" + totalLoadedContentPacks)}): {(currentlyLoadingDefsFrom != null && currentlyLoadingDefsFrom.Name.Length > 0 ? currentlyLoadingDefsFrom.Name : "Waiting...")}");
            
            //Draw a bar
            barRect = new Rect(rect.x, rect.y + 25, rect.width - 24, 20);
            Widgets.FillableBar(barRect,
                totalLoadedContentPacks == 0 ? 0 : (float) numContentPacksLoaded / totalLoadedContentPacks);

            //------------------------XML Unification------------------------
            rect.y += 50;
            Widgets.Label(rect,
                currentStage < LoadingStage.UnifyXML ? "Waiting for XML Load To Complete..." :
                currentStage == LoadingStage.UnifyXML ? "Building Def Tree" : "Finished Building Def Tree");
            
            //Draw a bar
            barRect = new Rect(rect.x, rect.y + 25, rect.width - 24, 20);
            Widgets.FillableBar(barRect, currentStage <= LoadingStage.UnifyXML ? 0 : 1);

            //------------------------Patch Application------------------------
            rect.y += 50;
            Widgets.Label(rect,
                currentStage < LoadingStage.ApplyPatches ? "Waiting for XML Tree..." :
                currentStage == LoadingStage.ApplyPatches ? $"Applying mod patches ({numPatchesLoaded}/{numPatchesToLoad}): {(currentlyPatching == null ? "<waiting>" : currentlyPatching.Name)}"
                : "Patches Applied");
            
            //Draw a bar
            barRect = new Rect(rect.x, rect.y + 25, rect.width - 24, 20);
            Widgets.FillableBar(barRect, currentStage < LoadingStage.ApplyPatches ? 0 : numPatchesLoaded / (float) numPatchesToLoad);

            //------------------------XML Parse/Process Stage 1------------------------
            rect.y += 50;
            Widgets.Label(rect, currentStage < LoadingStage.ParseProcessXMLStage1
                ? "Waiting for patches to be applied..."
                : currentStage == LoadingStage.ParseProcessXMLStage1
                    ? $"Registering Defs from Patched XML: {numDefsPreProcessed}/{numDefsToPreProcess} ({((float) numDefsPreProcessed / numDefsToPreProcess).ToStringPercent()})"
                    : "Defs Registered");
            
            barRect = new Rect(rect.x, rect.y + 25, rect.width - 24, 20);
            Widgets.FillableBar(barRect,
                numDefsToPreProcess == 0 ? 0 : (float) numDefsPreProcessed / numDefsToPreProcess);
            
            //------------------------XML Parse/Process Stage 2------------------------
            rect.y += 50;
            Widgets.Label(rect, currentStage < LoadingStage.ParseProcessXMLStage2
                ? "Waiting for defs to be registered..."
                : currentStage == LoadingStage.ParseProcessXMLStage2
                    ? $"Creating Defs from Patched XML: {numDefsProcessed}/{numDefsToProcess} ({((float) numDefsProcessed / numDefsToProcess).ToStringPercent()})"
                    : "Defs Created");
            
            barRect = new Rect(rect.x, rect.y + 25, rect.width - 24, 20);
            Widgets.FillableBar(barRect,
                numDefsToProcess == 0 ? 0 : (float) numDefsProcessed / numDefsToProcess);
            
            //------------------------Reference Resolving------------------------
            rect.y += 50;
            Widgets.Label(rect, currentStage < LoadingStage.ResolveReferences
                ? "Waiting for defs to be created..."
                : currentStage == LoadingStage.ResolveReferences
                    ? $"Reloading DefDatabase: {currentDatabaseResolving.Name} ({numDatabasesReloaded}/{numDefDatabases})"
                    : "Databases Reloaded");
            
            barRect = new Rect(rect.x, rect.y + 25, rect.width - 24, 20);
            Widgets.FillableBar(barRect,
                numDefDatabases == 0 ? 0 : (float) numDatabasesReloaded / numDefDatabases);

            //------------------------Finishing Up------------------------
            rect.y += 50;
            Widgets.Label(rect,
                currentStage < LoadingStage.FinishUp ? "Waiting for databases to finish reload..." :
                currentStage == LoadingStage.FinishUp ? $"Running Startup Static CCtors: {currentStaticConstructor?.FullName} ({numStaticConstructorsCalled}/{numStaticConstructorsToCall})" : "Finished");
            
            barRect = new Rect(rect.x, rect.y + 25, rect.width - 24, 20);
            Widgets.FillableBar(barRect,
                numStaticConstructorsToCall == 0 ? 0 : (float) numStaticConstructorsCalled/ numStaticConstructorsToCall);

            Text.Anchor = TextAnchor.UpperLeft; //Reset this
        }
    }
}