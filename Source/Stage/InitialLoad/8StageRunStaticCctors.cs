using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace BetterLoading.Stage.InitialLoad
{
    public class StageRunStaticCctors : LoadingStage
    {
        private static Type? _modType;
        private static List<Type>? _toRun;
        private static int _numRun;

        private static List<Action>? _queue;

        private static bool _finishedProcessing;
        private static Exception? _encounteredException;

        private static StageRunStaticCctors? inst;

        public StageRunStaticCctors(Harmony instance) : base(instance)
        {
        }

        public override string GetStageName()
        {
            return "Executing Static Constructors";
        }

        public override string? GetCurrentStepName()
        {
            if((_numRun == (_toRun?.Count ?? 1)) && !_finishedProcessing) 
                return VersionControl.CurrentMinor == 4 ? "Baking static atlases and cleaning up" : "Cleaning up...";
            
            var result = _modType?.FullName ?? "Waiting...";
            if (HasError())
                result = $"WARNING: An error has occurred previously, now processing {result}";

            return result;
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
            return _numRun == _toRun?.Count && _finishedProcessing;
        }

        public override void DoPatching(Harmony instance)
        {
            if (VersionControl.CurrentMinor == 4)
            {
                //1.4, we need to patch the entire <DoPlayLoad>b__4_5 method and make it not run, instead of just CallAll, because there's 2 other method calls in that anon method which we don't wanna do until after static constructors
                var anonType = typeof(PlayDataLoader).GetNestedTypes(AccessTools.all).First(t => t.Name.Contains("<>"));
                var method = AccessTools.Method(anonType, "<DoPlayLoad>b__4_5");
                instance.Patch(method, new HarmonyMethod(typeof(StageRunStaticCctors), nameof(PreCallAll)));
            }
            else
            {
                //Pre-1.4, we can just patch CallAll
                instance.Patch(AccessTools.Method(typeof(StaticConstructorOnStartupUtility), nameof(StaticConstructorOnStartupUtility.CallAll)), new(typeof(StageRunStaticCctors), nameof(PreCallAll)));
                // instance.Patch(AccessTools.Method(typeof(RuntimeHelpers), nameof(RuntimeHelpers.RunClassConstructor), new []{typeof(RuntimeTypeHandle)}), new HarmonyMethod(typeof(StageRunStaticCctors), nameof(PreRunClassConstructor)));
            }
        }

        public override bool HasError()
        {
            return _encounteredException != null;
        }

        public override void BecomeActive()
        {
            inst = LoadingScreen.GetStageInstance<StageRunStaticCctors>();

            var patches = Harmony.GetPatchInfo(AccessTools.Method(typeof(StaticConstructorOnStartupUtility), nameof(StaticConstructorOnStartupUtility.CallAll)));
            
            //We register a prefix, others may have a postfix which will run on a background thread (probably not what they want)
            if (patches?.Postfixes?.Count > 0)
            {
                Log.Warning("[BetterLoading] One or more mods have Harmony-Postfixed StaticConstructorOnStartupUtility#CallAll. This is likely to cause errors or undesired behavior, as BetterLoading changes this method to be called from another Thread than the UI one. A list of patches follows.");
                Log.Warning("[BetterLoading] In addition, when BetterLoading is installed, this postfix will run before any static constructors do, so it likely will not behave as the modder intended anyway.");
                foreach (var postfix in patches.Postfixes) 
                    Log.Warning($"[BetterLoading]    - {postfix.PatchMethod.FullDescription()}");
                Log.Warning($"[BetterLoading] Modders: Consider either loading this data on world load, using a regular static constructor but asking your users to put the mod last in the load order, or if it MUST be done now, consider using BetterLoading API to detect the start of {nameof(StageRunPostFinalizeCallbacks)}, which fires once static constructors are done.");
                Log.Warning("[BetterLoading] Players: I'm not interested in this warning, if a mod is breaking, show this message to the person who made that mod, not to me. They can reach out to me if they need assistance.");
            }
        }


        public static IEnumerator StaticConstructAll()
        {
            if(_toRun == null)
                throw new InvalidOperationException("StaticConstructAll called before _toRun was set!");
            
            if(_queue == null)
                throw new InvalidOperationException("StaticConstructAll called before _queue was set!");
            
            GlobalTimingData.TicksStartedCctors = DateTime.UtcNow.Ticks;
            Log.Message("[BetterLoading] Starting StaticConstructAll() to run class constructors without freezing the game...");
            Utils.DebugLog($"Going to run class constructor on {_toRun.Count} types");
            Application.runInBackground = true;
            
            foreach (var type in _toRun)
            {
                try
                {
                    _modType = type;

                    RuntimeHelpers.RunClassConstructor(type.TypeHandle);

                    _numRun++;
                    BetterLoadingApi.DispatchChange(inst);
                }
                catch (Exception e)
                {
                    Log.Error("[BetterLoading] Exception occurred processing mod finalize events! Details: " + e);
                    _encounteredException = e;
                }

                yield return null;
            }

            try
            {
                Log.Message("[BetterLoading] Finished calling static constructors at " + DateTime.Now.ToLongTimeString() + ".");
                var existing = LongEventHandlerMirror.ToExecuteWhenFinished;
                
                Utils.DebugLog($"Finished running class constructors. ToExecuteWhenFinished length (tasks added as a result of calling QueueLongEvent while running static cctors) is {existing.Count}, " +
                               $"_queue length is {_queue.Count}");

                // Log.Message($"[BetterLoading] Restoring original job queue of {_queue.Count} item/s and merging with any just added (looking at you, Fluffy) ({existing.Count} entries have been added).");
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

                // Log.Message($"[BetterLoading] Job queue restored. Running GC...");

                StaticConstructorOnStartupUtility.coreStaticAssetsLoaded = true;
                
                var isOnePointFour = VersionControl.CurrentMajor == 1 && VersionControl.CurrentMinor == 4;
                if (isOnePointFour)
                {
                    Utils.DebugLog("1.4 detected, calling BakeStaticAtlases");
                    GlobalTextureAtlasManager.BakeStaticAtlases();
                    Utils.DebugLog("BakeStaticAtlases finished");
                }

                GC.Collect(int.MaxValue, GCCollectionMode.Forced); //Copied from PlayDataLoader

                // Log.Message($"[BetterLoading] GC Has completed. Relinquishing lock.");
            }
            catch (Exception e)
            {
                Debug.LogError("Exception occurred, releasing lock! " + e);
            }
            finally
            {
                StageRunPostFinalizeCallbacks.ShouldInterceptNext = true;
                _finishedProcessing = true;
                Application.runInBackground = Prefs.RunInBackground;
                // Log.Message("[BetterLoading] Lock released.");
            }
        }

        public static bool PreCallAll()
        {
            _toRun = GenTypes.AllTypesWithAttribute<StaticConstructorOnStartup>().ToList();

            // Log.Message("[BetterLoading] Overriding LongEventHandler's toExecuteWhenFinished", true);
            
            var result = LongEventHandlerMirror.ToExecuteWhenFinished;
            
            Utils.DebugLog($"Harmony prefix on StaticConstructorOnStartupUtility.CallAll() called. Found {_toRun.Count} types with StaticConstructorOnStartup attribute, and {result.Count} actions in ToExecuteWhenFinished.");
            if (BetterLoadingConfigManager.Config.VerboseLogging)
            {
                Utils.DebugLog("Entries in ToExecuteWhenFinished:");
                result.ForEach(action => Utils.DebugLog($"    - {action.Method.DeclaringType?.Name} :: {action.Method.Name} ({action.Method.DeclaringType})"));
                Utils.DebugLog("End of ToExecuteWhenFinished entries");
            }

            // Log.Message($"[BetterLoading]    Got list of pending actions: {result}. Removing up to and including the static constructor call...", true);

            var isOnePointFour = VersionControl.CurrentMajor == 1 && VersionControl.CurrentMinor == 4;
            var methodNameSubstring = isOnePointFour ? "b__4_5" : "m__2";
            var staticCallIdx = result.FindIndex(i => i.Method.DeclaringType?.DeclaringType?.Name == nameof(PlayDataLoader) && i.Method.Name.Contains(methodNameSubstring));

            // Log.Message($"[BetterLoading]        (Which is at index {staticCallIdx} of {result.Count})", true);
            
            Utils.DebugLog($"PlayDataLoader static constructor call index is at position {staticCallIdx} of {result.Count} in the queue. Everything before this will be removed, and the remaining tasks saved");

            result = result.Skip(staticCallIdx + 1).Take(int.MaxValue).ToList(); //Remove the static constructor call

            _queue = result;
            
            Utils.DebugLog($"After removing the static constructor call, the queue has {result.Count} items. Saving this for later.");
            
            BetterLoadingMain.LoadingScreen!.StartCoroutine(StaticConstructAll());

            LongEventHandlerMirror.ToExecuteWhenFinished = new();

            LongEventHandler.QueueLongEvent(WaitForStaticCtors, null, true, null);

            return false;
        }

        private static void WaitForStaticCtors()
        {
            //Called async so can just block
            // Log.Message("[BetterLoading] Blocking LEH until static ctors finish", true);
            Thread.Sleep(1000);
            // Log.Message("[BetterLoading] Awaiting release of lock...");
            
            while(!_finishedProcessing){Thread.Sleep(2000);} //wait for sync lock to be available
            
            // Log.Message("[BetterLoading] Lock released, assuming we're finished calling static ctors", true);
            Thread.Sleep(0);
            
        }
    }
}