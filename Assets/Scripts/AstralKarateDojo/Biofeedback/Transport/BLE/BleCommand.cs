namespace AstralKarateDojo.Biofeedback.Transport.BLE
{
    /// <summary>
    /// Base command for interacting with the Android BLE plugin.
    /// Concrete commands translate Unity calls into plugin commands and listen for responses.
    /// </summary>
    public abstract class BleCommand
    {
        /// <summary>Timeout (seconds) when queued as an active command.</summary>
        public float Timeout => _timeout;
        protected float _timeout = 5f;

        /// <summary>Run in parallel with other commands (no exclusive slot).</summary>
        public readonly bool RunParallel;

        /// <summary>Run continuously until manually ended (still parallel).</summary>
        public readonly bool RunContinuous;

        protected BleCommand(bool runParallel = false, bool runContinuous = false)
        {
            RunParallel = runParallel;
            RunContinuous = runContinuous;
        }

        public abstract void Start();
        public virtual void End() { }
        public virtual void EndOnTimeout() => End();
        public virtual bool CommandReceived(BleObject obj) => false;
    }
}
