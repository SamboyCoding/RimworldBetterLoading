using System;
using System.Linq;
using Harmony;
using Verse;

namespace BetterLoading.Stage.InitialLoad
{
    public class StageInitMods : LoadingStage
    {
        private readonly int numMods = typeof(Mod).InstantiableDescendantsAndSelf().Count();

        private static int currentModIdx = typeof(Mod).InstantiableDescendantsAndSelf().FirstIndexOf(t => t == typeof(BetterLoadingMain)) + 1;
        private static ModContentPack currentMod = BetterLoadingMain.ourContentPack;
        private static bool _completed;

        public StageInitMods(HarmonyInstance instance) : base(instance)
        { }
        
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

        public override void DoPatching(HarmonyInstance instance)
        {
            instance.Patch(AccessTools.Method(typeof(Activator), nameof(Activator.CreateInstance), new[] {typeof(Type), typeof(object[])}), new HarmonyMethod(typeof(StageInitMods), nameof(OnActivatorCreateInstance)));
        }

        public override bool IsCompleted()
        {
            _completed = GetCurrentProgress() >= GetMaximumProgress();
            return _completed;
        }

        public override int GetMaximumProgress()
        {
            return numMods;
        }

        public static void OnActivatorCreateInstance(Type type, params object[] args)
        {
            if (_completed) return; //If we're done, don't go again.
            if(!type.IsInstanceOfType(typeof(Mod))) return; //If we're not constructing a mod bail out.
            if (args.Length != 1 || !(args[0] is ModContentPack pack)) return; //Check the constructor we're using matches the required pattern.
            
            currentModIdx++;
            currentMod = pack;
        }
    }
}