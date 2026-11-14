using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using PointerTurn.Core.Drawing;
using PointerTurn.Core.Geometry;
using PointerTurn.Core.Vehicles;
using Xunit;

namespace PointerTurn.Core.Tests
{
    /// <summary>
    /// Reversing with trailers, with the shipped Austroads combinations. The yardstick is that
    /// driving is reversible: a reverse along a path leaves the tracks of the forward drive the
    /// other way along it.
    /// </summary>
    public class TrailerReversingTests
    {
        private static TurnPathConventions Conventions() =>
            TurnPathConventions.Load(Path.Combine(AppContext.BaseDirectory, "turn-paths.json"));

        private static DesignVehicle Semi() => Conventions().Vehicle("Semi 19 m");

        private static DesignVehicle BDouble() => Conventions().Vehicle("B-double 26 m");

        private static double Dist(Point2 a, Point2 b) => Segment.Dist(a, b);

        private static double AngleDiff(double a, double b) => Math.Abs(Math.Atan2(Math.Sin(a - b), Math.Cos(a - b)));

        /// <summary>A path for the rear axle, backing along +X from the origin: a straight, a turn to the left of travel, a straight.</summary>
        private static Path2 BackRoundACorner(double lead, double radius, double turnDeg, double tail)
        {
            var segments = new List<Segment>();
            var at = new Point2(lead, 0);
            if (lead > 0)
            {
                segments.Add(new LineSegment(new Point2(0, 0), at));
            }

            var arc = new ArcSegment(new Point2(lead, radius), radius, -Math.PI / 2, turnDeg * Math.PI / 180);
            segments.Add(arc);
            double h = turnDeg * Math.PI / 180;
            segments.Add(new LineSegment(arc.End, new Point2(arc.End.X + Math.Cos(h) * tail, arc.End.Y + Math.Sin(h) * tail)));
            return new Path2(segments, false);
        }

        private static double Area(TurnPathResult r)
        {
            var polygon = new List<Point2>(r.LeftBody);
            polygon.AddRange(Enumerable.Reverse(r.RightBody));
            double sum = 0;
            for (int i = 0, j = polygon.Count - 1; i < polygon.Count; j = i++)
            {
                sum += polygon[j].X * polygon[i].Y - polygon[i].X * polygon[j].Y;
            }

            return Math.Abs(sum) / 2;
        }

        private static List<double> InLine(DesignVehicle v, double heading) => v.Trailers.Select(_ => heading).ToList();

