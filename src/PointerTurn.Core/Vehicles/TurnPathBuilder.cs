using System;
using System.Collections.Generic;
using System.Linq;
using PointerTurn.Core.Drawing;
using PointerTurn.Core.Geometry;

namespace PointerTurn.Core.Vehicles
{
    /// <summary>A standard single-turn template, as in AS/NZS 2890.1:2004 Figures B3 to B6.</summary>
    public sealed class TurnTemplate
    {
        public DesignVehicle Vehicle { get; set; } = DesignVehicle.B99;

        /// <summary>Centre of the rear axle at the start (the parked position).</summary>
        public Point2 Start { get; set; }

        /// <summary>Direction of travel at the start, radians anticlockwise from +X.</summary>
        public double Heading { get; set; }

        /// <summary>Turn radius at the outer front wheel (m). Null: the vehicle's minimum.</summary>
        public double? Radius { get; set; }

        /// <summary>Change of direction, degrees (positive).</summary>
        public double TurnAngleDeg { get; set; } = 90;

        public bool Left { get; set; } = true;

        /// <summary>Straight travel before the turn starts (m).</summary>
        public double LeadIn { get; set; }

        /// <summary>Straight travel after the turn (m), which also lets the rear axle settle.</summary>
        public double LeadOut { get; set; } = 6;

        /// <summary>Which point follows the arc. The standard measures the radius at the outer front wheel.</summary>
        public ReferencePoint Reference { get; set; } = ReferencePoint.OuterFrontWheel;
    }

    /// <summary>Builds the paths that <see cref="TurnPathSimulator"/> drives a vehicle along.</summary>
    public static class TurnPathBuilder
    {
        /// <summary>
        /// The request for a single-turn template: the reference point travels straight for the
        /// lead-in, round an arc, then straight for the lead-out. The arc's radius is the
        /// steady-turn radius of the reference point when the outer front wheel is at the
        /// nominated radius.
        /// </summary>
        public static TurnPathRequest Template(TurnTemplate t)
        {
            DesignVehicle v = t.Vehicle;
            double radius = t.Radius ?? v.MinTurnRadius;
            if (radius <= v.Wheelbase)
            {
                throw new ArgumentException($"The turn radius must be larger than the wheelbase ({v.Wheelbase} m).");
            }

            if (t.TurnAngleDeg <= 0 || t.TurnAngleDeg > 360)
            {
                throw new ArgumentException("The turn angle must be between 0 and 360 degrees.");
            }

            VehiclePoint reference = v.Resolve(t.Reference, t.Left);
            double refRadius = v.RadiusOfPoint(radius, reference, t.Left);
            var pose = new VehiclePose(t.Start, t.Heading);
            Point2 p0 = pose.ToWorld(reference);
            double c = Math.Cos(t.Heading), s = Math.Sin(t.Heading);

            var segments = new List<Segment>();
            Point2 p1 = p0;
            if (t.LeadIn > 1e-9)
            {
                p1 = new Point2(p0.X + c * t.LeadIn, p0.Y + s * t.LeadIn);
                segments.Add(new LineSegment(p0, p1));
            }

            double side = t.Left ? 1 : -1;
            var centre = new Point2(p1.X - s * refRadius * side, p1.Y + c * refRadius * side);
            double startAngle = Math.Atan2(p1.Y - centre.Y, p1.X - centre.X);
            double sweep = t.TurnAngleDeg * Math.PI / 180 * side;
            var arc = new ArcSegment(centre, refRadius, startAngle, sweep);
            segments.Add(arc);

            if (t.LeadOut > 1e-9)
            {
                Point2 p2 = arc.End;
                double h = t.Heading + sweep;
                segments.Add(new LineSegment(p2, new Point2(p2.X + Math.Cos(h) * t.LeadOut, p2.Y + Math.Sin(h) * t.LeadOut)));
            }

            return new TurnPathRequest
            {
                Vehicle = v,
                Path = new Path2(segments, false),
                Reference = reference,
                InitialHeading = t.Heading,
            };
        }

        /// <summary>One leg of a drive: how the vehicle gets from a pose to a destination.</summary>
        public sealed class DriveLeg
        {
            public TurnPathRequest Request { get; set; } = null!;
            public bool Reverse { get; set; }

            /// <summary>Turned left (as the driver sees it) on the arc.</summary>
            public bool Left { get; set; }

            /// <summary>Angle turned on the arc, degrees.</summary>
            public double ArcDeg { get; set; }

            public double StraightLength { get; set; }

            /// <summary>
            /// Reversing with trailers: how far the trailers are, at the start, from where the leg
            /// needs them (degrees, over all the couplings). 0 when it carries straight on from
            /// where they are.
            /// </summary>
            public double TrailersOutDeg { get; set; }

            /// <summary>The moves in order, in words: "turn left 40°", "6.0 m straight".</summary>
            public List<string> Moves { get; set; } = new List<string>();

            public double Length => Request.Path.Length;
        }

        /// <summary>How a drive leg turns.</summary>
        public enum DriveTurns
        {
            /// <summary>One arc to the destination when its radius allows; otherwise a minimum-radius turn then straight.</summary>
            Arc,

            /// <summary>Always a turn at the chosen radius, then straight to the destination.</summary>
            Corner,
        }

        /// <summary>The most a leg may turn: a U-turn and a little, never a loop.</summary>
        public const double MaxLegTurnDeg = 190;

