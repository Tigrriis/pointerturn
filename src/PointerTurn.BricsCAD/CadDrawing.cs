using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using PointerTurn.Core.Drawing;
using PointerTurn.Core.Geometry;
#if AUTOCAD
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Colors;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
#else
using Bricscad.ApplicationServices;
using Bricscad.EditorInput;
using Teigha.Colors;
using Teigha.DatabaseServices;
using Teigha.Geometry;
#endif

// This file uses the API the two programs share. The AutoCAD build (src/PointerTurn.AutoCAD)
// compiles it with AUTOCAD defined, which swaps the namespaces above.

namespace PointerTurn.BricsCAD
{
    /// <summary>
    /// What the turning tools read from and draw in a BricsCAD document: layers, entities,
    /// curves, blocks, obstruction linework and surface levels. Every method runs on
    /// BricsCAD's main thread, inside a command, so each command is one UNDO step.
    /// </summary>
    internal sealed class CadDrawing : IBlockMaker, PointerTurn.Core.Vehicles.IObstructionSource
    {
        private const int MaxListedNames = 5000;

        private static readonly int[] ValidLineweights =
            { 0, 5, 9, 13, 15, 18, 20, 25, 30, 35, 40, 50, 53, 60, 70, 80, 90, 100, 106, 120, 140, 158, 200, 211 };

        private readonly Document _doc;

        public CadDrawing(Document doc)
        {
            _doc = doc;
        }

        private Database Db => _doc.Database;

        private Editor Ed => _doc.Editor;

        // ================================================================ read

        public DrawingInfo GetDrawingInfo()
        {
            using (Transaction tr = Db.TransactionManager.StartTransaction())
            {
                var info = new DrawingInfo
                {
                    FileName = Path.GetFileName(_doc.Name),
                    Units = Db.Insunits.ToString(),
                    CurrentSpace = SpaceName(tr),
                    CurrentLayer = ((LayerTableRecord)tr.GetObject(Db.Clayer, OpenMode.ForRead)).Name,
                    CurrentTextStyle = ((TextStyleTableRecord)tr.GetObject(Db.Textstyle, OpenMode.ForRead)).Name,
                    CurrentDimStyle = ((DimStyleTableRecord)tr.GetObject(Db.Dimstyle, OpenMode.ForRead)).Name,
                    DefaultTextHeight = Db.Textsize,
                };

                if (Db.Extmin.X <= Db.Extmax.X && Db.Extmin.Y <= Db.Extmax.Y)
                {
                    info.ExtentsMin = Arr(Db.Extmin);
                    info.ExtentsMax = Arr(Db.Extmax);
                }

                var space = (BlockTableRecord)tr.GetObject(Db.CurrentSpaceId, OpenMode.ForRead);
                foreach (ObjectId _ in space)
                {
                    info.EntityCount++;
                }

                var layers = (LayerTable)tr.GetObject(Db.LayerTableId, OpenMode.ForRead);
                foreach (ObjectId id in layers)
                {
                    var ltr = (LayerTableRecord)tr.GetObject(id, OpenMode.ForRead);
                    if (info.Layers.Count >= MaxListedNames)
                    {
                        break;
                    }

                    info.Layers.Add(new LayerInfo
                    {
                        Name = ltr.Name,
                        Color = ltr.Color.ColorIndex,
                        Linetype = ((LinetypeTableRecord)tr.GetObject(ltr.LinetypeObjectId, OpenMode.ForRead)).Name,
                        LineweightMm = (int)ltr.LineWeight >= 0 ? (int)ltr.LineWeight / 100.0 : (double?)null,
                        Off = ltr.IsOff ? true : (bool?)null,
                        Frozen = ltr.IsFrozen ? true : (bool?)null,
                        Locked = ltr.IsLocked ? true : (bool?)null,
                    });
                }

                info.Linetypes = Names<LinetypeTableRecord>(tr, Db.LinetypeTableId);
                info.TextStyles = Names<TextStyleTableRecord>(tr, Db.TextStyleTableId);
                info.DimStyles = Names<DimStyleTableRecord>(tr, Db.DimStyleTableId);

                var blocks = (BlockTable)tr.GetObject(Db.BlockTableId, OpenMode.ForRead);
                foreach (ObjectId id in blocks)
                {
                    var btr = (BlockTableRecord)tr.GetObject(id, OpenMode.ForRead);
                    if (!btr.IsLayout && !btr.IsAnonymous && !btr.IsFromExternalReference && info.Blocks.Count < MaxListedNames)
                    {
                        info.Blocks.Add(btr.Name);
                    }
                }

                tr.Commit();
                return info;
            }
        }

