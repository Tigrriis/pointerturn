using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
#if AUTOCAD
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
#else
using Bricscad.ApplicationServices;
using Teigha.DatabaseServices;
using Teigha.Geometry;
#endif

namespace PointerTurn.BricsCAD
{
    /// <summary>
    /// Reads ground levels from a TIN surface (BricsCAD's own, or Civil 3D's in AutoCAD), for the
    /// ground clearance check along a path. Runs on the main thread. The Civil types are reached
    /// by reflection, so the add-in still loads where they don't exist: older or lighter
    /// BricsCAD editions, and plain AutoCAD.
    /// </summary>
    internal static class SurfaceSampler
    {
#if AUTOCAD
        private const string TinSurfaceType = "Autodesk.Civil.DatabaseServices.TinSurface";
#else
        private const string TinSurfaceType = "Bricscad.Civil.TinSurfaceStatic";
#endif

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
                    throw new ArgumentException(Host.NoSurfaceToSample);
                }

                Entity ent = PickSurface(surfaces, surface);
                resolvedName = SurfaceName(ent);
                // BricsCAD: GetElevationAtPoint(Point3d). Civil 3D: FindElevationAtXY(x, y), which throws off the surface.
                MethodInfo? atPoint = ent.GetType().GetMethod("GetElevationAtPoint", new[] { typeof(Point3d) });
                MethodInfo? atXY = atPoint == null ? ent.GetType().GetMethod("FindElevationAtXY", new[] { typeof(double), typeof(double) }) : null;
                if (atPoint == null && atXY == null)
                {
                    throw new ArgumentException($"This {Host.Name} version can't read surface elevations.");
                }

                var levels = new List<double?>(points.Count);
                foreach (PointerTurn.Core.Drawing.Point2 p in points)
                {
                    try
                    {
                        double z = atPoint != null
                            ? (double)atPoint.Invoke(ent, new object[] { new Point3d(p.X, p.Y, 0) })!
                            : (double)atXY!.Invoke(ent, new object[] { p.X, p.Y })!;
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
                    if (t.FullName == TinSurfaceType)
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
