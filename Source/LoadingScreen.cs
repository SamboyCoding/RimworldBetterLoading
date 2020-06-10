using System;
using System.Collections.Generic;
using System.Linq;
using BetterLoading.Stage;
using BetterLoading.Stage.InitialLoad;
using UnityEngine;
using Verse;

namespace BetterLoading
{
    public sealed class LoadingScreen : MonoBehaviour
    {
        public static LoadingScreen Instance { get; private set; }

        /// <summary>
        /// The load list used at game boot.
        /// </summary>
        internal static List<LoadingStage> BootLoadList = new List<LoadingStage>
        {
            //For all of these stages, vanilla just shows "..."
            new StageInitMods(BetterLoadingMain.hInstance),
            new StageReadXML(BetterLoadingMain.hInstance),
            new StageUnifyXML(BetterLoadingMain.hInstance),
            new StageApplyPatches(BetterLoadingMain.hInstance),
            new StageRegisterDefs(BetterLoadingMain.hInstance),
            new StageConstructDefs(BetterLoadingMain.hInstance),
            //Only NOW does it show "Loading Defs..."
            new StageResolveDefDatabases(BetterLoadingMain.hInstance),
            //Now it shows "Initializing..."
            new StageRunPostLoadPreFinalizeCallbacks(BetterLoadingMain.hInstance),
            new StageRunStaticCctors(BetterLoadingMain.hInstance),
            new StageRunPostFinalizeCallbacks(BetterLoadingMain.hInstance)
        };
        
        private static Dictionary<Type, LoadingStage> _loadingStagesByType = new Dictionary<Type, LoadingStage>();

        private Texture2D background;
        private Texture2D errorBarColor;
        private Texture2D warningBarColor;

        private LoadingStage _currentStage = BootLoadList[0];

        public bool shouldShow = true;

        public EnumLoadingStage currentStage = EnumLoadingStage.CreateClasses;

        //------------File Loading--------------
        public int numWorldGeneratorsToRun;
        public int numWorldGeneratorsRun;
        public WorldGenStep? currentWorldGenStep;

        public List<Map> maps = new List<Map>();

        public int mapIndexSpawningItems = -1;
        public int numObjectsToSpawnCurrentMap;
        public int numObjectsSpawnedCurrentMap;

        public LoadingScreen()
        {
            Instance = this;
            BootLoadList.ForEach(s => _loadingStagesByType[s.GetType()] = s);
            
            _currentStage.BecomeActive();
            StageTimingData.ExecutedStages.Add(new StageTimingData
            {
                start = DateTime.Now,
                stage = _currentStage
            });
        }
        
        internal static void RegisterStageInstance<T>(T stage) where T: LoadingStage
        {
            try
            {
                GetStageInstance<T>(); //Verify not in dict already
            }
            catch (ArgumentException)
            {
                _loadingStagesByType.Add(typeof(T), stage);
                return;
            }
            
            throw new ArgumentException($"RegisterStageInstance called for an already registered stage type {typeof(T)} (mapped to {GetStageInstance<T>()}).");
        }

        internal static T GetStageInstance<T>() where T: LoadingStage
        {
            if (!_loadingStagesByType.TryGetValue(typeof(T), out var ret))
            {
                throw new ArgumentException($"GetStageInstance called for an unregistered stage type {typeof(T)}.");
            }

            return (T) ret;
        }

        public void Awake()
        {
            Log.Message("[BetterLoading] Injected into main UI.");
        }

