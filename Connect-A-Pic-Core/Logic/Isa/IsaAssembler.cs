using System.Globalization;

namespace CAP_Core.Logic.Isa
{
    /// <summary>
    /// Two-pass assembler for the learning ISA: turns assembly text into the
    /// 8-bit instruction words defined by <see cref="IsaInstruction.All"/>.
    /// Supports comments (<c>;</c>), labels (<c>name:</c>) and decimal operands.
    /// Every malformed input throws <see cref="IsaAssemblerException"/> with the
    /// 1-based line number.
    /// </summary>
    public sealed class IsaAssembler
    {
        private const char CommentStart = ';';
        private const char LabelEnd = ':';

        /// <summary>
        /// Assembles source text into program ROM words.
        /// </summary>
        /// <param name="source">Assembly source, one instruction per line.</param>
        /// <returns>The encoded instruction bytes, at most <see cref="IsaMachine.ProgramRomWords"/>.</returns>
        /// <exception cref="IsaAssemblerException">The source is malformed.</exception>
        public byte[] Assemble(string source)
        {
            var lines = SplitLines(source);
            var labels = CollectLabels(lines);
            return EmitWords(lines, labels);
        }

        private static List<SourceLine> SplitLines(string source)
        {
            var result = new List<SourceLine>();
            var rawLines = source.Replace("\r\n", "\n").Split('\n');
            for (int i = 0; i < rawLines.Length; i++)
            {
                var text = StripComment(rawLines[i]).Trim();
                result.Add(new SourceLine(i + 1, text));
            }

            return result;
        }

        private static string StripComment(string line)
        {
            int commentIndex = line.IndexOf(CommentStart);
            return commentIndex >= 0 ? line[..commentIndex] : line;
        }

        private static Dictionary<string, int> CollectLabels(List<SourceLine> lines)
        {
            var labels = new Dictionary<string, int>(StringComparer.Ordinal);
            int address = 0;
            foreach (var line in lines)
            {
                var body = ExtractLabel(line, labels, address);
                if (body.Length > 0)
                {
                    address++;
                }
            }

            if (address > IsaMachine.ProgramRomWords)
            {
                throw new IsaAssemblerException(
                    $"Program has {address} instructions but the ROM holds only {IsaMachine.ProgramRomWords} words.",
                    lines[^1].Number);
            }

            return labels;
        }

        private static string ExtractLabel(SourceLine line, Dictionary<string, int> labels, int address)
        {
            int colonIndex = line.Text.IndexOf(LabelEnd);
            if (colonIndex < 0)
            {
                return line.Text;
            }

            var name = line.Text[..colonIndex].Trim();
            if (!IsValidLabel(name))
            {
                throw new IsaAssemblerException($"Invalid label name '{name}'.", line.Number);
            }

            if (!labels.TryAdd(name, address))
            {
                throw new IsaAssemblerException($"Duplicate label '{name}'.", line.Number);
            }

            return line.Text[(colonIndex + 1)..].Trim();
        }

        private static bool IsValidLabel(string name)
        {
            if (name.Length == 0 || !(char.IsLetter(name[0]) || name[0] == '_'))
            {
                return false;
            }

            return name.All(c => char.IsLetterOrDigit(c) || c == '_');
        }

        private static byte[] EmitWords(List<SourceLine> lines, Dictionary<string, int> labels)
        {
            var words = new List<byte>();
            foreach (var line in lines)
            {
                var body = SkipLabel(line.Text);
                if (body.Length > 0)
                {
                    words.Add(EmitWord(line, body, labels));
                }
            }

            return words.ToArray();
        }

        private static string SkipLabel(string text)
        {
            int colonIndex = text.IndexOf(LabelEnd);
            return colonIndex < 0 ? text : text[(colonIndex + 1)..].Trim();
        }

        private static byte EmitWord(SourceLine line, string body, Dictionary<string, int> labels)
        {
            var tokens = body.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var instruction = IsaInstruction.FindByMnemonic(tokens[0])
                ?? throw new IsaAssemblerException($"Unknown mnemonic '{tokens[0]}'.", line.Number);
            int operand = ResolveOperand(line, instruction, tokens, labels);
            return instruction.Encode(operand);
        }

        private static int ResolveOperand(
            SourceLine line, IsaInstruction instruction, string[] tokens, Dictionary<string, int> labels)
        {
            if (instruction.OperandKind == IsaOperandKind.None)
            {
                if (tokens.Length > 1)
                {
                    throw new IsaAssemblerException(
                        $"{instruction.Mnemonic} takes no operand.", line.Number);
                }

                return 0;
            }

            if (tokens.Length != 2)
            {
                throw new IsaAssemblerException(
                    $"{instruction.Mnemonic} needs exactly one operand.", line.Number);
            }

            int operand = instruction.OperandKind == IsaOperandKind.CodeAddress
                ? ResolveCodeAddress(line, tokens[1], labels)
                : ParseNumber(line, tokens[1]);

            if (operand > instruction.MaxOperand())
            {
                throw new IsaAssemblerException(
                    $"Operand {operand} is out of range for {instruction.Mnemonic} (0–{instruction.MaxOperand()}).",
                    line.Number);
            }

            return operand;
        }

        private static int ResolveCodeAddress(SourceLine line, string token, Dictionary<string, int> labels)
        {
            if (labels.TryGetValue(token, out int address))
            {
                return address;
            }

            if (int.TryParse(token, NumberStyles.None, CultureInfo.InvariantCulture, out int numeric))
            {
                return numeric;
            }

            throw new IsaAssemblerException($"Undefined label '{token}'.", line.Number);
        }

        private static int ParseNumber(SourceLine line, string token)
        {
            if (!int.TryParse(token, NumberStyles.None, CultureInfo.InvariantCulture, out int value))
            {
                throw new IsaAssemblerException($"Expected a decimal number, got '{token}'.", line.Number);
            }

            return value;
        }

        private sealed record SourceLine(int Number, string Text);
    }
}
