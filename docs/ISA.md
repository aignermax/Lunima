# Lunima Learning ISA (4-bit)

The spec the photonic CPU will be built and verified against (rung 5 of
[ROADMAP.md](ROADMAP.md)). Hack-computer spirit: the smallest machine that can run a
counter loop. `IsaInstruction.All` in `Connect-A-Pic-Core/Logic/Isa/` is the single
source of truth for the encoding; this page is the human-readable mirror.
`IsaEmulator` is the golden model — the photonic implementation will be checked
against it cycle by cycle.

## Machine model

- **Data word:** 4 bits, unsigned, values 0–15.
- **Registers:** one accumulator `ACC` (4 bit) and the program counter `PC` (4 bit).
  An accumulator machine is the simplest model that runs a counter loop.
- **Program ROM:** 16 words, one 8-bit instruction per word.
- **Data RAM:** 4 words of 4 bits — the direct scale-up path of the shipped
  RAM 2x2 example.
- **Overflow semantics:** all arithmetic wraps modulo 16 (4-bit wrap-around).
  `15 + 1 = 0`; there are no carry/overflow flags. `NOT` is bitwise and masked to
  4 bits. This is pinned by `IsaEmulatorTests.Add_WrapsModulo16`.

## Instruction encoding

Fixed 8-bit words: **high nibble = opcode, low nibble = operand**
(immediate, RAM address, or ROM address).

| Opcode | Mnemonic | Operand | Effect |
|--------|----------|---------|--------|
| `0x0` | `LOAD imm`  | 0–15 | `ACC := imm` |
| `0x1` | `ADD addr`  | RAM 0–3 | `ACC := (ACC + RAM[addr]) mod 16` |
| `0x2` | `AND addr`  | RAM 0–3 | `ACC := ACC & RAM[addr]` |
| `0x3` | `NOT`       | — | `ACC := ~ACC & 0xF` |
| `0x4` | `STORE addr`| RAM 0–3 | `RAM[addr] := ACC` |
| `0x5` | `JMP addr`  | ROM 0–15 | `PC := addr` |
| `0x6` | `JZ addr`   | ROM 0–15 | `PC := addr` if `ACC == 0`, else `PC := PC + 1` |
| `0x7` | `HALT`      | — | stop; further steps are no-ops |

After every non-jump instruction `PC := PC + 1` (mod 16). Opcodes `0x8`–`0xF` are
reserved and fault when executed, as do RAM addresses ≥ 4 — assembled programs can
never contain either.

## Assembly syntax

One instruction per line; `;` starts a comment; `name:` defines a label for
`JMP`/`JZ` (also on the same line as an instruction); operands are decimal.
Examples: [`examples/isa/count-to-5.asm`](../examples/isa/count-to-5.asm) and
[`examples/isa/add-two-numbers.asm`](../examples/isa/add-two-numbers.asm).

## What implements which part

- `ADD` → shipped example **Logic Gate 4-Bit Adder** (ripple-carry; `Cin = 0`, `Cout`
  dropped gives exactly the mod-16 wrap above).
- `AND` → shipped example **Logic Gate AND 4-bit** (four AND-from-NAND slices,
  `A0–A3` & `B0–B3` → `Y0–Y3`; the single-slice **Logic Gate AND-from-NAND** is the
  gate it scales, and the NAND + NOT datapath of **Logic Gate ALU 1-bit** is the same
  cascade with an OR sibling — `OR` is not an ISA instruction).
- `NOT` → shipped example **Logic Gate NOT 4-bit** (four NOT-NAND slices, `A0–A3` →
  `Y0–Y3`; the single-slice **Logic Gate NOT-NAND** is the gate it scales).
- `PC` (increment, load for jumps) → shipped example **Logic Gate PC 2-bit** (scale to 4 bit).
- Data RAM (`LOAD`/`STORE` path) → shipped example **RAM 2x2** (4 words × 4 bit).
- The shipped **Logic Gate Register 2-bit** is the template for the accumulator.
