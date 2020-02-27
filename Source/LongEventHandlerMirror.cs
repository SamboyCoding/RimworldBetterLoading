using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Verse;

namespace BetterLoading
{
    public static class LongEventHandlerMirror
    {
        private static readonly FieldInfo ListField = typeof(LongEventHandler).GetField("toExecuteWhenFinished", BindingFlags.NonPublic | BindingFlags.Static);
        private static readonly FieldInfo ExecutingField = typeof(LongEventHandler).GetField("executingToExecuteWhenFinished", BindingFlags.NonPublic | BindingFlags.Static);
        private static readonly FieldInfo EventQueueField = typeof(LongEventHandler).GetField("eventQueue", BindingFlags.NonPublic | BindingFlags.Static);

        public static List<Action> ToExecuteWhenFinished
        {
            get => (List<Action>) ListField.GetValue(null);
            set => ListField.SetValue(null, value);
        }

        public static bool CurrentlyExecutingToExecuteWhenFinished
        {
            get => (bool) ExecutingField.GetValue(null);
            set => ExecutingField.SetValue(null, value);
        }
        
        public static List<object> EventQueue
        {
            //This is what happens when your list contains a private type ;-;
            get => ((IEnumerable) EventQueueField.GetValue(null)).GetEnumerator().ToIEnumerable<object>().ToList();
            set => EventQueueField.SetValue(null, value);
        }
    }
}