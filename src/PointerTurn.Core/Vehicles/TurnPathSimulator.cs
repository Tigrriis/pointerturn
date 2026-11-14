using System;
using System.Collections.Generic;
using System.Linq;
using PointerTurn.Core.Drawing;
using PointerTurn.Core.Geometry;

namespace PointerTurn.Core.Vehicles
{
    /// <summary>What to simulate: a vehicle, the path one of its points follows, and how.</summary>
    public sealed class TurnPathRequest
    {
        public DesignVehicle Vehicle { get; set; } = DesignVehicle.B99;

        /// <summary>The path the reference point follows, in world coordinates.</summary>
        public Path2 Path { get; set; } = null!;

        /// <summary>The point of the vehicle (vehicle frame) that follows the path.</summary>
        public VehiclePoint Reference { get; set; }

        /// <summary>
        /// The vehicle travels backwards along the path. A vehicle with trailers is then steered
        /// along it by its rear axle, whatever <see cref="Reference"/> says, and finishes with the
        /// combination in line at the end of the path.
        /// </summary>
        public bool Reverse { get; set; }

        /// <summary>Heading at the start, radians. Null: the path's start tangent (plus 180 degrees when reversing).</summary>
        public double? InitialHeading { get; set; }

        /// <summary>
        /// Which way each trailer faces at the start (radians), front to back. Null: straight behind
        /// the vehicle, or, reversing, where reversing along the path needs them (see
        /// <see cref="TurnPathSimulator.TrailerHeadingsToReverse"/>). A manoeuvre drawn leg by leg
        /// passes on where the last leg left them.
        /// </summary>
        public IReadOnlyList<double>? InitialTrailerHeadings { get; set; }

        /// <summary>
        /// The tightest the vehicle may be steered while reversing with trailers, as the radius of
        /// its outer front wheel (m). Null: the vehicle's minimum.
        /// </summary>
        public double? TurnRadius { get; set; }

        /// <summary>Distance the reference point moves per step (m).</summary>
        public double Step { get; set; } = 0.02;

        /// <summary>Clearance added on the inside of the turn. Null: the vehicle's.</summary>
        public double? ClearanceInside { get; set; }

        /// <summary>Clearance added on the outside of the turn. Null: the vehicle's.</summary>
        public double? ClearanceOutside { get; set; }

        /// <summary>Points closer than this are merged, and the output polylines are simplified to this (m).</summary>
        public double Tolerance { get; set; } = 0.001;
    }

    /// <summary>One trailer through a manoeuvre: where its axle group was, and which way it faced, at every pose of the vehicle.</summary>
    public sealed class TrailerTrack
    {
        public TrailerTrack(TrailerUnit unit)
        {
            Unit = unit;
            Body = unit.Body();
        }

        public TrailerUnit Unit { get; }

        /// <summary>The trailer as a body: origin at the centre of its axle group, its coupling at (wheelbase, 0).</summary>
        public DesignVehicle Body { get; }

        public List<VehiclePose> Poses { get; } = new List<VehiclePose>();

        public List<double> Curvatures { get; } = new List<double>();
    }

    /// <summary>The simulated manoeuvre: poses, swept path and clearance envelopes, wheel paths, checks.</summary>
    public sealed class TurnPathResult
    {
        public DesignVehicle Vehicle { get; set; } = null!;
        public Path2 Path { get; set; } = null!;
        public VehiclePoint Reference { get; set; }
        public bool Reverse { get; set; }

        /// <summary>One pose per step, from the start of the path to its end.</summary>
        public List<VehiclePose> Poses { get; } = new List<VehiclePose>();

        /// <summary>Distance of the reference point along the path at each pose.</summary>
        public List<double> Chainages { get; } = new List<double>();

        /// <summary>Curvature of the rear axle's path at each pose (positive = turning left).</summary>
        public List<double> Curvatures { get; } = new List<double>();

        /// <summary>Swept path of the body: the envelope on the vehicle's left and right.</summary>
        public List<Point2> LeftBody { get; set; } = new List<Point2>();

        public List<Point2> RightBody { get; set; } = new List<Point2>();

        /// <summary>The swept path with the clearances added.</summary>
        public List<Point2> LeftClearance { get; set; } = new List<Point2>();

        public List<Point2> RightClearance { get; set; } = new List<Point2>();

        /// <summary>Wheel centre paths: front-left, front-right, rear-left, rear-right.</summary>
        public List<List<Point2>> WheelPaths { get; } = new List<List<Point2>>();

        public double ClearanceLeft { get; set; }

        public double ClearanceRight { get; set; }

        /// <summary>Net change of heading over the manoeuvre, degrees (positive = left).</summary>
        public double NetTurnDeg { get; set; }

        /// <summary>The tightest turn in the manoeuvre, as the outer front wheel's radius (m). Infinity if straight.</summary>
        public double TightestRadius { get; set; } = double.PositiveInfinity;

        /// <summary>Largest steering angle of the front axle (bicycle model), degrees.</summary>
        public double MaxSteerAngleDeg { get; set; }

        /// <summary>True when the manoeuvre never needs a tighter turn than the vehicle's minimum (within 1 mm).</summary>
        public bool WithinMinimumRadius => TightestRadius >= Vehicle.MinTurnRadius - 0.001;

        public List<string> Warnings { get; } = new List<string>();

        /// <summary>The trailers, front to back. Empty for a rigid vehicle.</summary>
        public List<TrailerTrack> Trailers { get; } = new List<TrailerTrack>();

        /// <summary>The sharpest angle between two coupled units during the manoeuvre, degrees. 0 for a rigid vehicle.</summary>
        public double MaxArticulationDeg { get; set; }

        /// <summary>
        /// Reversing with trailers: the furthest the vehicle's rear axle left the path to steer the
        /// trailers (m). 0 otherwise.
        /// </summary>
        public double MaxPathDeviation { get; set; }

        /// <summary>
        /// Reversing with trailers: how far off its line the last trailer's axle group finishes (m).
        /// 0 when the combination finishes where the path ends.
        /// </summary>
        public double EndOffset { get; set; }

        /// <summary>Distance the vehicle's reference point travelled (m): the path's length, unless the manoeuvre stopped short.</summary>
        public double Travelled => Chainages.Count > 0 ? Chainages[Chainages.Count - 1] : 0;

        /// <summary>Which way each trailer faces at the end (radians): where the next leg of a manoeuvre starts them.</summary>
        public List<double> EndTrailerHeadings => Trailers.Select(t => t.Poses[t.Poses.Count - 1].Heading).ToList();

        /// <summary>Overall length of the reference point's path.</summary>
        public double PathLength => Path.Length;

        /// <summary>The pose when the reference point has travelled <paramref name="chainage"/> along the path.</summary>
        public VehiclePose PoseAt(double chainage) => PoseAt(Poses, chainage);

        /// <summary>Where each trailer's axle group is when the reference point has travelled <paramref name="chainage"/> along the path.</summary>
        public List<VehiclePose> TrailerPosesAt(double chainage) => Trailers.Select(t => PoseAt(t.Poses, chainage)).ToList();

        private VehiclePose PoseAt(List<VehiclePose> poses, double chainage)
        {
            if (poses.Count == 0)
            {
                throw new InvalidOperationException("No poses.");
            }

            int i = Chainages.BinarySearch(chainage);
            if (i >= 0)
            {
                return poses[i];
            }

            i = ~i;
            if (i <= 0)
            {
                return poses[0];
            }

            if (i >= poses.Count)
            {
                return poses[poses.Count - 1];
            }

            double t = (chainage - Chainages[i - 1]) / Math.Max(1e-12, Chainages[i] - Chainages[i - 1]);
            VehiclePose a = poses[i - 1], b = poses[i];
            double dh = Angles.Wrap(b.Heading - a.Heading);
            return new VehiclePose(
                new Point2(a.RearAxle.X + (b.RearAxle.X - a.RearAxle.X) * t, a.RearAxle.Y + (b.RearAxle.Y - a.RearAxle.Y) * t),
                a.Heading + dh * t);
        }