        public void OnGUI()
        {
            if (!shouldShow) return;

            if (!LongEventHandler.AnyEventNowOrWaiting)
            {
                Log.Message("[BetterLoading] Long event has finished, hiding loading screen.");
                shouldShow = false;
                return;
            }

            if (background == null)
                background = SolidColorMaterials.NewSolidColorTexture(new Color(0.1f, 0.1f, 0.1f, 1));
            if(warningBarColor == null)
                warningBarColor = SolidColorMaterials.NewSolidColorTexture(new Color(0.89f, 0.8f, 0.11f)); //RGB (226,203,29)
            if (errorBarColor == null)
                errorBarColor = SolidColorMaterials.NewSolidColorTexture(new Color(0.73f, 0.09f, 0.09f)); //RGB(185, 24, 24)

            try
            {
                List<LoadingStage>? currentList = null;
                if (BootLoadList.Contains(_currentStage))
                    currentList = BootLoadList;

                if (currentList == null)
                {
                    Log.Error("[BetterLoading] Current Load Stage is not in a load list!");
                    shouldShow = false;
                    return;
                }

                var idx = currentList.IndexOf(_currentStage);

                //Handle cases where this stage is complete.
                while (_currentStage.IsCompleted())
                {
                    if (idx + 1 >= currentList.Count)
                    {
                        Log.Message("[BetterLoading] Finished processing load list, hiding.");
                        shouldShow = false;
                        return;
                    }

                    //Move to next stage
                    Log.Message($"[BetterLoading] Finished stage {_currentStage.GetStageName()} at {DateTime.Now.ToLongTimeString()}.");
                    try
                    {
                        _currentStage.BecomeInactive();
                    }
                    catch (Exception e)
                    {
                        Log.Error($"[BetterLoading] The stage {_currentStage} errored during BecomeInactive: {e}");
                    }

                    StageTimingData.ExecutedStages.Last().end = DateTime.Now;

                    _currentStage = currentList[idx + 1];
                    try
                    {
                        Log.Message($"[BetterLoading] Starting stage {_currentStage.GetStageName()}.");
                        _currentStage.BecomeActive();
                    }
                    catch (Exception e)
                    {
                        Log.Error($"[BetterLoading] The stage {_currentStage} errored during BecomeActive: {e}");
                    }

                    StageTimingData.ExecutedStages.Add(new StageTimingData
                    {
                        start = DateTime.Now,
                        stage = _currentStage
                    });
                    idx++;
                }

                var currentProgress = _currentStage.GetCurrentProgress();
                var maxProgress = _currentStage.GetMaximumProgress();
                
                if (maxProgress == 0)
                {
                    Log.Warning($"[BetterLoading] The stage {_currentStage.GetType().FullName} returned maxProgress = 0.");
                    maxProgress = 1;
                }
                
                if (currentProgress > maxProgress)
                {
                    Log.Error(
                        $"[BetterLoading] Clamping! The stage of type {_currentStage.GetType().FullName} has returned currentProgress {currentProgress} > maxProgress {maxProgress}. Please report this!",
                        true);
                    currentProgress = maxProgress;
                }

                //Draw background
                var bgRect = new Rect(0, 0, Screen.width, Screen.height);
                GUI.DrawTexture(bgRect, background);
                
                //Draw title
                Text.Font = GameFont.Medium;
                Text.Anchor = TextAnchor.MiddleCenter;

                var titleRect = new Rect(200, 200, Screen.width - 400, 40);
                Widgets.Label(titleRect, "Initializing Game...");

                Text.Font = GameFont.Small;
                
                
                //Render current stage bar and label
                var pct = currentProgress / (float) maxProgress;

                var currentStageText = _currentStage.GetStageName();
                var subStageText = _currentStage.GetCurrentStepName();
                if (subStageText != null)
                    currentStageText = $"{currentStageText} - {subStageText}";

                var rect = new Rect(200, Screen.height - 440, Screen.width - 400, 40);

                var color = _currentStage.HasError() ? errorBarColor : _currentStage.HasWarning() ? warningBarColor : null;

                if (color != null)
                    Widgets.FillableBar(rect, pct, color);
                else
                    Widgets.FillableBar(rect, pct); //use default blue
                
                Widgets.Label(rect, $"{currentProgress}/{maxProgress} ({pct.ToStringPercent()})");
                Text.Anchor = TextAnchor.UpperLeft;

                rect.y += 50;
                Widgets.Label(rect, currentStageText);

                //Render global progress bar.
                rect = new Rect(200, Screen.height - 240, Screen.width - 400, 40);

                Text.Anchor = TextAnchor.MiddleCenter;
                
                pct = (idx + 1) / (float) currentList.Count;
                Widgets.FillableBar(rect, pct);
                Widgets.Label(rect, $"{idx + 1}/{currentList.Count} ({pct.ToStringPercent()})");
                
                Text.Anchor = TextAnchor.UpperLeft;

            }
            catch (Exception e)
            {
                Log.ErrorOnce($"Encountered exception while rendering loading screen: {e}", 0xBEEF99, true);
            }
        }

