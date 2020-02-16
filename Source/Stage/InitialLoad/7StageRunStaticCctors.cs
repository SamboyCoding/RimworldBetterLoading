using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using Harmony;
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

        private static readonly object SyncLock = new object();
        
        public StageRunStaticCctors(HarmonyInstance instance) : base(instance)
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

        public override void DoPatching(HarmonyInstance instance)
        {
            instance.Patch(AccessTools.Method(typeof(StaticConstructorOnStartupUtility), nameof(StaticConstructorOnStartupUtility.CallAll)), new HarmonyMethod(typeof(StageRunStaticCctors), nameof(PreCallAll)));
            // instance.Patch(AccessTools.Method(typeof(RuntimeHelpers), nameof(RuntimeHelpers.RunClassConstructor), new []{typeof(RuntimeTypeHandle)}), new HarmonyMethod(typeof(StageRunStaticCctors), nameof(PreRunClassConstructor)));
        }

        public static IEnumerator StaticConstructAll()
        {
            lock (SyncLock)
            {
                Application.runInBackground = true;
                foreach (var type in _toRun)
                {
                    _modType = type;

                    RuntimeHelpers.RunClassConstructor(type.TypeHandle);

                    _numRun++;

                    yield return null;
                }

                Application.runInBackground = Prefs.RunInBackground;

                Log.Message("[BetterLoading] Finished calling static constructors at " + DateTime.Now.ToLongTimeString() + ". AND I didn't make the game freeze. Take that, Tynan.");
                var field = typeof(LongEventHandler).GetField("toExecuteWhenFinished", BindingFlags.NonPublic | BindingFlags.Static);

                var existing = field.GetValue(null) as List<Action>;

                Log.Message($"[BetterLoading] Restoring original job queue of {_queue.Count} item/s and merging with any just added (looking at you, Fluffy) ({existing.Count} entries have been added).");
                if (existing.Count > 0 && _queue.Count == 0)
                {
                    //This is probably usually the case
                    //Don't touch anything
                } else if (existing.Count == 0 && _queue.Count > 0)
                {
                    //Load cached stuff from queue
                    field.SetValue(null, _queue);
                }
                else
                {
                    //Need to merge - queue first
                    var result = _queue;
                    result.AddRange(existing);
                    field.SetValue(null, result);
                }
                
                _queue = null;

                StaticConstructorOnStartupUtility.coreStaticAssetsLoaded = true;
                
                GC.Collect(int.MaxValue, GCCollectionMode.Forced); //Copied from PlayDataLoader
            }
        }

        public static bool PreCallAll()
        {
            Log.Message("Static constructors? Oh, sit down, vanilla, I'll do it myself. Starting now, at " + DateTime.Now.ToLongTimeString());
            _toRun = GenTypes.AllTypesWithAttribute<StaticConstructorOnStartup>().ToList();

            BetterLoadingMain.LoadingScreen.StartCoroutine(StaticConstructAll());
            
            Log.Message("[BetterLoading] Overriding LongEventHandler's toExecuteWhenFinished");
            var field = typeof(LongEventHandler).GetField("toExecuteWhenFinished", BindingFlags.NonPublic | BindingFlags.Static);
            var result = (List<Action>) field.GetValue(null);

            
            Log.Message($"[BetterLoading]    Got list of pending actions: {result}. Removing up to and including the static constructor call...");
            
            var staticCallIdx = result.FindIndex(i => i.Method.DeclaringType?.Name == "PlayDataLoader" && i.Method.Name.Contains("m__2"));
            
            Log.Message($"[BetterLoading]        (Which is at index {staticCallIdx} of {result.Count})");

            result = result.Skip(staticCallIdx + 1).Take(int.MaxValue).ToList(); //Remove the static constructor call

            _queue = result;
            
            Log.Message($"[BetterLoading]     Updating field in LEH to a new list of size {result.Count}...");
            field.SetValue(null, new List<Action>());
            
            LongEventHandler.QueueLongEvent(WaitForStaticCtors, null, true, null);

            return false;
        }

        private static void WaitForStaticCtors()
        {
            //Called async so can just block
            Log.Message("[BetterLoading] Blocking LEH until static ctors finish");
            Thread.Sleep(1000);
            lock (SyncLock)
            {
                //wait for sync lock to be available
                Log.Message("[BetterLoading] Obtained sync lock, assuming we're finished blocking");
                Thread.Sleep(0);
            }
        }
    }
}