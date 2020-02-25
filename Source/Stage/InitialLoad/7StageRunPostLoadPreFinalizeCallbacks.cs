using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Harmony;
using Verse;

namespace BetterLoading.Stage.InitialLoad
{
    public class StageRunPostLoadPreFinalizeCallbacks : LoadingStage
    {
        private static int _numTasksToRun = 2;
        private static int _numTasksRun;

        private static Action _currentAction;

        private static bool _hasBeenCalled;

        private static bool _finishedExecuting;
        private static bool _done;


        public StageRunPostLoadPreFinalizeCallbacks(HarmonyInstance instance) : base(instance)
        {
        }

        public override string GetStageName()
        {
            return "Running Post Content Load Callbacks";
        }

        public override string? GetCurrentStepName()
        {
            if (_currentAction == null)
                return "Waiting for vanilla to start processing the task list... *yawn*";

            return (_currentAction.Method.DeclaringType?.FullName ?? "<unknown anonymous method>") + (_currentAction.Target != null ? $" ({_currentAction.Target})" : "");
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
            return _numTasksToRun - 1;
        }

        public override void DoPatching(HarmonyInstance instance)
        {
            instance.Patch(AccessTools.Method(typeof(LongEventHandler), "ExecuteToExecuteWhenFinished"), new HarmonyMethod(typeof(StageRunPostLoadPreFinalizeCallbacks), nameof(PreExecToExecWhenFinished)));
        }


        public static bool PreExecToExecWhenFinished(ref List<Action> ___toExecuteWhenFinished)
        {
            if (_hasBeenCalled) return true;

            _done = false;

            _numTasksToRun = ___toExecuteWhenFinished.Count;

            //Redirect all actions via us 
            Log.Message($"[BetterLoading] Processing {___toExecuteWhenFinished.Count} post-load tasks.");
            // ___toExecuteWhenFinished = ___toExecuteWhenFinished.Select(GetExecAction).ToList();

            _hasBeenCalled = true;

            BetterLoadingMain.LoadingScreen.StartCoroutine
            (
                ToExecuteWhenFinishedHandler.ExecuteToExecuteWhenFinishedTheGoodVersion
                (
                    ___toExecuteWhenFinished,
                    true,
                    currentAction => _currentAction = currentAction,
                    () => _numTasksRun++,
                    () => _finishedExecuting = true
                )
            );

            var finalTask = ___toExecuteWhenFinished.Last();

            LongEventHandler.QueueLongEvent(() =>
            {
                Log.Message("[BetterLoading] Blocking loading screen from being dismissed until post-load actions are complete.");
                Thread.Sleep(1000);

                while (!_finishedExecuting)
                {
                    Thread.Sleep(2000); //Wait
                }
                
                Log.Message($"Obtained synclock, assuming post-load actions are complete and starting static constructors");

                finalTask();

                Thread.Sleep(0);

                _done = true;
            }, null, true, null);
        return false;
    }
}

}