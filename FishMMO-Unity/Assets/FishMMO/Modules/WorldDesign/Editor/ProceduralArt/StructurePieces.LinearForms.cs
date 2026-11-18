#if UNITY_EDITOR
using System.Collections.Generic;
using FishMMO.Shared.Biomes;
using UnityEngine;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>
	/// The linear forms the later drafts ask for (2026-10-26, phase I2f-b): a frontier's brick barrier and its forts, a snow-block
	/// windbreak, a hand rope up a steep way, a palm-rib sand screen, a fir-bough break, marker wands, posts, poles and cairns set
	/// along a way, a bisse's bracketed flume and its water-hammer, a great canal's river weir, a qanat's shaft mouths and its gallery
	/// mouth. Registered after the I2f pieces (<see cref="AddLinearForms"/>), each in its own frame as those are.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>The record.</b> Fired-brick frontier walls with a ditch before them and square forts behind them run for days across the
	/// steppe's edge (the "Red Snake", fifth and sixth centuries); snow-block windbreaks and fir-bough breaks shelter the northern camps;
	/// hand ropes on iron-shod stakes climb the pilgrims' steep paths; palm-rib (jarid) screens hold the dunes off the oasis gardens;
	/// wand lines, carved hitching posts, clan poles and cairns mark the ways over the ice, the snow and the lava; the Valais bisses hang
	/// their flumes on iron brackets across the cliffs with a hammer knocking at the head while the water runs; the canal builders took
	/// their great canals off the rivers at brush-and-boulder weirs; the qanat's shafts dot the desert over its gallery to where it
	/// daylights at an arched mouth.
	/// </para>
	/// <para>
	/// <b>Frames.</b> Walls and fences run along x one module long, foot at y = 0, as the I2f pieces. A marker stands at the origin.
	/// A channel's pieces run along z: the bracketed flume is a deck piece (y = 0 its rim, its cliff on −x), the hammer and the gallery
	/// mouth stand across the channel at z = 0 (y = 0 its banks; the gallery's tunnel runs back into the slope toward −z, its mouth
	/// opening toward +z), the weir lies along x across a river (y = 0 the water). A shaft mouth and a fort stand at the origin on the
	/// ground.
	/// </para>
	/// </remarks>
	public static partial class StructurePieces
	{
		/// <summary>The module lengths along x of the new linear pieces, metres.</summary>
		public const float BrickBarrierModule = 4f, SnowWallModule = 3f, HandRopeModule = 5f, PalmRibModule = 3f, BoughBreakModule = 3f, WeirModule = 6f;

		/// <summary>The heights the new linear pieces are built at, metres (a style's own height scales them in y).</summary>
		public const float BrickBarrierHeight = 3.5f, SnowWallHeight = 1.7f, HandRopeHeight = 1.05f, PalmRibHeight = 2f, BoughBreakHeight = 1.5f;

		/// <summary>A brick fort's side, metres (square), and how far inside its barrier's line its centre stands.</summary>
		public const float FortSide = 22f, FortInset = 12.2f;

		/// <summary>A qanat gallery mouth's tunnel depth back into the slope (−z), its inner half width, and its floor under the banks, metres.</summary>
		public const float GalleryDepth = 4.5f, GalleryHalfWidth = 1.5f, GalleryLedge = 0.12f;

		/// <summary>How far down the channel (+z) a gallery mouth's cutting is revetted in stone, metres.</summary>
		public const float GalleryRevetment = 3.5f;

		private static void AddLinearForms(List<StructurePiece> list)
		{
			Linear(list, "LinearBrickBarrier", StructureStyle.Stone, StructureSize.Medium, BrickBarrierModule,
				"Four metres of a frontier's fired-brick barrier along x, 3.5 m: v0 standing, its crown broken into stubs; v1 slumped to a ruined core in its own rubble.",
				BrickBarrier);
			Linear(list, "LinearSnowWall", StructureStyle.Timber, StructureSize.Medium, SnowWallModule,
				"Three metres of a snow-block windbreak along x, 1.7 m: v0 wind-slab blocks in staggered courses; v1 drifted over on one face, its top course fallen.",
				SnowWall);
			Linear(list, "LinearHandRope", StructureStyle.Timber, StructureSize.Small, HandRopeModule,
				"Five metres of hand rope along x: an iron-shod ash stake at its start, a hemp rope sagging to the next; v1 older, the rope slacker and wrapped at the stake.",
				HandRope);
			Linear(list, "LinearPalmRib", StructureStyle.Timber, StructureSize.Medium, PalmRibModule,
				"Three metres of palm-rib screen along x, 2 m: jarid ribs set upright close on palm-trunk rails between posts; v1 weathered, ribs gone and leaning, sand drifted at its foot.",
				PalmRibScreen);
			Linear(list, "LinearBoughBreak", StructureStyle.Timber, StructureSize.Medium, BoughBreakModule,
				"Three metres of fir-bough windbreak along x, 1.5 m: cut boughs leant against stakes and a rail; v1 lower and older, boughs fallen.",
				BoughBreak);
			StructurePiece fort = Piece(list, "LinearBrickFort", StructureStyle.Stone, StructureSize.Building,
				"A frontier's square brick fort, 22 m, centred on the origin, its gate toward −z (laid inside its barrier): v0 curtain walls and corner towers ruined to their stumps; v1 further gone, breached.",
				BrickFort, "linear");
			fort.Anchor = StructureAnchor.Ground;
			foreach ((string id, string text, System.Action<StructureDraft> builder) in new (string, string, System.Action<StructureDraft>)[]
			{
				("MarkerWand", "A wand line's birch wand at the origin, a saffron rag tied near its top: v0 the wand; v1 set in a small cairn (a run's end).", MarkerWand),
				("MarkerPost", "A carved larch hitching post at the origin, ring-banded, a white horsehair tassel: v0 plain; v1 with a fat-lamp hook.", MarkerPost),
				("MarkerPole", "A spruce clan pole at the origin, a sign cut in it and a strip of red cloth: v0 plain; v1 with a bark-torch bracket.", MarkerPole),
				("MarkerCairn", "A waymarking cairn at the origin: v0 waist-high of platy stones capped by a bronze token; v1 a rounded heap of lava stones.", MarkerCairn),
			})
			{
				Piece(list, id, StructureStyle.Timber, StructureSize.Small, text, builder, "linear").Anchor = StructureAnchor.Ground;
			}
			StructurePiece bracket = Piece(list, "ChannelBracketFlume", StructureStyle.Timber, StructureSize.Medium,
				"Four metres of a bisse's flume along z hung on brackets from a cliff on −x, its rim at y = 0, a plank walkway beside it on +x: v0 iron brackets and a rope rail; v1 deeper, on timber brackets.",
				BracketFlume, "linear");
			bracket.Anchor = StructureAnchor.Deck;
			bracket.ModuleLength = FlumeModule;
			Piece(list, "ChannelHammer", StructureStyle.Timber, StructureSize.Small,
				"A bisse's water-hammer across a channel running along z (its banks at y = 0): a paddle wheel on an axle between two posts, a cam lifting a hammer onto a board on +x; v1 under a little board hood.",
				WaterHammer, "linear").Anchor = StructureAnchor.Ground;
			Linear(list, "ChannelWeir", StructureStyle.Timber, StructureSize.Medium, WeirModule,
				"Six metres of a brush-and-boulder river weir along x, the water at y = 0: boulders in a row, brush fascines bound behind them, stakes driven between; v1 two rows, heavier.",
				RiverWeir).Anchor = StructureAnchor.Ground;
			Piece(list, "QanatShaft", StructureStyle.Stone, StructureSize.Small,
				"A qanat's shaft mouth at the origin: a ring of spoil 3.6 m across round a stone-lined mouth over the dark of the shaft; v1 with a timber windlass over it.",
				QanatShaft, "linear").Anchor = StructureAnchor.Ground;
			Piece(list, "QanatGallery", StructureStyle.Stone, StructureSize.Medium,
				"A qanat's gallery mouth across its channel running along z (banks at y = 0): an arched stone mouth opening toward +z, a vaulted gallery 4.5 m back into the slope with a ledge either side of the water, walkable, earth heaped over it; v1 its arch of rougher stones.",
				QanatGallery, "linear").Anchor = StructureAnchor.Ground;
		}

		// ── Walls ─────────────────────────────────────────────────────

		/// <summary>A fired-brick barrier: a battered footing, the wall 1.4 m thick, its crown broken into stubs (v0); v1 a ruined core 2 m in its own rubble.</summary>
		private static void BrickBarrier(StructureDraft d)
		{
			float half = BrickBarrierModule * 0.5f;
			bool ruin = d.Variant == 1;
			float body = ruin ? 1.9f : 2.9f;
			using (d.Builds(FenceRoles))
			{
				d.Add(StructureShapes.Box(new Vector3(0f, -0.3f + 0.35f, 0f), new Vector3(BrickBarrierModule, 0.7f, 1.9f), StructureMaterial.Stone, 1));
				d.Add(StructureShapes.Box(new Vector3(0f, 0.4f + (body - 0.4f) * 0.5f, 0f), new Vector3(BrickBarrierModule, body - 0.4f, 1.4f), StructureMaterial.Stone, 2));
				if (d.Lod < 2)
				{
					// The crown: stubs of the broken top, each its own height.
					int stubs = d.Fine ? 5 : 3;
					for (int k = 0; k < stubs; k++)
					{
						float w = BrickBarrierModule / stubs;
						float top = ruin ? d.Range(k, 1, 0.05f, 0.35f) : d.Range(k, 1, 0.15f, BrickBarrierHeight - body);
						if (top < 0.1f)
						{
							continue;
						}
						d.Add(StructureShapes.Block(new Vector3(-half + w * (k + 0.5f), body + top * 0.5f - 0.02f, d.Range(k, 2, -0.12f, 0.12f)),
							new Vector3(w * d.Range(k, 3, 0.7f, 0.98f), top + 0.04f, 1.4f * d.Range(k, 4, 0.75f, 1f)), Quaternion.Euler(0f, 0f, d.Range(k, 5, -3f, 3f)),
							StructureMaterial.Stone, 10 + k));
					}
				}
			}
			if (ruin && d.Lod < 2)
			{
				// The fallen brick heaped against both faces.
				foreach (int side in new[] { -1, 1 })
				{
					var rings = new List<Vector3[]>();
					foreach (float x in new[] { -half, half })
					{
						float zIn = side * 0.7f, zOut = side * 2.2f;
						// Counter-clockwise looking back along x: from the far foot over the slope's top to the wall's face.
						rings.Add(side > 0
							? new[] { new Vector3(x, -0.25f, zOut), new Vector3(x, 1.1f, zIn + side * 0.1f), new Vector3(x, -0.25f, zIn) }
							: new[] { new Vector3(x, -0.25f, zIn), new Vector3(x, 1.1f, zIn + side * 0.1f), new Vector3(x, -0.25f, zOut) });
					}
					d.Add(StructureShapes.Loft(rings, StructureMaterial.Stone, 30 + side, StructureRole.Rubble));
				}
			}
		}

		/// <summary>A square brick fort: four curtains on a footing, a tower at each corner, a gate gap toward −z; v1 lower and breached.</summary>
		private static void BrickFort(StructureDraft d)
		{
			float half = FortSide * 0.5f;
			bool gone = d.Variant == 1;
			const float thick = 1.3f, tower = 3.8f;
			using (d.Builds(FenceRoles))
			{
				for (int side = 0; side < 4; side++)
				{
					// Each curtain between its corner towers, along x for ±z and along z for ±x.
					bool alongX = side < 2;
					float sign = side % 2 == 0 ? 1f : -1f;
					float length = FortSide - tower;
					int pieces = side == 1 ? 2 : (d.Lod < 2 && gone ? 2 : 1);
					float gap = side == 1 ? 3.4f : gone ? 4f : 0f;
					float run = (length - gap) / pieces;
					for (int k = 0; k < pieces; k++)
					{
						float along = -length * 0.5f + run * (k + 0.5f) + (k > 0 ? gap : 0f);
						float h = (gone ? 1.8f : 3f) * d.Range(side * 8 + k, 1, 0.75f, 1f);
						Vector3 centre = alongX ? new Vector3(along, h * 0.5f - 0.3f, sign * (half - thick * 0.5f)) : new Vector3(sign * (half - thick * 0.5f), h * 0.5f - 0.3f, along);
						Vector3 size = alongX ? new Vector3(run, h + 0.6f, thick) : new Vector3(thick, h + 0.6f, run);
						d.Add(StructureShapes.Box(centre, size, StructureMaterial.Stone, 1 + side * 8 + k));
					}
				}
				for (int c = 0; c < 4; c++)
				{
					float x = (c % 2 == 0 ? 1f : -1f) * (half - tower * 0.5f), z = (c < 2 ? 1f : -1f) * (half - tower * 0.5f);
					float h = (gone ? 2.4f : 4.2f) * d.Range(c, 2, 0.8f, 1f);
					d.Add(StructureShapes.Box(new Vector3(x, h * 0.5f - 0.3f, z), new Vector3(tower, h + 0.6f, tower), StructureMaterial.Stone, 40 + c));
				}
			}
			if (d.Lod < 2)
			{
				int heaps = d.Fine ? 6 : 3;
				for (int k = 0; k < heaps; k++)
				{
					float x = d.Range(k, 3, -half + 3f, half - 3f), z = d.Range(k, 4, -half + 3f, half - 3f);
					d.Add(StructureShapes.Block(new Vector3(x, 0.15f, z), new Vector3(d.Range(k, 5, 1.2f, 2.6f), 0.6f, d.Range(k, 6, 1f, 2.2f)),
						Quaternion.Euler(d.Range(k, 7, -6f, 6f), d.Range(k, 8, 0f, 90f), d.Range(k, 9, -6f, 6f)), StructureMaterial.Stone, 60 + k, StructureRole.Rubble));
				}
			}
		}

		/// <summary>A snow-block windbreak: wind-slab blocks in staggered courses, battered a little (v0); v1 drifted over its −z face, its top course fallen.</summary>
		private static void SnowWall(StructureDraft d)
		{
			float half = SnowWallModule * 0.5f;
			bool drifted = d.Variant == 1;
			int courses = drifted ? 3 : 4;
			float courseH = SnowWallHeight / 4f;
			using (d.Builds(BiomeStructureRole.Wall))
			{
				// The core, so no joint shows daylight.
				var rings = new List<Vector3[]>();
				float top = courses * courseH - 0.05f;
				foreach (float x in new[] { -half, half })
				{
					rings.Add(new[] { new Vector3(x, -0.25f, 0.3f), new Vector3(x, top, 0.2f), new Vector3(x, top, -0.2f), new Vector3(x, -0.25f, -0.3f) });
				}
				d.Add(StructureShapes.Loft(rings, StructureMaterial.Plaster, 1));
				if (d.Lod < 2)
				{
					int per = d.Fine ? 4 : 3;
					for (int c = 0; c < courses; c++)
					{
						float y = courseH * (c + 0.5f);
						float face = Mathf.Lerp(0.3f, 0.2f, y / SnowWallHeight);
						float stagger = (c % 2) * 0.5f * SnowWallModule / per;
						for (int k = 0; k < per; k++)
						{
							int key = 10 + c * 8 + k;
							float x = -half + (k + 0.5f) * SnowWallModule / per + stagger;
							x = x > half ? x - SnowWallModule : x;
							d.Add(StructureShapes.Block(new Vector3(x, y, 0f), new Vector3(SnowWallModule / per * 0.94f, courseH * 0.92f, face * 2f + 0.06f),
								Quaternion.Euler(d.Range(key, 1, -2f, 2f), d.Range(key, 2, -3f, 3f), d.Range(key, 3, -2f, 2f)), StructureMaterial.Plaster, key, StructureRole.Structure,
								d.Bevel(0.04f)));
						}
					}
				}
			}
			if (drifted)
			{
				// A drift banked against the −z face to two thirds of the wall.
				var drift = new List<Vector3[]>();
				foreach (float x in new[] { -half, half })
				{
					drift.Add(new[] { new Vector3(x, -0.25f, -0.25f), new Vector3(x, SnowWallHeight * 0.55f, -0.28f), new Vector3(x, -0.25f, -1.6f) });
				}
				using (d.Builds(BiomeStructureRole.Wall))
				{
					d.Add(StructureShapes.Loft(drift, StructureMaterial.Plaster, 40));
				}
			}
		}

		// ── Fences ────────────────────────────────────────────────────

		/// <summary>A hand rope: an iron-shod ash stake at the module's start, an eye at its top, the rope sagging to the next stake's (v1 slacker, wrapped).</summary>
		private static void HandRope(StructureDraft d)
		{
			float half = HandRopeModule * 0.5f;
			bool old = d.Variant == 1;
			float sag = old ? 0.42f : 0.26f;
			using (d.Builds(FenceRoles))
			{
				HandRopeStake(d, -half, old);
			}
			using (d.Builds(BiomeStructureRole.Cloth))
			{
				int spans = d.Fine ? 8 : d.Mid ? 5 : 2;
				Vector3 Rope(float t) => new Vector3(-half + HandRopeModule * t, HandRopeHeight - 0.04f - sag * 4f * t * (1f - t), 0.04f);
				for (int k = 0; k < spans; k++)
				{
					d.Add(StructureShapes.Rod(Rope(k / (float)spans), Rope((k + 1) / (float)spans), 0.018f, d.Seg(5, 3), StructureMaterial.Canvas, 20 + k));
				}
				if (old && d.Mid)
				{
					d.Add(StructureShapes.Cylinder(0.055f, -0.08f, 0.08f, d.Seg(6, 4), StructureMaterial.Canvas, 40).Transform(Matrix4x4.Translate(new Vector3(-half, HandRopeHeight - 0.12f, 0f))));
				}
			}
		}

		/// <summary>A hand rope's stake at <paramref name="x"/>: ash, leant a little up the line, an iron shoe at its foot and an iron eye at its top.</summary>
		private static void HandRopeStake(StructureDraft d, float x, bool old)
		{
			float lean = old ? 0.12f : 0.05f;
			d.Add(StructureShapes.Rod(new Vector3(x, -0.45f, 0f), new Vector3(x + lean, HandRopeHeight + 0.08f, 0f), 0.055f, d.Seg(6, 4), StructureMaterial.Timber, 1));
			using (d.Builds(BiomeStructureRole.Ironwork))
			{
				d.Add(StructureShapes.Rod(new Vector3(x, -0.3f, 0f), new Vector3(x, -0.58f, 0f), 0.06f, d.Seg(6, 4), StructureMaterial.Iron, 2, StructureRole.Structure, 0.08f));
				if (d.Mid)
				{
					d.Add(StructureShapes.Box(new Vector3(x + lean * 0.95f, HandRopeHeight - 0.04f, 0.045f), new Vector3(0.06f, 0.07f, 0.04f), StructureMaterial.Iron, 3, StructureRole.Detail));
				}
			}
		}

		/// <summary>A palm-rib screen: palm-trunk posts at both ends, two split-trunk rails, jarid ribs upright close along it; v1 weathered, sand drifted at its foot.</summary>
		private static void PalmRibScreen(StructureDraft d)
		{
			float half = PalmRibModule * 0.5f, h = PalmRibHeight;
			bool old = d.Variant == 1;
			using (d.Builds(FenceRoles))
			{
				foreach (float x in new[] { -half + 0.08f, half - 0.08f })
				{
					d.Add(StructureShapes.Rod(new Vector3(x, -0.45f, -0.08f), new Vector3(x, h + 0.05f, -0.08f), 0.09f, d.Seg(7, 4), StructureMaterial.Timber, x < 0f ? 1 : 2));
				}
				foreach (float y in new[] { 0.45f, h - 0.35f })
				{
					d.Add(StructureShapes.Beam(new Vector3(-half, y, -0.08f), new Vector3(half, y, -0.08f), 0.1f, 0.08f, StructureMaterial.Timber, y < 1f ? 3 : 4));
				}
				if (d.Lod == 2)
				{
					d.Add(StructureShapes.Box(new Vector3(0f, h * 0.5f - 0.05f, 0.01f), new Vector3(PalmRibModule - 0.3f, h * 0.95f, 0.04f), StructureMaterial.Timber, 5));
				}
				else
				{
					int ribs = d.Fine ? 26 : 14;
					for (int k = 0; k < ribs; k++)
					{
						if (old && d.Rand(k, 1) < 0.15f)
						{
							continue;
						}
						float x = -half + 0.2f + (PalmRibModule - 0.4f) * (k + 0.5f) / ribs;
						float top = h * d.Range(k, 2, 0.9f, 1.04f);
						float lean = old ? d.Range(k, 3, -0.18f, 0.18f) : d.Range(k, 3, -0.05f, 0.05f);
						d.Add(StructureShapes.Rod(new Vector3(x, -0.2f, 0.01f), new Vector3(x + lean, top, 0.01f + d.Range(k, 4, -0.02f, 0.02f)), d.Range(k, 5, 0.014f, 0.022f), 4,
							StructureMaterial.Timber, 10 + k, StructureRole.Structure, 0.12f));
					}
				}
			}
			if (old && d.Mid)
			{
				var rings = new List<Vector3[]>();
				foreach (float x in new[] { -half, half })
				{
					rings.Add(new[] { new Vector3(x, -0.25f, 1.1f), new Vector3(x, 0.45f, 0.06f), new Vector3(x, -0.25f, 0.04f) });
				}
				using (d.Builds(BiomeStructureRole.Turf))
				{
					d.Add(StructureShapes.Loft(rings, StructureMaterial.Earth, 60));
				}
			}
		}

		/// <summary>A fir-bough windbreak: stakes at its ends and middle, a rail along their tops, boughs leant against it on −z; v1 lower, boughs fallen.</summary>
		private static void BoughBreak(StructureDraft d)
		{
			float half = BoughBreakModule * 0.5f;
			bool old = d.Variant == 1;
			float h = old ? 1.2f : BoughBreakHeight;
			using (d.Builds(FenceRoles))
			{
				foreach (float x in new[] { -half + 0.08f, 0f, half - 0.08f })
				{
					d.Add(StructureShapes.Rod(new Vector3(x, -0.4f, 0f), new Vector3(x, h + 0.1f, 0f), 0.045f, d.Seg(5, 3), StructureMaterial.Timber, 1 + Mathf.RoundToInt(x)));
				}
				d.Add(StructureShapes.Rod(new Vector3(-half, h - 0.05f, 0.02f), new Vector3(half, h - 0.05f, 0.02f), 0.04f, d.Seg(5, 3), StructureMaterial.Timber, 8));
			}
			using (d.Builds(BiomeStructureRole.Roof))
			{
				int boughs = d.Lod == 2 ? 1 : d.Fine ? 11 : 6;
				for (int k = 0; k < boughs; k++)
				{
					if (d.Lod == 2)
					{
						d.Add(StructureShapes.Block(new Vector3(0f, h * 0.48f, -0.16f), new Vector3(BoughBreakModule, h, 0.12f), Quaternion.Euler(-10f, 0f, 0f), StructureMaterial.Thatch, 20));
						break;
					}
					float x = -half + BoughBreakModule * (k + 0.5f) / boughs + d.Range(k, 1, -0.1f, 0.1f);
					float len = h * d.Range(k, 2, 0.95f, 1.15f);
					bool fallen = old && d.Rand(k, 3) < 0.2f;
					Quaternion lean = fallen ? Quaternion.Euler(-70f, d.Range(k, 4, -20f, 20f), 0f) : Quaternion.Euler(-12f - d.Range(k, 5, 0f, 8f), d.Range(k, 4, -8f, 8f), d.Range(k, 6, -10f, 10f));
					Vector3 foot = new Vector3(x, -0.1f, fallen ? -0.4f : -0.3f);
					Vector3 up = lean * Vector3.up;
					d.Add(StructureShapes.Block(foot + up * (len * 0.5f), new Vector3(d.Range(k, 7, 0.45f, 0.7f), len, 0.1f), lean, StructureMaterial.Thatch, 30 + k));
				}
			}
		}

		// ── Markers ───────────────────────────────────────────────────

		/// <summary>A birch wand with a saffron rag tied near its top (v0); v1 set in a small cairn, as at a run's end.</summary>
		private static void MarkerWand(StructureDraft d)
		{
			using (d.Builds(FenceRoles))
			{
				d.Add(StructureShapes.Rod(new Vector3(0f, -0.4f, 0f), new Vector3(0.03f, 2.2f, 0.02f), 0.025f, d.Seg(5, 3), StructureMaterial.Timber, 1));
			}
			using (d.Builds(BiomeStructureRole.Banner))
			{
				d.Add(StructureShapes.Block(new Vector3(0.07f, 1.9f, 0.05f), new Vector3(0.16f, 0.32f, 0.02f), Quaternion.Euler(0f, 40f, 8f), StructureMaterial.Dyed, 2));
			}
			if (d.Variant == 1)
			{
				SmallCairn(d, 0.55f, 0.45f, 10);
			}
		}

		/// <summary>A cairn of a few stones round the origin, <paramref name="radius"/> across its foot, <paramref name="height"/> high.</summary>
		private static void SmallCairn(StructureDraft d, float radius, float height, int key)
		{
			int stones = d.Fine ? 7 : d.Mid ? 4 : 1;
			using (d.Builds(PlinthRoles))
			{
				if (stones == 1)
				{
					d.Add(StructureShapes.Block(new Vector3(0f, height * 0.35f, 0f), new Vector3(radius * 1.6f, height * 0.9f, radius * 1.5f), Quaternion.Euler(0f, 20f, 0f),
						StructureMaterial.Fieldstone, key));
					return;
				}
				for (int k = 0; k < stones; k++)
				{
					float a = k * 2.4f;
					float r = k == stones - 1 ? 0f : radius * d.Range(key + k, 1, 0.45f, 0.7f);
					float y = k == stones - 1 ? height * 0.7f : height * 0.2f;
					float s = d.Range(key + k, 2, 0.24f, 0.34f);
					d.Add(StructureShapes.Block(new Vector3(Mathf.Cos(a) * r, y, Mathf.Sin(a) * r), new Vector3(s * 1.3f, s, s * 1.1f),
						Quaternion.Euler(d.Range(key + k, 3, -15f, 15f), d.Range(key + k, 4, 0f, 180f), d.Range(key + k, 5, -15f, 15f)), StructureMaterial.Fieldstone, key + k,
						StructureRole.Structure, d.Bevel(0.03f)));
				}
			}
		}

		/// <summary>A carved larch hitching post, ring-banded, a white horsehair tassel tied below its head (v0); v1 with an iron fat-lamp hook.</summary>
		private static void MarkerPost(StructureDraft d)
		{
			const float top = 2.4f;
			using (d.Builds(FenceRoles))
			{
				d.Add(StructureShapes.Cylinder(0.11f, -0.5f, top, d.Seg(8, 5), StructureMaterial.Timber, 1, true));
				if (d.Lod < 2)
				{
					foreach (float y in d.Fine ? new[] { 0.9f, 1.4f, 1.85f } : new[] { 1.4f })
					{
						d.Add(StructureShapes.Cylinder(0.135f, y - 0.05f, y + 0.05f, d.Seg(8, 5), StructureMaterial.Timber, 10 + Mathf.RoundToInt(y * 10f), false, StructureRole.Detail));
					}
				}
				// The head: a cup on a neck, as the posts are crowned.
				d.Add(StructureShapes.Lathe(new[] { new Vector2(0.08f, top), new Vector2(0.06f, top + 0.12f), new Vector2(0.16f, top + 0.26f), new Vector2(0.16f, top + 0.3f) },
					d.Seg(8, 5), StructureMaterial.Timber, 4));
			}
			using (d.Builds(BiomeStructureRole.Cloth))
			{
				int hairs = d.Fine ? 4 : d.Mid ? 2 : 1;
				for (int k = 0; k < hairs; k++)
				{
					float a = k * 1.7f;
					var at = new Vector3(Mathf.Cos(a) * 0.12f, 2.05f, Mathf.Sin(a) * 0.12f);
					d.Add(StructureShapes.Rod(at, at + new Vector3(Mathf.Cos(a) * 0.05f, -0.45f, Mathf.Sin(a) * 0.05f), 0.02f, 4, StructureMaterial.Canvas, 20 + k));
				}
			}
			if (d.Variant == 1)
			{
				using (d.Builds(BiomeStructureRole.Ironwork))
				{
					d.Add(StructureShapes.Rod(new Vector3(0.1f, 1.75f, 0f), new Vector3(0.42f, 1.75f, 0f), 0.015f, 4, StructureMaterial.Iron, 30));
					d.Add(StructureShapes.Rod(new Vector3(0.42f, 1.75f, 0f), new Vector3(0.42f, 1.58f, 0f), 0.012f, 4, StructureMaterial.Iron, 31));
					d.Add(StructureShapes.Lathe(new[] { new Vector2(0.02f, 1.5f), new Vector2(0.09f, 1.53f), new Vector2(0.1f, 1.58f) }, d.Seg(8, 5), StructureMaterial.Iron, 32)
						.Transform(Matrix4x4.Translate(new Vector3(0.42f, 0f, 0f))));
				}
			}
		}

		/// <summary>A spruce clan pole, its sign cut on the face, a strip of red cloth tied high (v0); v1 with an iron bark-torch bracket.</summary>
		private static void MarkerPole(StructureDraft d)
		{
			const float top = 3f;
			using (d.Builds(FenceRoles))
			{
				d.Add(StructureShapes.Rod(new Vector3(0f, -0.5f, 0f), new Vector3(0.02f, top, 0.01f), 0.075f, d.Seg(7, 4), StructureMaterial.Timber, 1, StructureRole.Structure, 0.15f));
				if (d.Mid)
				{
					// The clan sign: two axe-cut blazes crossing on the face toward +z.
					d.Add(StructureShapes.Block(new Vector3(0.01f, 1.55f, 0.065f), new Vector3(0.05f, 0.34f, 0.03f), Quaternion.Euler(0f, 0f, 28f), StructureMaterial.Timber, 2, StructureRole.Detail));
					d.Add(StructureShapes.Block(new Vector3(0.01f, 1.55f, 0.065f), new Vector3(0.05f, 0.34f, 0.03f), Quaternion.Euler(0f, 0f, -28f), StructureMaterial.Timber, 3, StructureRole.Detail));
				}
			}
			using (d.Builds(BiomeStructureRole.Banner))
			{
				d.Add(StructureShapes.Block(new Vector3(0.09f, 2.35f, 0.02f), new Vector3(0.07f, 0.65f, 0.015f), Quaternion.Euler(0f, 25f, 6f), StructureMaterial.Dyed, 4));
			}
			if (d.Variant == 1)
			{
				using (d.Builds(BiomeStructureRole.Ironwork))
				{
					d.Add(StructureShapes.Rod(new Vector3(0.06f, 2f, 0f), new Vector3(0.3f, 2f, 0f), 0.014f, 4, StructureMaterial.Iron, 10));
					d.Add(StructureShapes.Cylinder(0.05f, -0.06f, 0.06f, d.Seg(6, 4), StructureMaterial.Iron, 11).Transform(Matrix4x4.Translate(new Vector3(0.32f, 2f, 0f))));
				}
				using (d.Builds(FenceRoles))
				{
					d.Add(StructureShapes.Rod(new Vector3(0.32f, 1.78f, 0f), new Vector3(0.33f, 2.32f, 0f), 0.03f, 5, StructureMaterial.Timber, 12));
				}
				using (d.Builds(BiomeStructureRole.Roof))
				{
					// The torch's head of rolled birch bark (unlit by day; a way light is NEW-LIGHTS' to add).
					d.Add(StructureShapes.Cylinder(0.055f, 2.25f, 2.45f, d.Seg(6, 4), StructureMaterial.Thatch, 13).Transform(Matrix4x4.Translate(new Vector3(0.33f, 0f, 0f))));
				}
			}
		}

		/// <summary>A waymarking cairn: v0 waist-high, platy stones stacked and diminishing, a bronze token on its cap; v1 a rounded heap of lava stones.</summary>
		private static void MarkerCairn(StructureDraft d)
		{
			if (d.Variant == 0)
			{
				int slabs = d.Fine ? 8 : d.Mid ? 5 : 2;
				using (d.Builds(PlinthRoles))
				{
					for (int k = 0; k < slabs; k++)
					{
						float t = k / (float)Mathf.Max(1, slabs - 1);
						float w = Mathf.Lerp(0.85f, 0.35f, t) * d.Range(k, 1, 0.9f, 1.1f);
						float th = 1f / slabs;
						d.Add(StructureShapes.Block(new Vector3(d.Range(k, 2, -0.04f, 0.04f), -0.1f + th * (k + 0.5f), d.Range(k, 3, -0.04f, 0.04f)), new Vector3(w, th * 1.05f, w * d.Range(k, 4, 0.8f, 1f)),
							Quaternion.Euler(d.Range(k, 5, -4f, 4f), d.Range(k, 6, 0f, 90f), d.Range(k, 7, -4f, 4f)), StructureMaterial.Fieldstone, 1 + k, StructureRole.Structure, d.Bevel(0.02f)));
					}
				}
				using (d.Builds(BiomeStructureRole.Ironwork))
				{
					d.Add(StructureShapes.Cylinder(0.09f, 0.9f, 0.94f, d.Seg(8, 5), StructureMaterial.Iron, 20, false, StructureRole.Detail));
				}
				return;
			}
			int stones = d.Fine ? 8 : d.Mid ? 5 : 1;
			using (d.Builds(PlinthRoles))
			{
				d.Add(StructureShapes.Sphere(new Vector3(0f, 0.25f, 0f), new Vector3(0.62f, 0.75f, 0.58f), d.Seg(8, 5), d.Fine ? 5 : 3, StructureMaterial.Fieldstone, 1));
				for (int k = 1; k < stones; k++)
				{
					float a = k * 2.1f, y = d.Range(k, 1, 0.1f, 0.85f);
					float r = 0.55f * Mathf.Sqrt(Mathf.Max(0f, 1f - (y - 0.25f) * (y - 0.25f) / 0.6f));
					d.Add(StructureShapes.Block(new Vector3(Mathf.Cos(a) * r, y, Mathf.Sin(a) * r), Vector3.one * d.Range(k, 2, 0.2f, 0.32f),
						Quaternion.Euler(d.Range(k, 3, 0f, 90f), d.Range(k, 4, 0f, 90f), 0f), StructureMaterial.Fieldstone, 10 + k, StructureRole.Structure, d.Bevel(0.05f)));
				}
			}
		}

		// ── Channels ──────────────────────────────────────────────────

		/// <summary>
		/// A bisse's flume along z, its rim at y = 0, hung from a cliff on −x: the trough holding its dark water, a plank walkway beside
		/// it on +x, brackets from the cliff under both (v0 iron, a rope rail on the walkway's edge; v1 deeper, timber brackets).
		/// </summary>
		private static void BracketFlume(StructureDraft d)
		{
			const float half = FlumeModule * 0.5f, inner = 0.6f, cliff = -0.75f, walk = 0.78f;
			bool timber = d.Variant == 1;
			float depth = timber ? 0.55f : 0.42f;
			using (d.Builds(FenceRoles))
			{
				d.Add(StructureShapes.Box(new Vector3(0f, -depth - 0.03f, 0f), new Vector3(inner + 0.12f, 0.06f, FlumeModule), StructureMaterial.Timber, 1)).Grain = Vector3.forward;
				for (int s = -1; s <= 1; s += 2)
				{
					d.Add(StructureShapes.Box(new Vector3(s * (inner * 0.5f + 0.03f), -depth * 0.5f, 0f), new Vector3(0.06f, depth, FlumeModule), StructureMaterial.Timber, 2 + s)).Grain = Vector3.forward;
				}
			}
			using (d.Builds(DeckRoles))
			{
				d.Add(StructureShapes.Box(new Vector3(walk, -0.03f, 0f), new Vector3(0.5f, 0.06f, FlumeModule), StructureMaterial.Timber, 5)).Grain = Vector3.forward;
			}
			int brackets = d.Lod == 2 ? 1 : 2;
			for (int k = 0; k < brackets; k++)
			{
				float z = brackets == 1 ? 0f : -half + 1f + k * 2f;
				Vector3 arm0 = new Vector3(cliff, -depth - 0.09f, z), arm1 = new Vector3(walk + 0.22f, -depth - 0.09f, z), foot = new Vector3(cliff, -depth - 0.85f, z);
				if (timber)
				{
					using (d.Builds(FenceRoles))
					{
						d.Add(StructureShapes.Beam(arm0, arm1, 0.08f, 0.08f, StructureMaterial.Timber, 10 + k * 2));
						d.Add(StructureShapes.Beam(foot, Vector3.Lerp(arm0, arm1, 0.6f), 0.07f, 0.07f, StructureMaterial.Timber, 11 + k * 2));
					}
				}
				else
				{
					using (d.Builds(BiomeStructureRole.Ironwork))
					{
						d.Add(StructureShapes.Beam(arm0, arm1, 0.04f, 0.05f, StructureMaterial.Iron, 10 + k * 2));
						d.Add(StructureShapes.Beam(foot, Vector3.Lerp(arm0, arm1, 0.6f), 0.035f, 0.035f, StructureMaterial.Iron, 11 + k * 2));
					}
				}
			}
			if (!timber && d.Mid)
			{
				using (d.Builds(FenceRoles))
				{
					foreach (float z in new[] { -half + 0.2f, half - 0.2f })
					{
						d.Add(StructureShapes.Rod(new Vector3(walk + 0.22f, -0.06f, z), new Vector3(walk + 0.22f, 0.9f, z), 0.025f, 4, StructureMaterial.Timber, 20 + (z > 0f ? 1 : 0)));
					}
				}
				using (d.Builds(BiomeStructureRole.Cloth))
				{
					d.Add(StructureShapes.Rod(new Vector3(walk + 0.22f, 0.85f, -half), new Vector3(walk + 0.22f, 0.8f, half), 0.015f, 4, StructureMaterial.Canvas, 22));
				}
			}
			d.Add(StructureShapes.Box(new Vector3(0f, -FlumeWater, 0f), new Vector3(inner, 0.02f, FlumeModule - 0.02f), StructureMaterial.Ash, 30, StructureRole.Detail));
		}

		/// <summary>
		/// A bisse's water-hammer across a channel along z: two posts either side carrying an axle across x, a paddle wheel on it dipping
		/// into the water, a cam at its +x end under a hammer's arm, the hammer resting on its board; v1 a little board hood over the hammer.
		/// </summary>
		private static void WaterHammer(StructureDraft d)
		{
			const float axle = 0.12f, wheel = 0.42f;
			using (d.Builds(FenceRoles))
			{
				foreach (float x in new[] { -0.85f, 0.85f })
				{
					d.Add(StructureShapes.Box(new Vector3(x, -0.1f, 0f), new Vector3(0.12f, 0.9f, 0.12f), StructureMaterial.Timber, x < 0f ? 1 : 2)).Grain = Vector3.up;
				}
				d.Add(StructureShapes.Rod(new Vector3(-0.92f, axle, 0f), new Vector3(1.35f, axle, 0f), 0.04f, d.Seg(6, 4), StructureMaterial.Timber, 3));
				int paddles = d.Fine ? 8 : d.Mid ? 6 : 4;
				for (int k = 0; k < paddles; k++)
				{
					float a = k * Mathf.PI * 2f / paddles;
					var at = new Vector3(0f, axle + Mathf.Sin(a) * wheel * 0.55f, Mathf.Cos(a) * wheel * 0.55f);
					d.Add(StructureShapes.Block(at, new Vector3(0.5f, 0.04f, wheel * 0.85f), Quaternion.Euler(-a * Mathf.Rad2Deg, 0f, 0f), StructureMaterial.Timber, 10 + k));
				}
				// The cam, the hammer's arm on its pivot post, its head over the board.
				d.Add(StructureShapes.Box(new Vector3(1.22f, axle + 0.09f, 0f), new Vector3(0.08f, 0.1f, 0.06f), StructureMaterial.Timber, 20));
				d.Add(StructureShapes.Box(new Vector3(1.45f, 0.1f, -0.55f), new Vector3(0.1f, 0.75f, 0.1f), StructureMaterial.Timber, 21)).Grain = Vector3.up;
				d.Add(StructureShapes.Beam(new Vector3(1.22f, axle + 0.16f, 0.08f), new Vector3(1.45f, 0.42f, -0.55f), 0.06f, 0.05f, StructureMaterial.Timber, 22));
				d.Add(StructureShapes.Box(new Vector3(1.2f, axle + 0.24f, 0.22f), new Vector3(0.16f, 0.14f, 0.22f), StructureMaterial.Timber, 23));
				d.Add(StructureShapes.Box(new Vector3(1.2f, -0.02f, 0.22f), new Vector3(0.36f, 0.05f, 0.4f), StructureMaterial.Timber, 24)).Grain = Vector3.right;
			}
			if (d.Variant == 1)
			{
				using (d.Builds(BiomeStructureRole.ShingleRoof))
				{
					d.Add(StructureShapes.Block(new Vector3(1.32f, 0.78f, 0.1f), new Vector3(0.6f, 0.04f, 0.5f), Quaternion.Euler(0f, 0f, 22f), StructureMaterial.Timber, 30));
					d.Add(StructureShapes.Block(new Vector3(1.62f, 0.78f, 0.1f), new Vector3(0.6f, 0.04f, 0.5f), Quaternion.Euler(0f, 0f, -22f), StructureMaterial.Timber, 31));
				}
			}
		}

		/// <summary>A brush-and-boulder weir along x across a river (y = 0 its water): boulders in a row, brush fascines behind them, stakes between; v1 two rows.</summary>
		private static void RiverWeir(StructureDraft d)
		{
			float half = WeirModule * 0.5f;
			int rows = d.Variant == 1 ? 2 : 1;
			using (d.Builds(PlinthRoles))
			{
				for (int r = 0; r < rows; r++)
				{
					int stones = d.Fine ? 9 : d.Mid ? 6 : 3;
					for (int k = 0; k < stones; k++)
					{
						int key = 1 + r * 16 + k;
						float s = d.Range(key, 1, 0.6f, 0.85f);
						d.Add(StructureShapes.Block(new Vector3(-half + WeirModule * (k + 0.5f) / stones, -0.35f + s * 0.4f, r * 0.75f), new Vector3(WeirModule / stones * 0.95f, s, s * 0.9f),
							Quaternion.Euler(d.Range(key, 2, -10f, 10f), d.Range(key, 3, -20f, 20f), d.Range(key, 4, -10f, 10f)), StructureMaterial.Fieldstone, key, StructureRole.Structure, d.Bevel(0.06f)));
					}
				}
			}
			using (d.Builds(FenceRoles))
			{
				int fascines = d.Mid ? 3 : 1;
				for (int k = 0; k < fascines; k++)
				{
					d.Add(StructureShapes.Rod(new Vector3(-half, -0.25f + k * 0.18f, -0.55f - k * 0.05f), new Vector3(half, -0.22f + k * 0.18f, -0.55f - k * 0.05f), 0.11f, d.Seg(6, 4), StructureMaterial.Timber, 50 + k));
				}
				int stakes = d.Fine ? 7 : d.Mid ? 4 : 0;
				for (int k = 0; k < stakes; k++)
				{
					float x = -half + 0.4f + (WeirModule - 0.8f) * k / Mathf.Max(1, stakes - 1);
					d.Add(StructureShapes.Rod(new Vector3(x, -0.6f, -0.35f), new Vector3(x + d.Range(k, 5, -0.05f, 0.05f), 0.35f, -0.38f), 0.04f, 4, StructureMaterial.Timber, 60 + k, StructureRole.Structure, 0.08f));
				}
			}
		}

		/// <summary>A qanat's shaft mouth: a ring of spoil round a stone-lined mouth over the shaft's dark; v1 with a windlass over it.</summary>
		private static void QanatShaft(StructureDraft d)
		{
			int seg = d.Seg(12, 6);
			using (d.Builds(BiomeStructureRole.Turf))
			{
				// The spoil ring: an annulus up its outside, over its crest and down into the mouth.
				d.Add(StructureShapes.Lathe(new[] { new Vector2(1.8f, -0.2f), new Vector2(1.25f, 0.42f), new Vector2(0.9f, 0.55f), new Vector2(0.72f, 0.35f), new Vector2(0.72f, -0.2f) },
					seg, StructureMaterial.Earth, 1, true, 0f, true));
			}
			using (d.Builds(PlinthRoles))
			{
				d.Add(StructureShapes.Lathe(new[] { new Vector2(0.72f, 0.2f), new Vector2(0.72f, 0.62f), new Vector2(0.52f, 0.62f), new Vector2(0.52f, -0.1f) }, seg,
					StructureMaterial.Fieldstone, 2, false, 0f, true));
			}
			// The shaft's dark, a hand under the collar.
			d.Add(StructureShapes.Cylinder(0.53f, -0.3f, 0.3f, seg, StructureMaterial.Ash, 3, false, StructureRole.Detail));
			if (d.Variant == 1)
			{
				using (d.Builds(FenceRoles))
				{
					foreach (float x in new[] { -0.95f, 0.95f })
					{
						d.Add(StructureShapes.Box(new Vector3(x, 0.8f, 0f), new Vector3(0.12f, 1.3f, 0.12f), StructureMaterial.Timber, x < 0f ? 10 : 11)).Grain = Vector3.up;
					}
					d.Add(StructureShapes.Rod(new Vector3(-1.05f, 1.32f, 0f), new Vector3(1.25f, 1.32f, 0f), 0.08f, d.Seg(7, 4), StructureMaterial.Timber, 12));
					if (d.Mid)
					{
						d.Add(StructureShapes.Beam(new Vector3(1.2f, 1.32f, 0f), new Vector3(1.2f, 1.02f, 0f), 0.05f, 0.05f, StructureMaterial.Timber, 13));
						d.Add(StructureShapes.Beam(new Vector3(1.2f, 1.02f, 0f), new Vector3(1.38f, 1.02f, 0f), 0.04f, 0.04f, StructureMaterial.Timber, 14));
					}
				}
				using (d.Builds(BiomeStructureRole.Cloth))
				{
					d.Add(StructureShapes.Rod(new Vector3(0f, 1.28f, 0f), new Vector3(0f, 0.25f, 0f), 0.015f, 4, StructureMaterial.Canvas, 15));
				}
			}
		}

		/// <summary>
		/// A qanat's gallery mouth across its channel (along z, banks at y = 0): side walls and a barrel vault running 4.5 m back into the
		/// slope (−z), a ledge either side of the water to walk in on, an arched face toward +z with its spandrels, the dark of the
		/// gallery beyond, earth heaped over the vault; v1 its arch of rougher stones.
		/// </summary>
		private static void QanatGallery(StructureDraft d)
		{
			const float half = GalleryHalfWidth, spring = 1.7f, wall = 0.4f, floor = -GalleryLedge;
			float z0 = -GalleryDepth, zMid = -GalleryDepth * 0.5f;
			bool rough = d.Variant == 1;
			using (d.Builds(BiomeStructureRole.Wall))
			{
				foreach (int s in new[] { -1, 1 })
				{
					d.Add(StructureShapes.Box(new Vector3(s * (half + wall * 0.5f), (spring - 0.8f) * 0.5f, zMid), new Vector3(wall, spring + 0.8f, GalleryDepth), StructureMaterial.Stone, 1 + s));
				}
				// The vault: wedges round a half circle from springing to springing, each the gallery's depth.
				int wedges = d.Fine ? 9 : d.Mid ? 6 : 3;
				float r = half + wall * 0.5f;
				for (int k = 0; k < wedges; k++)
				{
					float a = Mathf.PI * (k + 0.5f) / wedges;
					var at = new Vector3(Mathf.Cos(a) * r, spring + Mathf.Sin(a) * r, zMid);
					float chord = 2f * r * Mathf.Sin(Mathf.PI * 0.5f / wedges) + 0.04f;
					d.Add(StructureShapes.Block(at, new Vector3(chord, wall, GalleryDepth), Quaternion.Euler(0f, 0f, a * Mathf.Rad2Deg - 90f), StructureMaterial.Stone, 10 + k));
				}
				// The face: spandrels either side of the arch up to over its crown, the corners between the arch's back and them filled
				// course by course, a band over the crown, the arch's voussoirs proud of it all.
				float crown = spring + r + wall * 0.5f, outer = r + wall * 0.5f, head = crown + 0.45f;
				foreach (int s in new[] { -1, 1 })
				{
					d.Add(StructureShapes.Box(new Vector3(s * (outer + 0.6f), (head - 0.8f) * 0.5f, -0.2f), new Vector3(1.3f, head + 0.8f, 0.4f), StructureMaterial.Stone, 30 + s));
					int courses = d.Fine ? 8 : d.Mid ? 4 : 2;
					for (int k = 0; k < courses; k++)
					{
						float y0 = spring + outer * k / courses, y1 = spring + outer * (k + 1) / courses;
						float x0 = Mathf.Sqrt(Mathf.Max(0f, outer * outer - (y0 - spring) * (y0 - spring))) - 0.04f;
						if (x0 >= outer)
						{
							continue;
						}
						d.Add(StructureShapes.Box(new Vector3(s * 0.5f * (x0 + outer + 0.05f), 0.5f * (y0 + y1), -0.2f), new Vector3(outer + 0.05f - x0, y1 - y0 + 0.02f, 0.4f),
							StructureMaterial.Stone, 80 + k * 2 + (s > 0 ? 1 : 0)));
					}
				}
				d.Add(StructureShapes.Box(new Vector3(0f, 0.5f * (crown - 0.04f + head), -0.2f), new Vector3(2f * outer + 0.1f, head - crown + 0.04f, 0.4f), StructureMaterial.Stone, 33));
				if (d.Lod < 2)
				{
					int stones = d.Fine ? 9 : 5;
					for (int k = 0; k < stones; k++)
					{
						float a = Mathf.PI * (k + 0.5f) / stones;
						var at = new Vector3(Mathf.Cos(a) * (r + 0.05f), spring + Mathf.Sin(a) * (r + 0.05f), 0.06f);
						float size = rough ? d.Range(k, 1, 0.3f, 0.45f) : 0.34f;
						d.Add(StructureShapes.Block(at, new Vector3(2f * r * Mathf.Sin(Mathf.PI * 0.5f / stones) * 0.92f, size + 0.1f, 0.28f),
							Quaternion.Euler(rough ? d.Range(k, 2, -4f, 4f) : 0f, 0f, a * Mathf.Rad2Deg - 90f), StructureMaterial.Stone, 40 + k, StructureRole.Detail));
					}
				}
			}
			using (d.Builds(FloorRoles))
			{
				foreach (int s in new[] { -1, 1 })
				{
					d.Add(StructureShapes.Box(new Vector3(s * (half - 0.45f), floor - 0.2f, zMid + 0.1f), new Vector3(0.9f, 0.4f, GalleryDepth + 0.2f), StructureMaterial.Stone, 50 + s));
				}
			}
			// The cutting it daylights into, its sides revetted in stone the first few metres down the channel (+z).
			using (d.Builds(BiomeStructureRole.Wall))
			{
				foreach (int s in new[] { -1, 1 })
				{
					d.Add(StructureShapes.Box(new Vector3(s * (half - 0.1f), -0.3f, GalleryRevetment * 0.5f), new Vector3(0.35f, 1.2f, GalleryRevetment), StructureMaterial.Stone, 55 + s));
				}
			}
			// The gallery's dark beyond, where it runs on under the desert.
			d.Add(StructureShapes.Box(new Vector3(0f, spring * 0.5f + 0.2f, z0 + 0.05f), new Vector3(half * 2f, spring + half + wall * 0.5f + 0.6f, 0.1f), StructureMaterial.Ash, 60, StructureRole.Detail));
			if (d.Mid)
			{
				using (d.Builds(BiomeStructureRole.Turf))
				{
					var rings = new List<Vector3[]>();
					foreach (float z in new[] { z0 - 0.4f, -0.35f })
					{
						// Lofted along +z, counter-clockwise looking back along it (−x is then on the right): from the −x foot over the crown.
						rings.Add(new[] { new Vector3(-half - 1.4f, spring + 0.2f, z), new Vector3(0f, spring + half + wall + 0.95f, z), new Vector3(half + 1.4f, spring + 0.2f, z) });
					}
					d.Add(StructureShapes.Loft(rings, StructureMaterial.Earth, 70));
				}
			}
		}
	}
}
#endif
