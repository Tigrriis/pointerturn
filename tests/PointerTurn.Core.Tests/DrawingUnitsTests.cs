using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using PointerTurn.Core.Drawing;
using PointerTurn.Core.Geometry;
using PointerTurn.Core.Vehicles;
using Xunit;

namespace PointerTurn.Core.Tests
{
    /// <summary>Drawings in millimetres (or anything but metres): the vehicle tools work in metres and convert at the edges.</summary>
    public class DrawingUnitsTests
    {
        [Fact]
        public void UnitsComeFromTheDrawingUnlessTheSettingSaysOtherwise()
        {
            Assert.Equal(1000, DrawingUnits.FromInsunits("Millimeters")!.PerMetre);
            Assert.Equal("millimetres", DrawingUnits.FromInsunits("millimeters")!.Name);
            Assert.Equal(100, DrawingUnits.FromInsunits("Centimeters")!.PerMetre);
            Assert.True(DrawingUnits.FromInsunits("Meters")!.IsMetres);
            Assert.Equal(1 / 0.3048, DrawingUnits.FromInsunits("Feet")!.PerMetre, 9);
            Assert.Null(DrawingUnits.FromInsunits("Undefined"));
            Assert.Null(DrawingUnits.FromInsunits("Unitless"));
            Assert.Null(DrawingUnits.FromInsunits(null));
            Assert.Null(DrawingUnits.FromInsunits("LightYears"));

            // auto: what the drawing says, or metres when it doesn't say.
            Assert.Equal(1000, DrawingUnits.Resolve("auto", "Millimeters").PerMetre);
            Assert.Equal(1000, DrawingUnits.Resolve(null, "Millimeters").PerMetre);
            Assert.True(DrawingUnits.Resolve("auto", "Unitless").IsMetres);
            Assert.True(DrawingUnits.Resolve("", null).IsMetres);

            // An explicit setting wins over the drawing.
            Assert.True(DrawingUnits.Resolve("m", "Millimeters").IsMetres);
            Assert.Equal(1000, DrawingUnits.Resolve("Millimetres", "Meters").PerMetre);
            Assert.Equal(1000, DrawingUnits.Resolve("mm", "Unitless").PerMetre);
            Assert.Throws<ArgumentException>(() => DrawingUnits.Resolve("furlongs", "Meters"));

            // A scale saved with a path.
            Assert.Same(DrawingUnits.Millimetres, DrawingUnits.Of(1000));
            Assert.Same(DrawingUnits.Metres, DrawingUnits.Of(1));
            Assert.Same(DrawingUnits.Metres, DrawingUnits.Of(0));
            Assert.Equal("centimetres", DrawingUnits.Of(100).Name);
            Assert.Equal(250, DrawingUnits.Of(250).PerMetre);
        }

        [Fact]
        public void TheSizeOfTheDrawingQuestionsUnlikelyUnits()
        {
            // A car park drawn in millimetres is tens of thousands of units across; one drawn in metres, tens.
            Assert.Null(DrawingUnits.Millimetres.Doubt(85000));
            Assert.Null(DrawingUnits.Metres.Doubt(85));
            Assert.Null(DrawingUnits.Metres.Doubt(4000));        // a 4 km site
            Assert.Null(DrawingUnits.Metres.Doubt(null));        // an empty drawing
            Assert.Null(DrawingUnits.Metres.Doubt(0));
            Assert.Null(DrawingUnits.Metres.Doubt(8));           // a small detail in metres is fine

            // A metre drawing whose INSUNITS says millimetres: 85 units would be 85 mm.
            string? small = DrawingUnits.Millimetres.Doubt(85);
            Assert.NotNull(small);
            Assert.Contains("0.09 m across", small);
            Assert.Contains("choose Metres", small);

            // A millimetre drawing taken as metres: 85 km across.
            string? large = DrawingUnits.Metres.Doubt(85000);
            Assert.NotNull(large);
            Assert.Contains("85 km", large);
            Assert.Contains("choose Millimetres", large);
        }

