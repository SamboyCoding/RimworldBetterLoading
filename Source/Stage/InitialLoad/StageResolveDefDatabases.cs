using System;
using System.Linq;
using HarmonyLib;
using Verse;

namespace BetterLoading.Stage.InitialLoad
{
    public class StageResolveDefDatabases : LoadingStage
    {
        private static Type _currentDatabase;
        private static int _numDatabases = 1;
        private static int _currentDatabaseNum;

        private static bool _isActive;


        public StageResolveDefDatabases(Harmony instance) : base(instance)
        {
        }

        public override string GetStageName()
        {
            return "Reloading Def Databases...";
        }

        public override void BecomeActive()
        {
            _isActive = true;
        }

        public override void BecomeInactive()
        {
            _isActive = false;
        }

        public override string? GetCurrentStepName()
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

        public override void DoPatching(Harmony instance)
        {
            instance.Patch(AccessTools.Method(typeof(GenGeneric), "MethodOnGenericType", new[] {typeof(Type), typeof(Type), typeof(string)}), new HarmonyMethod(typeof(StageResolveDefDatabases), nameof(PreMOGT)));
        }

        public static void PreMOGT(Type genericParam, string methodName)
        {
            if (!typeof(Def).IsAssignableFrom(genericParam)) return;

            if (methodName != nameof(DefDatabase<Def>.ResolveAllReferences)) return;

            if (_currentDatabase == null)
                _numDatabases = typeof(Def).AllSubclasses().Count() - 1;

            _currentDatabase = genericParam;
            _currentDatabaseNum++;
        }
    }
}