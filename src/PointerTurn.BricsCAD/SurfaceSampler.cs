using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Bricscad.ApplicationServices;
using Teigha.DatabaseServices;
using Teigha.Geometry;

namespace PointerTurn.BricsCAD
{
    /// <summary>
    /// Reads ground levels from a BricsCAD TIN surface, for the ground clearance check along a
    /// path. Runs on the main thread. The Civil types are reached by reflection so the add-in
    /// still loads on BricsCAD versions or editions without them.
    /// </summary>
    internal static class SurfaceSampler
    {
        /// <summary>
        /// Levels of a TIN surface at many plan points in one transaction; null where a point is off
        /// the surface. <paramref name="surface"/> null means the only surface.
        /// </summary>
        public static List<double?> SampleMany(Document doc, string? surface, IReadOnlyList<PointerTurn.Core.Drawing.Point2> points, out string resolvedName)
        {
            Database db = doc.Database;
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                List<Entity> surfaces = FindSurfaces(db, tr);
                if (surfaces.Count == 0)
                {
                    throw new ArgumentException("There is no BricsCAD TIN surface in this drawing. For a Civil Site Design surface, make a BricsCAD copy of it with CSD's Surface Exchange first.");
                }

                Entity ent = PickSurface(surfaces, surface);
                resolvedName = SurfaceName(ent);
                MethodInfo elevationAt = ent.GetType().GetMethod("GetElevationAtPoint", new[] { typeof(Point3d) })
                    ?? throw new ArgumentException("This BricsCAD version can't read surface elevations.");
                var levels = new List<double?>(points.Count);
                foreach (PointerTurn.Core.Drawing.Point2 p in points)
                {
                    try
                    {
                        double z = (double)elevationAt.Invoke(ent, new object[] { new Point3d(p.X, p.Y, 0) })!;
                        levels.Add(double.IsNaN(z) || double.IsInfinity(z) || Math.Abs(z) > 1e7 ? (double?)null : z);
                    }
                    catch (Exception)
                    {
                        levels.Add(null);
                    }
                }

                tr.Commit();
                return levels;
            }
        }

        public static List<string> SurfaceNames(Document doc)
        {
            using (Transaction tr = doc.Database.TransactionManager.StartTransaction())
            {
                return FindSurfaces(doc.Database, tr).Select(SurfaceName).ToList();
            }
        }

        private static List<Entity> FindSurfaces(Database db, Transaction tr)
        {
            var list = new List<Entity>();
            var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
            var ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);
            foreach (ObjectId id in ms)
            {
                if (!(tr.GetObject(id, OpenMode.ForRead) is Entity e))
                {
                    continue;
                }

                for (Type? t = e.GetType(); t != null; t = t.BaseType)
                {
                    if (t.FullName == "Bricscad.Civil.TinSurfaceStatic")
                    {
                        list.Add(e);
                        break;
                    }
                }
            }

            return list;
        }

        private static Entity PickSurface(List<Entity> surfaces, string? wanted)
        {
            if (string.IsNullOrWhiteSpace(wanted))
            {
                if (surfaces.Count == 1)
                {
                    return surfaces[0];
                }

                throw new ArgumentException($"The drawing has {surfaces.Count} TIN surfaces; choose one of: {string.Join(", ", surfaces.Select(SurfaceName))}.");
            }

            return surfaces.FirstOrDefault(s => string.Equals(SurfaceName(s), wanted, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(s.Handle.ToString(), wanted, StringComparison.OrdinalIgnoreCase))
                ?? throw new ArgumentException($"No TIN surface '{wanted}'. Surfaces: {string.Join(", ", surfaces.Select(SurfaceName))}.");
        }

        private static string SurfaceName(Entity e)
        {
            string? name = e.GetType().GetProperty("Name")?.GetValue(e) as string;
            return string.IsNullOrWhiteSpace(name) ? e.Handle.ToString() : name!;
        }
    }
}
