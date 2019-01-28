namespace BetterLoading
{
    public enum LoadingStage
    {
        CreateClasses = 0,
        ReadXMLFiles,
        UnifyXML,
        ApplyPatches,
        ParseProcessXMLStage1,
        ParseProcessXMLStage2,
        ResolveReferences,
        FinishUp,
    }
}