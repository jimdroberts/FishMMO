using System;
using System.Collections.Generic;
using UnityEngine;
using FishMMO.Shared.Celestial;

namespace FishMMO.Shared.Atlas
{
	/// <summary>A scene's rectangle on a globe: centre, heading and size.</summary>
	[Serializable]
	public struct AtlasFootprint
	{
		public double Latitude;
		public double Longitude;
		/// <summary>Clockwise from north, in degrees: the direction the scene's +Z faces.</summary>
		public float HeadingDegrees;
		/// <summary>Size along the scene's X and Z, in km.</summary>
		public Vector2 SizeKm;

		public AtlasFootprint(double latitude, double longitude, float headingDegrees, Vector2 sizeKm)
		{
			Latitude = latitude;
			Longitude = longitude;
			HeadingDegrees = headingDegrees;
			SizeKm = sizeKm;
		}

		public static AtlasFootprint Of(WorldAtlasScene scene)
		{
			return new AtlasFootprint(scene.Latitude, scene.Longitude, scene.HeadingDegrees, scene.SizeKm);
		}

		/// <summary>Half the diagonal, in km: nothing of the scene is further from its centre.</summary>
		public float RadiusKm => 0.5f * Mathf.Sqrt(SizeKm.x * SizeKm.x + SizeKm.y * SizeKm.y);
	}

	/// <summary>
	/// Globe maths for the world atlas: positions, time zones, scene rectangles on a sphere,
	/// overlap, free spots, the auto radius and the flat projection routes are planned in.
	/// </summary>
	/// <remarks>
	/// The frame: +Y is north, longitude 0 faces +Z, longitude 90° east faces +X. Distances are in
	/// km and angles in degrees unless a name says otherwise. Pure and double precision, so the
	/// editor designer and the in-game atlas agree.
	/// </remarks>
	public static class AtlasGeometry
	{
		/// <summary>No scene may span more of its body than this, in degrees of arc.</summary>
		public const double MaxSceneArcDegrees = 60.0;
		public const double Deg2Rad = Math.PI / 180.0;
		public const double Rad2Deg = 180.0 / Math.PI;

		/// <summary>Whole hours from longitude 0: one zone per 15°.</summary>
		public static int TimeZoneOf(double longitude)
		{
			return Math.Max(-12, Math.Min(12, (int)Math.Round(WrapLongitude(longitude) / 15.0, MidpointRounding.AwayFromZero)));
		}

		/// <summary>A longitude in (−180, 180].</summary>
		public static double WrapLongitude(double longitude)
		{
			double l = Math.IEEERemainder(longitude, 360.0);
			return l <= -180.0 ? l + 360.0 : l;
		}

		/// <summary>The unit vector at a latitude and longitude.</summary>
		public static Vector3d ToUnit(double latitude, double longitude)
		{
			double lat = latitude * Deg2Rad, lon = longitude * Deg2Rad;
			double c = Math.Cos(lat);
			return new Vector3d(c * Math.Sin(lon), Math.Sin(lat), c * Math.Cos(lon));
		}

		/// <summary>Latitude and longitude of a direction.</summary>
		public static void FromUnit(Vector3d v, out double latitude, out double longitude)
		{
			Vector3d n = v.Normalized;
			latitude = Math.Asin(Math.Max(-1.0, Math.Min(1.0, n.Y))) * Rad2Deg;
			longitude = Math.Abs(n.X) < 1e-15 && Math.Abs(n.Z) < 1e-15 ? 0.0 : Math.Atan2(n.X, n.Z) * Rad2Deg;
		}

		/// <summary>East and north unit vectors at a point. At a pole, "east" is taken from longitude.</summary>
		public static void Frame(double latitude, double longitude, out Vector3d east, out Vector3d north)
		{
			double lat = latitude * Deg2Rad, lon = longitude * Deg2Rad;
			east = new Vector3d(Math.Cos(lon), 0.0, -Math.Sin(lon));
			north = new Vector3d(-Math.Sin(lat) * Math.Sin(lon), Math.Cos(lat), -Math.Sin(lat) * Math.Cos(lon));
		}

		/// <summary>Great-circle distance between two points, in degrees.</summary>
		public static double AngularDistance(double lat1, double lon1, double lat2, double lon2)
		{
			double d = Vector3d.Dot(ToUnit(lat1, lon1), ToUnit(lat2, lon2));
			return Math.Acos(Math.Max(-1.0, Math.Min(1.0, d))) * Rad2Deg;
		}

		/// <summary>
		/// The point reached by walking east and north from a place along the surface (the
		/// exponential map), as a unit vector.
		/// </summary>
		public static Vector3d Offset(double latitude, double longitude, double eastKm, double northKm, double radiusKm)
		{
			Frame(latitude, longitude, out Vector3d east, out Vector3d north);
			Vector3d centre = ToUnit(latitude, longitude);
			double distance = Math.Sqrt(eastKm * eastKm + northKm * northKm);
			if (distance < 1e-12 || radiusKm <= 0.0)
			{
				return centre;
			}
			double angle = distance / radiusKm;
			Vector3d direction = (east * (eastKm / distance)) + (north * (northKm / distance));
			return (centre * Math.Cos(angle)) + (direction * Math.Sin(angle));
		}