        [Fact]
        public void GeometryIsScaledBothWaysAndMetresAreLeftAlone()
        {
            DrawingUnits mm = DrawingUnits.Millimetres;
            Assert.Equal(new Point2(2.5, -0.75), mm.ToModel(new Point2(2500, -750)));
            Assert.Equal(new Point2(2500, -750), mm.ToDrawing(new Point2(2.5, -0.75)));
            Assert.Equal(0.3, mm.ToModel(300), 12);

            var line = new LineSpec { Start = new Point2(1, 2), End = new Point2(3, 4), Layer = "L", ColorIndex = 3, LinetypeScale = 0.5 };
            var scaledLine = (LineSpec)mm.ToDrawing(line);
            Assert.Equal(new Point2(1000, 2000), scaledLine.Start);
            Assert.Equal(new Point2(3000, 4000), scaledLine.End);
            Assert.Equal("L", scaledLine.Layer);
            Assert.Equal((short)3, scaledLine.ColorIndex);
            Assert.Equal(0.5, scaledLine.LinetypeScale);     // relative to the drawing's own: not scaled
            Assert.Equal(new Point2(1, 2), line.Start);       // the original is untouched

            var poly = (PolylineSpec)mm.ToDrawing(new PolylineSpec { Points = { new Point2(0, 0), new Point2(1, 0), new Point2(1, 1) }, Bulges = new List<double> { 0, 0.4142, 0 }, Closed = true });
            Assert.Equal(new Point2(1000, 1000), poly.Points[2]);
            Assert.Equal(0.4142, poly.Bulges![1]);            // a bulge is a shape, not a size
            Assert.True(poly.Closed);

            var arc = (ArcSpec)mm.ToDrawing(new ArcSpec { Center = new Point2(1, 1), Radius = 6.3, StartAngleDeg = 10, EndAngleDeg = 80 });
            Assert.Equal(6300, arc.Radius, 9);
            Assert.Equal(10, arc.StartAngleDeg);
            var circle = (CircleSpec)mm.ToDrawing(new CircleSpec { Center = new Point2(2, 3), Radius = 0.15 });
            Assert.Equal(150, circle.Radius, 9);
            Assert.Equal(new Point2(2000, 3000), circle.Center);

            var text = (TextSpec)mm.ToDrawing(new TextSpec { Position = new Point2(1, 1), Text = "B99", Height = 0.3, RotationDeg = 30, Align = TextAlign.Center });
            Assert.Equal(300, text.Height!.Value, 9);
            Assert.Equal(30, text.RotationDeg);
            Assert.Equal(TextAlign.Center, text.Align);
            Assert.Null(((TextSpec)mm.ToDrawing(new TextSpec { Text = "x" })).Height);    // the drawing's default stays the drawing's default

            var hatch = (HatchSpec)mm.ToDrawing(new HatchSpec { Pattern = "ANSI31", Scale = 0.5, Loops = { new PolylineSpec { Points = { new Point2(0, 0), new Point2(2, 0), new Point2(2, 2) }, Closed = true } } });
            Assert.Equal(500, hatch.Scale, 9);
            Assert.Equal(new Point2(2000, 2000), hatch.Loops[0].Points[2]);
            Assert.Throws<NotSupportedException>(() => mm.ToDrawing(new MTextSpec { Text = "x" }));

            // A curve read from a millimetre drawing, in metres.
            Path2 path = mm.ToModel(new Path2(new List<Segment> { new LineSegment(new Point2(0, 0), new Point2(5000, 0)), new ArcSegment(new Point2(5000, 6300), 6300, -Math.PI / 2, Math.PI / 2) }, false));
            Assert.Equal(5 + 6.3 * Math.PI / 2, path.Length, 9);
            Assert.Equal(11.3, path.End.X, 9);

            Obstruction wall = Assert.Single(mm.ToModel(new[] { new Obstruction("1A", "A-WALL", new Path2(new List<Segment> { new LineSegment(new Point2(0, 4500), new Point2(30000, 4500)) }, false)) }));
            Assert.Equal("1A", wall.Handle);
            Assert.Equal(new Point2(30, 4.5), wall.Path.End);

            // A surface: asked in metres, sampled in drawing units, answered in metres.
            var asked = new List<Point2>();
            Func<IReadOnlyList<Point2>, IReadOnlyList<double?>> surface = mm.ToModel(points =>
            {
                asked.AddRange(points);
                return points.Select(p => p.X > 5000 ? (double?)null : 12500).ToList();
            });
            IReadOnlyList<double?> levels = surface(new[] { new Point2(1, 2), new Point2(9, 9) });
            Assert.Equal(new Point2(1000, 2000), asked[0]);
            Assert.Equal(12.5, levels[0]);
            Assert.Null(levels[1]);

            // In a metre drawing nothing is copied or changed.
            Assert.Same(line, DrawingUnits.Metres.ToDrawing(line));
            Assert.Same(line, DrawingUnits.Metres.ToModel(line));
        }

        [Fact]
        public void ARecordKeepsItsUnitsAndStaysPutWhenTheyChange()
        {
            var r = new ManoeuvreRecord
            {
                Mode = "solve",
                UnitsPerMetre = 1000,
                Start = new[] { 10.0, 5 },
                Target = new[] { 12.0, 9 },
                Stops = { new DriveStop { Point = new[] { 20.0, 0 }, HeadingDeg = 90 } },
                FollowNear = new[] { 1.0, 1 },
                BlockBase = new[] { 10.0, 5 },
                Solved = new List<SolvedMove> { new SolvedMove { Segments = { new[] { 0.0, 0, 1, 0 } } } },
                SolvedFor = "x",
            };
            ManoeuvreRecord back = ManoeuvreRecord.FromJson(r.ToJson());
            Assert.Equal(1000, back.UnitsPerMetre);
            Assert.Contains("\"units_per_metre\":1000", r.ToJson());

            // A path saved before units were kept was drawn in metres.
            Assert.Equal(1, ManoeuvreRecord.FromJson("{\"mode\":\"drive\",\"start\":[1,2]}").UnitsPerMetre);

            // Told it is a metre drawing after all: the positions stay where they are in the drawing
            // (10000, 5000 drawing units), so they are 1000 times further apart in metres, and the
            // manoeuvre must be solved again.
            back.SetUnits(1);
            Assert.Equal(1, back.UnitsPerMetre);
            Assert.Equal(new[] { 10000.0, 5000 }, back.Start);
            Assert.Equal(new[] { 12000.0, 9000 }, back.Target);
            Assert.Equal(new[] { 20000.0, 0 }, back.Stops[0].Point);
            Assert.Equal(90, back.Stops[0].HeadingDeg);
            Assert.Equal(new[] { 1000.0, 1000 }, back.FollowNear);
            Assert.Equal(new[] { 10000.0, 5000 }, back.BlockBase);
            Assert.Null(back.Solved);
            Assert.True(back.NeedsSolving);

            // The same units again changes nothing.
            back.SetUnits(1);
            Assert.Equal(new[] { 10000.0, 5000 }, back.Start);
        }

        // ------------------------------------------------------------------ the tools, in a millimetre drawing

    }
}