        [Theory]
        [InlineData("Semi 19 m")]
        [InlineData("B-double 26 m")]
        public void AReverseAlongAPathIsTheDriveTheOtherWayPlayedBackwards(string vehicle)
        {
            DesignVehicle v = Conventions().Vehicle(vehicle);
            Path2 path = BackRoundACorner(12, v.RearAxleRadius(15), 90, 25);
            TurnPathResult forward = TurnPathSimulator.Simulate(new TurnPathRequest { Vehicle = v, Path = PathOps.Reverse(path), Reference = v.RearAxleCentre });
            TurnPathResult back = TurnPathSimulator.Simulate(new TurnPathRequest { Vehicle = v, Path = path, Reference = v.FrontAxleCentre, Reverse = true });

            // The rear axle is steered along the path, whatever reference point was asked for.
            Assert.True(back.Reverse);
            Assert.Equal(v.RearAxleCentre.X, back.Reference.X);
            Assert.Equal(forward.Poses.Count, back.Poses.Count);
            int last = back.Poses.Count - 1;
            for (double s = 0; s <= path.Length; s += 0.5)
            {
                // The same place, the same way round, at the same point of the path: every unit.
                VehiclePose there = back.PoseAt(s), was = forward.PoseAt(path.Length - s);
                Assert.True(Dist(there.RearAxle, was.RearAxle) < 1e-4, $"rear axle at {s}: {Dist(there.RearAxle, was.RearAxle)}");
                Assert.True(AngleDiff(there.Heading, was.Heading) < 1e-3);   // sampled either side of where a turn starts
                List<VehiclePose> towed = back.TrailerPosesAt(s), wasTowed = forward.TrailerPosesAt(path.Length - s);
                for (int t = 0; t < v.Trailers.Count; t++)
                {
                    Assert.True(Dist(towed[t].RearAxle, wasTowed[t].RearAxle) < 1e-4, $"{v.Trailers[t].Name} at {s}: {Dist(towed[t].RearAxle, wasTowed[t].RearAxle)}");
                    Assert.True(AngleDiff(towed[t].Heading, wasTowed[t].Heading) < 1e-4);
                }
            }

            // So it finishes in line at the end of the path, having folded as far as the forward drive does...
            Assert.All(back.Trailers, t => Assert.True(AngleDiff(t.Poses[last].Heading, back.Poses[last].Heading) < 1e-9));
            Assert.InRange(back.MaxArticulationDeg, forward.MaxArticulationDeg - 0.05, forward.MaxArticulationDeg + 0.05);
            Assert.InRange(back.MaxArticulationDeg, 20, 60);
            Assert.Equal(0, back.MaxPathDeviation, 9);
            Assert.Equal(0, back.EndOffset, 9);
            Assert.Equal(path.Length, back.Travelled, 9);
            Assert.True(back.WithinMinimumRadius);
            Assert.Equal(90, back.NetTurnDeg, 3);    // it turns the way the path does, facing back along it

            // ...and sweeps the same ground, with the same clearances on the same sides.
            Assert.InRange(Area(back), Area(forward) - 0.3, Area(forward) + 0.3);
            Assert.True(Area(back) > path.Length * v.Width);
            Assert.Equal(forward.WheelPaths.Count, back.WheelPaths.Count);
            PolylineSpec outline = back.ClosedOutline();
            Assert.True(outline.Closed);
            Assert.All(outline.Bulges!, b => Assert.Equal(0, b));   // square trucks: square ends

            // It starts where that needs the trailers, which 12 m of straight isn't enough to make straight: it says so.
            Assert.True(AngleDiff(back.Trailers[0].Poses[0].Heading, back.Poses[0].Heading) > 0.05);
            Assert.Contains(back.Warnings, w => w.Contains("starts folded"));
            Assert.Equal(back.Trailers.Select(t => t.Poses[0].Heading), TurnPathSimulator.TrailerHeadingsToReverse(v, path));

            // Straight back along a line: nothing folds, and nothing to report.
            TurnPathResult straight = TurnPathSimulator.Simulate(new TurnPathRequest
            {
                Vehicle = v,
                Path = new Path2(new List<Segment> { new LineSegment(new Point2(0, 0), new Point2(30, 0)) }, false),
                Reference = v.RearAxleCentre,
                Reverse = true,
            });
            Assert.Empty(straight.Warnings);
            Assert.Equal(0, straight.MaxArticulationDeg, 9);
            Assert.Equal(new Point2(30, 0), straight.Poses[straight.Poses.Count - 1].RearAxle);
            Assert.Equal(Math.PI, straight.Poses[0].Heading, 9);
            Assert.True(Dist(straight.Trailers[v.Trailers.Count - 1].Poses[0].RearAxle, new Point2(v.TowedLength, 0)) < 1e-9, "the last trailer leads the way back");
        }

