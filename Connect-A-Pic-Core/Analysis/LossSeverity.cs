namespace CAP_Core.Analysis
{
    /// <summary>
    /// Severity classification for optical path loss based on dB thresholds.
    /// Used for visual color-coding of loss budget results.
    /// </summary>
    public enum LossSeverity
    {
        /// <summary>
        /// Acceptable loss: less than 3 dB. Displayed as green.
        /// </summary>
        Low,

        /// <summary>
        /// Moderate loss: between 3 dB and 10 dB. Displayed as yellow.
        /// </summary>
        Medium,

        /// <summary>
        /// Critical loss: greater than 10 dB. Displayed as red.
        /// </summary>
        High
    }
}
