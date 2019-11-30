using Verse;

namespace BetterLoading.Stage.InitialLoad
{
    public class StageInitMods : LoadingStage
    {
        private int numMods;
        private int currentModIdx;
        private ModContentPack currentMod;

        public StageInitMods()
        {
            //TODO: Harmony patches
        }
        
        public override string getStageName()
        {
            return "Initializing Mods";
        }

        public override string getCurrentStepName()
        {
            return currentMod.Name;
        }

        public override int getCurrentProgress()
        {
            return currentModIdx;
        }

        public override int getMaximumProgress()
        {
            return numMods;
        }

        public override bool isCompleted()
        {
            return currentModIdx > numMods;
        }
    }
}