        /// <summary>
        /// The leg that takes the vehicle from <paramref name="start"/> to <paramref name="target"/>
        /// the way a car can. With <see cref="DriveTurns.Arc"/>: a single arc, tangent to the way
        /// the vehicle faces, when its radius is at least the turn radius; otherwise (and always with
        /// <see cref="DriveTurns.Corner"/>) a turn at the turn radius until it points at the
        /// destination, then straight to it. Forwards when the destination is ahead of the rear
        /// axle, in reverse when it is behind, unless <paramref name="reverse"/> says. Forwards the
        /// front axle centre arrives at the destination; in reverse the rear axle centre. A leg never
        /// turns more than <see cref="MaxLegTurnDeg"/>: a destination inside the turning circle, or
        /// one that would need a loop, gives null, as does one too close to move to.
        ///
        /// A vehicle with trailers reverses when the destination is behind its last trailer, and it
        /// is that trailer's axle group that arrives there, with the combination in line: see
        /// <see cref="ReverseTowed"/>. <paramref name="trailerHeadings"/> is which way each trailer
        /// faces at the start (radians, front to back; null: straight behind).
        /// </summary>
        public static DriveLeg? DriveTo(DesignVehicle v, VehiclePose start, Point2 target, double? outerWheelRadius = null, bool? reverse = null, DriveTurns turns = DriveTurns.Arc,
            IReadOnlyList<double>? trailerHeadings = null)
        {
            double radius = outerWheelRadius ?? v.MinTurnRadius;
            if (radius < v.MinTurnRadius - 1e-9)
            {
                throw new ArgumentException($"The turn radius can't be less than the {v.Name}'s minimum of {v.MinTurnRadius:0.#} m.");
            }

            if (v.IsArticulated)
            {
                VehiclePose tail = v.TrailerPoses(start, trailerHeadings)[v.Trailers.Count - 1];
                bool behind = (target.X - tail.RearAxle.X) * Math.Cos(tail.Heading) + (target.Y - tail.RearAxle.Y) * Math.Sin(tail.Heading) < 0;
                if (reverse ?? behind)
                {
                    return ReverseTowed(v, start, trailerHeadings, target, null, radius, turns);
                }

                reverse = false;
            }

            DriveLeg? leg = DriveRigid(v, start, target, radius, reverse, turns);
            if (leg != null)
            {
                leg.Request.InitialTrailerHeadings = trailerHeadings;
            }

            return leg;
        }

        private static DriveLeg? DriveRigid(DesignVehicle v, VehiclePose start, Point2 target, double radius, bool? reverse, DriveTurns turns)
        {
            double c = Math.Cos(start.Heading), s = Math.Sin(start.Heading);
            double ahead = (target.X - start.RearAxle.X) * c + (target.Y - start.RearAxle.Y) * s;
            bool back = reverse ?? ahead < v.Wheelbase / 2;

            // The point that drives to the destination, its turning radius, and the direction of travel.
            VehiclePoint reference = back ? v.RearAxleCentre : v.FrontAxleCentre;
            double refRadius = v.RadiusOfPoint(radius, reference, left: true);
            Point2 p0 = start.ToWorld(reference);
            double travel = back ? start.Heading + Math.PI : start.Heading;
            double ux = Math.Cos(travel), uy = Math.Sin(travel);
            double nx = -uy, ny = ux;
            double dxT = target.X - p0.X, dyT = target.Y - p0.Y;
            double along = dxT * ux + dyT * uy, lateral = dxT * nx + dyT * ny;
            if (Math.Sqrt(dxT * dxT + dyT * dyT) < 0.05)
            {
                return null;
            }

            double maxSweep = MaxLegTurnDeg * Math.PI / 180;

            if (turns == DriveTurns.Arc && along > 1e-9)
            {
                // The one arc tangent to the direction of travel through the destination.
                double d2 = along * along + lateral * lateral;
                if (Math.Abs(lateral) < 1e-9)
                {
                    return Leg(v, start, back, reference, new Path2(new List<Segment> { new LineSegment(p0, target) }, false), left: true, sweep: 0, straight: along);
                }

                double arcRadius = d2 / (2 * Math.Abs(lateral));
                if (arcRadius >= refRadius - 1e-9)
                {
                    double side = Math.Sign(lateral);
                    var centre = new Point2(p0.X + nx * arcRadius * side, p0.Y + ny * arcRadius * side);
                    double startAngle = Math.Atan2(p0.Y - centre.Y, p0.X - centre.X);
                    double sweep = 2 * Math.Atan2(Math.Abs(lateral), along) * side; // the inscribed-angle relation: always under 180 degrees
                    var arc = new ArcSegment(centre, arcRadius, startAngle, sweep);
                    return Leg(v, start, back, reference, new Path2(new List<Segment> { arc }, false), left: back ? side < 0 : side > 0, sweep: Math.Abs(sweep), straight: 0);
                }
            }

            DriveLeg? best = null;
            foreach (double side in new[] { 1.0, -1.0 })
            {
                // Turning circle on this side of the direction of travel.
                var centre = new Point2(p0.X + nx * refRadius * side, p0.Y + ny * refRadius * side);
                double dx = target.X - centre.X, dy = target.Y - centre.Y;
                double d = Math.Sqrt(dx * dx + dy * dy);
                if (d < refRadius + 1e-9)
                {
                    continue; // inside this circle
                }

                double phi = Math.Atan2(dy, dx);
                double beta = Math.Acos(Math.Min(1, refRadius / d));
                double startAngle = Math.Atan2(p0.Y - centre.Y, p0.X - centre.X);
                double exitAngle = side > 0 ? phi - beta : phi + beta;
                double sweep = side > 0 ? Angles.Normalize(exitAngle - startAngle) : -Angles.Normalize(startAngle - exitAngle);
                if (Math.Abs(sweep) > maxSweep)
                {
                    continue; // that would be a loop
                }

                double straight = Math.Sqrt(Math.Max(0, d * d - refRadius * refRadius));
                var segments = new List<Segment>();
                if (Math.Abs(sweep) > 1e-6)
                {
                    segments.Add(new ArcSegment(centre, refRadius, startAngle, sweep));
                }

                Point2 exit = segments.Count > 0 ? segments[0].End : p0;
                if (straight > 1e-6)
                {
                    segments.Add(new LineSegment(exit, target));
                }

                if (segments.Count == 0)
                {
                    continue;
                }

                DriveLeg leg = Leg(v, start, back, reference, new Path2(segments, false), left: back ? side < 0 : side > 0, sweep: Math.Abs(sweep), straight: straight);
                if (best == null || leg.Length < best.Length)
                {
                    best = leg;
                }
            }

            return best;
        }

        private static DriveLeg Leg(DesignVehicle v, VehiclePose start, bool back, VehiclePoint reference, Path2 path, bool left, double sweep, double straight)
        {
            var leg = new DriveLeg
            {
                Request = new TurnPathRequest { Vehicle = v, Path = path, Reference = reference, Reverse = back, InitialHeading = start.Heading },
                Reverse = back,
                Left = left,
                ArcDeg = sweep * 180 / Math.PI,
                StraightLength = straight,
            };
            if (leg.ArcDeg > 0.5)
            {
                leg.Moves.Add($"turn {(left ? "left" : "right")} {leg.ArcDeg:0}°");
            }

            if (straight > 0.005)
            {
                leg.Moves.Add($"{straight:0.0} m straight");
            }

            return leg;
        }

