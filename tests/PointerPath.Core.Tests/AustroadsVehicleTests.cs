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
    /// <summary>
    /// The Austroads design vehicles shipped in turn-paths.json, checked against the dimensions on
    /// Figures 2.3, 2.4, 2.7, 2.9 and 2.10 of AP-G34-23 (Design Vehicles and Turning Path Templates
    /// Guide, Edition 4.0). Every figure's chain of dimensions has to add up to its overall length.
    /// </summary>
    public class AustroadsVehicleTests
    {
        /// <summary>The shipped vehicles, and the combinations held back from them while trailers are switched off.</summary>
        private static TurnPathConventions Conventions()
        {
            TurnPathConventions c = TurnPathConventions.Load(Path.Combine(AppContext.BaseDirectory, "turn-paths.json"));
            c.AddMissingVehicles(TurnPathConventions.Load(Path.Combine(AppContext.BaseDirectory, "articulated-vehicles.json")));
            return c;
        }

        private static double Dist(Point2 a, Point2 b) => Segment.Dist(a, b);

        [Theory]
        // AS 2890.2-2002 Figure 2.1 and Table 2.1: length, width, front overhang, wheelbase, rear overhang,
        // design turning radius, swept circle, rear axles.
        [InlineData("SRV", 6.4, 2.3, 1.05, 3.8, 1.55, 7.1, 15.3, 1)]
        [InlineData("MRV", 8.8, 2.5, 1.5, 5.0, 2.3, 10.0, 21.6, 1)]
        [InlineData("HRV", 12.5, 2.5, 2.4, 6.6, 3.5, 12.5, 27.8, 2)]
        public void TheCommercialVehiclesAreAsTheStandardDimensionsThem(string name, double length, double width, double front, double wheelbase, double rear, double radius, double sweptCircle, int rearAxles)
        {
            TurnPathConventions c = Conventions();
            DesignVehicle v = c.Vehicle(name);
            Assert.Equal(length, v.Length);
            Assert.Equal(width, v.Width);
            Assert.Equal(front, v.FrontOverhang);
            Assert.Equal(wheelbase, v.Wheelbase);
            Assert.Equal(rear, v.RearOverhang);
            Assert.Equal(length, front + wheelbase + rear, 9);
            Assert.Equal(radius, v.MinTurnRadius);
            Assert.Equal(rearAxles, v.RearAxles);
            Assert.False(v.IsArticulated);
            Assert.Empty(v.Validate());

            // Clause 5.4: 300 mm both sides, as the templates' broken lines.
            Assert.Equal(0.3, v.ClearanceInside);
            Assert.Equal(0.3, v.ClearanceOutside);

            // The tyres are drawn flush with a square body, and the design turning radius is to the
            // outside edge of the outer front tyre (Clause 1.4.22): the side of the body.
            Assert.Equal(v.Width, v.Track + v.WheelWidth, 9);
            Assert.Equal(0, v.CornerInset, 9);
            Assert.True(v.RadiusToTyreEdge);
            Assert.Equal(v.Width / 2, v.OuterWheelOffset, 9);
            Assert.Equal(v.Width / 2, Math.Abs(v.OuterFrontWheel(left: true).Y), 9);
            double rho = v.RearAxleRadius(radius);
            Assert.Equal(radius, Math.Sqrt(Math.Pow(rho + v.Width / 2, 2) + wheelbase * wheelbase), 9);
            Assert.Equal(radius, v.OuterWheelRadius(rho), 9);

            // Table 2.1's swept circle is the circle the outermost corner of the body traces on full
            // lock. It comes out of the other dimensions: within 70 mm of the diameter for the MRV
            // and HRV, and 130 mm over for the SRV (15.43 against the table's 15.3).
            double corner = 2 * Math.Sqrt(Math.Pow(rho + v.Width / 2, 2) + Math.Pow(wheelbase + front, 2));
            Assert.InRange(corner - sweptCircle, 0, name == "SRV" ? 0.14 : 0.07);

            // Driven round a half turn at the design radius it comes up to full lock, and no further.
            TurnPathResult half = Turn(v, radius, 180);
            Assert.True(half.WithinMinimumRadius);
            Assert.Empty(half.Warnings);
            Assert.InRange(half.TightestRadius, radius, radius + 0.1);
            Assert.False(Turn(v, radius - 0.6, 180).WithinMinimumRadius);

            // Figure A1: 150 mm under the body, level for a metre behind the rear axle, then up at 10 degrees.
            (double X, double Z)[] under = v.UndersideTemplate();
            Assert.Equal(5, under.Length);
            Assert.Equal((-1.0, 0.15), under[1]);
            Assert.Equal((0.0, 0.15), under[2]);
            Assert.Equal(-rear, under[0].X, 9);
            Assert.Equal(0.15 + (rear - 1) * Math.Tan(10 * Math.PI / 180), under[0].Z, 9);
            Assert.Equal((wheelbase + front, 0.15), (under[4].X, Math.Round(under[4].Z, 9)));
        }

        [Fact]
        public void TheCommercialVehiclesSweepWiderTheLongerTheyAre()
        {
            TurnPathConventions c = Conventions();
            double Width(string name) => SweptWidth(Turn(c.Vehicle(name), c.Vehicle(name).MinTurnRadius, 90));
            Assert.True(Width("SRV") > SweptWidth(Turn(c.Vehicle("B99"), 7.1, 90)) + 0.5);
            Assert.True(Width("MRV") > Width("SRV") + 0.3);
            Assert.True(Width("HRV") > Width("MRV") + 0.5);

            // The HRV has a rear pair 1.4 m apart (Figure A1), the others one rear axle.
            int Wheels(string name) => TurnPathDrafter.VehicleOutline(c.Vehicle(name), new VehiclePose(new Point2(0, 0), 0), "V", wheels: true)
                .Count(e => e is PolylineSpec p && p.Closed && p.Points.Count == 4);
            Assert.Equal(4, Wheels("SRV"));
            Assert.Equal(4, Wheels("MRV"));
            Assert.Equal(6, Wheels("HRV"));
            Assert.Equal(1.4, c.Vehicle("HRV").AxleSpacing);

            // Measuring the radius to the wheel's centre instead would turn the same vehicle wider.
            DesignVehicle centre = c.Vehicle("MRV").Clone();
            centre.RadiusToTyreEdge = false;
            Assert.Equal(c.Vehicle("MRV").RearAxleRadius(10) + 0.125, centre.RearAxleRadius(10), 9);

            // The cars and the Austroads trucks are measured as before, and a car's underside still rises from the axle.
            Assert.All(new[] { "B99", "B85", "Service 8.8 m", "Truck 12.5 m" }, n => Assert.False(c.Vehicle(n).RadiusToTyreEdge));
            Assert.Equal(4, c.Vehicle("B85").UndersideTemplate().Length);
        }

        [Fact]
        public void TheRigidTrucksAreAsDimensioned()
        {
            TurnPathConventions c = Conventions();

            // Figure 2.3: 2300 behind the rear axle, 5000 wheelbase, 1500 ahead of the front axle, 2500 wide.
            DesignVehicle service = c.Vehicle("Service 8.8 m");
            Assert.Equal(2.3, service.RearOverhang);
            Assert.Equal(5.0, service.Wheelbase);
            Assert.Equal(1.5, service.FrontOverhang);
            Assert.Equal(8.8, service.RearOverhang + service.Wheelbase + service.FrontOverhang, 9);
            Assert.Equal(8.8, service.Length);
            Assert.Equal(2.5, service.Width);
            Assert.Equal(9, service.MinTurnRadius);          // the smallest template: 9 m radius
            Assert.Equal(1, service.RearAxles);

            // Figure 2.4: 3450 behind the centre of the rear pair, 6850 to the front axle, 2200 ahead of it.
            DesignVehicle truck = c.Vehicle("Truck 12.5 m");
            Assert.Equal(3.45, truck.RearOverhang);
            Assert.Equal(6.85, truck.Wheelbase);
            Assert.Equal(2.2, truck.FrontOverhang);
            Assert.Equal(12.5, truck.RearOverhang + truck.Wheelbase + truck.FrontOverhang, 9);
            Assert.Equal(12.5, truck.Length);
            Assert.Equal(12.5, truck.MinTurnRadius);
            Assert.Equal(2, truck.RearAxles);
            Assert.Equal(1.3, truck.AxleSpacing);

            foreach (DesignVehicle v in new[] { service, truck })
            {
                Assert.Empty(v.Validate());
                Assert.False(v.IsArticulated);
                // Square bodies: they sweep with their real corners. Clearance 0.5 m (Section 3.4.1).
                Assert.Equal(v.Width, v.EffectiveWidth);
                Assert.Equal(v.FrontOverhang, v.EffectiveFrontOverhang);
                Assert.Equal(v.RearOverhang, v.EffectiveRearOverhang);
                Assert.Equal(0.5, v.ClearanceInside);
                Assert.Equal(0.5, v.ClearanceOutside);
                Assert.Equal(v.Length, v.OverallLength, 9);
            }
        }

        [Fact]
        public void TheCombinationsAddUpToTheirOverallLengths()
        {
            TurnPathConventions c = Conventions();

            // Figure 2.7: 1600 + 5300 to the centre of the drive axles; kingpin 200 ahead of it; trailer
            // 1400 ahead of the kingpin, 9500 to the centre of its three axles, 2800 behind them.
            DesignVehicle semi = c.Vehicle("Semi 19 m");
            Assert.Equal(1.6, semi.FrontOverhang);
            Assert.Equal(5.3, semi.Wheelbase);
            Assert.Equal(0.2, semi.Hitch);
            TrailerUnit trailer = Assert.Single(semi.Trailers);
            Assert.Equal(1.4, trailer.FrontOverhang);
            Assert.Equal(9.5, trailer.Wheelbase);
            Assert.Equal(2.8, trailer.RearOverhang);
            Assert.Equal(13.7, trailer.Length, 9);
            Assert.Equal(3, trailer.Axles);
            Assert.Equal(19.0, semi.OverallLength, 9);
            Assert.Equal(1.6 + 5.3 - 0.2 + 9.5 + 2.8, semi.OverallLength, 9);

            // Figure 2.9: 1000 + 4000; kingpin 400 ahead of the drive axles; A-trailer 900 / 8100 with the
            // B-trailer's kingpin over its axles; B-trailer 200 / 9400 / 2900.
            DesignVehicle b25 = c.Vehicle("B-double 25 m");
            Assert.Equal(1.0, b25.FrontOverhang);
            Assert.Equal(4.0, b25.Wheelbase);
            Assert.Equal(0.4, b25.Hitch);
            Assert.Equal(2, b25.Trailers.Count);
            Assert.Equal(0.9, b25.Trailers[0].FrontOverhang);
            Assert.Equal(8.1, b25.Trailers[0].Wheelbase);
            Assert.Equal(0, b25.Trailers[0].Hitch);
            Assert.Equal(0.2, b25.Trailers[1].FrontOverhang);
            Assert.Equal(9.4, b25.Trailers[1].Wheelbase);
            Assert.Equal(2.9, b25.Trailers[1].RearOverhang);
            Assert.Equal(12.5, b25.Trailers[1].Length, 9);
            Assert.Equal(25.0, b25.OverallLength, 9);
            Assert.Equal(1.0 + 4.0 - 0.4 + 8.1 + 9.4 + 2.9, b25.OverallLength, 9);

            // Figure 2.10: 1400 + 4500 with the kingpin over the drive axles; A-trailer 1400 / 8050;
            // B-trailer 450 / 9150 / 2900 (12500 long).
            DesignVehicle b26 = c.Vehicle("B-double 26 m");
            Assert.Equal(1.4, b26.FrontOverhang);
            Assert.Equal(4.5, b26.Wheelbase);
            Assert.Equal(0, b26.Hitch);
            Assert.Equal(1.4, b26.Trailers[0].FrontOverhang);
            Assert.Equal(8.05, b26.Trailers[0].Wheelbase);
            Assert.Equal(0.45, b26.Trailers[1].FrontOverhang);
            Assert.Equal(9.15, b26.Trailers[1].Wheelbase);
            Assert.Equal(2.9, b26.Trailers[1].RearOverhang);
            Assert.Equal(12.5, b26.Trailers[1].Length, 9);
            Assert.Equal(26.0, b26.OverallLength, 9);
            Assert.Equal(1.4 + 4.5 + 8.05 + 9.15 + 2.9, b26.OverallLength, 9);

            foreach (DesignVehicle v in new[] { semi, b25, b26 })
            {
                Assert.Empty(v.Validate());
                Assert.True(v.IsArticulated);
                Assert.Equal(2.5, v.Width);
                Assert.All(v.Trailers, t => Assert.Equal(2.5, t.Width));
                Assert.Equal(12.5, v.MinTurnRadius);          // the smallest template: 12.5 m radius
                Assert.Equal(70, v.MaxArticulationDeg);       // Section 3.5
                Assert.Equal(0.5, v.ClearanceInside);
            }

            Assert.Contains("towing a 13.7 m semi-trailer (19 m overall)", c.Describe());
        }

        /// <summary>The swept width of a left turn: the outer edge's radius less the inner edge's, about the turn centre.</summary>
        private static double SweptWidth(TurnPathResult r)
        {
            Point2 centre = r.Path.Segments.OfType<ArcSegment>().First().Center;
            double inner = r.LeftBody.Min(p => Dist(p, centre));
            double outer = r.RightBody.Where(p => p.X > centre.X - 2 && p.Y < centre.Y + 2).Max(p => Dist(p, centre));
            return outer - inner;
        }

        private static TurnPathResult Turn(DesignVehicle v, double radius, double angle) =>
            TurnPathSimulator.Simulate(TurnPathBuilder.Template(new TurnTemplate
            {
                Vehicle = v,
                Start = new Point2(0, 0),
                Heading = 0,
                Radius = radius,
                TurnAngleDeg = angle,
                Left = true,
                LeadIn = 0,
                LeadOut = 30,
            }));

        [Fact]
        public void EachTurnsAtItsSmallestTemplateRadius()
        {
            TurnPathConventions c = Conventions();
            var widths = new Dictionary<string, double>();
            foreach (string name in new[] { "Service 8.8 m", "Truck 12.5 m", "Semi 19 m", "B-double 25 m", "B-double 26 m" })
            {
                DesignVehicle v = c.Vehicle(name);
                TurnPathResult r = Turn(v, v.MinTurnRadius, 90);
                Assert.True(r.WithinMinimumRadius, name);
                Assert.Empty(r.Warnings);
                Assert.Equal(90, r.NetTurnDeg, 0);
                // A long wheelbase is still settling into the turn after 90 degrees, so the tightest
                // turn the body makes is at, or a little easier than, the radius its front wheel follows.
                Assert.InRange(r.TightestRadius, v.MinTurnRadius - 0.01, v.MinTurnRadius * 1.15);
                Assert.Equal(v.Trailers.Count, r.Trailers.Count);
                widths[name] = SweptWidth(r);

                // The combinations stay inside the 70 degree limit even through a half turn at this radius.
                if (v.IsArticulated)
                {
                    Assert.InRange(r.MaxArticulationDeg, 25, 45);
                    TurnPathResult half = Turn(v, v.MinTurnRadius, 180);
                    Assert.InRange(half.MaxArticulationDeg, 45, 70);
                    Assert.Empty(half.Warnings);
                }
            }

            // The longer the vehicle, the wider it sweeps.
            Assert.InRange(widths["Service 8.8 m"], 4.5, 4.9);
            Assert.InRange(widths["Truck 12.5 m"], 5.3, 5.7);
            Assert.True(widths["Semi 19 m"] > widths["Truck 12.5 m"] + 1.5);
            Assert.True(widths["B-double 25 m"] > widths["Semi 19 m"] + 0.8);
            Assert.True(widths["B-double 26 m"] > widths["B-double 25 m"]);

            // A cross-check from outside the guide. The guide's Table 2.1 names the 19 m semi-trailer
            // as the prescriptive vehicle for PBS Level 1 and the 26 m B-double for Level 2; the PBS
            // scheme limits the swept path in a 90 degree turn at 12.5 m to 7.4 m and 8.7 m for
            // those levels. Each vehicle should sit just inside its limit.
            Assert.InRange(widths["Semi 19 m"], 7.1, 7.4);
            Assert.InRange(widths["B-double 26 m"], 8.4, 8.7);

            // A larger radius sweeps a narrower path.
            Assert.True(SweptWidth(Turn(c.Vehicle("Semi 19 m"), 15, 90)) < widths["Semi 19 m"] - 0.3);

            // Below the smallest template radius it says so.
            Assert.False(Turn(c.Vehicle("Truck 12.5 m"), 10, 90).WithinMinimumRadius);
        }

        [Fact]
        public void TheyAreDrawnWithTheirAxleGroups()
        {
            TurnPathConventions c = Conventions();
            int Wheels(string name) => TurnPathDrafter.VehicleOutline(c.Vehicle(name), new VehiclePose(new Point2(0, 0), 0), "V", wheels: true)
                .Count(e => e is PolylineSpec p && p.Closed && p.Points.Count == 4);
            int Bodies(string name) => TurnPathDrafter.VehicleOutline(c.Vehicle(name), new VehiclePose(new Point2(0, 0), 0), "V", wheels: false).Count;

            Assert.Equal(4, Wheels("Service 8.8 m"));             // two axles
            Assert.Equal(6, Wheels("Truck 12.5 m"));              // steer axle and a rear pair
            Assert.Equal(6 + 6, Wheels("Semi 19 m"));             // prime mover, and three axles on the trailer
            Assert.Equal(6 + 6 + 6, Wheels("B-double 25 m"));
            Assert.Equal(1, Bodies("Truck 12.5 m"));
            Assert.Equal(2, Bodies("Semi 19 m"));
            Assert.Equal(3, Bodies("B-double 26 m"));

            // Parked in a line, the 26 m B-double is 26 m from nose to tail.
            var points = TurnPathDrafter.VehicleOutline(c.Vehicle("B-double 26 m"), new VehiclePose(new Point2(0, 0), 0), "V", wheels: false)
                .OfType<PolylineSpec>().SelectMany(p => p.Points).ToList();
            Assert.Equal(26.0, points.Max(p => p.X) - points.Min(p => p.X), 6);
            Assert.Equal(2.5, points.Max(p => p.Y) - points.Min(p => p.Y), 6);

            // The rear pair's wheels sit further in than the steer axle's (dual tyres).
            TurnPathResult r = Turn(c.Vehicle("Truck 12.5 m"), 15, 45);
            Assert.Equal(2.25, Dist(r.WheelPaths[0][0], r.WheelPaths[1][0]), 6);
            Assert.Equal(1.98, Dist(r.WheelPaths[2][0], r.WheelPaths[3][0]), 6);
        }

        [Fact]
        public void ASavedCopyOfTheSettingsPicksUpVehiclesItDoesNotHave()
        {
            // "Save as defaults" writes a copy that is used from then on. One saved before the trucks
            // were added has only the cars, perhaps edited.
            TurnPathConventions saved = TurnPathConventions.Default();
            saved.Vehicles["B99"].ClearanceOutside = 0.45;
            Assert.Equal(2, saved.Vehicles.Count);

            List<string> added = saved.AddMissingVehicles(Conventions());
            Assert.Equal(new[] { "SRV", "MRV", "HRV", "Service 8.8 m", "Truck 12.5 m", "Semi 19 m", "B-double 25 m", "B-double 26 m" }, added);
            Assert.Equal(10, saved.Vehicles.Count);
            Assert.Equal(0.45, saved.Vehicles["B99"].ClearanceOutside);      // their own edits stay
            Assert.Equal(19.0, saved.Vehicle("semi 19 m").OverallLength, 9);

            // Nothing to add the second time, and the copies are their own.
            Assert.Empty(saved.AddMissingVehicles(Conventions()));
            saved.Vehicles["Semi 19 m"].Trailers[0].Wheelbase = 1;
            Assert.Equal(9.5, Conventions().Vehicle("Semi 19 m").Trailers[0].Wheelbase);
        }

    }
}