        /// <summary>
        /// The swept area as one closed loop: left envelope, the leading end of the body at the end,
        /// the right envelope back, and the trailing end at the start. Bulges describe the end arcs.
        /// </summary>
        public PolylineSpec ClosedOutline()
        {
            DesignVehicle v = Vehicle;
            double frontBulge = v.EffectiveWidth > 1e-9 ? 2 * v.FrontOverhangRounding / v.EffectiveWidth : 0;
            double rearBulge = v.EffectiveWidth > 1e-9 ? 2 * v.RearOverhangRounding / v.EffectiveWidth : 0;
            bool forward = !Reverse;

            var pts = new List<Point2>();
            var bulges = new List<double>();
            foreach (Point2 p in LeftBody)
            {
                pts.Add(p);
                bulges.Add(0);
            }

            // Leading end: forward = the nose (front-left to front-right, bulging ahead, clockwise);
            // reverse = the tail (rear-left to rear-right, bulging behind, anticlockwise). The tail
            // of a combination is its last trailer's, which is square.
            bool towing = Trailers.Count > 0;
            bulges[bulges.Count - 1] = forward ? -frontBulge : towing ? 0 : rearBulge;
            for (int i = RightBody.Count - 1; i >= 0; i--)
            {
                pts.Add(RightBody[i]);
                bulges.Add(0);
            }

            // Trailing end closes the loop back to the first left point. Behind a trailer it is square.
            bulges[bulges.Count - 1] = forward ? (towing ? 0 : -rearBulge) : frontBulge;
            return new PolylineSpec { Points = pts, Bulges = bulges, Closed = true };
        }
    }

    /// <summary>
    /// Simulates a vehicle following a path and works out the area its body sweeps.
    ///
    /// Model: the rear wheels cannot slip sideways (kinematic bicycle model). When a point of
    /// the vehicle at (a, b) in the vehicle frame moves by dP, the heading changes by
    /// dθ = (dP · n) / a, where n is the vehicle's left normal; the rear axle then follows the
    /// classic tractrix. With the rear axle itself as the reference (a = 0), the heading is
    /// simply the path tangent. The instantaneous centre always lies on the rear axle line, so
    /// at each pose the swept path's inner edge is traced by the body point nearest to that
    /// centre and the outer edge by the point farthest from it; between poses where that
    /// point changes, the body's own outline joins the two.
    ///
    /// Trailers are dragged behind by their couplings under the same no-slip rule. Reversing, they
    /// are pushed instead, and the vehicle is steered to keep them in hand: see the notes above
    /// the Push method.
    /// </summary>
    public static class TurnPathSimulator
    {
        /// <summary>Curvature below this (radius above 100 km) counts as straight.</summary>
        private const double StraightCurvature = 1e-5;

        public static TurnPathResult Simulate(TurnPathRequest req)
        {
            if (req.Path == null || req.Path.Segments.Count == 0)
            {
                throw new ArgumentException("The path is empty.");
            }

            DesignVehicle v = req.Vehicle;
            List<string> problems = v.Validate();
            if (problems.Count > 0)
            {
                throw new ArgumentException($"Vehicle {v.Name}: {string.Join("; ", problems)}.");
            }

            double step = req.Step > 0 ? Math.Min(req.Step, 0.5) : 0.02;
            if (req.Path.Length < step)
            {
                throw new ArgumentException("The path is shorter than one simulation step.");
            }

            // Reversing with trailers, the vehicle's rear axle is what is steered along the path.
            bool pushing = v.IsArticulated && req.Reverse;
            var result = new TurnPathResult { Vehicle = v, Path = req.Path, Reference = pushing ? v.RearAxleCentre : req.Reference, Reverse = req.Reverse };
            if (pushing)
            {
                Push(req, step, result);
            }
            else
            {
                Integrate(req, step, result);
                result.Trailers.AddRange(Tow(v, result.Poses, req.InitialTrailerHeadings));
            }

            result.Curvatures.AddRange(CurvaturesOf(result.Poses, result.Reverse));
            foreach (TrailerTrack track in result.Trailers)
            {
                track.Curvatures.AddRange(CurvaturesOf(track.Poses, result.Reverse));
            }

            result.MaxArticulationDeg = SharpestFold(result) * 180 / Math.PI;
            Envelopes(req, result);
            Checks(result);
            return result;
        }

        // ------------------------------------------------------------ kinematics

        private static void Integrate(TurnPathRequest req, double step, TurnPathResult r)
        {
            Path2 path = req.Path;
            double a = req.Reference.X, b = req.Reference.Y;
            double heading = req.InitialHeading ?? path.TangentAt(0) + (req.Reverse ? Math.PI : 0);
            bool steer = Math.Abs(a) > 1e-9;

            int steps = (int)Math.Ceiling(path.Length / step - 1e-9);
            Point2 prev = path.PointAt(0);
            for (int i = 0; i <= steps; i++)
            {
                double s = Math.Min(path.Length, i * step);
                Point2 p = path.PointAt(s);
                if (i > 0)
                {
                    if (steer)
                    {
                        double dx = p.X - prev.X, dy = p.Y - prev.Y;
                        // Heun's method on dθ = (dP · n(θ)) / a.
                        double d1 = (-dx * Math.Sin(heading) + dy * Math.Cos(heading)) / a;
                        double mid = heading + d1 / 2;
                        heading += (-dx * Math.Sin(mid) + dy * Math.Cos(mid)) / a;
                    }
                    else
                    {
                        heading = path.TangentAt(s) + (req.Reverse ? Math.PI : 0);
                    }
                }

                heading = Angles.Normalize(heading);
                double c = Math.Cos(heading), sn = Math.Sin(heading);
                var rear = new Point2(p.X - a * c + b * sn, p.Y - a * sn - b * c);
                r.Poses.Add(new VehiclePose(rear, heading));
                r.Chainages.Add(s);
                prev = p;
            }

            double net = 0;
            for (int i = 1; i < r.Poses.Count; i++)
            {
                net += Angles.Wrap(r.Poses[i].Heading - r.Poses[i - 1].Heading);
            }

            r.NetTurnDeg = net * 180 / Math.PI;
        }

        /// <summary>
        /// Tows the trailers. Each is dragged by its coupling: its axle group can't slip sideways,
        /// so when the coupling moves by dP the trailer turns by (dP . n) / wheelbase, n being the
        /// trailer's left normal. That is the same law that makes the vehicle's rear axle follow its
        /// front, applied again for every trailer down the line.
        /// </summary>
        private static List<TrailerTrack> Tow(DesignVehicle v, List<VehiclePose> towing, IReadOnlyList<double>? initialHeadings)
        {
            var tracks = new List<TrailerTrack>();
            double hitch = v.Hitch;
            for (int t = 0; t < v.Trailers.Count; t++)
            {
                TrailerUnit unit = v.Trailers[t];
                var track = new TrailerTrack(unit);
                double length = unit.Wheelbase;
                double heading = initialHeadings != null && t < initialHeadings.Count ? initialHeadings[t] : towing[0].Heading;
                Point2 prev = towing[0].ToWorld(hitch, 0);
                for (int i = 0; i < towing.Count; i++)
                {
                    Point2 coupling = towing[i].ToWorld(hitch, 0);
                    if (i > 0)
                    {
                        heading = Drag(heading, coupling.X - prev.X, coupling.Y - prev.Y, length);
                    }

                    heading = Angles.Normalize(heading);
                    track.Poses.Add(new VehiclePose(new Point2(coupling.X - length * Math.Cos(heading), coupling.Y - length * Math.Sin(heading)), heading));
                    prev = coupling;
                }

                tracks.Add(track);
                towing = track.Poses;
                hitch = unit.Hitch;
            }

            return tracks;
        }

