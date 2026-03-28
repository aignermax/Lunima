namespace CAP_Core.Components.Core
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
        /// Gets the absolute Nazca-coordinate position of this pin.
        /// Accounts for Y-flip, NazcaOriginOffset, and component rotation transformations.
        /// Used for GDS/Nazca export where waveguide coordinates must match stub pin positions.
        /// Fix for Issue #329: NazcaOriginOffset compensation.
        /// Fix for Issue #338: Rotation transformation applied to local pin coordinates.
        /// </summary>
        public (double x, double y) GetAbsoluteNazcaPosition()
        {
            // Component Nazca placement
            var (originOffsetX, originOffsetY) = CalculateOriginOffset(ParentComponent);
            double nazcaCompX = ParentComponent.PhysicalX + originOffsetX;
            double nazcaCompY = -(ParentComponent.PhysicalY + originOffsetY);

            // Local pin Nazca coordinates in unrotated component space
            double localPinNazcaX = OffsetXMicrometers;
            double localPinNazcaY = ParentComponent.HeightMicrometers - OffsetYMicrometers;

            // Apply component rotation to local pin coordinates
            // Nazca places cells with .put(x, y, -RotationDegrees), so pin world positions
            // must use the same negated rotation to match stub pin locations.
            double rotRad = -ParentComponent.RotationDegrees * Math.PI / 180.0;
            double rotatedPinX = localPinNazcaX * Math.Cos(rotRad) - localPinNazcaY * Math.Sin(rotRad);
            double rotatedPinY = localPinNazcaX * Math.Sin(rotRad) + localPinNazcaY * Math.Cos(rotRad);

            return (nazcaCompX + rotatedPinX, nazcaCompY + rotatedPinY);
        }

        /// <summary>
        /// Calculates the Nazca origin offset for a component (same logic as SimpleNazcaExporter).
        /// </summary>
        private static (double OffsetX, double OffsetY) CalculateOriginOffset(Component comp)
        {
            var funcName = comp.NazcaFunctionName;

            // For PDK components, use NazcaOriginOffset with rotation
            if (!string.IsNullOrEmpty(funcName) &&
                (funcName.StartsWith("ebeam_", StringComparison.OrdinalIgnoreCase) ||
                 funcName.StartsWith("demo_pdk.", StringComparison.OrdinalIgnoreCase)))
            {
                double rotRad = comp.RotationDegrees * Math.PI / 180.0;
                double offsetX = comp.NazcaOriginOffsetX * Math.Cos(rotRad) - comp.NazcaOriginOffsetY * Math.Sin(rotRad);
                double offsetY = comp.NazcaOriginOffsetX * Math.Sin(rotRad) + comp.NazcaOriginOffsetY * Math.Cos(rotRad);
                return (offsetX, offsetY);
            }

            // Fallback for legacy components
            return (0, comp.HeightMicrometers);
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
