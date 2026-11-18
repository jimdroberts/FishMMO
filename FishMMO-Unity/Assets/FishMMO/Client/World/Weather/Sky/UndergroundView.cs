using System.Collections.Generic;
using UnityEngine;
using FishMMO.Shared;
using FishMMO.Shared.Biomes;
using FishMMO.Shared.Weather;

namespace FishMMO.Client
{
	/// <summary>
	/// Whether a camera is sealed in a cave: deep enough under the ground, with nothing in view but the cave's own deep
	/// rock — no mouth, no water — so every full-screen effect over the open air (the clouds' march and composite, the
	/// height fog, the froxel fog) would come out to nothing at every pixel and need not be drawn.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Why nothing.</b> Those effects keep, at each pixel, <c>FishOpenAir(camera, surface)</c> of themselves
	/// (FishGroundMap.hlsl): 1 − the lesser of how far underground the eye and the surface it looks at are, from the fine
	/// ground map (MistGroundMap) — 0 when both are twenty metres or more under the ground there, and an effect faded by 0
	/// is (0, 0, 0, 1) over the frame, which leaves it as it was. So the view is sealed when the eye is that deep, and so
	/// is every surface it can see: every piece of the cave whose box is in the frustum lies wholly that far under the
	/// lowest ground the shaders can read over it (<see cref="MistGroundMap.TryLowestGround"/>, with what a filter in
	/// halves may round away), none of them is a mouth or switched off (a hole would show the hillside), and what stands
	/// inside the cave stands inside those boxes.
	/// </para>
	/// <para>
	/// <b>Why no sea.</b> The sea lays the clouds and the fogs over itself from what those passes publish, with no fade
	/// underground: a sea cave's sea in view would lose them. So no piece in view may come down to the sea's top. A cave's
	/// lakes, rivers and falls (a grotto's pool, its streams and pools, 2026-10-20) fade what they lay over themselves as the
	/// passes do underground (FishWaterFog.hlsl's FISH_WATER_OPEN_AIR), to nothing deep in: they never unseal a view. Lava is
	/// not water here: opaque, it is covered through its depth as the rock is.
	/// </para>
	/// <para>
	/// <b>Not the light shafts.</b> Their composite fades by FishOpenAir as the clouds' does (2026-10-21; before, a cave's far
	/// walls glowed toward the sun), but they are drawn as before (FishCloudsFeature), with the clouds' buffer standing in,
	/// which the gather reads only at the sky, of which a sealed view has none.
	/// </para>
	/// <para>
	/// When in doubt it says no and everything is drawn. A mouth within <see cref="MouthClearance"/> of the camera says no
	/// whichever way it looks, so the clouds' history is running again before an opening can turn into view. Not from
	/// SkySystem's eased underground share: that is eased over seconds, and a view turns in a frame.
	/// </para>
	/// </remarks>
	public static class UndergroundView
	{
		/// <summary>The switch, for measuring it: off, every effect is drawn as if it were never asked.</summary>
		public static bool Enabled = true;

		/// <summary>How far under the ground both the eye and what it sees must be: FishUnderground is 1 from here, m.</summary>
		public const float SealedDepth = 20f;
		/// <summary>How near a mouth's box may come before the view is not sealed, whatever it faces, m.</summary>
		public const float MouthClearance = 48f;
		/// <summary>How far above the sea's still level every piece in view must stand, its waves aside, m.</summary>
		private const float SeaClearance = 2f;

		private static readonly Unity.Profiling.ProfilerMarker WorkMarker = new Unity.Profiling.ProfilerMarker("Sky.UndergroundView");
		private static readonly Plane[] planes = new Plane[6];
		private static Camera lastCamera;
		private static int lastFrame = -1;
		private static bool lastSealed;

		/// <summary>Whether <paramref name="camera"/> is sealed in a cave this frame (worked out once a frame for it).</summary>
		public static bool Sealed(Camera camera)
		{
			// Over a cloud world nothing is underground (FishUnderground is off there: MistGroundMap), so no view is sealed.
			if (!Enabled || camera == null || SceneCloudWorld.Any)
			{
				return false;
			}
			int frame = Time.frameCount;
			if (camera == lastCamera && frame == lastFrame)
			{
				return lastSealed;
			}
			lastCamera = camera;
			lastFrame = frame;
			WorkMarker.Begin();
			lastSealed = Work(camera);
			WorkMarker.End();
			return lastSealed;
		}

		private static bool Work(Camera camera)
		{
			IReadOnlyList<CaveVisibility> caves = CaveVisibility.Live;
			if (caves.Count == 0)
			{
				return false;
			}
			SkySystem sky = SkySystem.Instance;
			MistGroundMap ground = sky != null ? sky.MistGround : null;
			if (ground == null)
			{
				return false;
			}
			Vector3 eye = camera.transform.position;
			if (SurfaceWater.IsUnder(eye))
			{
				return false;
			}
			// The eye itself deep enough under the ground the shaders read over it.
			if (!ground.TryLowestGround(eye.x - 0.5f, eye.z - 0.5f, eye.x + 0.5f, eye.z + 0.5f, out float overEye)
				|| overEye - eye.y < SealedDepth)
			{
				return false;
			}
			CaveVisibility cave = null;
			for (int c = 0; c < caves.Count && cave == null; c++)
			{
				if (caves[c] != null && caves[c].Holds(eye))
				{
					cave = caves[c];
				}
			}
			if (cave == null)
			{
				return false;
			}

			bool sea = SurfaceWater.TryGetLevel(out float seaLevel);
			float seaTop = seaLevel + SurfaceWater.WaveHeight + SeaClearance;
			GeometryUtility.CalculateFrustumPlanes(camera, planes);
			float clearance2 = MouthClearance * MouthClearance;
			for (int i = 0; i < cave.PieceCount; i++)
			{
				Bounds box = cave.BoxOf(i);
				bool mouth = cave.AtMouth(i);
				if (mouth && box.SqrDistance(eye) <= clearance2)
				{
					return false;
				}
				// A little wider than the piece, so a rounding at the frustum's edge never leaves one out.
				box.Expand(1f);
				if (!GeometryUtility.TestPlanesAABB(planes, box))
				{
					continue;
				}
				if (mouth || !cave.Drawn(i))
				{
					return false;
				}
				if (!ground.TryLowestGround(box.min.x, box.min.z, box.max.x, box.max.z, out float over) || over - box.max.y < SealedDepth)
				{
					return false;
				}
				if (sea && box.min.y <= seaTop)
				{
					return false;
				}
			}
			return true;
		}
	}
}
