using System;
using System.IO;
using Tomlet;
using Tomlet.Exceptions;
using Tomlet.Models;
using Verse;

namespace BetterLoading;

/// <summary>
/// This class exists because XML is the spawn of the devil and I refuse to use it for config files.
/// </summary>
public static class BetterLoadingConfigManager
{
    private static string _oldCachedLoadingTipsPath = Path.Combine(GenFilePaths.ConfigFolderPath, "BetterLoading_Cached_Tips");
    public static string ConfigFilePath = Path.Combine(GenFilePaths.ConfigFolderPath, "BetterLoading.toml");

    public static BetterLoadingConfig Config { get; private set; } = new();
    
    static BetterLoadingConfigManager()
    {
        //Register a byte array <=> base64 string converter
        TomletMain.RegisterMapper(bytes => new TomlString(Convert.ToBase64String(bytes ?? throw new NullReferenceException("Cannot serialize a null byte array"))), tomlValue =>
        {
            if (tomlValue is not TomlString tomlString)
                throw new TomlTypeMismatchException(typeof(TomlString), tomlValue.GetType(), typeof(byte[]));
            return Convert.FromBase64String(tomlString.Value);
        });
    }

    public static void Load()
    {
        if(File.Exists(_oldCachedLoadingTipsPath))
            File.Delete(_oldCachedLoadingTipsPath);

        if (!File.Exists(ConfigFilePath))
        {
            Config = BetterLoadingConfig.CreateDefault();
            return;
        }

        try
        {
            var doc = TomlParser.ParseFile(ConfigFilePath);
            Config = TomletMain.To<BetterLoadingConfig>(doc);
            LoadingScreenTipManager.TryReadCachedTipsFromConfig();
        }
        catch (TomlException e)
        {
            Log.Error($"[BetterLoading] {e.GetType()} thrown while parsing config file: {e.Message}. Config will be reset.");
            File.Delete(ConfigFilePath);
            Config = BetterLoadingConfig.CreateDefault();
        }
    }

    public static void Save()
    {
        var tomlString = TomletMain.TomlStringFrom(Config);
        File.WriteAllText(ConfigFilePath, tomlString);
    }
}