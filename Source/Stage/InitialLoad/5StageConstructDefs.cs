using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Xml;
using HarmonyLib;
using UnityEngine;
using Verse;

namespace BetterLoading.Stage.InitialLoad
{
    public class StageConstructDefs : LoadingStage
    {
        private static int _numDefsToResolve = 1;
        private static int _currentDefNum;
        private static bool _shouldCount;
        private static LoadableXmlAsset _asset;

        private static StageConstructDefs inst;

        private static readonly ConcurrentDictionary<Type, Func<XmlNode, bool, object>> objectFromXmlMethods = new ConcurrentDictionary<Type, Func<XmlNode, bool, object>>();
        private static ConcurrentDictionary<TypeCacheKey, Type> typeCache = new ConcurrentDictionary<TypeCacheKey, Type>(EqualityComparer<TypeCacheKey>.Default);

        private static MethodInfo GetTypeInternal = typeof(GenTypes).GetMethod("GetTypeInAnyAssemblyInt", BindingFlags.Static | BindingFlags.NonPublic);


        public StageConstructDefs(Harmony instance) : base(instance)
        {
        }

        public override string GetStageName()
        {
            return "Constructing Defs";
        }

        public override bool IsCompleted()
        {
            return _currentDefNum == _numDefsToResolve;
        }

        public override void BecomeActive()
        {
            _shouldCount = true;
            inst = LoadingScreen.GetStageInstance<StageConstructDefs>();
        }

        public override void BecomeInactive()
        {
            _numDefsToResolve = 1; //Cannot be zero because we can't return 0 from GetMaxProgress
            _currentDefNum = 0;
            _shouldCount = false;
            GlobalTimingData.TicksFinishedConstructingDefs = DateTime.UtcNow.Ticks;
        }

        public override string? GetCurrentStepName()
        {
            if (_asset?.name == null)
                return "<initializing...>";

            return $"{_asset.name} (from {_asset.mod.Name})";
        }

        public override int GetCurrentProgress()
        {
            return _currentDefNum;
        }

        public override int GetMaximumProgress()
        {
            return _numDefsToResolve;
        }

        public override void DoPatching(Harmony instance)
        {
            instance.Patch(AccessTools.Method(typeof(LoadedModManager), nameof(LoadedModManager.ParseAndProcessXML)), new HarmonyMethod(typeof(StageConstructDefs), nameof(PreParseProcXml))/*, new HarmonyMethod(typeof(StageConstructDefs), nameof(ParallelParseAndProcessXML))*/);
            instance.Patch(AccessTools.Method(typeof(DirectXmlLoader), nameof(DirectXmlLoader.DefFromNode)), new HarmonyMethod(typeof(StageConstructDefs), nameof(PreDefFromNode)));
            instance.Patch(AccessTools.Method(typeof(GenTypes), nameof(GenTypes.GetTypeInAnyAssembly)),  new HarmonyMethod(typeof(Utils), nameof(Utils.HarmonyPatchCancelMethod)),new HarmonyMethod(typeof(StageConstructDefs), nameof(ThreadSafeGetTypeInAnyAssembly)));
        }

        public static void ThreadSafeGetTypeInAnyAssembly(string typeName, string namespaceIfAmbiguous, ref Type __result)
        {
            var key = new TypeCacheKey(typeName, namespaceIfAmbiguous);
            if (!typeCache.TryGetValue(key, out var type))
            {
                type = (Type) GetTypeInternal.Invoke(null, new object[] {typeName, namespaceIfAmbiguous});
                typeCache.TryAdd(key, type);
            }

            __result = type;
        }

        public static bool PreParseProcXml(XmlDocument xmlDoc)
        {
            _numDefsToResolve = xmlDoc.DocumentElement?.ChildNodes.Count ?? 1;
            _currentDefNum = 0;
            BetterLoadingApi.DispatchChange(inst);

            // return false;
            return true;
        }

        public static void PreDefFromNode(LoadableXmlAsset loadingAsset)
        {
            if (!_shouldCount) return;

            _currentDefNum++;
            _asset = loadingAsset;
            BetterLoadingApi.DispatchChange(inst);
        }

        public static void ParallelParseAndProcessXML(XmlDocument xmlDoc, Dictionary<XmlNode, LoadableXmlAsset> assetlookup)
        {
            var xmlNodeList = xmlDoc.DocumentElement.ChildNodes.Cast<XmlNode>().ToList();

            //Changed from vanilla in that it's parallel and doesn't have any DeepProfiling 
            xmlNodeList
                .AsParallel()
                .Where(node => node.NodeType == XmlNodeType.Element)
                .Select(node => (node, assetlookup.TryGetValue(node)))
                .Do(tuple => XmlInheritance.TryRegister(tuple.node, tuple.Item2?.mod));

            XmlInheritance.Resolve();

            //DefFromNode is the slow part of this function, so technically this is the only part that *has* to be parallel.
            //This might break horribly.
            try
            {
                var processedDefs = xmlNodeList.AsParallel()
                    .Select(node => (node, assetlookup.TryGetValue(node)))
                    .Select(tuple =>
                    {
                        var (node, asset) = tuple;
                        _asset = asset;
                        var def = DirectXmlLoader.DefFromNode(node, asset);
                        _currentDefNum++;
                        return (asset, def);
                    })
                    .Where(tuple => tuple.def != null).ToList();

                //Associated mod-associated defs with that mod, by the name of the asset.
                processedDefs.Where(tuple => tuple.asset?.mod != null)
                    .Do(tuple => tuple.asset.mod.AddDef(tuple.def, tuple.asset.name));

                //For modless defs, register to PatchedDefs
                processedDefs.Where(tuple => tuple.asset?.mod == null)
                    .Do(tuple => LoadedModManagerMirror.PatchedDefs.Add(tuple.def));

                _currentDefNum = _numDefsToResolve;
            }
            catch (Exception e)
            {
                Log.Error("Exception processing XML: " + e);
                
            }
            
        }
    }
}