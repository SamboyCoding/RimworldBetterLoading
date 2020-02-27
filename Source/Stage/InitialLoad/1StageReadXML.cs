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
            instance.Patch(AccessTools.Method(typeof(ModContentPack), nameof(ModContentPack.LoadDefs)), postfix: new HarmonyMethod(typeof(StageReadXML), nameof(OnLoadDefsComplete)));
        }

        public override void BecomeActive()
        {
            _numPacks = LoadedModManager.RunningMods.Count();
        }

        public static void OnLoadDefsComplete(ModContentPack __instance)
        {
            _currentPack = __instance;
            _currentPackIdx++;
        }
    }
}