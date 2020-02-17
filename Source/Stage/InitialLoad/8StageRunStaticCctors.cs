using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using HarmonyLib;
using UnityEngine;
using Verse;

namespace BetterLoading.Stage.InitialLoad
{
    public class StageRunStaticCctors : LoadingStage
    {
        private static Type? _modType;
        private static List<Type> _toRun;
        private static int _numRun;

        private static List<Action> _queue;

        private static bool _finishedProcessing;

        public StageRunStaticCctors(Harmony instance) : base(instance)
        {
        }

        public override string GetStageName()
        {
            return "Finalizing Mods";
        }

        public override string? GetCurrentStepName()
        {
            return _modType?.FullName ?? "Waiting for vanilla to finish being slow...";
        }

        public override int GetCurrentProgress()
        {
            return _numRun;
        }

        public override int GetMaximumProgress()
        {
            return _toRun?.Count ?? 1;
        }

        public override bool IsCompleted()
        {
            return _numRun == _toRun?.Count;
        }

        public override void DoPatching(Harmony instance)
        {
            instance.Patch(AccessTools.Method(typeof(StaticConstructorOnStartupUtility), nameof(StaticConstructorOnStartupUtility.CallAll)), new HarmonyMethod(typeof(StageRunStaticCctors), nameof(PreCallAll)));
            // instance.Patch(AccessTools.Method(typeof(RuntimeHelpers), nameof(RuntimeHelpers.RunClassConstructor), new []{typeof(RuntimeTypeHandle)}), new HarmonyMethod(typeof(StageRunStaticCctors), nameof(PreRunClassConstructor)));
        }

        public static IEnumerator StaticConstructAll()
        {
            Log.Message("[BetterLoading] Starting Antifreeze(tm) StaticConstructorCaller. Synchronizing retransmission chronicity...");
            Application.runInBackground = true;
            Log.Message("[BetterLoading] Contact Engaged. Here goes nothing...");
            foreach (var type in _toRun)
            {
                _modType = type;

                RuntimeHelpers.RunClassConstructor(type.TypeHandle);

                _numRun++;

                yield return null;
            }

            Application.runInBackground = Prefs.RunInBackground;

            try
            {
                Log.Message("[BetterLoading] Finished calling static constructors at " + DateTime.Now.ToLongTimeString() + ". AND I didn't make the game freeze. Take that, Tynan.");
                var existing = LongEventHandlerMirror.ToExecuteWhenFinished;

                Log.Message($"[BetterLoading] Restoring original job queue of {_queue.Count} item/s and merging with any just added (looking at you, Fluffy) ({existing.Count} entries have been added).");
                if (existing.Count > 0 && _queue.Count == 0)
                {
                    //This is probably usually the case
                    //Don't touch anything
                }
                else if (existing.Count == 0 && _queue.Count > 0)
                {
                    //Load cached stuff from queue
                    LongEventHandlerMirror.ToExecuteWhenFinished = _queue;
                }
                else
                {
                    //Need to merge - queue first
                    var result = _queue;
                    result.AddRange(existing);
                    LongEventHandlerMirror.ToExecuteWhenFinished = result;
                }

                _queue = null;

                Log.Message($"[BetterLoading] Job queue restored. Running GC...");

                StaticConstructorOnStartupUtility.coreStaticAssetsLoaded = true;

                GC.Collect(int.MaxValue, GCCollectionMode.Forced); //Copied from PlayDataLoader

                Log.Message($"[BetterLoading] GC Has completed. Relinquishing lock.");
            }
            catch (Exception e)
            {
                Debug.LogError("Exception occurred, releasing lock! " + e);
            }
            finally
            {
                StageRunPostFinalizeCallbacks.ShouldInterceptNext = true;
                _finishedProcessing = true;
                Log.Message("[BetterLoading] Lock released.");
            }
        }

        public static bool PreCallAll()
        {
            Log.Message("Static constructors? Oh, sit down, vanilla, I'll do it myself. Starting now, at " + DateTime.Now.ToLongTimeString(), true);
            _toRun = GenTypes.AllTypesWithAttribute<StaticConstructorOnStartup>().ToList();

            BetterLoadingMain.LoadingScreen.StartCoroutine(StaticConstructAll());

            Log.Message("[BetterLoading] Overriding LongEventHandler's toExecuteWhenFinished", true);
            
            var result = LongEventHandlerMirror.ToExecuteWhenFinished;

            Log.Message($"[BetterLoading]    Got list of pending actions: {result}. Removing up to and including the static constructor call...", true);

            var staticCallIdx = result.FindIndex(i => i.Method.DeclaringType?.Name == "PlayDataLoader" && i.Method.Name.Contains("m__2"));

            Log.Message($"[BetterLoading]        (Which is at index {staticCallIdx} of {result.Count})", true);

            result = result.Skip(staticCallIdx + 1).Take(int.MaxValue).ToList(); //Remove the static constructor call

            _queue = result;

            Log.Message($"[BetterLoading]     Updating field in LEH to a new list of size {result.Count}...", true);
            LongEventHandlerMirror.ToExecuteWhenFinished = new List<Action>();

            LongEventHandler.QueueLongEvent(WaitForStaticCtors, null, true, null);

            return false;
        }

        private static void WaitForStaticCtors()
        {
            //Called async so can just block
            Log.Message("[BetterLoading] Blocking LEH until static ctors finish", true);
            Thread.Sleep(1000);
            Log.Message("[BetterLoading] Awaiting release of lock...");
            
            while(!_finishedProcessing){Thread.Sleep(2000);} //wait for sync lock to be available
            
            Log.Message("[BetterLoading] Lock released, assuming we're finished calling static ctors", true);
            Thread.Sleep(0);
            
        }
    }
}