        // ================================================================ write

        public IReadOnlyList<ItemResult> EnsureLayers(IReadOnlyList<LayerSpec> layers)
        {
            var results = new List<ItemResult>();
            using (_doc.LockDocument())
            using (Transaction tr = Db.TransactionManager.StartTransaction())
            {
                for (int i = 0; i < layers.Count; i++)
                {
                    LayerSpec spec = layers[i];
                    try
                    {
                        var notes = new List<string>();
                        bool created = GetOrCreateLayer(tr, spec.Name, out LayerTableRecord ltr);
                        notes.Add(created ? "created" : "updated");
                        ltr.UpgradeOpen();

                        if (spec.Rgb != null && spec.Rgb.Length == 3)
                        {
                            ltr.Color = Color.FromRgb(spec.Rgb[0], spec.Rgb[1], spec.Rgb[2]);
                        }
                        else if (spec.ColorIndex != null)
                        {
                            ltr.Color = Color.FromColorIndex(ColorMethod.ByAci, spec.ColorIndex.Value);
                        }

                        if (spec.Plot != null)
                        {
                            ltr.IsPlottable = spec.Plot.Value;
                        }

                        if (spec.Linetype != null)
                        {
                            ObjectId ltId = FindOrLoadLinetype(tr, spec.Linetype);
                            if (ltId.IsNull)
                            {
                                notes.Add($"linetype {spec.Linetype} not found, left unchanged");
                            }
                            else
                            {
                                ltr.LinetypeObjectId = ltId;
                            }
                        }

                        if (spec.LineweightMm != null)
                        {
                            int target = (int)Math.Round(spec.LineweightMm.Value * 100);
                            int nearest = ValidLineweights.OrderBy(v => Math.Abs(v - target)).First();
                            ltr.LineWeight = (LineWeight)nearest;
                        }

                        if (spec.Description != null)
                        {
                            ltr.Description = spec.Description;
                        }

                        results.Add(ItemResult.Success(i, ltr.Handle.ToString(), string.Join("; ", notes)));
                    }
                    catch (System.Exception ex)
                    {
                        results.Add(ItemResult.Failure(i, ex.Message));
                    }
                }

                tr.Commit();
            }

            Flush();
            return results;
        }

        public IReadOnlyList<ItemResult> CreateEntities(IReadOnlyList<EntitySpec> entities)
        {
            var results = new List<ItemResult>();
            using (_doc.LockDocument())
            using (Transaction tr = Db.TransactionManager.StartTransaction())
            {
                var space = (BlockTableRecord)tr.GetObject(Db.CurrentSpaceId, OpenMode.ForWrite);
                for (int i = 0; i < entities.Count; i++)
                {
                    try
                    {
                        results.Add(CreateOne(i, entities[i], space, tr));
                    }
                    catch (System.Exception ex)
                    {
                        results.Add(ItemResult.Failure(i, ex.Message));
                    }
                }

                tr.Commit();
            }

            Flush();
            return results;
        }

        private ItemResult CreateOne(int index, EntitySpec spec, BlockTableRecord space, Transaction tr)
        {
            // Resolve anything that can fail before creating objects.
            ObjectId blockId = ObjectId.Null;
            if (spec is BlockInsertSpec bi)
            {
                var bt = (BlockTable)tr.GetObject(Db.BlockTableId, OpenMode.ForRead);
                if (!bt.Has(bi.BlockName))
                {
                    return ItemResult.Failure(index, $"Block '{bi.BlockName}' is not defined in this drawing.");
                }

                blockId = bt[bi.BlockName];
            }

            Entity ent = Build(spec, blockId);
            ent.SetDatabaseDefaults(Db);

            string? note = null;
            if (spec.Layer != null)
            {
                if (GetOrCreateLayer(tr, spec.Layer, out _))
                {
                    note = $"created layer {spec.Layer}";
                }

                ent.Layer = spec.Layer;
            }

            ent.ColorIndex = spec.ColorIndex ?? 256; // 256 = ByLayer
            if (spec.LinetypeScale is double ltScale && ltScale > 0)
            {
                ent.LinetypeScale = ltScale;
            }

            ApplyTextDefaults(ent, spec);

            space.AppendEntity(ent);
            tr.AddNewlyCreatedDBObject(ent, true);

            if (ent is Hatch hatchEnt && spec is HatchSpec hatchSpec)
            {
                try
                {
                    note = Join(note, FinishHatch(hatchEnt, hatchSpec, tr));
                }
                catch (System.Exception)
                {
                    hatchEnt.Erase(); // don't leave an empty hatch behind
                    throw;
                }
            }

            if (ent is DBText text && text.HorizontalMode != TextHorizontalMode.TextLeft)
            {
                text.AdjustAlignment(Db);
            }

            if (ent is BlockReference br && spec is BlockInsertSpec insert)
            {
                note = Join(note, AddAttributes(br, blockId, insert.Attributes, tr));
            }

            return ItemResult.Success(index, ent.Handle.ToString(), note);
        }