        [Theory]
        [InlineData("Semi 19 m", 30)]
        [InlineData("Semi 19 m", 18)]
        [InlineData("B-double 26 m", 45)]
        public void StartedOffItsLineTheVehicleSteersTheTrailersOntoIt(string vehicle, double lead)
        {
            // Started straight, when the path needs the trailers already folding into the turn ahead.
            DesignVehicle v = Conventions().Vehicle(vehicle);
            Path2 path = BackRoundACorner(lead, v.RearAxleRadius(15), 90, 30);
            List<double> needs = TurnPathSimulator.TrailerHeadingsToReverse(v, path);
            Assert.True(AngleDiff(needs[0], Math.PI) > 0.002);

            TurnPathResult r = TurnPathSimulator.Simulate(new TurnPathRequest
            {
                Vehicle = v,
                Path = path,
                Reference = v.RearAxleCentre,
                Reverse = true,
                InitialTrailerHeadings = InLine(v, Math.PI),
            });

            // It starts where it was put, moves a step at a time, and never steers past its lock.
            int last = r.Poses.Count - 1;
            Assert.Equal(new Point2(0, 0), r.Poses[0].RearAxle);
            Assert.All(r.Trailers, t => Assert.Equal(Math.PI, t.Poses[0].Heading, 9));
            Assert.True(Dist(r.Trailers[v.Trailers.Count - 1].Poses[0].RearAxle, new Point2(v.TowedLength, 0)) < 1e-9);
            for (int i = 1; i <= last; i++)
            {
                Assert.True(Dist(r.Poses[i].RearAxle, r.Poses[i - 1].RearAxle) <= 0.02 + 1e-9);
            }

            Assert.True(r.WithinMinimumRadius, $"tightest {r.TightestRadius}");
            Assert.True(r.MaxArticulationDeg < v.MaxArticulationDeg);

            // The vehicle leaves the path to push the trailers round (the less, the more straight it had to do it in)...
            Assert.InRange(r.MaxPathDeviation, 0.1, lead < 20 ? 4.0 : 1.5);

            // ...and brings everything back: on the path, in line, where the path ends.
            Assert.DoesNotContain(r.Warnings, w => w.Contains("Reversing"));
            Assert.True(r.EndOffset < 0.05, $"end offset {r.EndOffset}");
            Assert.True(Dist(r.Poses[last].RearAxle, path.End) < 0.1, $"{Dist(r.Poses[last].RearAxle, path.End)} m from the end of the path");
            Assert.All(r.Trailers, t => Assert.True(AngleDiff(t.Poses[last].Heading, r.Poses[last].Heading) < 0.01));
            Assert.Equal(r.Chainages[last], r.Travelled);
            Assert.InRange(r.Travelled, path.Length - 0.5, path.Length + 6);

            // The trailers are rigid: every coupling stays the trailer's length from its axles.
            for (int i = 0; i <= last; i += 50)
            {
                Point2 kingpin = r.Poses[i].ToWorld(v.Hitch, 0);
                Assert.Equal(v.Trailers[0].Wheelbase, Dist(kingpin, r.Trailers[0].Poses[i].RearAxle), 9);
            }

            // A coarse step (the live preview) gives the same manoeuvre.
            TurnPathResult coarse = TurnPathSimulator.Simulate(new TurnPathRequest
            {
                Vehicle = v,
                Path = path,
                Reference = v.RearAxleCentre,
                Reverse = true,
                InitialTrailerHeadings = InLine(v, Math.PI),
                Step = 0.3,
                Tolerance = 0.005,
            });
            Assert.True(Dist(coarse.Poses[coarse.Poses.Count - 1].RearAxle, r.Poses[last].RearAxle) < 0.15);
            Assert.Equal(r.MaxPathDeviation, coarse.MaxPathDeviation, 0);
        }

        [Fact]
        public void ATrailerThatCantBeBroughtRoundSaysSo()
        {
            // Started straight on a path that turns at once: the trailer would have to be folded 30 degrees already.
            DesignVehicle v = Semi();
            Path2 path = BackRoundACorner(0, v.RearAxleRadius(15), 60, 6);
            TurnPathResult r = TurnPathSimulator.Simulate(new TurnPathRequest
            {
                Vehicle = v,
                Path = path,
                Reference = v.RearAxleCentre,
                Reverse = true,
                InitialTrailerHeadings = InLine(v, Math.PI),
            });
            Assert.Contains(r.Warnings, w => w.StartsWith("Reversing"));
            Assert.True(r.WithinMinimumRadius);
            Assert.True(r.Travelled < 3 * path.Length, "it gives up rather than circling");
            Assert.NotEmpty(r.LeftBody);

            // A lock it can't steer inside is honoured too.
            TurnPathResult wide = TurnPathSimulator.Simulate(new TurnPathRequest
            {
                Vehicle = v,
                Path = BackRoundACorner(30, v.RearAxleRadius(25), 60, 30),
                Reference = v.RearAxleCentre,
                Reverse = true,
                InitialTrailerHeadings = InLine(v, Math.PI),
                TurnRadius = 20,
            });
            Assert.True(wide.TightestRadius >= 20 - 0.001, $"tightest {wide.TightestRadius}");
            Assert.True(wide.EndOffset < 0.05);
        }

