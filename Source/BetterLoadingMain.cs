using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Xml;
using Harmony;
using JetBrains.Annotations;
using RimWorld;
using UnityEngine;
using Verse;

namespace BetterLoading
{
    public class BetterLoadingMain : Mod
    {
        public static LoadingScreenManager Manager;

        public BetterLoadingMain(ModContentPack content) : base(content)
        {
            Manager = Camera.main.gameObject.AddComponent<LoadingScreenManager>();
            var inst = HarmonyInstance.Create("me.samboycoding.blm");
            inst.PatchAll(Assembly.GetExecutingAssembly());
        }

        [HarmonyPatch(typeof(LoadedModManager))]
        [HarmonyPatch(nameof(LoadedModManager.LoadModXML))]
        [UsedImplicitly]
        public class LoadModXmlPatch
        {
            [UsedImplicitly]
            public static void Prefix()
            {
                Log.Message("Loading Screen Manager :: Read XML Files :: Start");
                Manager.currentStage = LoadingStage.ReadXMLFiles;
                Manager.totalLoadedContentPacks = LoadedModManager.RunningMods.Count();
                Manager.numContentPacksLoaded = 0;
            }
        }

        [HarmonyPatch(typeof(ModContentPack))]
        [HarmonyPatch(nameof(ModContentPack.LoadDefs))]
        [UsedImplicitly]
        public class LoadDefsPatch
        {
            [UsedImplicitly]
            public static void Prefix(ModContentPack __instance)
            {
                Manager.numContentPacksLoaded += 1;
                Manager.currentlyLoadingDefsFrom = __instance;
            }
        }

        [HarmonyPatch(typeof(LoadedModManager))]
        [HarmonyPatch(nameof(LoadedModManager.CombineIntoUnifiedXML))]
        [UsedImplicitly]
        public class CombineXmlPatch
        {
            [UsedImplicitly]
            public static void Prefix()
            {
                Log.Message("Loading Screen Manager :: Unify XML Tree :: Start");
                Manager.currentStage = LoadingStage.UnifyXML;
            }
        }

        [HarmonyPatch(typeof(LoadedModManager))]
        [HarmonyPatch(nameof(LoadedModManager.ApplyPatches))]
        [UsedImplicitly]
        public class ApplyPatchesPatch
        {
            [UsedImplicitly]
            public static void Prefix()
            {
                Log.Message("Loading Screen Manager :: Apply XML Patches :: Start");
                Manager.numPatchesToLoad = LoadedModManager.RunningMods.Count();
                Manager.currentStage = LoadingStage.ApplyPatches;
            }
        }

        [HarmonyPatch(typeof(ModContentPack))]
        [HarmonyPatch("LoadPatches")]
        [UsedImplicitly]
        public class GetPatchesPatch
        {
            [UsedImplicitly]
            public static void Prefix(ModContentPack __instance)
            {
                Manager.currentlyPatching = __instance;
                Manager.numPatchesLoaded++;
            }
        }

        [HarmonyPatch(typeof(LoadedModManager))]
        [HarmonyPatch(nameof(LoadedModManager.ParseAndProcessXML))]
        [UsedImplicitly]
        public class PAndPXmlPatch
        {
            [UsedImplicitly]
            public static void Prefix(XmlDocument xmlDoc)
            {
                Log.Message("Loading Screen Manager :: Pre-Parse XML Tree :: Start");
                Manager.numDefsToProcess = xmlDoc.DocumentElement.ChildNodes.Count;

                Manager.numDefsToPreProcess = 0;
                var enumerator = xmlDoc.DocumentElement.ChildNodes.GetEnumerator();
                while (enumerator.MoveNext())
                {
                    if (((XmlNode) enumerator.Current).NodeType == XmlNodeType.Element) Manager.numDefsToPreProcess++;
                }

                Manager.currentStage = LoadingStage.ParseProcessXMLStage1;
            }
        }

