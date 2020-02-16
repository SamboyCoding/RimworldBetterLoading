using System.Linq;
using System.Xml;
using Harmony;
using Verse;

namespace BetterLoading.Stage.InitialLoad
{
    public class StageRegisterDef : LoadingStage
    {
        private static int _numDefsToRegister = 1;
        private static int _currentDefNum;
        
        public StageRegisterDef(HarmonyInstance instance) : base(instance)
        {
        }

        public override string GetStageName()
        {
            return "Registering Defs";
        }

        public override bool IsCompleted()
        {
            return _currentDefNum == _numDefsToRegister;
        }

        public override void BecomeInactive()
        {
            _numDefsToRegister = 1; //Cannot be zero because we can't return 0 from GetMaxProgress
            _currentDefNum = 0;
        }

        public override string? GetCurrentStepName()
        {
            return null;
        }

        public override int GetCurrentProgress()
        {
            return _currentDefNum;
        }

        public override int GetMaximumProgress()
        {
            return _numDefsToRegister;
        }

        public override void DoPatching(HarmonyInstance instance)
        {
            instance.Patch(AccessTools.Method(typeof(LoadedModManager), nameof(LoadedModManager.ParseAndProcessXML)), new HarmonyMethod(typeof(StageRegisterDef), nameof(PreParseProcXml)));
            instance.Patch(AccessTools.Method(typeof(XmlInheritance), nameof(XmlInheritance.TryRegister)), new HarmonyMethod(typeof(StageRegisterDef), nameof(PreRegisterDef)));
        }

        public static void PreParseProcXml(XmlDocument xmlDoc)
        {
            _numDefsToRegister = xmlDoc.DocumentElement?.ChildNodes.GetEnumerator().ToIEnumerable<XmlNode>().Count(e => e.NodeType == XmlNodeType.Element) ?? 0;
            _currentDefNum = 0;
        }

        public static void PreRegisterDef()
        {
            if (_currentDefNum >= _numDefsToRegister) return;
            
            _currentDefNum++;
        }
    }
}