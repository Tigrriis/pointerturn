using System;
using System.Collections.Generic;
using System.Linq;
using PointerPath.Core.Drawing;

namespace PointerPath.Core.Geometry
{
    /// <summary>Reversing paths, and turning them into entities to draw.</summary>
    public static class PathOps
    {
        public static Segment Reverse(Segment s) =>
            s is LineSegment l ? new LineSegment(l.Q, l.P) : ReverseArc((ArcSegment)s);

        private static Segment ReverseArc(ArcSegment a) => new ArcSegment(a.Center, a.Radius, a.EndAngle, -a.Sweep);

        public static Path2 Reverse(Path2 p) =>
            new Path2(p.Segments.AsEnumerable().Reverse().Select(Reverse).ToList(), p.Closed);

        // ------------------------------------------------------------------ conversion

        /// <summary>A line, an arc, or a polyline with bulges, as simple as the path allows.</summary>
        public static EntitySpec ToSpec(Path2 p)
        {
            if (!p.Closed && p.Segments.Count == 1)
            {
                Segment s = p.Segments[0];
                if (s is LineSegment l)
                {
                    return new LineSpec { Start = l.P, End = l.Q };
                }

                var a = (ArcSegment)s;
                if (Math.Abs(a.Sweep) < 2 * Math.PI - 1e-9)
                {
                    // Arcs are anticlockwise from start to end angle.
                    double start = a.Sweep > 0 ? a.StartAngle : a.EndAngle;
                    double end = a.Sweep > 0 ? a.EndAngle : a.StartAngle;
                    return new ArcSpec { Center = a.Center, Radius = a.Radius, StartAngleDeg = Deg(start), EndAngleDeg = Deg(end) };
                }
            }

            return ToPolyline(p);
        }

        public static PolylineSpec ToPolyline(Path2 p)
        {
            var points = new List<Point2>();
            var bulges = new List<double>();
            foreach (Segment s in p.Segments)
            {
                if (s is ArcSegment a && Math.Abs(a.Sweep) > Math.PI + 1e-9)
                {
                    // Bulges can't describe more than a half circle well: split big arcs.
                    double half = a.Sweep / 2;
                    foreach (var part in new[] { new ArcSegment(a.Center, a.Radius, a.StartAngle, half), new ArcSegment(a.Center, a.Radius, a.StartAngle + half, half) })
                    {
                        points.Add(part.Start);
                        bulges.Add(Math.Tan(part.Sweep / 4));
                    }

                    continue;
                }

                points.Add(s.Start);
                bulges.Add(s is ArcSegment arc ? Math.Tan(arc.Sweep / 4) : 0);
            }

            if (!p.Closed)
            {
                points.Add(p.End);
                bulges.Add(0);
            }

            bool anyArc = bulges.Any(b => Math.Abs(b) > 1e-12);
            return new PolylineSpec { Points = points, Bulges = anyArc ? bulges : null, Closed = p.Closed };
        }

        private static double Deg(double rad)
        {
            double d = rad * 180 / Math.PI % 360;
            return d < 0 ? d + 360 : d;
        }
    }
}
