using System.Collections.Generic;
using System.Linq;
using Harmony;
using Verse;

namespace BetterLoading.Stage.InitialLoad
{
    public class StageApplyPatches : LoadingStage
    {
        private static List<ModContentPack> _modList;
        private static ModContentPack? _currentMod;
        private static int _currentModNum = -1;

        private static bool _loadingPatches;
        private static int _numPatches = -1;
        private static int _currentPatch;

        public StageApplyPatches(HarmonyInstance instance) : base(instance)
        {
            _modList = LoadedModManager.RunningMods.ToList();
        }

        public override void BecomeInactive()
        {
            _currentMod = null;
            _currentModNum = -1;
        }

        public override string GetStageName()
        {
            return "Applying Patches";
        }

        public override string? GetCurrentStepName()
        {
            if (_currentMod == null)
                return "<initializing>";

            var result = _currentMod.Name + ": ";

            if (_numPatches < 0)
                result += "Loading Patches...";
            else
                result += $"{_currentPatch} of {_numPatches}";

            return result;
        }

        public override int GetCurrentProgress()
        {
            return _currentModNum;
        }

        public override int GetMaximumProgress()
        {
            return _modList.Count;
        }

        public override bool IsCompleted()
        {
            return GetCurrentProgress() == GetMaximumProgress() && _currentPatch >= _numPatches && !_loadingPatches;
        }

        public override void DoPatching(HarmonyInstance instance)
        {
            instance.Patch(AccessTools.Method(typeof(ModContentPack), "LoadPatches"), new HarmonyMethod(typeof(StageApplyPatches), nameof(PreLoadPatches)), new HarmonyMethod(typeof(StageApplyPatches), nameof(PostLoadPatches)));

            instance.Patch(AccessTools.Method(typeof(PatchOperation), nameof(PatchOperation.Apply)), new HarmonyMethod(typeof(StageApplyPatches), nameof(PostApplyPatch)));
        }

        public static void PreLoadPatches(ModContentPack __instance)
        {
            _loadingPatches = true;
            _currentMod = __instance;
            _currentModNum = _modList.IndexOf(_currentMod) + 1;
            _numPatches = -1;
            _currentPatch = 0;
        }

        public static void PostLoadPatches(List<PatchOperation> ___patches)
        {
            _numPatches = ___patches.Count;
            _currentPatch = 0;
            _loadingPatches = false;
        }

        public static void PostApplyPatch()
        {
            _currentPatch++;
            if (_currentPatch > _numPatches)
                _numPatches++;
        }
    }
}