using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using PointerPath.Core.Drawing;
using PointerPath.Core.Geometry;
using PointerPath.Core.Vehicles;
using Xunit;

namespace PointerPath.Core.Tests
{
    /// <summary>AS/NZS 2890.1 B85/B99 swept paths: kinematics, envelopes, templates and the tool.</summary>
    public class TurnPathTests
    {
        private static TurnPathConventions Conventions() =>
            TurnPathConventions.Load(Path.Combine(AppContext.BaseDirectory, "turn-paths.json"));

        private static double Dist(Point2 a, Point2 b) => Segment.Dist(a, b);

        /// <summary>A long left turn so the rear axle settles into its steady-state circle.</summary>
        private static TurnPathResult SteadyTurn(DesignVehicle v, double radius, ReferencePoint reference = ReferencePoint.OuterFrontWheel) =>
            TurnPathSimulator.Simulate(TurnPathBuilder.Template(new TurnTemplate
            {
                Vehicle = v,
                Start = new Point2(0, 0),
                Heading = 0,
                Radius = radius,
                TurnAngleDeg = 270,
                Left = true,
                LeadIn = 0,
                LeadOut = 0,
                Reference = reference,
            }));

        // ------------------------------------------------------------- vehicles

        [Fact]
        public void StandardVehiclesMatchAppendixB()
        {
            DesignVehicle b99 = DesignVehicle.B99, b85 = DesignVehicle.B85;
            Assert.Equal(5.2, b99.Length);
            Assert.Equal(1.94, b99.Width);
            Assert.Equal(3.05, b99.Wheelbase);
            Assert.Equal(0.95, b99.FrontOverhang);   // Figure B1: the front overhang is the shorter
            Assert.Equal(1.2, b99.RearOverhang);
            Assert.Equal(5.2, b99.FrontOverhang + b99.Wheelbase + b99.RearOverhang, 9);
            Assert.True(b99.FrontOverhangRounding > b99.RearOverhangRounding, "the front is the rounder end");
            Assert.Equal(6.3, b99.MinTurnRadius);
            Assert.Equal(0.6, b99.ClearanceOutside);

            Assert.Equal(4.91, b85.Length);
            Assert.Equal(1.87, b85.Width);
            Assert.Equal(0.92, b85.FrontOverhang);   // Figure B2: 920 front, 2800 wheelbase, 1190 rear
            Assert.Equal(1.19, b85.RearOverhang);
            Assert.Equal(4.91, b85.FrontOverhang + b85.Wheelbase + b85.RearOverhang, 9);
            Assert.True(b85.FrontOverhangRounding > b85.RearOverhangRounding);
            Assert.Equal(5.8, b85.MinTurnRadius);
            Assert.Equal(0.3, b85.ClearanceOutside);
            Assert.Empty(b99.Validate());
            Assert.Empty(b85.Validate());
        }

        [Fact]
        public void ShippedConventionsLoadAndMatchTheBuiltIns()
        {
            TurnPathConventions c = Conventions();
            Assert.Equal("B99", c.Vehicle(null).Name);
            foreach (DesignVehicle built in DesignVehicle.Standard)
            {
                DesignVehicle file = c.Vehicle(built.Name);
                Assert.Equal(built.Length, file.Length);
                Assert.Equal(built.Wheelbase, file.Wheelbase);
                Assert.Equal(built.EffectiveWidth, file.EffectiveWidth);
                Assert.Equal(built.MinTurnRadius, file.MinTurnRadius);
                Assert.Equal(built.ClearanceOutside, file.ClearanceOutside);
            }

            Assert.Throws<ArgumentException>(() => c.Vehicle("Road train"));
            Assert.Equal("VEHICLE BLOCK", c.Layers.Block);

            // The layers carry no office's prefix, each part has one of its own, and each has a style.
            List<string> names = c.Layers.All().Select(kv => kv.Value).ToList();
            Assert.Equal(names.Count, names.Distinct(StringComparer.OrdinalIgnoreCase).Count());
            Assert.All(names, name => Assert.StartsWith("VEHICLE ", name));
            Assert.All(names, name => Assert.True(c.LayerStyles.ContainsKey(name), name));
            Assert.Equal(new TurnPathLayers().All().Select(kv => kv.Value), names);
            Assert.False(c.Draw.Block);
            Assert.Contains(c.Layers.All(), kv => kv.Key == "block");
            Assert.True(c.LayerStyles.ContainsKey(c.Layers.Block));
            Assert.True(c.LayerStyles.ContainsKey(c.Layers.Clearance));
            Assert.Equal("DASHED", c.LayerStyles[c.Layers.Clearance].Linetype);

            // The template turn the dialog starts from: one 90 degree turn to the left, 6 m straight after.
            Assert.Equal(new[] { 90.0 }, c.TemplateAngles);
            Assert.True(c.TemplateLeft);
            Assert.Equal(0, c.LeadIn);
            Assert.Equal(6, c.LeadOut);
        }

