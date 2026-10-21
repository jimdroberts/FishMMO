#if UNITY_EDITOR
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using FishMMO.Shared.Biomes;
using UnityEditor;
using UnityEngine;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>
	/// The fences round the houses' yards (2026-10-19: "some houses should have fences around them with gates to the roads"): after the
	/// template's features, each yard the layout laid (PointOfInterestLayouts' Yards, carried out by PropsFeature with the house as it
	/// was laid) is fenced round its front garden and the plot behind, a gate in the front fence on the line from the house's door to
	/// the street (in the back fence, behind a shop).
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>What it is built of.</b> The site's biome's own enclosing style where it names one (its site-bounds, field-edge, fold or
	/// lane style, a hedge, wall or fence, in that order of preference) kept to a garden's height and never broken; else by what the
	/// site is built of: field stone in a stone town (a drystone wall or, one yard in three, a low mortared one), wattle hurdles where
	/// it is built of hide or earth (the steppe's and the marsh's), pickets or wattle in a timber village. The gate is the linear
	/// features' field gateway (stone piers in a wall, timber posts in a fence or hedge), its gate swung open into the yard.
	/// </para>
	/// <para>
	/// <b>Where it may not stand.</b> The layout kept every run clear of the streets, the other buildings and plots, the other yards'
	/// fences (a side alongside a neighbour's is that fence, shared) and the boundary, so laying each run module by module
	/// (PointOfInterestLinearFeatures.LayAlong, planned) leaves out only a module over water or pitched too steep (2026-10-19: the
	/// ways' own clearance, wider than the layout's and round a square's ends, cut front fences short); this leaves a gap before any
	/// building's door the run passes. Every run of the site is joined up together (PointOfInterestLinearFeatures.Joins): a gap, a
	/// run's end and a side stopping on a neighbour's fence each end on a post.
	/// </para>
	/// <para>
	/// <b>Its own draws.</b> Every choice is a hash of the site's seed and the yard's index: nothing else at the site moves.
	/// </para>
	/// </remarks>
	[InitializeOnLoad]
	public sealed class PointOfInterestYards : IPointOfInterestSiteBuilder
	{
		static PointOfInterestYards() => PointOfInterestSiteBuilders.Register(new PointOfInterestYards());

		/// <summary>The tallest a yard's fence or wall is built, metres (a hedge a little more): a garden's, not a field's or a castle's.</summary>
		public const float FenceHeight = 1.25f, HedgeHeight = 1.5f;
		/// <summary>The gap left in a run before a building's door, metres either side of the way out, and how near the run passes to count.</summary>
		public const float DoorGapMetres = 1.1f, DoorReachMetres = 1.4f;
		/// <summary>How far in front of a door's leaf its way out is taken, metres.</summary>
		private const float DoorStep = 0.8f;

		/// <summary>One yard as the house was laid: its fence's runs in the world (x, z), where its gate goes, and the house's door.</summary>
		public struct Planned
		{
			public PointOfInterestYardFence[] Fences;
			/// <summary>The gate's middle with no door to aim at (world x, z).</summary>
			public Vector2 GateAt;
			/// <summary>Just outside the house's front door (world x, z), when it has one: the gate is set on the line out from it.</summary>
			public Vector2? Door;
			/// <summary>The house's decay: a fence round an old house is weathered and gapped.</summary>
			public float Decay;
		}

		/// <summary>The yards PropsFeature planned, keyed by the site's shared list of laid props, for this builder after the features.</summary>
		private static readonly ConditionalWeakTable<object, List<Planned>> pending = new ConditionalWeakTable<object, List<Planned>>();

		/// <summary>
		/// Whether a slot's tag lays houses that may keep a yard (pure but for the kit's table): a house or cottage (not a stilt house or a
		/// ruin) a front garden; a shop (its stall-board on the street) the plot behind only; anything else (a church, a temple, a tavern,
		/// a smithy, a stable) none. "=Id" asks the kit's piece's own tags.
		/// </summary>
		public static PointOfInterestYardKind KindFor(string tag)
		{
			if (string.IsNullOrWhiteSpace(tag))
			{
				return PointOfInterestYardKind.None;
			}
			if (tag[0] == '=')
			{
				StructurePiece piece = StructurePieces.Find(tag.Substring(1).Trim());
				return piece != null ? KindFor(piece) : PointOfInterestYardKind.None;
			}
			StructureKitPieceSource.Parse(tag, out List<string> required, out List<string> _);
			return KindOf(required.Contains, required.Count);
		}

		/// <summary>Whether a kit piece is a house that may keep a yard (its own tags, as <see cref="KindFor(string)"/>).</summary>
		public static PointOfInterestYardKind KindFor(StructurePiece piece) => piece != null && piece.Tags != null ? KindOf(piece.HasTag, piece.Tags.Length) : PointOfInterestYardKind.None;

		private static PointOfInterestYardKind KindOf(System.Func<string, bool> has, int count)
		{
			if (count == 0 || has("stilt") || has("ruin") || has("church") || has("temple") || has("tavern") || has("swamp") || has("water"))
			{
				return PointOfInterestYardKind.None;
			}
			if (has("shop"))
			{
				return PointOfInterestYardKind.Behind;
			}
			return has("house") ? PointOfInterestYardKind.Fronted : PointOfInterestYardKind.None;
		}

		/// <summary>Hands this builder a site's yards (PropsFeature, as it lays the houses); with no shared list to key them by, they are fenced at once.</summary>
		public static void Offer(PointOfInterestSiteContext context, List<Planned> yards)
		{
			if (context == null || yards == null || yards.Count == 0)
			{
				return;
			}
			if (context.Laid == null)
			{
				Lay(context, yards);
				return;
			}
			pending.Remove(context.Laid);
			pending.Add(context.Laid, yards);
		}

		public bool Handles(PointOfInterestRecord record) => record != null;

		public void Build(PointOfInterestSiteContext context)
		{
			if (context.Laid == null || !pending.TryGetValue(context.Laid, out List<Planned> yards))
			{
				return;
			}
			pending.Remove(context.Laid);
			Lay(context, yards);
		}

		/// <summary>Fences every planned yard of a site; returns the pieces laid.</summary>
		public static int Lay(PointOfInterestSiteContext context, List<Planned> yards)
		{
			PointOfInterestRecord record = context.Record;
			if (record == null || yards == null)
			{
				return 0;
			}
			BiomeTemplate biome = context.BiomeAt?.Invoke(record.Position.x, record.Position.z);
			BiomeLinearStyle style = StyleFor(biome, context.Style, record.SiteSeed);
			bool stone = style.Kind == BiomeLinearKind.Wall;
			GameObject gate = StructureKit.Prefab("LinearGate", stone ? 1 : 0);
			// The doors the runs keep clear of: every building's at the site, a pace out from its leaf.
			var doors = new List<Vector2>();
			foreach (PointOfInterestSiteContext.PlacedDoor door in context.Doors)
			{
				Vector3 middle = door.Hinge + door.Across * (door.Width * 0.5f) - door.Inward * DoorStep;
				doors.Add(new Vector2(middle.x, middle.z));
			}
			// Every run of every yard joined up together (PointOfInterestLinearFeatures.Joins, 2026-10-19): a yard's corners, its sides
			// stopping on a neighbour's shared fence, the gate's and the doors' gaps each end on a post.
			var joins = new PointOfInterestLinearFeatures.Joins();
			BiomeStructurePalette gatePalette = StructurePieces.LinearPalette(BiomeTemplateArt.StructurePalette(biome), style, "LinearGate");
			int laid = 0, gates = 0;
			for (int y = 0; y < yards.Count; y++)
			{
				Planned yard = yards[y];
				if (yard.Fences == null)
				{
					continue;
				}
				BiomeLinearStyle own = style;
				// An old house's fence is weathered and gapped as the linear features' broken lines are.
				own.Breaks = Mathf.Clamp(yard.Decay * 0.8f - 0.05f, 0f, 0.6f);
				for (int f = 0; f < yard.Fences.Length; f++)
				{
					PointOfInterestYardFence run = yard.Fences[f];
					int seed = unchecked((int)BiomeLinearFeatures.Hash(record.SiteSeed, 0x594152, y, f));
					var pieces = new List<(Vector2 A, Vector2 B)>();
					if (run.Gate)
					{
						Vector2 at = yard.Door.HasValue ? Project(yard.Door.Value, run.A, run.B) : yard.GateAt;
						if (GateSpan(run.A, run.B, at, StructurePieces.GateModule, 0.4f, out Vector2 g0, out Vector2 g1))
						{
							pieces.Add((run.A, g0));
							pieces.Add((g1, run.B));
							if (gate != null && PlaceGate(context, joins, gatePalette, gate, g0, g1))
							{
								laid++;
								gates++;
							}
						}
						else
						{
							pieces.Add((run.A, run.B));
						}
					}
					else
					{
						pieces.Add((run.A, run.B));
					}
					foreach ((Vector2 a, Vector2 b) in pieces)
					{
						foreach ((Vector2 c, Vector2 d) in ClearOfDoors(a, b, doors, DoorReachMetres, DoorGapMetres))
						{
							if ((d - c).magnitude < 0.4f)
							{
								continue;
							}
							// The layout kept every run off the streets, the plots and the boundary: only water and steepness leave a module out.
							laid += PointOfInterestLinearFeatures.LayAlong(context, biome, own, new[] { c, d }, seed, joins: joins, planned: true);
						}
					}
				}
			}
			// What was queued is laid now, with the closing posts: the count is what stands.
			laid = joins.Close();
			if (joins.MissingPosts > 0)
			{
				PointOfInterestFeatureKit.Note(context, "yards: the fences' closing post (LinearPost) has no art yet (Generate Biome Art)");
			}
			if (gate == null && yards.Count > 0)
			{
				PointOfInterestFeatureKit.Note(context, "yards: the field gateway (LinearGate) has no art yet (Generate Biome Art)");
			}
			context.Notes?.Add($"{record.Name}: {yards.Count} house yard(s) fenced in {BiomeLinearFeatures.Id(style)} ({laid} piece(s), {gates} gate(s)).");
			return laid;
		}

		/// <summary>
		/// The style a site's yards are fenced in (pure but for the biome's list): its biome's first enclosing style (site bounds, then
		/// field edges, folds, lanes; a hedge, wall or fence), kept to a garden's height, never broken, a palisade made wattle and a wild
		/// hedge clipped; else by what the site is built of (<paramref name="siteStyle"/>): stone a drystone wall or (one site in three)
		/// a low mortared one, hide or earth wattle, anything else pickets or (two sites in five) wattle.
		/// </summary>
		public static BiomeLinearStyle StyleFor(BiomeTemplate biome, string siteStyle, int siteSeed)
		{
			IReadOnlyList<BiomeLinearStyle> styles = biome != null ? BiomeTemplateArt.LinearFeatures(biome) : null;
			if (styles != null)
			{
				foreach (BiomeLinearPlace place in new[] { BiomeLinearPlace.SiteBounds, BiomeLinearPlace.FieldEdges, BiomeLinearPlace.Folds, BiomeLinearPlace.Lanes })
				{
					foreach (BiomeLinearStyle s in styles)
					{
						if (s.Where == place && BiomeLinearFeatures.IsLaid(s)
							&& (s.Kind == BiomeLinearKind.Hedge || s.Kind == BiomeLinearKind.Wall || s.Kind == BiomeLinearKind.Fence))
						{
							return Tamed(s);
						}
					}
				}
			}
			float roll = BiomeLinearFeatures.Unit(BiomeLinearFeatures.Hash(siteSeed, 0x594152, 0x5354, 0));
			string built = siteStyle ?? string.Empty;
			BiomeLinearForm form;
			if (built.IndexOf("stone", System.StringComparison.OrdinalIgnoreCase) >= 0)
			{
				form = roll < 0.33f ? BiomeLinearForm.Mortared : BiomeLinearForm.Drystone;
			}
			else if (built.IndexOf("hide", System.StringComparison.OrdinalIgnoreCase) >= 0 || built.IndexOf("earth", System.StringComparison.OrdinalIgnoreCase) >= 0)
			{
				form = BiomeLinearForm.Wattle;
			}
			else
			{
				form = roll < 0.4f ? BiomeLinearForm.Wattle : BiomeLinearForm.Picket;
			}
			return Tamed(new BiomeLinearStyle { Kind = BiomeLinearFeatures.KindOf(form), Form = form, Where = BiomeLinearPlace.SiteBounds, Density = 1f });
		}

		/// <summary>A style kept to a garden's: at most <see cref="FenceHeight"/> (a hedge <see cref="HedgeHeight"/>), unbroken, laid every time.</summary>
		private static BiomeLinearStyle Tamed(BiomeLinearStyle s)
		{
			if (s.Form == BiomeLinearForm.Palisade)
			{
				s.Form = BiomeLinearForm.Wattle;
			}
			if (s.Form == BiomeLinearForm.Wild)
			{
				s.Form = BiomeLinearForm.Clipped;
			}
			s.Kind = BiomeLinearFeatures.KindOf(s.Form);
			s.Height = Mathf.Min(BiomeLinearFeatures.HeightOf(s), s.Kind == BiomeLinearKind.Hedge ? HedgeHeight : FenceHeight);
			s.Breaks = 0f;
			s.Density = 1f;
			s.Where = BiomeLinearPlace.SiteBounds;
			return s;
		}

		/// <summary>The point on a run (its segment) nearest a point.</summary>
		public static Vector2 Project(Vector2 p, Vector2 a, Vector2 b)
		{
			Vector2 d = b - a;
			float t = d.sqrMagnitude > 1e-8f ? Mathf.Clamp01(Vector2.Dot(p - a, d) / d.sqrMagnitude) : 0f;
			return a + d * t;
		}

		/// <summary>
		/// Where a gate of <paramref name="width"/> stands on a run, centred as near <paramref name="at"/> as leaves
		/// <paramref name="margin"/> of fence either end (pure): false when the run is too short for it.
		/// </summary>
		public static bool GateSpan(Vector2 a, Vector2 b, Vector2 at, float width, float margin, out Vector2 g0, out Vector2 g1)
		{
			Vector2 d = b - a;
			float length = d.magnitude;
			g0 = g1 = a;
			if (length < width + 2f * margin)
			{
				return false;
			}
			d /= length;
			float middle = Mathf.Clamp(Vector2.Dot(at - a, d), margin + width * 0.5f, length - margin - width * 0.5f);
			g0 = a + d * (middle - width * 0.5f);
			g1 = a + d * (middle + width * 0.5f);
			return true;
		}

		/// <summary>
		/// A run's stretches clear of the doors it passes (pure): wherever a door's way out lies within <paramref name="reach"/> of the
		/// run, <paramref name="gap"/> metres of it either side of the door's foot are left open.
		/// </summary>
		public static List<(Vector2 A, Vector2 B)> ClearOfDoors(Vector2 a, Vector2 b, IReadOnlyList<Vector2> doors, float reach, float gap)
		{
			var open = new List<(float, float)>();
			Vector2 d = b - a;
			float length = d.magnitude;
			var kept = new List<(Vector2, Vector2)>();
			if (length < 1e-4f)
			{
				return kept;
			}
			Vector2 u = d / length;
			if (doors != null)
			{
				foreach (Vector2 door in doors)
				{
					float t = Vector2.Dot(door - a, u);
					if (t < -gap || t > length + gap || (a + u * Mathf.Clamp(t, 0f, length) - door).magnitude > reach)
					{
						continue;
					}
					open.Add((t - gap, t + gap));
				}
			}
			open.Sort((p, q) => p.Item1.CompareTo(q.Item1));
			float from = 0f;
			foreach ((float lo, float hi) in open)
			{
				if (lo > from)
				{
					kept.Add((a + u * from, a + u * Mathf.Min(lo, length)));
				}
				from = Mathf.Max(from, hi);
				if (from >= length)
				{
					break;
				}
			}
			if (from < length)
			{
				kept.Add((a + u * from, b));
			}
			return kept;
		}

		/// <summary>The gateway between two ground points, queued with the runs it stands in (its posts close their ends): its gate swung into the yard (the run's left).</summary>
		private static bool PlaceGate(PointOfInterestSiteContext context, PointOfInterestLinearFeatures.Joins joins, BiomeStructurePalette palette, GameObject prefab, Vector2 a, Vector2 b)
		{
			Vector2 mid = 0.5f * (a + b);
			if (context.IsWet(mid.x, mid.y, 0.05f) || context.Water != null && context.Water.KindAt(mid.x, mid.y) != WaterKind.None)
			{
				return false;
			}
			joins.Gate(PointOfInterestLinearFeatures.Copy(context), palette, prefab, PointOfInterestLinearFeatures.Joint(context, a), PointOfInterestLinearFeatures.Joint(context, b),
				StructurePieces.GateModule);
			return true;
		}
	}
}
#endif
