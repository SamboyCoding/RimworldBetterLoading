using System;
using System.Collections.Generic;
using System.Threading;
using HarmonyLib;
using Verse;

namespace BetterLoading.Stage.InitialLoad
{
    public class StageRunPostFinalizeCallbacks : LoadingStage
    {
        public static bool ShouldInterceptNext;

        private static int _numTasksToRun = 2;
        private static int _numTasksRun;

        private static Action _currentAction;

        private static bool _hasBeenCalled;

        private static bool _finishedExecuting;

        public StageRunPostFinalizeCallbacks(Harmony instance) : base(instance)
        {
        }

        public override string GetStageName()
        {
            return "Running Post-Finalize Callbacks";
        }

        public override string? GetCurrentStepName()
        {
            if (_currentAction == null)
                return "Waiting for vanilla... again";

            return (_currentAction.Method.DeclaringType?.FullName ?? "<unknown anonymous method>") + (_currentAction.Target != null ? $" ({_currentAction.Target})" : "");
        }

        public override bool IsCompleted()
        {
            return _finishedExecuting;
        }

        public override int GetCurrentProgress()
        {
            return _numTasksRun;
        }

        public override int GetMaximumProgress()
        {
            return _numTasksToRun;
        }

        public override void DoPatching(Harmony instance)
        {
            instance.Patch(AccessTools.Method(typeof(LongEventHandler), "ExecuteToExecuteWhenFinished"), new HarmonyMethod(typeof(StageRunPostFinalizeCallbacks), nameof(PreExecToExecWhenFinished)));
        }

        public static bool PreExecToExecWhenFinished(ref List<Action> ___toExecuteWhenFinished)
        {
            if (_hasBeenCalled || !ShouldInterceptNext) return true;

            _hasBeenCalled = true;
            ShouldInterceptNext = false;
            _numTasksToRun = ___toExecuteWhenFinished.Count;

            _finishedExecuting = false;

            if (_numTasksToRun == 0)
            {
                _numTasksToRun = 1;
                _finishedExecuting = true;
                return false;
            }
            
            Log.Message($"[BetterLoading] Processing {___toExecuteWhenFinished.Count} post-finalize tasks.");

            BetterLoadingMain.LoadingScreen.StartCoroutine
            (
                ToExecuteWhenFinishedHandler.ExecuteToExecuteWhenFinishedTheGoodVersion
                (
                    ___toExecuteWhenFinished,
                    false,
                    currentAction => _currentAction = currentAction,
                    () => _numTasksRun++,
                    () =>
                    {
                        Log.Message("[BetterLoading] Finished post-finalize callbacks, releasing lock.");
                        _finishedExecuting = true;
                    })
            );

            LongEventHandler.QueueLongEvent(() =>
            {
                Thread.Sleep(500);

                Log.Message("[BetterLoading] Blocking loading screen from being dismissed until post-finalize actions are complete.");
                while (!_finishedExecuting)
                {
                    Thread.Sleep(500); //Wait
                }

                Log.Message("Obtained lock, assuming we're done with post-finalize.");

                Thread.Sleep(0);

                _finishedExecuting = true;
            }, null, true, null);
            return false;
        }
    }
}