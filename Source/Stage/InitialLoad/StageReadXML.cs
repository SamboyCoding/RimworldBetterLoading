using System.Linq;
using Harmony;
using Verse;

namespace BetterLoading.Stage.InitialLoad
{
    public class StageReadXML : LoadingStage
    {
        private ModContentPack? _currentPack;
        private int _currentPackIdx;
        private int _numPacks = -1;

        public StageReadXML(HarmonyInstance instance) : base(instance)
        {
            instance.Patch(AccessTools.Method(typeof(ModContentPack), nameof(ModContentPack.LoadDefs)), new HarmonyMethod(typeof(StageReadXML), nameof(OnLoadDefs)));
        }

        public override string GetStageName()
        {
            return "Reading Mod Content Files";
        }

        public override string GetCurrentStepName()
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

        public override void BecomeActive()
        {
            _numPacks = LoadedModManager.RunningMods.Count();
        }

        private void OnLoadDefs(ModContentPack __instance)
        {
            _currentPack = __instance;
            _currentPackIdx++;
        }
    }
}