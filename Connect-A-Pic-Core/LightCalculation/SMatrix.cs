using System.Numerics;
using MathNet.Numerics.LinearAlgebra;
using System.Linq.Dynamic.Core;
using CAP_Core.Components.Core;
using CAP_Core.Components.FormulaReading;

namespace CAP_Core.LightCalculation
{
    public class SMatrix
    {
        public Matrix<Complex> SMat { get; private set; } // the SMat works like SMat[PinNROutflow, PinNRInflow] --> so opposite from what one might expect
        public readonly Dictionary<Guid, int> PinReference; // all PinIDs inside of the matrix. the int is the index of the row/column in the SMat.. and also of the inputVector.
        public Dictionary<Guid, double> SliderReference { get; internal set; }
        private readonly Dictionary<int, Guid> ReversePinReference; // sometimes we want to find the GUID and only have the ID
        private readonly int size;
        public const int MaxToStringPinGuidSize = 6;
        public Dictionary<(Guid PinIdStart, Guid PinIdEnd), ConnectionFunction> NonLinearConnections { get; set; }

        /// <summary>
        /// Optional rebuild factory used by <c>Component.Clone()</c> when the
        /// S-matrix was produced by a parametric PDK template. Rebuilding via
        /// this factory gives the clone its own <c>ParametricSMatrix</c>
        /// instance (isolated parameter state) and avoids trying to re-parse
        /// the parametric formula through <c>MathExpressionReader</c>, which
        /// would otherwise throw because the raw-formula string is not valid
        /// NCalc syntax. <c>null</c> for non-parametric matrices.
        /// Convention: set only by the PDK template converter at construction
        /// and carried forward by <c>Component.Clone</c>. Do not mutate from
        /// elsewhere — the "set once, propagate on clone" invariant would
        /// break. The setter is <c>public</c> because the template converter
        /// lives in a separate assembly (<c>CAP.Avalonia</c>).
        /// </summary>
        public Func<List<Pin>, List<Slider>, SMatrix>? ParametricRebuild { get; set; }

        public SMatrix(List<Guid> allPinsInGrid, List<(Guid sliderID, double value)> AllSliders)
        {
            if (allPinsInGrid != null && allPinsInGrid.Count > 0)
            {
                size = allPinsInGrid.Count;
            }
            else
            {
                size = 0;
            }

            SMat = Matrix<Complex>.Build.Sparse(size, size);
            // initialize PinReferences
            PinReference = new();
            ReversePinReference = new();
            int i = 0;
            foreach (var pin in allPinsInGrid)
            {
                PinReference.Add(pin, i);
                ReversePinReference.Add(i, pin);
                i++;
            }
            NonLinearConnections = new();
            SliderReference = new();
            foreach (var slider in AllSliders)
            {
                SliderReference.Add(slider.sliderID, slider.value);
            }
        }

        public void SetValues(Dictionary<(Guid PinIdInflow, Guid PinIdOutflow), Complex> transfers, bool reset = false)
        {
            if (transfers == null || PinReference == null)
            {
                return;
            }

            if (reset)
            {
                SMat = Matrix<Complex>.Build.Sparse(size, size);
            }

            foreach (var relation in transfers.Keys)
            {
                if (PinReference.ContainsKey(relation.PinIdInflow) && PinReference.ContainsKey(relation.PinIdOutflow))
                {
                    int indexInflow = PinReference[relation.PinIdInflow];
                    int indexOutflow = PinReference[relation.PinIdOutflow];
                    SMat[indexOutflow, indexInflow] = transfers[relation];
                }
            }
        }

        public Dictionary<(Guid PinIdStart, Guid PinIdEnd), Complex> GetNonNullValues()
        {
            var transfers = new Dictionary<(Guid inflow, Guid outflow), Complex>();
            for (int iOut = 0; iOut < size; iOut++)
            {
                for (int iIn = 0; iIn < size; iIn++)
                {
                    if (SMat[iOut, iIn] == Complex.Zero) continue;
                    transfers[(ReversePinReference[iIn], ReversePinReference[iOut])] = SMat[iOut, iIn];
                }
            }
            return transfers;
        }

