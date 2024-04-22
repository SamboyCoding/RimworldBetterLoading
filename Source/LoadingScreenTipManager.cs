using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using BetterLoading.Compat;
using Verse;

namespace BetterLoading
{
    public static class LoadingScreenTipManager
    {
        private static bool _tipDatabaseHasLoadedBackingField;
        private static bool _hideVanillaTipsBackingField;

        public static bool GameTipDatabaseHasLoaded
        {
            get => _tipDatabaseHasLoadedBackingField;
            set
            {
                _tipDatabaseHasLoadedBackingField = value;
                if (value) OnAvailableTipSetChanged();
            }
        }

        public static bool HideVanillaTips
        {
            get => _hideVanillaTipsBackingField;
            set
            {
                _hideVanillaTipsBackingField = value;
                OnAvailableTipSetChanged();
            }
        }


        public static string? LastShownTip;
        public static DateTime TimeLastTipShown = DateTime.MinValue;
        public const long MinTicksPerTip = 5 * 10_000_000; //5 seconds

        public static List<BetterLoadingTip> Tips = new();

        public static void OnAvailableTipSetChanged()
        {

            Tips.Clear();
            
            if (!HideVanillaTips)
                ReadTipsFromGameDatabase();

            if (ShitRimworldSaysCompat.TipsFromShitRimWorldSays != null)
                Tips.AddRange(ShitRimworldSaysCompat.TipsFromShitRimWorldSays);

            Tips = Tips.InRandomOrder().ToList();

            if (Tips.Count == 0)
                TryReadCachedTipsFromConfig();
            else
                TryWriteCachedTipsToConfig();
        }

        private static void ReadTipsFromGameDatabase()
        {
            if (!GameTipDatabaseHasLoaded)
                return;

            var allTips = DefDatabase<TipSetDef>.AllDefsListForReading.SelectMany(set => set.tips.Select(t => new BetterLoadingTip {TipBody = t, Source = set.modContentPack.Name})).ToList();

            Tips = allTips;
        }

        public static void TryReadCachedTipsFromConfig()
        {
            var version = BetterLoadingConfigManager.Config.TipCache.Version;

            if (version != BetterLoadingConfig.TipCacheConfig.SupportedVersion)
                return;

            var blobString = Encoding.UTF8.GetString(BetterLoadingConfigManager.Config.TipCache.Tips);
            var tips = blobString.Split('\0');

            Tips = tips
                .Select(t => t.Split('\u0001'))
                .Select(split => new BetterLoadingTip() {TipBody = split[0], Source = split[1]})
                .InRandomOrder()
                .ToList();

            Log.Message($"[BetterLoading] Read tip cache of {Tips.Count} tips from config.");
        }

        public static void TryWriteCachedTipsToConfig()
        {
            if (Tips.Count == 0)
                return;

            var blobString = string.Join("\0", Tips.Select(t => t.TipBody + "\u0001" + t.Source));
            var blob = Encoding.UTF8.GetBytes(blobString);

            BetterLoadingConfigManager.Config.TipCache.Version = BetterLoadingConfig.TipCacheConfig.SupportedVersion;
            BetterLoadingConfigManager.Config.TipCache.Tips = blob;

            BetterLoadingConfigManager.Save();
        }

        private static string FormatTip(BetterLoadingTip tip)
        {
            return $"{tip.TipBody}\n\n-{tip.Source}";
        }

        public static string GetTipToDisplay()
        {
            if (Tips.Count == 0 && (!GameTipDatabaseHasLoaded || HideVanillaTips))
                return HideVanillaTips ? "Gameplay tips will be shown here once they are loaded" : "Gameplay tips will be shown here once the game loads them (after stage 7 completes)";

            var timeCurrentTipShownFor = (DateTime.UtcNow - TimeLastTipShown).Ticks;
            var showTime = MinTicksPerTip;
            if (!string.IsNullOrWhiteSpace(LastShownTip))
            {
                var words = LastShownTip!.Split(' ').Length / 3.3; // ≈ 200 words per minute
                showTime = Math.Max(showTime, (long)(words * 10_000_000)); 
            }
            if (LastShownTip != null && timeCurrentTipShownFor < showTime)
                //We have a tip and it's not been long enough to change to the next one yet, return the last one
                return LastShownTip;

            //No tip chosen yet, or time for next tip - pick another and reset timer.
            LastShownTip = Tips.NullOrEmpty()
                ? "BetterLoading Warning: No tips could be located in your game. This is probably a bug with another mod"
                : FormatTip(Tips.Pop());

            TimeLastTipShown = DateTime.UtcNow;

            return LastShownTip;
        }
    }
}