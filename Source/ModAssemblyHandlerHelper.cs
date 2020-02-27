using System.Collections.Generic;
using System.IO;
using System.Linq;
using Verse;

namespace BetterLoading
{
    public static class ModAssemblyHandlerHelper
    {
        public static List<FileInfo> GetDlls(ModContentPack mod)
        {
            //Copied and adapter from ModAssemblyHandler#reloadAll
            return ModContentPack.GetAllFilesForMod(mod, "Assemblies/", e => e.ToLower() == ".dll").Select(f => f.Value).ToList();
        }
    }
}