        /// <summary>
        /// A transient entity for temporary graphics (jigs): lines, polylines, arcs and circles only.
        /// The caller owns and disposes it; it is never added to the database.
        /// </summary>
        internal static Entity? PreviewEntity(EntitySpec spec)
        {
            switch (spec)
            {
                case LineSpec l:
                    return new Line(P3(l.Start), P3(l.End));

                case PolylineSpec p:
                {
                    var pl = new Polyline(p.Points.Count);
                    for (int i = 0; i < p.Points.Count; i++)
                    {
                        double bulge = p.Bulges != null ? p.Bulges[i] : 0;
                        pl.AddVertexAt(i, new Point2d(p.Points[i].X, p.Points[i].Y), bulge, 0, 0);
                    }

                    pl.Closed = p.Closed;
                    return pl;
                }

                case CircleSpec c:
                    return new Circle(P3(c.Center), Vector3d.ZAxis, c.Radius);

                case ArcSpec a:
                    return new Arc(P3(a.Center), a.Radius, Rad(a.StartAngleDeg), Rad(a.EndAngleDeg));

                default:
                    return null;
            }
        }

        private Entity Build(EntitySpec spec, ObjectId blockId)
        {
            switch (spec)
            {
                case LineSpec l:
                    return new Line(P3(l.Start), P3(l.End));

                case PolylineSpec p:
                {
                    var pl = new Polyline(p.Points.Count);
                    for (int i = 0; i < p.Points.Count; i++)
                    {
                        double bulge = p.Bulges != null ? p.Bulges[i] : 0;
                        pl.AddVertexAt(i, new Point2d(p.Points[i].X, p.Points[i].Y), bulge, 0, 0);
                    }

                    pl.Closed = p.Closed;
                    return pl;
                }

                case CircleSpec c:
                    return new Circle(P3(c.Center), Vector3d.ZAxis, c.Radius);

                case HatchSpec h:
                {
                    // Loops are added once the hatch is in the drawing (FinishHatch).
                    var hatch = new Hatch();
                    hatch.PatternScale = h.Scale;
                    hatch.PatternAngle = Rad(h.AngleDeg);
                    return hatch;
                }

                case ArcSpec a:
                    return new Arc(P3(a.Center), a.Radius, Rad(a.StartAngleDeg), Rad(a.EndAngleDeg));

                case TextSpec _:
                    return new DBText();

                case MTextSpec _:
                    return new MText();

                case LinearDimensionSpec d:
                    return new RotatedDimension(Rad(d.RotationDeg), P3(d.P1), P3(d.P2), P3(d.DimLinePoint), d.TextOverride ?? "", Db.Dimstyle);

                case AlignedDimensionSpec d:
                    return new AlignedDimension(P3(d.P1), P3(d.P2), P3(d.DimLinePoint), d.TextOverride ?? "", Db.Dimstyle);

                case BlockInsertSpec b:
                    return new BlockReference(P3(b.Position), blockId)
                    {
                        ScaleFactors = new Scale3d(b.Scale),
                        Rotation = Rad(b.RotationDeg),
                    };

                default:
                    throw new NotSupportedException(spec.Kind.ToString());
            }
        }

