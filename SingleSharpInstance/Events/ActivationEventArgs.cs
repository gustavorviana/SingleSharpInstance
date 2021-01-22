using System;

namespace SingleSharpInstance.Events
{
    public class ActivationEventArgs : EventArgs
    {
        /// <summary>
        /// Signals if this is the first activation
        /// </summary>
        public bool IsFirstActivation { get; }

        /// <summary>
        /// Activation arguments
        /// </summary>
        public string[] Args { get; }

        public ActivationEventArgs(string[] args, bool firstActivation)
        {
            this.IsFirstActivation = firstActivation;
            this.Args = args;
        }
    }
}
