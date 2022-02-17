using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Verse;

namespace BetterLoading.Stage.InitialLoad
{
    public class StageReadXML : LoadingStage
    {
        private static ModContentPack? _currentPack;
        private static int _currentPackIdx = 1;
        private int _numPacks = -1;

        private static StageReadXML? inst;

        public StageReadXML(Harmony instance) : base(instance)
        {
        }

        public override string GetStageName()
        {
            return "Reading Mod Content Files";
        }

        public override string? GetCurrentStepName()
        {
            return _currentPack?.Name;
        }

        public override int GetCurrentProgress()
        {
            return _currentPackIdx;
        }

        public override int GetMaximumProgress()
        {
            return _numPacks;
        }

        public override void DoPatching(Harmony instance)
        {
            instance.Patch(AccessTools.Method(typeof(LoadedModManager), nameof(LoadedModManager.LoadModXML)), new(typeof(Utils), nameof(Utils.HarmonyPatchCancelMethod)), new(typeof(StageReadXML), nameof(AlternativeLoadModXml)));
            instance.Patch(AccessTools.Method(typeof(ModContentPack), nameof(ModContentPack.LoadDefs)), postfix: new(typeof(StageReadXML), nameof(OnLoadDefsComplete)));
        }

        public override void BecomeActive()
        {
            _numPacks = LoadedModManager.RunningMods.Count();
            inst = LoadingScreen.GetStageInstance<StageReadXML>();
        }

        public static void AlternativeLoadModXml(ref List<LoadableXmlAsset> __result)
        {
            __result = LoadedModManager.RunningModsListForReading.AsParallel().SelectMany(m =>
            {
                DeepProfiler.Start("Loading " + m);
                try
                {
                    return m.LoadDefs();
                }
                catch (Exception e)
                {
                    Log.Error("[BetterLoading] [Enhanced XML Load] Could not load defs for mod " + m.PackageIdPlayerFacing + ": " + e);
                    return new List<LoadableXmlAsset>();
                }
                finally
                {
                    DeepProfiler.End();
                }
            }).ToList();
            
            Log.Message($"[BetterLoading] [Enhanced XML Load] Loaded {__result.Count} loadable assets.");
            _currentPackIdx = inst._numPacks + 1;
        }

        public static void OnLoadDefsComplete(ModContentPack __instance)
        {
            _currentPack = __instance;
            _currentPackIdx++;
            BetterLoadingApi.DispatchChange(inst);
        }
    }
}