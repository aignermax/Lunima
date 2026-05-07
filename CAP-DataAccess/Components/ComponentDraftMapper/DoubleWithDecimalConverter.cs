using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CAP_DataAccess.Components.ComponentDraftMapper
{
    /// <summary>
    /// Custom JSON converter for <see cref="double"/> values that always emits at
    /// least one decimal place for whole-number values (e.g. <c>6.0</c> instead
    /// of <c>6</c>). Non-integer values are written with their natural precision.
    /// </summary>
    /// <remarks>
    /// Without this converter <c>System.Text.Json</c> serialises <c>6.0</c> as
    /// <c>6</c>, which is semantically identical but differs textually from the
    /// hand-authored PDK JSON files that use the <c>6.0</c> form. That mismatch
    /// causes 200+ line diffs on every save even when only one value was edited
    /// (issue #518).
    /// </remarks>
    public sealed class DoubleWithDecimalConverter : JsonConverter<double>
    {
        /// <inheritdoc/>
        public override double Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            => reader.GetDouble();

        /// <inheritdoc/>
        public override void Write(Utf8JsonWriter writer, double value, JsonSerializerOptions options)
        {
            if (double.IsInteger(value) && !double.IsInfinity(value))
            {
                // Emit "6.0" so the output is round-trip stable with hand-authored PDK files.
                writer.WriteRawValue(value.ToString("F1", CultureInfo.InvariantCulture));
            }
            else
            {
                writer.WriteNumberValue(value);
            }
        }
    }
}