        [Theory]
        [InlineData(12.5)]
        [InlineData(15)]
        public void ASemiReversesOntoADock(double radius)
        {
            // Parked along +X at the origin; the dock is behind it and to its right, entered facing +Y.
            DesignVehicle v = Semi();
            var start = new VehiclePose(new Point2(0, 0), 0);
            var dock = new Point2(-30, -25);
            TurnPathBuilder.DriveLeg leg = TurnPathBuilder.DriveToPose(v, start, dock, Math.PI / 2, radius)!;
            Assert.True(leg.Reverse);
            Assert.True(leg.TrailersOutDeg < 0.1);

            // It swings its front out first (the driver turns left), then follows the trailer round to the right.
            Assert.StartsWith("turn left", leg.Moves[0]);
            Assert.StartsWith("turn right", leg.Moves[1]);
            Assert.InRange(Math.Abs(((ArcSegment)leg.Request.Path.Segments[0]).Sweep) * 180 / Math.PI, 5, TurnPathBuilder.MaxSwingDeg);
            Assert.True(Dist(start.RearAxle, leg.Request.Path.Start) < 1e-12);
            Assert.Equal(radius, leg.Request.TurnRadius);

            TurnPathResult r = TurnPathSimulator.Simulate(leg.Request);
            int last = r.Poses.Count - 1;
            VehiclePose tail = r.Trailers[0].Poses[last];
            Assert.True(Dist(tail.RearAxle, dock) < 0.01, $"{Dist(tail.RearAxle, dock)} m from the dock");
            Assert.True(AngleDiff(tail.Heading, Math.PI / 2) < 0.002);
            Assert.True(AngleDiff(r.Poses[last].Heading, Math.PI / 2) < 0.002);
            Assert.True(Dist(r.Poses[last].RearAxle, new Point2(dock.X, dock.Y + v.TowedLength)) < 0.01);
            Assert.Empty(r.Warnings);

            // It started straight, as it was, and never steered tighter than the radius: the turns
            // are planned with steering in hand.
            Assert.True(Dist(r.Trailers[0].Poses[0].RearAxle, new Point2(-v.TowedLength, 0)) < 1e-9);
            Assert.Equal(0, r.Trailers[0].Poses[0].Heading, 9);
            Assert.True(r.TightestRadius > radius + 0.5, $"tightest {r.TightestRadius}");
            Assert.True(r.MaxPathDeviation < 0.02);
            Assert.InRange(r.MaxArticulationDeg, 25, 55);

            // The whole combination is inside the swept path at the dock.
            var polygon = new List<Point2>(r.LeftBody);
            polygon.AddRange(Enumerable.Reverse(r.RightBody));
            Assert.True(Inside(polygon, new Point2(dock.X, dock.Y - v.Trailers[0].RearOverhang + 0.1)), "the back of the trailer");
            Assert.True(Inside(polygon, new Point2(dock.X, dock.Y + v.TowedLength + v.Wheelbase + v.FrontOverhang - 0.1)), "the front of the prime mover");
            Assert.False(Inside(polygon, new Point2(dock.X, dock.Y - v.Trailers[0].RearOverhang - 0.1)));

            // The same leg under the cursor, at the preview's step.
            leg.Request.Step = 0.25;
            leg.Request.Tolerance = 0.005;
            TurnPathResult preview = TurnPathSimulator.Simulate(leg.Request);
            Assert.True(Dist(preview.Trailers[0].Poses[preview.Poses.Count - 1].RearAxle, dock) < 0.05);
        }

        private static bool Inside(List<Point2> polygon, Point2 p)
        {
            bool inside = false;
            for (int i = 0, j = polygon.Count - 1; i < polygon.Count; j = i++)
            {
                if ((polygon[i].Y > p.Y) != (polygon[j].Y > p.Y)
                    && p.X < (polygon[j].X - polygon[i].X) * (p.Y - polygon[i].Y) / (polygon[j].Y - polygon[i].Y) + polygon[i].X)
                {
                    inside = !inside;
                }
            }

            return inside;
        }

        [Fact]
        public void ABDoubleSwingsOutAndBackToSetBothTrailersUp()
        {
            DesignVehicle v = BDouble();
            var start = new VehiclePose(new Point2(0, 0), 0);
            var dock = new Point2(-40, -40);
            TurnPathBuilder.DriveLeg leg = TurnPathBuilder.DriveToPose(v, start, dock, Math.PI / 2, 12.5)!;
            Assert.True(leg.Reverse);
            Assert.True(leg.Moves.Count(m => m.StartsWith("turn")) >= 3, string.Join(", ", leg.Moves));
            Assert.True(leg.TrailersOutDeg < TurnPathBuilder.MaxTrailersOutDeg);

            TurnPathResult r = TurnPathSimulator.Simulate(leg.Request);
            int last = r.Poses.Count - 1;
            Assert.Equal(2, r.Trailers.Count);
            Assert.True(Dist(r.Trailers[1].Poses[last].RearAxle, dock) < 0.03, $"{Dist(r.Trailers[1].Poses[last].RearAxle, dock)} m from the dock");
            Assert.All(r.Trailers, t => Assert.True(AngleDiff(t.Poses[last].Heading, Math.PI / 2) < 0.01));
            Assert.True(AngleDiff(r.Poses[last].Heading, Math.PI / 2) < 0.01);
            Assert.True(r.WithinMinimumRadius);
            Assert.True(r.MaxArticulationDeg < v.MaxArticulationDeg);
            Assert.DoesNotContain(r.Warnings, w => w.Contains("Reversing"));
            Assert.All(r.Trailers, t => Assert.Equal(0, t.Poses[0].Heading, 9));
        }