        /// <summary>
        /// The leg that brings the vehicle to <paramref name="target"/> facing <paramref name="arrivalHeading"/>:
        /// a turn at the turn radius, a straight, and a second turn (either may be left or right, or
        /// vanish). The rear axle follows the path, so the vehicle arrives exactly on the heading.
        /// Forwards the front axle centre arrives at the target, in reverse the rear axle centre;
        /// both are tried and the shorter is used unless <paramref name="reverse"/> says. No turn is
        /// more than <see cref="MaxLegTurnDeg"/>, and two turns the same way together no more than
        /// that, so there are no loops: null when that can't be done in one leg.
        ///
        /// A vehicle with trailers reverses its last trailer's axle group to the target, with the
        /// combination in line on the heading: see <see cref="ReverseTowed"/>.
        /// <paramref name="trailerHeadings"/> is which way each trailer faces at the start (radians,
        /// front to back; null: straight behind).
        /// </summary>
        public static DriveLeg? DriveToPose(DesignVehicle v, VehiclePose start, Point2 target, double arrivalHeading, double? outerWheelRadius = null, bool? reverse = null,
            IReadOnlyList<double>? trailerHeadings = null)
        {
            double radius = outerWheelRadius ?? v.MinTurnRadius;
            if (radius < v.MinTurnRadius - 1e-9)
            {
                throw new ArgumentException($"The turn radius can't be less than the {v.Name}'s minimum of {v.MinTurnRadius:0.#} m.");
            }

            double rho = v.RearAxleRadius(radius);
            DriveLeg? best = null;
            foreach (bool back in reverse is bool forced ? new[] { forced } : new[] { false, true })
            {
                DriveLeg? leg;
                if (back && v.IsArticulated)
                {
                    leg = ReverseTowed(v, start, trailerHeadings, target, arrivalHeading, radius, DriveTurns.Arc);
                }
                else
                {
                    Point2 goal = back ? target : new Point2(target.X - v.Wheelbase * Math.Cos(arrivalHeading), target.Y - v.Wheelbase * Math.Sin(arrivalHeading));
                    double flip = back ? Math.PI : 0;
                    leg = TurnStraightTurn(v, start, start.RearAxle, start.Heading + flip, goal, arrivalHeading + flip, rho, back);
                    if (leg != null)
                    {
                        leg.Request.InitialTrailerHeadings = trailerHeadings;
                    }
                }

                if (leg != null && (best == null || leg.Length < best.Length - 1e-9))
                {
                    best = leg;
                }
            }

            return best;
        }

        /// <summary>The shortest turn-straight-turn path between two positions and directions of travel, within the turn caps.</summary>
        private static DriveLeg? TurnStraightTurn(DesignVehicle v, VehiclePose start, Point2 p0, double t0, Point2 p1, double t1, double rho, bool back)
        {
            Route? route = TurnStraightTurnRoute(p0, t0, p1, t1, rho);
            return route == null ? null : route.ToLeg(new TurnPathRequest { Vehicle = v, Path = route.Path(), Reference = v.RearAxleCentre, Reverse = back, InitialHeading = start.Heading }, back);
        }

        /// <summary>A leg's path as it is put together: the segments, and each move along it.</summary>
        private sealed class Route
        {
            public List<Segment> Segments { get; } = new List<Segment>();

            /// <summary>The moves in order: a turn (radians, positive = to the left of the direction of travel) or a straight (m).</summary>
            public List<(bool Turn, double Amount)> Moves { get; } = new List<(bool Turn, double Amount)>();

            public double Length => Segments.Sum(x => x.Length);

            public Point2 End => Segments[Segments.Count - 1].End;

            public Path2 Path() => new Path2(new List<Segment>(Segments), false);

            public void Turn(ArcSegment arc)
            {
                Segments.Add(arc);
                if (Moves.Count > 0 && Moves[Moves.Count - 1].Turn && Math.Sign(Moves[Moves.Count - 1].Amount) == Math.Sign(arc.Sweep))
                {
                    Moves[Moves.Count - 1] = (true, Moves[Moves.Count - 1].Amount + arc.Sweep);   // carries straight on round
                }
                else
                {
                    Moves.Add((true, arc.Sweep));
                }
            }

            public void Straight(LineSegment line)
            {
                Segments.Add(line);
                Moves.Add((false, line.Length));
            }

            public void Then(Route rest)
            {
                foreach (Segment x in rest.Segments)
                {
                    if (x is ArcSegment arc)
                    {
                        Turn(arc);
                    }
                    else
                    {
                        Straight((LineSegment)x);
                    }
                }
            }

            public DriveLeg ToLeg(TurnPathRequest request, bool back)
            {
                // In reverse the driver's left is the right of the direction of travel.
                bool Left(double sweep) => back ? sweep < 0 : sweep > 0;
                var leg = new DriveLeg { Request = request, Reverse = back, Left = true };
                bool first = true;
                foreach ((bool turn, double amount) in Moves)
                {
                    if (turn)
                    {
                        double deg = Math.Abs(amount) * 180 / Math.PI;
                        leg.ArcDeg += deg;
                        if (first)
                        {
                            leg.Left = Left(amount);
                            first = false;
                        }

                        if (deg > 0.5)
                        {
                            leg.Moves.Add($"turn {(Left(amount) ? "left" : "right")} {deg:0}°");
                        }
                    }
                    else
                    {
                        leg.StraightLength += amount;
                        if (amount > 0.005)
                        {
                            leg.Moves.Add($"{amount:0.0} m straight");
                        }
                    }
                }

                return leg;
            }
        }

