#if UNITY_EDITOR
using System;
using FishMMO.Shared.NameGeneration;
using UnityEditor;
using UnityEngine;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>How an overhang or arch site is built (<see cref="OverhangShaper"/>).</summary>
	public enum OverhangBuild : byte
	{
		/// <summary>A stretched boulder slab projecting from the face over an alcove cut under it (<see cref="OverhangPlacer"/>).</summary>
		Slab = 0,
		/// <summary>The <c>Overhang</c> cliff section stood at the face's foot, its upper half leaning out over the ground.</summary>
		Section = 1,
		/// <summary>The <c>Arch</c> cliff section as a fin standing out of the face, pierced through its middle.</summary>
		Arch = 2,
	}

	/// <summary>
	/// The overhang and natural-arch kinds: a rock shelter or an arch at a steep face, laid as props in the scene's "POI"
	/// set (Jim, 2026-10-10: both FallLedges' stretched slab and a placeable lean/arch cliff section).
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Which.</b> A small or medium overhang is the slab (<see cref="OverhangPlacer.PlanAt"/>), with the ground under it
	/// cut back into an alcove; a large one is the <c>Overhang</c> section, a whole leaning wall. Every arch is the
	/// <c>Arch</c> section, its length along the site's heading so its inner end is sunk in the face and its arch, through
	/// its middle, opens along the face's foot.
	/// </para>
	/// <para>
	/// <b>Seating.</b> A chosen point, not a cliff band, so not <see cref="CliffRockPlacement"/>'s seating: the section's foot
	/// is set on the lowest ground under its front (a slope sinks the rest), its back in the face. The keep-out covers its
	/// footprint, so nothing grows in the shelter or the arch.
	/// </para>
	/// </remarks>
	public sealed class OverhangShaper : IPointOfInterestShaper
	{
		public static readonly OverhangShaper Instance = new OverhangShaper();

		public const string ShaperName = "Overhang";
		private const int Format = 1;

		public bool Handles(POIType kind) => kind == POIType.Overhang || kind == POIType.NaturalArch;

		private static int SeedOf(PointOfInterestRecord site) => (int)(PointOfInterestPlanner.Mix(unchecked((uint)site.SiteSeed ^ 0x0E4A9C17u)) & 0x7FFFFFFFu);

		/// <summary>What a planned site stores (a shape's values): build, section variant and scale, and the slab.</summary>
		public struct Plan
		{
			public OverhangBuild Build;
			public int Variant;
			public float Scale;
			/// <summary>The section's origin (its foot's centre), world, and its turn about +y.</summary>
			public Vector3 Position;
			public float Yaw;
			/// <summary>The slab, for <see cref="OverhangBuild.Slab"/> (and as the fallback where the section art is missing).</summary>
			public OverhangSlab Slab;
			public bool HasSlab;

			public float[] ToValues()
				=> new[]
				{
					Format, (float)Build, Variant, Scale, Position.x, Position.y, Position.z, Yaw, HasSlab ? 1f : 0f,
					Slab.Middle.x, Slab.Middle.y, Slab.Middle.z, Slab.Size.x, Slab.Size.y, Slab.Size.z, Slab.Yaw,
					Slab.Foot.x, Slab.Foot.y, Slab.Foot.z, Slab.AlcoveFront, Slab.AlcoveBack,
				};

			public static bool TryFrom(float[] v, out Plan plan)
			{
				plan = default;
				if (v == null || v.Length != 21 || Mathf.RoundToInt(v[0]) != Format)
				{
					return false;
				}
				plan = new Plan
				{
					Build = (OverhangBuild)Mathf.RoundToInt(v[1]),
					Variant = Mathf.RoundToInt(v[2]),
					Scale = v[3],
					Position = new Vector3(v[4], v[5], v[6]),
					Yaw = v[7],
					HasSlab = v[8] > 0.5f,
					Slab = new OverhangSlab
					{
						Middle = new Vector3(v[9], v[10], v[11]),
						Size = new Vector3(v[12], v[13], v[14]),
						Yaw = v[15],
						Foot = new Vector3(v[16], v[17], v[18]),
						AlcoveFront = v[19],
						AlcoveBack = v[20],
					},
				};
				return true;
			}
		}

		/// <summary>The section style a build uses, or null for the slab.</summary>
		public static string StyleOf(OverhangBuild build) => build == OverhangBuild.Section ? "Overhang" : build == OverhangBuild.Arch ? "Arch" : null;

		/// <summary>
		/// What a site gets, decided from its kind, size and the ground. Pure: no scene, no assets. Null-free: false with
		/// <paramref name="problem"/> where the face cannot take one.
		/// </summary>
		public static bool PlanSite(POIType kind, int size, Vector3 foot, float yaw, int seed, Func<float, float, float> ground, out Plan plan, out string problem)
		{
			plan = default;
			problem = null;
			var random = new DeterministicRNG(seed);
			float yr = yaw * Mathf.Deg2Rad;
			var outward = new Vector3(Mathf.Sin(yr), 0f, Mathf.Cos(yr));
			var side = new Vector3(outward.z, 0f, -outward.x);
			int variant = random.Next(CliffSections.VariantCount);
			float h0 = ground(foot.x, foot.z);
			float faceHeight = 0f;
			for (float d = 0f; d <= 32f; d += 1f)
			{
				faceHeight = Mathf.Max(faceHeight, ground(foot.x - outward.x * d, foot.z - outward.z * d) - h0);
			}

			if (kind == POIType.NaturalArch)
			{
				CliffStyle st = CliffSections.StyleOf("Arch", variant);
				float scale = random.Range(0.9f, 1.15f);
				float length = st.Length * scale;
				// Its inner sixth sunk in the face (the arch, through its middle, then opens a third of its length out); its foot
				// on the lowest ground under the part that stands out of the face.
				float centre = 0.5f * length - length / 6f;
				Vector3 at = foot + outward * centre;
				float low = float.MaxValue;
				for (float d = 0f; d <= centre + 0.5f * length; d += 1f)
				{
					Vector3 p = foot + outward * d;
					low = Mathf.Min(low, ground(p.x, p.z));
				}
				// The arch's own frame has its length along x: turn it so x runs out of the face.
				float archYaw = Mathf.Repeat(yaw - 90f, 360f);
				plan = new Plan { Build = OverhangBuild.Arch, Variant = variant, Scale = scale, Position = new Vector3(at.x, low, at.z), Yaw = archYaw };
				return true;
			}

			if (size >= 2 && faceHeight >= 9f)
			{
				CliffStyle st = CliffSections.StyleOf("Overhang", variant);
				float scale = Mathf.Clamp(0.85f * faceHeight / st.Height, 0.8f, 1.3f);
				float depth = st.Depth * scale, length = st.Length * scale;
				// Its front a metre out from the foot, the rest in the slope; its foot on the lowest ground across that front.
				Vector3 at = foot + outward * (1f - 0.5f * depth);
				float low = float.MaxValue;
				for (int k = -2; k <= 2; k++)
				{
					Vector3 p = foot + outward * 1f + side * (k * 0.22f * length);
					low = Mathf.Min(low, ground(p.x, p.z));
				}
				// The section's front faces −z: turn −z to face out.
				plan = new Plan { Build = OverhangBuild.Section, Variant = variant, Scale = scale, Position = new Vector3(at.x, low, at.z), Yaw = Mathf.Repeat(yaw + 180f, 360f) };
				// A slab too, should the section's art be missing when it is placed.
				plan.HasSlab = OverhangPlacer.PlanAt(foot, yaw, size, ground, seed ^ 0x51AB, out plan.Slab, out _);
				return true;
			}

			if (!OverhangPlacer.PlanAt(foot, yaw, size, ground, seed ^ 0x51AB, out OverhangSlab slab, out problem))
			{
				return false;
			}
			plan = new Plan { Build = OverhangBuild.Slab, Variant = variant, Scale = 1f, Position = slab.Middle, Yaw = slab.Yaw, Slab = slab, HasSlab = true };
			return true;
		}

		/// <summary>The disc the plan keeps everything else out of: the shelter's or the arch's footprint.</summary>
		public static Vector3 KeepOutOf(in Plan plan, float yaw)
		{
			float yr = yaw * Mathf.Deg2Rad;
			var outward = new Vector3(Mathf.Sin(yr), 0f, Mathf.Cos(yr));
			switch (plan.Build)
			{
				case OverhangBuild.Arch:
				{
					float length = CliffSections.StyleOf("Arch", plan.Variant).Length * plan.Scale;
					return new Vector3(plan.Position.x, 0.5f * length + 2f, plan.Position.z);
				}
				case OverhangBuild.Section:
				{
					CliffStyle st = CliffSections.StyleOf("Overhang", plan.Variant);
					Vector3 front = plan.Position + outward * (0.5f * st.Depth * plan.Scale);
					return new Vector3(front.x, 0.5f * st.Length * plan.Scale + 2f, front.z);
				}
				default:
				{
					OverhangSlab s = plan.Slab;
					float mid = 0.5f * (s.AlcoveFront + s.AlcoveBack);
					Vector3 c = s.Foot + outward * mid;
					return new Vector3(c.x, 0.5f * Mathf.Max(s.Size.x, s.AlcoveFront - s.AlcoveBack) + 2f, c.z);
				}
			}
		}

		// ── The seam ──────────────────────────────────────────────

		void IPointOfInterestShaper.Plan(PointOfInterestPlanContext context, PointOfInterestRecord site)
		{
			if (!PlanSite(site.Kind, site.SizeClass, site.Position, site.Yaw, SeedOf(site), context.GroundAt, out Plan plan, out string problem))
			{
				context.Reject(site, problem);
				return;
			}
			if (plan.Build == OverhangBuild.Slab)
			{
				OverhangPlacer.CarveAlcove(context.Terrains, context.Tiles, plan.Slab);
			}
			Vector3 keep = KeepOutOf(plan, site.Yaw);
			context.AddKeepOut(site, keep.x, keep.z, Mathf.Max(keep.y, site.Radius * 0.6f));
			context.AddShape(new PointOfInterestShape
			{
				SiteId = site.Id,
				Shaper = ShaperName,
				Position = plan.Position,
				Yaw = plan.Yaw,
				Size = plan.Build == OverhangBuild.Slab ? plan.Slab.Size : Vector3.one * plan.Scale,
				Seed = SeedOf(site),
				Values = plan.ToValues(),
			});
		}

		void IPointOfInterestShaper.Place(PointOfInterestPlaceContext context, PointOfInterestRecord site)
		{
			string where = $"{PointOfInterestKinds.Info(site.Kind).DisplayName} at ({site.Position.x:F0}, {site.Position.z:F0})";
			PointOfInterestShape shape = context.ShapesOf(site).Find(s => s.Shaper == ShaperName);
			if (shape == null || !Plan.TryFrom(shape.Values, out Plan plan))
			{
				context.Notes?.Add($"{where}: nothing is stored for it; re-cut the scene.");
				return;
			}
			string rock = PointOfInterestRock.At(context.Request, site, plan.Position);
			string style = StyleOf(plan.Build);
			if (style != null && PlaceSection(context, style, plan, rock))
			{
				return;
			}
			if (plan.HasSlab && OverhangPlacer.PlaceAt(context, plan.Slab, rock))
			{
				if (style != null)
				{
					context.Notes?.Add($"{where}: the {style} cliff section's art is missing (run Generate Biome Art); a slab stands in.");
				}
				return;
			}
			context.Notes?.Add($"{where}: its rock art is missing ({style ?? OverhangPlacer.SlabPrefab(rock)} in {rock}); run Generate Biome Art, then repaint.");
		}

		/// <summary>Lays a section as a prop: its shared prefab in the site's stone (made once, as the cliffs' are).</summary>
		private static bool PlaceSection(PointOfInterestPlaceContext context, string style, in Plan plan, string rock)
		{
			int levels = CliffSections.LevelCount;
			var meshes = new Mesh[levels];
			for (int lod = 0; lod < levels; lod++)
			{
				meshes[lod] = AssetDatabase.LoadAssetAtPath<Mesh>(ProceduralArtCatalogue.MeshPath(CliffSections.MeshName(style, plan.Variant, lod)));
				if (meshes[lod] == null)
				{
					return false;
				}
			}
			Material material = PointOfInterestRock.MaterialOf(rock);
			if (material == null)
			{
				return false;
			}
			GameObject prefab = CliffPlacer.RockPrefab($"Crag Section_{style}_{plan.Variant} {material.name}", meshes, meshes[Mathf.Min(CliffRocks.CollisionLod, levels - 1)],
				material, CliffPlacer.ColliderLayer, CliffRocks.LodHeights, false);
			if (prefab == null)
			{
				return false;
			}
			context.AddProp(prefab, plan.Position, Quaternion.Euler(0f, plan.Yaw, 0f), Vector3.one * plan.Scale);
			return true;
		}
	}
}
#endif
