using System;
using System.Collections.Generic;
using Harmony;

namespace BetterLoading.Stage
{
    public abstract class LoadingStage
    {
        private static List<Type> _initializedStages = new List<Type>();
        /// <summary>
        /// Required public no-args constructor
        /// </summary>
        public LoadingStage(HarmonyInstance instance)
        {
            if (!_initializedStages.Contains(GetType()))
            {
                // ReSharper disable once VirtualMemberCallInConstructor
                DoPatching(instance);
                _initializedStages.Add(GetType());
            }
        }

        /// <summary>
        /// Gets the name of the stage, to draw on the label when this stage is the active one.
        /// </summary>
        /// <returns>A string representing the human-readable name of this stage.</returns>
        public abstract string GetStageName();

        /// <summary>
        /// Gets the name of the current action being performed by this step (e.g., for the 'Initializing Mods' stage, this could be the name of the current mod).
        /// If this returns null the current stage progress label will just show a number, not a description of the action. 
        /// </summary>
        /// <returns>A (human-readable) string describing the current action being performed. </returns>
        public abstract string? GetCurrentStepName();

        /// <summary>
        /// Gets the current progress value (i.e the 100 in the example displayed progress 100/200, or the numerator of the fraction used to calculate percentage) 
        /// </summary>
        /// <returns>A number greater than 0 and less than, equal to, or, to mark the stage as complete, 1 more than, the value returned by getMaximumProgress()</returns>
        public abstract int GetCurrentProgress();
        
        /// <summary>
        /// Gets the maximum value that should be returned by getCurrentProgress()
        /// </summary>
        /// <returns>A number greater than 0.</returns>
        public abstract int GetMaximumProgress();

        /// <summary>
        /// Run your harmony patches in here - it will only be called once per type.
        /// </summary>
        /// <param name="instance">A HarmonyInstance bound to BetterLoading</param>
        public abstract void DoPatching(HarmonyInstance instance);

        /// <summary>
        /// Returns whether or not this stage is complete (if it is, the loading screen will move to the next).
        /// The default implementation returns true if GetCurrentProgress returns (GetMaximumProcess + 1)
        /// </summary>
        /// <returns>True to move on to the next stage, otherwise false.</returns>
        public virtual bool IsCompleted()
        {
            return GetCurrentProgress() - GetMaximumProgress() == 1;
        }

        /// <summary>
        /// Called when a stage becomes the active stage, to allow it to do late-stage initialization.
        /// </summary>
        public virtual void BecomeActive()
        {
        }
        
        /// <summary>
        /// Called when a stage is no longer active (e.g. it finishes), to allow it to do cleanup.
        /// </summary>
        public virtual void BecomeInactive()
        {
        }
    }
}