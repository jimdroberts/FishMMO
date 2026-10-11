#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEngine;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>How far an overhang's slab projects, how far it is sunk, how thick and how wide it is.</summary>
	public sealed class OverhangSettings
	{
		/// <summary>How far the slab projects over the face, metres, the least and the most; the most is reached on a face this many times as tall.</summary>
		public float MinReach = 1f, MaxReach = 3f, ReachShare = 0.3f;
		/// <summary>How far the slab is sunk behind the face, as a multiple of its reach, the least and the most.</summary>
		public float MinBack = 1.2f, MaxBack = 2f;
		/// <summary>The slab's thickness as a share of the face's height, held between these, metres.</summary>
		public float ThicknessShare = 0.15f, MinThickness = 0.6f, MaxThickness = 2f;
		/// <summary>How wide the slab is, as a multiple of the width asked for, the least and the most.</summary>
		public float MinWidth = 1f, MaxWidth = 1.2f;
	}

	/// <summary>The numbers <see cref="OverhangPlacer.Draw"/> chose for a slab.</summary>
	public struct OverhangDimensions
	{
		public float Reach, Back, Thickness, Across;
	}

	/// <summary>
	/// A rock shelter planned on a face (<see cref="OverhangPlacer.PlanAt"/>): the slab's box, and the alcove carved under it.
	/// </summary>
	public struct OverhangSlab
	{
		/// <summary>The middle of the slab's box, world.</summary>
		public Vector3 Middle;
		/// <summary>The box in the slab's own frame: x across the face, y up, z out of it.</summary>
		public Vector3 Size;
		/// <summary>The direction the slab projects, out of the face, degrees about +y.</summary>
		public float Yaw;
		/// <summary>The foot the shelter was planned from, world: the alcove's floor is its height.</summary>
		public Vector3 Foot;
		/// <summary>The alcove under the slab, as distances out of the face from the foot (negative: into the slope).</summary>
		public float AlcoveFront, AlcoveBack;

		public Quaternion Rotation => Quaternion.Euler(0f, Yaw, 0f);
	}

	/// <summary>
	/// An overhang as a stretched slab of the rock's own boulder art: projecting from a face, sunk into it behind, laid as a
	/// prop with collision. The hard cap over a plunge fall (<see cref="FallLedges"/>) and a point of interest's rock
	/// shelter on any steep face (<see cref="PlanAt"/>) are the same slab (Jim, 2026-10-10: "FallLedges' stretched slab
	/// generalised").
	/// </summary>
	public static class OverhangPlacer
	{
		/// <summary>
		/// A slab's reach, depth behind the face, thickness and width, drawn from <paramref name="next"/> (three draws, always,
		/// in that order: reach, back, width). <paramref name="room"/> caps the thickness (the face below it); the width is the
		/// asked <paramref name="width"/> stretched a little and held to <paramref name="maxAcross"/>.
		/// </summary>
		/// <remarks>The arithmetic is FallLedges' own, operation for operation: its output must not change by a bit.</remarks>
		public static OverhangDimensions Draw(float faceHeight, float room, float width, float maxAcross, OverhangSettings settings, Func<float> next)
		{
			settings ??= new OverhangSettings();
			float reach = Mathf.Lerp(settings.MinReach, Mathf.Clamp(settings.ReachShare * faceHeight, settings.MinReach, settings.MaxReach), next());
			float back = reach * Mathf.Lerp(settings.MinBack, settings.MaxBack, next());
			float thickness = Mathf.Clamp(settings.ThicknessShare * faceHeight, settings.MinThickness, settings.MaxThickness);
			thickness = Mathf.Min(thickness, room);
			float across = Mathf.Min(width * Mathf.Lerp(settings.MinWidth, settings.MaxWidth, next()), maxAcross);
			return new OverhangDimensions { Reach = reach, Back = back, Thickness = thickness, Across = across };
		}

		/// <summary>
		/// The prop transform that stretches art whose meshes span <paramref name="art"/> to a box of <paramref name="size"/>
		/// with its middle at <paramref name="middle"/>. Scale is applied before the turn (T·R·S), so the box stays a box.
		/// </summary>
		public static void Stretch(Bounds art, Vector3 middle, Quaternion rotation, Vector3 size, out Vector3 position, out Vector3 scale)
		{
			Vector3 own = art.size;
			scale = new Vector3(size.x / Mathf.Max(0.01f, own.x), size.y / Mathf.Max(0.01f, own.y), size.z / Mathf.Max(0.01f, own.z));
			position = middle - rotation * Vector3.Scale(art.center, scale);
		}

		// ── A shelter on any face ─────────────────────────────────

		/// <summary>The settings of a point of interest's rock shelter: a deeper reach than a fall's cap, a thicker slab.</summary>
		public static OverhangSettings Shelter => new OverhangSettings
		{
			MinReach = 2f, MaxReach = 6f, ReachShare = 0.3f,
			MinBack = 1.2f, MaxBack = 1.8f,
			ThicknessShare = 0.12f, MinThickness = 1f, MaxThickness = 2.5f,
		};

		/// <summary>The headroom under a shelter's slab, metres, by size class: an agent (2 m) stands under it.</summary>
		public static float ClearanceFor(int size) => 2.6f + 0.4f * Mathf.Clamp(size, 0, 2);

		/// <summary>The width asked of a shelter, metres, by size class (the slab is 3–10 m across).</summary>
		public static float WidthFor(int size, float t)
		{
			switch (Mathf.Clamp(size, 0, 2))
			{
				case 0: return Mathf.Lerp(3f, 4.5f, t);
				case 1: return Mathf.Lerp(4.5f, 6.5f, t);
				default: return Mathf.Lerp(6.5f, 8.3f, t);
			}
		}

		/// <summary>
		/// A rock shelter on the face behind <paramref name="foot"/> (the site faces <paramref name="yaw"/>, out of the face):
		/// a slab whose underside clears the foot by <see cref="ClearanceFor"/>, projecting from the face at that height and
		/// sunk into it behind, over an alcove cut back into the slope under it. Null-free: false with
		/// <paramref name="problem"/> where the face is too low or too gentle to hold one. Deterministic in its inputs.
		/// </summary>
		public static bool PlanAt(Vector3 foot, float yaw, int size, Func<float, float, float> ground, int seed, out OverhangSlab slab, out string problem)
		{
			slab = default;
			problem = null;
			var random = new DeterministicRNG(seed);
			float yr = yaw * Mathf.Deg2Rad;
			var outward = new Vector3(Mathf.Sin(yr), 0f, Mathf.Cos(yr));
			float h0 = ground(foot.x, foot.z);
			float GroundAt(float along) => ground(foot.x + outward.x * along, foot.z + outward.z * along);
			// How far into the slope (along −outward) the ground first reaches an altitude; NaN if it never does within 32 m.
			float Into(float altitude)
			{
				for (float d = 0f; d <= 32f; d += 0.25f)
				{
					if (GroundAt(-d) >= altitude)
					{
						return d;
					}
				}
				return float.NaN;
			}
			float faceHeight = 0f;
			for (float d = 0f; d <= 32f; d += 1f)
			{
				faceHeight = Mathf.Max(faceHeight, GroundAt(-d) - h0);
			}
			float clearance = ClearanceFor(size);
			float width = WidthFor(size, random.NextFloat());
			OverhangDimensions dims = Draw(faceHeight, faceHeight - clearance - 3f, width, 10f, Shelter, random.NextFloat);
			if (dims.Thickness < 0.8f)
			{
				problem = $"the face ({faceHeight:0} m) is too low to hold a slab {clearance:0.#} m over its foot with rock above";
				return false;
			}
			float bottom = h0 + clearance, top = bottom + dims.Thickness;
			float edge = Into(top), under = Into(bottom);
			if (float.IsNaN(edge) || float.IsNaN(under))
			{
				problem = "the face never rises past the slab within 32 m";
				return false;
			}
			// The slab's back must be buried: the slope behind it stands over its top.
			if (GroundAt(-(edge + dims.Back)) < top)
			{
				problem = "the face is too gentle to bury the slab's back";
				return false;
			}
			float front = -edge + dims.Reach;
			float middle = front - 0.5f * (dims.Reach + dims.Back);
			slab = new OverhangSlab
			{
				Middle = new Vector3(foot.x + outward.x * middle, top - 0.5f * dims.Thickness, foot.z + outward.z * middle),
				Size = new Vector3(dims.Across, dims.Thickness, dims.Reach + dims.Back),
				Yaw = Mathf.Repeat(yaw, 360f),
				Foot = new Vector3(foot.x, h0, foot.z),
				AlcoveFront = front,
				// Back under the slab to half its buried depth: the step up the slope stays inside the slab.
				AlcoveBack = -edge - 0.5f * dims.Back,
			};
			return true;
		}

		/// <summary>
		/// Lowers the ground under a shelter's slab to its foot, easing back over <paramref name="blend"/> metres at the
		/// alcove's sides and front: the undercut a heightfield can hold, roofed by the slab. Returns the samples lowered.
		/// </summary>
		public static int CarveAlcove(Terrain[,] terrains, TerrainTilePlan tiles, in OverhangSlab slab, float blend = 1.5f)
		{
			float yr = slab.Yaw * Mathf.Deg2Rad;
			var outward = new Vector3(Mathf.Sin(yr), 0f, Mathf.Cos(yr));
			var side = new Vector3(outward.z, 0f, -outward.x);
			Vector3 foot = slab.Foot;
			float half = 0.5f * slab.Size.x, front = slab.AlcoveFront, back = slab.AlcoveBack, floor = foot.y;
			float reach = Mathf.Max(Mathf.Abs(front), Mathf.Abs(back)) + half + blend;
			var area = new Rect(foot.x - reach, foot.z - reach, 2f * reach, 2f * reach);
			return PointOfInterestTerrain.EditHeights(terrains, tiles, area, (east, north, metres) =>
			{
				float dx = east - foot.x, dz = north - foot.z;
				float a = dx * outward.x + dz * outward.z, c = Mathf.Abs(dx * side.x + dz * side.z);
				float wa = a > front ? 1f - Mathf.Clamp01((a - front) / blend) : a < back ? 0f : 1f;
				float wc = 1f - Mathf.Clamp01((c - half) / blend);
				float w = wa * wc;
				w = w * w * (3f - 2f * w);
				return metres > floor ? metres - w * (metres - floor) : metres;
			});
		}

		/// <summary>The slab art for a rock: the river boulders' slab of its stone.</summary>
		public static string SlabPrefab(string rock) => $"Boulder_{RiverBoulders.Material(rock)}_Slab";

		/// <summary>The bounds of everything a prefab draws, in its own space; a unit box when it draws nothing.</summary>
		public static Bounds ArtBounds(GameObject prefab)
		{
			var bounds = new Bounds(Vector3.zero, Vector3.one);
			bool any = false;
			if (prefab != null)
			{
				foreach (MeshFilter filter in prefab.GetComponentsInChildren<MeshFilter>())
				{
					if (filter.sharedMesh == null)
					{
						continue;
					}
					Bounds b = filter.sharedMesh.bounds;
					if (!any) { bounds = b; any = true; } else { bounds.Encapsulate(b); }
				}
			}
			return bounds;
		}

		/// <summary>
		/// Lays a shelter's slab, stretched from the rock's boulder slab, as a prop with collision in a point of interest's
		/// set. False when the art is missing (run Generate Biome Art).
		/// </summary>
		public static bool PlaceAt(PointOfInterestPlaceContext context, in OverhangSlab slab, string rock)
		{
			var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{RiverBoulders.PrefabFolder}/{SlabPrefab(rock)}.prefab");
			if (prefab == null)
			{
				return false;
			}
			Stretch(ArtBounds(prefab), slab.Middle, slab.Rotation, slab.Size, out Vector3 position, out Vector3 scale);
			context.AddProp(prefab, position, slab.Rotation, scale);
			return true;
		}
	}
}
#endif
