using System;
using System.Collections.Generic;

namespace CAP_Core.Routing
{
    /// <summary>
    /// Smooth pose-to-pose router:
    /// straight (optional) -> circular bend -> straight -> circular bend -> straight (optional)
    ///
    /// This replaces the old cardinal/Manhattan special-case logic with a CSC path
    /// (same idea as a Dubins path with fixed minimum bend radius).
    /// </summary>
    public class ManhattanRouter
    {
        private const double Eps = 1e-6;

        private readonly double _bendRadius;
        private readonly double _leadOut;
        private readonly double _leadIn;

        private enum Turn
        {
            Left,
            Right
        }

        private sealed class Candidate
        {
            public Turn FirstTurn { get; set; }
            public Turn SecondTurn { get; set; }

            public (double X, double Y) FirstCenter { get; set; }
            public (double X, double Y) SecondCenter { get; set; }

            public (double X, double Y) FirstTangentPoint { get; set; }
            public (double X, double Y) SecondTangentPoint { get; set; }

            public double StraightLength { get; set; }
            public double StraightAngleDeg { get; set; }

            public double FirstSweepDeg { get; set; }
            public double SecondSweepDeg { get; set; }

            public double TotalLength { get; set; }

            public bool IsReasonable =>
                Math.Abs(FirstSweepDeg) <= 180.0 + 1e-6 &&
                Math.Abs(SecondSweepDeg) <= 180.0 + 1e-6;
        }

        /// <param name="minBendRadius">Minimum bend radius.</param>
        /// <param name="leadOut">
        /// Optional short straight at the start before the first bend.
        /// Set to 0 if you want the bend to start immediately.
        /// </param>
        /// <param name="leadIn">
        /// Optional short straight at the end after the last bend.
        /// Set to 0 if you want the last bend to end directly on the pin.
        /// </param>
        public ManhattanRouter(double minBendRadius, double leadOut = 0.0, double leadIn = 0.0)
        {
            _bendRadius = Math.Max(minBendRadius, 0.0);
            _leadOut = Math.Max(leadOut, 0.0);
            _leadIn = Math.Max(leadIn, 0.0);
        }

        public void Route(
            double startX, double startY, double startAngle,
            double endX, double endY, double endInputAngle,
            RoutedPath path)
        {
            var start = (X: startX, Y: startY);
            var end = (X: endX, Y: endY);

            double startAngleDeg = Normalize360(startAngle);
            double endAngleDeg = Normalize360(endInputAngle);

            // If your destination pin stores the OUTWARD pin direction, not the incoming route
            // direction, then use this instead:
            //
            // endAngleDeg = Normalize360(endInputAngle + 180.0);

            if (_bendRadius <= Eps)
            {
                AddStraight(path, start, end, AngleOf(Sub(end, start)));
                return;
            }

            var adjustedStart = Add(start, Scale(UnitFromAngle(startAngleDeg), _leadOut));
            var adjustedEnd = Sub(end, Scale(UnitFromAngle(endAngleDeg), _leadIn));

            if (_leadOut > Eps)
                AddStraight(path, start, adjustedStart, startAngleDeg);

            if (Distance(adjustedStart, adjustedEnd) <= Eps &&
                AngleDistanceDeg(startAngleDeg, endAngleDeg) <= 1e-4)
            {
                if (_leadIn > Eps)
                    AddStraight(path, adjustedEnd, end, endAngleDeg);

                return;
            }

            if (!TryAddCscRoute(path, adjustedStart, startAngleDeg, adjustedEnd, endAngleDeg, _bendRadius))
            {
                // This should be extremely rare.
                // Fallback only so the function never leaves the path half-built.
                AddStraight(path, adjustedStart, adjustedEnd, AngleOf(Sub(adjustedEnd, adjustedStart)));
            }

            if (_leadIn > Eps)
                AddStraight(path, adjustedEnd, end, endAngleDeg);
        }