        [Fact]
        public void AClickBehindTheTrailerReversesItThere()
        {
            DesignVehicle v = Semi();
            var start = new VehiclePose(new Point2(0, 0), 0);
            double reach = v.TowedLength;
            Assert.Equal(9.3, reach, 9);   // kingpin 0.2 m ahead of the drive axles, trailer axles 9.5 m behind it
            Assert.Equal(new Point2(-reach, 0), Assert.Single(v.TrailerPoses(start, null)).RearAxle);
            VehiclePose folded = Assert.Single(v.TrailerPoses(start, new[] { Math.PI / 2 }));
            Assert.True(Dist(folded.RearAxle, new Point2(v.Hitch, -9.5)) < 1e-9);
            Assert.Equal(0, DesignVehicle.B99.TowedLength);

            // Straight back: the trailer's axles go to the click, so the vehicle backs up that far less its trailer.
            TurnPathBuilder.DriveLeg back = TurnPathBuilder.DriveTo(v, start, new Point2(-30, 0))!;
            Assert.True(back.Reverse);
            Assert.Equal(30 - reach, back.Length, 9);
            Assert.Equal(new[] { $"{30 - reach:0.0} m straight" }, back.Moves);
            TurnPathResult r = TurnPathSimulator.Simulate(back.Request);
            Assert.True(Dist(r.Trailers[0].Poses[r.Poses.Count - 1].RearAxle, new Point2(-30, 0)) < 1e-9);

            // Behind and to one side: a swing, a turn, and the trailer arrives in line with the vehicle.
            foreach (TurnPathBuilder.DriveTurns turns in new[] { TurnPathBuilder.DriveTurns.Arc, TurnPathBuilder.DriveTurns.Corner })
            {
                var target = new Point2(-40, -12);
                TurnPathBuilder.DriveLeg leg = TurnPathBuilder.DriveTo(v, start, target, 12.5, turns: turns)!;
                Assert.True(leg.Reverse);
                TurnPathResult to = TurnPathSimulator.Simulate(leg.Request);
                int last = to.Poses.Count - 1;
                Assert.True(Dist(to.Trailers[0].Poses[last].RearAxle, target) < 0.01, $"{turns}: {Dist(to.Trailers[0].Poses[last].RearAxle, target)}");
                Assert.True(AngleDiff(to.Trailers[0].Poses[last].Heading, to.Poses[last].Heading) < 0.002);
                Assert.Empty(to.Warnings);
            }

            // Ahead of the trailer's axles is not behind: alongside the trailer it drives forwards or not at all.
            Assert.False(TurnPathBuilder.DriveTo(v, start, new Point2(25, 4))!.Reverse);
            TurnPathBuilder.DriveLeg? alongside = TurnPathBuilder.DriveTo(v, start, new Point2(-5, 3));
            Assert.True(alongside == null || !alongside.Reverse);

            // Too close to line up on, or with the trailer folded right round the wrong way: no leg.
            Assert.Null(TurnPathBuilder.DriveTo(v, start, new Point2(-12, -6)));
            Assert.Null(TurnPathBuilder.DriveToPose(v, start, new Point2(-30, -25), Math.PI / 2, 12.5, reverse: true, trailerHeadings: new[] { 60 * Math.PI / 180 }));
            Assert.Throws<ArgumentException>(() => TurnPathBuilder.DriveTo(v, start, new Point2(-30, 0), 4));
            Assert.Throws<ArgumentException>(() => TurnPathBuilder.ReverseTowed(DesignVehicle.B99, start, null, new Point2(-30, 0), null, 6.3));

            // A rigid vehicle is driven as it always was.
            TurnPathBuilder.DriveLeg car = TurnPathBuilder.DriveTo(DesignVehicle.B99, start, new Point2(-12, 2))!;
            Assert.True(car.Reverse);
            Assert.Null(car.Request.TurnRadius);
        }

