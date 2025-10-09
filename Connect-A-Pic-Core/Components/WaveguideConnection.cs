using System.Numerics;

namespace CAP_Core.Components
{
    public class WaveguideConnection
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public PhysicalPin StartPin { get; set; }
        public PhysicalPin EndPin { get; set; }
        public double WidthMicrometers { get; set; } = 0.5; // Standard: 500nm
        public double BendRadiusMicrometers { get; set; } = 10.0;
        public WaveguideType Type { get; set; } = WaveguideType.Auto;

        // S-Matrix für Transmission (vereinfacht: 1.0, kann verlustbehaftet sein)
        public Complex TransmissionCoefficient { get; set; } = Complex.One;

        // Nazca-Export
        public string ExportToNazca()
        {
            var startComp = StartPin.ParentComponent;
            var endComp = EndPin.ParentComponent;
            var startCellName = $"cell_{startComp.PhysicalX}_{startComp.PhysicalY}";
            var endCellName = $"cell_{endComp.PhysicalX}_{endComp.PhysicalY}";

            return Type switch
            {
                WaveguideType.Straight =>
                    $"        ic.strt_p2p(pin1={startCellName}.pin['{StartPin.Name}'], " +
                    $"pin2={endCellName}.pin['{EndPin.Name}']).put()\n",

                WaveguideType.SBend =>
                    $"        ic.sbend_p2p(pin1={startCellName}.pin['{StartPin.Name}'], " +
                    $"pin2={endCellName}.pin['{EndPin.Name}'], " +
                    $"radius={BendRadiusMicrometers}).put()\n",

                _ => // Auto: Nazca wählt automatisch
                    $"        ic.cobra_p2p(pin1={startCellName}.pin['{StartPin.Name}'], " +
                    $"pin2={endCellName}.pin['{EndPin.Name}']).put()\n"
            };
        }
    }
}
