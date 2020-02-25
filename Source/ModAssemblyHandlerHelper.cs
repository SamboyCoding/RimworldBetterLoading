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
            var directoryInfo = new DirectoryInfo(mod.AssembliesFolder);
            
            return directoryInfo.Exists
                ? directoryInfo.GetFiles("*.*", SearchOption.AllDirectories).Where(file => file.Extension.ToLower() == ".dll").ToList()
                : new List<FileInfo>();
        }
    }
}