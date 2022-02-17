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

        private static Action? _currentAction;

        private static bool _hasBeenCalled;

        private static bool _finishedExecuting;

        private static StageRunPostFinalizeCallbacks? inst;

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
                return "Waiting...";

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

        public override void BecomeActive()
        {
            inst = LoadingScreen.GetStageInstance<StageRunPostFinalizeCallbacks>();
        }

        public override void DoPatching(Harmony instance)
        {
            instance.Patch(AccessTools.Method(typeof(LongEventHandler), "ExecuteToExecuteWhenFinished"), new(typeof(StageRunPostFinalizeCallbacks), nameof(PreExecToExecWhenFinished)));
            instance.Patch(AccessTools.Method(typeof(LongEventHandler), "UpdateCurrentSynchronousEvent"), new(typeof(StageRunPostFinalizeCallbacks), nameof(PreUpdateCurrentSynchronousEvent)));
        }

        public static bool PreExecToExecWhenFinished(List<Action> ___toExecuteWhenFinished)
        {
            if (!ShouldInterceptNext) return true;
            if (_hasBeenCalled)
            {
                //Don't let normal ExecuteToExecuteWhenFinished run while we're still executing, to avoid "Already executing" warnings
                return _finishedExecuting;
            }

            GlobalTimingData.TicksStartedPostFinalize = DateTime.UtcNow.Ticks;

            _hasBeenCalled = true;
            ShouldInterceptNext = false;

            _finishedExecuting = false;

            if (___toExecuteWhenFinished.Count == 0)
            {
                _numTasksToRun = 1;
                _finishedExecuting = true;
                return false;
            }

            _numTasksToRun = ___toExecuteWhenFinished.Count;

            var initialNumTasksToRun = _numTasksToRun;
            Log.Message($"[BetterLoading] Processing {initialNumTasksToRun} post-finalize tasks.");

            BetterLoadingMain.LoadingScreen.StartCoroutine
            (
                ToExecuteWhenFinishedHandler.ExecuteToExecuteWhenFinishedTheGoodVersion
                (
                    ___toExecuteWhenFinished,
                    false,
                    currentAction => _currentAction = currentAction,
                    () =>
                    {
                        _numTasksRun++;
                        //toExecuteWhenFinished actions themselves can call ExecuteWhenFinished and thus increase toExecuteWhenFinished count
                        _numTasksToRun = LongEventHandlerMirror.ToExecuteWhenFinished.Count;
                        BetterLoadingApi.DispatchChange(inst);
                    },
                    () =>
                    {
                        if (initialNumTasksToRun != _numTasksToRun)
                            Log.Message($"[BetterLoading] Processed an additional {_numTasksToRun - initialNumTasksToRun} post-finalize tasks.");
                        _finishedExecuting = true;
                    })
            );

            LongEventHandler.QueueLongEvent(() =>
            {
                Thread.Sleep(500);

                // Log.Message("[BetterLoading] Blocking loading screen from being dismissed until post-finalize actions are complete.");
                while (!_finishedExecuting)
                {
                    Thread.Sleep(500); //Wait
                }

                Log.Message("[BetterLoading] Obtained lock, assuming we're done with post-finalize.");

                Thread.Sleep(0);
                GlobalTimingData.TicksFinishedPostFinalize = DateTime.UtcNow.Ticks;

                _finishedExecuting = true;
            }, null, true, null);
            return false;
        }

        public static bool PreUpdateCurrentSynchronousEvent(/*object ___currentEvent*/)
        {
            if (_hasBeenCalled)
            {
                //Don't let normal UpdateCurrentSynchronousEvent run while we're still executing, since some loading logic can rely on ExecuteToExecuteWhenFinished running synchronously
                return _finishedExecuting;
            }
            return true;
        }
    }
}