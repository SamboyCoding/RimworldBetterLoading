using System;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Xml;
using HarmonyLib;
using JetBrains.Annotations;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace BetterLoading
{
    public sealed class BetterLoadingMain : Mod
    {
        public static ModContentPack? ourContentPack;
        public static Harmony? hInstance;
        public static LoadingScreen? LoadingScreen;
        
        public BetterLoadingMain(ModContentPack content) : base(content)
        {
            ourContentPack = content;
            
            hInstance = new Harmony("me.samboycoding.blm");
            if (Camera.main == null) return; //Just in case
            
            LogMsg("BetterLoading :: Init");

            LoadingScreen = Resources.FindObjectsOfTypeAll<Root_Entry>()[0].gameObject.AddComponent<LoadingScreen>();

            LoadingScreen.Instance.numModClasses = typeof(Mod).InstantiableDescendantsAndSelf().Count();
            LoadingScreen.Instance.currentModClassBeingInstantiated = typeof(Mod).InstantiableDescendantsAndSelf().FirstIndexOf(t => t == typeof(BetterLoadingMain));

            hInstance.Patch(AccessTools.Method(typeof(LongEventHandler), nameof(LongEventHandler.LongEventsOnGUI)),
                new HarmonyMethod(typeof(BetterLoadingMain), nameof(DisableVanillaLoadScreen)));

            //Harmony.PatchAll(Assembly.GetExecutingAssembly());
            Log.Message("Ctor finished");
        }

        public static bool DisableVanillaLoadScreen()
        {
            //Disable when our load screen is shown
            return !LoadingScreen.shouldShow;
        }

        private static void LogMsg(string message)
        {
            Log.Message($"[{DateTime.Now}] {message}");
        }

        #region Initial Game Load Patches


        // [HarmonyPatch(typeof(GenGeneric))]
        // [HarmonyPatch("MethodOnGenericType")]
        // [HarmonyPatch(new[] {typeof(Type), typeof(Type), typeof(string)})]
        // [UsedImplicitly]
        // public class DefDatabaseReferencesPatch
        // {
        //     [UsedImplicitly]
        //     public static void Prefix(Type genericParam, string methodName)
        //     {
        //         if ((LoadingScreen.Instance.currentStage == EnumLoadingStage.ParseProcessXMLStage2 ||
        //              LoadingScreen.Instance.currentStage == EnumLoadingStage.ResolveReferences)
        //             && genericParam.IsSubclassOf(typeof(Def))
        //             && methodName == "ResolveAllReferences")
        //         {
        //             LoadingScreen.Instance.currentDatabaseResolving = genericParam;
        //             LoadingScreen.Instance.numDatabasesReloaded++;
        //
        //             if (LoadingScreen.Instance.currentStage != EnumLoadingStage.ResolveReferences)
        //             {
        //                 LogMsg("Loading Screen Manager :: Resolve References :: Start");
        //                 LoadingScreen.Instance.numDefDatabases =
        //                     typeof(Def).AllSubclasses().Count() - 1; //-1 because Def subclasses Def. Or something.
        //                 LoadingScreen.Instance.currentStage = EnumLoadingStage.ResolveReferences;
        //             }
        //         }
        //     }
        // }

        [HarmonyPatch(typeof(StaticConstructorOnStartupUtility))]
        [HarmonyPatch(nameof(StaticConstructorOnStartupUtility.CallAll))]
        [UsedImplicitly]
        public class FinishUpPatch
        {
            [UsedImplicitly]
            public static void Prefix()
            {
                LogMsg("Loading Screen Manager :: Call Static CCtors :: Start");
                LoadingScreen.Instance.currentStage = EnumLoadingStage.FinishUp;
                LoadingScreen.Instance.numStaticConstructorsToCall =
                    GenTypes.AllTypesWithAttribute<StaticConstructorOnStartup>().Count();
            }
        }

        [HarmonyPatch(typeof(RuntimeHelpers))]
        [HarmonyPatch(nameof(RuntimeHelpers.RunClassConstructor))]
        [HarmonyPatch(new[] {typeof(RuntimeTypeHandle)})]
        [UsedImplicitly]
        public class RunClassConstructorPatch
        {
            [UsedImplicitly]
            public static void Prefix(RuntimeTypeHandle type)
            {
                //This patch is really sketchy as it's more than possible that this could be called in a million and one places.
                //Need to safeguard as much as is humanly possible.
                if (LoadingScreen.Instance.currentStage != EnumLoadingStage.FinishUp) return;
                var typeImpl = Type.GetTypeFromHandle(type);
                if (!typeImpl.TryGetAttribute(out StaticConstructorOnStartup _)) return;
                //We are calling the constructor of a StaticConstructorOnStartup-Annotated class. In theory.
                LoadingScreen.Instance.currentStaticConstructor = typeImpl;
                LoadingScreen.Instance.numStaticConstructorsCalled++;
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
                Resources.FindObjectsOfTypeAll<Root_Play>()[0].gameObject
                    .AddComponent<LoadingScreen>();
                LogMsg("Loading Screen Manager :: Load Small Components :: Start");

                LoadingScreen.Instance.shouldShow = true;
                LoadingScreen.Instance.currentStage = EnumLoadingStage.LoadSmallComponents;
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
                if (LoadingScreen.Instance.currentStage == EnumLoadingStage.LoadSmallComponents)
                {
                    LogMsg("Loading Screen Manager :: Load World Map :: Start");
                    LoadingScreen.Instance.currentStage = EnumLoadingStage.LoadWorldMap;
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
                LogMsg("Loading Screen Manager :: Generate World Data :: Start");
                LoadingScreen.Instance.currentStage = EnumLoadingStage.GenerateWorldData;
                LoadingScreen.Instance.numWorldGeneratorsToRun = WorldGenerator.GenStepsInOrder.Count() - 2;
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
                LogMsg("Loading Screen Manager :: Generate World Data :: Start");
                LoadingScreen.Instance.currentStage = EnumLoadingStage.GenerateWorldData;
                LoadingScreen.Instance.numWorldGeneratorsToRun = WorldGenerator.GenStepsInOrder.Count() - 2;
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
                LoadingScreen.Instance.numWorldGeneratorsRun++;
                LoadingScreen.Instance.currentWorldGenStep = __instance;
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
                LoadingScreen.Instance.numWorldGeneratorsRun++;
                LoadingScreen.Instance.currentWorldGenStep = __instance;
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
                LogMsg("Loading Screen Manager :: Finalize World Data :: Start");
                LoadingScreen.Instance.currentStage = EnumLoadingStage.FinalizeWorld;
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
                if (LoadingScreen.Instance.currentStage >= EnumLoadingStage.FinalizeWorld &&
                    LoadingScreen.Instance.currentStage <= EnumLoadingStage.LoadMaps_LoadData)
                {
                    LogMsg("Loading Screen Manager :: Load Map (Construct Components) :: Start");
                    LoadingScreen.Instance.currentStage = EnumLoadingStage.LoadMaps_ConstructComponents;
                    LoadingScreen.Instance.maps.Add(__instance);
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
                if (LoadingScreen.Instance.currentStage == EnumLoadingStage.LoadMaps_ConstructComponents)
                {
                    LogMsg("Loading Screen Manager :: Load Map (Load Components) :: Start");
                    LoadingScreen.Instance.currentStage = EnumLoadingStage.LoadMaps_LoadComponents;
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
                if (LoadingScreen.Instance.currentStage == EnumLoadingStage.LoadMaps_LoadComponents)
                {
                    LogMsg("Loading Screen Manager :: Load Map (Load Objects) :: Start");
                    LoadingScreen.Instance.currentStage = EnumLoadingStage.LoadMaps_LoadData;
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
                if (LoadingScreen.Instance.currentStage == EnumLoadingStage.LoadMaps_LoadData)
                {
                    LogMsg("Loading Screen Manager :: Init Camera :: Start");
                    LoadingScreen.Instance.currentStage = EnumLoadingStage.InitCamera;
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
                if (LoadingScreen.Instance.currentStage != EnumLoadingStage.InitCamera) return;

                LogMsg("Loading Screen Manager :: Resolve Cross-References :: Start");
                LoadingScreen.Instance.currentStage = EnumLoadingStage.ResolveSaveFileCrossReferences;
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
                LogMsg("Loading Screen Manager :: Spawn Things (Non-Buildings) :: Start");
                LoadingScreen.Instance.currentStage = EnumLoadingStage.SpawnThings_NonBuildings;
                LoadingScreen.Instance.mapIndexSpawningItems++;

                //Reflection, fuck yeah!
//                LoadingScreen.Instance.numObjectsToSpawnCurrentMap = __instance.compressor.ThingsToSpawnAfterLoad().Count() +
//                                                      Traverse.Create(__instance).Field<List<Thing>>("loadedFullThings")
//                                                          .Value.Count;
//                LoadingScreen.Instance.numObjectsSpawnedCurrentMap = 0;
            }
        }

        [HarmonyPatch(typeof(GenSpawn))]
        [HarmonyPatch(nameof(GenSpawn.Spawn))]
        [HarmonyPatch(new[]
            {typeof(Thing), typeof(IntVec3), typeof(Map), typeof(Rot4), typeof(WipeMode), typeof(bool)})]
        [UsedImplicitly]
        public class GenSpawnSpawnPatch
        {
            [UsedImplicitly]
            public static void Prefix()
            {
                if (LoadingScreen.Instance.currentStage == EnumLoadingStage.SpawnThings_NonBuildings)
                    LoadingScreen.Instance.numObjectsSpawnedCurrentMap++;
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
                if (LoadingScreen.Instance.currentStage == EnumLoadingStage.SpawnThings_NonBuildings)
                {
                    LogMsg("Loading Screen Manager :: Spawn Things (Buildings) :: Start");
                    LoadingScreen.Instance.currentStage = EnumLoadingStage.SpawnThings_Buildings;
                }

                if (LoadingScreen.Instance.currentStage == EnumLoadingStage.SpawnThings_Buildings)
                    LoadingScreen.Instance.numObjectsSpawnedCurrentMap++;
            }
        }

        [HarmonyPatch(typeof(GenPlace))]
        [HarmonyPatch(nameof(GenPlace.TryPlaceThing))]
        [HarmonyPatch(new[]
        {
            typeof(Thing), typeof(IntVec3), typeof(Map), typeof(ThingPlaceMode), typeof(Action<Thing, int>),
            typeof(Predicate<IntVec3>)
        })]
        [UsedImplicitly]
        public class GenPlaceTryPlacePatch
        {
            [UsedImplicitly]
            public static void Prefix()
            {
                if (LoadingScreen.Instance.currentStage == EnumLoadingStage.SpawnThings_Buildings)
                {
                    LogMsg("Loading Screen Manager :: Spawn Things (Back-Compat) :: Start");
                    LoadingScreen.Instance.currentStage = EnumLoadingStage.SpawnThings_BackCompat;
                }

                if (LoadingScreen.Instance.currentStage == EnumLoadingStage.SpawnThings_BackCompat)
                    LoadingScreen.Instance.numObjectsSpawnedCurrentMap++;
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
                LogMsg("Loading Screen Manager :: Spawn Things (Rebuild/Recalc) :: Start");
                LoadingScreen.Instance.currentStage = EnumLoadingStage.SpawnThings_RebuildRecalc;
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
                LogMsg("Loading Screen Manager :: Finalize Load :: Start");
                LoadingScreen.Instance.currentStage = EnumLoadingStage.FinalizeLoad;
            }
        }

        #endregion
    }
}