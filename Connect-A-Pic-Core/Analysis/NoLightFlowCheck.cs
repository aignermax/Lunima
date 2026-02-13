using System.Numerics;

namespace CAP_Core.Analysis
{
    /// <summary>
    /// Checks for components where no light flows through any of their pins.
    /// This may indicate the component is disconnected from the optical network.
    /// </summary>
    public class NoLightFlowCheck : ISanityCheck
    {
        private const string Category = "No Light Flow";

        /// <summary>
        /// Minimum magnitude threshold to consider light present at a pin.
        /// </summary>
        public const double MinLightMagnitude = 1e-10;

        /// <inheritdoc />
        public IEnumerable<SanityCheckEntry> Run(SanityCheckContext context)
        {
            if (context.LightField == null || context.LightField.Count == 0)
            {
                yield break;
            }

            foreach (var component in context.Components)
            {
                if (HasAnyLightFlow(component, context.LightField))
                {
                    continue;
                }

                yield return new SanityCheckEntry(
                    SanityCheckSeverity.Info,
                    Category,
                    $"Component '{component.Identifier}' has no " +
                    "light flowing through any of its pins.");
            }
        }

        private static bool HasAnyLightFlow(
            Components.Component component,
            IReadOnlyDictionary<Guid, Complex> lightField)
        {
            var pins = component.GetAllPins();
            foreach (var pin in pins)
            {
                if (HasLight(pin.IDInFlow, lightField) ||
                    HasLight(pin.IDOutFlow, lightField))
                {
                    return true;
                }
            }
            return false;
        }

        private static bool HasLight(
            Guid pinId,
            IReadOnlyDictionary<Guid, Complex> lightField)
        {
            return lightField.TryGetValue(pinId, out var value) &&
                   value.Magnitude > MinLightMagnitude;
        }
    }
}
