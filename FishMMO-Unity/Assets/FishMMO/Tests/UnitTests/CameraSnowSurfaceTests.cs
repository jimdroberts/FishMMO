using FishMMO.Shared;
using NUnit.Framework;
using UnityEngine;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// The camera's boom stops at the snow's drawn surface (<see cref="KCCCamera.SnowCrossing"/>). The terrain shader lifts the
	/// ground under snow while the terrain collider stays at the bare ground, so a camera that only met the collider went down
	/// through the white surface and showed the grass and the world from under it.
	/// </summary>
	[TestFixture]
	public class CameraSnowSurfaceTests
	{
		private const float Radius = 0.2f;

		/// <summary>Level bare ground at <paramref name="ground"/> under <paramref name="depth"/> metres of snow everywhere.</summary>
		private static KCCCamera.SnowSurfaceQuery Snowfield(float ground, float depth)
		{
			return (Vector3 point, out float g, out float surface) =>
			{
				g = ground;
				surface = ground + depth;
				return true;
			};
		}

		private static bool NoSnow(Vector3 point, out float ground, out float surface)
		{
			ground = 0f;
			surface = 0f;
			return false;
		}

		[Test]
		public void ABoomDippingIntoTheSnow_StopsWhereTheCameraSphereMeetsItsSurface()
		{
			Vector3 down = new Vector3(0f, -1f, -1f).normalized;
			float distance = KCCCamera.SnowCrossing(Snowfield(0f, 1f), new Vector3(0f, 2f, 0f), down, 6f, Radius);

			// The sphere's bottom meets the surface at y = 1.2: 0.8 m down the 45° boom.
			Assert.AreEqual(0.8f * Mathf.Sqrt(2f), distance, 0.01f);
		}

		[Test]
		public void WithNoSnowLying_TheBoomIsNotStopped()
		{
			Vector3 down = new Vector3(0f, -1f, -1f).normalized;
			Assert.IsTrue(float.IsPositiveInfinity(KCCCamera.SnowCrossing(NoSnow, new Vector3(0f, 2f, 0f), down, 6f, Radius)));
			Assert.IsTrue(float.IsPositiveInfinity(KCCCamera.SnowCrossing(null, new Vector3(0f, 2f, 0f), down, 6f, Radius)),
				"no weather installed: nothing to keep out of");
		}

		[Test]
		public void APivotAlreadyInTheSnow_LeavesTheBoomAlone()
		{
			// A character waist-deep in a drift: there is nowhere short of the pivot to pull the camera back to.
			Vector3 back = new Vector3(0f, 0f, -1f);
			Assert.IsTrue(float.IsPositiveInfinity(KCCCamera.SnowCrossing(Snowfield(0f, 1.5f), new Vector3(0f, 1f, 0f), back, 6f, Radius)));
		}

		[Test]
		public void UnderTheBareGround_TheSnowAboveIsNotAnObstruction()
		{
			// Inside a cave the terrain's heightmap (and its snow) runs over the roof; only the collider counts there.
			Vector3 back = new Vector3(0f, 0f, -1f);
			Assert.IsTrue(float.IsPositiveInfinity(KCCCamera.SnowCrossing(Snowfield(0f, 1f), new Vector3(0f, -3f, 0f), back, 6f, Radius)));
		}

		[Test]
		public void ABoomThatStaysAboveTheSnow_IsNotStopped()
		{
			Vector3 back = new Vector3(0f, 0f, -1f);
			Assert.IsTrue(float.IsPositiveInfinity(KCCCamera.SnowCrossing(Snowfield(0f, 1f), new Vector3(0f, 1.5f, 0f), back, 6f, Radius)));
		}
	}
}
