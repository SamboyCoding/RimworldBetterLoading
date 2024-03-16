using System;
using System.Collections.Generic;
using BetterLoading.Stage;

namespace BetterLoading
{
    public class StageTimingData
    {
        internal static readonly List<StageTimingData> ExecutedStages = new();
        
        public readonly DateTime Start;
        public DateTime End;
        public readonly LoadingStage Stage;

        public StageTimingData(DateTime start, LoadingStage stage)
        {
            Start = start;
            Stage = stage;
        }
    }
}