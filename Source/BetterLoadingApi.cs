using System;
using System.Collections.Generic;
using System.Linq;
using BetterLoading.Stage;
using Verse;

namespace BetterLoading
{
    public static class BetterLoadingApi
    {
        /// <summary>
        /// Invoked when a loading stage changes (updates its progress), and is called SYNCHRONOUSLY, ON THE UI THREAD.
        /// If you care about your users, at all, don't do long-running code here.
        /// </summary>
        public static event Action<LoadingStage> OnStageChangeSync = stage => { };
        
        public static void AddInitialLoadStage<T>(T stage) where T: LoadingStage
        {
            LoadingScreen.BootLoadList.Add(stage);
            LoadingScreen.RegisterStageInstance(stage);
        }

        public static List<LoadingStage> GetInitialLoadStages()
        {
            return LoadingScreen.BootLoadList.ToList(); //Clone
        }

        public static void InsertInitialLoadStage(LoadingStage stage, int where)
        {
            if (where >= LoadingScreen.BootLoadList.Count || where < 0)
            {
                Log.Warning($"An attempt was made to insert a loading stage into the boot load list at index {where} which is out of bounds considering it contains {LoadingScreen.BootLoadList.Count} items. This attempt will be ignored.");
                return;
            }
            
            LoadingScreen.BootLoadList.Insert(where, stage);
        }

        public static void DispatchChange(LoadingStage? stage)
        {
            if (stage == null) return;
            
            OnStageChangeSync(stage);
        }
    }
}