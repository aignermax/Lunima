namespace CAP_Core.Logic.Isa
{
    /// <summary>
    /// Thrown when assembly source text cannot be assembled. Always carries the
    /// 1-based source line number that caused the failure.
    /// </summary>
    public sealed class IsaAssemblerException : Exception
    {
        /// <summary>
        /// Creates an assembler error for a specific source line.
        /// </summary>
        /// <param name="message">What went wrong, without the line prefix.</param>
        /// <param name="lineNumber">The 1-based source line number.</param>
        public IsaAssemblerException(string message, int lineNumber)
            : base($"Line {lineNumber}: {message}")
        {
            LineNumber = lineNumber;
        }

        /// <summary>The 1-based source line number that caused the error.</summary>
        public int LineNumber { get; }
    }
}
