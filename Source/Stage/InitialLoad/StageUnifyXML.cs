using System.Collections.Generic;
using System.Xml;
using Harmony;
using Verse;

namespace BetterLoading.Stage.InitialLoad
{
    public class StageUnifyXML : LoadingStage
    {
        private static List<LoadableXmlAsset>? _assets;
        private static LoadableXmlAsset? _currentAsset;
        private static int _numAssets = int.MaxValue;
        private static int _currentAssetNo = 0;
        private static bool _valid;
        
        public StageUnifyXML(HarmonyInstance instance) : base(instance)
        {
        }

        public override string GetStageName()
        {
            return "Injecting Mod XML";
        }

        public override string? GetCurrentStepName()
        {
            if (_currentAsset == null) return "<initializing...>";
            return _currentAsset.mod.Name + " : " + _currentAsset.name;
        }

        public override int GetCurrentProgress()
        {
            return _currentAssetNo;
        }

        public override int GetMaximumProgress()
        {
            return _numAssets;
        }

        public override void DoPatching(HarmonyInstance instance)
        {
            instance.Patch(AccessTools.Method(typeof(LoadedModManager), nameof(LoadedModManager.CombineIntoUnifiedXML)),
                new HarmonyMethod(typeof(StageUnifyXML), nameof(PreUnifyXML)));
            
            instance.Patch(AccessTools.Method(typeof(XmlDocument), "get_" + nameof(XmlDocument.DocumentElement)),
                postfix: new HarmonyMethod(typeof(StageUnifyXML), nameof(PostGetDocumentElement)));
        }

        public static void PreUnifyXML(List<LoadableXmlAsset> xmls)
        {
            _numAssets = xmls.Count;
            _assets = xmls;
        }

        public override void BecomeActive()
        {
            _valid = true;
        }

        public override void BecomeInactive()
        {
            _valid = false;
        }

        public static void PostGetDocumentElement(object __instance)
        {
            if(!_valid || _assets == null) return;

            var idx = _assets.FindIndex(a => a.xmlDoc == __instance);

            if (idx == -1 || idx + 1 == _currentAssetNo) return;
            
            var next = _assets[idx];

            _currentAsset = next;
            _currentAssetNo = idx + 1;
        }
    }
}