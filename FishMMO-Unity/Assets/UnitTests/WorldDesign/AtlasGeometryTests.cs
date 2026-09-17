using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using FishMMO.Shared.Atlas;
using FishMMO.Shared.Celestial;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests.WorldDesign
{
	/// <summary>
	/// Globe maths for the world atlas: positions, time zones, scene rectangles on a sphere, the
	/// flat projection routes are planned in, overlap, free spots and the auto radius.
	/// </summary>
	[TestFixture]
	public class AtlasGeometryTests
	{
		private const double Radius = 30.0;

		[Test]
		public void LatitudeAndLongitudeRoundTrip()
		{
			for (double lat = -85; lat <= 85; lat += 17)
			{
				for (double lon = -175; lon <= 175; lon += 35)
				{
					AtlasGeometry.FromUnit(AtlasGeometry.ToUnit(lat, lon), out double la, out double lo);
					Assert.That(la, Is.EqualTo(lat).Within(1e-9));
					Assert.That(lo, Is.EqualTo(lon).Within(1e-9));
				}
			}
		}

		[Test]
		public void LongitudeZeroFacesPlusZAndEastIsPlusX()
		{
			Vector3d zero = AtlasGeometry.ToUnit(0, 0);
			Vector3d east = AtlasGeometry.ToUnit(0, 90);
			Vector3d north = AtlasGeometry.ToUnit(90, 0);
			Assert.That(zero.Z, Is.EqualTo(1.0).Within(1e-12));
			Assert.That(east.X, Is.EqualTo(1.0).Within(1e-12));
			Assert.That(north.Y, Is.EqualTo(1.0).Within(1e-12));
		}

		[Test]
		public void EachTimeZoneIsFifteenDegreesWide()
		{
			LogAssert.AreEqual(0, AtlasGeometry.TimeZoneOf(0));
			LogAssert.AreEqual(0, AtlasGeometry.TimeZoneOf(7.4));
			LogAssert.AreEqual(1, AtlasGeometry.TimeZoneOf(7.6));
			LogAssert.AreEqual(-1, AtlasGeometry.TimeZoneOf(-7.6));
			LogAssert.AreEqual(-11, AtlasGeometry.TimeZoneOf(-172));
			LogAssert.AreEqual(12, AtlasGeometry.TimeZoneOf(180));
			LogAssert.AreEqual(12, AtlasGeometry.TimeZoneOf(-180), "−180° is the same meridian as 180°");
			LogAssert.AreEqual(-2, AtlasGeometry.TimeZoneOf(330), "longitudes wrap");
		}

		[Test]
		public void TheLocalFrameIsRightHandedAndOutward()
		{
			foreach (double lat in new[] { -60.0, 0.0, 35.0, 80.0 })
			{
				foreach (double lon in new[] { -120.0, 0.0, 45.0, 170.0 })
				{
					AtlasGeometry.Frame(lat, lon, out Vector3d east, out Vector3d north);
					Vector3d up = AtlasGeometry.ToUnit(lat, lon);
					Assert.That(east.Magnitude, Is.EqualTo(1.0).Within(1e-9));
					Assert.That(north.Magnitude, Is.EqualTo(1.0).Within(1e-9));
					Assert.That(Vector3d.Dot(east, north), Is.EqualTo(0.0).Within(1e-9));
					Assert.That(Vector3d.Dot(east, up), Is.EqualTo(0.0).Within(1e-9));
					Vector3d cross = Vector3d.Cross(east, north);
					Assert.That(Vector3d.Dot(cross, up), Is.EqualTo(1.0).Within(1e-9), "east × north points out of the globe");
				}
			}
		}

		[Test]
		public void WalkingNorthRaisesTheLatitudeByDistanceOverRadius()
		{
			Vector3d p = AtlasGeometry.Offset(0, 20, 0, 3, Radius);
			AtlasGeometry.FromUnit(p, out double lat, out double lon);
			Assert.That(lat, Is.EqualTo(3.0 / Radius * AtlasGeometry.Rad2Deg).Within(1e-9));
			Assert.That(lon, Is.EqualTo(20.0).Within(1e-9));

			Vector3d q = AtlasGeometry.Offset(40, -30, 2.5, -1.5, Radius);
			AtlasGeometry.FromUnit(q, out double qlat, out double qlon);
			double km = AtlasGeometry.AngularDistance(40, -30, qlat, qlon) * AtlasGeometry.Deg2Rad * Radius;
			Assert.That(km, Is.EqualTo(Math.Sqrt(2.5 * 2.5 + 1.5 * 1.5)).Within(1e-6), "distances along the surface are kept");
		}

		[Test]
		public void AHeadingTurnsTheSceneClockwiseFromNorth()
		{
			var facingEast = new AtlasFootprint(0, 0, 90f, new Vector2(2, 2));
			Vector3d forward = AtlasGeometry.SceneToUnit(facingEast, 0, 1, Radius);
			AtlasGeometry.FromUnit(forward, out double lat, out double lon);
			Assert.That(lat, Is.EqualTo(0.0).Within(1e-9));
			LogAssert.IsTrue(lon > 0.0, "a scene heading 90° has its +Z pointing east");

			var facingNorth = new AtlasFootprint(0, 0, 0f, new Vector2(2, 2));
			AtlasGeometry.FromUnit(AtlasGeometry.SceneToUnit(facingNorth, 1, 0, Radius), out double rlat, out double rlon);
			Assert.That(rlat, Is.EqualTo(0.0).Within(1e-9));
			LogAssert.IsTrue(rlon > 0.0, "with no heading, the scene's +X is east");
		}

		[Test]
		public void TheProjectionKeepsDistancesFromItsCentre()
		{
			const double clat = 25, clon = 40;
			foreach (Vector2 km in new[] { new Vector2(3, 4), new Vector2(-10, 2), new Vector2(0.5f, -20) })
			{
				Vector3d p = AtlasGeometry.Unproject(clat, clon, km, Radius);
				Vector2 back = AtlasGeometry.Project(clat, clon, p, Radius);
				Assert.That(back.x, Is.EqualTo(km.x).Within(1e-3f));
				Assert.That(back.y, Is.EqualTo(km.y).Within(1e-3f));
				AtlasGeometry.FromUnit(p, out double lat, out double lon);
				double arc = AtlasGeometry.AngularDistance(clat, clon, lat, lon) * AtlasGeometry.Deg2Rad * Radius;
				Assert.That(arc, Is.EqualTo(km.magnitude).Within(1e-3));
			}
		}

		[Test]
		public void ScenesOverlapOnlyWhenTheyTouch()
		{
			double step = 1.0 / Radius * AtlasGeometry.Rad2Deg;
			var a = new AtlasFootprint(10, 10, 0f, new Vector2(2, 2));
			LogAssert.IsTrue(AtlasGeometry.Overlaps(a, new AtlasFootprint(10, 10 + 1.5 * step / Math.Cos(10 * AtlasGeometry.Deg2Rad), 0f, new Vector2(2, 2)), Radius));
			LogAssert.IsFalse(AtlasGeometry.Overlaps(a, new AtlasFootprint(10, 10 + 2.2 * step / Math.Cos(10 * AtlasGeometry.Deg2Rad), 0f, new Vector2(2, 2)), Radius));
			LogAssert.IsFalse(AtlasGeometry.Overlaps(a, new AtlasFootprint(-50, 120, 0f, new Vector2(2, 2)), Radius), "far apart");

			// A diamond (45°) whose corner reaches into the gap a square would leave.
			var diamond = new AtlasFootprint(10, 10 + 2.3 * step / Math.Cos(10 * AtlasGeometry.Deg2Rad), 45f, new Vector2(2, 2));
			LogAssert.IsTrue(AtlasGeometry.Overlaps(a, diamond, Radius), "headings are part of the shape");
		}

		[Test]
		public void AScenePolygonContainsItsCentreAndNotItsNeighbours()
		{
			var square = new[] { new Vector2(-1, -1), new Vector2(1, -1), new Vector2(1, 1), new Vector2(-1, 1) };
			LogAssert.IsTrue(AtlasGeometry.Contains(square, Vector2.zero));
			LogAssert.IsFalse(AtlasGeometry.Contains(square, new Vector2(1.5f, 0)));
			Assert.That(AtlasGeometry.DistanceTo(square, new Vector2(3, 0)), Is.EqualTo(2f).Within(1e-5f));
			Assert.That(AtlasGeometry.DistanceTo(square, Vector2.zero), Is.EqualTo(0f));
			var reversed = new[] { square[3], square[2], square[1], square[0] };
			LogAssert.IsTrue(AtlasGeometry.Contains(reversed, new Vector2(0.5f, 0.5f)), "either winding");
		}

		[Test]
		public void NoSceneMaySpanMoreThanSixtyDegrees()
		{
			double limit = AtlasGeometry.MaxSceneArcDegrees * AtlasGeometry.Deg2Rad * Radius;
			LogAssert.IsTrue(AtlasGeometry.Fits(new AtlasFootprint(0, 0, 0f, new Vector2((float)limit * 0.99f, 1f)), Radius));
			LogAssert.IsFalse(AtlasGeometry.Fits(new AtlasFootprint(0, 0, 0f, new Vector2(1f, (float)limit * 1.01f)), Radius));
		}

		[Test]
		public void TheAutoRadiusTakesTheLargestNeed()
		{
			Assert.That(AtlasGeometry.RequiredRadius(30f, 35f, new[] { 10f }, 4f), Is.EqualTo(30f), "a small world keeps its minimum");

			float area = 20000f;
			float byArea = Mathf.Sqrt(area / (0.35f * 4f * Mathf.PI));
			Assert.That(AtlasGeometry.RequiredRadius(10f, 35f, new[] { 50f, area }, 4f), Is.EqualTo(byArea).Within(1e-3f), "the fullest layer decides");

			float wide = 200f;
			Assert.That(AtlasGeometry.RequiredRadius(10f, 35f, new[] { 1f }, wide), Is.EqualTo(wide / (60f * Mathf.Deg2Rad)).Within(1e-2f), "the widest scene decides");
		}

		[Test]
		public void AFreeSpotIsTheWishWhenFreeAndClearOtherwise()
		{
			var others = new List<AtlasFootprint>
			{
				new AtlasFootprint(0, 0, 0f, new Vector2(4, 4)),
				new AtlasFootprint(0, 9, 0f, new Vector2(4, 4)),
				new AtlasFootprint(8, 0, 30f, new Vector2(3, 5)),
			};
			var wantFree = new AtlasFootprint(-30, 60, 0f, new Vector2(3, 3));
			LogAssert.IsTrue(AtlasGeometry.FindFreeSpot(wantFree, others, Radius, out double flat, out double flon));
			LogAssert.AreEqual(-30.0, flat);
			LogAssert.AreEqual(60.0, flon);

			var wantTaken = new AtlasFootprint(0.5, 0.5, 0f, new Vector2(3, 3));
			LogAssert.IsTrue(AtlasGeometry.FindFreeSpot(wantTaken, others, Radius, out double lat, out double lon));
			var placed = new AtlasFootprint(lat, lon, 0f, new Vector2(3, 3));
			foreach (AtlasFootprint other in others)
			{
				LogAssert.IsFalse(AtlasGeometry.Overlaps(placed, other, Radius), $"the free spot ({lat:0.00}, {lon:0.00}) must not overlap");
			}
			LogAssert.IsTrue(AtlasGeometry.AngularDistance(0.5, 0.5, lat, lon) < 30.0, "and it stays near the wish");
		}
	}
}