        /// <summary>Text properties are set after SetDatabaseDefaults so they are not overwritten.</summary>
        private void ApplyTextDefaults(Entity ent, EntitySpec spec)
        {
            if (ent is DBText text && spec is TextSpec t)
            {
                text.TextString = t.Text;
                text.Height = t.Height ?? Db.Textsize;
                text.Rotation = Rad(t.RotationDeg);
                text.Position = P3(t.Position);
                switch (t.Align)
                {
                    case TextAlign.Center:
                        text.HorizontalMode = TextHorizontalMode.TextCenter;
                        break;
                    case TextAlign.Right:
                        text.HorizontalMode = TextHorizontalMode.TextRight;
                        break;
                    case TextAlign.MiddleCenter:
                        text.HorizontalMode = TextHorizontalMode.TextCenter;
                        text.VerticalMode = TextVerticalMode.TextVerticalMid;
                        break;
                }

                if (t.Align != TextAlign.Left)
                {
                    text.AlignmentPoint = P3(t.Position);
                }
            }
            else if (ent is MText mtext && spec is MTextSpec m)
            {
                mtext.Location = P3(m.Position);
                mtext.Attachment = AttachmentPoint.TopLeft;
                mtext.TextHeight = m.Height ?? Db.Textsize;
                mtext.Width = m.Width ?? 0;
                mtext.Rotation = Rad(m.RotationDeg);
                mtext.Contents = ToMTextContents(m.Text);
            }
        }

        private static string? AddAttributes(BlockReference br, ObjectId blockId, Dictionary<string, string>? values, Transaction tr)
        {
            var btr = (BlockTableRecord)tr.GetObject(blockId, OpenMode.ForRead);
            if (!btr.HasAttributeDefinitions)
            {
                return values != null && values.Count > 0 ? "block has no attributes; values ignored" : null;
            }

            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (ObjectId id in btr)
            {
                if (!(tr.GetObject(id, OpenMode.ForRead) is AttributeDefinition def) || def.Constant)
                {
                    continue;
                }

                var attr = new AttributeReference();
                attr.SetAttributeFromBlock(def, br.BlockTransform);
                if (values != null && values.TryGetValue(def.Tag, out string? value))
                {
                    attr.TextString = value;
                    used.Add(def.Tag);
                }
                else
                {
                    attr.TextString = def.TextString;
                }

                br.AttributeCollection.AppendAttribute(attr);
                tr.AddNewlyCreatedDBObject(attr, true);
            }

            var unknown = values?.Keys.Where(k => !used.Contains(k)).ToList();
            return unknown != null && unknown.Count > 0 ? "unknown attribute tag(s): " + string.Join(", ", unknown) : null;
        }

        // ================================================================ user input

        /// <summary>Set the pattern and add the boundary loops of a hatch that is already in the drawing.</summary>
        private string? FinishHatch(Hatch hatch, HatchSpec h, Transaction tr)
        {
            hatch.SetHatchPattern(HatchPatternType.PreDefined, h.Pattern.ToUpperInvariant());
            int loops = 0;

            foreach (PolylineSpec loop in h.Loops)
            {
                AppendPointLoop(hatch, loop.Points, loop.Bulges, loops++ == 0);
            }

            if (h.BoundaryHandles.Count > 0)
            {
                hatch.Associative = h.Associative;
                foreach (string handle in h.BoundaryHandles)
                {
                    ObjectId id = ResolveHandle(handle);
                    if (id.IsNull || !(tr.GetObject(id, OpenMode.ForRead) is Curve c) || !c.Closed)
                    {
                        throw new InvalidOperationException($"{handle} is not a closed curve, so it can't bound a hatch.");
                    }

                    hatch.AppendLoop(loops++ == 0 ? HatchLoopTypes.External : HatchLoopTypes.Default, new ObjectIdCollection { id });
                }
            }

            foreach (Point2 seed in h.SeedPoints)
            {
                DBObjectCollection found = Ed.TraceBoundary(new Point3d(seed.X, seed.Y, 0), true);
                if (found == null || found.Count == 0)
                {
                    throw new InvalidOperationException(
                        $"No closed boundary was found around {seed}. The area must be fully enclosed by linework and visible on screen.");
                }

                bool outer = true;
                foreach (DBObject o in found)
                {
                    using (o)
                    {
                        switch (o)
                        {
                            case Polyline pl:
                            {
                                var pts = new List<Point2>();
                                var bulges = new List<double>();
                                for (int i = 0; i < pl.NumberOfVertices; i++)
                                {
                                    Point3d v = pl.GetPoint3dAt(i);
                                    pts.Add(new Point2(v.X, v.Y));
                                    bulges.Add(pl.Normal.Z < 0 ? -pl.GetBulgeAt(i) : pl.GetBulgeAt(i));
                                }

                                AppendPointLoop(hatch, pts, bulges, outer);
                                break;
                            }

                            case Circle circle:
                            {
                                Point2 c = new Point2(circle.Center.X, circle.Center.Y);
                                var pts = new List<Point2> { new Point2(c.X + circle.Radius, c.Y), new Point2(c.X - circle.Radius, c.Y) };
                                AppendPointLoop(hatch, pts, new List<double> { 1, 1 }, outer);
                                break;
                            }

                            default:
                                throw new InvalidOperationException($"The boundary around {seed} came back as a {o.GetType().Name}, which can't be hatched here; draw it with polylines.");
                        }
                    }

                    outer = false;
                    loops++;
                }
            }

            if (loops == 0)
            {
                throw new InvalidOperationException("The hatch has no boundary.");
            }

            hatch.EvaluateHatch(true);
            if (h.SendToBack)
            {
                var space = (BlockTableRecord)tr.GetObject(hatch.OwnerId, OpenMode.ForRead);
                var order = (DrawOrderTable)tr.GetObject(space.DrawOrderTableId, OpenMode.ForWrite);
                order.MoveToBottom(new ObjectIdCollection { hatch.ObjectId });
            }

            double area = 0;
            try
            {
                area = hatch.Area;
            }
            catch (System.Exception)
            {
                // Area isn't available for every hatch.
            }

            return area > 0 ? $"area {Round(area)}" : null;
        }

