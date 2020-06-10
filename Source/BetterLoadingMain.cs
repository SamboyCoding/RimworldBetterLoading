using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HarmonyLib;
using JetBrains.Annotations;
using RimWorld;
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

        public static readonly Dictionary<ModContentPack, List<DllLoadError>> DllPathsThatFailedToLoad = new Dictionary<ModContentPack, List<DllLoadError>>();

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
                                    reasonMessage = loadFailures.First(msg => msg.text.Contains($"assembly {filename}"))
                                }
                        )
                        .ToList();

                    Log.Error($"[BetterLoading] {dllsThatShouldBeLoaded.Count - dllsActuallyLoaded.Count} assemblies for {pack.Name} failed to load! The ones that didn't load are: {didntLoad.ToCommaList()}");
                    Log.Error($"[BetterLoading] Got {failures.Count} messages that identify those failures.");

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
" + string.Join("\n", DllPathsThatFailedToLoad.Select(kvp => $"{kvp.Key.Name} - {kvp.Value.Select(e => e.dllName + ".dll").ToCommaList()}").ToArray())
  + "\n\nIf you would like to see what info BetterLoading has been able to work out in terms of mod misconfiguration, open the log window (you may have to enable developer mode in settings first) and look at the last few lines (anything containing, and after, the phrase 'failed load, identified')";

            foreach (var kvp in DllPathsThatFailedToLoad)
            {
                var modThatFailedLoad = kvp.Key;
                var errors = kvp.Value;

                Log.Message($"Errors for mod {modThatFailedLoad.Name}:");
                foreach (var dllLoadError in errors)
                {
                    var loaderErrors = GetLoaderErrors(dllLoadError.reasonMessage.text);
                    if (loaderErrors.Count > 0)
                    {
                        Log.Message($"\t{dllLoadError.dllName}.dll failed load, identified {loaderErrors.Count} types that failed to load.");

                        var externalErrors = loaderErrors.Where(e => e.asm != dllLoadError.dllName).ToList();

                        if (externalErrors.Count == 0)
                        {
                            Log.Message($"\t{dllLoadError.dllName} load failure seems to be entirely internal - corrupt dll?");
                            continue;
                        }

                        var missingAssemblies = externalErrors
                            .Select(e => e.asm)
                            .Distinct()
                            .ToList();

                        //Try find mods containing the DLLs we're dependent on but can't get
                        var dependentMods = missingAssemblies
                            .Where(asm => !asm.Contains("Harmony")) //Ignore harmony as that'll likely break
                            .Select(e => ModLister.AllInstalledMods.FirstOrDefault(m => ModContainsAssembly(m, e)))
                            .Where(mod => mod != null)
                            .ToList();

                        if (dependentMods.Count > 0)
                        {
                            Log.Message($"\t{dllLoadError.dllName} appears to have a dependency on these mod(s): {dependentMods.Select(m => m.Name).ToStringSafeEnumerable()}");

                            var notLoaded = dependentMods.Where(requiredMod => LoadedModManager.RunningMods.All(runningMod => runningMod.Name != requiredMod.Name)).ToList();
                            if (notLoaded.Count > 0)
                                notLoaded.ForEach(m => Log.Warning($"\t{modThatFailedLoad.Name} depends on {m.Name} which is not enabled, so it didn't load properly."));

                            var modsLoadedAfterTarget = LoadedModManager.RunningMods.Skip(LoadedModManager.RunningModsListForReading.FindIndex(i => i.Name == modThatFailedLoad.Name)).Take(int.MaxValue).ToList();
                            var depsLoadedAfterDependent = modsLoadedAfterTarget.Where(loadedAfter => dependentMods.Any(dep => dep.Name == loadedAfter.Name)).ToList();
                            if (depsLoadedAfterDependent.Count > 0)
                                depsLoadedAfterDependent.ForEach(m => Log.Warning($"\t{modThatFailedLoad.Name} is loaded before {m.Name} but depends on it, so must be loaded after. It didn't load properly because of this."));
                        }

                        if (dependentMods.Count != missingAssemblies.Count)
                        {
                            var notInAnyMods = missingAssemblies
                                .Where(asm => ModLister.AllInstalledMods.All(m => !ModContainsAssembly(m, asm)))
                                .Select(asm => $"{asm}.dll")
                                .ToList();
                            
                            Log.Message($"\t{dllLoadError.dllName} (also) depends on these DLL(s) which couldn't be found in any installed mods: {notInAnyMods.ToStringSafeEnumerable()}");
                        }
                    }
                }
            }

            Find.WindowStack.Add(new Dialog_MessageBox(messageTitle));
        }

        private static bool ModContainsAssembly(ModMetaData mod, string assemblyName)
        {
            var searchPaths = new List<string>();

            //Sourced from ModContentPack#InitLoadFolders
            if (mod.LoadFoldersForVersion(VersionControl.CurrentVersionStringWithoutBuild) is {} forBuild)
                searchPaths.AddRange(forBuild.Select(p => p.folderName));
            if (mod.LoadFoldersForVersion("default") is {} forDefault)
                searchPaths.AddRange(forDefault.Select(p => p.folderName));

            if (searchPaths.Count == 0)
            {
                //Add default ones - common folder, version folder, + root
                var pathWithVer = Path.Combine(mod.RootDir.FullName, VersionControl.CurrentVersionStringWithoutBuild);
                if (Directory.Exists(pathWithVer))
                    searchPaths.Add(pathWithVer);
                var commonPath = Path.Combine(mod.RootDir.FullName, ModContentPack.CommonFolderName);
                if (Directory.Exists(commonPath))
                    searchPaths.Add(commonPath);
                searchPaths.Add(mod.RootDir.FullName);
            }

            var searchFolderNames = searchPaths.Select(p => Path.Combine(mod.RootDir.FullName, p)).ToList();

            var modDlls = searchFolderNames
                .Select(p => Path.Combine(p, "Assemblies")) //Get assemblies folder
                .Where(Directory.Exists) //Ensure exists
                .SelectMany(Directory.GetFiles) //Map to files
                .Where(p => Path.GetExtension(p) == ".dll") //Ensure dll
                .Select(Path.GetFileNameWithoutExtension) //File names
                .ToList();

            return modDlls
                .Contains(assemblyName); //Check for target
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

                var attemptedLoadOf = errorDetail.Select(e => e.Split(',')).Select(arr => (type: arr[0].Trim(), asm: arr[1].Trim())).ToList();

                return attemptedLoadOf;
            }
            catch (Exception)
            {
                //We really don't want this to fail, it's just gonna be a pain
                Log.Warning("[BetterLoading] Failed to scrape Loader Errors.");
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