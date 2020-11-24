using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using JetBrains.Annotations;
using Verse;

namespace BetterLoading
{
    public static class ModContentPackMirror
    {
        private static readonly FieldInfo PatchesField = typeof(ModContentPack).GetField("patches", BindingFlags.NonPublic | BindingFlags.Instance);
        
        public static List<PatchOperation>? GetPatches(ModContentPack instance)
        {
            return (List<PatchOperation>?) PatchesField.GetValue(instance);
        }
    }
}