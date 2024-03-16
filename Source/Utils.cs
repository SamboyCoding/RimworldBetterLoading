using System;
using System.Collections;
using System.Collections.Generic;

namespace BetterLoading
{
    public static class Utils
    {
        public static IEnumerable<T> ToIEnumerable<T>(this IEnumerator<T> enumerator) {
            while ( enumerator.MoveNext() ) {
                yield return enumerator.Current;
            }
        }
        
        public static IEnumerable<T> ToIEnumerable<T>(this IEnumerator enumerator) {
            while ( enumerator.MoveNext() ) {
                yield return (T) enumerator.Current;
            }
        }

        public static bool DeclaresOwnMethod(this Type t, string methodName)
        {
            return t.GetMethod(methodName)?.DeclaringType == t;
        }

        public static bool HarmonyPatchCancelMethod()
        {
            return false;
        }
        
        public static void DebugLog(string message)
        {
            if (BetterLoadingConfigManager.Config.VerboseLogging)
                Verse.Log.Message($"[BetterLoading Verbose] {message}");
        }
    }
}