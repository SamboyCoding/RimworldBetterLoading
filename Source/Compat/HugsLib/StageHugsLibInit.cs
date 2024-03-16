using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BetterLoading.Stage;
using HarmonyLib;
using Verse;

namespace BetterLoading.Compat.HugsLib
{
    public class StageHugsLibInit : LoadingStage
    {
        private static bool _hasEnumeratedChildren;
        private static bool _done;

        private static List<object>? _children;
        private static int _numChildrenInitialized;
        private static int _numChildrenDefLoaded;

        private static object? _currentChildMod;
        private static Harmony? hInstance;

        private static PropertyInfo _modIdentifierProperty = null!;

        private static StageHugsLibInit? inst;

        public StageHugsLibInit(Harmony instance) : base(instance)
        {
        }

        public override string GetStageName()
        {
            return "Initializing HugsLib";
        }

        public override string? GetCurrentStepName()
        {
            if (!_hasEnumeratedChildren)
                return "Waiting for HugsLib child mod list";

            if (_currentChildMod != null)
            {
                if (_numChildrenInitialized < _children!.Count)
                    return $"Initializing child mods: {_numChildrenInitialized} / {_children}: {_modIdentifierProperty.GetValue(_currentChildMod, null)}";

                if (_numChildrenDefLoaded < _children.Count)
                    return $"Invoking post-load callbacks: {_numChildrenDefLoaded} / {_children}: {_modIdentifierProperty.GetValue(_currentChildMod, null)}";
            }

            return _numChildrenDefLoaded == _children!.Count ? "Finishing up" : null;
        }

        public override int GetCurrentProgress()
        {
            if (_done) return GetMaximumProgress() + 1;

            var result = _hasEnumeratedChildren ? 1 : 0;

            result += _numChildrenInitialized;
            result += _numChildrenDefLoaded;
            // result += _done ? 1 : 0;

            return Math.Min(result, GetMaximumProgress());
        }

        public override int GetMaximumProgress()
        {
            //Enumerate, init, update, callback
            var childCount = _children?.Count ?? 0;
            return 1 + childCount + childCount + childCount;
        }

        public override void BecomeActive()
        {
            inst = LoadingScreen.GetStageInstance<StageHugsLibInit>();
        }

        public override void DoPatching(Harmony instance)
        {
            hInstance = instance;

            var hlAssembly = LoadedModManager.RunningMods.First(m => m.Name == "HugsLib").assemblies.loadedAssemblies.Find(a => a.GetName().Name == "HugsLib");

            var controllerType = hlAssembly.GetTypes().FirstOrDefault(t => t.Name == "HugsLibController") ?? throw new("Type HugsLibController is missing");

            _modIdentifierProperty = hlAssembly.GetTypes().First(t => t.Name == "ModBase").GetProperty("ModIdentifier") ?? throw new("Property ModBase.ModIdentifier is missing");

            Log.Message($"[BetterLoading:HugsLib Compat] Resolved required HugsLib types as follows: Controller: {controllerType.FullName} / Mod Identifier (Property): {_modIdentifierProperty.Name}");

            hInstance.Patch(
                AccessTools.Method(controllerType, "LoadReloadInitialize") ?? throw new("Method HugsLibController.LoadReloadInitialize is missing"),
                postfix: new(typeof(StageHugsLibInit), nameof(PostLRI))
            );
            hInstance.Patch(
                AccessTools.Method(controllerType, "EnumerateChildMods")  ?? throw new("Method HugsLibController.EnumerateChildMods is missing"),
                postfix: new(typeof(StageHugsLibInit), nameof(PostEnumerateChildren))
            );

            Log.Message("[BetterLoading:HugsLib Compat] Successfully blind-patched HugsLib.");
        }

        public static void PostLRI()
        {
            _done = true;
            Log.Message("[BetterLoading:HugsLib Compat] HugsLib has Loaded, Reloaded, and Initialized. Marking as done.");
        }

        public static void PostEnumerateChildren(object ___childMods, Dictionary<Assembly, ModContentPack> ___assemblyContentPacks)
        {
            if (!(___childMods is IEnumerable enumerable)) return;

            var children = enumerable.GetEnumerator().ToIEnumerable<object>().ToList();

            Log.Message($"[BetterLoading:HugsLib Compat] HugsLib has enumerated child mods, it has found {children.Count} of them.");

            _children = children.Where(m => ___assemblyContentPacks.ContainsKey(m.GetType().Assembly)).ToList();
            _hasEnumeratedChildren = true;

            foreach (var childMod in _children)
            {
                if (childMod.GetType().DeclaresOwnMethod("Initialize"))
                    hInstance!.Patch(AccessTools.Method(childMod.GetType(), "Initialize"), new(typeof(StageHugsLibInit), nameof(PreChildInit)), new(typeof(StageHugsLibInit), nameof(PostChildInit)));

                if (childMod.GetType().DeclaresOwnMethod("DefsLoaded"))
                    hInstance!.Patch(AccessTools.Method(childMod.GetType(), "DefsLoaded"), new(typeof(StageHugsLibInit), nameof(PreDefsLoaded)), new(typeof(StageHugsLibInit), nameof(PostDefsLoaded)));
            }
        }

        public static void PreChildInit(object __instance)
        {
            if (__instance.GetType().GetProperty("ModIdentifier") == null) return;

            _currentChildMod = __instance;
            BetterLoadingApi.DispatchChange(inst);
        }

        public static void PostChildInit()
        {
            _numChildrenInitialized++;
            BetterLoadingApi.DispatchChange(inst);
        }

        public static void PreDefsLoaded(object __instance)
        {
            if (__instance.GetType().GetProperty("ModIdentifier") == null) return;

            _currentChildMod = __instance;
            BetterLoadingApi.DispatchChange(inst);
        }

        public static void PostDefsLoaded()
        {
            _numChildrenDefLoaded++;
            BetterLoadingApi.DispatchChange(inst);
        }
    }
}