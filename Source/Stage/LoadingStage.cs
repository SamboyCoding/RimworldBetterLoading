using Harmony;

namespace BetterLoading.Stage
{
    public abstract class LoadingStage
    {
        /// <summary>
        /// Required public no-args constructor
        /// </summary>
        public LoadingStage(HarmonyInstance instance)
        {
        }

        /// <summary>
        /// Gets the name of the stage, to draw on the label when this stage is the active one.
        /// </summary>
        /// <returns>A string representing the human-readable name of this stage.</returns>
        public abstract string GetStageName();

        /// <summary>
        /// Gets the name of the current action being performed by this step (e.g., for the 'Initializing Mods' stage, this could be the name of the current mod).
        /// </summary>
        /// <returns>A (human-readable) string describing the current action being performed. </returns>
        public abstract string GetCurrentStepName();

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
    }
}