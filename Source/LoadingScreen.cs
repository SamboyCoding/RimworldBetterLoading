using BetterLoading.Stage;
using BetterLoading.Stage.InitialLoad;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BetterLoading.Stage.SaveLoad;
using UnityEngine;
using Verse;

namespace BetterLoading
{
    public sealed class LoadingScreen : MonoBehaviour
    {
        public static LoadingScreen Instance { get; private set; }

        private static string _cachedLoadingTipsPath = Path.Combine(GenFilePaths.ConfigFolderPath, "BetterLoading_Cached_Tips");

        private static bool _tipsAvailable;

        private static List<string> _tips = File.Exists(_cachedLoadingTipsPath) ? File.ReadAllText(_cachedLoadingTipsPath).Split('\0').ToList() : new List<string>();
        private static string? _currentTip;
        private static long _timeLastTipShown;
        private const int _ticksPerTip = 5 * 10_000_000; //3 seconds

        /// <summary>
        /// The load list used at game boot.
        /// </summary>
        internal static List<LoadingStage> BootLoadList = new List<LoadingStage>
        {
            //For all of these stages, vanilla just shows "..."
            new StageInitMods(BetterLoadingMain.hInstance!),
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
        /// The load list used at game boot.
        /// </summary>
        internal static List<LoadingStage> LoadSaveFileLoadList = new List<LoadingStage>
        {
            //For all of these stages, vanilla just shows "..."
            new LoadSmallComponents(BetterLoadingMain.hInstance!),
            new LoadWorldMap(BetterLoadingMain.hInstance),
            new LoadMaps(BetterLoadingMain.hInstance),
            new FinalizeScribeLoad(BetterLoadingMain.hInstance),
            new SpawnAllThings(BetterLoadingMain.hInstance),
            new FinalizeGameState(BetterLoadingMain.hInstance)
        };
        
        private static Dictionary<Type, LoadingStage> _loadingStagesByType = new Dictionary<Type, LoadingStage>();

        public Texture2D? Background;
        private Texture2D errorBarColor;
        private Texture2D warningBarColor;
        private Texture2D loadingBarBgColor;
        private Texture2D loadingBarDefaultColor;
        private Texture2D loadingBarWhiteColor;

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

        private float totalLoadLerpSpeed = 1.5f;
        private float stageLoadLerpSpeed = 3.0f;
        private float totalLoadPercentLerp;
        private float stageLoadPercentLerp;
        private float bgLerp = 0f;
        private Texture2D? bgSolidColor;
        private Texture2D? bgContrastReducer;

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
            _tipsAvailable = _tips.Count > 0;
        }

        private void DrawBG()
        {
            const float TARGET_DARKNESS = 0.25f;
            const bool SOLID_COLOR_BG = false;

            bgContrastReducer ??= SolidColorMaterials.NewSolidColorTexture(new Color(1, 1, 1, 1));
            bgSolidColor ??= SolidColorMaterials.NewSolidColorTexture(new Color(0.1f, 0.1f, 0.1f, 1));

            if (SOLID_COLOR_BG || this.Background == null)
            {
                var bgRect = new Rect(0, 0, Screen.width, Screen.height);
                GUI.DrawTexture(bgRect, bgSolidColor);
                return;
            }

            var size = new Vector2(Background.width, Background.height);
            var flag = !(Screen.width > Screen.height * (size.x / size.y));
            Rect rect;
            if (flag)
            {
                float height = Screen.height;
                var num = Screen.height * (size.x / size.y);
                rect = new Rect((Screen.width * 0.5f) - num / 2f, 0f, num, height);
            }
            else
            {
                float width = Screen.width;
                var num2 = Screen.width * (size.y / size.x);
                rect = new Rect(0f, (Screen.height * 0.5f) - num2 / 2f, width, num2);
            }

            // From the moment the loading screen spawns, darken the background gradually.
            bgLerp = Mathf.MoveTowards(bgLerp, 1f, Time.deltaTime * Mathf.Abs(1f - bgLerp) * 0.5f);
            var bgDarkness = Mathf.Lerp(1f, TARGET_DARKNESS, bgLerp);

            var oldCol = GUI.color;

            //Draw default rimworld loading background.
            GUI.color = new Color(bgDarkness, bgDarkness, bgDarkness, 1f);
            GUI.DrawTexture(rect, Background, ScaleMode.ScaleToFit);

            // Draw solid color, with transparency - it's the easiest way to reduce background contrast.
            const float COL = 0.2f;
            var alpha = Mathf.Lerp(0f, 0.5f, bgLerp);
            GUI.color = new Color(COL, COL, COL, alpha);
            var rect2 = new Rect(0, 0, Screen.width, Screen.height);
            GUI.DrawTexture(rect2, bgContrastReducer);

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

            if (warningBarColor == null)
            {
                warningBarColor = SolidColorMaterials.NewSolidColorTexture(new Color(0.89f, 0.8f, 0.11f)); //RGB (226,203,29)
                errorBarColor = SolidColorMaterials.NewSolidColorTexture(new Color(0.73f, 0.09f, 0.09f)); //RGB(185, 24, 24)
                loadingBarBgColor = SolidColorMaterials.NewSolidColorTexture(new Color(0.5f, 0.5f, 0.5f, 1f));
                loadingBarDefaultColor = SolidColorMaterials.NewSolidColorTexture(new Color(0.2f, 0.8f, 0.85f));
                loadingBarWhiteColor = SolidColorMaterials.NewSolidColorTexture(new Color(1f, 1f, 1f, 1f));
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
                        $"[BetterLoading] Clamping! The stage of type {_currentStage.GetType().FullName} has returned currentProgress {currentProgress} > maxProgress {maxProgress}. Please report this!");
                    currentProgress = maxProgress;
                }

