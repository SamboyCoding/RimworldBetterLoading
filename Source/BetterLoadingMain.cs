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
using RimWorld.Planet;
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
        
        #region Initial Game Load Patches

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
        #endregion
        
        #region Save Game Loading Patches

        [HarmonyPatch(typeof(Game))]
        [HarmonyPatch(nameof(Game.LoadGame))]
        [UsedImplicitly]
        public class LoadGamePatch
        {
            [UsedImplicitly]
            public static void Prefix()
            {
                Manager = Resources.FindObjectsOfTypeAll<Root_Play>()[0].gameObject.AddComponent<LoadingScreenManager>();
                Log.Message("Loading Screen Manager :: Load Small Components :: Start");

                Manager.shouldShow = true;
                Manager.currentStage = LoadingStage.LoadSmallComponents;
            }
        }
        
        [HarmonyPatch(typeof(World))]
        [HarmonyPatch(nameof(World.ExposeData))]
        [UsedImplicitly]
        public class LoadWorldPatch
        {
            [UsedImplicitly]
            public static void Prefix()
            {
                if (Manager.currentStage == LoadingStage.LoadSmallComponents)
                {
                    Log.Message("Loading Screen Manager :: Load World Map :: Start");
                    Manager.currentStage = LoadingStage.LoadWorldMap;
                }
            }
        }
        
        [HarmonyPatch(typeof(WorldGenerator))]
        [HarmonyPatch(nameof(WorldGenerator.GenerateFromScribe))]
        [UsedImplicitly]
        public class GenerateWorldPatch
        {
            [UsedImplicitly]
            public static void Prefix()
            {
                Log.Message("Loading Screen Manager :: Generate World Data :: Start");
                Manager.currentStage = LoadingStage.GenerateWorldData;
                Manager.numWorldGeneratorsToRun = WorldGenerator.GenStepsInOrder.Count() - 1;
            }
        }
        
        [HarmonyPatch(typeof(WorldGenerator))]
        [HarmonyPatch(nameof(WorldGenerator.GenerateWithoutWorldData))]
        [UsedImplicitly]
        public class GenerateWorldPatch2
        {
            [UsedImplicitly]
            public static void Prefix()
            {
                Log.Message("Loading Screen Manager :: Generate World Data :: Start");
                Manager.currentStage = LoadingStage.GenerateWorldData;
                Manager.numWorldGeneratorsToRun = WorldGenerator.GenStepsInOrder.Count() - 1;
            }
        }
        
        [HarmonyPatch(typeof(WorldGenStep))]
        [HarmonyPatch(nameof(WorldGenStep.GenerateFromScribe))]
        [UsedImplicitly]
        public class WorldGenStepExecPatch
        {
            [UsedImplicitly]
            public static void Prefix(WorldGenStep __instance)
            {
                Manager.numWorldGeneratorsRun++;
                Manager.currentWorldGenStep = __instance;
            }
        }
        
        [HarmonyPatch(typeof(WorldGenStep))]
        [HarmonyPatch(nameof(WorldGenStep.GenerateWithoutWorldData))]
        [UsedImplicitly]
        public class WorldGenStepExecPatch2
        {
            [UsedImplicitly]
            public static void Prefix(WorldGenStep __instance)
            {
                Manager.numWorldGeneratorsRun++;
                Manager.currentWorldGenStep = __instance;
            }
        }
        
        [HarmonyPatch(typeof(World))]
        [HarmonyPatch(nameof(World.FinalizeInit))]
        [UsedImplicitly]
        public class WorldFinalizePatch
        {
            [UsedImplicitly]
            public static void Prefix()
            {
                Log.Message("Loading Screen Manager :: Finalize World Data :: Start");
                Manager.currentStage = LoadingStage.FinalizeWorld;
            }
        }
        
        [HarmonyPatch(typeof(Map))]
        [HarmonyPatch(nameof(Map.ExposeData))]
        [UsedImplicitly]
        public class MapExposePatch
        {
            [UsedImplicitly]
            public static void Prefix(Map __instance)
            {
                if (Manager.currentStage >= LoadingStage.FinalizeWorld &&
                    Manager.currentStage <= LoadingStage.LoadMaps_LoadData)
                {
                    Log.Message("Loading Screen Manager :: Load Map (Construct Components) :: Start");
                    Manager.currentStage = LoadingStage.LoadMaps_ConstructComponents;
                    Manager.maps.Add(__instance);
                }
            }
        }
        
        [HarmonyPatch(typeof(Map))]
        [HarmonyPatch("ExposeComponents")]
        [UsedImplicitly]
        public class MapLoadPatch
        {
            [UsedImplicitly]
            public static void Prefix()
            {
                if (Manager.currentStage == LoadingStage.LoadMaps_ConstructComponents)
                {
                    Log.Message("Loading Screen Manager :: Load Map (Load Components) :: Start");
                    Manager.currentStage = LoadingStage.LoadMaps_LoadComponents;
                }
            }
        }
        
        [HarmonyPatch(typeof(MapFileCompressor))]
        [HarmonyPatch(nameof(MapFileCompressor.ExposeData))]
        [UsedImplicitly]
        public class MapLoadCompressedPatch
        {
            [UsedImplicitly]
            public static void Prefix()
            {
                if (Manager.currentStage == LoadingStage.LoadMaps_LoadComponents)
                {
                    Log.Message("Loading Screen Manager :: Load Map (Load Objects) :: Start");
                    Manager.currentStage = LoadingStage.LoadMaps_LoadData;
                }
            }
        }
        
        [HarmonyPatch(typeof(CameraDriver))]
        [HarmonyPatch(nameof(CameraDriver.Expose))]
        [UsedImplicitly]
        public class CameraLoadPatch
        {
            [UsedImplicitly]
            public static void Prefix()
            {
                if (Manager.currentStage == LoadingStage.LoadMaps_LoadData)
                {
                    Log.Message("Loading Screen Manager :: Init Camera :: Start");
                    Manager.currentStage = LoadingStage.InitCamera;
                }
            }
        }
        
        [HarmonyPatch(typeof(ScribeLoader))]
        [HarmonyPatch(nameof(ScribeLoader.FinalizeLoading))]
        [UsedImplicitly]
        public class ResolveSaveFileReferencesPatch
        {
            [UsedImplicitly]
            public static void Prefix()
            {
                Log.Message("Loading Screen Manager :: Resolve Cross-References :: Start");
                Manager.currentStage = LoadingStage.ResolveSaveFileCrossReferences;
            }
        }
        
        [HarmonyPatch(typeof(Map))]
        [HarmonyPatch(nameof(Map.FinalizeLoading))]
        [UsedImplicitly]
        public class MapFinalizeLoadPatch
        {
            [UsedImplicitly]
            public static void Prefix(Map __instance)
            {
                Log.Message("Loading Screen Manager :: Spawn Things (Non-Buildings) :: Start");
                Manager.currentStage = LoadingStage.SpawnThings_NonBuildings;
                Manager.mapIndexSpawningItems++;
                
                //Reflection, fuck yeah!
                Manager.numObjectsToSpawnCurrentMap = __instance.compressor.ThingsToSpawnAfterLoad().Count() +
                                                      Traverse.Create(__instance).Field<List<Thing>>("loadedFullThings")
                                                          .Value.Count;
                Manager.numObjectsSpawnedCurrentMap = 0;
            }
        }
        
        [HarmonyPatch(typeof(GenSpawn))]
        [HarmonyPatch(nameof(GenSpawn.Spawn))]
        [HarmonyPatch(new [] {typeof(Thing), typeof(IntVec3), typeof(Map), typeof(Rot4), typeof(WipeMode), typeof(bool)})]
        [UsedImplicitly]
        public class GenSpawnSpawnPatch
        {
            [UsedImplicitly]
            public static void Prefix()
            {
                if (Manager.currentStage == LoadingStage.SpawnThings_NonBuildings)
                    Manager.numObjectsSpawnedCurrentMap++;

            }
        }
        
        [HarmonyPatch(typeof(GenSpawn))]
        [HarmonyPatch(nameof(GenSpawn.SpawnBuildingAsPossible))]
        [UsedImplicitly]
        public class GenSpawnSpawnBuildingPatch
        {
            [UsedImplicitly]
            public static void Prefix(Map __instance)
            {
                if (Manager.currentStage == LoadingStage.SpawnThings_NonBuildings)
                {
                    Log.Message("Loading Screen Manager :: Spawn Things (Buildings) :: Start");
                    Manager.currentStage = LoadingStage.SpawnThings_Buildings;
                }

                if (Manager.currentStage == LoadingStage.SpawnThings_Buildings)
                    Manager.numObjectsSpawnedCurrentMap++;
            }
        }
        
        [HarmonyPatch(typeof(GenPlace))]
        [HarmonyPatch(nameof(GenPlace.TryPlaceThing))]
        [HarmonyPatch(new [] {typeof(Thing), typeof(IntVec3), typeof(Map), typeof(ThingPlaceMode), typeof(Action<Thing, int>), typeof(Predicate<IntVec3>)})]
        [UsedImplicitly]
        public class GenPlaceTryPlacePatch
        {
            [UsedImplicitly]
            public static void Prefix()
            {
                if (Manager.currentStage == LoadingStage.SpawnThings_Buildings)
                {
                    Log.Message("Loading Screen Manager :: Spawn Things (Back-Compat) :: Start");
                    Manager.currentStage = LoadingStage.SpawnThings_BackCompat;
                }

                if (Manager.currentStage == LoadingStage.SpawnThings_BackCompat)
                    Manager.numObjectsSpawnedCurrentMap++;
            }
        }
        
        [HarmonyPatch(typeof(Map))]
        [HarmonyPatch(nameof(Map.FinalizeInit))]
        [UsedImplicitly]
        public class MapFinalizeInitPatch
        {
            [UsedImplicitly]
            public static void Prefix()
            {
                Log.Message("Loading Screen Manager :: Spawn Things (Rebuild/Recalc) :: Start");
                Manager.currentStage = LoadingStage.SpawnThings_RebuildRecalc;
            }
        }
        
        [HarmonyPatch(typeof(Game))]
        [HarmonyPatch(nameof(Game.FinalizeInit))]
        [UsedImplicitly]
        public class GameFinalizeInitPatch
        {
            [UsedImplicitly]
            public static void Prefix()
            {
                Log.Message("Loading Screen Manager :: Finalize Load :: Start");
                Manager.currentStage = LoadingStage.FinalizeLoad;
            }
        }
        
        #endregion
    }
}