        [Fact]
        public void ADriveGoesForwardsThenReversesWithTheTrailerAsItWasLeft()
        {
            TurnPathConventions c = Conventions();
            var r = new ManoeuvreRecord
            {
                Vehicle = "Semi 19 m",
                Mode = "drive",
                Start = new[] { 0.0, 0 },
                HeadingDeg = 0,
                Radius = 12.5,
                // Pull forward swinging left, then back onto a dock behind and to the right, facing +Y.
                Stops =
                {
                    new DriveStop { Point = new[] { 30.0, 6 } },
                    new DriveStop { Point = new[] { -10.0, -30 }, HeadingDeg = 90 },
                },
            };
            ManoeuvrePlan plan = ManoeuvrePlanner.Plan(r, c);
            Assert.Equal(2, plan.Legs.Count);
            TurnPathResult first = plan.Legs[0].Result, second = plan.Legs[1].Result;
            Assert.False(first.Reverse);
            Assert.True(second.Reverse);
            Assert.StartsWith("reverse, turn", plan.Legs[1].Description);

            // The reverse starts exactly where the forward leg stopped, trailer still folded.
            int end = first.Poses.Count - 1;
            Assert.True(Dist(first.Poses[end].RearAxle, second.Poses[0].RearAxle) < 1e-9);
            Assert.True(AngleDiff(first.Poses[end].Heading, second.Poses[0].Heading) < 1e-9);
            Assert.True(Dist(first.Trailers[0].Poses[end].RearAxle, second.Trailers[0].Poses[0].RearAxle) < 1e-6);
            Assert.True(AngleDiff(first.Trailers[0].Poses[end].Heading, second.Poses[0].Heading) > 0.05, "the forward leg leaves the trailer folded");

            // And puts the trailer's axles on the dock, in line.
            int last = second.Poses.Count - 1;
            Assert.True(Dist(second.Trailers[0].Poses[last].RearAxle, new Point2(-10, -30)) < 0.02, $"{Dist(second.Trailers[0].Poses[last].RearAxle, new Point2(-10, -30))}");
            Assert.True(AngleDiff(second.Poses[last].Heading, Math.PI / 2) < 0.005);
            Assert.DoesNotContain(plan.Warnings, w => w.Contains("Reversing"));

            // Both legs are drawn, the vehicle and trailer at the end of each.
            List<DraftedEntity> drafted = ManoeuvrePlanner.Draft(plan, r, c);
            Assert.True(drafted.Count(d => d.Spec is CircleSpec) >= 3, "a kingpin at the start, and at the end of each leg");
            Assert.Contains(drafted, d => d.Group == "swept_path");

            // It survives being saved and drawn again.
            ManoeuvreRecord again = ManoeuvreRecord.FromJson(r.ToJson());
            ManoeuvrePlan replanned = ManoeuvrePlanner.Plan(again, c);
            Assert.True(Dist(replanned.Legs[1].Result.Poses[last].RearAxle, second.Poses[last].RearAxle) < 1e-9);

            // Somewhere the trailer can't be lined up on from here says why.
            r.Stops.Add(new DriveStop { Point = new[] { -13.0, -36 } });
            var ex = Assert.Throws<ArgumentException>(() => ManoeuvrePlanner.Plan(r, c));
            Assert.Contains("last trailer", ex.Message);

            // Following a line in reverse: the trailer starts where the line needs it.
            var follow = new ManoeuvreRecord { Vehicle = "Semi 19 m", Mode = "follow", FollowHandle = "1", Reverse = true, Reference = "rear_axle" };
            ManoeuvrePlan followed = ManoeuvrePlanner.Plan(follow, c, _ => new LineSpec { Start = new Point2(0, 0), End = new Point2(30, 0) });
            TurnPathResult line = Assert.Single(followed.Legs).Result;
            Assert.True(line.Reverse);
            Assert.Empty(followed.Warnings);
            Assert.True(Dist(line.Trailers[0].Poses[line.Poses.Count - 1].RearAxle, new Point2(30 + 9.3, 0)) < 1e-9);

            // The solver is still for rigid vehicles.
            var solve = new ManoeuvreRecord { Vehicle = "Semi 19 m", Mode = "solve", Start = new[] { 0.0, 0 }, Target = new[] { 0.0, 5 }, TargetHeadingDeg = 180, Radius = 12.5 };
            Assert.Contains("rigid vehicles", Assert.Throws<ArgumentException>(() => ManoeuvrePlanner.Solve(solve, c, (min, max) => new List<Obstruction>())).Message);
            Assert.DoesNotContain("forwards only", c.Describe());
        }
    }
}