        /// <param name="only">One of the four ways round (0 to 3: left or right first, left or right last); -1: the shortest.</param>
        private static Route? TurnStraightTurnRoute(Point2 p0, double t0, Point2 p1, double t1, double rho, int only = -1)
        {
            double maxSweep = MaxLegTurnDeg * Math.PI / 180;
            Route? best = null;
            foreach (double s0 in new[] { 1.0, -1.0 })
            {
                foreach (double s1 in new[] { 1.0, -1.0 })
                {
                    if (only >= 0 && only != (s0 > 0 ? 0 : 2) + (s1 > 0 ? 0 : 1))
                    {
                        continue;
                    }

                    var c0 = new Point2(p0.X - Math.Sin(t0) * rho * s0, p0.Y + Math.Cos(t0) * rho * s0);
                    var c1 = new Point2(p1.X - Math.Sin(t1) * rho * s1, p1.Y + Math.Cos(t1) * rho * s1);
                    double dx = c1.X - c0.X, dy = c1.Y - c0.Y;
                    double dist = Math.Sqrt(dx * dx + dy * dy);
                    double beta, straight;
                    if (s0 == s1)
                    {
                        beta = dist < 1e-9 ? t1 : Math.Atan2(dy, dx);
                        straight = dist;
                    }
                    else
                    {
                        if (dist < 2 * rho)
                        {
                            continue; // the circles overlap: no crossing tangent
                        }

                        double gamma = Math.Asin(Math.Min(1, 2 * rho / dist));
                        beta = Math.Atan2(dy, dx) + gamma * s0;
                        straight = Math.Sqrt(Math.Max(0, dist * dist - 4 * rho * rho));
                    }

                    double sweep0 = Turn(t0, beta, s0), sweep1 = Turn(beta, t1, s1);
                    if (Math.Abs(sweep0) > maxSweep || Math.Abs(sweep1) > maxSweep || (s0 == s1 && Math.Abs(sweep0) + Math.Abs(sweep1) > maxSweep))
                    {
                        continue; // that would be a loop
                    }

                    var route = new Route();
                    Point2 at = p0;
                    if (Math.Abs(sweep0) > 1e-6)
                    {
                        var arc = new ArcSegment(c0, rho, Math.Atan2(p0.Y - c0.Y, p0.X - c0.X), sweep0);
                        route.Segments.Add(arc);
                        route.Moves.Add((true, sweep0));
                        at = arc.End;
                    }

                    if (straight > 1e-6)
                    {
                        var to = new Point2(at.X + Math.Cos(beta) * straight, at.Y + Math.Sin(beta) * straight);
                        route.Straight(new LineSegment(at, to));
                        at = to;
                    }

                    if (Math.Abs(sweep1) > 1e-6)
                    {
                        var arc = new ArcSegment(c1, rho, Math.Atan2(at.Y - c1.Y, at.X - c1.X), sweep1);
                        route.Segments.Add(arc);
                        route.Moves.Add((true, sweep1));
                        at = arc.End;
                    }

                    if (route.Segments.Count == 0 || Segment.Dist(at, p1) > 1e-4)
                    {
                        continue;
                    }

                    double length = route.Length;
                    if (length < 0.05 || (best != null && length >= best.Length))
                    {
                        continue;
                    }

                    best = route;
                }
            }

            return best;
        }

        /// <summary>
        /// How much of the steering the turn radius allows a reverse with trailers is planned to use.
        /// The rest is kept in hand: a trailer can't be held round a turn in reverse at full lock,
        /// because there would be nothing left to steer it back with.
        /// </summary>
        public const double ReverseTowedLock = 0.85;

        /// <summary>
        /// A reverse with trailers has to start with the trailers where it needs them. This much out
        /// (degrees, over all the couplings) is steered out on the way; more, and there is no leg.
        /// </summary>
        public const double MaxTrailersOutDeg = 0.5;

        /// <summary>The most the vehicle swings its front out at the start of a reverse to set its trailers up.</summary>
        public const double MaxSwingDeg = 75;

