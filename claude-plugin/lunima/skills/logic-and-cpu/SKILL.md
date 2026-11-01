---
name: logic-and-cpu
description: Build digital logic and a small processor from photonic circuits with Lunima — turn optical cells into gates with extracted truth tables, compose gates hierarchically, verify networks, clock registers, and run programs of Lunima's 4-bit ISA on the photonic units. Use for gates, adders, ALUs, registers, RAM, counters and the photonic CPU.
---

# From optical gates to a photonic CPU

Lunima's ladder: optical parts → **gate** → cell (adder slice, register) → unit (ALU, RAM,
accumulator, PC) → **CPU** that runs the 4-bit ISA. Each step is verified before the next.

## 1. Gate

1. Build the optical circuit (photonic-circuit-design skill) and `create_group {component_ids, name}`.
2. `make_gate {id, inputs, outputs, bias}`. Inputs (at most 4) and bias pins inject light, and outputs are thresholded.
   - The result is the **optical truth table** with output powers and `noise_margin_of_gap`.
   - A margin below about 0.05 is fragile. Improve the optics before relying on the gate.
3. `save_as_prefab {group_id}` stores the gate in the library. Remove it later with `delete_prefab`.

The shipped examples already contain working gates. Load one with `load_example`: "Logic Gate NOT-NAND", "Logic Gate OR-AND", "Logic Gate Register 2-bit", "Logic Gate Full Adder", "Logic Gate 4-Bit Adder", "Logic Gate ALU 1-bit", "Logic Gate RAM 2x2", "Logic Gate PC 2-bit".

## 2. Compose

- **Place gates:** `list_component_types {filter}` lists library gates under `groups`. Place them with `place_component {type: "<gate>", x, y, name}`.
- **Wire them:** use `create_connection` with pin names, gate output → gate input.
- **Grow the hierarchy:** group a finished cell and save it as a prefab too, then compose cells into units.
- **Watch for fan-out:** one output driving two inputs needs a splitter or copy cell. `logic_build` reports fan-out warnings.

## 3. Verify the logic

- `logic_build` builds the network. It reports the network inputs/outputs (named after the unwired gate pins or signal names), the gates, registers and the critical-path delay in ps.
- `logic_truth_table` enumerates every input combination. Compare each row with the intended function; for an adder, compute every sum.
- `logic_evaluate {inputs}` tests single cases.
- `logic_step {inputs, reset}` is one clock edge. Check that registers load when enabled and hold otherwise.

## 4. Run programs

`isa_run {program | sample, photonic}` runs Lunima's 4-bit accumulator ISA (docs/ISA.md):
- Instructions: `LOAD imm`, `ADD addr`, `AND addr`, `NOT`, `STORE addr`, `JMP addr`, `JZ addr`, `HALT`.
- Machine: 4-bit wrap-around arithmetic, 4 RAM words, a 16-word ROM, `label:`, `;` comments.
- Without `photonic` the C# golden model runs.
- With `photonic: true` (after `logic_build`), every operation the built chip implements runs through the optical network. `photonic_units` shows which ones: ADD on the 4-bit adder, AND/NOT on the logic unit, the zero flag, accumulator and RAM. `photonic_status` gives the light's propagation time.
- Run the same program both ways. The results must match; a mismatch is a hardware bug.

## Physical sanity

A gate's output must stay above its threshold through a chain of gates. Check that each stage's power stays within its gates' margins (`simulate`, `get_light_values`) before claiming a deep circuit works.