        [Fact]
        public void ConventionsRoundTripThroughJson()
        {
            TurnPathConventions c = TurnPathConventions.Default();
            c.Layers.SweptPath = "MY-{vehicle}-SWEPT";
            c.Draw.Hatch = true;
            c.Draw.Block = true;
            c.Layers.Set("block", "BLK-{vehicle}");
            c.TemplateAngles = new List<double> { 30, 60, 90 };
            c.TemplateLeft = false;
            string path = Path.Combine(Path.GetTempPath(), "csdbridge-turn-paths-" + Guid.NewGuid().ToString("N") + ".json");
            try
            {
                c.Save(path);
                TurnPathConventions back = TurnPathConventions.Load(path);
                Assert.Equal("MY-{vehicle}-SWEPT", back.Layers.SweptPath);
                Assert.Equal("MY-B85-SWEPT", back.Layers.Resolve("B85").SweptPath);
                Assert.True(back.Draw.Hatch);
                Assert.True(back.Draw.Block);
                Assert.Equal("BLK-B99", back.Layers.Resolve("B99").Block);
                Assert.Equal(new[] { 30.0, 60, 90 }, back.TemplateAngles);
                Assert.False(back.TemplateLeft);
                Assert.Equal(2, back.Vehicles.Count);
                Assert.Equal(6.3, back.Vehicle("b99").MinTurnRadius);
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Fact]
        public void BodyOutlineIsTheStandardsShape()
        {
            DesignVehicle v = DesignVehicle.B99;
            var (pts, bulges) = v.BodyOutline();
            Assert.Equal(8, pts.Count);
            // Rounded corners: tangent to the side, through the effective corner.
            Assert.Equal(2 * Math.Atan2(0.05, v.CornerLength), v.CornerSweep, 9);
            Assert.Equal(Math.Tan(v.CornerSweep / 4), bulges[0], 9);
            Assert.True(bulges.All(b => b >= 0));
            // Nose and tail reach the overall length at the centreline: sagitta = overall - effective overhang.
            double frontSagitta = bulges[3] * v.EffectiveWidth / 2;
            double rearSagitta = bulges[7] * v.EffectiveWidth / 2;
            Assert.Equal(0.15, frontSagitta, 9);   // the front is the rounder end
            Assert.Equal(0.05, rearSagitta, 9);
            Assert.True(bulges[3] > 0 && bulges[7] > 0);
            Assert.Equal(v.Wheelbase + v.EffectiveFrontOverhang, pts.Max(p => p.X), 9);
            Assert.Equal(-v.EffectiveRearOverhang, pts.Min(p => p.X), 9);
            Assert.Equal(v.Width / 2, pts.Max(p => p.Y), 9);
        }

        // ----------------------------------------------------------- kinematics

        [Fact]
        public void SteadyTurnHasTheOuterFrontWheelOnTheNominatedRadius()
        {
            DesignVehicle v = DesignVehicle.B99;
            TurnPathResult r = SteadyTurn(v, 6.3);
            var arc = (ArcSegment)r.Path.Segments[0];
            Point2 centre = arc.Center;
            Assert.Equal(6.3, arc.Radius, 9);

            // Near the end of a 270 degree turn the tractrix has converged.
            VehiclePose last = r.Poses[r.Poses.Count - 1];
            double rr = v.RearAxleRadius(6.3);
            // (the tractrix converges asymptotically: within a few millimetres after 270 degrees)
            Assert.Equal(rr, Dist(last.RearAxle, centre), 2);
            Assert.Equal(6.3, Dist(last.ToWorld(v.OuterFrontWheel(left: true)), centre), 3);
            Assert.InRange(r.TightestRadius, 6.3 - 0.001, 6.32);
            Assert.True(r.WithinMinimumRadius);
            Assert.Empty(r.Warnings);
            // The heading lags the reference point's arc by the steady-state slip angle (about 29 degrees here).
            double slip = Math.Atan2(v.Wheelbase, rr + v.Track / 2) * 180 / Math.PI;
            Assert.Equal(270 - slip, r.NetTurnDeg, 0);
            Assert.InRange(r.MaxSteerAngleDeg, 30, 45);
        }

        [Fact]
        public void HeadingLagsThePathAndSettlesOnTheLeadOut()
        {
            DesignVehicle v = DesignVehicle.B99;
            TurnPathResult r = TurnPathSimulator.Simulate(TurnPathBuilder.Template(new TurnTemplate
            {
                Vehicle = v, Start = new Point2(10, 20), Heading = Math.PI / 2, Radius = 8, TurnAngleDeg = 90, Left = false, LeadIn = 2, LeadOut = 15,
            }));

            // Starts where asked, facing north; the first poses go straight.
            Assert.Equal(10, r.Poses[0].RearAxle.X, 9);
            Assert.Equal(20, r.Poses[0].RearAxle.Y, 9);
            Assert.Equal(Math.PI / 2, r.Poses[0].Heading, 9);
            VehiclePose during = r.PoseAt(1.0);
            Assert.Equal(Math.PI / 2, during.Heading, 6);

            // Ends facing east, with the rear axle on the lead-out line (within the settling tolerance).
            VehiclePose last = r.Poses[r.Poses.Count - 1];
            Assert.Equal(0, Math.Abs(((last.Heading + Math.PI) % (2 * Math.PI)) - Math.PI), 2);
            Assert.Equal(-90, r.NetTurnDeg, 0);
            Assert.True(r.WithinMinimumRadius);
        }

        [Fact]
        public void RearAxleReferenceFollowsThePathExactly()
        {
            DesignVehicle v = DesignVehicle.B85;
            TurnPathResult r = SteadyTurn(v, 5.8, ReferencePoint.RearAxle);
            var arc = (ArcSegment)r.Path.Segments[0];
            Assert.Equal(v.RearAxleRadius(5.8), arc.Radius, 9);
            foreach (VehiclePose p in r.Poses)
            {
                Assert.Equal(arc.Radius, Dist(p.RearAxle, arc.Center), 6);
            }

            Assert.Equal(5.8, r.TightestRadius, 3);
        }

        [Fact]
        public void TooTightATurnIsReported()
        {
            TurnPathResult r = SteadyTurn(DesignVehicle.B99, 5.0);
            Assert.False(r.WithinMinimumRadius);
            Assert.Contains(r.Warnings, w => w.Contains("tighter than"));
            Assert.InRange(r.TightestRadius, 4.99, 5.02);
        }

        // ------------------------------------------------------------- envelope

        [Fact]
        public void SteadyStateEnvelopeIsTheBodysExtremeCircles()
        {
            DesignVehicle v = DesignVehicle.B99;
            TurnPathResult r = SteadyTurn(v, 6.3);
            var arc = (ArcSegment)r.Path.Segments[0];
            Point2 c = arc.Center;
            double rr = v.RearAxleRadius(6.3);
            double inner = rr - v.Width / 2;                                       // inner side at the rear axle
            double xf = v.Wheelbase + v.EffectiveFrontOverhang, he = v.EffectiveWidth / 2;
            double outer = Math.Sqrt(xf * xf + (rr + he) * (rr + he));             // outer front effective corner

            // Second half of the turn: everything has settled. Left is inside, right outside.
            double along = arc.Length;
            var leftLate = r.LeftBody.Where(p => AngleAlong(c, arc, p) > 0.55 * along && AngleAlong(c, arc, p) < 0.95 * along).ToList();
            var rightLate = r.RightBody.Where(p => AngleAlong(c, arc, p) > 0.55 * along && AngleAlong(c, arc, p) < 0.95 * along).ToList();
            Assert.True(leftLate.Count > 10 && rightLate.Count > 10);
            foreach (Point2 p in leftLate)
            {
                Assert.InRange(Dist(p, c), inner - 0.002, inner + 0.012);
            }

            foreach (Point2 p in rightLate)
            {
                Assert.InRange(Dist(p, c), outer - 0.012, outer + 0.002);
            }

            // The outer body line is a little outside the nominated (wheel) radius, as the standard notes.
            Assert.True(outer > 6.3 && outer < 7.2);

            // Clearances: 300 mm inside, 600 mm outside for B99.
            foreach (Point2 p in r.LeftClearance.Where(p => AngleAlong(c, arc, p) > 0.55 * along && AngleAlong(c, arc, p) < 0.95 * along))
            {
                Assert.InRange(Dist(p, c), inner - 0.3 - 0.002, inner - 0.3 + 0.012);
            }

            foreach (Point2 p in r.RightClearance.Where(p => AngleAlong(c, arc, p) > 0.55 * along && AngleAlong(c, arc, p) < 0.95 * along))
            {
                Assert.InRange(Dist(p, c), outer + 0.6 - 0.012, outer + 0.6 + 0.002);
            }

            Assert.Equal(0.3, r.ClearanceLeft);
            Assert.Equal(0.6, r.ClearanceRight);
        }

        /// <summary>Arc length from the arc's start to the point's bearing from the centre (for ordering points round the turn).</summary>
        private static double AngleAlong(Point2 c, ArcSegment arc, Point2 p)
        {
            double a = Math.Atan2(p.Y - c.Y, p.X - c.X);
            double along = arc.AngleAlong(a);
            return along < 0 ? -1 : along * arc.Radius;
        }

        [Fact]
        public void EnvelopeStartsAtTheParkedBodyAndEndsAtTheFrontCorners()
        {
            DesignVehicle v = DesignVehicle.B99;
            TurnPathResult r = TurnPathSimulator.Simulate(TurnPathBuilder.Template(new TurnTemplate
            {
                Vehicle = v, Start = new Point2(0, 0), Heading = 0, Radius = 6.3, TurnAngleDeg = 90, Left = true, LeadIn = 3, LeadOut = 6,
            }));

            VehiclePose start = r.Poses[0], end = r.Poses[r.Poses.Count - 1];
            double xf = v.Wheelbase + v.EffectiveFrontOverhang, xr = -v.EffectiveRearOverhang, he = v.EffectiveWidth / 2;
            Assert.Equal(0, Dist(r.LeftBody[0], start.ToWorld(xr, he)), 1e-6);
            Assert.Equal(0, Dist(r.RightBody[0], start.ToWorld(xr, -he)), 1e-6);
            Assert.Equal(0, Dist(r.LeftBody[r.LeftBody.Count - 1], end.ToWorld(xf, he)), 1e-6);
            Assert.Equal(0, Dist(r.RightBody[r.RightBody.Count - 1], end.ToWorld(xf, -he)), 1e-6);

            // Along the lead-in the swept path is the body width, both sides straight: on the outside
            // of the turn only up to where the tail will be when the turn starts and it swings out.
            foreach (Point2 p in r.LeftBody.Where(p => p.X > 0.1 && p.X < 2.9))
            {
                Assert.Equal(v.Width / 2, p.Y, 6);
            }

            foreach (Point2 p in r.RightBody.Where(p => p.X > 0.1 && p.X < 2.9 - v.RearOverhang))
            {
                Assert.Equal(-v.Width / 2, p.Y, 6);
            }

            // The lines are monotone along the manoeuvre: no doubling back or self-crossing.
            Assert.True(IsSimple(r.LeftBody), "left envelope crosses itself");
            Assert.True(IsSimple(r.RightBody), "right envelope crosses itself");
            Assert.True(IsSimple(r.LeftClearance), "left clearance crosses itself");
            Assert.True(IsSimple(r.RightClearance), "right clearance crosses itself");

            // The closed outline has the body's end arcs and encloses roughly the expected area.
            PolylineSpec outline = r.ClosedOutline();
            Assert.True(outline.Closed);
            Assert.Equal(outline.Points.Count, outline.Bulges!.Count);
            Assert.Equal(2, outline.Bulges.Count(b => Math.Abs(b) > 1e-9));
            Assert.True(IsSimple(outline.Points), "closed outline crosses itself");
            Path2 loop = Path2.From(outline)!;
            Assert.InRange(loop.Area, 30, 60);
        }

        private static bool IsSimple(List<Point2> pts)
        {
            for (int i = 0; i < pts.Count - 1; i++)
            {
                for (int j = i + 2; j < pts.Count - 1; j++)
                {
                    var a = new Path2(new List<Segment> { new LineSegment(pts[i], pts[i + 1]) }, false);
                    var b = new Path2(new List<Segment> { new LineSegment(pts[j], pts[j + 1]) }, false);
                    if (Intersections.Find(a, b, extend: false).Count > 0)
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        [Fact]
        public void EnvelopeContainsEveryBodyPosition()
        {
            // The swept path must enclose the body at every pose: sample body corners and check they lie
            // between the left and right envelopes (never left of the left line or right of the right line).
            DesignVehicle v = DesignVehicle.B85;
            TurnPathResult r = TurnPathSimulator.Simulate(TurnPathBuilder.Template(new TurnTemplate
            {
                Vehicle = v, Start = new Point2(0, 0), Heading = 0.3, Radius = 5.8, TurnAngleDeg = 120, Left = false, LeadIn = 2, LeadOut = 5,
            }));

            var leftPath = new Path2(Segments(r.LeftBody), false);
            var rightPath = new Path2(Segments(r.RightBody), false);
            var (bodyPts, _) = v.BodyOutline();
            for (int i = 0; i < r.Poses.Count; i += 7)
            {
                foreach (VehiclePoint bp in bodyPts)
                {
                    Point2 w = r.Poses[i].ToWorld(bp);
                    // Signed side of the nearest envelope segment: the body must be on the inner side of each line.
                    Assert.True(SideOf(leftPath, w) <= 0.003, $"pose {i}: body point {w} is outside the left envelope");
                    Assert.True(SideOf(rightPath, w) >= -0.003, $"pose {i}: body point {w} is outside the right envelope");
                }
            }
        }

        [Fact]
        public void RearSwingOutIsPartOfTheOuterEnvelope()
        {
            // Turning right from a straight: the rear swings out to the left, so the left envelope must
            // bulge past the lead-in's straight side line before the front corner's trace takes over.
            DesignVehicle v = DesignVehicle.B99;
            TurnPathResult r = TurnPathSimulator.Simulate(TurnPathBuilder.Template(new TurnTemplate
            {
                Vehicle = v, Start = new Point2(0, 0), Heading = 0, Radius = 6.3, TurnAngleDeg = 90, Left = false, LeadIn = 3, LeadOut = 4,
            }));
            double hw = v.Width / 2;
            double maxBulge = r.LeftBody.Where(p => p.X > 1 && p.X < 4).Select(p => p.Y - hw).DefaultIfEmpty(0).Max();
            Assert.InRange(maxBulge, 0.005, 0.08);
            Assert.True(IsSimple(r.LeftBody));
        }

        private static List<Segment> Segments(List<Point2> pts)
        {
            var list = new List<Segment>();
            for (int i = 0; i < pts.Count - 1; i++)
            {
                list.Add(new LineSegment(pts[i], pts[i + 1]));
            }

            return list;
        }

        /// <summary>Positive when the point is to the left of the polyline's direction at its closest point.</summary>
        private static double SideOf(Path2 line, Point2 p)
        {
            var (closest, along, _) = line.Closest(p);
            double t = line.TangentAt(along);
            double nx = -Math.Sin(t), ny = Math.Cos(t);
            return (p.X - closest.X) * nx + (p.Y - closest.Y) * ny;
        }

        [Fact]
        public void ReverseTravelSwapsTheLeadingEnd()
        {
            DesignVehicle v = DesignVehicle.B99;
            var line = new Path2(new List<Segment> { new LineSegment(new Point2(0, 0), new Point2(0, 10)) }, false);
            TurnPathResult r = TurnPathSimulator.Simulate(new TurnPathRequest { Vehicle = v, Path = line, Reference = v.RearAxleCentre, Reverse = true });

            // Facing south while moving north; the envelope starts at the front corners and ends at the rear corners.
            Assert.Equal(1.5 * Math.PI, r.Poses[0].Heading, 9);
            double xf = v.Wheelbase + v.EffectiveFrontOverhang, xr = -v.EffectiveRearOverhang, he = v.EffectiveWidth / 2;
            Assert.Equal(0, Dist(r.LeftBody[0], r.Poses[0].ToWorld(xf, he)), 1e-6);
            Assert.Equal(0, Dist(r.LeftBody[r.LeftBody.Count - 1], r.Poses[r.Poses.Count - 1].ToWorld(xr, he)), 1e-6);
            Assert.Equal(0, r.NetTurnDeg, 6);
            Assert.True(double.IsPositiveInfinity(r.TightestRadius));

            // Facing south, the vehicle's left is at +X: the left clearance is further out that way.
            // (Away from the ends, where the lines wrap round the corners.)
            Assert.All(r.LeftBody.Where(p => p.Y > -3.5 && p.Y < 9.5), p => Assert.Equal(v.Width / 2, p.X, 6));
            Assert.All(r.LeftClearance.Where(p => p.Y > -3.5 && p.Y < 9.5), p => Assert.Equal(v.Width / 2 + 0.3, p.X, 6));
            Assert.All(r.RightClearance.Where(p => p.Y > -3.5 && p.Y < 9.5), p => Assert.Equal(-v.Width / 2 - 0.3, p.X, 6));
        }

        [Theory]
        [InlineData("B85")]
        [InlineData("B99")]
        public void ReversingAtFullLockSwingsTheFrontOut(string name)
        {
            // Parked facing +Y, the car backs round to its right at full lock: its rear axle follows
            // an arc about a centre on its right, so the body turns about that centre and the front,
            // furthest from it, swings out to the left of where the car stood.
            DesignVehicle v = Conventions().Vehicle(name);
            double rho = v.RearAxleRadius(v.MinTurnRadius);
            var centre = new Point2(rho, 0);
            var path = new Path2(new List<Segment> { new ArcSegment(centre, rho, Math.PI, Math.PI / 2) }, false);
            TurnPathResult r = TurnPathSimulator.Simulate(new TurnPathRequest { Vehicle = v, Path = path, Reference = v.RearAxleCentre, Reverse = true, InitialHeading = Math.PI / 2 });
            Assert.Equal(Math.PI / 2, r.Poses[0].Heading, 9);
            Assert.Equal(90, r.NetTurnDeg, 3);
            Assert.True(r.WithinMinimumRadius);

            // By hand: the front-left corner is (wheelbase + overhang) ahead of the rear axle and half
            // the width to the left, so it turns on this radius about the centre...
            double ahead = v.Wheelbase + v.EffectiveFrontOverhang, half = v.EffectiveWidth / 2;
            double corner = Math.Sqrt(ahead * ahead + (rho + half) * (rho + half));
            // ...and reaches this far left of the car's side: about a metre.
            double swing = corner - (rho + v.Width / 2);
            Assert.InRange(swing, 0.9, 1.2);
            Assert.Equal(rho - corner, r.LeftBody.Min(p => p.X), 3);
            Assert.Equal(-v.Width / 2 - swing, r.LeftBody.Min(p => p.X), 3);

            // The swing is beside where the car stood, ahead of its rear axle: the front, not the rear.
            Point2 furthest = r.LeftBody.OrderBy(p => p.X).First();
            Assert.InRange(furthest.Y, -0.2, 0.2);
            Assert.Contains(r.LeftBody, p => p.Y > 1 && p.X < -v.Width / 2 - 0.3);

            // The outside edge is that corner's circle, the inside edge the side at the rear axle.
            foreach (Point2 p in r.LeftBody.Where(p => p.Y < -0.5 && p.X < rho - 1))
            {
                Assert.Equal(corner, Dist(p, centre), 2);
            }

            foreach (Point2 p in r.RightBody.Where(p => p.Y < -0.5 && p.X < rho - 1))
            {
                Assert.Equal(rho - v.Width / 2, Dist(p, centre), 2);
            }

            // The front swings further than the rear moves at all: it is the front wheels that steer.
            double frontTravel = 0, rearTravel = 0;
            for (int i = 1; i < r.Poses.Count; i++)
            {
                frontTravel += Dist(r.Poses[i - 1].ToWorld(v.Wheelbase, 0), r.Poses[i].ToWorld(v.Wheelbase, 0));
                rearTravel += Dist(r.Poses[i - 1].RearAxle, r.Poses[i].RearAxle);
            }

            Assert.Equal(rho * Math.PI / 2, rearTravel, 2);
            Assert.Equal(Math.Sqrt(rho * rho + v.Wheelbase * v.Wheelbase) * Math.PI / 2, frontTravel, 2);
            Assert.True(frontTravel > 1.15 * rearTravel);

            // Driving forwards round the same arc the other way sweeps the same ground.
            TurnPathResult forward = TurnPathSimulator.Simulate(new TurnPathRequest { Vehicle = v, Path = PathOps.Reverse(path), Reference = v.RearAxleCentre });
            Assert.Equal(forward.LeftBody.Min(p => p.X), r.LeftBody.Min(p => p.X), 3);
        }

        [Fact]
        public void CarsSavedBackToFrontAreTurnedRoundAndTrailersLeftOut()
        {
            // The shipped file has the cars the right way round, and nothing that tows.
            TurnPathConventions shipped = Conventions();
            Assert.Empty(shipped.CorrectReversedCars());
            Assert.Empty(shipped.RemoveVehiclesWithTrailers());
            Assert.Equal(new[] { "B99", "B85", "SRV", "MRV", "HRV", "Service 8.8 m", "Truck 12.5 m" }, shipped.Vehicles.Keys);
            Assert.Equal(0.92, shipped.Vehicle("B85").FrontOverhang);
            Assert.Equal(1.2, shipped.Vehicle("B99").RearOverhang);

            // A copy saved from an earlier version: both cars back to front, an edited car, and a semi-trailer.
            string path = Path.Combine(Path.GetTempPath(), $"turn-paths-{Guid.NewGuid():N}.json");
            File.WriteAllText(path, @"{ ""vehicles"": {
                ""B85"": { ""length"": 4.91, ""width"": 1.87, ""wheelbase"": 2.8, ""front_overhang"": 1.19, ""rear_overhang"": 0.92,
                           ""effective_front_overhang"": 1.1, ""effective_rear_overhang"": 0.75, ""effective_width"": 1.77, ""track"": 1.57, ""min_turn_radius"": 5.8 },
                ""B99"": { ""length"": 5.2, ""width"": 1.94, ""wheelbase"": 3.05, ""front_overhang"": 1.2, ""rear_overhang"": 0.95,
                           ""effective_front_overhang"": 1.15, ""effective_rear_overhang"": 0.8, ""effective_width"": 1.84, ""track"": 1.62, ""min_turn_radius"": 6.3 },
                ""My car"": { ""length"": 4.91, ""width"": 1.87, ""wheelbase"": 2.8, ""front_overhang"": 1.21, ""rear_overhang"": 0.9,
                           ""effective_front_overhang"": 1.1, ""effective_rear_overhang"": 0.75, ""effective_width"": 1.77, ""track"": 1.57, ""min_turn_radius"": 5.8 },
                ""Semi"": { ""length"": 6, ""width"": 2.5, ""wheelbase"": 3.6, ""front_overhang"": 1.4, ""rear_overhang"": 1, ""track"": 2.1, ""min_turn_radius"": 12.5, ""hitch"": 0.25,
                           ""trailers"": [ { ""name"": ""semi-trailer"", ""width"": 2.5, ""front_overhang"": 1.2, ""wheelbase"": 8.5, ""rear_overhang"": 3.3 } ] } } }");
            try
            {
                TurnPathConventions saved = TurnPathConventions.Load(path);
                Assert.Equal(new[] { "B85", "B99" }, saved.CorrectReversedCars());
                DesignVehicle b85 = saved.Vehicle("B85"), b99 = saved.Vehicle("B99");
                Assert.Equal((0.92, 1.19, 0.75, 1.1), (b85.FrontOverhang, b85.RearOverhang, b85.EffectiveFrontOverhang, b85.EffectiveRearOverhang));
                Assert.Equal((0.95, 1.2, 0.8, 1.15), (b99.FrontOverhang, b99.RearOverhang, b99.EffectiveFrontOverhang, b99.EffectiveRearOverhang));
                Assert.Empty(b85.Validate());
                Assert.Equal(1.21, saved.Vehicle("My car").FrontOverhang);   // someone's own car is theirs
                Assert.Empty(saved.CorrectReversedCars());                  // and once is enough

                Assert.Equal(new[] { "Semi" }, saved.RemoveVehiclesWithTrailers());
                Assert.Equal(new[] { "B85", "B99", "My car" }, saved.Vehicles.Keys);
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Fact]
        public void SCurveKeepsTheEnvelopeOutsideTheBody()
        {
            // Left then right: the extreme corner switches sides at the inflection. The clearance offsets
            // must stay outside the body lines everywhere.
            DesignVehicle v = DesignVehicle.B99;
            Path2 path = TurnPathBuilder.FromPoints(new[] { new Point2(0, 0), new Point2(12, 0), new Point2(20, 8), new Point2(32, 8) }, 8);
            TurnPathResult r = TurnPathSimulator.Simulate(new TurnPathRequest { Vehicle = v, Path = path, Reference = v.FrontAxleCentre });
            Assert.True(r.WithinMinimumRadius);
            Assert.True(IsSimple(r.LeftBody));
            Assert.True(IsSimple(r.RightBody));

            var leftBody = new Path2(Segments(r.LeftBody), false);
            var rightBody = new Path2(Segments(r.RightBody), false);
            foreach (Point2 p in r.LeftClearance)
            {
                Assert.InRange(leftBody.Closest(p).Distance, 0.299, 0.62);
            }

            foreach (Point2 p in r.RightClearance)
            {
                Assert.InRange(rightBody.Closest(p).Distance, 0.299, 0.62);
            }
        }

        // ------------------------------------------------------------- builders

        [Fact]
        public void TemplatePathIsLeadInArcLeadOut()
        {
            DesignVehicle v = DesignVehicle.B99;
            TurnPathRequest req = TurnPathBuilder.Template(new TurnTemplate
            {
                Vehicle = v, Start = new Point2(5, 5), Heading = 0, Radius = 6.3, TurnAngleDeg = 90, Left = true, LeadIn = 2, LeadOut = 4,
            });
            Assert.Equal(3, req.Path.Segments.Count);
            var lead = Assert.IsType<LineSegment>(req.Path.Segments[0]);
            var arc = Assert.IsType<ArcSegment>(req.Path.Segments[1]);
            var tail = Assert.IsType<LineSegment>(req.Path.Segments[2]);
            // The outer (right) front wheel starts the path.
            Assert.Equal(5 + v.Wheelbase, lead.P.X, 9);
            Assert.Equal(5 - v.Track / 2, lead.P.Y, 9);
            Assert.Equal(2, lead.Length, 9);
            Assert.Equal(6.3, arc.Radius, 9);
            Assert.Equal(Math.PI / 2, arc.Sweep, 9);
            Assert.Equal(4, tail.Length, 9);
            Assert.Equal(Math.PI / 2, Math.Atan2(tail.Q.Y - tail.P.Y, tail.Q.X - tail.P.X), 9);

            Assert.Throws<ArgumentException>(() => TurnPathBuilder.Template(new TurnTemplate { Vehicle = v, Radius = 2 }));
        }

        [Fact]
        public void PointsPathGetsTangentCornerArcsAndShrinksThemWhenShort()
        {
            var warnings = new List<string>();
            Path2 p = TurnPathBuilder.FromPoints(new[] { new Point2(0, 0), new Point2(10, 0), new Point2(10, 10) }, 4, warnings);
            Assert.Equal(3, p.Segments.Count);
            var arc = Assert.IsType<ArcSegment>(p.Segments[1]);
            Assert.Equal(4, arc.Radius, 9);
            Assert.Equal(Math.PI / 2, arc.Sweep, 9);
            Assert.Equal(0, Dist(arc.Start, new Point2(6, 0)), 1e-9);
            Assert.Equal(0, Dist(arc.End, new Point2(10, 4)), 1e-9);
            Assert.Empty(warnings);

            Path2 tight = TurnPathBuilder.FromPoints(new[] { new Point2(0, 0), new Point2(3, 0), new Point2(3, 10) }, 6, warnings);
            var small = Assert.IsType<ArcSegment>(tight.Segments[0]);
            Assert.Equal(3, small.Radius, 9); // limited by the 3 m first leg
            Assert.Single(warnings);

            Assert.Throws<ArgumentException>(() => TurnPathBuilder.FromPoints(new[] { new Point2(0, 0), new Point2(5, 0), new Point2(0, 0) }, 2));
        }

        [Fact]
        public void DriveToTurnsAtTheRadiusThenGoesStraight()
        {
            DesignVehicle v = DesignVehicle.B99;
            var start = new VehiclePose(new Point2(0, 0), 0); // facing east, rear axle at the origin
            // Corner turns: minimum-radius arc then straight.
            TurnPathBuilder.DriveLeg leg = TurnPathBuilder.DriveTo(v, start, new Point2(20, 12), turns: TurnPathBuilder.DriveTurns.Corner)!;
            Assert.False(leg.Reverse);
            Assert.True(leg.Left);
            Assert.Equal(2, leg.Request.Path.Segments.Count);
            var arc = Assert.IsType<ArcSegment>(leg.Request.Path.Segments[0]);
            var line = Assert.IsType<LineSegment>(leg.Request.Path.Segments[1]);
            // The front axle centre starts the path and arrives at the destination.
            Assert.Equal(0, Dist(leg.Request.Path.Start, new Point2(v.Wheelbase, 0)), 1e-9);
            Assert.Equal(0, Dist(leg.Request.Path.End, new Point2(20, 12)), 1e-9);
            Assert.Equal(v.RadiusOfPoint(v.MinTurnRadius, v.FrontAxleCentre, true), arc.Radius, 9);
            // The straight is tangent to the arc: same direction at the join.
            Assert.Equal(arc.TangentAt(arc.Length), Math.Atan2(line.Q.Y - line.P.Y, line.Q.X - line.P.X), 6);

            // Driving it, the vehicle ends at the destination with the wheels never past full lock.
            TurnPathResult r = TurnPathSimulator.Simulate(leg.Request);
            Assert.True(r.WithinMinimumRadius);
            Assert.Equal(0, Dist(r.Poses[r.Poses.Count - 1].ToWorld(v.FrontAxleCentre), new Point2(20, 12)), 0.03);
        }

        [Fact]
        public void DriveToReversesForPointsBehindAndGoesRoundForPointsInsideTheCircle()
        {
            DesignVehicle v = DesignVehicle.B99;
            var start = new VehiclePose(new Point2(0, 0), Math.PI / 2); // facing north

            TurnPathBuilder.DriveLeg back = TurnPathBuilder.DriveTo(v, start, new Point2(2, -8))!;
            Assert.True(back.Reverse);
            Assert.Equal(0, Dist(back.Request.Path.Start, new Point2(0, 0)), 1e-9); // the rear axle drives in reverse
            Assert.Equal(0, Dist(back.Request.Path.End, new Point2(2, -8)), 1e-9);
            TurnPathResult rb = TurnPathSimulator.Simulate(back.Request);
            Assert.True(rb.Reverse);
            Assert.Equal(0, Dist(rb.Poses[rb.Poses.Count - 1].RearAxle, new Point2(2, -8)), 0.03);

            // A point just to the left, inside the left turning circle: not reachable in one leg (no loops).
            Assert.Null(TurnPathBuilder.DriveTo(v, start, new Point2(-2, v.Wheelbase + 1)));
            Assert.Null(TurnPathBuilder.DriveTo(v, start, new Point2(-2, v.Wheelbase + 1), turns: TurnPathBuilder.DriveTurns.Corner));
            // A U-turn is still allowed: a point beside the car, two turning circles over.
            double rf = v.RadiusOfPoint(v.MinTurnRadius, v.FrontAxleCentre, true);
            TurnPathBuilder.DriveLeg uturn = TurnPathBuilder.DriveTo(v, start, new Point2(2 * rf + 0.5, v.Wheelbase - 1))!;
            Assert.InRange(uturn.ArcDeg, 150, 190);

            // Too close to move: nothing. Too tight a radius: refused.
            Assert.Null(TurnPathBuilder.DriveTo(v, start, new Point2(0, v.Wheelbase)));
            Assert.Throws<ArgumentException>(() => TurnPathBuilder.DriveTo(v, start, new Point2(10, 10), 4));
            // A forced direction is honoured.
            Assert.True(TurnPathBuilder.DriveTo(v, start, new Point2(10, 10), reverse: true)!.Reverse);
        }

        [Fact]
        public void DriveToUsesOneArcWhenTheRadiusAllows()
        {
            DesignVehicle v = DesignVehicle.B99;
            var start = new VehiclePose(new Point2(0, 0), 0);
            double rf = v.RadiusOfPoint(v.MinTurnRadius, v.FrontAxleCentre, true);

            // A gentle destination: one arc, tangent to the heading, ending at the point.
            TurnPathBuilder.DriveLeg arc = TurnPathBuilder.DriveTo(v, start, new Point2(v.Wheelbase + 10, 3))!;
            Assert.Single(arc.Request.Path.Segments);
            var seg = Assert.IsType<ArcSegment>(arc.Request.Path.Segments[0]);
            Assert.Equal(109.0 / 6, seg.Radius, 6); // d² / (2 × lateral) = (100 + 9) / 6
            Assert.True(seg.Radius > rf);
            Assert.Equal(0, seg.TangentAt(0), 6);
            Assert.Equal(0, Dist(arc.Request.Path.End, new Point2(v.Wheelbase + 10, 3)), 1e-9);
            Assert.True(arc.Left);
            Assert.Equal(0, arc.StraightLength, 9);

            // Driving it, the car arrives pointing along the arc.
            TurnPathResult r = TurnPathSimulator.Simulate(arc.Request);
            Assert.Equal(0, Dist(r.Poses[r.Poses.Count - 1].ToWorld(v.FrontAxleCentre), new Point2(v.Wheelbase + 10, 3)), 0.03);
            Assert.True(r.WithinMinimumRadius);

            // A point ahead that one arc can't reach without being tighter than the car can steer is
            // exactly a point inside the turning circle: refused, never a loop.
            Assert.Null(TurnPathBuilder.DriveTo(v, start, new Point2(v.Wheelbase + 4, 6)));

            // Beside the car, past the turning circle: one arc would be more than a half circle, so
            // it is a minimum-radius turn then straight.
            TurnPathBuilder.DriveLeg tight = TurnPathBuilder.DriveTo(v, start, new Point2(v.Wheelbase - 1, 2 * rf + 0.5))!;
            Assert.Equal(2, tight.Request.Path.Segments.Count);
            Assert.Equal(rf, ((ArcSegment)tight.Request.Path.Segments[0]).Radius, 9);
            Assert.True(tight.StraightLength > 0);

            // Straight ahead: just a line. Corner mode never uses the single arc.
            Assert.IsType<LineSegment>(TurnPathBuilder.DriveTo(v, start, new Point2(15, 0))!.Request.Path.Segments[0]);
            Assert.Equal(2, TurnPathBuilder.DriveTo(v, start, new Point2(v.Wheelbase + 10, 3), turns: TurnPathBuilder.DriveTurns.Corner)!.Request.Path.Segments.Count);

            // In reverse the same rule applies to the rear axle.
            TurnPathBuilder.DriveLeg back = TurnPathBuilder.DriveTo(v, start, new Point2(-12, 2))!;
            Assert.True(back.Reverse);
            Assert.Single(back.Request.Path.Segments);
            Assert.Equal(0, Dist(back.Request.Path.End, new Point2(-12, 2)), 1e-9);
        }

        [Fact]
        public void ADestinationInsideTheTurningCircleMovesOutToItsEdge()
        {
            DesignVehicle v = DesignVehicle.B99;
            var start = new VehiclePose(new Point2(0, 0), 0); // facing east, rear axle at the origin
            double w = v.Wheelbase;
            double rf = v.RadiusOfPoint(v.MinTurnRadius, v.FrontAxleCentre, true);
            var left = new Point2(w, rf); // the centre of the front axle's turning circle, to the left

            // On the edge of a circle: its radius from the centre, and a hair more.
            void OnEdge(double distance, double radius) => Assert.InRange(distance, radius, radius + 0.001);

            // A destination the car can drive to is left where it is.
            TurnPathBuilder.DriveLeg plain = TurnPathBuilder.DriveToNearest(v, start, new Point2(20, 12), out Point2 same)!;
            Assert.Equal(new Point2(20, 12), same);
            Assert.Equal(TurnPathBuilder.DriveTo(v, start, new Point2(20, 12))!.Length, plain.Length, 12);

            // One inside the left turning circle (tighter than the car steers) has no leg of its own...
            var inside = new Point2(w + 4, 6);
            Assert.True(Dist(inside, left) < rf);
            Assert.Null(TurnPathBuilder.DriveTo(v, start, inside));

            // ...so the leg goes to the nearest point the car can reach: straight out, on the edge of the circle.
            TurnPathBuilder.DriveLeg leg = TurnPathBuilder.DriveToNearest(v, start, inside, out Point2 reached)!;
            OnEdge(Dist(reached, left), rf);
            OnEdge(Dist(reached, inside), rf - Dist(inside, left));
            Assert.Equal(Math.Atan2(inside.Y - left.Y, inside.X - left.X), Math.Atan2(reached.Y - left.Y, reached.X - left.X), 9);
            Assert.False(leg.Reverse);
            Assert.True(leg.Left);
            Assert.Equal(0, Dist(leg.Request.Path.End, reached), 1e-9);
            var arc = Assert.IsType<ArcSegment>(Assert.Single(leg.Request.Path.Segments));
            Assert.InRange(arc.Radius, rf, rf + 0.001); // the circle itself, a hair wider

            // Driving it, the car gets there without steering past full lock.
            TurnPathResult r = TurnPathSimulator.Simulate(leg.Request);
            Assert.True(r.WithinMinimumRadius);
            Assert.Equal(0, Dist(r.Poses[r.Poses.Count - 1].ToWorld(v.FrontAxleCentre), reached), 0.03);

            // The point it moved to is an ordinary destination: the same leg is planned to it again,
            // whichever way the legs turn, and from a drawing a long way from the origin.
            Assert.Equal(leg.Length, TurnPathBuilder.DriveTo(v, start, reached)!.Length, 12);
            TurnPathBuilder.DriveLeg corner = TurnPathBuilder.DriveToNearest(v, start, inside, out Point2 cornered, turns: TurnPathBuilder.DriveTurns.Corner)!;
            Assert.Equal(reached, cornered);
            Assert.NotNull(TurnPathBuilder.DriveTo(v, start, cornered, turns: TurnPathBuilder.DriveTurns.Corner));
            Assert.InRange(corner.StraightLength, 0, 0.05);
            var far = new VehiclePose(new Point2(525000, 5250000), 0.7);
            Point2 farInside = far.ToWorld(w + 4, 6);
            Assert.Null(TurnPathBuilder.DriveTo(v, far, farInside));
            Assert.NotNull(TurnPathBuilder.DriveToNearest(v, far, farInside, out Point2 farReached));
            Assert.NotNull(TurnPathBuilder.DriveTo(v, far, farReached));
            Assert.NotNull(TurnPathBuilder.DriveTo(v, far, farReached, turns: TurnPathBuilder.DriveTurns.Corner));

            // To the right it is the right circle, and behind the car the rear axle's circles, in reverse.
            TurnPathBuilder.DriveLeg right = TurnPathBuilder.DriveToNearest(v, start, new Point2(w + 4, -6), out Point2 mirrored)!;
            Assert.False(right.Left);
            Assert.Equal(reached.X, mirrored.X, 9);
            Assert.Equal(-reached.Y, mirrored.Y, 9);
            double rho = v.RearAxleRadius(v.MinTurnRadius);
            var behind = new Point2(-3, 2);
            Assert.Null(TurnPathBuilder.DriveTo(v, start, behind));
            TurnPathBuilder.DriveLeg back = TurnPathBuilder.DriveToNearest(v, start, behind, out Point2 backTo)!;
            Assert.True(back.Reverse);
            OnEdge(Dist(backTo, new Point2(0, rho)), rho);
            Assert.True(TurnPathSimulator.Simulate(back.Request).WithinMinimumRadius);

            // Coming out of a turn the wheels are still over, and the circles lie along the way they point.
            double fullLock = Math.Atan(w / rho);
            TurnPathBuilder.DriveLeg turning = TurnPathBuilder.DriveToNearest(v, start, new Point2(w + 2, 4.5), out Point2 onTurned, wheels: fullLock)!;
            double nx = -Math.Sin(fullLock), ny = Math.Cos(fullLock);
            OnEdge(Dist(onTurned, new Point2(w + nx * rf, ny * rf)), rf);
            Assert.True(TurnPathSimulator.Simulate(turning.Request).WithinMinimumRadius);

            // With the wheels set before moving off, the car turns about a centre on the line of its rear axle.
            var beside = new Point2(w + 2, 3);
            Assert.Null(TurnPathBuilder.DriveTo(v, start, beside, turnWheelsWhenStationary: true));
            TurnPathBuilder.DriveLeg set = TurnPathBuilder.DriveToNearest(v, start, beside, out Point2 setTo, turnWheelsWhenStationary: true)!;
            OnEdge(Dist(setTo, new Point2(0, rho)), Math.Sqrt(rho * rho + w * w));
            Assert.NotNull(TurnPathBuilder.DriveTo(v, start, setTo, turnWheelsWhenStationary: true));
            TurnPathResult rs = TurnPathSimulator.Simulate(set.Request);
            Assert.True(rs.WithinMinimumRadius);
            Assert.Equal(0, Dist(rs.Poses[rs.Poses.Count - 1].ToWorld(v.FrontAxleCentre), setTo), 0.03);

            // Nowhere near to go: the edge of the circle beside the front of the car would take a loop
            // to get to, and a point on top of the front axle is too close to move to.
            Assert.Null(TurnPathBuilder.DriveToNearest(v, start, new Point2(w - 1, 1.5), out _, reverse: false));
            Assert.Null(TurnPathBuilder.DriveToNearest(v, start, new Point2(w, 0), out _));
        }

        [Fact]
        public void ThreePointTurnLegsChainFromEachOthersEndPose()
        {
            DesignVehicle v = DesignVehicle.B99;
            var pose = new VehiclePose(new Point2(0, 0), 0);
            var targets = new[] { new Point2(12, 5), new Point2(6, -4), new Point2(14, 12) };
            var results = new List<TurnPathResult>();
            foreach (Point2 t in targets)
            {
                TurnPathBuilder.DriveLeg? leg = TurnPathBuilder.DriveTo(v, pose, t);
                Assert.True(leg != null, $"no leg to {t}");
                Assert.True(leg!.ArcDeg <= TurnPathBuilder.MaxLegTurnDeg);
                TurnPathResult r = TurnPathSimulator.Simulate(leg.Request);
                Assert.True(r.WithinMinimumRadius);
                results.Add(r);
                pose = r.Poses[r.Poses.Count - 1];
            }

            Assert.False(results[0].Reverse);
            Assert.True(results[1].Reverse);
            // Each leg keeps its own swept path; the middle one starts where the first ended.
            Assert.Equal(0, Dist(results[1].Poses[0].RearAxle, results[0].Poses[results[0].Poses.Count - 1].RearAxle), 1e-9);
            Assert.All(results, r => Assert.True(r.LeftBody.Count > 3 && r.RightBody.Count > 3));
        }

        [Theory]
        [InlineData("B85", 5.8)]
        [InlineData("B99", 6.3)]
        [InlineData("B99", 8.0)]
        public void ATurnDrivenInSeveralLegsIsTheSameTurn(string name, double radius)
        {
            // A long turn to the left at the radius: the front axle goes round its circle. Clicking
            // points along that circle, one leg each, has to give the same turn as one click at its end.
            TurnPathConventions c = Conventions();
            DesignVehicle v = c.Vehicle(name);
            double rho = v.RearAxleRadius(radius), front = Math.Sqrt(rho * rho + v.Wheelbase * v.Wheelbase);
            double fullLock = Math.Atan(v.Wheelbase / rho);
            var centre = new Point2(v.Wheelbase, front);
            Point2 On(double deg) => new Point2(centre.X + front * Math.Sin(deg * Math.PI / 180), centre.Y - front * Math.Cos(deg * Math.PI / 180));
            ManoeuvrePlan Drive(params double[] degrees)
            {
                var r = new ManoeuvreRecord { Vehicle = name, Mode = "drive", Start = new[] { 0.0, 0 }, HeadingDeg = 0, Radius = radius };
                r.Stops.AddRange(degrees.Select(d => new DriveStop { Point = new[] { On(d).X, On(d).Y } }));
                return ManoeuvrePlanner.Plan(r, c);
            }

            ManoeuvrePlan one = Drive(150), five = Drive(30, 60, 90, 120, 150), many = Drive(Enumerable.Range(1, 30).Select(i => i * 5.0).ToArray());
            VehiclePose end = one.Legs[0].Result.Poses[one.Legs[0].Result.Poses.Count - 1];
            foreach (ManoeuvrePlan plan in new[] { five, many })
            {
                TurnPathResult last = plan.Legs[plan.Legs.Count - 1].Result;
                VehiclePose stops = last.Poses[last.Poses.Count - 1];
                Assert.True(Dist(end.RearAxle, stops.RearAxle) < 0.002, $"{plan.Legs.Count} legs end {Dist(end.RearAxle, stops.RearAxle)} m from where one leg does");
                Assert.Equal(end.Heading, stops.Heading, 3);
                Assert.All(plan.Legs, l => Assert.True(l.Result.WithinMinimumRadius));
                Assert.Empty(plan.Warnings);

                // Every leg sets off the way the last one left the front wheels pointing: no kink in the
                // front axle's path, and the same tightest turn as the single leg.
                for (int i = 1; i < plan.Legs.Count; i++)
                {
                    Assert.Equal(0, Math.IEEERemainder(plan.Legs[i].Result.Path.TangentAt(0) - plan.Legs[i - 1].Result.EndWheelHeading, 2 * Math.PI), 6);
                }

                Assert.Equal(one.Legs[0].Result.TightestRadius, plan.Legs.Min(l => l.Result.TightestRadius), 2);
                Assert.Equal(one.Legs[0].Result.NetTurnDeg, plan.Legs.Sum(l => l.Result.NetTurnDeg), 2);
            }

            // The wheels: straight at the start, wound on through the turn towards the lock, never past it.
            TurnPathResult turn = one.Legs[0].Result;
            double steer = Math.IEEERemainder(turn.EndWheelHeading - end.Heading, 2 * Math.PI);
            Assert.InRange(steer, 0.9 * fullLock, fullLock + 1e-9);
            Assert.All(five.Legs, l =>
            {
                VehiclePose at = l.Result.Poses[l.Result.Poses.Count - 1];
                Assert.InRange(Math.IEEERemainder(l.Result.EndWheelHeading - at.Heading, 2 * Math.PI), 0, fullLock + 1e-9);
            });

            // Out of the turn and straight into one the other way: the leg starts along the wheels,
            // not along the body, and the steering comes back through straight without passing the lock.
            Point2 nose = end.ToWorld(v.Wheelbase, 0);
            var across = new Point2(nose.X + 14 * Math.Cos(turn.EndWheelHeading - 1.2), nose.Y + 14 * Math.Sin(turn.EndWheelHeading - 1.2));
            TurnPathBuilder.DriveLeg right = TurnPathBuilder.DriveTo(v, end, across, radius, wheels: turn.EndWheelHeading)!;
            Assert.False(right.Left);
            Assert.Equal(0, Math.IEEERemainder(right.Request.Path.TangentAt(0) - turn.EndWheelHeading, 2 * Math.PI), 9);
            TurnPathResult bend = TurnPathSimulator.Simulate(right.Request);
            Assert.True(bend.WithinMinimumRadius, $"tightest {bend.TightestRadius}");
            Assert.True(Dist(bend.Poses[bend.Poses.Count - 1].ToWorld(v.Wheelbase, 0), across) < 1e-6);

            // Wheels said to be further over than the lock are taken as on it.
            TurnPathBuilder.DriveLeg over = TurnPathBuilder.DriveTo(v, end, across, radius, wheels: end.Heading + 1.2)!;
            Assert.Equal(fullLock, Math.IEEERemainder(over.Request.Path.TangentAt(0) - end.Heading, 2 * Math.PI), 9);

            // A leg with the wheels straight still starts along the body, and after a reverse they are straight.
            Assert.Equal(0, Math.IEEERemainder(TurnPathBuilder.DriveTo(v, end, across, radius)!.Request.Path.TangentAt(0) - end.Heading, 2 * Math.PI), 9);
            TurnPathResult back = TurnPathSimulator.Simulate(TurnPathBuilder.DriveTo(v, end, end.ToWorld(-6, 2.5), radius)!.Request);
            Assert.True(back.Reverse);
            Assert.Equal(0, Math.IEEERemainder(back.EndWheelHeading - back.Poses[back.Poses.Count - 1].Heading, 2 * Math.PI), 9);
        }

        [Theory]
        [InlineData("B85", 5.8)]
        [InlineData("B99", 6.3)]
        [InlineData("Service 8.8 m", 12.5)]
        public void WheelsTurnedWhenStationaryPutTheVehicleOnItsTurningCircleFromTheStart(string name, double radius)
        {
            TurnPathConventions c = Conventions();
            DesignVehicle v = c.Vehicle(name);
            double w = v.Wheelbase, rho = v.RearAxleRadius(radius), front = Math.Sqrt(rho * rho + w * w), fullLock = Math.Atan(w / rho);
            var start = new VehiclePose(new Point2(0, 0), 0);
            var centre = new Point2(0, rho); // the turning centre at full left lock, on the line of the rear axle
            var behind = new Point2(w, 2 * front + 3); // most of a half turn away

            // Turn then straight: full lock from the first metre, so the rear axle goes round the
            // turning centre at its radius and the outer front wheel at the vehicle's.
            TurnPathBuilder.DriveLeg set = TurnPathBuilder.DriveTo(v, start, behind, radius, turns: TurnPathBuilder.DriveTurns.Corner, turnWheelsWhenStationary: true)!;
            Assert.True(set.Left);
            Assert.Equal(fullLock, Math.IEEERemainder(set.Request.Path.TangentAt(0), 2 * Math.PI), 9);
            TurnPathResult turned = TurnPathSimulator.Simulate(set.Request);
            Assert.True(turned.WithinMinimumRadius, $"tightest {turned.TightestRadius}");
            Assert.Equal(radius, turned.TightestRadius, 2);
            Assert.True(Dist(turned.Poses[turned.Poses.Count - 1].ToWorld(w, 0), behind) < 1e-6);
            double arc = set.ArcDeg * Math.PI / 180 * front;
            for (int i = 0; i < turned.Poses.Count && turned.Chainages[i] <= arc; i++)
            {
                Assert.Equal(rho, Dist(turned.Poses[i].RearAxle, centre), 3);
            }

            // Wheels straight at the start, the lock wound on while rolling: the same destination by
            // a wider way round, the rear axle swinging outside that circle.
            TurnPathBuilder.DriveLeg rolling = TurnPathBuilder.DriveTo(v, start, behind, radius, turns: TurnPathBuilder.DriveTurns.Corner)!;
            Assert.Equal(0, Math.IEEERemainder(rolling.Request.Path.TangentAt(0), 2 * Math.PI), 9);
            TurnPathResult rolled = TurnPathSimulator.Simulate(rolling.Request);
            Assert.True(rolled.Poses.Max(p => p.RearAxle.X) > turned.Poses.Max(p => p.RearAxle.X) + 0.2);
            Assert.True(rolled.Poses.Max(p => Dist(p.RearAxle, centre)) > rho + 0.2);

            // The wheels the last leg left count for nothing: the leg is the same whatever they were.
            TurnPathBuilder.DriveLeg ignoring = TurnPathBuilder.DriveTo(v, start, behind, radius, turns: TurnPathBuilder.DriveTurns.Corner, wheels: -fullLock, turnWheelsWhenStationary: true)!;
            Assert.Equal(set.Request.Path.Length, ignoring.Request.Path.Length, 9);
            Assert.Equal(set.ArcDeg, ignoring.ArcDeg, 9);

            // One arc to the destination: the wheels are set to whatever takes the front axle there
            // about a single centre, and the vehicle turns exactly as far as the arc does.
            foreach (Point2 to in new[] { new Point2(w + 9, 5), new Point2(w + 6, -7), new Point2(3, 2 * front - 0.5) })
            {
                TurnPathBuilder.DriveLeg leg = TurnPathBuilder.DriveTo(v, start, to, radius, turnWheelsWhenStationary: true)!;
                Assert.Equal(0, leg.StraightLength, 9);
                Assert.Equal(to.Y > 0, leg.Left);
                double r = (to.X * to.X + to.Y * to.Y - w * w) / (2 * Math.Abs(to.Y));
                Assert.True(r >= rho);
                var about = new Point2(0, r * Math.Sign(to.Y));
                TurnPathResult res = TurnPathSimulator.Simulate(leg.Request);
                Assert.True(res.WithinMinimumRadius);
                Assert.All(res.Poses, p => Assert.Equal(r, Dist(p.RearAxle, about), 3));
                VehiclePose end = res.Poses[res.Poses.Count - 1];
                Assert.True(Dist(end.ToWorld(w, 0), to) < 1e-6);
                Assert.Equal(leg.ArcDeg, Math.Abs(res.NetTurnDeg), 2);
            }

            // Straight ahead is straight ahead, and a destination inside the turning circle has no leg.
            TurnPathBuilder.DriveLeg ahead = TurnPathBuilder.DriveTo(v, start, new Point2(w + 8, 0), radius, turnWheelsWhenStationary: true)!;
            Assert.Equal(0, ahead.ArcDeg, 9);
            Assert.Equal(8, ahead.StraightLength, 9);
            Assert.Null(TurnPathBuilder.DriveTo(v, start, new Point2(w * 0.8, rho * 0.5), radius, reverse: false, turnWheelsWhenStationary: true));

            // Reversing is the same either way: the rear axle leads, and goes straight onto its circle.
            Point2 back = start.ToWorld(-2 * rho, 0.6 * rho);
            Assert.Equal(TurnPathBuilder.DriveTo(v, start, back, radius)!.Request.Path.Length,
                TurnPathBuilder.DriveTo(v, start, back, radius, turnWheelsWhenStationary: true)!.Request.Path.Length, 9);
        }

        [Fact]
        public void ADriveWithWheelsTurnedWhenStationaryStartsEveryLegOnItsCircle()
        {
            TurnPathConventions c = Conventions();
            Assert.False(c.DriveTurnWheelsWhenStationary);
            DesignVehicle v = c.Vehicle("B85");
            double w = v.Wheelbase, rho = v.RearAxleRadius(5.8), front = Math.Sqrt(rho * rho + w * w);
            ManoeuvrePlan Drive(bool stationary)
            {
                var r = new ManoeuvreRecord { Vehicle = "B85", Mode = "drive", Start = new[] { 0.0, 0 }, HeadingDeg = 0, Radius = 5.8, Turns = "corner", TurnWheelsWhenStationary = stationary };
                r.Stops.Add(new DriveStop { Point = new[] { w, 2 * front + 3 } });     // round to the left
                r.Stops.Add(new DriveStop { Point = new[] { -12.0, 12.0 } });          // and left again
                return ManoeuvrePlanner.Plan(ManoeuvreRecord.FromJson(r.ToJson()), c);
            }

            ManoeuvrePlan set = Drive(true), rolling = Drive(false);
            Assert.Equal(2, set.Legs.Count);
            Assert.All(set.Legs.Concat(rolling.Legs), l => Assert.False(l.Result.Reverse));
            Assert.Empty(set.Warnings);
            Assert.All(set.Legs, l =>
            {
                // On full lock from the first step of each leg.
                Assert.Equal(5.8, v.OuterWheelRadius(1 / Math.Abs(l.Result.Curvatures[0])), 2);
                Assert.Equal(5.8, l.Result.TightestRadius, 2);
            });

            // Carried over instead, the first leg starts straight and the second as the first left the wheels.
            Assert.True(Math.Abs(rolling.Legs[0].Result.Curvatures[0]) < 0.1 / rho, $"starts at {rolling.Legs[0].Result.Curvatures[0]} of {1 / rho}");
            Assert.Equal(0, Math.IEEERemainder(rolling.Legs[1].Result.Path.TangentAt(0) - rolling.Legs[0].Result.EndWheelHeading, 2 * Math.PI), 6);
            Assert.True(rolling.Legs[0].Result.Poses.Max(p => p.RearAxle.X) > set.Legs[0].Result.Poses.Max(p => p.RearAxle.X) + 0.2);

            // Saved with the path, and off unless it says so.
            Assert.Contains("\"turn_wheels_when_stationary\":true", new ManoeuvreRecord { TurnWheelsWhenStationary = true }.ToJson());
            Assert.False(ManoeuvreRecord.FromJson("{\"mode\":\"drive\",\"start\":[1,2]}").TurnWheelsWhenStationary);
        }

        [Fact]
        public void CurveIsRunFromTheEndNearestThePick()
        {
            var spec = new LineSpec { Start = new Point2(0, 0), End = new Point2(20, 0) };
            Assert.Equal(20, TurnPathBuilder.FromCurve(spec, new Point2(19, 1)).Start.X, 9);
            Assert.Equal(0, TurnPathBuilder.FromCurve(spec, new Point2(1, 1)).Start.X, 9);
            Assert.Equal(0, TurnPathBuilder.FromCurve(spec, null).Start.X, 9);
            Assert.Throws<ArgumentException>(() => TurnPathBuilder.FromCurve(new TextSpec(), null));
        }

        // --------------------------------------------------------------- clean-up

        [Fact]
        public void OffsetChainRoundsConvexCornersAndMitresConcaveOnes()
        {
            var pts = new List<Point2> { new Point2(0, 0), new Point2(10, 0), new Point2(10, 10) };
            // Left of travel: east then north turns left, so the left offset is the inside: a mitre at (9, 1).
            List<Point2> left = TurnPathSimulator.OffsetChain(pts, 1, leftSide: true);
            Assert.Contains(left, p => Dist(p, new Point2(9, 1)) < 1e-9);
            Assert.Equal(3, left.Count);
            // Right of travel is the outside: a quarter-circle of radius 1 about the corner.
            List<Point2> right = TurnPathSimulator.OffsetChain(pts, 1, leftSide: false);
            Assert.True(right.Count > 10);
            foreach (Point2 p in right.Skip(1).Take(right.Count - 2))
            {
                Assert.Equal(1, Dist(p, new Point2(10, 0)), 9);
            }

            Assert.Equal(0, Dist(right[0], new Point2(0, -1)), 1e-9);
            Assert.Equal(0, Dist(right[right.Count - 1], new Point2(11, 10)), 1e-9);
        }

        [Fact]
        public void CleanUpRemovesBacktracksAndSmallLoops()
        {
            var back = new List<Point2> { new Point2(0, 0), new Point2(5, 0), new Point2(2, 0), new Point2(3, 0), new Point2(8, 0), new Point2(8, 5) };
            List<Point2> cleaned = TurnPathSimulator.RemoveBacktracks(back, 1e-6);
            Assert.Equal(new[] { 0.0, 5, 8, 8 }, cleaned.Select(p => p.X).ToArray());
            // A short spike forward then a longer move back: the spike goes.
            var spike = new List<Point2> { new Point2(0, 0), new Point2(1, 0), new Point2(-5, 0), new Point2(-5, 3) };
            Assert.Equal(new[] { 0.0, -5, -5 }, TurnPathSimulator.RemoveBacktracks(spike, 1e-6).Select(p => p.X).ToArray());

            var loop = new List<Point2> { new Point2(0, 0), new Point2(4, 0), new Point2(4, 1), new Point2(3, 1), new Point2(3, -1), new Point2(10, -1) };
            List<Point2> noLoop = TurnPathSimulator.RemoveLoops(loop, 100);
            Assert.Equal(4, noLoop.Count);
            Assert.Equal(0, Dist(noLoop[1], new Point2(3, 0)), 1e-9);
            Assert.Equal(0, Dist(noLoop[2], new Point2(3, -1)), 1e-9);

            // A loop longer than the limit is left alone.
            Assert.Equal(6, TurnPathSimulator.RemoveLoops(loop, 1).Count);

            var zig = new List<Point2> { new Point2(0, 0), new Point2(1, 0.0004), new Point2(2, 0), new Point2(3, 0.0004), new Point2(4, 0) };
            Assert.Equal(2, TurnPathSimulator.Simplify(zig, 0.001).Count);
        }

        // --------------------------------------------------------------- drafter

        [Fact]
        public void DrafterPutsEachPartOnItsLayerAndSplitsLongPolylines()
        {
            TurnPathConventions c = Conventions();
            DesignVehicle v = c.Vehicle("B99");
            TurnPathResult r = SteadyTurn(v, 6.3);
            TurnPathStyle style = TurnPathStyle.From(c);
            style.Layers.SweptPath = "SWEPT {vehicle}";
            style.Draw.WheelPaths = true;
            style.Draw.Hatch = true;
            style.OutlineInterval = 5;
            List<DraftedEntity> drafted = TurnPathDrafter.Draw(r, style);

            Assert.All(drafted.Where(d => d.Group == "swept_path"), d => Assert.Equal("SWEPT B99", d.Spec.Layer));
            Assert.All(drafted.Where(d => d.Group == "clearance"), d => Assert.Equal(c.Layers.Clearance, d.Spec.Layer));
            Assert.Equal(4, drafted.Count(d => d.Group == "wheel_paths"));
            Assert.Single(drafted, d => d.Group == "hatch");
            Assert.Single(drafted, d => d.Group == "labels");
            Assert.Single(drafted, d => d.Group == "path");
            Assert.True(drafted.Count(d => d.Group == "vehicle_intermediate") >= 3);
            // Start and end: body + 4 wheels + 2 axle lines each.
            Assert.Equal(14, drafted.Count(d => d.Group == "vehicle"));
            Assert.All(drafted.Select(d => d.Spec).OfType<PolylineSpec>(), p => Assert.True(p.Points.Count <= TurnPathDrafter.MaxPolylinePoints));
            var label = Assert.IsType<TextSpec>(drafted.Single(d => d.Group == "labels").Spec);
            Assert.Equal("B99", label.Text);
            Assert.Equal(0.3, label.Height);

            // Missing layers are created with the conventions' styles; existing ones are left alone.
            List<LayerSpec> missing = TurnPathDrafter.MissingLayers(
                drafted.Select(d => d.Spec.Layer!), new[] { c.Layers.Clearance }, c.LayerStyles);
            Assert.DoesNotContain(missing, m => m.Name == c.Layers.Clearance);
            Assert.Contains(missing, m => m.Name == "SWEPT B99");
            Assert.Equal("DASHDOT", missing.Single(m => m.Name == c.Layers.Path).Linetype);
        }

        // ------------------------------------------------------------------ tool

    }
}
