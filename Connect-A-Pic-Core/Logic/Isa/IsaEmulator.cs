namespace CAP_Core.Logic.Isa
{
    /// <summary>
    /// Reference interpreter (golden model) for the learning ISA specified in
    /// docs/ISA.md. Executes one instruction per <see cref="Step"/> and exposes the
    /// full machine state so a later photonic implementation can be checked against
    /// it cycle by cycle. All arithmetic wraps modulo 16 (4-bit data word).
    /// </summary>
    public sealed class IsaEmulator
    {
        private readonly byte[] _program;
        private readonly IIsaAlu _alu;
        private readonly IIsaZeroFlag _zeroFlag;
        private readonly IIsaDataMemory _dataMemory;

        /// <summary>
        /// Creates a machine with the given program loaded into the ROM.
        /// Unused ROM words are zero (LOAD 0).
        /// </summary>
        /// <param name="program">Encoded instruction bytes, at most <see cref="IsaMachine.ProgramRomWords"/>.</param>
        /// <param name="alu">
        /// The ALU that computes ADD, AND and NOT. Defaults to <see cref="GoldenIsaAlu"/>
        /// (the C# golden model); pass <see cref="PhotonicAdderAlu"/>,
        /// <see cref="PhotonicNotAlu"/> or <see cref="PhotonicAndAlu"/> (or a
        /// <see cref="CompositeIsaAlu"/> for several) to run the operation on the
        /// photonic network. No other instruction is affected.
        /// </param>
        /// <param name="zeroFlag">
        /// The zero flag <c>JZ</c> asks for its branch decision. Defaults to
        /// <see cref="GoldenIsaZeroFlag"/> (the C# <c>ACC == 0</c> check); pass
        /// <see cref="PhotonicZeroFlag"/> to decide every <c>JZ</c> on the photonic
        /// zero-detect network. No other instruction is affected.
        /// </param>
        /// <param name="dataMemory">
        /// The data memory <c>STORE</c> writes and the RAM operands of
        /// <c>ADD</c>/<c>AND</c> read. Defaults to <see cref="GoldenIsaDataMemory"/>
        /// (the C# array); pass <see cref="PhotonicDataMemory"/> to hold the four
        /// data words in the photonic registers of the RAM 4x4 network. No other
        /// instruction is affected.
        /// </param>
        /// <exception cref="ArgumentException">The program is larger than the ROM.</exception>
        public IsaEmulator(byte[] program, IIsaAlu? alu = null, IIsaZeroFlag? zeroFlag = null,
            IIsaDataMemory? dataMemory = null)
        {
            if (program.Length > IsaMachine.ProgramRomWords)
            {
                throw new ArgumentException(
                    $"Program has {program.Length} words but the ROM holds only {IsaMachine.ProgramRomWords}.",
                    nameof(program));
            }

            _program = new byte[IsaMachine.ProgramRomWords];
            program.CopyTo(_program, 0);
            _alu = alu ?? new GoldenIsaAlu();
            _zeroFlag = zeroFlag ?? new GoldenIsaZeroFlag();
            _dataMemory = dataMemory ?? new GoldenIsaDataMemory();
        }

        /// <summary>Address of the next instruction to execute (0–15).</summary>
        public int ProgramCounter { get; private set; }

        /// <summary>The accumulator, always in the range 0–15.</summary>
        public int Accumulator { get; private set; }

        /// <summary>
        /// The 4-word data RAM, read through the configured <see cref="IIsaDataMemory"/>
        /// — for the photonic memory this consults the network, so the UI always shows
        /// the words the light actually holds.
        /// </summary>
        public IReadOnlyList<int> Ram =>
            Enumerable.Range(0, IsaMachine.RamWords).Select(_dataMemory.Read).ToArray();

        /// <summary>True once HALT has executed; further <see cref="Step"/> calls are no-ops.</summary>
        public bool IsHalted { get; private set; }

        /// <summary>
        /// Restores the power-on state: PC, accumulator and RAM cleared, halt flag reset.
        /// The program stays loaded.
        /// </summary>
        public void Reset()
        {
            ProgramCounter = 0;
            Accumulator = 0;
            IsHalted = false;
            _dataMemory.Reset();
        }

        /// <summary>
        /// Executes the single instruction at <see cref="ProgramCounter"/>.
        /// Does nothing when the machine is halted.
        /// </summary>
        /// <exception cref="InvalidOperationException">
        /// The fetched word is a reserved opcode or addresses RAM beyond word 3.
        /// </exception>
        public void Step()
        {
            if (IsHalted)
            {
                return;
            }

            var instruction = IsaInstruction.Decode(_program[ProgramCounter], out int operand)
                ?? throw new InvalidOperationException(
                    $"Reserved opcode 0x{_program[ProgramCounter]:X2} at ROM address {ProgramCounter}.");

            int nextPc = (ProgramCounter + 1) % IsaMachine.ProgramRomWords;
            switch (instruction.Opcode)
            {
                case IsaOpcode.Load:
                    Accumulator = operand;
                    break;
                case IsaOpcode.Add:
                    Accumulator = _alu.Add(Accumulator, ReadRam(operand));
                    break;
                case IsaOpcode.And:
                    Accumulator = _alu.And(Accumulator, ReadRam(operand));
                    break;
                case IsaOpcode.Not:
                    Accumulator = _alu.Not(Accumulator);
                    break;
                case IsaOpcode.Store:
                    _dataMemory.Write(CheckedRamAddress(operand), Accumulator);
                    break;
                case IsaOpcode.Jmp:
                    nextPc = operand;
                    break;
                case IsaOpcode.Jz:
                    nextPc = _zeroFlag.IsZero(Accumulator) ? operand : nextPc;
                    break;
                case IsaOpcode.Halt:
                    IsHalted = true;
                    break;
            }

            ProgramCounter = nextPc;
        }

        /// <summary>
        /// Steps until the machine halts or the step budget is exhausted.
        /// </summary>
        /// <param name="maxSteps">Maximum number of instructions to execute.</param>
        /// <returns>The number of instructions actually executed.</returns>
        public int Run(int maxSteps)
        {
            int steps = 0;
            while (!IsHalted && steps < maxSteps)
            {
                Step();
                steps++;
            }

            return steps;
        }

        private int ReadRam(int operand)
        {
            return _dataMemory.Read(CheckedRamAddress(operand));
        }

        private static int CheckedRamAddress(int operand)
        {
            if (operand >= IsaMachine.RamWords)
            {
                throw new InvalidOperationException(
                    $"RAM address {operand} is out of range; the RAM has {IsaMachine.RamWords} words.");
            }

            return operand;
        }
    }
}
