namespace BetterLoading
{
    public enum EnumLoadingStage
    {
        CreateClasses = 0,
        ReadXMLFiles,
        UnifyXML,
        ApplyPatches,
        ParseProcessXMLStage1,
        ParseProcessXMLStage2,
        ResolveReferences,
        FinishUp,
        //Save loading stuff
        LoadSmallComponents,
        LoadWorldMap,
        GenerateWorldData,
        FinalizeWorld,
        LoadMaps_ConstructComponents,
        LoadMaps_LoadComponents,
        LoadMaps_LoadData,
        InitCamera,
        ResolveSaveFileCrossReferences,
        SpawnThings_NonBuildings,
        SpawnThings_Buildings,
        SpawnThings_BackCompat,
        SpawnThings_RebuildRecalc,
        FinalizeLoad
    }
}