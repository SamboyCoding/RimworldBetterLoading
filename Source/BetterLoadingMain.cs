using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BetterLoading.Compat;
using BetterLoading.Stage.SaveLoad;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Object = UnityEngine.Object;

namespace BetterLoading
{
    public sealed class BetterLoadingMain : Mod
    {
        public static ModContentPack? ourContentPack;
        public static Harmony hInstance = new("me.samboycoding.blm");
        public static LoadingScreen? LoadingScreen;

        public static readonly Dictionary<ModContentPack, List<DllLoadError>> DllPathsThatFailedToLoad = new();

        public class DllLoadError
        {
            public string dllName;
            public LogMessage? reasonMessage;

            public DllLoadError(string dllName, LogMessage? reasonMessage)
            {
                this.dllName = dllName;
                this.reasonMessage = reasonMessage;
            }
        }

        public BetterLoadingMain(ModContentPack content) : base(content)
        {
            ourContentPack = content;

            if (Camera.main == null) return; //Just in case

            hInstance.Patch(AccessTools.Method(typeof(PlayDataLoader), nameof(PlayDataLoader.ClearAllPlayData)), new(typeof(BetterLoadingMain), nameof(OnClearPlayData)));

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
                            filename => new DllLoadError(filename, loadFailures.FirstOrDefault(msg => msg.text.Contains($"assembly {filename}")))
                        )
                        .ToList();

                    Log.Error($"[BetterLoading] {dllsThatShouldBeLoaded.Count - dllsActuallyLoaded.Count} assemblies for {pack.Name} failed to load! The ones that didn't load are: {string.Join(", ", didntLoad)}");
                    Log.Error($"[BetterLoading] Got {failures.Count} messages that identify those failures.");