        private static void AppendPointLoop(Hatch hatch, IReadOnlyList<Point2> points, IReadOnlyList<double>? bulges, bool outer)
        {
            var pts = new Point2dCollection();
            var bs = new DoubleCollection();
            for (int i = 0; i < points.Count; i++)
            {
                pts.Add(new Point2d(points[i].X, points[i].Y));
                bs.Add(bulges != null && i < bulges.Count ? bulges[i] : 0);
            }

            // Close the loop explicitly: hatch polyline loops repeat the first point at the end.
            if (points.Count > 0 && (points[0].X != points[points.Count - 1].X || points[0].Y != points[points.Count - 1].Y))
            {
                pts.Add(new Point2d(points[0].X, points[0].Y));
                bs.Add(0);
            }

            hatch.AppendLoop((outer ? HatchLoopTypes.External : HatchLoopTypes.Default) | HatchLoopTypes.Polyline, pts, bs);
        }

        public IReadOnlyDictionary<string, EntitySpec> GetCurves(IReadOnlyList<string> handles)
        {
            var result = new Dictionary<string, EntitySpec>(StringComparer.OrdinalIgnoreCase);
            using (Transaction tr = Db.TransactionManager.StartTransaction())
            {
                foreach (string h in handles)
                {
                    ObjectId id = ResolveHandle(h);
                    if (id.IsNull || !(tr.GetObject(id, OpenMode.ForRead) is Entity ent))
                    {
                        continue;
                    }

                    EntitySpec? spec = CurveSpec(ent);
                    if (spec != null)
                    {
                        spec.Layer = ent.Layer;
                        spec.ColorIndex = ent.ColorIndex == 256 ? (short?)null : (short)ent.ColorIndex;
                        result[ent.Handle.ToString()] = spec;
                    }
                }

                tr.Commit();
            }

            return result;
        }

