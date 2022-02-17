using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Verse;

namespace BetterLoading.Compat;

public static class ShitRimworldSaysCompat
{
    private static FieldInfo? _tipQuoteAuthorField; 
    private static FieldInfo? _tipQuoteBodyField;
    public static List<BetterLoadingTip>? TipsFromShitRimWorldSays;
    private static Assembly? _srwsAssembly;

    public static void PatchShitRimworldSaysIfPresent()
    {
        //We can postfix-patch TipDatabase#Notify_TipsUpdated which is called when tips update
        //Then we look for the field "_quotes" and read into our tip list
        
        _srwsAssembly = LoadedModManager.RunningMods.FirstOrDefault(m => m.Name == "Shit Rimworld Says")?.assemblies.loadedAssemblies.Find(a => a.GetName().Name == "ShitRimWorldSays");
        
        if(_srwsAssembly == null)
            return;
        
        Log.Message("Shit Rimworld Says found: " + (_srwsAssembly));
        

        var tipDbType = _srwsAssembly.GetType("ShitRimWorldSays.TipDatabase");
        
        if(tipDbType == null)
            return;
        
        var tipQuoteType = _srwsAssembly.GetType("ShitRimWorldSays.Tip_Quote");

        if (tipQuoteType == null)
        {
            Log.Error("[BetterLoading|ShitRimWorldSays Compat] Found a TipDatabase but couldn't find Tip_Quote? Has the mod been updated? Please report this.");
            return;
        }
        
        Log.Message("[BetterLoading|ShitRimWorldSays Compat] Found ShitRimWorldSays, enabling compatibility. Enjoy your warcrime tips.");

        //These are public instance fields, so no binding flags needed
        _tipQuoteAuthorField = tipQuoteType.GetField("author");
        _tipQuoteBodyField = tipQuoteType.GetField("body");

        var srwsModType = _srwsAssembly.GetType("ShitRimWorldSays.ShitRimWorldSays");

        BetterLoadingMain.hInstance!.Patch(AccessTools.Method(tipDbType, "Notify_TipsUpdated"), postfix: new(typeof(ShitRimworldSaysCompat), nameof(Notify_TipsUpdated_Postfix)));
        BetterLoadingMain.hInstance!.Patch(AccessTools.FirstConstructor(srwsModType, ctor => ctor.GetParameters().Length == 1), postfix: new(typeof(ShitRimworldSaysCompat), nameof(ShitRimworldSays_ctor_Postfix)));
    }

    public static bool UserWantsToHideVanillaTips()
    {
        if (_srwsAssembly == null)
            //Not loaded, so don't hide tips
            return false;

        var settingsProp = _srwsAssembly.GetType("ShitRimWorldSays.ShitRimWorldSays")!.GetProperty("Settings") ?? throw new("Failed to find ShitRimWorldSays.ShitRimWorldSays.Settings property");
        var settings = settingsProp.GetValue(null) ?? throw new("Failed to get ShitRimWorldSays.ShitRimWorldSays.Settings");
        var replaceGameTipsProp = _srwsAssembly.GetType("ShitRimWorldSays.Settings")!.GetField("replaceGameTips") ?? throw new("Failed to find ShitRimWorldSays.ShitRimWorldSays.Settings.replaceGameTips field");
        
        return (bool)replaceGameTipsProp.GetValue(settings);
    }

    // ReSharper disable once InconsistentNaming
    public static void Notify_TipsUpdated_Postfix(HashSet<object> ____quotes)
    {
        TipsFromShitRimWorldSays = new();
        
        //Each of the quotes is a Tip_Quote object.
        foreach (var quote in ____quotes)
        {
            var body = (string) _tipQuoteBodyField!.GetValue(quote);
            var author = (string) _tipQuoteAuthorField!.GetValue(quote);
            
            if(body == "[removed]" || author == "[deleted]")
                continue; //Skip removed tips
            
            if(body.Length > 500)
                continue; //Skip too long tips
            
            TipsFromShitRimWorldSays.Add(new() {Source = $"u/{author}, on r/ShitRimworldSays", TipBody = body});
        }
        
        Log.Message($"[BetterLoading|ShitRimWorldSays Compat] ShitRimWorldSays loaded {TipsFromShitRimWorldSays.Count} tips from reddit.");
        LoadingScreenTipManager.OnAvailableTipSetChanged();
    }

    public static void ShitRimworldSays_ctor_Postfix()
    {
        if (UserWantsToHideVanillaTips())
        {
            Log.Message("[BetterLoading|ShitRimWorldSays Compat] Hiding vanilla loading tips, as you have disabled them in SRWS mod settings.");
            LoadingScreenTipManager.HideVanillaTips = true;
        }
    }
}