		/// <summary>A point in a scene's own km frame (X right, Z forward) on the globe.</summary>
		public static Vector3d SceneToUnit(in AtlasFootprint footprint, double xKm, double zKm, double radiusKm)
		{
			double h = footprint.HeadingDegrees * Deg2Rad;
			double cos = Math.Cos(h), sin = Math.Sin(h);
			double eastKm = xKm * cos + zKm * sin;
			double northKm = -xKm * sin + zKm * cos;
			return Offset(footprint.Latitude, footprint.Longitude, eastKm, northKm, radiusKm);
		}

		/// <summary>The four corners of a scene on the globe, counter-clockwise seen from outside.</summary>
		public static Vector3d[] Corners(in AtlasFootprint footprint, double radiusKm)
		{
			double hx = footprint.SizeKm.x * 0.5, hz = footprint.SizeKm.y * 0.5;
			return new[]
			{
				SceneToUnit(footprint, -hx, -hz, radiusKm),
				SceneToUnit(footprint, hx, -hz, radiusKm),
				SceneToUnit(footprint, hx, hz, radiusKm),
				SceneToUnit(footprint, -hx, hz, radiusKm),
			};
		}

		/// <summary>
		/// Azimuthal equidistant projection about a centre: distances from the centre stay true.
		/// X is east, Y is north, in km.
		/// </summary>
		public static Vector2 Project(double centreLatitude, double centreLongitude, Vector3d point, double radiusKm)
		{
			Vector3d centre = ToUnit(centreLatitude, centreLongitude);
			Vector3d p = point.Normalized;
			double angle = Math.Acos(Math.Max(-1.0, Math.Min(1.0, Vector3d.Dot(centre, p))));
			if (angle < 1e-12)
			{
				return Vector2.zero;
			}
			Frame(centreLatitude, centreLongitude, out Vector3d east, out Vector3d north);
			double e = Vector3d.Dot(p, east), n = Vector3d.Dot(p, north);
			double length = Math.Sqrt(e * e + n * n);
			if (length < 1e-15)
			{
				// The antipode: every bearing is the same distance away.
				return new Vector2((float)(angle * radiusKm), 0f);
			}
			double scale = angle * radiusKm / length;
			return new Vector2((float)(e * scale), (float)(n * scale));
		}

		/// <summary>The inverse of <see cref="Project"/>.</summary>
		public static Vector3d Unproject(double centreLatitude, double centreLongitude, Vector2 km, double radiusKm)
		{
			return Offset(centreLatitude, centreLongitude, km.x, km.y, radiusKm);
		}

		/// <summary>A scene's corners in the projection about some centre.</summary>
		public static Vector2[] ProjectedCorners(double centreLatitude, double centreLongitude, in AtlasFootprint footprint, double radiusKm)
		{
			Vector3d[] corners = Corners(footprint, radiusKm);
			var result = new Vector2[4];
			for (int i = 0; i < 4; i++)
			{
				result[i] = Project(centreLatitude, centreLongitude, corners[i], radiusKm);
			}
			return result;
		}

		/// <summary>True when two scenes' rectangles intersect (touching counts as clear).</summary>
		public static bool Overlaps(in AtlasFootprint a, in AtlasFootprint b, double radiusKm)
		{
			double reach = (a.RadiusKm + b.RadiusKm) / Math.Max(1e-6, radiusKm) * Rad2Deg;
			if (AngularDistance(a.Latitude, a.Longitude, b.Latitude, b.Longitude) >= reach)
			{
				return false;
			}
			Vector2[] pa = ProjectedCorners(a.Latitude, a.Longitude, a, radiusKm);
			Vector2[] pb = ProjectedCorners(a.Latitude, a.Longitude, b, radiusKm);
			return ConvexOverlap(pa, pb);
		}

		/// <summary>Separating-axis test for two convex polygons. Touching edges do not overlap.</summary>
		public static bool ConvexOverlap(IReadOnlyList<Vector2> a, IReadOnlyList<Vector2> b)
		{
			return !HasSeparatingAxis(a, a, b) && !HasSeparatingAxis(b, a, b);
		}

		private static bool HasSeparatingAxis(IReadOnlyList<Vector2> edges, IReadOnlyList<Vector2> a, IReadOnlyList<Vector2> b)
		{
			const float epsilon = 1e-4f;
			for (int i = 0; i < edges.Count; i++)
			{
				Vector2 e = edges[(i + 1) % edges.Count] - edges[i];
				var axis = new Vector2(-e.y, e.x);
				if (axis.sqrMagnitude < 1e-12f)
				{
					continue;
				}
				Range(axis, a, out float minA, out float maxA);
				Range(axis, b, out float minB, out float maxB);
				float scale = axis.magnitude;
				if (maxA <= minB + epsilon * scale || maxB <= minA + epsilon * scale)
				{
					return true;
				}
			}
			return false;
		}