        /// <summary>A trailer's heading after its coupling has moved by (dx, dy): Heun's method, as for the vehicle itself.</summary>
        private static double Drag(double heading, double dx, double dy, double length)
        {
            double d1 = (-dx * Math.Sin(heading) + dy * Math.Cos(heading)) / length;
            double mid = heading + d1 / 2;
            return heading + (-dx * Math.Sin(mid) + dy * Math.Cos(mid)) / length;
        }

        /// <summary>The sharpest angle between two coupled units over the manoeuvre, radians.</summary>
        private static double SharpestFold(TurnPathResult r)
        {
            double sharpest = 0;
            List<VehiclePose> towing = r.Poses;
            foreach (TrailerTrack track in r.Trailers)
            {
                for (int i = 0; i < track.Poses.Count; i++)
                {
                    sharpest = Math.Max(sharpest, Math.Abs(Angles.Wrap(towing[i].Heading - track.Poses[i].Heading)));
                }

                towing = track.Poses;
            }

            return sharpest;
        }

        // ------------------------------------------------------ reversing trailers
        //
        // A trailer pushed backwards doesn't follow the vehicle: it folds one way or the other,
        // faster the further it has folded, and the driver steers against that all the way. So a
        // reverse can't be worked out the way a forward drive is, unit by unit behind a point on
        // the path. Two things make it tractable.
        //
        // Driving is reversible. The tracks a combination leaves driving a path forwards are exactly
        // the tracks it would leave reversing back along it, so the reverse that finishes with
        // everything in line at the end of the path is the forward drive from that end, played
        // backwards. That gives the line to hold: where every unit should be, and how hard each is
        // turning, at every point of the path.
        //
        // The combination doesn't start on that line (it starts wherever the last leg left it, or
        // straight), so it is driven: the vehicle's rear axle moves back a step at a time, steered
        // no tighter than its lock, and every trailer is pushed by its coupling under the no-slip
        // rule. The steering comes from the back of the combination forwards. The last trailer's
        // axle group is brought onto its line by turning it a little more or less than the line
        // does; a trailer turns as the fold at its coupling dictates, so that asks for a fold; and
        // a fold is changed by turning the unit in front more or less than it otherwise would. At
        // the front that is the steering. Each fold is corrected a few times faster than the one
        // behind it, which is what keeps the chain from hunting.

        /// <summary>Distance over which the fold at the first coupling is corrected (m); each coupling further back takes <see cref="PushSlower"/> times longer.</summary>
        private const double PushSettle = 1.5;

        private const double PushSlower = 4;

        /// <summary>A fold past this can't be simulated on: the trailer has stopped moving back and is being pushed sideways.</summary>
        private const double JackknifeFold = 85 * Math.PI / 180;

        /// <summary>
        /// The line a reverse along the path holds: the vehicle's rear axle on the path, facing back
        /// along it, and the trailers where driving the path the other way, from in line at its end,
        /// would have them.
        /// </summary>
        private static TurnPathResult LineToHold(DesignVehicle v, Path2 path, double step, out List<TrailerTrack> trailers)
        {
            var lead = new TurnPathResult { Vehicle = v, Path = path, Reverse = true };
            Integrate(new TurnPathRequest { Vehicle = v, Path = path, Reference = v.RearAxleCentre, Reverse = true }, step, lead);
            var outward = new List<VehiclePose>(lead.Poses);
            outward.Reverse();
            trailers = Tow(v, outward, null);
            foreach (TrailerTrack t in trailers)
            {
                t.Poses.Reverse();
            }

            return lead;
        }

        /// <summary>
        /// Which way each trailer has to face (radians, front to back) at the start of a reverse
        /// along the path for the vehicle's rear axle to stay on the path and the combination to
        /// finish in line at its end.
        /// </summary>
        public static List<double> TrailerHeadingsToReverse(DesignVehicle v, Path2 path, double step = 0.02)
        {
            step = step > 0 ? Math.Min(step, 0.5) : 0.02;
            if (path == null || path.Segments.Count == 0 || path.Length < step)
            {
                throw new ArgumentException("The path is empty, or shorter than one simulation step.");
            }

            LineToHold(v, path, step, out List<TrailerTrack> trailers);
            return trailers.Select(t => t.Poses[0].Heading).ToList();
        }

