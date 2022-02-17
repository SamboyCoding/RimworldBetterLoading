using System;
using Tomlet.Attributes;

namespace BetterLoading;

public class BetterLoadingConfig
{

    public static BetterLoadingConfig CreateDefault()
    {
        return new()
        {
            TipCache = new()
            {
                Version = TipCacheConfig.SupportedVersion,
            }
        };
    }
    
    [TomlPrecedingComment("The TipCache caches information about loading screen tips so that they can be displayed as soon as the loading screen starts after the first run.")]
    public TipCacheConfig TipCache; 

    [TomlDoNotInlineObject]
    public class TipCacheConfig
    {
        public static readonly int SupportedVersion = 1;
        
        [TomlPrecedingComment("The internal version number of the TipCache tip blob. If this number is different from the one expected by the mod, the TipCache will be cleared.")]
        public int Version;
        [TomlPrecedingComment("The raw tip blob. NOT intended to be manually edited.")]
        public byte[] Tips = Array.Empty<byte>();
    }
}