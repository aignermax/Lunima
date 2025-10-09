namespace CAP_Core.Components
{
    public class PhysicalPin
    {
        public string Name { get; set; }
        public double OffsetXMicrometers { get; set; }  // µm vom Component-Origin
        public double OffsetYMicrometers { get; set; }  // µm vom Component-Origin
        public double AngleDegrees { get; set; }        // Ausgangsrichtung
        public Guid PinId { get; set; }
        public Component ParentComponent { get; set; }

        // Absolute Position berechnen
        public (double x, double y) GetAbsolutePosition()
        {
            return (
                ParentComponent.PhysicalX + OffsetXMicrometers,
                ParentComponent.PhysicalY + OffsetYMicrometers
            );
        }
    }
}