        private static void Push(TurnPathRequest req, double step, TurnPathResult r)
        {
            DesignVehicle v = r.Vehicle;
            int n = v.Trailers.Count;
            TurnPathResult lead = LineToHold(v, req.Path, step, out List<TrailerTrack> line);

            // Where it starts: as the last leg left it. Not told, the trailers start on the line.
            int last = lead.Poses.Count - 1;
            var th = new double[n + 1];
            th[0] = req.InitialHeading ?? lead.Poses[0].Heading;
            bool onLine = Math.Abs(Angles.Wrap(th[0] - lead.Poses[0].Heading)) < 1e-7;
            for (int k = 1; k <= n; k++)
            {
                th[k] = req.InitialTrailerHeadings != null && k - 1 < req.InitialTrailerHeadings.Count ? req.InitialTrailerHeadings[k - 1] : line[k - 1].Poses[0].Heading;
                onLine &= Math.Abs(Angles.Wrap(th[k] - line[k - 1].Poses[0].Heading)) < 1e-7;
            }

            if (onLine)
            {
                // Already where the line starts: the reverse is the forward drive played backwards.
                r.Poses.AddRange(lead.Poses);
                r.Chainages.AddRange(lead.Chainages);
                r.NetTurnDeg = lead.NetTurnDeg;
                r.Trailers.AddRange(line);
                if (req.InitialTrailerHeadings == null)
                {
                    // Nobody said where the trailers start, so they start where the path needs them. Say so when that isn't straight.
                    double front = th[0];
                    for (int k = 1; k <= n; k++)
                    {
                        double fold = Math.Abs(Angles.Wrap(front - th[k])) * 180 / Math.PI;
                        if (fold > 1)
                        {
                            r.Warnings.Add($"To reverse along this path the {v.Trailers[k - 1].Name} starts folded {fold:0} degrees against the unit in front of it, as drawn: where it would be had the {v.Name} driven the path the other way. Start the path with a straight to start it in line.");
                        }

                        front = th[k];
                    }
                }

                return;
            }

            // What the line asks of each unit (0 = the vehicle) at each pose: its heading and how
            // hard it turns (positive = to its left).
            var lineHeading = new double[n + 1][];
            var lineTurn = new double[n + 1][];
            for (int k = 0; k <= n; k++)
            {
                List<VehiclePose> poses = k == 0 ? lead.Poses : line[k - 1].Poses;
                lineHeading[k] = poses.Select(p => p.Heading).ToArray();
                lineTurn[k] = CurvaturesOf(poses, reverse: true).ToArray();
            }

            List<VehiclePose> tail = line[n - 1].Poses;
            double At(double[] values, double j)
            {
                int i = Math.Min(last - 1, (int)j);
                return values[i] + (values[i + 1] - values[i]) * (j - i);
            }

            double HeadingAt(double[] values, double j)
            {
                int i = Math.Min(last - 1, (int)j);
                return values[i] + Angles.Wrap(values[i + 1] - values[i]) * (j - i);
            }

            // Where along its line the last trailer's axle group is: the nearest point of the line
            // near where it was, never going back. Past the end of the line counts as past the end.
            int window = Math.Max(3, (int)Math.Ceiling(2 / step));
            double Progress(Point2 p, double from)
            {
                double best = double.MaxValue, at = from;
                int i0 = Math.Max(0, Math.Min(last - 1, (int)from - 1)), i1 = Math.Min(last - 1, (int)from + window);
                for (int i = i0; i <= i1; i++)
                {
                    Point2 a = tail[i].RearAxle, b = tail[i + 1].RearAxle;
                    double dx = b.X - a.X, dy = b.Y - a.Y, len2 = dx * dx + dy * dy;
                    double t = len2 < 1e-18 ? 0 : ((p.X - a.X) * dx + (p.Y - a.Y) * dy) / len2;
                    t = Math.Max(0, i == last - 1 ? t : Math.Min(1, t));
                    double ex = p.X - (a.X + dx * Math.Min(1, t)), ey = p.Y - (a.Y + dy * Math.Min(1, t));
                    double d2 = ex * ex + ey * ey;
                    if (d2 < best)
                    {
                        best = d2;
                        at = i + t;
                    }
                }

                return Math.Max(from, at);
            }

            // The same, as a distance along the line (its last step is usually a short one).
            var run = new double[last + 1];
            for (int i = 1; i <= last; i++)
            {
                run[i] = run[i - 1] + Segment.Dist(tail[i - 1].RearAxle, tail[i].RearAxle);
            }

            double Along(double j)
            {
                int i = Math.Min(last - 1, (int)j);
                return run[i] + (run[i + 1] - run[i]) * (j - i);
            }

            // The combination: where each unit's axle is and which way it faces.
            var length = new double[n + 1];   // coupling to axle group, of trailer k
            var hitch = new double[n + 1];    // where trailer k couples on, ahead of the axle of the unit in front
            for (int k = 1; k <= n; k++)
            {
                length[k] = v.Trailers[k - 1].Wheelbase;
                hitch[k] = k == 1 ? v.Hitch : v.Trailers[k - 2].Hitch;
            }

            var axle = new Point2[n + 1];
            axle[0] = req.Path.PointAt(0);
            Point2 Coupling(int k) => new Point2(axle[k - 1].X + hitch[k] * Math.Cos(th[k - 1]), axle[k - 1].Y + hitch[k] * Math.Sin(th[k - 1]));
            for (int k = 1; k <= n; k++)
            {
                Point2 c = Coupling(k);
                axle[k] = new Point2(c.X - length[k] * Math.Cos(th[k]), c.Y - length[k] * Math.Sin(th[k]));
            }

            var before = new Point2[n + 1];
            void Back(double ds, double turn)
            {
                for (int k = 1; k <= n; k++)
                {
                    before[k] = Coupling(k);
                }

                // The rear axle backs along an arc: reversing, a turn to the left swings the heading right.
                double dth = -turn * ds, half = dth / 2;
                double chord = Math.Abs(half) < 1e-9 ? ds : ds * Math.Sin(half) / half;
                axle[0] = new Point2(axle[0].X - chord * Math.Cos(th[0] + half), axle[0].Y - chord * Math.Sin(th[0] + half));
                th[0] += dth;
                for (int k = 1; k <= n; k++)
                {
                    Point2 c = Coupling(k);
                    th[k] = Drag(th[k], c.X - before[k].X, c.Y - before[k].Y, length[k]);
                    axle[k] = new Point2(c.X - length[k] * Math.Cos(th[k]), c.Y - length[k] * Math.Sin(th[k]));
                }
            }

            double travelled = 0;
            void Record()
            {
                r.Poses.Add(new VehiclePose(axle[0], Angles.Normalize(th[0])));
                r.Chainages.Add(travelled);
                for (int k = 1; k <= n; k++)
                {
                    r.Trailers[k - 1].Poses.Add(new VehiclePose(axle[k], Angles.Normalize(th[k])));
                }
            }

            r.Trailers.AddRange(v.Trailers.Select(t => new TrailerTrack(t)));
            Record();

            double fullLock = 1 / v.RearAxleRadius(req.TurnRadius ?? v.MinTurnRadius);
            double settle = Math.Max(PushSettle, 3 * step);
            // The last trailer comes onto its line slowest of all: a fold takes metres to wind on and
            // longer to wind off, so anything brisker than about its own length asks for folds that
            // can't be taken off again in time.
            double look = Math.Max(settle * Math.Pow(PushSlower, n), length[n]);
            double maxFold = Math.Max(10, v.MaxArticulationDeg - 5) * Math.PI / 180;
            var savedAxle = new Point2[n + 1];
            var savedTh = new double[n + 1];
            double j = Progress(axle[n], 0);
            double worst = 0;
            bool folded = false;
            int limit = 2 * last + (int)(20 / step);   // well past the length of the path: it isn't getting there
            for (int count = 0; j < last && count < limit; count++)
            {
                // The last trailer: off its line by e to its left, and facing psi to the left of it.
                double refHeading = HeadingAt(lineHeading[n], j);
                int i = Math.Min(last - 1, (int)j);
                double f = j - i;
                double px = tail[i].RearAxle.X + (tail[i + 1].RearAxle.X - tail[i].RearAxle.X) * f;
                double py = tail[i].RearAxle.Y + (tail[i + 1].RearAxle.Y - tail[i].RearAxle.Y) * f;
                double e = -(axle[n].X - px) * Math.Sin(refHeading) + (axle[n].Y - py) * Math.Cos(refHeading);
                double psi = Angles.Wrap(th[n] - refHeading);
                worst = Math.Max(worst, Math.Abs(e));

                // Backing, it drifts left when it faces right of the line, and turning left swings it
                // right: so it turns as the line does, less for being left of it, more for facing left.
                double turn = At(lineTurn[n], j) - Math.Max(-look / 2, Math.Min(look / 2, e)) / (look * look) + 2 * psi / look;
                double pace = settle * Math.Pow(PushSlower, n - 1);
                for (int k = n; k >= 1; k--)
                {
                    // The fold at this coupling that turns the trailer that hard, and the turn of the
                    // unit in front that brings the fold there.
                    double fold = Angles.Wrap(th[k - 1] - th[k]);
                    double lineFold = Angles.Wrap(HeadingAt(lineHeading[k - 1], j) - HeadingAt(lineHeading[k], j));
                    double lineFront = At(lineTurn[k - 1], j);
                    double most = Math.Max(maxFold, Math.Abs(lineFold));
                    double want = Math.Max(-most, Math.Min(most, Math.Atan(length[k] * turn) - Math.Atan(hitch[k] * lineFront)));
                    double grip = 1 - hitch[k] * Math.Cos(fold) / length[k];
                    double lineGrip = 1 - hitch[k] * Math.Cos(lineFold) / length[k];
                    turn = (lineFront * lineGrip + (Math.Sin(fold) - Math.Sin(lineFold)) / length[k] + (fold - want) / pace) / (Math.Abs(grip) < 0.2 ? 0.2 : grip);
                    pace /= PushSlower;
                }

                turn = Math.Max(-fullLock, Math.Min(fullLock, turn));

                Array.Copy(axle, savedAxle, n + 1);
                Array.Copy(th, savedTh, n + 1);
                Back(step, turn);
                double next = Progress(axle[n], j);
                double ds = step;
                if (next >= last && Along(next) - Along(j) > 1e-9)
                {
                    // The last step, cut short so the trailer stops at the end of its line.
                    ds = step * Math.Min(1, (run[last] - Along(j)) / (Along(next) - Along(j)));
                    Array.Copy(savedAxle, axle, n + 1);
                    Array.Copy(savedTh, th, n + 1);
                    Back(ds, turn);
                }

                j = next;
                travelled += ds;
                Record();

                for (int k = 1; k <= n; k++)
                {
                    folded |= Math.Abs(Angles.Wrap(th[k - 1] - th[k])) > JackknifeFold;
                }

                if (folded)
                {
                    break;
                }
            }

            double net = 0;
            for (int i = 1; i < r.Poses.Count; i++)
            {
                net += Angles.Wrap(r.Poses[i].Heading - r.Poses[i - 1].Heading);
            }

            r.NetTurnDeg = net * 180 / Math.PI;
            int stride = Math.Max(1, (int)(0.1 / step));
            for (int i = 0; i < r.Poses.Count; i += stride)
            {
                r.MaxPathDeviation = Math.Max(r.MaxPathDeviation, r.Path.Closest(r.Poses[i].RearAxle).Distance);
            }

            if (folded)
            {
                r.Warnings.Add($"Reversing stopped after {travelled:0.0} m of {r.Path.Length:0.0} m: a trailer folded right round against the unit pushing it (a jackknife). Reverse a gentler turn, or start with the combination straighter.");
                return;
            }

            if (j < last)
            {
                r.Warnings.Add($"Reversing stopped after {travelled:0.0} m: the {v.Trailers[n - 1].Name} couldn't be pushed any further along the path.");
                return;
            }

            Point2 end = tail[last].RearAxle;
            double endHeading = tail[last].Heading;
            r.EndOffset = Math.Abs(-(axle[n].X - end.X) * Math.Sin(endHeading) + (axle[n].Y - end.Y) * Math.Cos(endHeading));
            double askew = Math.Abs(Angles.Wrap(th[n] - endHeading)) * 180 / Math.PI;
            if (r.EndOffset > 0.05 || askew > 1)
            {
                r.Warnings.Add($"Reversing, the {v.Trailers[n - 1].Name} finishes {r.EndOffset:0.00} m off the line of the path and {askew:0} degrees out of line with it (it was up to {worst:0.0} m off on the way): there isn't the length, or the steering, to bring it round. Give the reverse a longer or gentler path, or start it with the trailer set up for the turn.");
            }
        }

