using FishMMO.Shared.Atlas;
using NUnit.Framework;
using UnityEngine;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// The planet's north in a scene's own terms (<see cref="AtlasGeometry.NorthBearing"/>), which the maps' compasses point at:
	/// degrees clockwise from the scene's +Z, the axis the maps draw up.
	/// </summary>
	[TestFixture]
	public class TrueNorthTests
	{
		private const double EarthLikeKm = 6371.0;

		[Test]
		public void AtTheCentre_NorthIsMinusTheHeading()
		{
			// The heading is which way +Z faces, clockwise from north, so north lies that far anticlockwise of +Z.
			var turned = new AtlasFootprint(10.0, 20.0, 30f, new Vector2(4f, 4f));
			Assert.That(AtlasGeometry.NorthBearing(turned, 0.0, 0.0, EarthLikeKm), Is.EqualTo(330f).Within(0.01f));

			var square = new AtlasFootprint(10.0, 20.0, 0f, new Vector2(4f, 4f));
			Assert.That(DeltaFromZero(AtlasGeometry.NorthBearing(square, 0.0, 0.0, EarthLikeKm)), Is.LessThan(0.01f),
				"a scene laid north-up has north along +Z");
		}

		[Test]
		public void AnUncutScene_TakesTheHeadingAlone()
		{
			var placed = new AtlasFootprint(70.0, 0.0, 90f, new Vector2(4f, 4f));
			Assert.That(AtlasGeometry.NorthBearing(placed, 1.5, -0.5, 0.0), Is.EqualTo(270f).Within(1e-4f));
		}

		[Test]
		public void NearAPole_NorthTurnsAcrossTheScene()
		{
			/* The meridians close in on the pole: on a scene laid north-up a few km from it, north leans in toward the
			 * centre's meridian on either side, so it is measured where the player stands, not taken from the heading. */
			var polar = new AtlasFootprint(89.9, 0.0, 0f, new Vector2(20f, 20f));
			float east = AtlasGeometry.NorthBearing(polar, 5.0, 0.0, EarthLikeKm);
			float west = AtlasGeometry.NorthBearing(polar, -5.0, 0.0, EarthLikeKm);
			Assert.That(DeltaFromZero(east), Is.GreaterThan(5f), "five km east of a scene centred near the pole, north has turned well off +Z");
			Assert.That(Mathf.DeltaAngle(0f, east), Is.EqualTo(-Mathf.DeltaAngle(0f, west)).Within(0.01f),
				"and turned the same amount the other way to the west");
		}

		private static float DeltaFromZero(float degrees) => Mathf.Abs(Mathf.DeltaAngle(0f, degrees));
	}
}