                //Draw background
                DrawBG();

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
                if (pct < stageLoadPercentLerp)
                    lerpScalar = 3f;
                var dst = Mathf.Abs(pct - stageLoadPercentLerp);
                stageLoadPercentLerp = Mathf.MoveTowards(stageLoadPercentLerp, pct, Time.deltaTime * dst * stageLoadLerpSpeed * lerpScalar);
                
                if (subStageText != null)
                    currentStageText = $"{subStageText}";

                var rect = new Rect(450, currentBarHeight, Screen.width - 900, 26);

                var color = _currentStage.HasError() ? errorBarColor : _currentStage.HasWarning() ? warningBarColor : null;

                GUI.DrawTexture(rect.ExpandedBy(2), loadingBarWhiteColor);
                Widgets.FillableBar(rect, stageLoadPercentLerp, color != null ? color : loadingBarDefaultColor, loadingBarBgColor, false);

                Widgets.Label(rect, $"<color=black><b>{pct.ToStringPercent()}</b>, {currentProgress} of {maxProgress}</color>");

                // Draw current step item.
                rect.y += 40;
                rect.height += 100; //Allow for wrapping
                Text.Anchor = TextAnchor.UpperCenter;
                Widgets.Label(rect, "Current: " + currentStageText);

                //Draw loading lines
                Text.Font = GameFont.Medium;
                rect.y += 120;
                if (!_tipsAvailable)
                {
                    Widgets.Label(rect, "Gameplay tips will be shown here once the game loads them (after stage 7 completes)");
                }
                else
                {
                    //Load tips if required
                    if (_currentTip == null || (DateTime.Now.Ticks - _timeLastTipShown) >= _ticksPerTip)
                    {
                        //No tip chosen yet, or time for next tip - pick another and reset timer.

                        if (_tips.NullOrEmpty())
                        {
                            _currentTip = "BetterLoading Warning: No tips could be located in your game. This is probably a bug with another mod";
                        }
                        else
                        {
                            _currentTip = _tips.Pop();
                        }

                        _timeLastTipShown = DateTime.Now.Ticks;
                    }
                    
                    //Draw tip.
                    Widgets.Label(rect, _currentTip);
                }
                Text.Font = GameFont.Small;

                rect.height -= 100; //Remove increased height

                //Render global progress bar.
                rect = new Rect(200, globalBarHeight, Screen.width - 400, 36);

                Text.Anchor = TextAnchor.MiddleCenter;

                // Takes the current stage progress to give a more accurate percentage.
                pct = Mathf.Clamp01((idx + 1) / (float) currentList.Count + currentProgress / (float)maxProgress * 1f / currentList.Count);
                dst = Mathf.Abs(pct - totalLoadPercentLerp);
                totalLoadPercentLerp = Mathf.MoveTowards(totalLoadPercentLerp, pct, Time.deltaTime * dst * totalLoadLerpSpeed);
                GUI.DrawTexture(rect.ExpandedBy(2), loadingBarWhiteColor);
                Widgets.FillableBar(rect, totalLoadPercentLerp, loadingBarDefaultColor, loadingBarBgColor, false);
                Widgets.Label(rect, $"<color=black><b>{pct.ToStringPercent()}</b></color>");
                
                Text.Anchor = TextAnchor.UpperLeft;

            }
            catch (Exception e)
            {
                Log.ErrorOnce($"Encountered exception while rendering loading screen: {e}", 0xBEEF99);
            }
        }

        private static List<string> LoadGameplayTips()
        {
            return DefDatabase<TipSetDef>.AllDefsListForReading.SelectMany(set => set.tips).InRandomOrder().ToList();
        }

        public static void MarkTipsNowAvailable()
        {
            Log.Message("[BetterLoading] Tips should now be available. Showing...");

            var tips = LoadGameplayTips();
            var cachedTips = File.Exists(_cachedLoadingTipsPath) ? File.ReadAllText(_cachedLoadingTipsPath).Split('\0').ToList() : new List<string>();
            if (!tips.SequenceEqual(cachedTips))
            {
                File.WriteAllText(_cachedLoadingTipsPath, string.Join("\0", tips));
                _tips = tips;
            }
            
            _tipsAvailable = true;
        }
    }
}