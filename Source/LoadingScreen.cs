using BetterLoading.Stage;
using BetterLoading.Stage.InitialLoad;
using System;
using System.Collections.Generic;
using System.Linq;
using BetterLoading.Stage.SaveLoad;
using UnityEngine;
using Verse;

namespace BetterLoading
{
    public sealed class LoadingScreen : MonoBehaviour
    {
        public static LoadingScreen? Instance { get; private set; }
        /// <summary>
        /// The load list used at game boot.
        /// </summary>
        internal static List<LoadingStage> BootLoadList = new()
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
        
        /// <summary>
        /// The load list used during loading a save (Game.LoadGame)
        ///
        /// Stage 0 - Load Small Components. Initial load up until beginning of World.ExposeData.
        /// Stage 1 - Load World Map. Covers World.ExposeData and the subsequent call to World.FinalizeInit from Game.LoadGame
        /// Stage 2 - Load Maps. Covers Map.ExposeData for each map in the save
        /// Stage 3 - Finalize Scribe Load. Covers Scribe.loader.FinalizeLoading, which essentially just calls ResolveAllCrossReferences and DoAllPostLoadInits.
        /// Stage 4 - Finalize Maps. Covers Map.FinalizeLoading (which is called directly for each map from Game.LoadGame). Ends when FinalizeLoading returns on the last map.
        /// Stage 5 - Finalize Game State. Covers everything from the end of FinalizeLoading on the last map, until the long event finishes, which in practice is Game.FinalizeInit and GameComponentUtility.LoadedGame
        /// </summary>
        internal static List<LoadingStage> LoadSaveFileLoadList = new()
        {
            //For all of these stages, vanilla just shows "..."
            new LoadSmallComponents(BetterLoadingMain.hInstance),
            new LoadWorldMap(BetterLoadingMain.hInstance),
            new LoadMaps(BetterLoadingMain.hInstance),
            new FinalizeScribeLoad(BetterLoadingMain.hInstance),
            new FinalizeMap(BetterLoadingMain.hInstance),
            new FinalizeGameState(BetterLoadingMain.hInstance)
        };
        
        private static Dictionary<Type, LoadingStage> _loadingStagesByType = new();

        public Texture2D? Background;
        private Texture2D? _errorBarColor;
        private Texture2D? _warningBarColor;
        private Texture2D? _loadingBarBgColor;
        private Texture2D? _loadingBarDefaultColor;
        private Texture2D? _loadingBarWhiteColor;

        private LoadingStage _currentStage = BootLoadList[0];

        public bool shouldShow = true;

        private float totalLoadLerpSpeed = 1.5f;
        private float stageLoadLerpSpeed = 3.0f;
        private float _totalLoadPercentLerp;
        private float _stageLoadPercentLerp;
        private float _bgLerp;
        private Texture2D? _bgSolidColor;
        private Texture2D? _bgContrastReducer;

        public void StartSaveLoad()
        {
            Log.Message("[BetterLoading] Game Load detected, re-showing for save-load screen.");
            _currentStage = LoadSaveFileLoadList[0];
            shouldShow = true;
        }