        [HarmonyPatch(typeof(XmlInheritance))]
        [HarmonyPatch(nameof(XmlInheritance.TryRegister))]
        [UsedImplicitly]
        public class TryRegisterInheritancePatch
        {
            [UsedImplicitly]
            public static void Prefix()
            {
                if (Manager.currentStage == LoadingStage.ParseProcessXMLStage1)
                    Manager.numDefsPreProcessed++;
            }
        }

        [HarmonyPatch(typeof(XmlInheritance))]
        [HarmonyPatch(nameof(XmlInheritance.Resolve))]
        [UsedImplicitly]
        public class ResolveInheritancePatch
        {
            [UsedImplicitly]
            public static void Prefix()
            {
                if (Manager.currentStage == LoadingStage.ParseProcessXMLStage1)
                {
                    Log.Message("Loading Screen Manager :: Process XML Tree :: Start");
                    Manager.currentStage = LoadingStage.ParseProcessXMLStage2;
                }
            }
        }

        [HarmonyPatch(typeof(DirectXmlLoader))]
        [HarmonyPatch(nameof(DirectXmlLoader.DefFromNode))]
        [UsedImplicitly]
        public class DefFromNodePatch
        {
            [UsedImplicitly]
            public static void Prefix()
            {
                Manager.numDefsProcessed++;
            }
        }

        [HarmonyPatch(typeof(GenGeneric))]
        [HarmonyPatch("MethodOnGenericType")]
        [HarmonyPatch(new[] {typeof(Type), typeof(Type), typeof(string)})]
        [UsedImplicitly]
        public class DefDatabaseReferencesPatch
        {
            [UsedImplicitly]
            public static void Prefix(Type genericParam, string methodName)
            {
                if ((Manager.currentStage == LoadingStage.ParseProcessXMLStage2 ||
                     Manager.currentStage == LoadingStage.ResolveReferences)
                    && genericParam.IsSubclassOf(typeof(Def))
                    && methodName == "ResolveAllReferences")
                {
                    Manager.currentDatabaseResolving = genericParam;
                    Manager.numDatabasesReloaded++;

                    if (Manager.currentStage != LoadingStage.ResolveReferences)
                    {
                        Log.Message("Loading Screen Manager :: Resolve References :: Start");
                        Manager.numDefDatabases = typeof(Def).AllSubclasses().Count() - 1; //-1 because Def subclasses Def. Or something.
                        Manager.currentStage = LoadingStage.ResolveReferences;
                    }
                }
            }
        }

        [HarmonyPatch(typeof(StaticConstructorOnStartupUtility))]
        [HarmonyPatch(nameof(StaticConstructorOnStartupUtility.CallAll))]
        [UsedImplicitly]
        public class FinishUpPatch
        {
            [UsedImplicitly]
            public static void Prefix()
            {
                Log.Message("Loading Screen Manager :: Call Static CCtors :: Start");
                Manager.currentStage = LoadingStage.FinishUp;
                Manager.numStaticConstructorsToCall =
                    GenTypes.AllTypesWithAttribute<StaticConstructorOnStartup>().Count();
            }
        }
        
        [HarmonyPatch(typeof(RuntimeHelpers))]
        [HarmonyPatch(nameof(RuntimeHelpers.RunClassConstructor))]
        [HarmonyPatch(new [] {typeof(RuntimeTypeHandle)})]
        [UsedImplicitly]
        public class RunClassConstructorPatch
        {
            [UsedImplicitly]
            public static void Prefix(RuntimeTypeHandle type)
            {
                //This patch is really sketchy as it's more than possible that this could be called in a million and one places.
                //Need to safeguard as much as is humanly possible.
                if (Manager.currentStage != LoadingStage.FinishUp) return;
                var typeImpl = Type.GetTypeFromHandle(type); 
                if(typeImpl.TryGetAttribute(out StaticConstructorOnStartup attrib))
                {
                    //We are calling the constructor of a StaticConstructorOnStartup-Annotated class. In theory.
                    Manager.currentStaticConstructor = typeImpl;
                    Manager.numStaticConstructorsCalled++;
                }
            }
        }
    }
}