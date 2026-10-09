using System.Numerics;
using CAP_Core.LightCalculation;
using Shouldly;
using Xunit;

namespace UnitTests.LightCalculation;

/// <summary>
/// The iterative field solve must not resume on the caller's synchronization context
/// (the UI dispatcher in the app) after every iteration: a slowly converging feedback loop
/// needs hundreds of iterations, and a context hop per iteration turned a 500-point
/// spectrum sweep into seconds of idle waiting.
/// </summary>
public class SMatrixSolveContextTests
{
    /// <summary>Round-trip amplitude of the feedback loop: converges, but only after many iterations.</summary>
    private const double LoopAmplitude = 0.9;

    /// <summary>Generous bound for the few awaits outside the iteration loop.</summary>
    private const int MaxCallerContextResumes = 3;

    [Fact]
    public async Task SlowlyConvergingLoop_DoesNotResumeOnTheCallerContextPerIteration()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var matrix = new SMatrix(new List<Guid> { a, b }, new());
        matrix.SetValues(new Dictionary<(Guid, Guid), Complex>
        {
            { (a, b), LoopAmplitude },
            { (b, a), LoopAmplitude },
        });
        var input = MathNet.Numerics.LinearAlgebra.Vector<Complex>.Build.Dense(2);
        input[matrix.PinReference[a]] = Complex.One;

        var context = new CountingSynchronizationContext();
        var previous = SynchronizationContext.Current;
        Task<Dictionary<Guid, Complex>> solve;
        SynchronizationContext.SetSynchronizationContext(context);
        try
        {
            solve = matrix.CalcFieldAtPinsAfterStepsAsync(input, SMatrix.DefaultMaxIterations, new CancellationTokenSource());
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previous);
        }
        var fields = await solve;

        // Geometric series of the loop: a = 1 / (1 - 0.81), b = 0.9 · a.
        fields[a].Magnitude.ShouldBe(1.0 / (1.0 - LoopAmplitude * LoopAmplitude), 1e-6);
        fields[b].Magnitude.ShouldBe(LoopAmplitude / (1.0 - LoopAmplitude * LoopAmplitude), 1e-6);
        context.PostCount.ShouldBeLessThanOrEqualTo(MaxCallerContextResumes,
            "the solve iterates off the caller's context instead of hopping back after every step");
    }

    private sealed class CountingSynchronizationContext : SynchronizationContext
    {
        private int _postCount;

        public int PostCount => Volatile.Read(ref _postCount);

        public override void Post(SendOrPostCallback d, object? state)
        {
            Interlocked.Increment(ref _postCount);
            // Like a UI dispatcher: the callback runs with this context current, so every
            // further await inside it would come back here as well.
            ThreadPool.QueueUserWorkItem(_ =>
            {
                SetSynchronizationContext(this);
                d(state);
            });
        }
    }
}