        /// <summary>
        /// The leg that reverses a vehicle with trailers so that its last trailer's axle group
        /// arrives at <paramref name="target"/> with the combination in line: facing
        /// <paramref name="arrivalHeading"/> when given (turn, straight, pivot turn: see
        /// <see cref="PivotTurns"/>), otherwise however a turn then a straight to the target leaves
        /// it (or a single arc, with <see cref="DriveTurns.Arc"/>, when its radius allows).
        ///
        /// The path is the one the vehicle's rear axle is steered along. A trailer only goes round a
        /// turn in reverse if it is already folded towards it, by as much as it would be had the
        /// combination driven out of the turn forwards. So the leg may start with a swing: the
        /// vehicle turns its front away from the turn, which folds the trailers into it, before it
        /// follows them round. The swing is the length that leaves the trailers folded as they are
        /// now, so the reverse carries straight on from where the last leg stopped. Null when the
        /// target can't be reached that way in one leg.
        /// </summary>
        public static DriveLeg? ReverseTowed(DesignVehicle v, VehiclePose start, IReadOnlyList<double>? trailerHeadings, Point2 target, double? arrivalHeading, double radius, DriveTurns turns = DriveTurns.Arc)
        {
            if (!v.IsArticulated)
            {
                throw new ArgumentException($"The {v.Name} doesn't tow a trailer.");
            }

            double rho = v.RearAxleRadius(radius) / ReverseTowedLock;
            double reach = v.TowedLength;
            List<double> facing = v.TrailerPoses(start, trailerHeadings).Select(p => p.Heading).ToList();
            Point2 p0 = start.RearAxle;
            double t0 = start.Heading + Math.PI;
            int ways = arrivalHeading != null ? 4 : 3;   // the ways round to the target: see TowedToPose, TurnStraightTurnRoute and TowedTo

            // Onto a heading, the last turn is a pivot: the combination backs up to it in line, swings
            // out, and follows the trailers round. Only where no such leg fits is the turn a plain
            // one, which the trailers have to drift out for all the way down the straight before it.
            PivotTurns? pivots = arrivalHeading != null ? PivotTurns.For(v, rho) : null;
            bool pivot = pivots != null;

            // The leg with these swings first, one after the other: each a length of arc, positive to
            // the left of the direction of travel. "way" picks one way round to the target; -1 the shortest.
            Route? Plan(double first, double second, int way)
            {
                var route = new Route();
                Point2 p = p0;
                double t = t0;
                foreach (double swing in new[] { first, second })
                {
                    if (Math.Abs(swing) > 1e-6)
                    {
                        double side = Math.Sign(swing);
                        var centre = new Point2(p.X - Math.Sin(t) * rho * side, p.Y + Math.Cos(t) * rho * side);
                        var arc = new ArcSegment(centre, rho, Math.Atan2(p.Y - centre.Y, p.X - centre.X), swing / rho);
                        route.Turn(arc);
                        p = arc.End;
                        t += swing / rho;
                    }
                }

                Route? rest;
                if (arrivalHeading is double h)
                {
                    var goal = new Point2(target.X + reach * Math.Cos(h), target.Y + reach * Math.Sin(h));
                    rest = pivot ? TowedToPose(p, t, goal, h + Math.PI, rho, pivots!, way) : TurnStraightTurnRoute(p, t, goal, h + Math.PI, rho, way);
                }
                else
                {
                    rest = TowedTo(p, t, target, reach, rho, turns, way);
                }

                if (rest == null)
                {
                    return null;
                }

                route.Then(rest);
                if (route.Length < 0.05 || route.Moves.Any(m => m.Turn && Math.Abs(m.Amount) > MaxLegTurnDeg * Math.PI / 180 + 1e-9))
                {
                    return null;   // nowhere to go, or a swing that runs on into a loop
                }

                return route;
            }

            // How far the trailers are from where a route needs them at the start (radians, root sum of squares).
            double Miss(Route? route)
            {
                if (route == null)
                {
                    return double.MaxValue;
                }

                Path2 path = route.Path();
                List<double> needs = TurnPathSimulator.TrailerHeadingsToReverse(v, path, Math.Max(0.25, path.Length / 150));
                double sum = 0;
                for (int k = 0; k < needs.Count; k++)
                {
                    double d = Angles.Wrap(needs[k] - facing[k]);
                    sum += d * d;
                }

                return Math.Sqrt(sum);
            }

            double most = rho * MaxSwingDeg * Math.PI / 180;

            // The best so far: the shortest of the routes that leave the trailers where they are;
            // failing one, the route that leaves them nearest.
            const double Near = 0.1 * Math.PI / 180;
            Route? best = null;
            double bestMiss = double.MaxValue;
            void Offer(Route? route, double miss)
            {
                if (route == null)
                {
                    return;
                }

                bool near = miss <= Near, found = bestMiss <= Near;
                if (best == null || (near && !found) || (near == found && (near ? route.Length < best.Length - 1e-9 : miss < bestMiss)))
                {
                    best = route;
                    bestMiss = miss;
                }
            }

            void Seek()
            {
                Route? direct = Plan(0, 0, -1);
                Offer(direct, Miss(direct));
                if (bestMiss > Near)
                {
                    // One swing, either way, for each way round: where the miss dips among its neighbours,
                    // close in on the dip (a golden-section search).
                    const int Steps = 16;
                    const double Golden = 0.6180339887498949;
                    var miss = new double[2 * Steps + 1];
                    for (int way = 0; way < ways; way++)
                    {
                        for (int i = 0; i <= 2 * Steps; i++)
                        {
                            miss[i] = Miss(Plan((i - Steps) * most / Steps, 0, way));
                        }

                        for (int i = 0; i <= 2 * Steps; i++)
                        {
                            if (miss[i] == double.MaxValue || (i > 0 && miss[i] > miss[i - 1]) || (i < 2 * Steps && miss[i] > miss[i + 1]))
                            {
                                continue;
                            }

                            double lo = (Math.Max(0, i - 1) - Steps) * most / Steps, hi = (Math.Min(2 * Steps, i + 1) - Steps) * most / Steps;
                            double a = hi - Golden * (hi - lo), b = lo + Golden * (hi - lo);
                            Route? ra = Plan(a, 0, way), rb = Plan(b, 0, way);
                            double ma = Miss(ra), mb = Miss(rb);
                            for (int k = 0; k < 18; k++)
                            {
                                if (ma <= mb)
                                {
                                    hi = b;
                                    b = a;
                                    rb = ra;
                                    mb = ma;
                                    a = hi - Golden * (hi - lo);
                                    ra = Plan(a, 0, way);
                                    ma = Miss(ra);
                                }
                                else
                                {
                                    lo = a;
                                    a = b;
                                    ra = rb;
                                    ma = mb;
                                    b = lo + Golden * (hi - lo);
                                    rb = Plan(b, 0, way);
                                    mb = Miss(rb);
                                }
                            }

                            Offer(ma <= mb ? ra : rb, Math.Min(ma, mb));
                            Offer(Plan((i - Steps) * most / Steps, 0, way), miss[i]);
                        }
                    }
                }

                if (bestMiss > Near)
                {
                    // One swing sets one coupling, when there is room for it. With more trailers, or
                    // without the room, swing out and back: try pairs of swings, then walk downhill from
                    // the best pair in ever smaller steps.
                    const int Steps = 6;
                    var pairs = new List<(double Miss, double First, double Second)>();
                    for (int i = -Steps; i <= Steps; i++)
                    {
                        for (int k = -Steps; k <= Steps; k++)
                        {
                            if (i == 0 || k == 0 || Math.Sign(i) == Math.Sign(k))
                            {
                                continue;   // one swing, or two the same way: already tried
                            }

                            double m = Miss(Plan(i * most / Steps, k * most / Steps, -1));
                            if (m < double.MaxValue)
                            {
                                pairs.Add((m, i * most / Steps, k * most / Steps));
                            }
                        }
                    }

                    foreach ((double from, double first, double second) in pairs.OrderBy(x => x.Miss).Take(3))
                    {
                        double c1 = first, c2 = second, least = from;
                        for (double stride = most / Steps / 2; least > Near / 4 && stride > 0.002; stride /= 2)
                        {
                            if (stride < 0.1 && least > 20 * Near)
                            {
                                break;   // this close in and still degrees out: there is nothing here
                            }

                            for (bool moved = true; moved;)
                            {
                                moved = false;
                                foreach ((double d1, double d2) in new[] { (stride, 0.0), (-stride, 0.0), (0.0, stride), (0.0, -stride), (stride, stride), (-stride, -stride), (stride, -stride), (-stride, stride) })
                                {
                                    double m = Miss(Plan(c1 + d1, c2 + d2, -1));
                                    if (m < least)
                                    {
                                        least = m;
                                        c1 += d1;
                                        c2 += d2;
                                        moved = true;
                                    }
                                }
                            }
                        }

                        Offer(Plan(c1, c2, -1), least);
                        if (least <= Near)
                        {
                            break;
                        }
                    }
                }
            }

            Seek();
            if (pivot && (best == null || bestMiss > MaxTrailersOutDeg * Math.PI / 180))
            {
                pivot = false;
                Seek();
            }

            // A leg that doesn't start where the trailers are isn't one the vehicle can drive.
            if (best == null || bestMiss > MaxTrailersOutDeg * Math.PI / 180)
            {
                return null;
            }

            DriveLeg leg = best.ToLeg(
                new TurnPathRequest { Vehicle = v, Path = best.Path(), Reference = v.RearAxleCentre, Reverse = true, InitialHeading = start.Heading, InitialTrailerHeadings = facing, TurnRadius = radius },
                back: true);
            leg.TrailersOutDeg = bestMiss * 180 / Math.PI;
            return leg;
        }

        /// <summary>
        /// The turns a combination can reverse round between two straights, in line on both: the
        /// way a driver backs a trailer round a corner. Backing up in line, the vehicle first swings
        /// its front out, away from the turn, which folds the trailers into the turn; they pivot
        /// about their axles; and the vehicle follows them round until everything is in line again.
        /// With two trailers the swing is out and back, to fold both couplings.
        ///
        /// For each angle of turn the swings are the ones that make that true, found by trying the
        /// turn (see <see cref="TurnPathSimulator.TrailerHeadingsToReverse"/>). They depend only on
        /// the combination and the radius, so they are worked out once and kept.
        /// </summary>
        private sealed class PivotTurns
        {
            private const double StepDeg = 5;
            private static readonly Dictionary<string, PivotTurns> Known = new Dictionary<string, PivotTurns>();

