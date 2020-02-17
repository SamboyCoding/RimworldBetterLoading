using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BetterLoading.Stage;
using Harmony;
using HugsLib;
using HugsLib.News;
using Verse;

namespace BetterLoading.Compat.HugsLib
{
    public class StageHugsLibInit : LoadingStage
    {
        private static bool _hasEnumeratedChildren;
        private static bool _done;

        private static List<object> _children;
        private static int _numChildrenInitialized;
        private static int _numChildrenCheckedForUpdate;
        private static int _numChildrenDefLoaded;

        private static object _currentChildMod;
        private static HarmonyInstance hInstance;

        private static PropertyInfo _modIdentifierProperty;

        public StageHugsLibInit(HarmonyInstance instance) : base(instance)
        {
        }

        public override string GetStageName()
        {
            return "Initializing HugsLib";
        }

        public override void BecomeActive()
        {
            Log.Message($"At becomeActive, toexecutewhenfinished has {LongEventHandlerMirror.ToExecuteWhenFinished.Count} items and eventqueue has {LongEventHandlerMirror.EventQueue.Count} items ");
        }

        public override string? GetCurrentStepName()
        {
            if (!_hasEnumeratedChildren)
                return "Waiting for HugsLib child mod list (probably vanilla being slow again)";

            if (_currentChildMod != null)
            {
                if (_numChildrenInitialized < _children.Count)
                    return $"Initializing child mods: {_numChildrenInitialized} / {_children}: {_modIdentifierProperty.GetValue(_currentChildMod, null)}";

                if (_numChildrenCheckedForUpdate < _children.Count)
                    return $"Checking for mod updates: {_numChildrenCheckedForUpdate} / {_children}: {_modIdentifierProperty.GetValue(_currentChildMod, null)}";

                if (_numChildrenDefLoaded < _children.Count)
                    return $"Invoking post-load callbacks: {_numChildrenDefLoaded} / {_children}: {_modIdentifierProperty.GetValue(_currentChildMod, null)}";
            }

            return _numChildrenDefLoaded == _children.Count ? "Finishing up" : null;
        }

        public override int GetCurrentProgress()
        {
            var result = _hasEnumeratedChildren ? 1 : 0;

            result += _numChildrenInitialized;
            result += _numChildrenCheckedForUpdate;
            result += _numChildrenDefLoaded;
            result += _done ? 1 : 0;

            return Math.Min(result, GetMaximumProgress());
        }

        public override int GetMaximumProgress()
        {
            //Enumerate, init, update, callback
            var childCount = _children?.Count ?? 0;
            return 1 + childCount + childCount + childCount;
        }

        public override void DoPatching(HarmonyInstance instance)
        {
            hInstance = instance;

            var hlAssembly = LoadedModManager.RunningMods.First(m => m.Name == "HugsLib").assemblies.loadedAssemblies.Find(a => a.GetName().Name == "HugsLib");

            var controllerType = hlAssembly.GetTypes().First(t => t.Name == "HugsLibController");
            var updateFeatureManagerType = hlAssembly.GetTypes().First(t => t.Name == "UpdateFeatureManager");
            
            _modIdentifierProperty = hlAssembly.GetTypes().First(t => t.Name == "ModBase").GetProperty("ModIdentifier");
            
            Log.Message($"[BetterLoading:HugsLib Compat] Resolved required hugslib types as follows: Controller: {controllerType?.FullName} / Update Manager: {updateFeatureManagerType?.FullName} / Mod Identifier: {_modIdentifierProperty?.Name}");

            hInstance.Patch(AccessTools.Method(controllerType, "LoadReloadInitialize"), postfix: new HarmonyMethod(typeof(StageHugsLibInit), nameof(PostLRI)));
            hInstance.Patch(AccessTools.Method(controllerType, "EnumerateChildMods"), postfix: new HarmonyMethod(typeof(StageHugsLibInit), nameof(PostEnumerateChildren)));
            hInstance.Patch(
                AccessTools.Method(updateFeatureManagerType, nameof(UpdateFeatureManager.InspectActiveMod)),
                new HarmonyMethod(typeof(StageHugsLibInit), nameof(PreUpdateCheck)),
                new HarmonyMethod(typeof(StageHugsLibInit), nameof(PostUpdateCheck))
            );
            
            Log.Message("[BetterLoading:HugsLib] Successfully blind-patched hugslib.");
        }

        public static void PostLRI()
        {
            _done = true;
        }

        public static void PostEnumerateChildren(object ___childMods, Dictionary<Assembly, ModContentPack> ___assemblyContentPacks)
        {
            if (!(___childMods is IEnumerable enumerable)) return;

            _children = enumerable.GetEnumerator().ToIEnumerable<object>().Where(m => ___assemblyContentPacks.ContainsKey(m.GetType().Assembly)).ToList();
            _hasEnumeratedChildren = true;

            foreach (var childMod in _children)
            {
                hInstance.Patch(AccessTools.Method(childMod.GetType(), nameof(ModBase.Initialize)), new HarmonyMethod(typeof(StageHugsLibInit), nameof(PreChildInit)), new HarmonyMethod(typeof(StageHugsLibInit), nameof(PostChildInit)));
                hInstance.Patch(AccessTools.Method(childMod.GetType(), nameof(ModBase.DefsLoaded)), new HarmonyMethod(typeof(StageHugsLibInit), nameof(PreDefsLoaded)), new HarmonyMethod(typeof(StageHugsLibInit), nameof(PostDefsLoaded)));
            }
        }

        public static void PreChildInit(object __instance)
        {
            if (__instance.GetType().GetProperty("ModIdentifier") == null) return;

            _currentChildMod = __instance;
        }

        public static void PostChildInit()
        {
            _numChildrenInitialized++;
        }

        public static void PreUpdateCheck(string modId)
        {
            _currentChildMod = _children?.Find(m => (string) _modIdentifierProperty.GetValue(m, null) == modId);
        }

        public static void PostUpdateCheck()
        {
            _numChildrenCheckedForUpdate++;
        }

        public static void PreDefsLoaded(object __instance)
        {
            if (__instance.GetType().GetProperty("ModIdentifier") == null) return;

            _currentChildMod = __instance;
        }

        public static void PostDefsLoaded()
        {
            _numChildrenDefLoaded++;
        }
    }
}