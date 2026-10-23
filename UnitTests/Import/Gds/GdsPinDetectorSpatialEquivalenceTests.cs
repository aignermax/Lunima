using CAP_DataAccess.Import.Gds;
using Shouldly;
using static UnitTests.Import.Gds.GdsPinDetectorFixture;

namespace UnitTests.Import.Gds;

/// <summary>Spatial-grid output matches the sequential reference for seeded geometry and labels.</summary>
public class GdsPinDetectorSpatialEquivalenceTests
{
    [Fact]
    public void Detect_SeededLabelField_IdenticalToBruteForceReference()
    {
        // ~110 labels over ~700 waveguide/metal segments in engineered spots
        // (exact touches, ordinal ties between coincident faces, the inclusive
        // tolerance boundary, near-misses, interior-only containment, layer
        // mixing, degenerate vertices) plus dense unstructured clusters. The
        // spatial grid is a candidate pre-filter only, so the detected pins —
        // names, positions, angles, widths, kinds, and their ORDER — must be
        // identical to the sequential reference scan.
        var box = new GdsBoundingBox(0, 0, 300, 240);
        var random = new Random(20260806);
        var elements = new List<GdsElement>();
        int labelCount = 0;

        void AddLabel(string name, double x, double y)
        {
            elements.Add(Label(1, 10, name, x, y));
            labelCount++;
        }

        GdsPolygon Rect(int layer, int dataType, double x0, double y0, double x1, double y1) =>
            Poly(layer, dataType, (x0, y0), (x1, y0), (x1, y1), (x0, y1), (x0, y0));

        // Spot pitch 20 µm ≫ the 1 µm touch tolerance: scenarios never interact.
        for (int n = 0; n < 100; n++)
        {
            double sx = 15 + (n % 10 * 20.0);
            double sy = 15 + (n / 10 * 20.0);
            switch (n % 10)
            {
                case 0: // label exactly on a stub end face, random orientation
                    elements.Add(random.Next(4) switch
                    {
                        0 => Rect(1, 0, sx - 4, sy - 0.25, sx, sy + 0.25),
                        1 => Rect(1, 0, sx, sy - 0.25, sx + 4, sy + 0.25),
                        2 => Rect(1, 0, sx - 0.25, sy - 4, sx + 0.25, sy),
                        _ => Rect(1, 0, sx - 0.25, sy, sx + 0.25, sy + 4),
                    });
                    AddLabel($"s{n}", sx, sy);
                    break;
                case 1: // two coincident faces, opposite normals — ordinal tie, first polygon wins
                    elements.Add(Rect(1, 0, sx - 3, sy - 0.25, sx, sy + 0.25));
                    elements.Add(Rect(1, 0, sx, sy - 0.25, sx + 3, sy + 0.25));
                    AddLabel($"tie{n}", sx, sy);
                    break;
                case 2: // anchor at EXACTLY the tolerance distance — inclusive boundary
                    elements.Add(Rect(1, 0, sx - 4, sy - 1, sx - 1, sy + 1));
                    AddLabel($"b{n}", sx, sy);
                    break;
                case 3: // just past the tolerance — bbox-edge fallback, name decides the kind
                    elements.Add(Rect(1, 0, sx - 4, sy - 0.5, sx - 1.01 - (0.3 * random.NextDouble()), sy + 0.5));
                    AddLabel(random.Next(2) == 0 ? $"anode_{n}" : $"o{n}", sx, sy);
                    break;
                case 4: // interior-only metal contact: pad center, outline far beyond tolerance
                    elements.Add(Rect(11, 0, sx - 5, sy - 5, sx + 5, sy + 5));
                    AddLabel($"p{n}", sx + random.NextDouble() - 0.5, sy + random.NextDouble() - 0.5);
                    break;
                case 5: // interior of a waveguide slab with an electrical NAME — geometry beats the name
                    elements.Add(Rect(1, 0, sx - 5, sy - 5, sx + 5, sy + 5));
                    AddLabel($"anode{n}", sx + random.NextDouble() - 0.5, sy + random.NextDouble() - 0.5);
                    break;
                case 6: // metal over waveguide, coincident faces: earliest polygon steers, metal kind wins
                    elements.Add(Rect(1, 0, sx - 3, sy - 0.25, sx, sy + 0.25));
                    elements.Add(Rect(11, 0, sx - 3, sy - 0.1, sx, sy + 0.1));
                    AddLabel($"m{n}", sx, sy);
                    break;
                case 7: // diagonal hypotenuse face — non-cardinal segment normal
                    elements.Add(Poly(1, 0, (sx - 2, sy - 2), (sx + 2, sy - 2), (sx - 2, sy + 2), (sx - 2, sy - 2)));
                    AddLabel($"d{n}", sx + 0.05, sy + 0.05);
                    break;
                case 8: // strictly closer LATER polygon must beat the earlier one — distance before ordinal
                    elements.Add(Rect(1, 0, sx - 4, sy - 0.5, sx - 0.5, sy + 0.5));
                    elements.Add(Rect(1, 0, sx + 0.3, sy - 0.5, sx + 4, sy + 0.5));
                    AddLabel($"near{n}", sx, sy);
                    break;
                default: // degenerate junk: duplicated vertex, unclosed outline, single point, ignored layer
                    elements.Add(Poly(1, 0,
                        (sx, sy - 0.5), (sx, sy - 0.5), (sx, sy + 0.5), (sx - 2, sy + 0.5), (sx - 2, sy - 0.5)));
                    elements.Add(Poly(1, 0, (sx + 3, sy)));
                    elements.Add(Rect(2, 0, sx - 1, sy - 1, sx + 1, sy + 1));
                    AddLabel($"g{n}", sx + 0.2, sy);
                    break;
            }
        }

        // Dense unstructured clusters (random rectangles of every layer class,
        // labels at random offsets around the tolerance window): no per-cluster
        // guarantees — the sequential-reference equality is the arbiter.
        string[] namePool = ["anode", "o1", "elec", "in", "pad", "wg"];
        for (int c = 0; c < 3; c++)
        {
            double cx = 250, cy = 40 + (c * 80.0);
            for (int r = 0; r < 10; r++)
            {
                (int layer, int dataType) = random.Next(3) switch { 0 => (1, 0), 1 => (11, 0), _ => (2, 0) };
                double x0 = cx + ((random.NextDouble() - 0.5) * 8);
                double y0 = cy + ((random.NextDouble() - 0.5) * 8);
                elements.Add(Rect(layer, dataType,
                    x0, y0, x0 + 0.5 + (2.5 * random.NextDouble()), y0 + 0.5 + (2.5 * random.NextDouble())));
            }
            for (int l = 0; l < 6; l++)
            {
                AddLabel($"{namePool[l]}_{c}{l}",
                    cx + ((random.NextDouble() - 0.5) * 10), cy + ((random.NextDouble() - 0.5) * 10));
            }
        }

        // Bounding-box edge touches: one heuristic pin survives, one is covered
        // by a label (which itself takes the face normal).
        elements.Add(Rect(1, 0, 0, 100, 3, 101));
        elements.Add(Rect(1, 0, 297, 50, 300, 51));
        AddLabel("cov", 0, 100.5);

        var cell = Cell(elements.ToArray());
        var expected = SequentialReferenceDetector.Detect(cell, box);
        var actual = GdsPinDetector.Detect(cell, box);

        expected.Count(p => p.Source == DetectedPinSource.Label).ShouldBe(labelCount,
            "every port-layer label must yield a pin");
        expected.ShouldContain(p => p.IsElectrical == true);
        expected.ShouldContain(p => p.IsElectrical == null);
        expected.ShouldContain(p => p.Source == DetectedPinSource.EdgeHeuristic);
        actual.ShouldBe(expected, "same pins, same order, same values as the sequential reference scan");
    }
}
