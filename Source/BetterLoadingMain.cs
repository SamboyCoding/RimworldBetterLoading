using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HarmonyLib;
using JetBrains.Annotations;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace BetterLoading
{
    public sealed class BetterLoadingMain : Mod
    {
        public static ModContentPack? ourContentPack;
        public static Harmony? hInstance;
        public static LoadingScreen? LoadingScreen;

        public static Dictionary<ModContentPack, List<DllLoadError>> DllPathsThatFailedToLoad = new Dictionary<ModContentPack, List<DllLoadError>>();

        public class DllLoadError
        {
            public string dllName;
            public LogMessage reasonMessage;
        }

        public BetterLoadingMain(ModContentPack content) : base(content)
        {
            ourContentPack = content;

            hInstance = new Harmony("me.samboycoding.blm");
            if (Camera.main == null) return; //Just in case

            LogMsg("[BetterLoading] Verifying all mods loaded properly...");

            var loadFailures = Log.Messages.Where(m => m.text.StartsWith("ReflectionTypeLoadException getting types")).ToList();
            foreach (var pack in LoadedModManager.RunningMods)
            {
                var dllsThatShouldBeLoaded = ModAssemblyHandlerHelper.GetDlls(pack);
                var dllsActuallyLoaded = pack.assemblies?.loadedAssemblies;

                if (dllsThatShouldBeLoaded.Count == 0) continue;

                if (dllsActuallyLoaded == null)
                {
                    Log.Error($"[BetterLoading] DLL Manager for {pack.Name} failed to load!");
                    continue;
                }

                if (dllsActuallyLoaded.Count != dllsThatShouldBeLoaded.Count)
                {
                    //Some assemblies failed to load

                    var loadedPaths = dllsActuallyLoaded.Select(dll => dll.Location.ToLower()).ToList();
                    var didntLoad = dllsThatShouldBeLoaded.Select(f => f.FullName).Where(path => loadedPaths.Contains(path.ToLower()) != true).ToList();

                    //Find the log messages where we identified details about which types failed to load
                    var failures = didntLoad
                        .Select(Path.GetFileNameWithoutExtension)
                        .Select(
                            filename =>
                                new DllLoadError
                                {
                                    dllName = filename,
                                    reasonMessage = loadFailures.First(f => f.text.Contains($"assembly {filename}"))
                                }
                        )
                        .ToList();

                    Log.Error($"[BetterLoading] {dllsThatShouldBeLoaded.Count - dllsActuallyLoaded.Count} assemblies for {pack.Name} failed to load! The ones that didn't load are: {didntLoad.ToCommaList()}");
                    Log.Error($"[BL] Got {failures.Count} messages that identify those failures.");

                    DllPathsThatFailedToLoad[pack] = failures;
                }
            }

            if (DllPathsThatFailedToLoad.Count == 0)
            {
                Log.Message("[BetterLoading] Injecting into main UI.");
                LoadingScreen = Resources.FindObjectsOfTypeAll<Root_Entry>()[0].gameObject.AddComponent<LoadingScreen>();

                hInstance.Patch(AccessTools.Method(typeof(LongEventHandler), nameof(LongEventHandler.LongEventsOnGUI)),
                    new HarmonyMethod(typeof(BetterLoadingMain), nameof(DisableVanillaLoadScreen)));
            }
            else
            {
                Log.Message("[BetterLoading] Not showing loading screen, not all mods loaded successfully so we would be unstable.");

                hInstance.Patch(AccessTools.Method(typeof(UIRoot_Entry), nameof(UIRoot_Entry.Init)), postfix: new HarmonyMethod(typeof(BetterLoadingMain), nameof(DisplayFailedLoadDialog)));
            }

            //Harmony.PatchAll(Assembly.GetExecutingAssembly());
        }

        public static void DisplayFailedLoadDialog()
        {
            var messageTitle = @"BetterLoading

BetterLoading did not display because not all of your modded .dll files (assemblies) loaded properly. 

This probably means you have a bad load order, or you're missing a dependency, such as HugsLib. 

BetterLoading has nothing to do with this, and it would have happened anyway, we're just letting you know.

Don't report this to the BetterLoading dev. If you want to report it to anyone, report it to the developers of the mods below, but it's probably your fault.

The assemblies that failed to load are:
" + string.Join("\n", DllPathsThatFailedToLoad.Select(kvp => $"{kvp.Key.Name} - {kvp.Value.Select(e => e.dllName + ".dll").ToCommaList()}").ToArray());

            foreach (var kvp in DllPathsThatFailedToLoad)
            {
                var mod = kvp.Key;
                var errors = kvp.Value;

                Log.Message($"Errors for mod {mod.Name}:");
                foreach (var dllLoadError in errors)
                {
                    var loaderErrors = GetLoaderErrors(dllLoadError.reasonMessage.text);
                    if (loaderErrors.Count > 0)
                    {
                        Log.Message($"\t{dllLoadError.dllName}.dll failed load, identified failed (typeName, assemblyName) list: {loaderErrors.ToStringSafeEnumerable()}");
                        //TODO: Maybe give advice to users on what exactly failed? We can potentially scan all mods to look for the failed assembly and work out what happened
                        //TODO: i.e. if they have the mod with this asm below the mod needing it - load order error. If the mod isn't loaded, suggest they load it, etc.
                    }
                }
            }

            Find.WindowStack.Add(new Dialog_MessageBox(messageTitle));
        }

        private static List<(string type, string asm)> GetLoaderErrors(string messageText)
        {
            if (!messageText.Contains("Loader exceptions:")) return new List<(string type, string asm)>();

            try
            {
                //Splt on arrows and remove the preamble before the first error
                var split = messageText.Split(new[] {"=> "}, StringSplitOptions.None).Skip(1).Take(int.MaxValue).ToList();

                var target = "from typeref, class/assembly ";
                var errorDetail = split.Select(e => e.Substring(e.IndexOf(target) + target.Length)).ToList();

                var attemptedLoadOf = errorDetail.Select(e => e.Split(',')).Select(arr => (type: arr[0], asm: arr[1])).ToList();

                return attemptedLoadOf;
            }
            catch (Exception)
            {
                //We really don't want this to fail, it's just gonna be a pain
                Log.Warning	("[BetterLoading] Failed to scrape Loader Errors");
                return new List<(string type, string asm)>();
            }
        }

        public static bool DisableVanillaLoadScreen()
        {
            //Disable when our load screen is shown
            return !LoadingScreen.shouldShow;
        }

        private static void LogMsg(string message)
        {
            Log.Message($"[{DateTime.Now}] {message}");
        }

        //Following code kept as reference

        #region Save Game Loading Patches

        [HarmonyPatch(typeof(Game))]
        [HarmonyPatch(nameof(Game.LoadGame))]
        [UsedImplicitly]
        public class LoadGamePatch
        {
            [UsedImplicitly]
            public static void Prefix()
            {
                Resources.FindObjectsOfTypeAll<Root_Play>()[0].gameObject
                    .AddComponent<LoadingScreen>();
                LogMsg("Loading Screen Manager :: Load Small Components :: Start");

                LoadingScreen.Instance.shouldShow = true;
                LoadingScreen.Instance.currentStage = EnumLoadingStage.LoadSmallComponents;
            }
        }

        [HarmonyPatch(typeof(World))]
        [HarmonyPatch(nameof(World.ExposeData))]
        [UsedImplicitly]
        public class LoadWorldPatch
        {
            [UsedImplicitly]
            public static void Prefix()
            {
                if (LoadingScreen.Instance.currentStage == EnumLoadingStage.LoadSmallComponents)
                {
                    LogMsg("Loading Screen Manager :: Load World Map :: Start");
                    LoadingScreen.Instance.currentStage = EnumLoadingStage.LoadWorldMap;
                }
            }
        }

        [HarmonyPatch(typeof(WorldGenerator))]
        [HarmonyPatch(nameof(WorldGenerator.GenerateFromScribe))]
        [UsedImplicitly]
        public class GenerateWorldPatch
        {
            [UsedImplicitly]
            public static void Prefix()
            {
                LogMsg("Loading Screen Manager :: Generate World Data :: Start");
                LoadingScreen.Instance.currentStage = EnumLoadingStage.GenerateWorldData;
                LoadingScreen.Instance.numWorldGeneratorsToRun = WorldGenerator.GenStepsInOrder.Count() - 2;
            }
        }

        [HarmonyPatch(typeof(WorldGenerator))]
        [HarmonyPatch(nameof(WorldGenerator.GenerateWithoutWorldData))]
        [UsedImplicitly]
        public class GenerateWorldPatch2
        {
            [UsedImplicitly]
            public static void Prefix()
            {
                LogMsg("Loading Screen Manager :: Generate World Data :: Start");
                LoadingScreen.Instance.currentStage = EnumLoadingStage.GenerateWorldData;
                LoadingScreen.Instance.numWorldGeneratorsToRun = WorldGenerator.GenStepsInOrder.Count() - 2;
            }
        }

        [HarmonyPatch(typeof(WorldGenStep))]
        [HarmonyPatch(nameof(WorldGenStep.GenerateFromScribe))]
        [UsedImplicitly]
        public class WorldGenStepExecPatch
        {
            [UsedImplicitly]
            public static void Prefix(WorldGenStep __instance)
            {
                LoadingScreen.Instance.numWorldGeneratorsRun++;
                LoadingScreen.Instance.currentWorldGenStep = __instance;
            }
        }

        [HarmonyPatch(typeof(WorldGenStep))]
        [HarmonyPatch(nameof(WorldGenStep.GenerateWithoutWorldData))]
        [UsedImplicitly]
        public class WorldGenStepExecPatch2
        {
            [UsedImplicitly]
            public static void Prefix(WorldGenStep __instance)
            {
                LoadingScreen.Instance.numWorldGeneratorsRun++;
                LoadingScreen.Instance.currentWorldGenStep = __instance;
            }
        }

        [HarmonyPatch(typeof(World))]
        [HarmonyPatch(nameof(World.FinalizeInit))]
        [UsedImplicitly]
        public class WorldFinalizePatch
        {
            [UsedImplicitly]
            public static void Prefix()
            {
                LogMsg("Loading Screen Manager :: Finalize World Data :: Start");
                LoadingScreen.Instance.currentStage = EnumLoadingStage.FinalizeWorld;
            }
        }

        [HarmonyPatch(typeof(Map))]
        [HarmonyPatch(nameof(Map.ExposeData))]
        [UsedImplicitly]
        public class MapExposePatch
        {
            [UsedImplicitly]
            public static void Prefix(Map __instance)
            {
                if (LoadingScreen.Instance.currentStage >= EnumLoadingStage.FinalizeWorld &&
                    LoadingScreen.Instance.currentStage <= EnumLoadingStage.LoadMaps_LoadData)
                {
                    LogMsg("Loading Screen Manager :: Load Map (Construct Components) :: Start");
                    LoadingScreen.Instance.currentStage = EnumLoadingStage.LoadMaps_ConstructComponents;
                    LoadingScreen.Instance.maps.Add(__instance);
                }
            }
        }

        [HarmonyPatch(typeof(Map))]
        [HarmonyPatch("ExposeComponents")]
        [UsedImplicitly]
        public class MapLoadPatch
        {
            [UsedImplicitly]
            public static void Prefix()
            {
                if (LoadingScreen.Instance.currentStage == EnumLoadingStage.LoadMaps_ConstructComponents)
                {
                    LogMsg("Loading Screen Manager :: Load Map (Load Components) :: Start");
                    LoadingScreen.Instance.currentStage = EnumLoadingStage.LoadMaps_LoadComponents;
                }
            }
        }

        [HarmonyPatch(typeof(MapFileCompressor))]
        [HarmonyPatch(nameof(MapFileCompressor.ExposeData))]
        [UsedImplicitly]
        public class MapLoadCompressedPatch
        {
            [UsedImplicitly]
            public static void Prefix()
            {
                if (LoadingScreen.Instance.currentStage == EnumLoadingStage.LoadMaps_LoadComponents)
                {
                    LogMsg("Loading Screen Manager :: Load Map (Load Objects) :: Start");
                    LoadingScreen.Instance.currentStage = EnumLoadingStage.LoadMaps_LoadData;
                }
            }
        }

        [HarmonyPatch(typeof(CameraDriver))]
        [HarmonyPatch(nameof(CameraDriver.Expose))]
        [UsedImplicitly]
        public class CameraLoadPatch
        {
            [UsedImplicitly]
            public static void Prefix()
            {
                if (LoadingScreen.Instance.currentStage == EnumLoadingStage.LoadMaps_LoadData)
                {
                    LogMsg("Loading Screen Manager :: Init Camera :: Start");
                    LoadingScreen.Instance.currentStage = EnumLoadingStage.InitCamera;
                }
            }
        }

        [HarmonyPatch(typeof(ScribeLoader))]
        [HarmonyPatch(nameof(ScribeLoader.FinalizeLoading))]
        [UsedImplicitly]
        public class ResolveSaveFileReferencesPatch
        {
            [UsedImplicitly]
            public static void Prefix()
            {
                if (LoadingScreen.Instance.currentStage != EnumLoadingStage.InitCamera) return;

                LogMsg("Loading Screen Manager :: Resolve Cross-References :: Start");
                LoadingScreen.Instance.currentStage = EnumLoadingStage.ResolveSaveFileCrossReferences;
            }
        }

        [HarmonyPatch(typeof(Map))]
        [HarmonyPatch(nameof(Map.FinalizeLoading))]
        [UsedImplicitly]
        public class MapFinalizeLoadPatch
        {
            [UsedImplicitly]
            public static void Prefix(Map __instance)
            {
                LogMsg("Loading Screen Manager :: Spawn Things (Non-Buildings) :: Start");
                LoadingScreen.Instance.currentStage = EnumLoadingStage.SpawnThings_NonBuildings;
                LoadingScreen.Instance.mapIndexSpawningItems++;

                //Reflection, fuck yeah!
//                LoadingScreen.Instance.numObjectsToSpawnCurrentMap = __instance.compressor.ThingsToSpawnAfterLoad().Count() +
//                                                      Traverse.Create(__instance).Field<List<Thing>>("loadedFullThings")
//                                                          .Value.Count;
//                LoadingScreen.Instance.numObjectsSpawnedCurrentMap = 0;
            }
        }

        [HarmonyPatch(typeof(GenSpawn))]
        [HarmonyPatch(nameof(GenSpawn.Spawn))]
        [HarmonyPatch(new[]
            {typeof(Thing), typeof(IntVec3), typeof(Map), typeof(Rot4), typeof(WipeMode), typeof(bool)})]
        [UsedImplicitly]
        public class GenSpawnSpawnPatch
        {
            [UsedImplicitly]
            public static void Prefix()
            {
                if (LoadingScreen.Instance.currentStage == EnumLoadingStage.SpawnThings_NonBuildings)
                    LoadingScreen.Instance.numObjectsSpawnedCurrentMap++;
            }
        }

        [HarmonyPatch(typeof(GenSpawn))]
        [HarmonyPatch(nameof(GenSpawn.SpawnBuildingAsPossible))]
        [UsedImplicitly]
        public class GenSpawnSpawnBuildingPatch
        {
            [UsedImplicitly]
            public static void Prefix(Map __instance)
            {
                if (LoadingScreen.Instance.currentStage == EnumLoadingStage.SpawnThings_NonBuildings)
                {
                    LogMsg("Loading Screen Manager :: Spawn Things (Buildings) :: Start");
                    LoadingScreen.Instance.currentStage = EnumLoadingStage.SpawnThings_Buildings;
                }

                if (LoadingScreen.Instance.currentStage == EnumLoadingStage.SpawnThings_Buildings)
                    LoadingScreen.Instance.numObjectsSpawnedCurrentMap++;
            }
        }

        [HarmonyPatch(typeof(GenPlace))]
        [HarmonyPatch(nameof(GenPlace.TryPlaceThing))]
        [HarmonyPatch(new[]
        {
            typeof(Thing), typeof(IntVec3), typeof(Map), typeof(ThingPlaceMode), typeof(Action<Thing, int>),
            typeof(Predicate<IntVec3>)
        })]
        [UsedImplicitly]
        public class GenPlaceTryPlacePatch
        {
            [UsedImplicitly]
            public static void Prefix()
            {
                if (LoadingScreen.Instance.currentStage == EnumLoadingStage.SpawnThings_Buildings)
                {
                    LogMsg("Loading Screen Manager :: Spawn Things (Back-Compat) :: Start");
                    LoadingScreen.Instance.currentStage = EnumLoadingStage.SpawnThings_BackCompat;
                }

                if (LoadingScreen.Instance.currentStage == EnumLoadingStage.SpawnThings_BackCompat)
                    LoadingScreen.Instance.numObjectsSpawnedCurrentMap++;
            }
        }

        [HarmonyPatch(typeof(Map))]
        [HarmonyPatch(nameof(Map.FinalizeInit))]
        [UsedImplicitly]
        public class MapFinalizeInitPatch
        {
            [UsedImplicitly]
            public static void Prefix()
            {
                LogMsg("Loading Screen Manager :: Spawn Things (Rebuild/Recalc) :: Start");
                LoadingScreen.Instance.currentStage = EnumLoadingStage.SpawnThings_RebuildRecalc;
            }
        }

        [HarmonyPatch(typeof(Game))]
        [HarmonyPatch(nameof(Game.FinalizeInit))]
        [UsedImplicitly]
        public class GameFinalizeInitPatch
        {
            [UsedImplicitly]
            public static void Prefix()
            {
                LogMsg("Loading Screen Manager :: Finalize Load :: Start");
                LoadingScreen.Instance.currentStage = EnumLoadingStage.FinalizeLoad;
            }
        }

        #endregion
    }
}