        /// <summary>Curvature of an axle's path at each pose, from neighbouring headings.</summary>
        private static List<double> CurvaturesOf(List<VehiclePose> poses, bool reverse)
        {
            int n = poses.Count;
            var curvatures = new List<double>(n);
            double last = 0;
            for (int i = 0; i < n; i++)
            {
                int i0 = Math.Max(0, i - 1), i1 = Math.Min(n - 1, i + 1);
                double ds = 0;
                for (int k = i0; k < i1; k++)
                {
                    ds += Segment.Dist(poses[k].RearAxle, poses[k + 1].RearAxle);
                }

                double dth = Angles.Wrap(poses[i1].Heading - poses[i0].Heading);
                // ds is a chord of the axle's path; on a circle chord = 2 R sin(dθ/2).
                double k2 = ds > 1e-9 ? (Math.Abs(dth) < 1e-6 ? dth / ds : 2 * Math.Sin(dth / 2) / ds) : last;
                // Reversing: the rear axle moves backwards, so a left turn of the body shows as a
                // negative heading rate per metre travelled. Curvature is about the geometry of the
                // turn (which side the centre is on), so use the sign of the heading change.
                if (ds > 1e-9 && reverse)
                {
                    k2 = -k2;
                }

                curvatures.Add(k2);
                last = k2;
            }

            return curvatures;
        }

        // -------------------------------------------------------------- envelope
        //
        // The swept path is the boundary of the union of the body over every pose. For a convex
        // body moving smoothly that boundary is made only of the traces of its outline's vertices
        // and rounded corners, the trace of the point of each side nearest the turn centre (the
        // side at the rear axle), and the body's own ends at the first and last pose. So: generate
        // those traces, keep the points that no other pose's body covers, and order them along
        // the path. Picking a single "extreme" point per pose would miss, for example, the rear of
        // the car swinging out at the start of a turn.
        //
        // A vehicle with trailers is several such bodies moving together. The swept path is the
        // union over all of them, so every body's traces are candidates and a point is dropped when
        // any body covers it at any pose: the prime mover's inner trace disappears under the
        // trailer that cuts inside it.

        /// <summary>
        /// A part of the body's outline that can bound the swept path: a vertex, or a rounded corner.
        /// Corners are modelled as the arc they are: the point of an arc that reaches furthest out
        /// moves continuously as the turn tightens.
        /// </summary>
        private sealed class ChainElement
        {
            private readonly VehiclePoint _point;
            private readonly VehiclePoint _centre;
            private readonly double _radius;
            private readonly double _angleLow;  // angle at the lower-index end
            private readonly double _sweep;     // signed, to the higher-index end

            private ChainElement(VehiclePoint point)
            {
                _point = point;
            }

            private ChainElement(VehiclePoint centre, double radius, double angleLow, double sweep)
            {
                _centre = centre;
                _radius = radius;
                _angleLow = angleLow;
                _sweep = sweep;
            }

            public static ChainElement Vertex(VehiclePoint p) => new ChainElement(p);

            public static ChainElement Arc(VehiclePoint centre, double radius, double angleLow, double sweep) =>
                radius < 1e-9 || Math.Abs(sweep) < 1e-9 ? new ChainElement(At(centre, radius, angleLow)) : new ChainElement(centre, radius, angleLow, sweep);

            public bool IsArc => _radius > 0;

            private static VehiclePoint At(VehiclePoint c, double r, double angle) => new VehiclePoint(c.X + r * Math.Cos(angle), c.Y + r * Math.Sin(angle));

            public VehiclePoint At(double t) => IsArc ? At(_centre, _radius, _angleLow + _sweep * t) : _point;

            /// <summary>
            /// The point of this element that reaches furthest towards the side at this pose, as a
            /// position along the element (0..1; always 0 for a vertex).
            /// </summary>
            public double Best(double curvature, double side)
            {
                if (!IsArc)
                {
                    return 0;
                }

                // Candidates: both ends, and where the radial line from the turn centre (or the lateral
                // direction, going straight) crosses the arc.
                double bestT = 0, bestScore = side * Leftness(curvature, At(0));
                void Try(double t)
                {
                    double sc = side * Leftness(curvature, At(t));
                    if (sc > bestScore)
                    {
                        bestScore = sc;
                        bestT = t;
                    }
                }

                Try(1);
                double wx, wy;
                if (Math.Abs(curvature) < StraightCurvature)
                {
                    wx = 0;
                    wy = 1;
                }
                else
                {
                    wx = _centre.X;
                    wy = _centre.Y - 1 / curvature;
                    double len = Math.Sqrt(wx * wx + wy * wy);
                    wx /= len;
                    wy /= len;
                }

                foreach (double dir in new[] { 1.0, -1.0 })
                {
                    double ang = Math.Atan2(wy * dir, wx * dir);
                    double off = Angles.Wrap(ang - _angleLow) * Math.Sign(_sweep);
                    if (off >= 0 && off <= Math.Abs(_sweep))
                    {
                        Try(off / Math.Abs(_sweep));
                    }
                }

                return bestT;
            }

            /// <summary>Points along the element from position <paramref name="from"/> to <paramref name="to"/> (exclusive of from).</summary>
            public IEnumerable<VehiclePoint> Between(double from, double to)
            {
                if (!IsArc)
                {
                    yield break;
                }

                int parts = Math.Max(1, (int)Math.Ceiling(Math.Abs(to - from) * Math.Abs(_sweep) / (4 * Math.PI / 180)));
                for (int i = 1; i <= parts; i++)
                {
                    yield return At(from + (to - from) * i / parts);
                }
            }
        }

        /// <summary>The outline parts that can bound the swept path on one side, front to rear.</summary>
        private static ChainElement[] Chain(DesignVehicle v, bool left)
        {
            double side = left ? 1 : -1;
            double hw = v.Width / 2 * side, he = v.EffectiveWidth / 2 * side, c = v.CornerLength;
            double xf = v.Wheelbase + v.EffectiveFrontOverhang, xr = -v.EffectiveRearOverhang;
            double rc = v.CornerRadius, sweep = v.CornerSweep;
            // Corner arc centres sit inside the body, below (left side) the full-width line.
            var frontCentre = new VehiclePoint(xf - c, hw - rc * side);
            var rearCentre = new VehiclePoint(xr + c, hw - rc * side);
            double up = side * Math.PI / 2; // direction from a corner centre to the side's tangent point
            return new[]
            {
                ChainElement.Vertex(new VehiclePoint(xf, he)),                       // 0 front effective corner
                ChainElement.Arc(frontCentre, rc, up - side * sweep, side * sweep),  // 1 front corner arc: corner -> side
                ChainElement.Vertex(new VehiclePoint(0, hw)),                        // 2 side at the rear axle (nearest the turn centre)
                ChainElement.Arc(rearCentre, rc, up, side * sweep),                  // 3 rear corner arc: side -> corner
                ChainElement.Vertex(new VehiclePoint(xr, he)),                       // 4 rear effective corner
            };
        }

