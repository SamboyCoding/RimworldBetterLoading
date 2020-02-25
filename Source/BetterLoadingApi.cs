using System.Collections.Generic;
using System.Linq;
using BetterLoading.Stage;
using Verse;

namespace BetterLoading
{
    public static class BetterLoadingApi
    {
        public static void AddInitialLoadStage(LoadingStage stage)
        {
            LoadingScreen.BootLoadList.Add(stage);
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
    }
}