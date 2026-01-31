namespace CAP_Core.Components
{
    /// <summary>
    /// Represents a physical optical port on a component with µm coordinates.
    /// Used for direct waveguide connections (non-grid mode) and Nazca export.
    /// </summary>
    public class PhysicalPin : ICloneable
    {
        public string Name { get; set; }
        public double OffsetXMicrometers { get; set; }
        public double OffsetYMicrometers { get; set; }
        public double AngleDegrees { get; set; }
        public Guid PinId { get; set; } = Guid.NewGuid();
        public Component ParentComponent { get; set; }

        /// <summary>
        /// Reference to the logical Pin for S-Matrix simulation integration.
        /// When set, waveguide connections through this physical pin will
        /// use the logical pin's IDInFlow/IDOutFlow for light propagation.
        /// </summary>
        public Pin LogicalPin { get; set; }

        public (double x, double y) GetAbsolutePosition()
        {
            return (
                ParentComponent.PhysicalX + OffsetXMicrometers,
                ParentComponent.PhysicalY + OffsetYMicrometers
            );
        }

        /// <summary>
        /// Gets the absolute angle of the pin in world-space.
        /// Pin angles are stored relative to the component's local coordinate system.
        /// This method adds the component's rotation to get the world-space angle.
        /// </summary>
        public double GetAbsoluteAngle()
        {
            double absoluteAngle = AngleDegrees + ParentComponent.RotationDegrees;
            // Normalize to 0-360 range
            while (absoluteAngle < 0) absoluteAngle += 360;
            while (absoluteAngle >= 360) absoluteAngle -= 360;
            return absoluteAngle;
        }

        public object Clone()
        {
            return new PhysicalPin
            {
                Name = Name,
                OffsetXMicrometers = OffsetXMicrometers,
                OffsetYMicrometers = OffsetYMicrometers,
                AngleDegrees = AngleDegrees,
                PinId = Guid.NewGuid(),
                // ParentComponent and LogicalPin are set after cloning by the Component
            };
        }
    }
}