        private static bool TryAddCscRoute(
            RoutedPath path,
            (double X, double Y) start,
            double startAngleDeg,
            (double X, double Y) end,
            double endAngleDeg,
            double radius)
        {
            Candidate? bestReasonable = null;
            Candidate? bestAny = null;

            foreach (var candidate in BuildCandidates(start, startAngleDeg, end, endAngleDeg, radius))
            {
                if (bestAny == null || candidate.TotalLength < bestAny.TotalLength)
                    bestAny = candidate;

                if (candidate.IsReasonable &&
                    (bestReasonable == null || candidate.TotalLength < bestReasonable.TotalLength))
                {
                    bestReasonable = candidate;
                }
            }

            var best = bestReasonable ?? bestAny;
            if (best == null)
                return false;

            if (Math.Abs(best.FirstSweepDeg) > Eps)
                AddArc(path, best.FirstCenter, radius, startAngleDeg, best.FirstSweepDeg);

            if (best.StraightLength > Eps)
                AddStraight(path, best.FirstTangentPoint, best.SecondTangentPoint, best.StraightAngleDeg);

            if (Math.Abs(best.SecondSweepDeg) > Eps)
                AddArc(path, best.SecondCenter, radius, best.StraightAngleDeg, best.SecondSweepDeg);

            return true;
        }

        private static IEnumerable<Candidate> BuildCandidates(
            (double X, double Y) start,
            double startAngleDeg,
            (double X, double Y) end,
            double endAngleDeg,
            double radius)
        {
            var turns = new[] { Turn.Left, Turn.Right };

            foreach (var firstTurn in turns)
            {
                foreach (var secondTurn in turns)
                {
                    var c1 = CircleCenter(start, startAngleDeg, firstTurn, radius);
                    var c2 = CircleCenter(end, endAngleDeg, secondTurn, radius);

                    double signedR1 = firstTurn == Turn.Left ? radius : -radius;
                    double signedR2 = secondTurn == Turn.Left ? radius : -radius;

                    var v = Sub(c2, c1);
                    double z = Dot(v, v);
                    if (z <= Eps)
                        continue;

                    double rr = signedR1 - signedR2;
                    double hSq = z - rr * rr;
                    if (hSq < -Eps)
                        continue;

                    hSq = Math.Max(0.0, hSq);
                    double h = Math.Sqrt(hSq);
                    var pv = LeftPerp(v);

                    foreach (double sign in new[] { -1.0, 1.0 })
                    {
                        // Tangents between two circles using signed radii.
                        var normal = Scale(
                            Add(Scale(v, rr), Scale(pv, h * sign)),
                            1.0 / z);

                        var tangentPoint1 = Add(c1, Scale(normal, signedR1));
                        var tangentPoint2 = Add(c2, Scale(normal, signedR2));

                        double straightLength = Distance(tangentPoint1, tangentPoint2);
                        var lineDir = straightLength <= Eps
                            ? TangentDirection(c1, tangentPoint1, firstTurn)
                            : Normalize(Sub(tangentPoint2, tangentPoint1));

                        var exitDirFromFirstArc = TangentDirection(c1, tangentPoint1, firstTurn);
                        var entryDirIntoSecondArc = TangentDirection(c2, tangentPoint2, secondTurn);

                        // Keep only tangents that are actually traversed in the forward direction.
                        if (Dot(exitDirFromFirstArc, lineDir) < 1.0 - 1e-6)
                            continue;

                        if (Dot(entryDirIntoSecondArc, lineDir) < 1.0 - 1e-6)
                            continue;

                        double lineAngleDeg = AngleOf(lineDir);
                        double firstSweepDeg = SignedSweepDeg(startAngleDeg, lineAngleDeg, firstTurn);
                        double secondSweepDeg = SignedSweepDeg(lineAngleDeg, endAngleDeg, secondTurn);

                        double totalLength =
                            radius * DegToRad(Math.Abs(firstSweepDeg)) +
                            straightLength +
                            radius * DegToRad(Math.Abs(secondSweepDeg));

                        yield return new Candidate
                        {
                            FirstTurn = firstTurn,
                            SecondTurn = secondTurn,
                            FirstCenter = c1,
                            SecondCenter = c2,
                            FirstTangentPoint = tangentPoint1,
                            SecondTangentPoint = tangentPoint2,
                            StraightLength = straightLength,
                            StraightAngleDeg = lineAngleDeg,
                            FirstSweepDeg = firstSweepDeg,
                            SecondSweepDeg = secondSweepDeg,
                            TotalLength = totalLength
                        };
                    }
                }
            }
        }

        private static void AddStraight(
            RoutedPath path,
            (double X, double Y) from,
            (double X, double Y) to,
            double angleDeg)
        {
            if (Distance(from, to) <= Eps)
                return;

            path.Segments.Add(new StraightSegment(
                from.X, from.Y,
                to.X, to.Y,
                Normalize360(angleDeg)));
        }