        private void DrawSaveFileLoad()
        {
            //Draw window
            var rect = new Rect(100, 100, Screen.width - 200, Screen.height - 200);
            rect = rect.Rounded();
            UIMenuBackgroundManager.background.BackgroundOnGUI();
            Widgets.DrawShadowAround(rect);
            Widgets.DrawWindowBackground(rect);

            Text.Font = GameFont.Medium;
            Text.Anchor = TextAnchor.UpperCenter;

            rect.y += 20; //Nudge down for title

            Widgets.Label(rect, "BetterLoading :: Loading Save File...");

            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.UpperLeft;

            rect.x += 20; //Indent
            rect.width -= 20;

            //Stages for this, in terms of actual time consuming events:
            //Scene Load is triggered for "Play" scene, which triggers Verse.Root_Play#Start
            //This then calls QueueLongEvent (Action, string, bool, Action) with the action being call SavedGameLoaderNow.LoadGameFromSaveFileNow - which is probably where the ACTUAL save file logic is.
            //    The event name is "LoadingLongEvent" and it's async
            //If something goes wrong it calls GameAndMapInitExceptionHandlers.ErrorWhileLoadingGame - cleanup this?
            //The key clue that the game load has begun is ScribeLoader#InitLoading being called which buffers the save file into an XmlDocument, and saves it into ScribeLoader.curXmlParent
            //    Scribe.mode is set to LoadingVars once the file is buffered in - but this doesn't have a setter to hook.
            //Once the file is read then Game#LoadGame is called. This is the time consuming bit.
            //
            //Stage one could be "Load Small Components" (Game#ExposeSmallComponents) as this picks up a few small details such as research, the tutor, the scenario, etc.
            //
            //Second stage start coincides with vanilla's "Loading World" and hook could go in World's constructor or World#ExposeData   
            //    This is split up into loading of the world info (i.e. seed, coverage, etc) which is quick
            //    And the grid, which may not be.
            //    Then WorldGenerator.GenerateFromScribe (or MAYBE GenerateWithoutWorldData) will be called
            //        -Progress bar this? We can do a hook in WorldGenStep#GenerateFromScribe/GenerateWithoutWorldData to increment, and the total is equal to WorldGenerator.GenStepsInOrder.Length
            //    Then World#FinalizeInit is called, which recalcs paths and has a callback for all WorldComponents (FinalizeInit method).
            //
            //Next Stage is vanilla's "Loading Map" 
            //    This is deceptively simple as it just calls Map#ExposeData once per map.
            //        First part of this instantiates 80-odd classes and a bunch of MapComponents, but SHOULD be quite quick? (Call to Map#ConstructComponents)
            //        Second part populates all 80 classes and map components with saved data (call to Map#ExposeComponents) 
            //        Third part loads compressed stuff as a byte array (Call to MapFileCompressor#ExposeData)
            //        Fourth part loads uncompressed stuff, and it's a direct call to Scribe_Collections#Look
            //    It then sets the current map index
            //
            //Next is vanilla's "Initializing Game" which actually just loads the camera pos (CameraDriver#Expose)
            //
            //Next is "Finalize Loading" which resolves cross references, sets the current load mode to Inactive, and calls post-load callbacks
            //
            //Then "Spawning All Things"
            //    This first off calls Map#FinalizeLoading for each map, which:
            //        Merges Map#loadedFullThings and MapCompressor#ThingsToSpawnAfterLoad()'s return value (hook said after load method?)
            //        Actually spawns stuff
            //            GenSpawn.Spawn for every non-building item in the new list (get total based on size of the two?)
            //            GenSpawn#SpawnBuildingAsPossible for every building in same list.
            //            Then calls GenPlace#TryPlaceThing for a few things if loading an older version
            //            Finally calls Map#FinalizeInit which recalculates and rebuilds a bunch of stuff and calls PostMapInit for each
            //
            //Finally Game#FinalizeInit is called, which flushes the log file, applies research mods, and calls GameComponent#FinalizeInit for each component in Current.Game.components

            //----------------Load Small Components------------
            rect.y += 50;
            Widgets.Label(rect,
                currentStage == EnumLoadingStage.LoadSmallComponents
                    ? "Loading Misc Game Data..."
                    : "Basic Game Data Loaded");

            //bar
            var barRect = new Rect(rect.x, rect.y + 25, rect.width - 24, 20);
            Widgets.FillableBar(barRect, currentStage == EnumLoadingStage.LoadSmallComponents ? 0 : 1);

            //----------------Load World Map------------
            rect.y += 50;
            Widgets.Label(rect,
                currentStage < EnumLoadingStage.LoadWorldMap ? "Waiting for game data load..." :
                currentStage == EnumLoadingStage.LoadWorldMap ? "Loading World Map..." : "World Map Loaded");

            //bar
            barRect = new Rect(rect.x, rect.y + 25, rect.width - 24, 20);
            Widgets.FillableBar(barRect, currentStage <= EnumLoadingStage.LoadWorldMap ? 0 : 1);

            //----------------Generate World Features------------
            rect.y += 50;
            Widgets.Label(rect, currentStage < EnumLoadingStage.GenerateWorldData
                ? "Waiting for world map..."
                : currentStage == EnumLoadingStage.GenerateWorldData
                    ? $"Generating World Feature: {currentWorldGenStep} ({numWorldGeneratorsRun}/{numWorldGeneratorsToRun})"
                    : "World Features Generated");

            //bar
            barRect = new Rect(rect.x, rect.y + 25, rect.width - 24, 20);
            Widgets.FillableBar(barRect,
                numWorldGeneratorsToRun == 0 ? 0 : (float) numWorldGeneratorsRun / numWorldGeneratorsToRun);

            //----------------Finalizing World------------
            rect.y += 50;
            Widgets.Label(rect, currentStage < EnumLoadingStage.FinalizeWorld
                ? "Waiting for feature generation..."
                : currentStage == EnumLoadingStage.FinalizeWorld
                    ? "Applying finishing touches to world..."
                    : "World Finalized.");

            //bar
            barRect = new Rect(rect.x, rect.y + 25, rect.width - 24, 20);
            Widgets.FillableBar(barRect, currentStage <= EnumLoadingStage.FinalizeWorld ? 0 : 1);

            //----------------Map Loading------------
            rect.y += 50;

            if (currentStage >= EnumLoadingStage.LoadMaps_ConstructComponents)
            {
                if (currentStage <= EnumLoadingStage.LoadMaps_LoadData)
                {
                    Widgets.Label(rect, "Loading Maps...");
                    barRect = new Rect(rect.x, rect.y + 25, rect.width - 24, 20);
                    Widgets.FillableBar(barRect, 0.2f);
                }
                else
                {
                    Widgets.Label(rect, "Maps Loaded");
                    barRect = new Rect(rect.x, rect.y + 25, rect.width - 24, 20);
                    Widgets.FillableBar(barRect, 1);
                }

                rect.y += 50;
                rect.x += 25; //Indent
                rect.width -= 25;

                var num = 0;
                foreach (var unused in maps)
                {
                    if (num < maps.Count - 1 || currentStage > EnumLoadingStage.LoadMaps_LoadData)
                    {
                        //This map is loaded fully
                        Widgets.Label(rect, "Map " + (num + 1) + ": Loaded");

                        //bar
                        barRect = new Rect(rect.x, rect.y + 25, rect.width - 24, 20);
                        Widgets.FillableBar(barRect, 1);
                    }
                    else
                    {
                        //This map is partially loaded
                        Widgets.Label(rect,
                            "Map " + (num + 1) + ": " + (currentStage == EnumLoadingStage.LoadMaps_ConstructComponents
                                ? "Constructing Components..."
                                : currentStage == EnumLoadingStage.LoadMaps_LoadComponents
                                    ? "Loading Misc Map Details..."
                                    : "Reading Object Data..."));

                        barRect = new Rect(rect.x, rect.y + 25, rect.width - 24, 20);
                        Widgets.FillableBar(barRect,
                            (float) (currentStage + 1 - EnumLoadingStage.LoadMaps_ConstructComponents) / 5);
                    }

                    num++;
                    rect.y += 50;
                }

                rect.x -= 25; //Unindent
                rect.width += 25;
            }
            else if (currentStage < EnumLoadingStage.LoadMaps_LoadComponents)
            {
                Widgets.Label(rect, "Waiting for map data...");
                barRect = new Rect(rect.x, rect.y + 25, rect.width - 24, 20);
                Widgets.FillableBar(barRect, 0);
            }

            //----------------Init Camera------------
            Widgets.Label(rect, currentStage < EnumLoadingStage.InitCamera
                ? "Waiting for maps to finish loading..."
                : currentStage == EnumLoadingStage.InitCamera
                    ? "Setting up camera..."
                    : "Camera Setup Complete");

            //bar
            barRect = new Rect(rect.x, rect.y + 25, rect.width - 24, 20);
            Widgets.FillableBar(barRect, currentStage <= EnumLoadingStage.InitCamera ? 0 : 1);

            //----------------Resolve Cross-References------------
            rect.y += 50;
            Widgets.Label(rect, currentStage < EnumLoadingStage.ResolveSaveFileCrossReferences
                ? "Waiting for camera setup..."
                : currentStage == EnumLoadingStage.ResolveSaveFileCrossReferences
                    ? "Resolving Def Cross-References..."
                    : "Defs Successfully Cross-Referenced");

            //bar
            barRect = new Rect(rect.x, rect.y + 25, rect.width - 24, 20);
            Widgets.FillableBar(barRect, currentStage <= EnumLoadingStage.ResolveSaveFileCrossReferences ? 0 : 1);

            //----------------Spawning All Things------------
            rect.y += 50;


            if (currentStage > EnumLoadingStage.SpawnThings_RebuildRecalc)
            {
                Widgets.Label(rect, "Things Spawned");
                barRect = new Rect(rect.x, rect.y + 25, rect.width - 24, 20);
                Widgets.FillableBar(barRect, 1);
            }
            else if (currentStage >= EnumLoadingStage.SpawnThings_NonBuildings)
            {
                Widgets.Label(rect, "Spawning all things...");
                barRect = new Rect(rect.x, rect.y + 25, rect.width - 24, 20);
                Widgets.FillableBar(barRect, (mapIndexSpawningItems + 1f) / (maps.Count + 1f));
            }
            else
            {
                Widgets.Label(rect, "Waiting for defs to be cross-referenced...");
                barRect = new Rect(rect.x, rect.y + 25, rect.width - 24, 20);
                Widgets.FillableBar(barRect, 0);
            }

            rect.y += 50;
            rect.x += 25; //Indent
            rect.width -= 25;

            var num2 = 0;
            foreach (var unused in maps)
            {
                if (num2 < mapIndexSpawningItems || currentStage > EnumLoadingStage.SpawnThings_RebuildRecalc)
                {
                    //This map is loaded fully
                    Widgets.Label(rect, "Map " + (num2 + 1) + ": Everything Spawned");

                    //bar
                    barRect = new Rect(rect.x, rect.y + 25, rect.width - 24, 20);
                    Widgets.FillableBar(barRect, 1);
                }
                else if (num2 == mapIndexSpawningItems)
                {
                    //This map is partially loaded
                    Widgets.Label(rect,
                        "Map " + (num2 + 1) + ": " + (currentStage == EnumLoadingStage.SpawnThings_NonBuildings
                            ? "Spawning Items..."
                            : currentStage == EnumLoadingStage.SpawnThings_Buildings
                                ? "Spawning Buildings..."
                                : currentStage == EnumLoadingStage.SpawnThings_BackCompat
                                    ? "Upgrading Level Format..."
                                    : "Rebuilding & Recalculating Pathfinding Map etc..."));

                    barRect = new Rect(rect.x, rect.y + 25, rect.width - 24, 20);
                    Widgets.FillableBar(barRect,
                        (float) (currentStage + 1 - EnumLoadingStage.SpawnThings_NonBuildings) / 5);
                }
                else
                {
                    //This map is not yet loaded
                    Widgets.Label(rect, "Map " + (num2 + 1) + ": Waiting...");

                    //bar
                    barRect = new Rect(rect.x, rect.y + 25, rect.width - 24, 20);
                    Widgets.FillableBar(barRect, 0);
                }

                num2++;
                rect.y += 50;
            }

            rect.x -= 25; //Unindent
            rect.width += 25;

            //----------------Finalize Load------------
            Widgets.Label(rect, currentStage < EnumLoadingStage.FinalizeLoad
                ? "Waiting for things to finish spawning..."
                : currentStage == EnumLoadingStage.FinalizeLoad
                    ? "Finalizing Game State..."
                    : "Load Complete.");

            //bar
            barRect = new Rect(rect.x, rect.y + 25, rect.width - 24, 20);
            Widgets.FillableBar(barRect, currentStage <= EnumLoadingStage.FinalizeLoad ? 0 : 1);
        }
    }
}