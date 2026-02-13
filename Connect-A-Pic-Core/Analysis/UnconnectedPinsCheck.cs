using CAP_Core.Components;

namespace CAP_Core.Analysis
{
    /// <summary>
    /// Checks for physical pins that have no waveguide connection.
    /// Unconnected optical pins may indicate an incomplete design.
    /// </summary>
    public class UnconnectedPinsCheck : ISanityCheck
    {
        private const string Category = "Unconnected Pins";

        /// <inheritdoc />
        public IEnumerable<SanityCheckEntry> Run(SanityCheckContext context)
        {
            var connectedPins = GetConnectedPinSet(context.Connections);
            var entries = new List<SanityCheckEntry>();

            foreach (var component in context.Components)
            {
                foreach (var pin in component.PhysicalPins)
                {
                    if (connectedPins.Contains(pin))
                    {
                        continue;
                    }

                    entries.Add(new SanityCheckEntry(
                        SanityCheckSeverity.Warning,
                        Category,
                        $"Pin '{pin.Name}' on '{component.Identifier}' " +
                        "is not connected to any waveguide."));
                }
            }

            return entries;
        }

        private static HashSet<PhysicalPin> GetConnectedPinSet(
            IReadOnlyList<WaveguideConnection> connections)
        {
            var pins = new HashSet<PhysicalPin>();
            foreach (var conn in connections)
            {
                pins.Add(conn.StartPin);
                pins.Add(conn.EndPin);
            }
            return pins;
        }
    }
}
