using System;
using HarmonyLib;
using JetBrains.Annotations;
using RimWorld;
using Verse;

namespace BetterLoading.Stage.SaveLoad
{
    public class FinalizeMap : LoadingStage
    {
        private enum MapFinalizeState
        {
            SpawnThings,
            SpawnBuildings,
            RebuildingPathfindingCache,
            FinalizeGeometry,
            InitThings,
            ListFilth,
            CountResources,
            CountWealth,
            InitComponents,
            
            NumStates
        }
        
        private static int _currMapIdx = -1;
        private static int _numThingsThisMapSoFar;
        private static int _numBuildingsThisMapSoFar;
        private static MapFinalizeState _state;

        private static bool _finished;

        public FinalizeMap([NotNull] Harmony instance) : base(instance)
        {
        }

        public override string GetStageName()
        {
            return "Initializing Maps";
        }

        public override string? GetCurrentStepName()
        {
            if (_currMapIdx == -1)
                return "Waiting...";

            var mapString = $"Map {_currMapIdx + 1} of {LoadMaps.NumMaps}:";
            return _state switch
            {
                MapFinalizeState.SpawnThings => $"{mapString} Spawning Things ({_numThingsThisMapSoFar} so far)",
                MapFinalizeState.SpawnBuildings => $"{mapString} Spawning Buildings ({_numBuildingsThisMapSoFar} so far)",
                MapFinalizeState.RebuildingPathfindingCache => $"{mapString} Calculating Pathfinding Costs",
                MapFinalizeState.FinalizeGeometry => $"{mapString} Initializing Map Grid",
                MapFinalizeState.InitThings => $"{mapString} Initializing Things",
                MapFinalizeState.ListFilth => $"{mapString} Loading Filth",
                MapFinalizeState.CountResources => $"{mapString} Counting Resources",
                MapFinalizeState.CountWealth => $"{mapString} Counting Wealth",
                MapFinalizeState.InitComponents => $"{mapString} Initializing Sub-Components",
                _ => throw new ArgumentOutOfRangeException()
            };
        }

        public override int GetCurrentProgress()
        {
            if (_currMapIdx == -1)
                return 0;

            var baseMapProgress = _currMapIdx * (int)MapFinalizeState.NumStates;

            return baseMapProgress + (int)_state;
        }

        public override int GetMaximumProgress()
        {
            return LoadMaps.NumMaps * (int) MapFinalizeState.NumStates;
        }

        public override bool IsCompleted()
        {
            return _finished;
        }
        
        public override void BecomeInactive()
        {
            _finished = false;
            _currMapIdx = -1;
            _numThingsThisMapSoFar = 0;
        }

        public override void DoPatching(Harmony instance)
        {
            instance.Patch(AccessTools.Method(typeof(Map), nameof(Map.FinalizeLoading)), new(typeof(FinalizeMap), nameof(OnMapStartFinalizeLoad)), new(typeof(FinalizeMap), nameof(OnMapFinishFinalizeLoad)));
            
            //SpawnThings
            var spawnParamTypes =
#if RIMWORLD_1_5
                new[] {typeof(Thing), typeof(IntVec3), typeof(Map), typeof(Rot4), typeof(WipeMode), typeof(bool), typeof(bool)};
#else
                new[] { typeof(Thing), typeof(IntVec3), typeof(Map), typeof(Rot4), typeof(WipeMode), typeof(bool) };
#endif
            instance.Patch(
                AccessTools.Method(typeof(GenSpawn), nameof(GenSpawn.Spawn), spawnParamTypes),
                new(typeof(FinalizeMap), nameof(OnThingAboutToSpawn))
            );

            //SpawnThings -> SpawnBuildings
            instance.Patch(AccessTools.Method(typeof(GenSpawn), nameof(GenSpawn.SpawnBuildingAsPossible)), new(typeof(FinalizeMap), nameof(OnBuildingAboutToSpawn)));
            
            //SpawnBuildings -> RebuildingPathfindingCache
            instance.Patch(AccessTools.Method(typeof(Map), nameof(Map.FinalizeInit)), new(typeof(FinalizeMap), nameof(OnMapStartFinalizeInit)));
            
            //RebuildingPathfindingCache -> FinalizeGeometry2
            instance.Patch(AccessTools.Method(typeof(RegionAndRoomUpdater), nameof(RegionAndRoomUpdater.RebuildAllRegionsAndRooms)), new(typeof(FinalizeMap), nameof(OnMapStartRebuildRegions)));
            
#if !RIMWORLD_1_3
            //FinalizeGeometry -> InitThings
            instance.Patch(AccessTools.Method(typeof(GasGrid), nameof(GasGrid.RecalculateEverHadGas)), postfix: new(typeof(FinalizeMap), nameof(OnMapFinishFinalizeGeometry)));
#endif
            
            //InitThings -> ListFilth
            instance.Patch(AccessTools.Method(typeof(ListerFilthInHomeArea), nameof(ListerFilthInHomeArea.RebuildAll)), new(typeof(FinalizeMap), nameof(OnMapStartListFilth)));
            
            //ListFilth -> CountResources
            instance.Patch(AccessTools.Method(typeof(ResourceCounter), nameof(ResourceCounter.UpdateResourceCounts)), new(typeof(FinalizeMap), nameof(OnMapStartCountResources)));
            
            //CountResources -> CountWealth
            instance.Patch(AccessTools.Method(typeof(WealthWatcher), nameof(WealthWatcher.ForceRecount)), new(typeof(FinalizeMap), nameof(OnMapStartCountWealth)));
            
            //CountWealth -> FinalizeComponents
            instance.Patch(AccessTools.Method(typeof(MapComponentUtility), nameof(MapComponentUtility.FinalizeInit)), new(typeof(FinalizeMap), nameof(OnMapStartFinalizeComponents)));
            
            //Transition to next map handled by postfix on FinalizeLoad
        }

        public static void OnMapStartFinalizeLoad()
        {
            _currMapIdx++;
            _numThingsThisMapSoFar = 0;
            _state = MapFinalizeState.SpawnThings;
        }

        public static void OnMapFinishFinalizeLoad()
        {
            if (_currMapIdx == LoadMaps.NumMaps - 1) 
                _finished = true;
        }

        public static void OnThingAboutToSpawn()
        {
            _numThingsThisMapSoFar++;
        }

        public static void OnBuildingAboutToSpawn()
        {
            _state = MapFinalizeState.SpawnBuildings;
            _numBuildingsThisMapSoFar++;
        }

        public static void OnMapStartFinalizeInit()
        {
            _state = MapFinalizeState.RebuildingPathfindingCache;
        }

        public static void OnMapStartRebuildRegions()
        {
            _state = MapFinalizeState.FinalizeGeometry;
        }
        
        public static void OnMapFinishFinalizeGeometry()
        {
            _state = MapFinalizeState.InitThings;
        }
        
        public static void OnMapStartListFilth()
        {
            _state = MapFinalizeState.ListFilth;
        }
        
        public static void OnMapStartCountResources()
        {
            _state = MapFinalizeState.CountResources;
        }
        
        public static void OnMapStartCountWealth()
        {
            _state = MapFinalizeState.CountWealth;
        }
        
        public static void OnMapStartFinalizeComponents()
        {
            _state = MapFinalizeState.InitComponents;
        }
    }
}