        private const int FrontCorner = 0;
        private const int RearAxleSide = 2;
        private const int RearCorner = 4;

        private static void Envelopes(TurnPathRequest req, TurnPathResult r)
        {
            DesignVehicle v = r.Vehicle;
            double inside = req.ClearanceInside ?? v.ClearanceInside;
            double outside = req.ClearanceOutside ?? v.ClearanceOutside;
            // Which side gets the circulation clearance: the outside of the overall turn. Reversing,
            // the turn centre is on the opposite side to the way the heading swings.
            double turn = r.Reverse ? -r.NetTurnDeg : r.NetTurnDeg;
            bool leftTurn = turn > 5, rightTurn = turn < -5;
            r.ClearanceLeft = leftTurn ? inside : rightTurn ? outside : inside;
            r.ClearanceRight = rightTurn ? inside : leftTurn ? outside : inside;

            double tol = req.Tolerance > 0 ? req.Tolerance : 0.001;
            double maxLoop = 2 * v.OverallLength;
            bool forward = !r.Reverse;
            var units = new List<SweptUnit> { new SweptUnit(v, r.Poses, r.Curvatures) };
            units.AddRange(r.Trailers.Select(t => new SweptUnit(t.Body, t.Poses, t.Curvatures)));
            var coverage = new Coverage(r, units);

            List<Point2> leftLine = Boundary(units, coverage, left: true, forward, tol);
            List<Point2> rightLine = Boundary(units, coverage, left: false, forward, tol);

            r.LeftBody = Clean(leftLine, tol, maxLoop);
            r.RightBody = Clean(rightLine, tol, maxLoop);
            // The lines run in the direction of travel; reversing, the vehicle's left is on the right of travel.
            r.LeftClearance = Clean(OffsetChain(r.LeftBody, r.ClearanceLeft, leftSide: forward), tol, maxLoop);
            r.RightClearance = Clean(OffsetChain(r.RightBody, r.ClearanceRight, leftSide: !forward), tol, maxLoop);

            // Front wheels, then the rear group (at its centre: where the vehicle pivots), then each trailer's.
            foreach (var (x, y) in new[] { (v.Wheelbase, v.Track / 2), (v.Wheelbase, -v.Track / 2), (0.0, v.RearTrackWidth / 2), (0.0, -v.RearTrackWidth / 2) })
            {
                r.WheelPaths.Add(Simplify(r.Poses.Select(p => p.ToWorld(x, y)).ToList(), tol));
            }

            foreach (TrailerTrack t in r.Trailers)
            {
                foreach (double y in new[] { t.Body.Track / 2, -t.Body.Track / 2 })
                {
                    r.WheelPaths.Add(Simplify(t.Poses.Select(p => p.ToWorld(0, y)).ToList(), tol));
                }
            }
        }

        /// <summary>
        /// The swept path's edge on one side: every outline part's trace, for every body (with each
        /// body's trailing end at the first pose and leading end at the last), minus points inside
        /// a body at any other pose, ordered along the path.
        /// </summary>
        private static List<Point2> Boundary(IReadOnlyList<SweptUnit> units, Coverage coverage, bool left, bool forward, double tol)
        {
            double side = left ? 1 : -1;
            var candidates = new List<(Point2 Point, int Unit, int Pose)>();
            for (int u = 0; u < units.Count; u++)
            {
                SweptUnit unit = units[u];
                ChainElement[] chain = Chain(unit.Body, left);
                List<VehiclePose> poses = unit.Poses;
                int n = poses.Count;
                int index = u;

                void Add(VehiclePose pose, int at, VehiclePoint p) => candidates.Add((pose.ToWorld(p), index, at));

                // An end of the body: from a corner to the side at the axle, or back.
                void Walk(VehiclePose pose, int at, int from, int to)
                {
                    int dir = Math.Sign(to - from);
                    for (int i = from; i != to; i += dir)
                    {
                        ChainElement e = chain[i];
                        Add(pose, at, e.At(dir > 0 ? 0 : 1));
                        foreach (VehiclePoint p in e.Between(dir > 0 ? 0 : 1, dir > 0 ? 1 : 0))
                        {
                            Add(pose, at, p);
                        }
                    }

                    Add(pose, at, chain[to].At(dir > 0 ? 0 : 1));
                }

                // Trailing end of the body at the start: from the trailing corner to the side at the axle.
                Walk(poses[0], 0, forward ? RearCorner : FrontCorner, RearAxleSide);

                var lastT = new double[chain.Length];
                for (int i = 0; i < n; i++)
                {
                    VehiclePose pose = poses[i];
                    double k = unit.Curvatures[i];
                    for (int e = 0; e < chain.Length; e++)
                    {
                        double t = chain[e].Best(k, side);
                        if (i > 0 && chain[e].IsArc && Math.Abs(t - lastT[e]) > 1e-9)
                        {
                            // The extreme point moved along the arc: fill in the arc between.
                            foreach (VehiclePoint p in chain[e].Between(lastT[e], t))
                            {
                                Add(pose, i, p);
                            }
                        }
                        else
                        {
                            Add(pose, i, chain[e].At(t));
                        }

                        lastT[e] = t;
                    }
                }

                // Leading end of the body at the finish: from the side at the axle to the leading corner.
                Walk(poses[n - 1], n - 1, RearAxleSide, forward ? FrontCorner : RearCorner);
            }

            var kept = new List<(double Key, int Order, Point2 Point)>();
            for (int i = 0; i < candidates.Count; i++)
            {
                var (p, unit, pose) = candidates[i];
                if (!coverage.Covered(p, unit, pose, 0.5 * tol))
                {
                    kept.Add((coverage.Key(p), i, p));
                }
            }

            return kept.OrderBy(x => x.Key).ThenBy(x => x.Order).Select(x => x.Point).ToList();
        }

        /// <summary>One body of the vehicle (the vehicle itself, or a trailer) and where it was at every pose.</summary>
        private sealed class SweptUnit
        {
            private readonly double[] _cos;
            private readonly double[] _sin;

            public SweptUnit(DesignVehicle body, List<VehiclePose> poses, List<double> curvatures)
            {
                Body = body;
                Poses = poses;
                Curvatures = curvatures;
                double xMax = Math.Max(Math.Abs(body.Wheelbase + body.FrontOverhang), Math.Abs(body.RearOverhang));
                Reach = Math.Sqrt(xMax * xMax + body.Width * body.Width / 4) + 1e-6;
                _cos = poses.Select(p => Math.Cos(p.Heading)).ToArray();
                _sin = poses.Select(p => Math.Sin(p.Heading)).ToArray();
            }

            public DesignVehicle Body { get; }

            public List<VehiclePose> Poses { get; }

            public List<double> Curvatures { get; }

            /// <summary>The farthest any point of the body is from its axle.</summary>
            public double Reach { get; }

            /// <summary>Is the point (given relative to the axle at pose <paramref name="j"/>) inside the body there, by more than the margin?</summary>
            public bool Covers(int j, double dx, double dy, double margin) =>
                Inside(dx * _cos[j] + dy * _sin[j], -dx * _sin[j] + dy * _cos[j], margin);

            /// <summary>Is (x, y) in the body's frame inside the body shrunk by <paramref name="m"/>?</summary>
            private bool Inside(double x, double y, double m)
            {
                DesignVehicle v = Body;
                double xf = v.Wheelbase + v.EffectiveFrontOverhang, xr = -v.EffectiveRearOverhang;
                double xfo = v.Wheelbase + v.FrontOverhang, xro = -v.RearOverhang;
                if (x < xro + m || x > xfo - m)
                {
                    return false;
                }

                double ay = Math.Abs(y), hw = v.Width / 2, he = v.EffectiveWidth / 2, c = v.CornerLength;
                double bound;
                if (x >= xf)
                {
                    bound = Cap(he, v.FrontOverhangRounding, x - xf);
                }
                else if (x <= xr)
                {
                    bound = Cap(he, v.RearOverhangRounding, xr - x);
                }
                else if (x > xf - c || x < xr + c)
                {
                    double rc = v.CornerRadius;
                    double ox = x > xf - c ? xf - c : xr + c;
                    double under = rc * rc - (x - ox) * (x - ox);
                    bound = rc < 1e-9 ? hw : hw - rc + Math.Sqrt(Math.Max(0, under));
                }
                else
                {
                    bound = hw;
                }

                return ay < bound - m;
            }

