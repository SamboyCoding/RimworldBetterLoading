using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using Verse;

namespace BetterLoading.Stage.InitialLoad
{
    public class StageResolveDefDatabases : LoadingStage
    {
        private static Type? _currentDatabase;
        private static int _numDatabases = 1;
        private static int _currentDatabaseNum;

        private static StageResolveDefDatabases? inst;

        public StageResolveDefDatabases(Harmony instance) : base(instance)
        {
        }

        public override string GetStageName()
        {
            return "Reloading Def Databases";
        }

        public override string GetCurrentStepName()
        {
            return _currentDatabase?.FullName ?? "<initializing>";
        }

        public override int GetCurrentProgress()
        {
            return _currentDatabaseNum;
        }

        public override int GetMaximumProgress()
        {
            return _numDatabases;
        }

        public override bool IsCompleted()
        {
            return _currentDatabaseNum == _numDatabases;
        }

        public override void BecomeActive()
        {
            inst = LoadingScreen.GetStageInstance<StageResolveDefDatabases>();
        }

        public override void BecomeInactive()
        {
            base.BecomeInactive();
            Log.Message("[BetterLoading] Tips should now be available. Showing...");
            LoadingScreenTipManager.GameTipDatabaseHasLoaded = true;
        }

        public override void DoPatching(Harmony instance)
        {
            instance.Patch(
                AccessTools.Method(typeof(GenGeneric), "MethodOnGenericType", new[] {typeof(Type), typeof(Type), typeof(string)}),
                new(typeof(StageResolveDefDatabases), nameof(PreMOGT)),
                new(typeof(StageResolveDefDatabases), nameof(PostMOGT)));

            instance.Patch(AccessTools.Method(typeof(DefGenerator), nameof(DefGenerator.GenerateImpliedDefs_PostResolve)), new(typeof(StageResolveDefDatabases), nameof(PreGenImplied)));
        }

        public static void PreMOGT(Type genericParam, string methodName)
        {
            if (!typeof(Def).IsAssignableFrom(genericParam)) return;

            if (methodName != nameof(DefDatabase<Def>.ResolveAllReferences)) return;

            if (_currentDatabase == null)
                _numDatabases = typeof(Def).AllSubclasses().Count() - 2; //ThingCategoryDef and RecipeDef aren't done

            _currentDatabase = genericParam;
            BetterLoadingApi.DispatchChange(inst);
        }

        public static void PostMOGT(Type genericParam, string methodName)
        {
            if (!typeof(Def).IsAssignableFrom(genericParam)) return;

            if (methodName != nameof(DefDatabase<Def>.ResolveAllReferences)) return;

            _currentDatabaseNum++;

            if (_currentDatabaseNum == _numDatabases - 3)
            {
                _currentDatabase = typeof(ThingDef);
                BetterLoadingApi.DispatchChange(inst);
            }
        }

        public static void PreGenImplied()
        {
            //Finished thingdef database at this point
            _currentDatabaseNum++;
            BetterLoadingApi.DispatchChange(inst);
        }
    }
}