            private readonly double _rho;

            // Swing lengths for a turn to the left of the direction of travel, every StepDeg from 0
            // (positive = to the left). NaN where the combination can't make the turn this way.
            private readonly double[] _first;
            private readonly double[] _second;

            private PivotTurns(DesignVehicle v, double rho)
            {
                _rho = rho;
                int n = (int)Math.Round(MaxLegTurnDeg / StepDeg) + 1;
                _first = new double[n];
                _second = new double[n];
                double most = rho * MaxSwingDeg * Math.PI / 180;
                bool two = v.Trailers.Count > 1;
                (double, double)[] moves = two
                    ? new[] { (1.0, 0.0), (-1.0, 0.0), (0.0, 1.0), (0.0, -1.0), (1.0, 1.0), (-1.0, -1.0), (1.0, -1.0), (-1.0, 1.0) }
                    : new[] { (1.0, 0.0), (-1.0, 0.0) };

                // How far out of line the trailers would have to start for the turn to end in line (radians).
                double Miss(double alpha, double c1, double c2)
                {
                    double sweep = alpha - (c1 + c2) / rho;
                    if (sweep < 1e-6 || Math.Abs(c1) > most || Math.Abs(c2) > most)
                    {
                        return double.MaxValue;
                    }

                    var route = new Route();
                    Append(route, new Point2(0, 0), 0, rho, c1, c2, sweep);
                    Path2 path = route.Path();
                    double sum = 0;
                    foreach (double heading in TurnPathSimulator.TrailerHeadingsToReverse(v, path, Math.Max(0.25, path.Length / 150)))
                    {
                        double d = Angles.Wrap(heading - Math.PI);
                        sum += d * d;
                    }

                    return Math.Sqrt(sum);
                }

                // Each angle starts from the swings of the angle before, and walks downhill from there
                // in ever smaller steps: the swings grow smoothly with the angle.
                const double Near = 0.03 * Math.PI / 180, Usable = 0.25 * Math.PI / 180;
                double c1 = 0, c2 = 0;
                for (int i = 1; i < n; i++)
                {
                    double alpha = i * StepDeg * Math.PI / 180;
                    double least = Miss(alpha, c1, c2);
                    for (double stride = 1.0; least > Near && stride > 0.0005; stride /= 2)
                    {
                        for (bool moved = true; moved && least > Near;)
                        {
                            moved = false;
                            foreach ((double d1, double d2) in moves)
                            {
                                double m = Miss(alpha, c1 + d1 * stride, c2 + d2 * stride);
                                if (m < least)
                                {
                                    least = m;
                                    c1 += d1 * stride;
                                    c2 += d2 * stride;
                                    moved = true;
                                }
                            }
                        }
                    }

                    _first[i] = least <= Usable ? c1 : double.NaN;
                    _second[i] = least <= Usable ? c2 : double.NaN;
                }
            }

            public static PivotTurns For(DesignVehicle v, double rho)
            {
                string key = string.Join("|", new[] { rho, v.Hitch }.Concat(v.Trailers.SelectMany(t => new[] { t.Wheelbase, t.Hitch }))
                    .Select(x => x.ToString("R", System.Globalization.CultureInfo.InvariantCulture)));
                lock (Known)
                {
                    if (!Known.TryGetValue(key, out PivotTurns? turns))
                    {
                        turns = new PivotTurns(v, rho);
                        Known[key] = turns;
                    }

                    return turns;
                }
            }

            /// <summary>
            /// The swings (lengths of arc, positive to the left of travel) and the sweep of the arc that
            /// follows them, for a turn of <paramref name="alpha"/> radians to one side (+1 left, -1 right).
            /// </summary>
            public bool Swings(double alpha, double side, out double first, out double second, out double sweep)
            {
                first = second = sweep = 0;
                double at = alpha * 180 / Math.PI / StepDeg;
                int i = (int)Math.Floor(at);
                if (alpha < 0 || i >= _first.Length - 1 && at > _first.Length - 1)
                {
                    return false;
                }

                i = Math.Min(i, _first.Length - 2);
                double f = at - i;
                if (double.IsNaN(_first[i]) || double.IsNaN(_first[i + 1]))
                {
                    return false;
                }

                first = side * (_first[i] + (_first[i + 1] - _first[i]) * f);
                second = side * (_second[i] + (_second[i + 1] - _second[i]) * f);
                sweep = side * alpha - (first + second) / _rho;
                return alpha < 1e-9 || sweep * side > 1e-6;
            }

            /// <summary>Where a turn made of these swings and this sweep ends, from a point and direction of travel.</summary>
            public static void End(double rho, ref double x, ref double y, ref double t, double first, double second, double sweep)
            {
                foreach (double w in new[] { first / rho, second / rho, sweep })
                {
                    if (Math.Abs(w) > 1e-12)
                    {
                        double side = Math.Sign(w);
                        x += side * rho * (Math.Sin(t + w) - Math.Sin(t));
                        y += side * rho * (Math.Cos(t) - Math.Cos(t + w));
                        t += w;
                    }
                }
            }

            /// <summary>Adds a turn made of these swings and this sweep to a route, from a point and direction of travel.</summary>
            public static void Append(Route route, Point2 p, double t, double rho, double first, double second, double sweep)
            {
                foreach (double w in new[] { first / rho, second / rho, sweep })
                {
                    if (Math.Abs(w) > 1e-9)
                    {
                        double side = Math.Sign(w);
                        var centre = new Point2(p.X - Math.Sin(t) * rho * side, p.Y + Math.Cos(t) * rho * side);
                        var arc = new ArcSegment(centre, rho, Math.Atan2(p.Y - centre.Y, p.X - centre.X), w);
                        route.Turn(arc);
                        p = arc.End;
                        t += w;
                    }
                }
            }
        }