		private static void Range(Vector2 axis, IReadOnlyList<Vector2> points, out float min, out float max)
		{
			min = float.MaxValue;
			max = float.MinValue;
			for (int i = 0; i < points.Count; i++)
			{
				float d = Vector2.Dot(axis, points[i]);
				min = Mathf.Min(min, d);
				max = Mathf.Max(max, d);
			}
		}

		/// <summary>True when a point lies inside a convex polygon (either winding).</summary>
		public static bool Contains(IReadOnlyList<Vector2> polygon, Vector2 point)
		{
			int sign = 0;
			for (int i = 0; i < polygon.Count; i++)
			{
				Vector2 a = polygon[i], b = polygon[(i + 1) % polygon.Count];
				float cross = (b.x - a.x) * (point.y - a.y) - (b.y - a.y) * (point.x - a.x);
				int s = cross > 0f ? 1 : cross < 0f ? -1 : 0;
				if (s == 0)
				{
					continue;
				}
				if (sign == 0)
				{
					sign = s;
				}
				else if (s != sign)
				{
					return false;
				}
			}
			return true;
		}

		/// <summary>Shortest distance from a point to a convex polygon; 0 inside.</summary>
		public static float DistanceTo(IReadOnlyList<Vector2> polygon, Vector2 point)
		{
			if (Contains(polygon, point))
			{
				return 0f;
			}
			float best = float.MaxValue;
			for (int i = 0; i < polygon.Count; i++)
			{
				Vector2 a = polygon[i], b = polygon[(i + 1) % polygon.Count];
				Vector2 ab = b - a;
				float t = ab.sqrMagnitude > 0f ? Mathf.Clamp01(Vector2.Dot(point - a, ab) / ab.sqrMagnitude) : 0f;
				best = Mathf.Min(best, Vector2.Distance(point, a + ab * t));
			}
			return best;
		}

		/// <summary>True when a scene is small enough for a body of this radius.</summary>
		public static bool Fits(in AtlasFootprint footprint, double radiusKm)
		{
			return Mathf.Max(footprint.SizeKm.x, footprint.SizeKm.y) / Math.Max(1e-6, radiusKm) <= MaxSceneArcDegrees * Deg2Rad + 1e-9;
		}

		/// <summary>
		/// The radius Auto mode settles on: the minimum; enough surface that no layer's scenes
		/// cover more than the allowed share; and enough that the largest scene spans at most
		/// <see cref="MaxSceneArcDegrees"/>.
		/// </summary>
		public static float RequiredRadius(float minimumKm, float maxCoveragePercent, IEnumerable<float> layerAreasKm2, float largestSideKm)
		{
			double radius = Math.Max(1.0, minimumKm);
			double coverage = Math.Max(0.01, maxCoveragePercent / 100.0);
			if (layerAreasKm2 != null)
			{
				foreach (float area in layerAreasKm2)
				{
					radius = Math.Max(radius, Math.Sqrt(Math.Max(0f, area) / (coverage * 4.0 * Math.PI)));
				}
			}
			radius = Math.Max(radius, largestSideKm / (MaxSceneArcDegrees * Deg2Rad));
			return (float)radius;
		}

		/// <summary>
		/// The nearest spot to a wished-for centre where a scene overlaps none of the others,
		/// searched in rings. False when the body is too full.
		/// </summary>
		public static bool FindFreeSpot(in AtlasFootprint wanted, IReadOnlyList<AtlasFootprint> others, double radiusKm, out double latitude, out double longitude)
		{
			latitude = wanted.Latitude;
			longitude = wanted.Longitude;
			if (IsFree(wanted, others, radiusKm))
			{
				return true;
			}
			double step = Math.Max(wanted.RadiusKm * 0.5, 0.05) / Math.Max(1e-6, radiusKm) * Rad2Deg;
			int rings = (int)Math.Ceiling(180.0 / step);
			for (int ring = 1; ring <= rings; ring++)
			{
				double distance = ring * step;
				int count = Math.Max(6, (int)Math.Ceiling(2.0 * Math.PI * Math.Sin(Math.Min(distance, 90.0) * Deg2Rad) / (step * Deg2Rad)));
				for (int k = 0; k < count; k++)
				{
					double bearing = 2.0 * Math.PI * k / count;
					Vector3d p = Offset(wanted.Latitude, wanted.Longitude,
						Math.Sin(bearing) * distance * Deg2Rad * radiusKm,
						Math.Cos(bearing) * distance * Deg2Rad * radiusKm, radiusKm);
					FromUnit(p, out double lat, out double lon);
					var candidate = new AtlasFootprint(lat, lon, wanted.HeadingDegrees, wanted.SizeKm);
					if (IsFree(candidate, others, radiusKm))
					{
						latitude = lat;
						longitude = lon;
						return true;
					}
				}
			}
			return false;
		}

		private static bool IsFree(in AtlasFootprint candidate, IReadOnlyList<AtlasFootprint> others, double radiusKm)
		{
			if (others == null)
			{
				return true;
			}
			for (int i = 0; i < others.Count; i++)
			{
				if (Overlaps(candidate, others[i], radiusKm))
				{
					return false;
				}
			}
			return true;
		}
	}
}
