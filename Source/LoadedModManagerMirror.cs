using System.Collections.Generic;
using System.Reflection;
using Verse;

namespace BetterLoading
{
    public static class LoadedModManagerMirror
    {
        private static readonly FieldInfo PatchedDefsField = typeof(LoadedModManager).GetField("patchedDefs", BindingFlags.NonPublic | BindingFlags.Static);

        public static List<Def> PatchedDefs
        {
            get => (List<Def>) PatchedDefsField.GetValue(null);
            set => PatchedDefsField.SetValue(null, value);
        }
    }
}