        /// <summary>
        /// The route of a towing vehicle's rear axle, backing from a point and direction of travel
        /// to a goal and direction of travel, that a combination can reverse: a turn at the radius,
        /// a straight, and a pivot turn (<see cref="PivotTurns"/>) that ends on the goal. On the
        /// straight, and at the goal, the combination is in line.
        /// </summary>
        /// <param name="only">One of the four ways round (0 to 3: left or right first, left or right last); -1: the shortest.</param>
        private static Route? TowedToPose(Point2 p0, double t0, Point2 goal, double tg, double rho, PivotTurns pivots, int only = -1)
        {
            double maxSweep = MaxLegTurnDeg * Math.PI / 180;
            const double Scan = 2.5 * Math.PI / 180;
            Route? best = null;
            foreach (double s0 in new[] { 1.0, -1.0 })
            {
                foreach (double s1 in new[] { 1.0, -1.0 })
                {
                    if (only >= 0 && only != (s0 > 0 ? 0 : 2) + (s1 > 0 ? 0 : 1))
                    {
                        continue;
                    }

                    // With the pivot turning by "last", the first turn has to bring the vehicle onto the
                    // pivot's way in: how far to the side of it the first turn leaves it (to make zero),
                    // and how far short of it (the straight).
                    bool At(double last, out double aside, out double straight, out double firstTurn)
                    {
                        aside = straight = firstTurn = 0;
                        if (!pivots.Swings(last, s1, out double c1, out double c2, out double sweep))
                        {
                            return false;
                        }

                        double ex = 0, ey = 0, et = 0;
                        PivotTurns.End(rho, ref ex, ref ey, ref et, c1, c2, sweep);
                        double te = tg - et;
                        double entryX = goal.X - (ex * Math.Cos(te) - ey * Math.Sin(te)), entryY = goal.Y - (ex * Math.Sin(te) + ey * Math.Cos(te));
                        firstTurn = Turn(t0, te, s0);
                        if (Math.Abs(firstTurn) > maxSweep)
                        {
                            return false;
                        }

                        double x = p0.X, y = p0.Y, t = t0;
                        PivotTurns.End(rho, ref x, ref y, ref t, 0, 0, firstTurn);
                        aside = -(entryX - x) * Math.Sin(te) + (entryY - y) * Math.Cos(te);
                        straight = (entryX - x) * Math.Cos(te) + (entryY - y) * Math.Sin(te);
                        return true;
                    }

                    void Take(double last)
                    {
                        if (!At(last, out double aside, out double straight, out double firstTurn) || Math.Abs(aside) > 1e-4 || straight < -1e-6)
                        {
                            return;
                        }

                        pivots.Swings(last, s1, out double c1, out double c2, out double sweep);
                        var route = new Route();
                        Point2 p = p0;
                        double t = t0;
                        if (Math.Abs(firstTurn) > 1e-6)
                        {
                            PivotTurns.Append(route, p, t, rho, 0, 0, firstTurn);
                            p = route.End;
                            t += firstTurn;
                        }

                        if (straight > 1e-6)
                        {
                            var to = new Point2(p.X + Math.Cos(t) * straight, p.Y + Math.Sin(t) * straight);
                            route.Straight(new LineSegment(p, to));
                            p = to;
                        }

                        PivotTurns.Append(route, p, t, rho, c1, c2, sweep);
                        if (route.Segments.Count == 0 || Segment.Dist(route.End, goal) > 1e-3)
                        {
                            return;
                        }

                        double length = route.Length;
                        if (length >= 0.05 && (best == null || length < best.Length))
                        {
                            best = route;
                        }
                    }

                    // Look along the pivot's angle for where the first turn lines up with it, and close in.
                    bool was = false;
                    double wasAside = 0, wasTurn = 0;
                    for (int i = 0; i * Scan <= maxSweep + 1e-9; i++)
                    {
                        double last = i * Scan;
                        bool ok = At(last, out double aside, out _, out double firstTurn);
                        if (ok && Math.Abs(aside) < 1e-9)
                        {
                            Take(last);
                        }
                        else if (ok && was && aside * wasAside < 0 && Math.Abs(firstTurn - wasTurn) < 0.5)
                        {
                            double lo = last - Scan, hi = last, loAside = wasAside;
                            for (int k = 0; k < 40; k++)
                            {
                                double mid = (lo + hi) / 2;
                                if (!At(mid, out double midAside, out _, out _))
                                {
                                    break;
                                }

                                if (midAside * loAside <= 0)
                                {
                                    hi = mid;
                                }
                                else
                                {
                                    lo = mid;
                                    loAside = midAside;
                                }
                            }

                            Take((lo + hi) / 2);
                        }

                        was = ok;
                        wasAside = aside;
                        wasTurn = firstTurn;
                    }
                }
            }

            return best;
        }

        /// <summary>
        /// The route of a towing vehicle's rear axle, from a point and direction of travel, that
        /// brings a point <paramref name="reach"/> ahead of it along its direction of travel (the
        /// last trailer's axle group, in line) to the target: one arc when its radius allows and
        /// <paramref name="turns"/> says, otherwise a turn at the radius then straight at the target.
        /// </summary>
        /// <param name="only">0: the single arc; 1 or 2: a turn to the left or right, then straight; -1: the arc when there is one, otherwise the shorter turn.</param>
        private static Route? TowedTo(Point2 p0, double travel, Point2 target, double reach, double rho, DriveTurns turns, int only = -1)
        {
            double ux = Math.Cos(travel), uy = Math.Sin(travel);
            double nx = -uy, ny = ux;
            double dxT = target.X - p0.X, dyT = target.Y - p0.Y;
            double along = dxT * ux + dyT * uy, lateral = dxT * nx + dyT * ny;
            double maxSweep = MaxLegTurnDeg * Math.PI / 180;

            if (Math.Abs(lateral) < 1e-9)
            {
                if (along < reach + 0.05)
                {
                    return null;
                }

                var straight = new Route();
                straight.Straight(new LineSegment(p0, new Point2(p0.X + ux * (along - reach), p0.Y + uy * (along - reach))));
                return straight;
            }

            if (turns == DriveTurns.Arc && along > 1e-9 && only <= 0)
            {
                // The arc, tangent to the direction of travel, that ends pointing at the target from
                // "reach" away: the target is then sqrt(r^2 + reach^2) from the arc's centre.
                double d2 = along * along + lateral * lateral - reach * reach;
                double arcRadius = d2 / (2 * Math.Abs(lateral));
                if (d2 > 0 && arcRadius >= rho - 1e-9)
                {
                    double side = Math.Sign(lateral);
                    var centre = new Point2(p0.X + nx * arcRadius * side, p0.Y + ny * arcRadius * side);
                    double startAngle = Math.Atan2(p0.Y - centre.Y, p0.X - centre.X);
                    double endAngle = Math.Atan2(target.Y - centre.Y, target.X - centre.X) - side * Math.Atan2(reach, arcRadius);
                    double sweep = side > 0 ? Angles.Normalize(endAngle - startAngle) : -Angles.Normalize(startAngle - endAngle);
                    if (Math.Abs(sweep) > 1e-6 && Math.Abs(sweep) < Math.PI && arcRadius * Math.Abs(sweep) >= 0.05)
                    {
                        var one = new Route();
                        one.Turn(new ArcSegment(centre, arcRadius, startAngle, sweep));
                        return one;
                    }
                }
            }

            Route? best = null;
            foreach (double side in new[] { 1.0, -1.0 })
            {
                if (only == 0 || (only > 0 && only != (side > 0 ? 1 : 2)))
                {
                    continue;
                }

                var centre = new Point2(p0.X + nx * rho * side, p0.Y + ny * rho * side);
                double dx = target.X - centre.X, dy = target.Y - centre.Y;
                double d = Math.Sqrt(dx * dx + dy * dy);
                double straight = Math.Sqrt(Math.Max(0, d * d - rho * rho)) - reach;
                if (d < rho + 1e-9 || straight < 0)
                {
                    continue; // inside this circle, or too close to line up on
                }

                double phi = Math.Atan2(dy, dx);
                double beta = Math.Acos(Math.Min(1, rho / d));
                double startAngle = Math.Atan2(p0.Y - centre.Y, p0.X - centre.X);
                double exitAngle = side > 0 ? phi - beta : phi + beta;
                double sweep = side > 0 ? Angles.Normalize(exitAngle - startAngle) : -Angles.Normalize(startAngle - exitAngle);
                if (Math.Abs(sweep) > maxSweep)
                {
                    continue; // that would be a loop
                }

                var route = new Route();
                Point2 exit = p0;
                if (Math.Abs(sweep) > 1e-6)
                {
                    var arc = new ArcSegment(centre, rho, startAngle, sweep);
                    route.Turn(arc);
                    exit = arc.End;
                }

                if (straight > 1e-6)
                {
                    double len = straight + reach;
                    route.Straight(new LineSegment(exit, new Point2(exit.X + (target.X - exit.X) * straight / len, exit.Y + (target.Y - exit.Y) * straight / len)));
                }

                if (route.Segments.Count > 0 && route.Length >= 0.05 && (best == null || route.Length < best.Length))
                {
                    best = route;
                }
            }

            return best;
        }

