using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using HarmonyLib;
using UnityEngine;
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


        public StageRunPostLoadPreFinalizeCallbacks(Harmony instance) : base(instance)
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
            return _numTasksToRun;
        }

        public override void DoPatching(Harmony instance)
        {
            instance.Patch(AccessTools.Method(typeof(LongEventHandler), "ExecuteToExecuteWhenFinished"), new HarmonyMethod(typeof(StageRunPostLoadPreFinalizeCallbacks), nameof(PreExecToExecWhenFinished)));
        }


        public static bool PreExecToExecWhenFinished(ref List<Action> ___toExecuteWhenFinished)
        {
            if (_hasBeenCalled) return true;

            _done = false;
            
            //Redirect all actions via us 
            Log.Message($"[BetterLoading] Processing {___toExecuteWhenFinished.Count} post-load tasks.");
            // ___toExecuteWhenFinished = ___toExecuteWhenFinished.Select(GetExecAction).ToList();

            _hasBeenCalled = true;

            var targetTypeName = typeof(PlayDataLoader).FullName ?? throw new Exception("WTF where has playdataloader gone.");

            var last = ___toExecuteWhenFinished.Skip(2900).Take(int.MaxValue).Select(i => i.Method.DeclaringType).ToList();
            
            Debug.Log($"BL Debug: last few task defining types: {last.ToStringSafeEnumerable()}");
            
            Debug.Log($"BL Debug: Looking for actions defined in type beginning with {targetTypeName}");

            var declaredInPDL = ___toExecuteWhenFinished.Where(task => task.Method.DeclaringType?.FullName?.StartsWith(targetTypeName) == true).ToList();
            
            Debug.Log($"BL Debug: types declared in PDL: {declaredInPDL.Select(a => a.Method).ToStringSafeEnumerable()}");

            var indexOfStaticCtor = ___toExecuteWhenFinished.IndexOf(declaredInPDL.Find(task => task.Method.Name.Contains("b__4_2"))); //The anon class that calls static ctors.
            
            Debug.Log($"BL Debug: Identified target index as {indexOfStaticCtor} which maps to the action-method {___toExecuteWhenFinished[indexOfStaticCtor].Method.FullDescription()}");

            //Ones to execute now are the ones before the ctors
            var toExecute = ___toExecuteWhenFinished.Take(indexOfStaticCtor).ToList();

            _numTasksToRun = toExecute.Count;
            
            //This completely skips the static constructor task, which we manually run, so get a reference to that
            var runStaticCtors = ___toExecuteWhenFinished[indexOfStaticCtor];

            //To execute after are the ones after the ctors - if there are any.
            var remainder = ___toExecuteWhenFinished.Skip(indexOfStaticCtor + 1).Take(int.MaxValue).ToList();
            
            Debug.Log($"BL Debug: This leaves {toExecute.Count} tasks to execute now, that one to execute in the middle, and then {remainder.Count} to execute after static ctors");

            LongEventHandlerMirror.ToExecuteWhenFinished = remainder;

            BetterLoadingMain.LoadingScreen.StartCoroutine
            (
                ToExecuteWhenFinishedHandler.ExecuteToExecuteWhenFinishedTheGoodVersion
                (
                    toExecute,
                    false,
                    currentAction => _currentAction = currentAction,
                    () => _numTasksRun++,
                    () => _finishedExecuting = true
                )
            );

            LongEventHandler.QueueLongEvent(() =>
            {
                Log.Message("[BetterLoading] Blocking loading screen from being dismissed until post-load actions are complete.");
                Thread.Sleep(1000);

                while (!_finishedExecuting)
                {
                    Thread.Sleep(2000); //Wait
                }
                
                Log.Message($"Obtained synclock, assuming post-load actions are complete and starting static constructors");

                runStaticCtors();

                Thread.Sleep(0);

                _done = true;
            }, null, true, null);
        return false;
    }
}

}