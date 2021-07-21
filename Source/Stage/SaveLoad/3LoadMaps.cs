using HarmonyLib;
using JetBrains.Annotations;

namespace BetterLoading.Stage.SaveLoad
{
    public class LoadMaps : LoadingStage {
        public LoadMaps([NotNull] Harmony instance) : base(instance)
        {
        }

        public override string GetStageName()
        {
            throw new System.NotImplementedException();
        }

        public override string? GetCurrentStepName()
        {
            throw new System.NotImplementedException();
        }

        public override int GetCurrentProgress()
        {
            throw new System.NotImplementedException();
        }

        public override int GetMaximumProgress()
        {
            throw new System.NotImplementedException();
        }

        public override void DoPatching(Harmony instance)
        {
        }
    }
}