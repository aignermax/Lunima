# PDK JSON Format Guide

This guide explains how to create a PDK (Process Design Kit) JSON file for Lunima, and how to convert Python-based (Nazca) PDKs using AI assistance.

---

## Quick Start

A PDK JSON file describes a set of photonic components — their physical dimensions, pin positions, and optical S-matrix responses. Lunima reads these files to populate the component library.

---

## Full PDK JSON Structure

```json
{
  "fileFormatVersion": 1,
  "name": "My Foundry PDK",
  "description": "Description of the PDK",
  "foundry": "My Foundry",
  "version": "1.0.0",
  "defaultWavelengthNm": 1550,
  "nazcaModuleName": "my_pdk",
  "components": [
    {
      "name": "1x2 MMI Splitter",
      "category": "Splitters",
      "nazcaFunction": "my_pdk.mmi1x2",
      "nazcaParameters": "length=100",
      "widthMicrometers": 80,
      "heightMicrometers": 55,
      "nazcaOriginOffsetX": 0,
      "nazcaOriginOffsetY": 27.5,
      "pins": [
        { "name": "in",   "offsetXMicrometers": 0,  "offsetYMicrometers": 27.5, "angleDegrees": 180 },
        { "name": "out1", "offsetXMicrometers": 80, "offsetYMicrometers": 25.5, "angleDegrees": 0   },
        { "name": "out2", "offsetXMicrometers": 80, "offsetYMicrometers": 29.5, "angleDegrees": 0   }
      ],
      "sMatrix": {
        "wavelengthNm": 1550,
        "connections": [
          { "fromPin": "in", "toPin": "out1", "magnitude": 0.707, "phaseDegrees": 0 },
          { "fromPin": "in", "toPin": "out2", "magnitude": 0.707, "phaseDegrees": 0 }
        ]
      }
    }
  ]
}
```

---

## Field Reference

### Top-Level Fields

| Field | Required | Description |
|-------|----------|-------------|
| `fileFormatVersion` | Yes | Always `1` |
| `name` | Yes | Display name of the PDK |
| `description` | No | Optional description |
| `foundry` | No | Foundry or company name |
| `version` | No | PDK version string |
| `defaultWavelengthNm` | No | Default simulation wavelength (e.g. `1550`) |
| `nazcaModuleName` | No | Python module name for Nazca export (e.g. `"nazca"`) |
| `components` | Yes | List of component definitions |

### Component Fields

| Field | Required | Description |
|-------|----------|-------------|
| `name` | Yes | Display name shown in the component library |
| `category` | Yes | Group name for the component library panel |
| `nazcaFunction` | Yes | Python function name for Nazca export (e.g. `"pdk.mmi1x2"`) |
| `nazcaParameters` | No | Optional default parameters (e.g. `"length=100"`) |
| `widthMicrometers` | Yes | Component bounding box width in µm |
| `heightMicrometers` | Yes | Component bounding box height in µm |
| `nazcaOriginOffsetX` | No | X offset of Nazca origin from bounding box bottom-left (µm) |
| `nazcaOriginOffsetY` | No | Y offset of Nazca origin from bounding box bottom-left (µm) |
| `pins` | Yes | List of optical port definitions |
| `sMatrix` | No | S-matrix for optical simulation (omit to skip simulation) |

### Pin Fields

| Field | Required | Description |
|-------|----------|-------------|
| `name` | Yes | Port name (must match S-matrix references) |
| `offsetXMicrometers` | Yes | X position relative to bounding box bottom-left corner (µm) |
| `offsetYMicrometers` | Yes | Y position relative to bounding box bottom-left corner (µm) |
| `angleDegrees` | Yes | Port direction: `0`=right, `90`=up, `180`=left, `270`=down |

### S-Matrix Fields

| Field | Required | Description |
|-------|----------|-------------|
| `wavelengthNm` | Yes | Reference wavelength in nm |
| `connections` | Yes | List of port-to-port transmission entries |
| `fromPin` | Yes | Source pin name |
| `toPin` | Yes | Destination pin name |
| `magnitude` | Yes | Amplitude transmission (0.0–1.0); `1.0` = lossless |
| `phaseDegrees` | Yes | Phase shift in degrees (0–360) |

> **Note:** Only specify connections with non-zero transmission. Reciprocal paths (e.g. `out1 → in`) are automatically handled by the simulator if you omit them.

---

## Coordinate System

**Lunima uses a Y-down coordinate system** with the origin at the top-left corner of the canvas.

