using HarmonyLib;
using JetBrains.Annotations;
using Verse;

namespace BetterLoading.Stage.SaveLoad
{
    public class SpawnAllThings : LoadingStage
    {
        private static int _currMapIdx = -1;
        private static int _numThingsThisMapSoFar;

        private static bool _finished;

        public SpawnAllThings([NotNull] Harmony instance) : base(instance)
        {
        }

        public override string GetStageName()
        {
            return "Spawning all things";
        }

        public override string? GetCurrentStepName()
        {
            if (_currMapIdx == -1)
                return "Waiting...";

            return $"Map {_currMapIdx + 1} of {LoadMaps.NumMaps}: {_numThingsThisMapSoFar} spawned so far";
        }

        public override int GetCurrentProgress()
        {
            if (_currMapIdx == -1)
                return 0;
            
            return _currMapIdx;
        }

        public override int GetMaximumProgress()
        {
            return LoadMaps.NumMaps;
        }

        public override bool IsCompleted()
        {
            return _finished;
        }
        
        public override void BecomeInactive()
        {
            _finished = false;
            _currMapIdx = 0;
            _numThingsThisMapSoFar = 0;
        }

        public override void DoPatching(Harmony instance)
        {
            instance.Patch(AccessTools.Method(typeof(Map), nameof(Map.FinalizeLoading)), new HarmonyMethod(typeof(SpawnAllThings), nameof(OnMapStartFinalizing)), new HarmonyMethod(typeof(SpawnAllThings), nameof(OnMapFinishFinalizing)));
            
            //Things
            instance.Patch(
                AccessTools.Method(typeof(GenSpawn), nameof(GenSpawn.Spawn), new[] {typeof(Thing), typeof(IntVec3), typeof(Map), typeof(Rot4), typeof(WipeMode), typeof(bool)}),
                new HarmonyMethod(typeof(SpawnAllThings), nameof(OnThingAboutToSpawn))
            );

            //Buildings
            instance.Patch(AccessTools.Method(typeof(GenSpawn), nameof(GenSpawn.SpawnBuildingAsPossible)), new HarmonyMethod(typeof(SpawnAllThings), nameof(OnThingAboutToSpawn)));
        }

        public static void OnMapStartFinalizing()
        {
            _currMapIdx++;
            _numThingsThisMapSoFar = 0;
        }

        public static void OnMapFinishFinalizing()
        {
            if (_currMapIdx == LoadMaps.NumMaps - 1) 
                _finished = true;
        }

        public static void OnThingAboutToSpawn()
        {
            _numThingsThisMapSoFar++;
        }
    }
}