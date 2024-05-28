using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using HarmonyLib;
using RimWorld;
using Verse;

namespace BetterLoading.Stage.InitialLoad
{
    public class StageRunPostLoadPreFinalizeCallbacks : LoadingStage
    {
        private static int _numTasksToRun = 2;
        private static int _numTasksRun;

        private static Action? _currentAction;

        private static bool _hasBeenCalled;

        private static bool _finishedExecuting;
        private static bool _done;

        private static StageRunPostLoadPreFinalizeCallbacks? inst;


        public StageRunPostLoadPreFinalizeCallbacks(Harmony instance) : base(instance)
        {
        }

        public override string GetStageName()
        {
            return "Running Pre-Static-Constructor Long Events";
        }

        public override string? GetCurrentStepName()
        {
            if (_currentAction == null)
                return "Waiting for tasks to start being processed...";

#if RIMWORLD_1_5
            if (_currentAction.Method is { } method && method.DeclaringType?.DeclaringType == typeof(ModContentPack))
            {
                //On 1.4 this Action was directly just a call to ModContentPack.ReloadContentInt.
                //Now on 1.5 it's an anon method wrapping that call due to a hotReload bool being added.
                //Let's try to grab the mod if we can
                var anonTypeInst = _currentAction.Target;
                //Compiler generated field `<>4__this` is the ModContentPack itself.
                if (anonTypeInst.GetType()?.GetField("<>4__this", AccessTools.all) is { } theField && theField.GetValue(anonTypeInst) is ModContentPack mcp)
                    return $"Reloading content for {mcp.Name}";
            }
#else
            if (_currentAction.Method.DeclaringType == typeof(ModContentPack) && _currentAction.Method.Name == nameof(ModContentPack.ReloadContent) + "Int" && _currentAction.Target is ModContentPack mcp)
            {
                return $"Reloading content for {mcp.Name}";
            }
#endif

            var methodDeclaringType = _currentAction.Method.DeclaringType?.FullName ?? "<unknown anonymous method>";
            var methodName = _currentAction.Method?.Name ?? "<unknown method>";
            var target = _currentAction.Target != null ? $"{_currentAction.Target}" : "";
            return $"{methodDeclaringType}::{methodName} (on instance {target})";
        }

        public override bool IsCompleted()
        {
            return _done;
        }

        public override int GetCurrentProgress()
        {
            return _numTasksRun;
        }

        public override int GetMaximumProgress()
        {
            return _numTasksToRun;
        }

        public override void BecomeActive()
        {
            inst = LoadingScreen.GetStageInstance<StageRunPostLoadPreFinalizeCallbacks>();
        }

        public override void BecomeInactive()
        {
            BetterLoadingMain.LoadingScreen!.PickBackground();
        }

        public override void DoPatching(Harmony instance)
        {
            instance.Patch(AccessTools.Method(typeof(LongEventHandler), "ExecuteToExecuteWhenFinished"), new(typeof(StageRunPostLoadPreFinalizeCallbacks), nameof(PreExecToExecWhenFinished)));
            instance.Patch(AccessTools.Method(typeof(LongEventHandler), "UpdateCurrentSynchronousEvent"), new(typeof(StageRunPostLoadPreFinalizeCallbacks), nameof(PreUpdateCurrentSynchronousEvent)));
        }