        public LoadingScreen()
        {
            Instance = this;
            BootLoadList.ForEach(s => _loadingStagesByType[s.GetType()] = s);
            
            _currentStage.BecomeActive();
            StageTimingData.ExecutedStages.Add(new(DateTime.Now, _currentStage));
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

        private void DrawBackground()
        {
            const float targetDarkness = 0.25f;

            _bgContrastReducer ??= SolidColorMaterials.NewSolidColorTexture(new(1, 1, 1, 1));
            _bgSolidColor ??= SolidColorMaterials.NewSolidColorTexture(new(0.1f, 0.1f, 0.1f, 1));

            if (Background == null)
            {
                var bgRect = new Rect(0, 0, Screen.width, Screen.height);
                GUI.DrawTexture(bgRect, _bgSolidColor);
                return;
            }

            var size = new Vector2(Background.width, Background.height);
            var flag = !(Screen.width > Screen.height * (size.x / size.y));
            Rect rect;
            if (flag)
            {
                float height = Screen.height;
                var num = Screen.height * (size.x / size.y);
                rect = new((Screen.width * 0.5f) - num / 2f, 0f, num, height);
            }
            else
            {
                float width = Screen.width;
                var num2 = Screen.width * (size.y / size.x);
                rect = new(0f, (Screen.height * 0.5f) - num2 / 2f, width, num2);
            }

            // From the moment the loading screen spawns, darken the background gradually.
            _bgLerp = Mathf.MoveTowards(_bgLerp, 1f, Time.deltaTime * Mathf.Abs(1f - _bgLerp) * 0.5f);
            var bgDarkness = Mathf.Lerp(1f, targetDarkness, _bgLerp);

            var oldCol = GUI.color;

            //Draw default rimworld loading background.
            GUI.color = new(bgDarkness, bgDarkness, bgDarkness, 1f);
            GUI.DrawTexture(rect, Background, ScaleMode.ScaleToFit);

            // Draw solid color, with transparency - it's the easiest way to reduce background contrast.
            const float graynessLevel = 0.2f;
            var alpha = Mathf.Lerp(0f, 0.5f, _bgLerp);
            GUI.color = new(graynessLevel, graynessLevel, graynessLevel, alpha);
            var rect2 = new Rect(0, 0, Screen.width, Screen.height);
            GUI.DrawTexture(rect2, _bgContrastReducer);

            GUI.color = oldCol;
        }

        public void OnGUI()
        {
            if (!shouldShow) return;

            if (!LongEventHandler.AnyEventNowOrWaiting)
            {
                Log.Message("[BetterLoading] Long event has finished, hiding loading screen.");
                shouldShow = false;
                
                if(BootLoadList.Contains(_currentStage))
                    BetterLoadingApi.DispatchLoadComplete();
                return;
            }

            if (_warningBarColor == null)
            {
                _warningBarColor = SolidColorMaterials.NewSolidColorTexture(new(0.89f, 0.8f, 0.11f)); //RGB (226,203,29)
                _errorBarColor = SolidColorMaterials.NewSolidColorTexture(new(0.73f, 0.09f, 0.09f)); //RGB(185, 24, 24)
                _loadingBarBgColor = SolidColorMaterials.NewSolidColorTexture(new(0.5f, 0.5f, 0.5f, 1f));
                _loadingBarDefaultColor = SolidColorMaterials.NewSolidColorTexture(new(0.2f, 0.8f, 0.85f));
                _loadingBarWhiteColor = SolidColorMaterials.NewSolidColorTexture(new(1f, 1f, 1f, 1f));
            }

            try
            {
                List<LoadingStage>? currentList = null;
                if (BootLoadList.Contains(_currentStage))
                    currentList = BootLoadList;
                else if (LoadSaveFileLoadList.Contains(_currentStage))
                    currentList = LoadSaveFileLoadList;

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
                        BetterLoadingApi.DispatchLoadComplete();
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

                    StageTimingData.ExecutedStages.Last().End = DateTime.Now;

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

                    StageTimingData.ExecutedStages.Add(new(DateTime.Now, _currentStage));
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
                    // Log.Error(
                    //     $"[BetterLoading] Clamping! The stage of type {_currentStage.GetType().FullName} has returned currentProgress {currentProgress} > maxProgress {maxProgress}. Please report this!");
                    currentProgress = maxProgress;
                }

                //Draw background
                DrawBackground();

                //Draw title
                Text.Font = GameFont.Medium;
                Text.Anchor = TextAnchor.MiddleCenter;

                // Heights of the two loading bars: center of screen.
                var globalBarHeight = Screen.height * 0.5f;
                var currentBarHeight = Screen.height * 0.5f + 64f;
                var titleHeight = Screen.height * 0.5f - 80f;

                // Get names of stage and step.
                var currentStageText = _currentStage.GetStageName();
                var subStageText = _currentStage.GetCurrentStepName();

                var titleRect = new Rect(200, titleHeight, Screen.width - 400, 46);
                var subTitleRect = new Rect(200, titleHeight + 36, Screen.width - 400, 46);
                Widgets.Label(titleRect, "<size=35><b>Loading...</b></size>");
                Widgets.Label(subTitleRect, $"<size=16><i>Stage {idx + 1} of {currentList.Count}: {currentStageText}</i></size>");

                Text.Font = GameFont.Small;

                //Render current stage bar and label

                // Interpolate progress bar, to make it a little smoother.
                // Also clamp between 1% and 100% (there was a bug where pct was < 0)
                var pct = Mathf.Clamp(currentProgress / (float) maxProgress, 0.01f, 1f);
                var lerpScalar = 1f;
                if (pct < _stageLoadPercentLerp)
                    lerpScalar = 3f;
                var dst = Mathf.Abs(pct - _stageLoadPercentLerp);
                _stageLoadPercentLerp = Mathf.MoveTowards(_stageLoadPercentLerp, pct, Time.deltaTime * dst * stageLoadLerpSpeed * lerpScalar);
                
                if (subStageText != null)
                    currentStageText = $"{subStageText}";

                var rect = new Rect(450, currentBarHeight, Screen.width - 900, 26);

                var color = _currentStage.HasError() ? _errorBarColor : _currentStage.HasWarning() ? _warningBarColor : null;

                GUI.DrawTexture(rect.ExpandedBy(2), _loadingBarWhiteColor);
                Widgets.FillableBar(rect, _stageLoadPercentLerp, color != null ? color : _loadingBarDefaultColor, _loadingBarBgColor, false);

                Widgets.Label(rect, $"<color=black><b>{pct.ToStringPercent()}</b>, {currentProgress} of {maxProgress}</color>");

                // Draw current step item.
                rect.y += 40;
                rect.height += 100; //Allow for wrapping
                Text.Anchor = TextAnchor.UpperCenter;
                Widgets.Label(rect, "Current: " + currentStageText);

                //Draw loading lines
                rect.height += 100;
                Text.Font = GameFont.Medium;
                rect.y += 120;
                Widgets.Label(rect, LoadingScreenTipManager.GetTipToDisplay());
                Text.Font = GameFont.Small;

                rect.height -= 200; //Remove increased height

                //Render global progress bar.
                rect = new(200, globalBarHeight, Screen.width - 400, 36);

                Text.Anchor = TextAnchor.MiddleCenter;

                // Takes the current stage progress to give a more accurate percentage.
                pct = Mathf.Clamp01((idx + 1) / (float) currentList.Count + currentProgress / (float)maxProgress * 1f / currentList.Count);
                dst = Mathf.Abs(pct - _totalLoadPercentLerp);
                _totalLoadPercentLerp = Mathf.MoveTowards(_totalLoadPercentLerp, pct, Time.deltaTime * dst * totalLoadLerpSpeed);
                GUI.DrawTexture(rect.ExpandedBy(2), _loadingBarWhiteColor);
                Widgets.FillableBar(rect, _totalLoadPercentLerp, _loadingBarDefaultColor, _loadingBarBgColor, false);
                Widgets.Label(rect, $"<color=black><b>{pct.ToStringPercent()}</b></color>");
                
                Text.Anchor = TextAnchor.UpperLeft;

            }
            catch (Exception e)
            {
                Log.ErrorOnce($"Encountered exception while rendering loading screen: {e}", 0xBEEF99);
            }
        }
    }
}