        /// <summary>Exact plan geometry of a curve in world X/Y, or null if it isn't a line, arc, circle or polyline.</summary>
        internal static EntitySpec? CurveSpec(Entity ent)
        {
            switch (ent)
            {
                case Line l:
                    return new LineSpec { Start = P2(l.StartPoint), End = P2(l.EndPoint) };

                case Arc a:
                    if (a.Normal.Z >= 0)
                    {
                        return new ArcSpec { Center = P2(a.Center), Radius = a.Radius, StartAngleDeg = Deg(a.StartAngle), EndAngleDeg = Deg(a.EndAngle) };
                    }

                    // Seen from below: it runs clockwise in plan, so swap the ends.
                    return new ArcSpec
                    {
                        Center = P2(a.Center),
                        Radius = a.Radius,
                        StartAngleDeg = Deg(Math.Atan2(a.EndPoint.Y - a.Center.Y, a.EndPoint.X - a.Center.X)),
                        EndAngleDeg = Deg(Math.Atan2(a.StartPoint.Y - a.Center.Y, a.StartPoint.X - a.Center.X)),
                    };

                case Circle c:
                    return new CircleSpec { Center = P2(c.Center), Radius = c.Radius };

                case Polyline p:
                {
                    var spec = new PolylineSpec { Closed = p.Closed, Bulges = new List<double>() };
                    for (int i = 0; i < p.NumberOfVertices; i++)
                    {
                        spec.Points.Add(P2(p.GetPoint3dAt(i)));
                        spec.Bulges.Add(p.Normal.Z < 0 ? -p.GetBulgeAt(i) : p.GetBulgeAt(i));
                    }

                    return spec;
                }

                case Polyline2d p2:
                {
                    var spec = new PolylineSpec { Closed = p2.Closed, Bulges = new List<double>() };
                    foreach (Vertex2d v in p2)
                    {
                        spec.Points.Add(P2(v.Position));
                        spec.Bulges.Add(p2.Normal.Z < 0 ? -v.Bulge : v.Bulge);
                    }

                    return spec;
                }

                case Polyline3d p3:
                {
                    var spec = new PolylineSpec { Closed = p3.Closed };
                    foreach (ObjectId vid in p3)
                    {
                        if (vid.GetObject(OpenMode.ForRead) is PolylineVertex3d v)
                        {
                            spec.Points.Add(P2(v.Position));
                        }
                    }

                    return spec;
                }

                default:
                    return null;
            }
        }

        private static Point2 P2(Point3d p) => new Point2(p.X, p.Y);

        // =========================================================== obstructions

        public IReadOnlyList<PointerTurn.Core.Vehicles.Obstruction> Obstructions(IReadOnlyList<string> layerPatterns, Point2 min, Point2 max, IReadOnlyCollection<string> excludeLayers) =>
            ObstructionScanner.Scan(_doc, layerPatterns, min, max, excludeLayers);

        // ============================================================== surfaces

        public IReadOnlyList<string> SurfaceNames() => SurfaceSampler.SurfaceNames(_doc);

        public Func<IReadOnlyList<Point2>, IReadOnlyList<double?>> Sampler(string? surfaceName, out string resolvedName)
        {
            // Check the surface exists now, so a bad name fails before any work is done.
            SurfaceSampler.SampleMany(_doc, surfaceName, Array.Empty<Point2>(), out resolvedName);
            string name = resolvedName;
            return points => SurfaceSampler.SampleMany(_doc, name, points, out _);
        }

        // ================================================================ blocks

        private static readonly char[] InvalidNameChars = { '<', '>', '/', '\\', '"', ':', ';', '?', '*', '|', ',', '=', '`' };

        public BlockResult MakeBlock(IReadOnlyList<string> handles, string name, Point2 basePoint, string? layer)
        {
            string clean = new string(name.Select(ch => Array.IndexOf(InvalidNameChars, ch) >= 0 ? '-' : ch).ToArray()).Trim();
            if (clean.Length == 0)
            {
                clean = "Turning path";
            }

            using (_doc.LockDocument())
            using (Transaction tr = Db.TransactionManager.StartTransaction())
            {
                var bt = (BlockTable)tr.GetObject(Db.BlockTableId, OpenMode.ForWrite);
                string unique = clean;
                for (int n = 2; bt.Has(unique); n++)
                {
                    unique = $"{clean} {n}";
                }

                var ids = new ObjectIdCollection();
                foreach (string h in handles)
                {
                    ObjectId id = ResolveHandle(h);
                    if (!id.IsNull && !id.IsErased)
                    {
                        ids.Add(id);
                    }
                }

                if (ids.Count == 0)
                {
                    throw new ArgumentException("None of the entities could be found to make the block from.");
                }

                var btr = new BlockTableRecord { Name = unique, Origin = P3(basePoint) };
                ObjectId btrId = bt.Add(btr);
                tr.AddNewlyCreatedDBObject(btr, true);
                // Moves the entities out of model space into the block, keeping their coordinates and layers.
                btr.AssumeOwnershipOf(ids);

                var space = (BlockTableRecord)tr.GetObject(Db.CurrentSpaceId, OpenMode.ForWrite);
                var br = new BlockReference(P3(basePoint), btrId);
                br.SetDatabaseDefaults(Db);
                if (!string.IsNullOrWhiteSpace(layer))
                {
                    GetOrCreateLayer(tr, layer!, out _);
                    br.Layer = layer;
                }

                space.AppendEntity(br);
                tr.AddNewlyCreatedDBObject(br, true);
                tr.Commit();
                Flush();
                return new BlockResult { BlockName = unique, Handle = br.Handle.ToString(), EntityCount = ids.Count };
            }
        }

