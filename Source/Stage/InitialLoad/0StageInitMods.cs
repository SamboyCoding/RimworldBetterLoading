using System;
using System.Linq;
using System.Runtime.CompilerServices;
using BetterLoading.Compat;
using BetterLoading.Compat.HugsLib;
using HarmonyLib;
using UnityEngine;
using Verse;

namespace BetterLoading.Stage.InitialLoad
{
    public class StageInitMods : LoadingStage
    {
        private static int _numMods = int.MaxValue;

        private static int _currentModIdx = typeof(Mod).InstantiableDescendantsAndSelf().FirstIndexOf(t => t == typeof(BetterLoadingMain)) + 1;
        private static ModContentPack _currentMod = BetterLoadingMain.ourContentPack;
        private static bool _completed;

        private static StageInitMods? inst;

        public StageInitMods(Harmony instance) : base(instance)
        {
        }

        public override string GetStageName()
        {
            return "Initializing Mods";
        }

        public override string? GetCurrentStepName()
        {
            return _currentMod?.Name;
        }

        public override int GetCurrentProgress()
        {
            return _currentModIdx;
        }

        public override void BecomeActive()
        {
            GlobalTimingData.TicksStarted = DateTime.UtcNow.Ticks;
            _numMods = typeof(Mod).InstantiableDescendantsAndSelf().Select(m => m.FullName).Distinct().Count();
            inst = LoadingScreen.GetStageInstance<StageInitMods>();
        }

        public override void BecomeInactive()
        {
            //Do compat here so classes are definitely 100% loaded
            if(HugsLibCompat.ShouldBeLoaded())
                HugsLibCompat.Load();
        }

        public override void DoPatching(Harmony instance)
        {
            instance.Patch(AccessTools.Method(typeof(Activator), nameof(Activator.CreateInstance), new[] {typeof(Type), typeof(object[])}), new(typeof(StageInitMods), nameof(OnActivatorCreateInstance)));
        }

        public override bool IsCompleted()
        {
            _completed = GetCurrentProgress() >= GetMaximumProgress();
            return _completed;
        }

        public override int GetMaximumProgress()
        {
            return _numMods;
        }

        public static void OnActivatorCreateInstance(Type type, params object[] args)
        {
            if (_completed) return; //If we're done, don't go again.
            if (!typeof(Mod).IsAssignableFrom(type)) return; //If we're not constructing a mod bail out.
            if (args.Length != 1 || args[0] is not ModContentPack pack) return; //Check the constructor we're using matches the required pattern.
            // Log.Message($"[BetterLoading] Class {type?.FullName} is being created for mod {pack}");

            _currentModIdx++;
            _currentMod = pack;
            BetterLoadingApi.DispatchChange(inst);
        }
    }
}