For component pin positions:
- `offsetXMicrometers` increases to the **right**
- `offsetYMicrometers` increases **downward**
- The reference point is the **bottom-left corner** of the component bounding box

**Nazca uses a Y-up coordinate system.** When converting from Nazca, you must flip Y coordinates:

```
lunima_y = component_height - nazca_y
```

### nazcaOriginOffset

Nazca components have their own internal origin point (the `ic0` port or similar). Use `nazcaOriginOffsetX/Y` to specify where this Nazca origin maps to in Lunima's bounding box coordinate system.

**Example:** A component with height 55 µm whose Nazca origin is at the center:
```json
"nazcaOriginOffsetX": 0,
"nazcaOriginOffsetY": 27.5
```

If your Nazca component has its origin at the bottom-left (unusual), set both offsets to `0`.

---

## Converting Python PDKs to JSON

### Step 1: Identify the component structure

In a Nazca PDK, a component is typically defined like:

```python
def mmi1x2(length=20, width=6):
    with nd.Cell(name='mmi1x2') as C:
        nd.Pin('in',  xs='Deep_Ridge').put(0, 0, 180)
        nd.Pin('out1', xs='Deep_Ridge').put(length, -2, 0)
        nd.Pin('out2', xs='Deep_Ridge').put(length, +2, 0)
        ...
    return C
```

### Step 2: Extract the key values

From Nazca pin definitions:
- `put(x, y, angle)` gives the pin position in Nazca coordinates
- Convert Y: `lunima_y = component_height - nazca_y`

### Step 3: Write the JSON entry

```json
{
  "name": "1x2 MMI Splitter",
  "category": "Splitters",
  "nazcaFunction": "pdk.mmi1x2",
  "widthMicrometers": 20,
  "heightMicrometers": 10,
  "nazcaOriginOffsetX": 0,
  "nazcaOriginOffsetY": 5,
  "pins": [
    { "name": "in",   "offsetXMicrometers": 0,  "offsetYMicrometers": 5, "angleDegrees": 180 },
    { "name": "out1", "offsetXMicrometers": 20, "offsetYMicrometers": 7, "angleDegrees": 0   },
    { "name": "out2", "offsetXMicrometers": 20, "offsetYMicrometers": 3, "angleDegrees": 0   }
  ]
}
```

> Nazca y=−2 → Lunima y = 5 − (−2) = 7
> Nazca y=+2 → Lunima y = 5 − (+2) = 3

---

## Using AI Assistance (ChatGPT / Claude)

AI models are very effective at converting Nazca Python PDKs to Lunima JSON format. Use the prompt template below.

### Template Prompt

```
I need to convert a Nazca Python PDK to Lunima JSON format.

Here is the Lunima PDK JSON schema:
- fileFormatVersion: 1
- Each component has: name, category, nazcaFunction, widthMicrometers, heightMicrometers,
  nazcaOriginOffsetX, nazcaOriginOffsetY, pins[], sMatrix{}
- Pins have: name, offsetXMicrometers, offsetYMicrometers, angleDegrees
- Coordinate system: Y-down, origin at bounding box bottom-left
- Nazca Y-axis is flipped: lunima_y = component_height - nazca_y
- sMatrix connections have: fromPin, toPin, magnitude (0–1), phaseDegrees

Here is my Nazca Python PDK code:
[PASTE YOUR PYTHON CODE HERE]

Please generate a complete Lunima PDK JSON file for all components found.
Use reasonable S-matrix values based on the component type if exact values are unknown
(e.g. 0.707 magnitude for 50/50 splitters, 0.99 for waveguides, 90° phase for crossing paths).
```

### Tips for Using AI

1. **Paste the entire PDK file** — AI works best with full context
2. **Review pin positions carefully** — the Y-axis flip is the most common source of errors
3. **Validate magnitude values** — check that `magnitude² ≤ 1` for each output port (energy conservation)
4. **Iterate component by component** — for large PDKs, ask the AI to convert one category at a time
5. **Check nazcaFunction names** — ensure they match the actual Python function names in your PDK

### Validating the Generated JSON

After loading the PDK in Lunima:
- Check the component library panel — all components should appear under their categories
- Place a component on the canvas and verify pins appear at correct positions
- Run a simulation and check that light flows through connections correctly
- Compare pin positions visually against known reference layouts

---

## Example: Complete Component Definitions

See the bundled example PDKs for reference:
- `PDKs/demo-pdk.json` — Demo components covering all major types
- `PDKs/siepic-ebeam-pdk.json` — Real-world SiEPIC EBeam foundry components

These files show correct usage of all fields including multi-pin components, optional parameters, and S-matrix definitions.