        public static bool PreExecToExecWhenFinished(List<Action> ___toExecuteWhenFinished)
        {
            if (_hasBeenCalled)
            {
                //Don't let normal ExecuteToExecuteWhenFinished run while we're still executing, to avoid "Already executing" warnings
                return _finishedExecuting;
            }

            _done = false;

            //Redirect all actions via us 
            Log.Message($"[BetterLoading] Processing {___toExecuteWhenFinished.Count} post-load tasks.");
            // ___toExecuteWhenFinished = ___toExecuteWhenFinished.Select(GetExecAction).ToList();

            _hasBeenCalled = true;

            var targetTypeName = typeof(PlayDataLoader).FullName ?? throw new("WTF where has playdataloader gone.");

            // var last = ___toExecuteWhenFinished.Skip(2900).Take(int.MaxValue).Select(i => i.Method.DeclaringType).ToList();
            // Debug.Log($"BL Debug: last few task defining types: {last.ToStringSafeEnumerable()}");

            // Debug.Log($"BL Debug: Looking for actions defined in type beginning with {targetTypeName}");

            var declaredInPDL = ___toExecuteWhenFinished.Where(task => task.Method.DeclaringType?.FullName?.StartsWith(targetTypeName) == true).ToList();

            // Debug.Log($"BL Debug: types declared in PDL: {declaredInPDL.Select(a => a.Method).ToStringSafeEnumerable()}");

            var targetMethodName =
//#if RIMWORLD_1_2
                //"b__4_2"
#if RIMWORLD_1_3
                "b__4_3";
#elif RIMWORLD_1_4
                "b__4_5";
#elif RIMWORLD_1_5
                "b__4_4";
#else
                #error Missing targetMethodName for this version
#endif

            // Log.Message($"BL Debug: Tasks defined in PDL: {string.Join(", ", declaredInPDL.Select(task => task.Method.FullDescription()))}");

            var indexOfStaticCtor = ___toExecuteWhenFinished.IndexOf(declaredInPDL.Find(task => task.Method.Name.Contains(targetMethodName))); //The anon class that calls static ctors.

            // Log.Message($"BL Debug: Identified target index as {indexOfStaticCtor} which maps to the action-method {___toExecuteWhenFinished[indexOfStaticCtor].Method.FullDescription()}");

            //Ones to execute now are the ones before the ctors
            var toExecute = ___toExecuteWhenFinished.Take(indexOfStaticCtor).ToList();

            _numTasksToRun = toExecute.Count;

            //This completely skips the static constructor task, which we manually run, so get a reference to that
            var runStaticCtors = ___toExecuteWhenFinished[indexOfStaticCtor];

            //To execute after are the ones after the ctors - if there are any.
            var remainder = ___toExecuteWhenFinished.Skip(indexOfStaticCtor + 1).Take(int.MaxValue).ToList();

            // Debug.Log($"BL Debug: This leaves {toExecute.Count} tasks to execute now, that one to execute in the middle, and then {remainder.Count} to execute after static ctors");

            LongEventHandlerMirror.ToExecuteWhenFinished = remainder;

            BetterLoadingMain.LoadingScreen!.StartCoroutine
            (
                ToExecuteWhenFinishedHandler.ExecuteToExecuteWhenFinishedTheGoodVersion
                (
                    toExecute,
                    false,
                    currentAction => _currentAction = currentAction,
                    () =>
                    {
                        _numTasksRun++;
                        BetterLoadingApi.DispatchChange(inst);
                    },
                    () => _finishedExecuting = true
                )
            );

            LongEventHandler.QueueLongEvent(() =>
            {
                // Log.Message("[BetterLoading] Blocking loading screen from being dismissed until post-load actions are complete.");
                Thread.Sleep(0);

                while (!_finishedExecuting)
                {
                    Thread.Sleep(0); //Wait
                }

                Log.Message($"[BetterLoading] Obtained synclock, assuming post-load actions are complete and starting static constructors.");

                _done = true;

                runStaticCtors();
            }, null, false, null);

            return false;
        }

        public static bool PreUpdateCurrentSynchronousEvent( /*object ___currentEvent*/)
        {
            if (_hasBeenCalled)
            {
                //Don't let normal UpdateCurrentSynchronousEvent run while we're still executing, since some loading logic can rely on ExecuteToExecuteWhenFinished running synchronously
                //(such as Steam client missing dialog box failing due to SubSoundDef.resolvedGrains not resolved in an ExecuteWhenFinished action yet)
                return _finishedExecuting;
            }

            return true;
        }
    }
}