        private ObjectId ResolveHandle(string hex)
        {
            try
            {
                var handle = new Handle(Convert.ToInt64(hex, 16));
                return Db.TryGetObjectId(handle, out ObjectId id) && !id.IsErased ? id : ObjectId.Null;
            }
            catch (FormatException)
            {
                return ObjectId.Null;
            }
        }

        /// <summary>Returns true if the layer had to be created.</summary>
        private bool GetOrCreateLayer(Transaction tr, string name, out LayerTableRecord layer)
        {
            var table = (LayerTable)tr.GetObject(Db.LayerTableId, OpenMode.ForRead);
            if (table.Has(name))
            {
                layer = (LayerTableRecord)tr.GetObject(table[name], OpenMode.ForRead);
                return false;
            }

            table.UpgradeOpen();
            layer = new LayerTableRecord { Name = name };
            table.Add(layer);
            tr.AddNewlyCreatedDBObject(layer, true);
            return true;
        }

        private ObjectId FindOrLoadLinetype(Transaction tr, string name)
        {
            var table = (LinetypeTable)tr.GetObject(Db.LinetypeTableId, OpenMode.ForRead);
            if (table.Has(name))
            {
                return table[name];
            }

#if AUTOCAD
            // AutoCAD ships acadiso.lin (metric) and acad.lin (imperial).
            foreach (string file in new[] { "acadiso.lin", "acad.lin" })
#else
            // BricsCAD ships iso.lin (metric) and default.lin (imperial).
            foreach (string file in new[] { "iso.lin", "default.lin" })
#endif
            {
                try
                {
                    Db.LoadLineTypeFile(name, file);
                }
                catch (System.Exception)
                {
                    continue;
                }

                table = (LinetypeTable)tr.GetObject(Db.LinetypeTableId, OpenMode.ForRead);
                if (table.Has(name))
                {
                    return table[name];
                }
            }

            return ObjectId.Null;
        }

        private string SpaceName(Transaction tr)
        {
            var space = (BlockTableRecord)tr.GetObject(Db.CurrentSpaceId, OpenMode.ForRead);
            if (string.Equals(space.Name, BlockTableRecord.ModelSpace, StringComparison.OrdinalIgnoreCase))
            {
                return "Model";
            }

            return space.IsLayout ? "Layout: " + ((Layout)tr.GetObject(space.LayoutId, OpenMode.ForRead)).LayoutName : space.Name;
        }

        private static List<string> Names<T>(Transaction tr, ObjectId tableId)
            where T : SymbolTableRecord
        {
            var names = new List<string>();
            foreach (ObjectId id in (SymbolTable)tr.GetObject(tableId, OpenMode.ForRead))
            {
                if (names.Count >= MaxListedNames)
                {
                    break;
                }

                names.Add(((T)tr.GetObject(id, OpenMode.ForRead)).Name);
            }

            return names;
        }

        /// <summary>Escape MText formatting characters and turn newlines into paragraph breaks.</summary>
        private static string ToMTextContents(string text) =>
            text.Replace("\\", "\\\\").Replace("{", "\\{").Replace("}", "\\}")
                .Replace("\r\n", "\n").Replace("\n", "\\P");

        private void Flush()
        {
            _doc.TransactionManager.FlushGraphics();
            Ed.UpdateScreen();
        }

        private static string? Join(string? a, string? b) =>
            a == null ? b : b == null ? a : a + "; " + b;

        private static Point3d P3(Point2 p) => new Point3d(p.X, p.Y, 0);

        private static double Rad(double deg) => deg * Math.PI / 180.0;

        private static double Deg(double rad) => rad * 180.0 / Math.PI;

        private static double Round(double v) => Math.Round(v, 3);

        private static double[] Arr(Point3d p) => new[] { Round(p.X), Round(p.Y) };

        private static double[] Arr(Point2d p) => new[] { Round(p.X), Round(p.Y) };
    }
}
