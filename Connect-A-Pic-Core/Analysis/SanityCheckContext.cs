using System.Numerics;
using CAP_Core.Components;
using CAP_Core.LightCalculation.Validation;

namespace CAP_Core.Analysis
{
    /// <summary>
    /// Provides all data needed by sanity checks to analyze a simulation result.
    /// </summary>
    public class SanityCheckContext
    {
        /// <summary>
        /// All components placed on the grid.
        /// </summary>
        public IReadOnlyList<Component> Components { get; }

        /// <summary>
        /// All waveguide connections in the design.
        /// </summary>
        public IReadOnlyList<WaveguideConnection> Connections { get; }

        /// <summary>
        /// Light field values at each pin (pin ID to complex amplitude).
        /// Null if no simulation has been run.
        /// </summary>
        public IReadOnlyDictionary<Guid, Complex>? LightField { get; }

        /// <summary>
        /// S-Matrix validation result from the simulation engine.
        /// Null if no validator was configured.
        /// </summary>
        public SMatrixValidationResult? ValidationResult { get; }

        /// <summary>
        /// Creates a new sanity check context.
        /// </summary>
        public SanityCheckContext(
            IReadOnlyList<Component> components,
            IReadOnlyList<WaveguideConnection> connections,
            IReadOnlyDictionary<Guid, Complex>? lightField,
            SMatrixValidationResult? validationResult)
        {
            Components = components;
            Connections = connections;
            LightField = lightField;
            ValidationResult = validationResult;
        }
    }
}
