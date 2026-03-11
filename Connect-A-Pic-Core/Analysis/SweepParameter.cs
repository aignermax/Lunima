using CAP_Core.Components.Core;

namespace CAP_Core.Analysis
{
    /// <summary>
    /// Identifies a component slider parameter to sweep during sensitivity analysis.
    /// </summary>
    public class SweepParameter
    {
        /// <summary>
        /// The component containing the parameter to sweep.
        /// </summary>
        public Component TargetComponent { get; }

        /// <summary>
        /// The slider index on the target component (0-based).
        /// </summary>
        public int SliderIndex { get; }

        /// <summary>
        /// Human-readable name for this parameter (e.g., "Coupling Ratio").
        /// </summary>
        public string DisplayName { get; }

        /// <summary>
        /// Creates a new sweep parameter targeting a specific component slider.
        /// </summary>
        public SweepParameter(Component targetComponent, int sliderIndex, string displayName)
        {
            TargetComponent = targetComponent ?? throw new ArgumentNullException(nameof(targetComponent));
            DisplayName = displayName ?? throw new ArgumentNullException(nameof(displayName));

            if (sliderIndex < 0)
                throw new ArgumentOutOfRangeException(nameof(sliderIndex), "Slider index must be non-negative.");

            var slider = targetComponent.GetSlider(sliderIndex);
            if (slider == null)
                throw new ArgumentException($"Component '{targetComponent.Identifier}' has no slider at index {sliderIndex}.", nameof(sliderIndex));

            SliderIndex = sliderIndex;
        }

        /// <summary>
        /// Gets the current slider instance from the target component.
        /// </summary>
        public Slider GetSlider()
        {
            return TargetComponent.GetSlider(SliderIndex)!;
        }
    }
}
