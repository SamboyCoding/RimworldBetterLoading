using System;
using System.Linq;
using Harmony;
using Verse;

namespace BetterLoading.Stage.InitialLoad
{
    public class StageInitMods : LoadingStage
    {
        private readonly int numMods = typeof(Mod).InstantiableDescendantsAndSelf().Count();
        
        private int currentModIdx = typeof(Mod).InstantiableDescendantsAndSelf().FirstIndexOf(t => t == typeof(BetterLoadingMain));
        private ModContentPack currentMod = BetterLoadingMain.ourContentPack;

        public StageInitMods(HarmonyInstance instance) : base(instance)
        {
            instance.Patch(AccessTools.Method(typeof(Activator), nameof(Activator.CreateInstance)), new HarmonyMethod(typeof(StageInitMods), nameof(OnActivatorCreateInstance)));
        }
        
        public override string GetStageName()
        {
            return "Initializing Mods";
        }

        public override string? GetCurrentStepName()
        {
            return currentMod.Name;
        }

        public override int GetCurrentProgress()
        {
            return currentModIdx;
        }

        public override int GetMaximumProgress()
        {
            return numMods;
        }

        public void OnActivatorCreateInstance(Type type, params object[] args)
        {
            if (IsCompleted()) return; //If we're done, don't go again.
            if(!typeof(Mod).IsAssignableFrom(type)) return; //If we're not constructing a mod bail out.
            if (args.Length != 1 || !(args[0] is ModContentPack pack)) return; //Check the constructor we're using matches the required pattern.
            
            currentModIdx++;
            currentMod = pack;
        }
    }
}