        public static SMatrix CreateSystemSMatrix(List<SMatrix> matrices)
        {
            var allPinIDs = matrices.SelectMany(x => x.PinReference.Keys).Distinct().ToList();
            var allSliderIDs = matrices.SelectMany(x => x.SliderReference.Select(k => (k.Key, k.Value))).ToList(); // convert SliderReference to the required tuple
            SMatrix sysMat = new(allPinIDs, allSliderIDs);

            foreach (SMatrix matrix in matrices)
            {
                var transfers = matrix.GetNonNullValues();
                var nonLinearTransfers = matrix.NonLinearConnections;
                sysMat.SetValues(transfers);
                // also copy the nonlinear functions
                foreach (var key in nonLinearTransfers.Keys)
                {
                    sysMat.NonLinearConnections.Add(key, nonLinearTransfers[key]);
                }
            }
            return sysMat;
        }

        // n is the number of time steps to move forward "steps=3" would return the light propagation after 3 steps.
        public async Task<Dictionary<Guid, Complex>> CalcFieldAtPinsAfterStepsAsync(MathNet.Numerics.LinearAlgebra.Vector<Complex> inputVector, int maxSteps, CancellationTokenSource cancellation)
        {
            if (maxSteps < 1) return new Dictionary<Guid, Complex>();

            // update the SMat using the non linear connections - including those who are not depending on the input vector (the PIN1 etc)
            await RecomputeSMatNonLinearPartsAsync(inputVector, SkipOuterLoopFunctions: false);
            try
            {

                var inputAfterSteps = SMat * inputVector + inputVector;
                for (int i = 1; i < maxSteps; i++)
                {
                    cancellation.Token.ThrowIfCancellationRequested();
                    await Task.Run(async () =>
                    {
                        var oldInputAfterSteps = inputAfterSteps;
                        // recalculating non linear values because the inputVector has changed and could now change the connections like activate a logic gate for example.
                        await RecomputeSMatNonLinearPartsAsync(inputAfterSteps, SkipOuterLoopFunctions: true);
                        // multiplying the adjusted matrix and also adding the initial inputVector again because there is more light incoming
                        inputAfterSteps = SMat * inputAfterSteps + inputVector;
                        if (oldInputAfterSteps.Equals(inputAfterSteps))
                            maxSteps = 0;
                    }, cancellation.Token);
                }

                return ConvertToDictWithGuids(inputAfterSteps);
            }
            catch (OperationCanceledException)
            {
                // Cancellation is expected during user-triggered stop; return
                // empty so the caller can handle it without noise.
                return new Dictionary<Guid, Complex>();
            }
            // Any other exception (formula evaluation error, matrix singular,
            // division by zero in a parametric connection, ...) previously
            // got swallowed here and produced a blank simulation with no
            // indication of what went wrong. Let it propagate so the UI
            // error-handling layer can log and surface it.
        }

        private List<object> GetWeightParameters(IEnumerable<Guid> parameterGuids, MathNet.Numerics.LinearAlgebra.Vector<Complex> inputVector)
        {
            List<object> usedParameterValues = new();
            foreach (var paramGuid in parameterGuids)
            {
                // first check if the parameterGuid is in the pin-Dict
                if (PinReference.TryGetValue(paramGuid, out int pinNumber))
                {
                    usedParameterValues.Add(inputVector[pinNumber]);
                }
                // check if parameterGuid is in the slider Dict
                else if (SliderReference.TryGetValue(paramGuid, out double sliderPosition))
                {
                    usedParameterValues.Add(sliderPosition);
                }
            }

            return usedParameterValues;
        }
        private async Task RecomputeSMatNonLinearPartsAsync(MathNet.Numerics.LinearAlgebra.Vector<Complex> inputVector, bool SkipOuterLoopFunctions = true)
        {
            foreach (var connection in NonLinearConnections)
            {
                if (connection.Value.IsInnerLoopFunction == false && SkipOuterLoopFunctions == true)// some functions 
                    continue;
                var indexStart = PinReference[connection.Key.PinIdStart];
                var indexEnd = PinReference[connection.Key.PinIdEnd];
                var weightParameters = GetWeightParameters(connection.Value.UsedParameterGuids, inputVector);
                var calculatedWeight = connection.Value.CalcConnectionWeightAsync(weightParameters);
                SMat[indexEnd, indexStart] = calculatedWeight;
            }
        }

        private Dictionary<Guid, Complex> ConvertToDictWithGuids(MathNet.Numerics.LinearAlgebra.Vector<Complex> lightPropagationVector)
        {
            var GuidsAndLightValues = new Dictionary<Guid, Complex>();
            for (int i = 0; i < lightPropagationVector.Count; i++)
            {
                GuidsAndLightValues.Add(ReversePinReference[i], lightPropagationVector[i]);
            }
            return GuidsAndLightValues;
        }

    }
}
