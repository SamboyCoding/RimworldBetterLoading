using HarmonyLib;
using JetBrains.Annotations;
using RimWorld.Planet;

namespace BetterLoading.Stage.SaveLoad
{
    public class LoadSmallComponents : LoadingStage
    {
        private static bool _hasFinished;
        
        public LoadSmallComponents([NotNull] Harmony instance) : base(instance)
        {
        }

        public override string GetStageName()
        {
            return "Loading Small Components";
        }

        public override string? GetCurrentStepName()
        {
            return null;
        }

        public override int GetCurrentProgress()
        {
            return _hasFinished ? 1 : 0;
        }

        public override int GetMaximumProgress()
        {
            return 1;
        }

        public override bool IsCompleted()
        {
            return _hasFinished;
        }

        public override void BecomeInactive()
        {
            _hasFinished = false;
        }

        public override void DoPatching(Harmony instance)
        {
            instance.Patch(AccessTools.Method(typeof(World), nameof(World.ExposeData)), new(typeof(LoadSmallComponents), nameof(OnLoadWorldStart)));
        }

        public static void OnLoadWorldStart()
        {
            _hasFinished = true;
        }
    }
}