        /// <summary>The signed angle turned going from one heading to another, turning left (side +1) or right (-1).</summary>
        private static double Turn(double from, double to, double side)
        {
            double a = side > 0 ? Angles.Normalize(to - from) : Angles.Normalize(from - to);
            if (a > 2 * Math.PI - 1e-7)
            {
                a = 0;
            }

            return a * side;
        }

        /// <summary>
        /// A path through points with a tangent arc at each corner. Corners that don't have room
        /// for the radius get a smaller one (reported in <paramref name="warnings"/>).
        /// </summary>
        public static Path2 FromPoints(IReadOnlyList<Point2> points, double cornerRadius, List<string>? warnings = null)
        {
            var pts = new List<Point2>();
            foreach (Point2 p in points)
            {
                if (pts.Count == 0 || Segment.Dist(pts[pts.Count - 1], p) > 1e-6)
                {
                    pts.Add(p);
                }
            }

            if (pts.Count < 2)
            {
                throw new ArgumentException("At least two distinct points are needed.");
            }

            if (cornerRadius < 0)
            {
                throw new ArgumentException("The corner radius can't be negative.");
            }

            var segments = new List<Segment>();
            Point2 cursor = pts[0];
            double usedOnIncoming = 0; // tangent length already taken from the current straight by the previous corner
            for (int i = 1; i < pts.Count - 1; i++)
            {
                Point2 a = pts[i - 1], b = pts[i], c = pts[i + 1];
                double d1x = b.X - a.X, d1y = b.Y - a.Y, l1 = Math.Sqrt(d1x * d1x + d1y * d1y);
                double d2x = c.X - b.X, d2y = c.Y - b.Y, l2 = Math.Sqrt(d2x * d2x + d2y * d2y);
                d1x /= l1;
                d1y /= l1;
                d2x /= l2;
                d2y /= l2;
                double turn = Math.Atan2(d1x * d2y - d1y * d2x, d1x * d2x + d1y * d2y);
                if (Math.Abs(turn) < 0.5 * Math.PI / 180 || cornerRadius < 1e-9)
                {
                    segments.Add(new LineSegment(cursor, b));
                    cursor = b;
                    usedOnIncoming = 0;
                    continue;
                }

                if (Math.PI - Math.Abs(turn) < 0.5 * Math.PI / 180)
                {
                    throw new ArgumentException($"Point {i + 1} doubles straight back on the previous one; a vehicle can't follow that.");
                }

                double radius = cornerRadius;
                double tangent = radius * Math.Tan(Math.Abs(turn) / 2);
                bool last = i == pts.Count - 2;
                double available = Math.Min(l1 - usedOnIncoming, last ? l2 : l2 / 2);
                if (tangent > available + 1e-9)
                {
                    radius = available / Math.Tan(Math.Abs(turn) / 2);
                    tangent = available;
                    warnings?.Add($"Corner at point {i + 1}: not enough room for a {cornerRadius:0.##} m radius; used {radius:0.##} m.");
                }

                var start = new Point2(b.X - d1x * tangent, b.Y - d1y * tangent);
                var end = new Point2(b.X + d2x * tangent, b.Y + d2y * tangent);
                if (Segment.Dist(cursor, start) > 1e-9)
                {
                    segments.Add(new LineSegment(cursor, start));
                }

                double side = Math.Sign(turn);
                var centre = new Point2(start.X - d1y * radius * side, start.Y + d1x * radius * side);
                segments.Add(new ArcSegment(centre, radius, Math.Atan2(start.Y - centre.Y, start.X - centre.X), turn));
                cursor = end;
                usedOnIncoming = tangent;
            }

            Point2 final = pts[pts.Count - 1];
            if (Segment.Dist(cursor, final) > 1e-9)
            {
                segments.Add(new LineSegment(cursor, final));
            }

            return new Path2(segments, false);
        }

        /// <summary>
        /// The path of an existing curve, run from the end nearest <paramref name="nearStart"/>
        /// (when given). Closed curves are opened at their first vertex.
        /// </summary>
        public static Path2 FromCurve(EntitySpec curve, Point2? nearStart)
        {
            Path2? path = Path2.From(curve);
            if (path == null)
            {
                throw new ArgumentException("That entity isn't a line, arc, circle or polyline.");
            }

            if (path.Closed)
            {
                path = new Path2(new List<Segment>(path.Segments), false);
            }

            if (nearStart is Point2 p && Segment.Dist(p, path.End) < Segment.Dist(p, path.Start))
            {
                path = PathOps.Reverse(path);
            }

            return path;
        }
    }
}
