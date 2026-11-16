# Optional process photodetector

`process.photodetector` describes a direct-detection receiver for any listen-only
output, including a coupler whose laser is off. Existing PDKs require no changes.
This is a headless description/resolution API; it does not change normalized
threshold decisions, calibrate optical traces, or implement an absolute BER chain.

```json
{
  "process": {
    "name": "Example process",
    "photodetector": {
      "responsivityAmpsPerWatt": 1.0,
      "darkCurrentNa": 6.0,
      "sensitivityDbm": -20.0,
      "bitRateHz": 10000000000,
      "ber": 1e-12,
      "wavelengthNm": 1550,
      "provenance": "generic",
      "source": "Illustrative composite assumptions; see generic references below."
    }
  }
}
```

All fields inside a present section are required. All numbers must be finite.

| Field | Units and constraint |
|---|---|
| `responsivityAmpsPerWatt` | A/W, strictly positive |
| `darkCurrentNa` | nA, nonnegative |
| `sensitivityDbm` | dBm referenced to 1 mW, finite |
| `bitRateHz` | bits/s, strictly positive |
| `ber` | dimensionless bit error ratio, strictly between zero and one |
| `wavelengthNm` | nm, strictly positive |
| `provenance` | `generic`, `literature`, `custom`, or `foundry` |
| `source` | Nonempty citation/assumption description; for `foundry`, a qualification document and conditions |

`foundry` is an explicit assertion by the PDK author, not certification by Lunima.
Use it only for data actually qualified by the foundry and identify its source,
revision and operating conditions. JSON presence and the process's `foundry` name
do not promote guessed/literature values to foundry data. Invalid/incomplete
sections fail both strict and tolerant PDK loading. Save/load and process forks
preserve the immutable description.

## Generic references and limits

The generic Ge-on-Si description uses 1 A/W at 1550 nm (rounded from 0.95 A/W)
and 6 nA dark current from
[Germanium wrap-around photodetectors on Silicon photonics (2015)](https://e3s-center.berkeley.edu/wp-content/uploads/2019/06/2015_Germanium-Wrap-Around-Photodetectors-on-Silicon-Photonics.pdf).
Its −20 dBm sensitivity at BER 10⁻¹² and 10 Gb/s is an **illustrative rounded
system assumption**, inspired by the −18.5 dBm receiver result in
[Joo et al., Optics Express 18, 16474 (2010)](https://doi.org/10.1364/OE.18.016474).
The default combines different literature devices; it is not a measured tuple
or a foundry guarantee. The rounding makes sensitivity 1.5 dB more optimistic
than that receiver result. Use measured receiver specifications for sign-off.
Sensitivity depends on the receiver electronics, modulation, BER and bit rate;
it is not derived from responsivity and dark current or extrapolated to other rates.

## Selection and precedence

Call `ProcessPhotodetectorResolver.Resolve(pdk, outputDraft)` to explicitly select
an absolute receiver. It uses `process.photodetector`, or the generic description
when absent. An explicit `compactModelParameters.responsivityAmpsPerWatt` wins
over either description. The resulting composite is labelled `custom`, with
the original provenance/source retained in its source text; overriding R does
not qualify or recompute sensitivity.

Pass that **same resolved object** to `OutputPhotocurrent`,
`PhotodiodeRcModel`/`CompactModelRegistry.Create` and `NoiseModel.Detector`.
`EyeAnalysisRequest.Detector` and `MonteCarloEyeMetric.Detector` carry it into
their existing noise consumers. Do not apply a different compact-model override
after resolving the shared object. This API deliberately requires explicit
selection: merely loading a PDK does not convert legacy normalized traces to watts.
The caller must supply calibrated absolute power before selecting this path.

Without an explicit detector, time-domain and noise consumers keep their legacy
0.8 A/W defaults and existing normalized behavior; explicit legacy compact-model
responsivity parameters still win. With a detector, `NoiseModel.Detector` takes
precedence over its legacy `ResponsivityAPerW` initializer.

`OutputPhotocurrent.FromWatts(outputPinId, resolved, powerWatts)` accepts null
when absolute power is unknown and then returns null current. `FromDbm` uses
`P[W] = 0.001 × 10^(P[dBm]/10)`. Both return signal current `I[A] = R[A/W] × P[W]`:
at R = 1 A/W, 0 dBm gives 1.000 mA and −10 dBm gives 0.100 mA.
Dark current is stored separately and is not added to signal photocurrent.
The time-domain path adds its existing RC filtering, reaching the same current
at DC. Dark-current noise and absolute BER calibration belong to the subsequent
noise-chain work; the current noise equations are otherwise unchanged.
