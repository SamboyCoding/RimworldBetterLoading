using System.Linq;
using BetterLoading.Compat.HugsLib;
using Verse;

namespace BetterLoading.Compat
{
    public static class HugsLibCompat
    {
        public static bool ShouldBeLoaded()
        {
            return LoadedModManager.RunningMods.Any(m => m.Name == "HugsLib");
        }

        public static void Load()
        {
            Log.Message("[BetterLoading] HugsLib detected, adding HugsLib support.");
            
            BetterLoadingApi.AddInitialLoadStage(new StageHugsLibInit(BetterLoadingMain.hInstance));
        }
    }
}