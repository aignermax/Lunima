using Xunit;
using Shouldly;
using CAP_Core.Components.Parametric;

namespace UnitTests.Components.Parametric
{
    public class ParameterDefinitionTests
    {
        [Fact]
        public void Constructor_ValidInput_CreatesInstance()
        {
            var param = new ParameterDefinition("coupling_ratio", 0.5, 0, 1, "Coupling");

            param.Name.ShouldBe("coupling_ratio");
            param.DefaultValue.ShouldBe(0.5);
            param.MinValue.ShouldBe(0);
            param.MaxValue.ShouldBe(1);
            param.Label.ShouldBe("Coupling");
        }

        [Fact]
        public void Constructor_NoLabel_UsesNameAsLabel()
        {
            var param = new ParameterDefinition("phase_shift", 0, 0, 360);
            param.Label.ShouldBe("phase_shift");
        }

        [Fact]
        public void Constructor_EmptyName_ThrowsException()
        {
            Should.Throw<ArgumentException>(
                () => new ParameterDefinition("", 0.5, 0, 1));
        }

        [Fact]
        public void Constructor_MinGreaterThanMax_ThrowsException()
        {
            Should.Throw<ArgumentException>(
                () => new ParameterDefinition("x", 0.5, 1.0, 0.0));
        }

        [Fact]
        public void Constructor_DefaultOutsideRange_ThrowsException()
        {
            Should.Throw<ArgumentOutOfRangeException>(
                () => new ParameterDefinition("x", 2.0, 0, 1));
        }
    }
}
