namespace CAP_Core.Logic.Isa
{
    /// <summary>
    /// Result of assembling source text with <see cref="IsaAssembler.AssembleWithSourceMap"/>:
    /// the program ROM words plus a source map so a UI can highlight the source line
    /// that produced the instruction the program counter currently points at.
    /// </summary>
    /// <param name="Words">The encoded instruction bytes.</param>
    /// <param name="InstructionLineNumbers">
    /// <c>InstructionLineNumbers[i]</c> is the 1-based source line that produced ROM word <c>i</c>.
    /// </param>
    public sealed record IsaAssemblyResult(byte[] Words, IReadOnlyList<int> InstructionLineNumbers);
}
