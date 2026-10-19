#if UNITY_EDITOR
using System.Collections.Generic;
using FishMMO.Shared.Biomes;
using UnityEngine;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>
	/// The linear features' pieces (2026-10-18, phase I2f, decisions 23 and 29): what a farmed and grazed country is divided and
	/// watered by, laid module by module along a line by PointOfInterestLinearFeatures as baked props, collided with and walked
	/// round on the NavMesh like any wall.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>The record.</b> Laid hedges (the pleachers cut part through at the foot and bent along the line, woven between stakes,
	/// bound along the top with hazel binders) are described from the thirteenth century on; drystone field walls, sheepfolds and
	/// clearance cairns are older than any record (the Céide Fields, the clapas of the Causses); the Lombard plain was walled in
	/// brick round its gardens (brolo) and watered by rogge carried off the rivers and springs, its fields bounded by rows of
	/// pollards with vines swagged from tree to tree (the piantata); the Valais carried its water across cliffs in bisses, the
	/// Alps fenced with upright slabs and pickets, the Carpathians with poles leant between stakes, everyone with wattle hurdles.
	/// </para>
	/// <para>
	/// <b>Frames.</b> A wall, hedge base, fence, gate or clearance row runs along x, centred on the origin, one module long
	/// (<see cref="StructurePiece.ModuleLength"/>), its foot at y = 0 and its footing a little below to sit on a slope, as the
	/// kit's field fence does; it is pitched to the ground's chord and scaled along x to the gap it fills, and in y to the height
	/// its style asks. A channel's pieces run along z, the water's way: its sluice and its crossing stand across it at z = 0, its
	/// flume is a deck piece (<see cref="StructureAnchor.Deck"/>, y = 0 the trough's rim) laid end to end on the boardwalk's bents.
	/// </para>
	/// <para>
	/// <b>Parts.</b> Each piece's body plays the palette role whose kit material it is built in (fences and hedge stems the
	/// Fence, a mortared wall the stone style's Fence, a drystone wall and clearance stones the Plinth, an earth bank its Turf,
	/// upright slabs Carving), so a biome's palette reaches them, and a style naming its own material lays them in that.
	/// </para>
	/// </remarks>
	public static partial class StructurePieces
	{
		/// <summary>The module lengths along x of the linear pieces, metres.</summary>
		public const float HedgeModule = 3f, DrystoneModule = 3f, MortaredModule = 4f, BankModule = 4f, SlabsModule = 3f,
			PoleFenceModule = 3f, WattleModule = 2f, PicketModule = 3f, SlabFenceModule = 3f, GateModule = 3f, ClearanceModule = 4f;

		/// <summary>The heights the linear pieces are built at, metres (a style's own height scales them in y).</summary>
		public const float HedgeBaseHeight = 1.1f, DrystoneHeight = 1.3f, MortaredHeight = 2.2f, BankHeight = 1.2f, SlabsHeight = 1f,
			PoleFenceHeight = 1.3f, WattleHeight = 1.2f, PicketHeight = 1.2f, SlabFenceHeight = 1.4f, ClearanceHeight = 0.8f;

		/// <summary>A channel's width as its sluice and crossing are built, metres (scaled across to the channel).</summary>
		public const float ChannelBuiltWidth = 1.2f;
		/// <summary>A flume module's length along z, metres, and how far its trough's water lies under its rim.</summary>
		public const float FlumeModule = 4f, FlumeWater = 0.14f;

		private static void AddLinear(List<StructurePiece> list)
		{
			Linear(list, "LinearHedge", StructureStyle.Timber, StructureSize.Medium, HedgeModule,
				"Three metres of a hedge's woody base along x, its leaves the biome's own bushes grown over it: v0 laid (pleachers bent along the line between stakes, hazel binders along the top), v1 the stems of a clipped or grown-out hedge.",
				HedgeBase);
			Linear(list, "LinearDrystone", StructureStyle.Stone, StructureSize.Medium, DrystoneModule,
				"Three metres of drystone wall along x, 1.3 m: v0 two battered faces of coursed field stone with through-stones and a coping on edge; v1 older and rougher, big stones, the coping tumbled.",
				DrystoneWall);
			Linear(list, "LinearMortared", StructureStyle.Stone, StructureSize.Medium, MortaredModule,
				"Four metres of mortared wall along x, 2.2 m, on a plinth course under a capping; v1 with a buttress pier at its start (a garden's brolo).",
				MortaredWall);
			Linear(list, "LinearEarthBank", StructureStyle.Stone, StructureSize.Medium, BankModule,
				"Four metres of earth bank along x, 1.2 m, turfed: v0 earth throughout; v1 faced with field stone, turf on top.",
				EarthBank);
			Linear(list, "LinearSlabs", StructureStyle.Stone, StructureSize.Medium, SlabsModule,
				"Three metres of stone slabs set upright on edge side by side along x, about a metre: v0 even; v1 taller and ragged.",
				UprightSlabs);
			Linear(list, "LinearPoleFence", StructureStyle.Timber, StructureSize.Medium, PoleFenceModule,
				"Three metres of leaning-pole fence along x: split poles leant at a slant between pairs of stakes and tied; v1 with a pole rail along the top.",
				PoleFence);
			Linear(list, "LinearWattle", StructureStyle.Timber, StructureSize.Medium, WattleModule,
				"A two-metre wattle hurdle along x: rods woven between upright sails; v1 weathered, a course or two gone and sagging.",
				WattleFence);
			Linear(list, "LinearPickets", StructureStyle.Timber, StructureSize.Medium, PicketModule,
				"Three metres of picket fence along x: pointed split pickets on two rails between posts; v1 a taller garden fence.",
				PicketFence);
			Linear(list, "LinearSlabFence", StructureStyle.Timber, StructureSize.Medium, SlabFenceModule,
				"Three metres of slab fence along x: split slabs set upright close together on two rails; v1 weathered, uneven and gapped.",
				SlabFence);
			Linear(list, "LinearGate", StructureStyle.Timber, StructureSize.Medium, GateModule,
				"A three-metre field gateway along x: v0 timber gateposts, v1 stone piers, a barred gate standing open on its hinge post.",
				FieldGate);
			Linear(list, "LinearClearance", StructureStyle.Stone, StructureSize.Medium, ClearanceModule,
				"Four metres of clearance row along x, the stones picked off a field: v0 heaped loose; v1 a turfed bank, its stones' tops showing.",
				ClearanceRow);
			StructurePiece sluice = Piece(list, "ChannelSluice", StructureStyle.Timber, StructureSize.Small,
				"A water channel's sluice across x at z = 0 (the channel along z, 1.2 m wide, scale x to the channel): v0 a plank board in grooved timber posts under a head beam; v1 stone cheeks and sill, the board between them (a channel's head or its partition).",
				ChannelSluice, "linear");
			sluice.Anchor = StructureAnchor.Ground;
			StructurePiece flume = Piece(list, "ChannelFlume", StructureStyle.Timber, StructureSize.Medium,
				"Four metres of plank flume along z, its rim at y = 0, carrying a channel over a dip or along a steep side on the boardwalk's bents: v0 an open trough with cross-ties; v1 deeper, braced with brackets.",
				ChannelFlume, "linear");
			flume.Anchor = StructureAnchor.Deck;
			flume.ModuleLength = FlumeModule;
			Piece(list, "ChannelBridge", StructureStyle.Timber, StructureSize.Small,
				"Where a way crosses a water channel running along z: v0 a plank footbridge across x over the channel; v1 the stone headwalls of a culvert either side of a road (scale z to the road).",
				ChannelBridge, "linear");
		}

		/// <summary>
		/// The piece (and variant) a form is laid in, module by module (pure): a laid hedge's base v0, a clipped or wild one's v1; a
		/// drystone wall v0, tumbled (<paramref name="broken"/>) v1; a mortared wall with its piers v1, an old one plain v0; an earth bank
		/// stone-faced on three lines in ten (<paramref name="roll"/>, drawn once a line); slabs and leaning-pole fences their taller or
		/// railed variant on half the lines; a picket fence its garden height on half; wattle, slab fences and palisades weathered when
		/// broken; the kit's field fence for rails (its variant drawn per module by the caller) and its palisade; heaped and turfed
		/// clearance rows. Null for a row of trees and a channel, which are no modules.
		/// </summary>
		public static (string Id, int Variant) LinearPieceFor(BiomeLinearForm form, bool broken, float roll)
		{
			switch (form)
			{
				case BiomeLinearForm.Laid: return ("LinearHedge", 0);
				case BiomeLinearForm.Clipped:
				case BiomeLinearForm.Wild: return ("LinearHedge", 1);
				case BiomeLinearForm.Drystone: return ("LinearDrystone", broken ? 1 : 0);
				case BiomeLinearForm.Mortared: return ("LinearMortared", broken ? 0 : 1);
				case BiomeLinearForm.EarthBank: return ("LinearEarthBank", roll < 0.3f ? 1 : 0);
				case BiomeLinearForm.UprightSlabs: return ("LinearSlabs", roll < 0.5f ? 1 : 0);
				case BiomeLinearForm.Rail: return ("FieldFence", 0);
				case BiomeLinearForm.LeaningPole: return ("LinearPoleFence", roll < 0.5f ? 1 : 0);
				case BiomeLinearForm.Wattle: return ("LinearWattle", broken ? 1 : 0);
				case BiomeLinearForm.Picket: return ("LinearPickets", roll < 0.5f ? 1 : 0);
				case BiomeLinearForm.Slab: return ("LinearSlabFence", broken ? 1 : 0);
				case BiomeLinearForm.Palisade: return ("PalisadeSegment", broken ? 1 : 0);
				case BiomeLinearForm.Heaped: return ("LinearClearance", 0);
				case BiomeLinearForm.Turfed: return ("LinearClearance", 1);
				default: return (null, 0);
			}
		}

		/// <summary>The height a linear module piece is built at, metres: what a style's own height scales it from (1 for one not listed).</summary>
		public static float LinearBuiltHeight(string id)
		{
			switch (id)
			{
				case "LinearHedge": return BiomeLinearFeatures.DefaultHeight(BiomeLinearForm.Laid);
				case "LinearDrystone": return DrystoneHeight;
				case "LinearMortared": return MortaredHeight;
				case "LinearEarthBank": return BankHeight;
				case "LinearSlabs": return SlabsHeight;
				case "FieldFence": return 1.25f;
				case "LinearPoleFence": return PoleFenceHeight;
				case "LinearWattle": return WattleHeight;
				case "LinearPickets": return PicketHeight;
				case "LinearSlabFence": return SlabFenceHeight;
				case "PalisadeSegment": return 3f;
				case "LinearClearance": return ClearanceHeight;
				default: return 1f;
			}
		}

		/// <summary>
		/// The palette roles a linear piece's body plays (what a style naming its own material lays in it): a hedge's stems, a fence's
		/// timber and a mortared wall the Fence; a drystone wall and clearance stones the Plinth; an earth bank and a turfed row its Turf;
		/// upright slabs Carving; a gateway's piers (in a wall or a clearance row) Dressing and its gate (in a fence) the Fence; none for a
		/// hedge's gate, whose posts are the biome's own timber.
		/// </summary>
		public static BiomeStructureRole[] LinearRolesOf(string id, BiomeLinearKind kind)
		{
			switch (id)
			{
				case "LinearDrystone": return new[] { BiomeStructureRole.Plinth };
				case "LinearEarthBank": return new[] { BiomeStructureRole.Turf };
				case "LinearSlabs": return new[] { BiomeStructureRole.Carving };
				case "LinearClearance": return new[] { BiomeStructureRole.Plinth, BiomeStructureRole.Turf };
				case "LinearGate":
					return kind == BiomeLinearKind.Fence ? new[] { BiomeStructureRole.Fence }
						: kind == BiomeLinearKind.Wall || kind == BiomeLinearKind.ClearanceRow ? new[] { BiomeStructureRole.Dressing } : System.Array.Empty<BiomeStructureRole>();
				case "ChannelSluice":
				case "ChannelBridge": return new[] { BiomeStructureRole.Dressing };
				default: return new[] { BiomeStructureRole.Fence };
			}
		}

		/// <summary>
		/// The palette a linear piece is laid in (pure): the biome's (<paramref name="biome"/>, null the kit's), or, where its style names
		/// its own material, that palette with the piece's body roles (<see cref="LinearRolesOf"/>) in the piece's style made of it, first
		/// (the first entry for a style and role wins). A fresh palette only when the style names its own.
		/// </summary>
		public static BiomeStructurePalette LinearPalette(BiomeStructurePalette biome, in BiomeLinearStyle style, string id)
		{
			if (!style.OwnMaterial || string.IsNullOrEmpty(id))
			{
				return biome;
			}
			StructurePiece piece = Find(id);
			if (piece == null)
			{
				return biome;
			}
			BiomeStructurePalette palette = biome != null ? biome.Clone() : new BiomeStructurePalette();
			int at = 0;
			foreach (BiomeStructureRole role in LinearRolesOf(id, style.Kind))
			{
				palette.Surfaces.Insert(at++, new BiomeStructureSurface((BiomeStructureStyle)(int)piece.Style, role, style.Material, style.Tint));
			}
			return palette;
		}

		/// <summary>
		/// The palettes the styles naming their own material lay their pieces in, the kit's otherwise (for the art generator: each
		/// material key one of them lays is made, PalettesInUse). One per style and piece its form may use (its gate included).
		/// </summary>
		public static IEnumerable<BiomeStructurePalette> LinearPalettes(IEnumerable<BiomeLinearStyle> styles)
		{
			if (styles == null)
			{
				yield break;
			}
			foreach (BiomeLinearStyle style in styles)
			{
				if (!style.OwnMaterial)
				{
					continue;
				}
				(string id, _) = LinearPieceFor(style.Form, false, 0f);
				if (id != null)
				{
					yield return LinearPalette(null, style, id);
				}
				if (style.Kind != BiomeLinearKind.TreeRow && style.Kind != BiomeLinearKind.Channel)
				{
					yield return LinearPalette(null, style, "LinearGate");
				}
				if (style.Kind == BiomeLinearKind.Channel)
				{
					yield return LinearPalette(null, style, "ChannelSluice");
				}
			}
		}

		private static StructurePiece Linear(List<StructurePiece> list, string id, StructureStyle style, StructureSize size, float module, string description,
			System.Action<StructureDraft> builder)
		{
			StructurePiece p = Piece(list, id, style, size, description, builder, "linear");
			p.ModuleLength = module;
			return p;
		}

		// ── Hedges ────────────────────────────────────────────────────

		/// <summary>
		/// A hedge's woody base: v0 laid (stakes every half metre, pleachers bent along the line at a slant through them, two
		/// binders twisted along the top), v1 the irregular stems of a clipped or grown-out hedge, a few low branches between.
		/// </summary>
		private static void HedgeBase(StructureDraft d)
		{
			const float half = HedgeModule * 0.5f, h = HedgeBaseHeight;
			using (d.Builds(FenceRoles))
			{
				if (d.Variant == 0)
				{
					int stakes = d.Fine ? 6 : d.Mid ? 4 : 2;
					for (int k = 0; k < stakes; k++)
					{
						float x = -half + HedgeModule * (k + 0.5f) / stakes;
						d.Add(StructureShapes.Rod(new Vector3(x, -0.3f, (k % 2 == 0 ? 0.06f : -0.06f) + d.Range(k, 1, -0.02f, 0.02f)), new Vector3(x + d.Range(k, 2, -0.04f, 0.04f), h, 0f), 0.035f, d.Seg(5, 3),
							StructureMaterial.Timber, 1 + k));
					}
					int pleachers = d.Fine ? 7 : d.Mid ? 4 : 2;
					for (int k = 0; k < pleachers; k++)
					{
						// Laid one way, rising from the stool at the foot along the line to the top of the next stakes.
						float x0 = -half - 0.4f + HedgeModule * k / pleachers;
						float z = d.Range(k, 3, -0.18f, 0.18f);
						d.Add(StructureShapes.Rod(new Vector3(x0, 0.05f, z), new Vector3(x0 + 1.9f, h * 0.85f, -z * 0.5f), d.Range(k, 4, 0.03f, 0.05f), d.Seg(5, 3),
							StructureMaterial.Timber, 20 + k));
					}
					if (d.Mid)
					{
						for (int b = 0; b < 2; b++)
						{
							d.Add(StructureShapes.Rod(new Vector3(-half, h - 0.04f - b * 0.07f, b * 0.04f - 0.02f), new Vector3(half, h - 0.06f - b * 0.07f, 0.02f - b * 0.04f),
								0.02f, d.Seg(4, 3), StructureMaterial.Timber, 40 + b, StructureRole.Detail));
						}
					}
				}
				else
				{
					int stems = d.Fine ? 7 : d.Mid ? 5 : 3;
					for (int k = 0; k < stems; k++)
					{
						float x = -half + HedgeModule * (k + 0.5f) / stems + d.Range(k, 5, -0.15f, 0.15f);
						float z = d.Range(k, 6, -0.25f, 0.25f);
						var top = new Vector3(x + d.Range(k, 7, -0.35f, 0.35f), h * d.Range(k, 8, 0.75f, 1f), z + d.Range(k, 9, -0.2f, 0.2f));
						d.Add(StructureShapes.Rod(new Vector3(x, -0.2f, z), top, d.Range(k, 10, 0.04f, 0.08f), d.Seg(6, 3), StructureMaterial.Timber, 50 + k));
						if (d.Fine)
						{
							d.Add(StructureShapes.Rod(top * 0.55f + new Vector3(x, 0f, z) * 0.45f, top + new Vector3(d.Range(k, 11, -0.5f, 0.5f), -0.1f, d.Range(k, 12, -0.3f, 0.3f)),
								0.025f, 4, StructureMaterial.Timber, 70 + k, StructureRole.Detail));
						}
					}
				}
			}
		}

		// ── Walls ─────────────────────────────────────────────────────

		/// <summary>
		/// A drystone wall: two faces of field stone battered in from a footing 0.7 m wide to 0.4 m under the coping, courses of
		/// stones each its own length, a through-stone now and then, a coping of stones set on edge (v0); v1 lower, rougher and
		/// bigger-stoned, its coping fallen to a few stones.
		/// </summary>
		private static void DrystoneWall(StructureDraft d)
		{
			bool rough = d.Variant == 1;
			float half = DrystoneModule * 0.5f, height = rough ? 1.05f : DrystoneHeight, cope = rough ? 0f : 0.28f;
			float body = height - cope;
			// The hearting: the wall's core, so no face shows daylight through it.
			var rings = new List<Vector3[]>();
			foreach (float x in new[] { -half, half })
			{
				rings.Add(new[] { new Vector3(x, -0.2f, 0.36f), new Vector3(x, body, 0.2f), new Vector3(x, body, -0.2f), new Vector3(x, -0.2f, -0.36f) });
			}
			using (d.Builds(PlinthRoles))
			{
				d.Add(StructureShapes.Loft(rings, StructureMaterial.Fieldstone, 1));
				if (d.Lod < 2)
				{
					int courses = d.Fine ? (rough ? 3 : 5) : (rough ? 2 : 3);
					int perCourse = d.Fine ? (rough ? 4 : 6) : (rough ? 3 : 4);
					float courseH = body / courses;
					for (int c = 0; c < courses; c++)
					{
						float y = courseH * (c + 0.5f);
						float face = Mathf.Lerp(0.36f, 0.2f, y / body);
						float stagger = (c % 2 == 0 ? 0f : 0.5f) * DrystoneModule / perCourse;
						for (int side = -1; side <= 1; side += 2)
						{
							for (int k = 0; k < perCourse; k++)
							{
								int key = 100 + (c * 2 + (side > 0 ? 1 : 0)) * 16 + k;
								float len = DrystoneModule / perCourse * d.Range(key, 1, 0.82f, 0.98f);
								float x = -half + (k + 0.5f) * DrystoneModule / perCourse + stagger;
								if (x > half)
								{
									x -= DrystoneModule;
								}
								var size = new Vector3(len, courseH * d.Range(key, 2, 0.85f, 1.05f), d.Range(key, 3, 0.16f, 0.24f));
								var at = new Vector3(x, y, side * (face - size.z * 0.35f));
								d.Add(StructureShapes.Block(at, size, Quaternion.Euler(d.Range(key, 4, -4f, 4f), d.Range(key, 5, -5f, 5f), d.Range(key, 6, -5f, 5f)),
									StructureMaterial.Fieldstone, key));
							}
						}
						// A through-stone across the wall half way up, its ends standing proud of both faces.
						if (!rough && c == courses / 2 && d.Fine)
						{
							d.Add(StructureShapes.Block(new Vector3(d.Range(c, 7, -0.6f, 0.6f), y + courseH * 0.5f, 0f), new Vector3(0.3f, 0.12f, 0.95f), Quaternion.identity,
								StructureMaterial.Fieldstone, 90));
						}
					}
				}
				// The coping: stones on edge along the top.
				int copes = rough ? (d.Fine ? 3 : 1) : d.Fine ? 10 : d.Mid ? 6 : 1;
				for (int k = 0; k < copes; k++)
				{
					if (d.Lod == 2 && !rough)
					{
						d.Add(StructureShapes.Box(new Vector3(0f, body + cope * 0.5f, 0f), new Vector3(DrystoneModule, cope, 0.36f), StructureMaterial.Fieldstone, 60));
						break;
					}
					int key = 300 + k;
					float x = -half + DrystoneModule * (k + 0.5f) / copes + (rough ? d.Range(key, 1, -0.4f, 0.4f) : 0f);
					var size = rough ? new Vector3(0.5f, 0.3f, 0.4f) : new Vector3(DrystoneModule / copes * 0.9f, cope, 0.38f);
					d.Add(StructureShapes.Block(new Vector3(x, body + size.y * 0.5f - (rough ? 0.08f : 0f), 0f), size,
						Quaternion.Euler(0f, d.Range(key, 2, -6f, 6f), d.Range(key, 3, -8f, 8f) + (rough ? 15f : 0f)), StructureMaterial.Fieldstone, key));
				}
			}
		}

		/// <summary>A mortared wall: a plinth course, the wall 0.45 m thick, a projecting capping; v1 a buttress pier at its start.</summary>
		private static void MortaredWall(StructureDraft d)
		{
			float half = MortaredModule * 0.5f, h = MortaredHeight;
			using (d.Builds(FenceRoles))
			{
				d.Add(StructureShapes.Box(new Vector3(0f, -0.15f + 0.2f, 0f), new Vector3(MortaredModule, 0.4f + 0.3f, 0.6f), StructureMaterial.Stone, 1));
				d.Add(StructureShapes.Box(new Vector3(0f, 0.35f + (h - 0.5f) * 0.5f, 0f), new Vector3(MortaredModule, h - 0.5f, 0.45f), StructureMaterial.Stone, 2));
				d.Add(StructureShapes.ChamferBox(new Vector3(0f, h - 0.075f, 0f), new Vector3(MortaredModule + 0.02f, 0.15f, 0.62f), d.Bevel(0.03f), StructureMaterial.Stone, 3));
				if (d.Variant == 1)
				{
					d.Add(StructureShapes.ChamferBox(new Vector3(-half + 0.3f, (h + 0.1f) * 0.5f - 0.15f, 0f), new Vector3(0.6f, h + 0.4f, 0.75f), d.Bevel(0.02f), StructureMaterial.Stone, 4));
					d.Add(StructureShapes.ChamferBox(new Vector3(-half + 0.3f, h + 0.27f, 0f), new Vector3(0.72f, 0.14f, 0.87f), d.Bevel(0.03f), StructureMaterial.Stone, 5));
				}
			}
		}

		/// <summary>An earth bank, trapezoid in section (2.6 m at its foot, 1 m over its top): v0 turf and earth; v1 faced with field stone, turf on top.</summary>
		private static void EarthBank(StructureDraft d)
		{
			float half = BankModule * 0.5f, h = BankHeight;
			var rings = new List<Vector3[]>();
			foreach (float x in new[] { -half, half })
			{
				// Counter-clockwise looking back along x: in (z, y), from the foot on +z up and over to the foot on −z.
				rings.Add(new[] { new Vector3(x, -0.3f, 1.3f), new Vector3(x, h, 0.5f), new Vector3(x, h, -0.5f), new Vector3(x, -0.3f, -1.3f) });
			}
			using (d.Builds(BiomeStructureRole.Turf))
			{
				d.Add(StructureShapes.Loft(rings, StructureMaterial.Earth, 1));
			}
			if (d.Variant == 1 && d.Lod < 2)
			{
				using (d.Builds(PlinthRoles))
				{
					int courses = d.Fine ? 3 : 2, per = d.Fine ? 6 : 3;
					for (int side = -1; side <= 1; side += 2)
					{
						for (int c = 0; c < courses; c++)
						{
							float y = (h - 0.25f) * (c + 0.5f) / courses;
							float z = Mathf.Lerp(1.3f, 0.5f, (y + 0.3f) / (h + 0.3f)) * side;
							for (int k = 0; k < per; k++)
							{
								int key = 20 + (c * 2 + (side > 0 ? 1 : 0)) * 8 + k;
								float x = -half + BankModule * (k + 0.5f + (c % 2) * 0.5f) / per;
								x = x > half ? x - BankModule : x;
								d.Add(StructureShapes.Block(new Vector3(x, y, z), new Vector3(BankModule / per * 0.9f, (h - 0.25f) / courses * 0.9f, 0.2f),
									Quaternion.Euler(-side * 32f, d.Range(key, 1, -4f, 4f), d.Range(key, 2, -4f, 4f)), StructureMaterial.Fieldstone, key));
							}
						}
					}
				}
			}
		}

		/// <summary>Stone slabs set upright on edge side by side (v0 even, about a metre; v1 taller, ragged), a hand buried.</summary>
		private static void UprightSlabs(StructureDraft d)
		{
			float half = SlabsModule * 0.5f;
			bool tall = d.Variant == 1;
			int slabs = d.Lod == 2 ? 1 : tall ? 5 : 6;
			using (d.Builds(BiomeStructureRole.Carving))
			{
				for (int k = 0; k < slabs; k++)
				{
					float w = SlabsModule / slabs;
					float hgt = d.Lod == 2 ? SlabsHeight : (tall ? 1.3f : SlabsHeight) * d.Range(k, 1, tall ? 0.75f : 0.92f, 1.05f);
					d.Add(StructureShapes.Block(new Vector3(-half + w * (k + 0.5f), (hgt - 0.3f) * 0.5f, d.Range(k, 2, -0.03f, 0.03f)), new Vector3(w - 0.03f, hgt + 0.3f, 0.12f),
						Quaternion.Euler(d.Range(k, 3, -4f, 4f), d.Range(k, 4, -3f, 3f), d.Range(k, 5, -3f, 3f)), StructureMaterial.Monolith, 1 + k, StructureRole.Structure, d.Bevel(0.015f)));
				}
			}
		}

		/// <summary>A clearance row: field stones heaped in a low ridge over a rubble core (v0); v1 a turfed bank, its stones' tops showing.</summary>
		private static void ClearanceRow(StructureDraft d)
		{
			float half = ClearanceModule * 0.5f, h = ClearanceHeight;
			bool turfed = d.Variant == 1;
			var rings = new List<Vector3[]>();
			foreach (float x in new[] { -half, half })
			{
				rings.Add(new[] { new Vector3(x, -0.2f, 0.85f), new Vector3(x, h * 0.8f, 0.35f), new Vector3(x, h * (turfed ? 0.95f : 0.85f), 0f), new Vector3(x, h * 0.8f, -0.35f), new Vector3(x, -0.2f, -0.85f) });
			}
			if (turfed)
			{
				using (d.Builds(BiomeStructureRole.Turf))
				{
					d.Add(StructureShapes.Loft(rings, StructureMaterial.Earth, 1));
				}
			}
			using (d.Builds(PlinthRoles))
			{
				if (!turfed)
				{
					d.Add(StructureShapes.Loft(rings, StructureMaterial.Fieldstone, 1));
				}
				int stones = d.Fine ? (turfed ? 6 : 14) : d.Mid ? (turfed ? 3 : 7) : 0;
				for (int k = 0; k < stones; k++)
				{
					int key = 10 + k;
					float x = -half + ClearanceModule * (k + 0.5f) / stones + d.Range(key, 1, -0.15f, 0.15f);
					float z = d.Range(key, 2, -0.5f, 0.5f);
					float r = d.Range(key, 3, 0.22f, 0.4f);
					float y = Mathf.Lerp(h * 0.85f, 0.1f, Mathf.Abs(z) / 0.85f) - (turfed ? r * 0.5f : r * 0.25f);
					d.Add(StructureShapes.Block(new Vector3(x, y, z), new Vector3(r * 1.5f, r, r * 1.2f),
						Quaternion.Euler(d.Range(key, 4, -20f, 20f), d.Range(key, 5, 0f, 180f), d.Range(key, 6, -20f, 20f)), StructureMaterial.Fieldstone, key, StructureRole.Structure, d.Bevel(0.04f)));
				}
			}
		}

		// ── Fences ────────────────────────────────────────────────────

		/// <summary>
		/// A leaning-pole fence: a pair of stakes at the module's start and middle, split poles leant along the line from the
		/// ground to the top of the next pair, tied; v1 a pole rail laid in the stakes' forks along the top.
		/// </summary>
		private static void PoleFence(StructureDraft d)
		{
			float half = PoleFenceModule * 0.5f, h = PoleFenceHeight;
			using (d.Builds(FenceRoles))
			{
				for (int p = 0; p < 2; p++)
				{
					float x = -half + p * PoleFenceModule * 0.5f;
					for (int s = -1; s <= 1; s += 2)
					{
						d.Add(StructureShapes.Rod(new Vector3(x, -0.4f, s * 0.09f), new Vector3(x + d.Range(p * 2 + s, 1, -0.03f, 0.03f), h + 0.15f, s * 0.11f), 0.045f, d.Seg(5, 3),
							StructureMaterial.Timber, 1 + p * 2 + (s > 0 ? 1 : 0)));
					}
					if (d.Mid)
					{
						d.Add(StructureShapes.Box(new Vector3(x, h - 0.1f, 0f), new Vector3(0.1f, 0.06f, 0.3f), StructureMaterial.Timber, 8 + p, StructureRole.Detail));
					}
				}
				int poles = d.Fine ? 8 : d.Mid ? 5 : 2;
				for (int k = 0; k < poles; k++)
				{
					float x0 = -half + PoleFenceModule * k / poles - 0.6f;
					d.Add(StructureShapes.Rod(new Vector3(x0, 0f, d.Range(k, 2, -0.03f, 0.03f)), new Vector3(x0 + 1.55f, h, d.Range(k, 3, -0.03f, 0.03f)), d.Range(k, 4, 0.03f, 0.045f), d.Seg(5, 3),
						StructureMaterial.Timber, 20 + k));
				}
				if (d.Variant == 1)
				{
					d.Add(StructureShapes.Rod(new Vector3(-half, h + 0.05f, 0f), new Vector3(half, h + 0.05f, 0f), 0.05f, d.Seg(6, 3), StructureMaterial.Timber, 40));
				}
			}
		}

		/// <summary>A wattle hurdle: upright sails, rods woven round them course by course; v1 weathered, a course or two gone, sagging.</summary>
		private static void WattleFence(StructureDraft d)
		{
			float half = WattleModule * 0.5f, h = WattleHeight;
			bool old = d.Variant == 1;
			using (d.Builds(FenceRoles))
			{
				int sails = d.Fine ? 8 : d.Mid ? 5 : 2;
				for (int k = 0; k < sails; k++)
				{
					float x = -half + 0.08f + (WattleModule - 0.16f) * k / Mathf.Max(1, sails - 1);
					d.Add(StructureShapes.Rod(new Vector3(x, -0.35f, 0f), new Vector3(x, h + 0.05f, 0f), 0.04f, d.Seg(5, 3), StructureMaterial.Timber, 1 + k));
				}
				if (d.Lod == 2)
				{
					d.Add(StructureShapes.Box(new Vector3(0f, h * 0.5f, 0f), new Vector3(WattleModule, h * 0.9f, 0.12f), StructureMaterial.Timber, 30));
					return;
				}
				int courses = d.Fine ? 12 : 6;
				for (int c = 0; c < courses; c++)
				{
					if (old && (c == 3 || c == 8))
					{
						continue;
					}
					float y = 0.08f + (h - 0.12f) * c / (courses - 1);
					float sag = old ? -0.06f * Mathf.Sin(Mathf.PI * c / courses) : 0f;
					float z = (c % 2 == 0 ? 1f : -1f) * 0.045f;
					d.Add(StructureShapes.Beam(new Vector3(-half, y, z), new Vector3(half, y + sag, -z), 0.07f + d.Range(c, 1, 0f, 0.03f), 0.04f, StructureMaterial.Timber, 40 + c));
				}
			}
		}

		/// <summary>A picket fence: posts at its start and middle, two rails, pointed split pickets close along it; v1 taller.</summary>
		private static void PicketFence(StructureDraft d)
		{
			float half = PicketModule * 0.5f, h = d.Variant == 1 ? 1.5f : PicketHeight;
			using (d.Builds(FenceRoles))
			{
				for (int p = 0; p < 2; p++)
				{
					float x = -half + p * PicketModule * 0.5f;
					d.Add(StructureShapes.Box(new Vector3(x, (h - 0.4f) * 0.5f, -0.06f), new Vector3(0.11f, h + 0.4f, 0.11f), StructureMaterial.Timber, 1 + p)).Grain = Vector3.up;
				}
				foreach (float y in new[] { 0.3f, h - 0.3f })
				{
					d.Add(StructureShapes.Beam(new Vector3(-half, y, -0.1f), new Vector3(half, y, -0.1f), 0.08f, 0.04f, StructureMaterial.Timber, y < 0.5f ? 5 : 6));
				}
				if (d.Lod == 2)
				{
					d.Add(StructureShapes.Box(new Vector3(0f, h * 0.5f, 0f), new Vector3(PicketModule, h, 0.03f), StructureMaterial.Timber, 7));
					return;
				}
				int pickets = d.Fine ? 20 : 10;
				for (int k = 0; k < pickets; k++)
				{
					float x = -half + PicketModule * (k + 0.5f) / pickets;
					float top = h * d.Range(k, 1, 0.97f, 1.02f);
					d.Add(StructureShapes.Rod(new Vector3(x, -0.05f, 0f), new Vector3(x, top, 0f), 0.04f, 4, StructureMaterial.Timber, 10 + k, StructureRole.Structure, 0.08f));
				}
			}
		}

		/// <summary>A slab fence: split slabs set upright close together on two rails; v1 weathered, uneven, a slab or two gone.</summary>
		private static void SlabFence(StructureDraft d)
		{
			float half = SlabFenceModule * 0.5f, h = SlabFenceHeight;
			bool old = d.Variant == 1;
			using (d.Builds(FenceRoles))
			{
				foreach (float y in new[] { 0.35f, h - 0.35f })
				{
					d.Add(StructureShapes.Beam(new Vector3(-half, y, -0.07f), new Vector3(half, y, -0.07f), 0.1f, 0.06f, StructureMaterial.Timber, y < 0.5f ? 1 : 2));
				}
				d.Add(StructureShapes.Box(new Vector3(-half + 0.06f, (h - 0.4f) * 0.5f, -0.15f), new Vector3(0.12f, h + 0.4f, 0.12f), StructureMaterial.Timber, 3)).Grain = Vector3.up;
				int slabs = d.Lod == 2 ? 1 : d.Fine ? 12 : 6;
				for (int k = 0; k < slabs; k++)
				{
					if (old && d.Lod < 2 && d.Rand(k, 1) < 0.12f)
					{
						continue;
					}
					float w = SlabFenceModule / slabs;
					float top = d.Lod == 2 ? h : h * d.Range(k, 2, old ? 0.8f : 0.95f, 1.03f);
					d.Add(StructureShapes.Block(new Vector3(-half + w * (k + 0.5f), (top - 0.2f) * 0.5f, 0f), new Vector3(w - 0.02f, top + 0.2f, 0.05f),
						Quaternion.Euler(0f, 0f, old ? d.Range(k, 3, -3f, 3f) : 0f), StructureMaterial.Timber, 10 + k)).Grain = Vector3.up;
				}
			}
		}

		/// <summary>
		/// A field gateway three metres wide: a hinge post and a latch post (v0 timber, v1 stone piers), the gate of five bars and a
		/// brace standing open into the field on its hinge, so the way through is clear.
		/// </summary>
		private static void FieldGate(StructureDraft d)
		{
			float half = GateModule * 0.5f;
			bool stone = d.Variant == 1;
			for (int s = -1; s <= 1; s += 2)
			{
				if (stone)
				{
					d.Add(StructureShapes.ChamferBox(new Vector3(s * (half - 0.25f), 0.55f, 0f), new Vector3(0.5f, 1.7f, 0.5f), d.Bevel(0.03f), StructureMaterial.Stone, 1 + s));
					if (d.Mid)
					{
						d.Add(StructureShapes.ChamferBox(new Vector3(s * (half - 0.25f), 1.46f, 0f), new Vector3(0.58f, 0.12f, 0.58f), d.Bevel(0.03f), StructureMaterial.Stone, 5 + s));
					}
				}
				else
				{
					using (d.Builds(FenceRoles))
					{
						d.Add(StructureShapes.Box(new Vector3(s * (half - 0.1f), 0.5f, 0f), new Vector3(0.2f, 1.8f, 0.2f), StructureMaterial.Timber, 1 + s)).Grain = Vector3.up;
					}
				}
			}
			// The gate, hung on the hinge post (−x) and swung open 80° into the field (+z): bars along its own length.
			float hingeX = -half + (stone ? 0.55f : 0.25f);
			float length = GateModule - (stone ? 1.15f : 0.55f);
			Quaternion open = Quaternion.Euler(0f, -80f, 0f);
			var hinge = new Vector3(hingeX, 0f, 0.15f);
			using (d.Builds(FenceRoles))
			{
				int bars = d.Fine ? 5 : d.Mid ? 3 : 2;
				for (int b = 0; b < bars; b++)
				{
					float y = 0.25f + 0.85f * b / Mathf.Max(1, bars - 1);
					d.Add(StructureShapes.Beam(hinge + open * new Vector3(0f, y, 0f), hinge + open * new Vector3(length, y, 0f), 0.07f, 0.04f, StructureMaterial.Timber, 20 + b));
				}
				d.Add(StructureShapes.Beam(hinge + open * new Vector3(0.02f, 0.15f, 0f), hinge + open * new Vector3(0.02f, 1.2f, 0f), 0.1f, 0.06f, StructureMaterial.Timber, 30));
				d.Add(StructureShapes.Beam(hinge + open * new Vector3(length, 0.15f, 0f), hinge + open * new Vector3(length, 1.15f, 0f), 0.08f, 0.05f, StructureMaterial.Timber, 31));
				if (d.Mid)
				{
					d.Add(StructureShapes.Beam(hinge + open * new Vector3(0.05f, 0.25f, 0.02f), hinge + open * new Vector3(length * 0.7f, 1.1f, 0.02f), 0.06f, 0.035f, StructureMaterial.Timber, 32));
				}
			}
		}

		// ── Channels ──────────────────────────────────────────────────

		/// <summary>
		/// A sluice across a channel running along z (1.2 m wide as built, its banks at y = 0): v0 two grooved timber posts, the board
		/// between them and a head beam; v1 stone cheeks on a stone sill with the board between (a channel's head, a partition).
		/// </summary>
		private static void ChannelSluice(StructureDraft d)
		{
			float half = ChannelBuiltWidth * 0.5f;
			bool stone = d.Variant == 1;
			for (int s = -1; s <= 1; s += 2)
			{
				if (stone)
				{
					d.Add(StructureShapes.ChamferBox(new Vector3(s * (half + 0.2f), 0f, 0f), new Vector3(0.4f, 1.3f, 0.7f), d.Bevel(0.03f), StructureMaterial.Stone, 1 + s));
				}
				else
				{
					using (d.Builds(FenceRoles))
					{
						d.Add(StructureShapes.Box(new Vector3(s * (half + 0.08f), 0.05f, 0f), new Vector3(0.16f, 1.4f, 0.16f), StructureMaterial.Timber, 1 + s)).Grain = Vector3.up;
					}
				}
			}
			if (stone)
			{
				d.Add(StructureShapes.Box(new Vector3(0f, -0.62f, 0f), new Vector3(ChannelBuiltWidth + 0.8f, 0.16f, 0.8f), StructureMaterial.Stone, 5));
			}
			using (d.Builds(FenceRoles))
			{
				d.Add(StructureShapes.Box(new Vector3(0f, -0.25f, 0f), new Vector3(ChannelBuiltWidth + 0.04f, 0.6f, 0.06f), StructureMaterial.Timber, 10)).Grain = Vector3.right;
				d.Add(StructureShapes.Beam(new Vector3(-half - 0.2f, 0.62f, 0f), new Vector3(half + 0.2f, 0.62f, 0f), 0.14f, 0.12f, StructureMaterial.Timber, 11));
				if (d.Mid)
				{
					// The board's lifting handle through the beam.
					d.Add(StructureShapes.Rod(new Vector3(0f, 0.05f, 0f), new Vector3(0f, 0.85f, 0f), 0.025f, d.Seg(5, 3), StructureMaterial.Timber, 12, StructureRole.Detail));
				}
			}
		}

		/// <summary>
		/// A flume module along z, its rim at y = 0: a plank floor and sides holding the water (a dark film a hand under the rim, as the
		/// kit's troughs hold theirs), cross-ties over it; v1 deeper, its sides braced by brackets.
		/// </summary>
		private static void ChannelFlume(StructureDraft d)
		{
			const float half = FlumeModule * 0.5f, inner = 0.6f;
			float depth = d.Variant == 1 ? 0.55f : 0.42f;
			using (d.Builds(FenceRoles))
			{
				d.Add(StructureShapes.Box(new Vector3(0f, -depth - 0.03f, 0f), new Vector3(inner + 0.12f, 0.06f, FlumeModule), StructureMaterial.Timber, 1)).Grain = Vector3.forward;
				for (int s = -1; s <= 1; s += 2)
				{
					d.Add(StructureShapes.Box(new Vector3(s * (inner * 0.5f + 0.03f), -depth * 0.5f, 0f), new Vector3(0.06f, depth, FlumeModule), StructureMaterial.Timber, 2 + s)).Grain = Vector3.forward;
					if (d.Variant == 1 && d.Mid)
					{
						for (int k = 0; k < 2; k++)
						{
							float z = -half + 0.5f + k * FlumeModule * 0.5f;
							d.Add(StructureShapes.Beam(new Vector3(s * (inner * 0.5f + 0.06f), -0.05f, z), new Vector3(s * 0.15f, -depth - 0.12f, z), 0.06f, 0.06f, StructureMaterial.Timber, 10 + k * 2 + (s > 0 ? 1 : 0)));
						}
					}
				}
				if (d.Mid)
				{
					for (int k = 0; k < 4; k++)
					{
						d.Add(StructureShapes.Box(new Vector3(0f, 0.025f, -half + 0.5f + k), new Vector3(inner + 0.2f, 0.05f, 0.07f), StructureMaterial.Timber, 20 + k, StructureRole.Detail));
					}
				}
			}
			d.Add(StructureShapes.Box(new Vector3(0f, -FlumeWater, 0f), new Vector3(inner, 0.02f, FlumeModule - 0.02f), StructureMaterial.Ash, 30, StructureRole.Detail));
		}

		/// <summary>
		/// Where a way crosses a channel running along z: v0 three planks across x on two sleepers, flush with the banks (a
		/// footbridge); v1 the stone headwalls of a culvert either side of a road 4 m wide (scale z to the road), the channel under it.
		/// </summary>
		private static void ChannelBridge(StructureDraft d)
		{
			if (d.Variant == 0)
			{
				using (d.Builds(DeckRoles))
				{
					for (int k = 0; k < 3; k++)
					{
						d.Add(StructureShapes.Box(new Vector3(0f, 0.04f, -0.42f + 0.42f * k), new Vector3(ChannelBuiltWidth + 1.2f, 0.06f, 0.38f), StructureMaterial.Timber, 1 + k)).Grain = Vector3.right;
					}
				}
				using (d.Builds(FenceRoles))
				{
					for (int s = -1; s <= 1; s += 2)
					{
						d.Add(StructureShapes.Box(new Vector3(s * (ChannelBuiltWidth * 0.5f + 0.35f), -0.06f, 0f), new Vector3(0.2f, 0.14f, 1.4f), StructureMaterial.Timber, 5 + s)).Grain = Vector3.forward;
					}
				}
				return;
			}
			for (int s = -1; s <= 1; s += 2)
			{
				d.Add(StructureShapes.ChamferBox(new Vector3(0f, -0.15f, s * 2.15f), new Vector3(ChannelBuiltWidth + 1.4f, 0.75f, 0.3f), d.Bevel(0.03f), StructureMaterial.Stone, 1 + s));
				d.Add(StructureShapes.Box(new Vector3(0f, -0.32f, s * 2.31f), new Vector3(ChannelBuiltWidth * 0.8f, 0.4f, 0.04f), StructureMaterial.Ash, 5 + s, StructureRole.Detail));
			}
		}
	}
}
#endif
