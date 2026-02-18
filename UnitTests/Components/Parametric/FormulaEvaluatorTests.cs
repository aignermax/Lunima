using Xunit;
using Shouldly;
using CAP_Core.Components.Parametric;

namespace UnitTests.Components.Parametric
{
    public class FormulaEvaluatorTests
    {
        private readonly FormulaEvaluator _evaluator = new();

        [Fact]
        public void Evaluate_ConstantValue_ReturnsConstant()
        {
            var result = _evaluator.Evaluate("0.707", new Dictionary<string, double>());
            result.ShouldBe(0.707, 1e-10);
        }

        [Fact]
        public void Evaluate_SimpleParameter_ReturnsParameterValue()
        {
            var parameters = new Dictionary<string, double> { { "x", 42.0 } };
            var result = _evaluator.Evaluate("x", parameters);
            result.ShouldBe(42.0, 1e-10);
        }

        [Fact]
        public void Evaluate_SqrtFormula_ReturnsCorrectResult()
        {
            var parameters = new Dictionary<string, double>
            {
                { "coupling_ratio", 0.5 }
            };
            var result = _evaluator.Evaluate("Sqrt(coupling_ratio)", parameters);
            result.ShouldBe(Math.Sqrt(0.5), 1e-10);
        }

        [Fact]
        public void Evaluate_ComplementSqrt_ReturnsCorrectResult()
        {
            var parameters = new Dictionary<string, double>
            {
                { "coupling_ratio", 0.5 }
            };
            var result = _evaluator.Evaluate("Sqrt(1 - coupling_ratio)", parameters);
            result.ShouldBe(Math.Sqrt(0.5), 1e-10);
        }

        [Fact]
        public void Evaluate_PiConstant_IsAvailable()
        {
            var result = _evaluator.Evaluate("pi", new Dictionary<string, double>());
            result.ShouldBe(Math.PI, 1e-10);
        }

        [Fact]
        public void Evaluate_TrigFunction_ReturnsCorrectResult()
        {
            var parameters = new Dictionary<string, double>
            {
                { "phase_shift", 90.0 }
            };
            var result = _evaluator.Evaluate("Sin(phase_shift * pi / 180)", parameters);
            result.ShouldBe(1.0, 1e-10);
        }

        [Fact]
        public void Evaluate_MultipleParameters_WorksCorrectly()
        {
            var parameters = new Dictionary<string, double>
            {
                { "a", 3.0 },
                { "b", 4.0 }
            };
            var result = _evaluator.Evaluate("Sqrt(a * a + b * b)", parameters);
            result.ShouldBe(5.0, 1e-10);
        }

        [Fact]
        public void Evaluate_UnknownParameter_ThrowsException()
        {
            var parameters = new Dictionary<string, double>();
            Should.Throw<InvalidOperationException>(
                () => _evaluator.Evaluate("unknown_var", parameters));
        }

        [Fact]
        public void Evaluate_EmptyFormula_ThrowsArgumentException()
        {
            Should.Throw<ArgumentException>(
                () => _evaluator.Evaluate("", new Dictionary<string, double>()));
        }

        [Fact]
        public void TryValidate_ValidFormula_ReturnsTrue()
        {
            var paramNames = new HashSet<string> { "coupling_ratio" };
            var isValid = _evaluator.TryValidate(
                "Sqrt(coupling_ratio)", paramNames, out string? error);

            isValid.ShouldBeTrue();
            error.ShouldBeNull();
        }

        [Fact]
        public void TryValidate_UnknownParameter_ReturnsFalse()
        {
            var paramNames = new HashSet<string> { "coupling_ratio" };
            var isValid = _evaluator.TryValidate(
                "Sqrt(unknown)", paramNames, out string? error);

            isValid.ShouldBeFalse();
            error.ShouldNotBeNull();
        }

        [Fact]
        public void TryValidate_EmptyFormula_ReturnsFalse()
        {
            var isValid = _evaluator.TryValidate(
                "", new HashSet<string>(), out string? error);

            isValid.ShouldBeFalse();
            error.ShouldNotBeNull();
        }

        [Fact]
        public void TryValidate_ConstantFormula_ReturnsTrue()
        {
            var isValid = _evaluator.TryValidate(
                "0.707", new HashSet<string>(), out string? error);

            isValid.ShouldBeTrue();
            error.ShouldBeNull();
        }
    }
}