                    DllPathsThatFailedToLoad[pack] = failures;
                }
            }

            if (DllPathsThatFailedToLoad.Count == 0)
            {
                BetterLoadingConfigManager.Load();
                ShitRimworldSaysCompat.PatchShitRimworldSaysIfPresent();

                //Prepatcher re-launches the game...
                var alreadyCreatedLoadScreens = Object.FindObjectsOfType<Component>().Where(c => c.GetType().FullName.Contains("LoadingScreen")).ToList();
                if (alreadyCreatedLoadScreens.Count > 0)
                {
                    Log.Warning("[BetterLoading] [Warning] Probably running with prepatcher! The loading screen handler has already been set up - will just destroy it and re-create. This isn't a problem, just letting you know!");
                    alreadyCreatedLoadScreens.ForEach(Object.Destroy);
                    LoadingScreen = null;
                }

                Log.Message("[BetterLoading] Injecting into main UI.");
                LoadingScreen = Object.FindObjectOfType<Root_Entry>().gameObject.AddComponent<LoadingScreen>();
                InitLoadingScreenBackground();

                hInstance.Patch(AccessTools.Method(typeof(LongEventHandler), nameof(LongEventHandler.LongEventsOnGUI)),
                    new(typeof(BetterLoadingMain), nameof(DisableVanillaLoadScreen)));

                hInstance.Patch(AccessTools.Method(typeof(Game), nameof(Game.LoadGame)), new(typeof(BetterLoadingMain), nameof(OnGameLoadStart)));

                BetterLoadingApi.OnGameLoadComplete += CreateTimingReport;
            }
            else
            {
                Log.Message("[BetterLoading] Not showing loading screen, not all mods loaded successfully so we would be unstable.");

                hInstance.Patch(AccessTools.Method(typeof(UIRoot_Entry), nameof(UIRoot_Entry.Init)), postfix: new(typeof(BetterLoadingMain), nameof(DisplayFailedLoadDialog)));
            }

            //Harmony.PatchAll(Assembly.GetExecutingAssembly());
        }

        private static void InitLoadingScreenBackground()
        {
            LoadingScreen!.PickBackground();
        }

        private void CreateTimingReport()
        {
            var timeBuildingXml = TimeSpan.FromTicks(GlobalTimingData.TicksFinishedBuildingXmlTree - GlobalTimingData.TicksStarted);
            var timeConstructingDefs = TimeSpan.FromTicks(GlobalTimingData.TicksFinishedConstructingDefs - GlobalTimingData.TicksFinishedBuildingXmlTree);
            var timeResolvingDatabases = TimeSpan.FromTicks(GlobalTimingData.TicksStartedCctors - GlobalTimingData.TicksFinishedConstructingDefs);
            var timeRunningCctors = TimeSpan.FromTicks(GlobalTimingData.TicksStartedPostFinalize - GlobalTimingData.TicksStartedCctors);
            var timeRunningPostFinalize = TimeSpan.FromTicks(GlobalTimingData.TicksFinishedPostFinalize - GlobalTimingData.TicksStartedPostFinalize);
            var totalLoadTime = TimeSpan.FromTicks(DateTime.UtcNow.Ticks - GlobalTimingData.TicksStarted);

            Log.Message($"[BetterLoading] Game load has finished. Timing data follows:\n" +
                        $"Spent {timeBuildingXml.TotalMilliseconds}ms reading, building, and patching XML tree.\n" +
                        $"Spent {timeConstructingDefs.TotalMilliseconds}ms turning XML into def instances.\n" +
                        $"Spent {timeResolvingDatabases.TotalMilliseconds}ms resolving cross-references and running post-load, pre-finalize callbacks.\n" +
                        $"Spent {timeRunningCctors.TotalMilliseconds}ms running static constructors (initializing mods).\n" +
                        $"Spent {timeRunningPostFinalize.TotalMilliseconds}ms running post-finalize callbacks.\n" +
                        $"In total, spent {totalLoadTime.TotalMilliseconds}ms launching the game.");
        }

        public static void DisplayFailedLoadDialog()
        {
            var messageTitle = @"BetterLoading

BetterLoading did not display because not all of your modded .dll files (assemblies) loaded properly. 

This probably means you have a bad load order, or you're missing a dependency, such as HugsLib. 

BetterLoading has nothing to do with this, and it would have happened anyway, we're just letting you know.

Don't report this to the BetterLoading dev. If you want to report it to anyone, report it to the developers of the mods below, but it's probably an issue with your setup.

The assemblies that failed to load are:
" + string.Join("\n", DllPathsThatFailedToLoad.Select(kvp => $"{kvp.Key.Name} - {kvp.Value.Select(e => e.dllName + ".dll").ToCommaList()}").ToArray())
  + "\n\nIf you would like to see what info BetterLoading has been able to work out in terms of mod misconfiguration, open the log window (you may have to enable developer mode in settings first) and look at the warning messages from BetterLoading.";

            foreach (var kvp in DllPathsThatFailedToLoad)
            {
                var modThatFailedLoad = kvp.Key;
                var errors = kvp.Value;

                Log.Warning($"[BetterLoading] Mod {modThatFailedLoad.Name} failed to load at least some of its code. Details:");
                foreach (var dllLoadError in errors)
                {
                    if (dllLoadError.reasonMessage == null)
                    {
                        Log.Warning($"[BetterLoading] \t{dllLoadError.dllName}.dll failed to load, but we couldn't work out why. Possibly intentional? For safety reasons, the loading screen will not show.");
                        continue;
                    }

                    var loaderErrors = GetLoaderErrors(dllLoadError.reasonMessage.text);
                    if (loaderErrors.Count > 0)
                    {
                        Log.Warning($"[BetterLoading] \t{dllLoadError.dllName}.dll failed load, identified {loaderErrors.Count} types that failed to load.");

                        var externalErrors = loaderErrors.Where(e => e.asm != dllLoadError.dllName).ToList();

                        if (externalErrors.Count == 0)
                        {
                            Log.Warning($"[BetterLoading] \t{dllLoadError.dllName}.dll load failure seems to be entirely internal - corrupt dll?");
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
                            Log.Warning($"[BetterLoading] \t{dllLoadError.dllName} appears to have a dependency on these mod(s): {dependentMods.Select(m => m?.Name).ToStringSafeEnumerable()}");

                            var notLoaded = dependentMods.Where(requiredMod => LoadedModManager.RunningMods.All(runningMod => runningMod.Name != requiredMod?.Name)).ToList();
                            if (notLoaded.Count > 0)
                                notLoaded.ForEach(m => Log.Warning($"[BetterLoading] \t{modThatFailedLoad.Name} depends on {m?.Name} which is not enabled, so it didn't load properly."));

                            var modsLoadedAfterTarget = LoadedModManager.RunningMods.Skip(LoadedModManager.RunningModsListForReading.FindIndex(i => i.Name == modThatFailedLoad.Name)).Take(int.MaxValue).ToList();
                            var depsLoadedAfterDependent = modsLoadedAfterTarget.Where(loadedAfter => dependentMods.Any(dep => dep?.Name == loadedAfter.Name)).ToList();
                            if (depsLoadedAfterDependent.Count > 0)
                                depsLoadedAfterDependent.ForEach(m => Log.Warning($"[BetterLoading] \t{modThatFailedLoad.Name} is loaded before {m.Name} but depends on it, so must be loaded after. It didn't load properly because of this."));
                        }

                        if (dependentMods.Count != missingAssemblies.Count)
                        {
                            var notInAnyMods = missingAssemblies
                                .Where(asm => ModLister.AllInstalledMods.All(m => !ModContainsAssembly(m, asm)))
                                .Select(asm => $"{asm}.dll")
                                .ToList();

                            Log.Warning($"[BetterLoading] \t{dllLoadError.dllName} (also) depends on these DLL(s) which couldn't be found in any installed mods: {notInAnyMods.ToStringSafeEnumerable()}");
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
            if (mod.LoadFoldersForVersion(VersionControl.CurrentVersionStringWithoutBuild) is { } forBuild)
                searchPaths.AddRange(forBuild.Select(p => p.folderName));
            if (mod.LoadFoldersForVersion("default") is { } forDefault)
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
            if (!messageText.Contains("Loader exceptions:")) return new();

            try
            {
                //Splt on arrows and remove the preamble before the first error
                var split = messageText.Split(new[] {"=> "}, StringSplitOptions.None).Skip(1).Take(int.MaxValue).ToList();

                var target = "from typeref, class/assembly ";
                var errorDetail = split.Select(e => e.Substring(e.IndexOf(target, StringComparison.Ordinal) + target.Length)).ToList();

                var attemptedLoadOf = errorDetail.Select(e => e.Split(',')).Select(arr => (type: arr[0].Trim(), asm: arr[1].Trim())).ToList();

                return attemptedLoadOf;
            }
            catch (Exception)
            {
                //We really don't want this to fail, it's just gonna be a pain
                Log.Warning("[BetterLoading] Failed to scrape Loader Errors.");
                return new();
            }
        }

        public static bool DisableVanillaLoadScreen()
        {
            //Disable when our load screen is shown
            return !LoadingScreen!.shouldShow;
        }

        public static void OnGameLoadStart()
        {
            if (ModLister.AllInstalledMods.FirstOrDefault(m => m.enabled && m.Name.ToLowerInvariant().Contains("multiplayer")) is { } mpMod)
            {
                Log.Warning($"[BetterLoading] Not showing game load/save screen because we've detected what we believe to be a multiplayer mod: {mpMod.Name}");
                return;
            }


            LoadingScreen = Object.FindObjectOfType<Root_Play>().gameObject
                .AddComponent<LoadingScreen>();
            InitLoadingScreenBackground();

            //Try and work out how many maps we have
            LoadMaps.CountMaps();

            LoadingScreen!.StartSaveLoad();
        }

        private static void LogMsg(string message)
        {
            Log.Message($"[{DateTime.Now}] {message}");
        }

        public static void OnClearPlayData()
        {
            //Reset our harmony patches.
            hInstance.UnpatchAll("me.samboycoding.blm");

            if (LoadingScreen == null)
                return;

            //Destroy loading screen.
            Object.Destroy(LoadingScreen);
        }
    }
}