            /// <summary>Half width of a rounded end <paramref name="d"/> beyond the corner line (sagitta <paramref name="sag"/>).</summary>
            private static double Cap(double he, double sag, double d)
            {
                if (sag < 1e-9)
                {
                    return d <= 0 ? he : -1;
                }

                if (d > sag)
                {
                    return -1;
                }

                double radius = (he * he + sag * sag) / (2 * sag);
                double dx = d + radius - sag;
                return Math.Sqrt(Math.Max(0, radius * radius - dx * dx));
            }
        }

        /// <summary>Answers "is this point inside a body at some other pose?" and orders points along the path.</summary>
        private sealed class Coverage
        {
            private readonly TurnPathResult _r;
            private readonly IReadOnlyList<SweptUnit> _units;
            private readonly double _stride;       // more than any axle moves between two poses
            private readonly int _every;           // poses between the ones tested
            private readonly Point2 _start;
            private readonly Point2 _end;
            private readonly double _tStart;
            private readonly double _tEnd;

            public Coverage(TurnPathResult r, IReadOnlyList<SweptUnit> units)
            {
                _r = r;
                _units = units;
                double step = 0;
                for (int i = 1; i < r.Chainages.Count; i++)
                {
                    step = Math.Max(step, r.Chainages[i] - r.Chainages[i - 1]);
                }

                // An axle moves no further than the point that leads it, bar a little for a coupling
                // that swings wide behind an axle: twice the step is a safe bound.
                _stride = Math.Max(1e-6, 2 * step);

                // A vehicle with trailers has several times the bodies and the length to test. A point
                // more than a fraction of a millimetre inside the swept path is under a body for at
                // least 80 mm of travel, so testing a pose every 80 mm finds it; what slips through
                // is closer to the edge than the tolerance the edge is drawn to.
                _every = units.Count > 1 && step > 0 ? Math.Max(1, (int)(0.08 / step)) : 1;
                _start = r.Path.Start;
                _end = r.Path.End;
                _tStart = r.Path.TangentAt(0);
                _tEnd = r.Path.TangentAt(r.Path.Length);
            }

            /// <summary>Distance along the path of the point's nearest path point, extended straight beyond the ends.</summary>
            public double Key(Point2 p)
            {
                var (_, along, _) = _r.Path.Closest(p);
                if (along <= 1e-9)
                {
                    return Math.Min(0, (p.X - _start.X) * Math.Cos(_tStart) + (p.Y - _start.Y) * Math.Sin(_tStart));
                }

                if (along >= _r.Path.Length - 1e-9)
                {
                    return _r.Path.Length + Math.Max(0, (p.X - _end.X) * Math.Cos(_tEnd) + (p.Y - _end.Y) * Math.Sin(_tEnd));
                }

                return along;
            }

            /// <summary>
            /// True when some body contains the point by more than <paramref name="margin"/>: its own
            /// body at any pose but <paramref name="ownPose"/>, or another body at any pose at all.
            /// </summary>
            public bool Covered(Point2 p, int ownUnit, int ownPose, double margin)
            {
                for (int u = 0; u < _units.Count; u++)
                {
                    SweptUnit unit = _units[u];
                    List<VehiclePose> poses = unit.Poses;
                    double reach2 = unit.Reach * unit.Reach;
                    if (u != ownUnit)
                    {
                        Point2 at = poses[ownPose].RearAxle;
                        double ox = p.X - at.X, oy = p.Y - at.Y;
                        if (ox * ox + oy * oy <= reach2 && unit.Covers(ownPose, ox, oy, margin))
                        {
                            return true;
                        }
                    }

                    // Walk outwards from the point's own pose. Only poses whose axle is within reach can
                    // cover it; while the axle is further off than that, skip the poses it needs to get back.
                    for (int dir = -1; dir <= 1; dir += 2)
                    {
                        int j = ownPose + dir;
                        while (j >= 0 && j < poses.Count)
                        {
                            Point2 axle = poses[j].RearAxle;
                            double dx = p.X - axle.X, dy = p.Y - axle.Y;
                            double d2 = dx * dx + dy * dy;
                            if (d2 > reach2)
                            {
                                j += dir * Math.Max(_every, (int)((Math.Sqrt(d2) - unit.Reach) / _stride));
                                continue;
                            }

                            if (unit.Covers(j, dx, dy, margin))
                            {
                                return true;
                            }

                            j += dir * _every;
                        }
                    }
                }

                return false;
            }
        }

        /// <summary>
        /// How far towards the left of the manoeuvre a body point reaches at this pose. Turning: the
        /// signed distance inside the circle about the instantaneous centre; straight: the lateral
        /// offset. The two agree in the limit, so the choice of extreme point is continuous.
        /// </summary>
        private static double Leftness(double curvature, VehiclePoint p)
        {
            if (Math.Abs(curvature) < StraightCurvature)
            {
                return p.Y;
            }

            double rho = 1 / curvature; // centre at (0, rho) in the vehicle frame
            double d = Math.Sqrt(p.X * p.X + (p.Y - rho) * (p.Y - rho));
            return Math.Sign(rho) * (Math.Abs(rho) - d);
        }

        // ------------------------------------------------------------ clearances

        /// <summary>
        /// Offsets an open polyline to one side by <paramref name="distance"/> with round joins at
        /// convex corners, i.e. the boundary of the Minkowski sum with a disc on that side. This is
        /// exactly the swept path of the body enlarged by the clearance.
        /// </summary>
        internal static List<Point2> OffsetChain(List<Point2> pts, double distance, bool leftSide)
        {
            pts = Dedupe(pts, 1e-9);
            if (distance <= 0 || pts.Count < 2)
            {
                return new List<Point2>(pts);
            }

            double side = leftSide ? 1 : -1;
            var normals = new List<(double X, double Y)>();
            for (int i = 0; i < pts.Count - 1; i++)
            {
                double dx = pts[i + 1].X - pts[i].X, dy = pts[i + 1].Y - pts[i].Y;
                double len = Math.Sqrt(dx * dx + dy * dy);
                normals.Add((-dy / len * side, dx / len * side));
            }

            var result = new List<Point2> { new Point2(pts[0].X + normals[0].X * distance, pts[0].Y + normals[0].Y * distance) };
            for (int i = 1; i < pts.Count - 1; i++)
            {
                var n0 = normals[i - 1];
                var n1 = normals[i];
                double cross = n0.X * n1.Y - n0.Y * n1.X;
                double dot = Math.Max(-1, Math.Min(1, n0.X * n1.X + n0.Y * n1.Y));
                double turn = Math.Atan2(cross, dot);
                // Turning away from the offset side opens a gap that the round join fills.
                bool convex = leftSide ? turn < 0 : turn > 0;
                if (convex && Math.Abs(turn) > 1e-6)
                {
                    int arcs = Math.Max(1, (int)Math.Ceiling(Math.Abs(turn) / (4 * Math.PI / 180)));
                    double a0 = Math.Atan2(n0.Y, n0.X);
                    for (int k = 0; k <= arcs; k++)
                    {
                        double ang = a0 + turn * k / arcs;
                        result.Add(new Point2(pts[i].X + Math.Cos(ang) * distance, pts[i].Y + Math.Sin(ang) * distance));
                    }
                }
                else
                {
                    // Concave (or straight): the two offset lines meet at the mitre, capped so sharp
                    // corners don't throw the point far away.
                    double mx = n0.X + n1.X, my = n0.Y + n1.Y;
                    double m2 = mx * mx + my * my;
                    if (m2 < 1e-12)
                    {
                        result.Add(new Point2(pts[i].X + n0.X * distance, pts[i].Y + n0.Y * distance));
                        continue;
                    }

                    double scale = Math.Min(2 / m2, 4.0 / Math.Sqrt(m2));
                    result.Add(new Point2(pts[i].X + mx * scale * distance, pts[i].Y + my * scale * distance));
                }
            }

            var nl = normals[normals.Count - 1];
            result.Add(new Point2(pts[pts.Count - 1].X + nl.X * distance, pts[pts.Count - 1].Y + nl.Y * distance));
            return result;
        }

