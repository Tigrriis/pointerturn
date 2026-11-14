using System;
using System.Collections.Generic;
using System.Linq;

namespace PointerPath.Core.Vehicles
{
    /// <summary>A ground (existing surface) level at a chainage.</summary>
    public readonly struct GroundPoint
    {
        public GroundPoint(double chainage, double level)
        {
            Chainage = chainage;
            Level = level;
        }

        public double Chainage { get; }
        public double Level { get; }
    }

    /// <summary>A ground long section along a centreline, sorted by chainage.</summary>
    public sealed class GroundProfile
    {
        private readonly GroundPoint[] _points;

        public GroundProfile(IEnumerable<GroundPoint> points)
        {
            _points = points
                .Where(p => !double.IsNaN(p.Chainage) && !double.IsNaN(p.Level) && !double.IsInfinity(p.Level))
                .OrderBy(p => p.Chainage)
                .ToArray();

            // Drop duplicate chainages (keep the first).
            var unique = new List<GroundPoint>(_points.Length);
            foreach (GroundPoint p in _points)
            {
                if (unique.Count == 0 || p.Chainage - unique[unique.Count - 1].Chainage > 1e-6)
                {
                    unique.Add(p);
                }
            }

            _points = unique.ToArray();
            if (_points.Length < 2)
            {
                throw new ArgumentException("A ground profile needs at least 2 points.");
            }
        }

        public IReadOnlyList<GroundPoint> Points => _points;

        public double Start => _points[0].Chainage;

        public double End => _points[_points.Length - 1].Chainage;

        /// <summary>Ground level at a chainage (linear between points, clamped at the ends).</summary>
        public double LevelAt(double chainage)
        {
            if (chainage <= _points[0].Chainage)
            {
                return _points[0].Level;
            }

            if (chainage >= _points[_points.Length - 1].Chainage)
            {
                return _points[_points.Length - 1].Level;
            }

            int lo = 0, hi = _points.Length - 1;
            while (hi - lo > 1)
            {
                int mid = (lo + hi) / 2;
                if (_points[mid].Chainage <= chainage)
                {
                    lo = mid;
                }
                else
                {
                    hi = mid;
                }
            }

            GroundPoint a = _points[lo], b = _points[hi];
            double t = (chainage - a.Chainage) / (b.Chainage - a.Chainage);
            return a.Level + t * (b.Level - a.Level);
        }

        /// <summary>Chainages of the ground points strictly between two chainages.</summary>
        public IEnumerable<double> ChainagesBetween(double from, double to) =>
            _points.Where(p => p.Chainage > from && p.Chainage < to).Select(p => p.Chainage);
    }
}