        private static void AddArc(
            RoutedPath path,
            (double X, double Y) center,
            double radius,
            double startAngleDeg,
            double sweepDeg)
        {
            if (Math.Abs(sweepDeg) <= Eps)
                return;

            // Split long arcs into <= 90° pieces because your current pipeline already
            // works well with discrete BendSegments of that size.
            double remaining = sweepDeg;
            double currentStartAngle = Normalize360(startAngleDeg);

            while (Math.Abs(remaining) > Eps)
            {
                double pieceSweep = Clamp(remaining, -90.0, 90.0);
                if (Math.Abs(remaining) <= 90.0 + Eps)
                    pieceSweep = remaining;

                path.Segments.Add(new BendSegment(
                    center.X,
                    center.Y,
                    radius,
                    currentStartAngle,
                    pieceSweep));

                currentStartAngle = Normalize360(currentStartAngle + pieceSweep);
                remaining -= pieceSweep;
            }
        }

        private static (double X, double Y) CircleCenter(
            (double X, double Y) point,
            double headingDeg,
            Turn turn,
            double radius)
        {
            var forward = UnitFromAngle(headingDeg);
            var left = LeftPerp(forward);
            double side = turn == Turn.Left ? 1.0 : -1.0;
            return Add(point, Scale(left, side * radius));
        }

        private static (double X, double Y) TangentDirection(
            (double X, double Y) center,
            (double X, double Y) pointOnCircle,
            Turn turn)
        {
            var radial = Normalize(Sub(pointOnCircle, center));
            return turn == Turn.Left ? LeftPerp(radial) : RightPerp(radial);
        }

        private static double SignedSweepDeg(double fromDeg, double toDeg, Turn turn)
        {
            fromDeg = Normalize360(fromDeg);
            toDeg = Normalize360(toDeg);

            return turn == Turn.Left
                ? PositiveDeltaDeg(fromDeg, toDeg)
                : -PositiveDeltaDeg(toDeg, fromDeg);
        }

        private static double PositiveDeltaDeg(double fromDeg, double toDeg)
        {
            double delta = Normalize360(toDeg) - Normalize360(fromDeg);
            if (delta < 0.0)
                delta += 360.0;
            return delta;
        }

        private static double AngleDistanceDeg(double aDeg, double bDeg)
        {
            double delta = PositiveDeltaDeg(aDeg, bDeg);
            return Math.Min(delta, 360.0 - delta);
        }

        private static double Normalize360(double angleDeg)
        {
            angleDeg %= 360.0;
            if (angleDeg < 0.0)
                angleDeg += 360.0;
            return angleDeg;
        }

        private static double DegToRad(double angleDeg)
            => angleDeg * Math.PI / 180.0;

        private static double AngleOf((double X, double Y) v)
            => Normalize360(Math.Atan2(v.Y, v.X) * 180.0 / Math.PI);

        private static (double X, double Y) UnitFromAngle(double angleDeg)
        {
            double angleRad = DegToRad(angleDeg);
            return (Math.Cos(angleRad), Math.Sin(angleRad));
        }

        private static (double X, double Y) Add((double X, double Y) a, (double X, double Y) b)
            => (a.X + b.X, a.Y + b.Y);

        private static (double X, double Y) Sub((double X, double Y) a, (double X, double Y) b)
            => (a.X - b.X, a.Y - b.Y);

        private static (double X, double Y) Scale((double X, double Y) v, double s)
            => (v.X * s, v.Y * s);

        private static (double X, double Y) Normalize((double X, double Y) v)
        {
            double len = Math.Sqrt(v.X * v.X + v.Y * v.Y);
            if (len <= Eps)
                return (0.0, 0.0);

            return (v.X / len, v.Y / len);
        }

        private static (double X, double Y) LeftPerp((double X, double Y) v)
            => (-v.Y, v.X);

        private static (double X, double Y) RightPerp((double X, double Y) v)
            => (v.Y, -v.X);

        private static double Dot((double X, double Y) a, (double X, double Y) b)
            => a.X * b.X + a.Y * b.Y;

        private static double Distance((double X, double Y) a, (double X, double Y) b)
        {
            double dx = a.X - b.X;
            double dy = a.Y - b.Y;
            return Math.Sqrt(dx * dx + dy * dy);
        }

        private static double Clamp(double value, double min, double max)
            => Math.Max(min, Math.Min(max, value));
    }
}