        // ------------------------------------------------------- polyline clean-up

        /// <summary>Merge near points, drop collinear backtracking, simplify, then cut out small self-intersections.</summary>
        internal static List<Point2> Clean(List<Point2> pts, double tol, double maxLoopLength)
        {
            pts = Dedupe(pts, tol * 0.5);
            pts = RemoveBacktracks(pts, tol);
            pts = Simplify(pts, tol);
            pts = RemoveLoops(pts, maxLoopLength);
            return pts;
        }

        internal static List<Point2> Dedupe(List<Point2> pts, double tol)
        {
            var result = new List<Point2>();
            foreach (Point2 p in pts)
            {
                if (result.Count == 0 || Segment.Dist(result[result.Count - 1], p) > tol)
                {
                    result.Add(p);
                }
            }

            return result;
        }

        /// <summary>
        /// Removes doubling back along a straight line: after an excursion forward to A, points that
        /// lie back along the same line are dropped until the line passes A again, so the furthest
        /// point reached stays on the boundary.
        /// </summary>
        internal static List<Point2> RemoveBacktracks(List<Point2> pts, double tol)
        {
            var list = new List<Point2>(pts);
            int i = 1;
            while (i < list.Count - 1)
            {
                Point2 a = list[i - 1], b = list[i], c = list[i + 1];
                double abx = b.X - a.X, aby = b.Y - a.Y, bcx = c.X - b.X, bcy = c.Y - b.Y;
                double dot = abx * bcx + aby * bcy;
                double cross = Math.Abs(abx * bcy - aby * bcx);
                double lenAb2 = abx * abx + aby * aby;
                double lenAb = Math.Sqrt(lenAb2), lenBc = Math.Sqrt(bcx * bcx + bcy * bcy);
                if (dot < 0 && cross <= tol * Math.Max(lenAb, lenBc))
                {
                    // c lies back along a->b: drop c (the excursion to b stands); otherwise b was the short spike.
                    double t = lenAb2 < 1e-24 ? 0 : ((c.X - a.X) * abx + (c.Y - a.Y) * aby) / lenAb2;
                    list.RemoveAt(t >= 0 && t <= 1 ? i + 1 : i);
                    if (i > 1)
                    {
                        i--;
                    }
                }
                else
                {
                    i++;
                }
            }

            return list;
        }

        /// <summary>Douglas-Peucker.</summary>
        internal static List<Point2> Simplify(List<Point2> pts, double tol)
        {
            if (pts.Count < 3)
            {
                return new List<Point2>(pts);
            }

            var keep = new bool[pts.Count];
            keep[0] = keep[pts.Count - 1] = true;
            var stack = new Stack<(int, int)>();
            stack.Push((0, pts.Count - 1));
            while (stack.Count > 0)
            {
                var (first, last) = stack.Pop();
                double worst = 0;
                int index = -1;
                Point2 a = pts[first], b = pts[last];
                double dx = b.X - a.X, dy = b.Y - a.Y, len = Math.Sqrt(dx * dx + dy * dy);
                for (int i = first + 1; i < last; i++)
                {
                    double d = len < 1e-12
                        ? Segment.Dist(pts[i], a)
                        : Math.Abs(dx * (pts[i].Y - a.Y) - dy * (pts[i].X - a.X)) / len;
                    if (d > worst)
                    {
                        worst = d;
                        index = i;
                    }
                }

                if (index >= 0 && worst > tol)
                {
                    keep[index] = true;
                    stack.Push((first, index));
                    stack.Push((index, last));
                }
            }

            var result = new List<Point2>();
            for (int i = 0; i < pts.Count; i++)
            {
                if (keep[i])
                {
                    result.Add(pts[i]);
                }
            }

            return result;
        }

        /// <summary>
        /// Where the line crosses itself a little later on (a small notch where the trace of one
        /// corner hands over to another), replaces the loop with the crossing point. Only loops
        /// shorter than <paramref name="maxLoopLength"/> are removed, so a manoeuvre that really
        /// crosses its own path (a full circle) is left alone.
        /// </summary>
        internal static List<Point2> RemoveLoops(List<Point2> pts, double maxLoopLength)
        {
            var list = new List<Point2>(pts);
            for (int i = 0; i < list.Count - 3; i++)
            {
                double length = 0;
                int j = i + 1;
                while (j < list.Count - 1)
                {
                    length += Segment.Dist(list[j], list[j + 1]);
                    if (length > maxLoopLength)
                    {
                        break;
                    }

                    j++;
                }

                for (j = Math.Min(j, list.Count - 2); j >= i + 2; j--)
                {
                    if (SegmentsCross(list[i], list[i + 1], list[j], list[j + 1], out Point2 x))
                    {
                        list.RemoveRange(i + 1, j - i);
                        list.Insert(i + 1, x);
                        break;
                    }
                }
            }

            return list;
        }

        private static bool SegmentsCross(Point2 a, Point2 b, Point2 c, Point2 d, out Point2 x)
        {
            x = default;
            double r1x = b.X - a.X, r1y = b.Y - a.Y, r2x = d.X - c.X, r2y = d.Y - c.Y;
            double denom = r1x * r2y - r1y * r2x;
            if (Math.Abs(denom) < 1e-15)
            {
                return false;
            }

            double ex = c.X - a.X, ey = c.Y - a.Y;
            double t = (ex * r2y - ey * r2x) / denom;
            double u = (ex * r1y - ey * r1x) / denom;
            const double Eps = 1e-9;
            if (t <= Eps || t >= 1 - Eps || u <= Eps || u >= 1 - Eps)
            {
                return false;
            }

            x = new Point2(a.X + r1x * t, a.Y + r1y * t);
            return true;
        }

        // ---------------------------------------------------------------- checks

        private static void Checks(TurnPathResult r)
        {
            DesignVehicle v = r.Vehicle;
            double maxK = 0;
            foreach (double k in r.Curvatures)
            {
                maxK = Math.Max(maxK, Math.Abs(k));
            }

            if (maxK > StraightCurvature)
            {
                r.TightestRadius = v.OuterWheelRadius(1 / maxK);
                r.MaxSteerAngleDeg = Math.Atan(v.Wheelbase * maxK) * 180 / Math.PI;
            }

            if (!r.WithinMinimumRadius)
            {
                r.Warnings.Add($"The manoeuvre needs a {r.TightestRadius:0.00} m turn (outer front wheel), tighter than the {v.Name}'s minimum of {v.MinTurnRadius:0.0} m.");
            }

            if (r.MaxArticulationDeg > v.MaxArticulationDeg + 0.05)
            {
                r.Warnings.Add($"A trailer folds to {r.MaxArticulationDeg:0} degrees against the unit towing it, past the {v.MaxArticulationDeg:0} degree limit for the {v.Name}: it would jackknife. Use a larger radius or a smaller angle of turn.");
            }
        }
    }

    internal static class Angles
    {
        /// <summary>To (-π, π].</summary>
        public static double Wrap(double a)
        {
            a %= 2 * Math.PI;
            if (a <= -Math.PI)
            {
                a += 2 * Math.PI;
            }
            else if (a > Math.PI)
            {
                a -= 2 * Math.PI;
            }

            return a;
        }

        /// <summary>To [0, 2π).</summary>
        public static double Normalize(double a)
        {
            a %= 2 * Math.PI;
            return a < 0 ? a + 2 * Math.PI : a;
        }
    }
}
