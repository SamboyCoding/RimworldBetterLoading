using System.Linq;
using System.Xml;
using Harmony;
using Verse;

namespace BetterLoading.Stage.InitialLoad
{
    public class StageConstructDefs : LoadingStage
    {
        private static int _numDefsToResolve = 1;
        private static int _currentDefNum;
        private static bool _shouldCount;
        private static LoadableXmlAsset _asset;
        
        public StageConstructDefs(HarmonyInstance instance) : base(instance)
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
        }

        public override void BecomeInactive()
        {
            _numDefsToResolve = 1; //Cannot be zero because we can't return 0 from GetMaxProgress
            _currentDefNum = 0;
            _shouldCount = false;
        }

        public override string? GetCurrentStepName()
        {
            return _asset?.name ?? "<initializing...>";
        }

        public override int GetCurrentProgress()
        {
            return _currentDefNum;
        }

        public override int GetMaximumProgress()
        {
            return _numDefsToResolve;
        }

        public override void DoPatching(HarmonyInstance instance)
        {
            instance.Patch(AccessTools.Method(typeof(LoadedModManager), nameof(LoadedModManager.ParseAndProcessXML)), new HarmonyMethod(typeof(StageConstructDefs), nameof(PreParseProcXml)));
            instance.Patch(AccessTools.Method(typeof(DirectXmlLoader), nameof(DirectXmlLoader.DefFromNode)), new HarmonyMethod(typeof(StageConstructDefs), nameof(PreDefFromNode)));
        }

        public static void PreParseProcXml(XmlDocument xmlDoc)
        {
            _numDefsToResolve = xmlDoc.DocumentElement?.ChildNodes.Count ?? 1;
            _currentDefNum = 0;
        }

        public static void PreDefFromNode(LoadableXmlAsset loadingAsset)
        {
            if(!_shouldCount) return;

            _currentDefNum++;
            _asset = loadingAsset;
        }
    }
}