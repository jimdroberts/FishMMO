#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using FishMMO.Shared.Biomes;
using FishMMO.Shared.Celestial;
using FishMMO.Shared.NameGeneration;
using UnityEngine;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>Where a template slot's pieces go in its site.</summary>
	/// <remarks>Append only: stored in template assets by ordinal.</remarks>
	public enum PointOfInterestSlotRole : byte
	{
		/// <summary>The layout's default for the slot (<see cref="PointOfInterestLayouts.RoleFor"/>).</summary>
		Auto,
		/// <summary>The first piece in the middle facing ahead, the rest packed round it facing in (a keep, a fire, an altar, stalls round a square).</summary>
		Centre,
		/// <summary>Evenly round a ring outside whatever stands in the middle, facing in (tents round a fire, a stone circle).</summary>
		Ring,
		/// <summary>A closed ring of modules along the edge, facing out, broken only by the gates (or one opening).</summary>
		Perimeter,
		/// <summary>Set into the perimeter, the first across the heading: how many is the slot's count.</summary>
		Gate,
		/// <summary>On the perimeter's corners, evenly; how many is the slot's count.</summary>
		WallTower,
		/// <summary>Along both sides of the site's streets, fronts to the street, nearest the middle first.</summary>
		Street,
		/// <summary>In rows square to the heading, all facing ahead (graves, a farm's sheds).</summary>
		Rows,
		/// <summary>Anywhere free, any way round (crates, rubble, bones).</summary>
		Scatter,
		/// <summary>End to end along the heading through the middle.</summary>
		Line,
		/// <summary>Modules end to end across the water the site stands over, at deck height (a bridge).</summary>
		Span,
		/// <summary>From the shore ahead out over the water, at deck height; the count is how many piers.</summary>
		Pier,
	}

	/// <summary>One piece a layout places: which slot it fills, where in the site's frame, and which way it faces.</summary>
	public struct PointOfInterestLayoutPiece
	{
		public int Slot;
		/// <summary>The request item it lays, or −1 for a piece the layout made up (a wall module, a gate, a span).</summary>
		public int Item;
		public PointOfInterestSlotRole Role;
		/// <summary>Metres in the site's frame: x to its right, y (z) ahead along its heading.</summary>
		public Vector2 Offset;
		/// <summary>Degrees about +y, added to the site's heading. A piece's front looks down its −z, so 180 faces ahead.</summary>
		public float Yaw;
		/// <summary>Width (along the piece's x) and depth (z) it was laid with, metres.</summary>
		public Vector2 Footprint;
	}

	/// <summary>A slot as a layout sees it: its role, its pieces' size, and how many it asks for.</summary>
	public struct PointOfInterestLayoutSlot
	{
		public PointOfInterestSlotRole Role;
		/// <summary>Its pieces' footprint (the size the layout gives the pieces it makes up).</summary>
		public Vector2 Footprint;
		/// <summary>The module length of a piece laid end to end; 0 for none.</summary>
		public float Module;
		public int Count;
		/// <summary>A landmark (a church, a temple): laid before the houses, fronting the market's plaza where it can.</summary>
		public bool Landmark;
	}

	/// <summary>One counted piece a layout places (any role but the made-up ones).</summary>
	public struct PointOfInterestLayoutItem
	{
		public int Slot;
		public Vector2 Footprint;
	}

	/// <summary>Everything a layout needs: pure data, so tests lay sites with no scene.</summary>
	public sealed class PointOfInterestLayoutRequest
	{
		public PointOfInterestLayout Layout;
		/// <summary>The site's footprint radius, metres: its outline's bounding reach.</summary>
		public float Radius;
		/// <summary>
		/// The site's outline (<see cref="PointOfInterestRecord.Outline"/>, world bearings); empty for a disc of
		/// <see cref="Radius"/>. The layout's boundary is its <see cref="PointOfInterestLayouts.Inset"/> share along every bearing.
		/// </summary>
		public float[] Outline = Array.Empty<float>();
		/// <summary>The site's heading, degrees: turns the outline's world bearings into the site's frame.</summary>
		public float Yaw;
		/// <summary>The site's pad, for its terraces (<see cref="PointOfInterestTerraces"/>); null for none.</summary>
		public PointOfInterestPad? Pad;
		/// <summary>The site's moat (MoatShaper), whose inner bank the wall keeps inside; null for none.</summary>
		public PointOfInterestShape Moat;
		/// <summary>The site's kind, which the town plan is chosen by; null chooses by size alone.</summary>
		public POIType? Kind;
		/// <summary>The town plan; Auto chooses (<see cref="PointOfInterestTownPlan"/>).</summary>
		public PointOfInterestTownPlan Plan;
		/// <summary>The plan the layout laid (filled by the packer).</summary>
		public PointOfInterestTownPlan ChosenPlan;
		/// <summary>The town plan's own seed: its draws move nothing else the layout lays.</summary>
		public int Seed;
		/// <summary>The least clear ground between two pieces, metres.</summary>
		public float Gap = 1f;
		public readonly List<PointOfInterestLayoutSlot> Slots = new List<PointOfInterestLayoutSlot>();
		public readonly List<PointOfInterestLayoutItem> Items = new List<PointOfInterestLayoutItem>();
		/// <summary>Half the length a <see cref="PointOfInterestSlotRole.Span"/> must cover; 0 takes the footprint.</summary>
		public float SpanHalf;
		/// <summary>
		/// Where a bridge's spans start and end along the heading (metres, signed, the start behind the centre): bank top to
		/// bank top, so a crossing off-centre between banks at different distances is covered. NaN uses ±<see cref="SpanHalf"/>.
		/// </summary>
		public float SpanFrom = float.NaN, SpanTo = float.NaN;
		/// <summary>Metres ahead of the centre where the water begins, for piers; NaN where there is none.</summary>
		public float ShoreAhead = float.NaN;
		/// <summary>
		/// Where a pier runs, metres ahead of the centre: from where the bank stands at its deck's height out to where its
		/// piles would no longer reach the bed (<see cref="MeasureShore(PointOfInterestSiteContext, out float, out float, out float)"/>);
		/// NaN lays it from <see cref="ShoreAhead"/> for its footprint's share of the site.
		/// </summary>
		public float PierFrom = float.NaN, PierTo = float.NaN;
		/// <summary>Spots kept clear for the site's gameplay (a waypoint, a respawn point): x, z in the site's frame and y the radius.</summary>
		public readonly List<Vector3> Clearings = new List<Vector3>();
		/// <summary>
		/// Bearings (degrees in the site's frame, 0 its heading) where ways come in (<see cref="ScenePath.FromEntrance"/>): a
		/// walled site opens its gates there first.
		/// </summary>
		public readonly List<float> Entrances = new List<float>();
		/// <summary>
		/// How far in from the centre each way's last point stands along its entrance's bearing, metres
		/// (<see cref="SiteEntrances.EndDistance"/>), parallel to <see cref="Entrances"/>: the street in from it passes through it.
		/// Missing entries take <see cref="SiteEntrances.EntryDepth"/> of the outline's reach.
		/// </summary>
		public readonly List<float> EntranceDepths = new List<float>();
		/// <summary>The streets the layout laid (filled by the packer), in the site's frame.</summary>
		public readonly List<PointOfInterestLayoutStreet> Streets = new List<PointOfInterestLayoutStreet>();
		/// <summary>The plazas the layout kept open (a green, a market place, a square; filled by the packer): convex polygons, site frame.</summary>
		public readonly List<Vector2[]> Plazas = new List<Vector2[]>();
	}

	/// <summary>
	/// Where a template's pieces go inside a footprint: footprint-based packing, so no two pieces overlap (with
	/// <see cref="PointOfInterestLayoutRequest.Gap"/> between them) and every piece but a span or pier lies inside the
	/// footprint. Pure: tests read it with no scene.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Order.</b> The pieces that define the site are laid first and the rest fit round them: the perimeter (walls,
	/// gates, towers), the spans and piers, the streets (kept clear), the middle, the lines, the rings, the street
	/// fronts, the rows and last the scatter. A piece with no room left is dropped, never forced into an overlap.
	/// </para>
	/// <para>
	/// <b>A wall ring closes.</b> It follows the site's boundary (its outline's <see cref="Inset"/> share, inside any moat's
	/// berm): each gate stands on its way's own bearing, and between two gates the wall's modules are equal chords of the
	/// line from jamb to jamb, each fitted to its chord and run on past its corner so its face meets its neighbour's; a site
	/// with no gate slot keeps an opening at the heading (or its first way's bearing) as its way in.
	/// </para>
	/// <para>
	/// <b>Streets</b> follow a town plan (<see cref="PointOfInterestTownPlan"/>): a street village, a green village, a market
	/// town, a hill town on its terraces, a walled city, a port, a bailey; each road in runs on as a street.
	/// </para>
	/// <para>
	/// <b>Facing.</b> A kit piece's front looks down its −z (StructureKit's convention), so a piece facing a direction
	/// <c>d</c> (degrees, 0 ahead) has yaw <c>d + 180</c> (<see cref="FacingYaw"/>).
	/// </para>
	/// </remarks>
	public static partial class PointOfInterestLayouts
	{
		/// <summary>Pieces stay this share of the footprint radius in, so nothing hangs over a pad's edge.</summary>
		public const float Inset = 0.9f;

		/// <summary>The opening a perimeter with no gate slot leaves at its heading, at least, metres.</summary>
		public const float EntranceMetres = 4f;

		/// <summary>The walk between two rows, beyond the gap, metres.</summary>
		public const float RowAisleMetres = 1f;

		/// <summary>A slot's role, its Auto resolved by the template's layout.</summary>
		public static PointOfInterestSlotRole RoleFor(PointOfInterestLayout layout, int slot, PointOfInterestSlotRole role)
		{
			if (role != PointOfInterestSlotRole.Auto)
			{
				return role;
			}
			switch (layout)
			{
				case PointOfInterestLayout.Single:
					return slot == 0 ? PointOfInterestSlotRole.Centre : PointOfInterestSlotRole.Scatter;
				case PointOfInterestLayout.Ring:
					return slot == 0 ? PointOfInterestSlotRole.Ring : PointOfInterestSlotRole.Scatter;
				case PointOfInterestLayout.Grid:
					return PointOfInterestSlotRole.Rows;
				case PointOfInterestLayout.Street:
					return PointOfInterestSlotRole.Street;
				case PointOfInterestLayout.Walled:
					return slot == 0 ? PointOfInterestSlotRole.Perimeter : PointOfInterestSlotRole.Rows;
				case PointOfInterestLayout.Linear:
					return PointOfInterestSlotRole.Line;
				default:
					return PointOfInterestSlotRole.Scatter;
			}
		}

		/// <summary>True for a role whose pieces the layout makes up from the site's geometry rather than counts.</summary>
		public static bool Generates(PointOfInterestSlotRole role)
			=> role == PointOfInterestSlotRole.Perimeter || role == PointOfInterestSlotRole.Gate || role == PointOfInterestSlotRole.WallTower
				|| role == PointOfInterestSlotRole.Span || role == PointOfInterestSlotRole.Pier;

		/// <summary>True for a role whose pieces may reach past the footprint (a bridge over a wide river, a pier out over the sea).</summary>
		public static bool Reaches(PointOfInterestSlotRole role) => role == PointOfInterestSlotRole.Span || role == PointOfInterestSlotRole.Pier;

		/// <summary>The yaw that turns a piece's front (its −z) to look along a direction, degrees (0 ahead, 90 right).</summary>
		public static float FacingYaw(float directionDegrees) => Mathf.Repeat(directionDegrees + 180f, 360f);

		/// <summary>The direction of a site-frame vector, degrees (0 ahead, 90 right).</summary>
		public static float DirectionOf(Vector2 v) => Mathf.Repeat(Mathf.Atan2(v.x, v.y) * Mathf.Rad2Deg, 360f);

		/// <summary>A site-frame offset turned by a yaw (Unity's rotation about +y).</summary>
		public static Vector2 Rotate(Vector2 local, float yawDegrees)
		{
			float a = yawDegrees * Mathf.Deg2Rad, c = Mathf.Cos(a), s = Mathf.Sin(a);
			return new Vector2(local.x * c + local.y * s, -local.x * s + local.y * c);
		}

		/// <summary>The four corners of a footprint at a centre and yaw.</summary>
		public static void Corners(Vector2 centre, float yawDegrees, Vector2 size, Vector2[] four)
		{
			Vector2 hx = Rotate(new Vector2(size.x * 0.5f, 0f), yawDegrees), hz = Rotate(new Vector2(0f, size.y * 0.5f), yawDegrees);
			four[0] = centre + hx + hz;
			four[1] = centre + hx - hz;
			four[2] = centre - hx - hz;
			four[3] = centre - hx + hz;
		}

		/// <summary>The farthest corner of a footprint from the site's centre, metres.</summary>
		public static float Reach(Vector2 centre, float yawDegrees, Vector2 size)
		{
			var four = new Vector2[4];
			Corners(centre, yawDegrees, size, four);
			float far = 0f;
			foreach (Vector2 corner in four)
			{
				far = Mathf.Max(far, corner.magnitude);
			}
			return far;
		}

		/// <summary>Whether two footprints (rectangles at centres and yaws) overlap by more than a hair: separating axes.</summary>
		public static bool Overlaps(Vector2 centreA, float yawA, Vector2 sizeA, Vector2 centreB, float yawB, Vector2 sizeB)
		{
			Box a = Box.Of(centreA, yawA, sizeA, 0f, 0), b = Box.Of(centreB, yawB, sizeB, 0f, 0);
			return a.Overlaps(b);
		}

		/// <summary>Lays a slot list by counts with point-sized pieces (the layout before any piece is measured).</summary>
		public static List<PointOfInterestLayoutPiece> Place(PointOfInterestLayout layout, float radius, IReadOnlyList<int> counts, DeterministicRNG random)
		{
			var request = new PointOfInterestLayoutRequest { Layout = layout, Radius = radius };
			for (int s = 0; s < counts.Count; s++)
			{
				PointOfInterestSlotRole role = RoleFor(layout, s, PointOfInterestSlotRole.Auto);
				int count = Math.Max(0, counts[s]);
				request.Slots.Add(new PointOfInterestLayoutSlot { Role = role, Count = count });
				if (!Generates(role))
				{
					for (int k = 0; k < count; k++)
					{
						request.Items.Add(new PointOfInterestLayoutItem { Slot = s });
					}
				}
			}
			return Place(request, random);
		}

		/// <summary>Lays a request: see the class remarks. Deterministic in <paramref name="random"/>.</summary>
		public static List<PointOfInterestLayoutPiece> Place(PointOfInterestLayoutRequest request, DeterministicRNG random)
		{
			var packer = new Packer(request, random);
			if (request.Radius > 0f && request.Slots.Count > 0)
			{
				packer.Run();
			}
			return packer.Pieces;
		}

		// The packer (its boxes, the perimeter, the town plans) is PointOfInterestLayouts.Packer.cs.
	}

	/// <summary>A piece's weathering: Auto reads the site's kind and biome; the rest force one finish, laid on a share of the pieces.</summary>
	/// <remarks>Append only: stored in template assets by ordinal.</remarks>
	public enum PointOfInterestFinish : byte
	{
		Auto,
		None,
		Mossy,
		Charred,
		Algae,
	}

	/// <summary>
	/// Lays a template's pieces by its layout inside the site's footprint, on the ground, as instanced props of the
	/// scene's POI prop set. A piece no source can resolve is skipped (and counted in the notes).
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Stable before the art.</b> Every draw is taken whether or not the piece resolves, and the layout packs the
	/// pieces by the kit's measured footprints (<see cref="PointOfInterestPieceResolver.Measure"/>, from the kit's table,
	/// never the art), so a site laid before its art exists lays the same pieces in the same places once it arrives.
	/// </para>
	/// <para>
	/// <b>Seating.</b> A piece on the ground is set at its centre's ground, lowered until its foundation reaches the
	/// lowest corner of its footprint, so a house on a slope shows its plinth rather than a gap. A deck piece (a bridge
	/// span, a pier) is set at its deck: the banks' height over a river, a metre above the water out from a shore.
	/// </para>
	/// <para>
	/// <b>Decay</b> leaves a piece out with probability decay / 2, lays a piece past the kit's ruin threshold as its ruin,
	/// and sinks and tilts the ones it keeps. <b>Counts</b> follow the site's size class: a small site draws from the
	/// lowest third of a slot's range, a large one (every capital) from the top third.
	/// </para>
	/// </remarks>
	[Serializable]
	public sealed class PropsFeature : PointOfInterestFeature
	{
		/// <summary>The most a fully ruined piece is sunk, metres, and tilted, degrees.</summary>
		public float MaxSinkMetres = 0.4f;
		public float MaxTiltDegrees = 8f;
		/// <summary>The least clear ground between two pieces, metres.</summary>
		[Min(0f)] public float Gap = 1f;
		/// <summary>Draw a slot's count from the third of its range the site's size class names.</summary>
		public bool CountsFollowSize = true;
		/// <summary>Weathering: Auto by kind and biome (algae under water and in swamps, moss on old pieces in damp country, char on battlefields).</summary>
		public PointOfInterestFinish Finish = PointOfInterestFinish.Auto;
		/// <summary>The share of pieces a forced finish is laid on.</summary>
		[Range(0f, 1f)] public float FinishChance = 1f;
		/// <summary>
		/// Leave out a ground piece whose middle is under water (a port's houses past its shore). Off for the kinds that
		/// stand in water on purpose (a drowned village); never applies under the sea, to stilts, piers or bridges.
		/// </summary>
		public bool KeepOutOfWater = true;
		/// <summary>Spots no piece is laid on, kept for the site's gameplay: x, z in the site's frame and y the radius, metres.</summary>
		public List<Vector3> Clearings = new List<Vector3>();

		/// <summary>One piece's draws, always all taken so a piece's draws never shift another's.</summary>
		private struct Draws
		{
			public int Seed;
			public float Decay, LeaveOut, Scale, TiltX, TiltZ, FinishRoll;
		}

		private static Draws Take(DeterministicRNG random, PointOfInterestPieceSlot slot, bool unscaled)
		{
			var d = new Draws
			{
				Decay = random.Range(slot.MinDecay, Mathf.Max(slot.MinDecay, slot.MaxDecay)),
				LeaveOut = random.NextFloat(),
				Seed = random.Next(),
			};
			float scale = random.Range(slot.MinScale, Mathf.Max(slot.MinScale, slot.MaxScale));
			d.Scale = unscaled ? 1f : scale;
			d.TiltX = random.Range(-1f, 1f);
			d.TiltZ = random.Range(-1f, 1f);
			d.FinishRoll = random.NextFloat();
			return d;
		}

		/// <summary>
		/// The streets the layout laid, written to the scene's ways (<see cref="ScenePathClass.Street"/>) so the path surface
		/// draws them: cobbled in a city, gravel in a town (cobbled where it is built of stone), trodden earth in a village.
		/// </summary>
		private static void AddStreets(PointOfInterestSiteContext context, PointOfInterestLayoutRequest request)
		{
			if (context.Points == null || request.Streets.Count == 0)
			{
				return;
			}
			PointOfInterestRecord record = context.Record;
			int rank = PathNetworkPlanner.RankOf(record.Kind);
			bool stoneBuilt = !string.IsNullOrEmpty(context.Style) && context.Style.IndexOf("Stone", StringComparison.OrdinalIgnoreCase) >= 0;
			ScenePathSurface surface = rank >= 4 ? ScenePathSurface.Stone : rank >= 3 ? (stoneBuilt ? ScenePathSurface.Stone : ScenePathSurface.Gravel) : ScenePathSurface.Earth;
			float wear = rank >= 3 ? 1f : 0.85f;
			int id = 0;
			foreach (ScenePath existing in context.Points.Paths)
			{
				id = Math.Max(id, existing != null ? existing.Id : 0);
			}
			// Walked on raised plank decks between the houses where the site's biome raises its streets (2026-10-18, I2i).
			bool boardwalks = BiomeBoardwalkRules.Raises(BiomeTemplateArt.Boardwalks(context.BiomeAt?.Invoke(record.Position.x, record.Position.z)), ScenePathClass.Street);
			var streetPaths = boardwalks ? new List<ScenePath>() : null;
			foreach (PointOfInterestLayoutStreet street in request.Streets)
			{
				List<Vector2> line = Resampled(street.Points, PathNetworkPlanner.PointSpacing);
				int n = line.Count;
				if (n < 2)
				{
					continue;
				}
				float width = street.Width;
				// A back lane is trodden, not paved, and worn less.
				ScenePathSurface laneSurface = street.Rank >= 2 && surface == ScenePathSurface.Stone ? ScenePathSurface.Gravel : surface;
				float laneWear = street.Rank >= 2 ? wear * 0.85f : wear;
				var path = new ScenePath
				{
					Id = ++id,
					Class = ScenePathClass.Street,
					FromId = record.Id,
					ToId = record.Id,
					Points = new Vector3[n],
					HalfWidth = new float[n],
					Wear = new float[n],
					Surface = new byte[n],
					Flags = new byte[n],
				};
				for (int i = 0; i < n; i++)
				{
					path.Points[i] = context.OnGround(line[i]);
					// Narrower than the strip kept clear for it: the houses' fronts keep a verge.
					path.HalfWidth[i] = width * 0.4f;
					path.Wear[i] = laneWear;
					path.Surface[i] = (byte)laneSurface;
					path.Flags[i] = (byte)(ScenePathPointFlags.InSite | (boardwalks ? ScenePathPointFlags.Boardwalk : ScenePathPointFlags.None));
				}
				context.Points.Paths.Add(path);
				streetPaths?.Add(path);
			}
			// The market places and squares paved (a four-cornered plaza; a green's seven corners are left to the grass): one
			// squared stretch from the middle of its narrow end to the middle of its wide one, as wide as its wide end.
			foreach (Vector2[] plaza in request.Plazas)
			{
				if (plaza == null || plaza.Length != 4 || boardwalks)
				{
					continue;
				}
				Vector2 a = (plaza[0] + plaza[1]) * 0.5f, b = (plaza[2] + plaza[3]) * 0.5f;
				float half = 0.5f * Mathf.Max(Vector2.Distance(plaza[0], plaza[1]), Vector2.Distance(plaza[2], plaza[3]));
				byte flags = (byte)(ScenePathPointFlags.InSite | ScenePathPointFlags.Squared);
				context.Points.Paths.Add(new ScenePath
				{
					Id = ++id,
					Class = ScenePathClass.Street,
					FromId = record.Id,
					ToId = record.Id,
					Points = new[] { context.OnGround(a), context.OnGround(b) },
					HalfWidth = new[] { half, half },
					Wear = new[] { wear, wear },
					Surface = new[] { (byte)surface, (byte)surface },
					Flags = new[] { flags, flags },
				});
			}
			if (streetPaths != null)
			{
				LayStreetBoardwalks(context, streetPaths);
			}
		}

		/// <summary>
		/// The plank decks of a boardwalk settlement's streets (2026-10-18, I2i; PathBoardwalks.Lay): each street's whole length, ramped
		/// down to the ground at an end that stops on its own and held at deck height into an end that meets another street.
		/// </summary>
		private static void LayStreetBoardwalks(PointOfInterestSiteContext context, List<ScenePath> streets)
		{
			bool Meets(ScenePath street, Vector3 end)
			{
				foreach (ScenePath other in streets)
				{
					if (other == street)
					{
						continue;
					}
					for (int k = 1; k < other.Count; k++)
					{
						Vector3 a = other.Points[k - 1], b = other.Points[k];
						var ab = new Vector2(b.x - a.x, b.z - a.z);
						var ap = new Vector2(end.x - a.x, end.z - a.z);
						float t = ab.sqrMagnitude > 1e-6f ? Mathf.Clamp01(Vector2.Dot(ap, ab) / ab.sqrMagnitude) : 0f;
						if ((ap - ab * t).magnitude <= PathBoardwalks.DeckHalfWidth(other.HalfWidth[k]) + 1.5f)
						{
							return true;
						}
					}
				}
				return false;
			}
			var missing = new List<string>();
			int laid = 0;
			foreach (ScenePath street in streets)
			{
				laid += PathBoardwalks.Lay(street, 0, street.Count - 1, context.GroundAt, context.AddProp, context.Record.SiteSeed ^ street.Id, missing,
					!Meets(street, street.Points[0]), !Meets(street, street.Points[street.Count - 1]));
				if (missing.Count > 0)
				{
					PointOfInterestFeatureKit.Note(context, missing[0]);
					return;
				}
			}
			PointOfInterestFeatureKit.Note(context, $"{streets.Count} street(s) raised on boardwalks ({laid} deck module(s) and bent(s))");
		}

		/// <summary>A polyline's fewest points (Douglas–Peucker) that keep every dropped one within <paramref name="tolerance"/> metres of it.</summary>
		private static List<Vector2> Simplified(Vector2[] points, float tolerance)
		{
			var kept = new List<Vector2>();
			if (points == null || points.Length == 0)
			{
				return kept;
			}
			var keep = new bool[points.Length];
			keep[0] = keep[points.Length - 1] = true;
			var stack = new Stack<(int a, int b)>();
			stack.Push((0, points.Length - 1));
			while (stack.Count > 0)
			{
				(int a, int b) = stack.Pop();
				Vector2 pa = points[a], ab = points[b] - pa;
				float worst = -1f;
				int at = -1;
				for (int i = a + 1; i < b; i++)
				{
					float t = Mathf.Clamp01(Vector2.Dot(points[i] - pa, ab) / Mathf.Max(1e-6f, ab.sqrMagnitude));
					float d = Vector2.Distance(pa + ab * t, points[i]);
					if (d > worst)
					{
						worst = d;
						at = i;
					}
				}
				if (at > 0 && worst > tolerance)
				{
					keep[at] = true;
					stack.Push((a, at));
					stack.Push((at, b));
				}
			}
			for (int i = 0; i < points.Length; i++)
			{
				if (keep[i])
				{
					kept.Add(points[i]);
				}
			}
			return kept;
		}

		/// <summary>A polyline resampled at (about) even spacing, its ends kept.</summary>
		private static List<Vector2> Resampled(Vector2[] points, float spacing)
		{
			var line = new List<Vector2>();
			if (points == null || points.Length < 2)
			{
				return line;
			}
			float total = 0f;
			for (int i = 1; i < points.Length; i++)
			{
				total += Vector2.Distance(points[i - 1], points[i]);
			}
			int n = Math.Max(2, Mathf.CeilToInt(total / Mathf.Max(0.1f, spacing)) + 1);
			int seg = 1;
			float before = 0f;
			for (int k = 0; k < n; k++)
			{
				float at = total * k / (n - 1);
				while (seg < points.Length - 1 && before + Vector2.Distance(points[seg - 1], points[seg]) < at)
				{
					before += Vector2.Distance(points[seg - 1], points[seg]);
					seg++;
				}
				float length = Mathf.Max(1e-4f, Vector2.Distance(points[seg - 1], points[seg]));
				line.Add(Vector2.Lerp(points[seg - 1], points[seg], Mathf.Clamp01((at - before) / length)));
			}
			return line;
		}

		/// <summary>
		/// The ground a site's pieces wear, written to the scene's ways as yards (<see cref="ScenePathClass.Yard"/>): bare
		/// earth under and just round each building (no grass blade comes up through a floor), a trodden apron round a
		/// well, a fire or a stall, and a worn way out of each front door. The grass between them grows; the path field
		/// thins it where feet go (FishGroundPaths.hlsl).
		/// </summary>
		/// <remarks>
		/// A building's yard is two rectangles (<see cref="ScenePathPointFlags.Squared"/>): its apron, worn as kept as the
		/// site is, and its plan, a floor (<see cref="ScenePathPointFlags.Floor"/>) always worn bare. A round-ended strip
		/// left grass in every corner of a house wider than about 3 m, a graveyard's lower wear let a share of the blades
		/// through a crypt's floor, and a door path or a well's apron reaching in through a wall carried its own wear
		/// indoors. A wall tower is a floor with no apron (the ground beside a wall is not worn by it), round because it
		/// may be: the circle just inside a square tower's corners. A ruin has no floors: it is overgrown, the grass and
		/// the details back inside its walls as thinly as its apron's wear keeps them off.
		/// </remarks>
		private static void AddYards(PointOfInterestSiteContext context,
			List<(Vector3 at, Quaternion turn, Vector2 size, PointOfInterestSlotRole role, string tag)> grounds)
		{
			if (context.Points == null || grounds.Count == 0)
			{
				return;
			}
			int id = 0;
			foreach (ScenePath existing in context.Points.Paths)
			{
				id = Math.Max(id, existing != null ? existing.Id : 0);
			}
			// How lived in the site is: a ruin's ground has grown back over, a camp's is fresh, a town's beaten bare.
			PointOfInterestGroup group = PointOfInterestKinds.Info(context.Record.Kind).Group;
			float kept = group == PointOfInterestGroup.Ruins || group == PointOfInterestGroup.Dead ? 0.3f
				: PointOfInterestKinds.Info(context.Record.Kind).Has(PointOfInterestTraits.Settlement) ? 1f : 0.8f;
			bool overgrown = group == PointOfInterestGroup.Ruins;
			void Add(Vector3 a, Vector3 b, float half, float wear, int toId, ScenePathPointFlags shape = ScenePathPointFlags.None)
			{
				byte flags = (byte)(ScenePathPointFlags.InSite | shape);
				var path = new ScenePath
				{
					Id = ++id,
					Class = ScenePathClass.Yard,
					FromId = -1,
					ToId = toId,
					Points = new[] { a, b },
					HalfWidth = new[] { half, half },
					Wear = new[] { wear, wear },
					Surface = new[] { (byte)ScenePathSurface.Earth, (byte)ScenePathSurface.Earth },
					Flags = new[] { flags, flags },
				};
				context.Points.Paths.Add(path);
			}
			foreach ((Vector3 at, Quaternion turn, Vector2 size, PointOfInterestSlotRole role, string tag) in grounds)
			{
				// Walls, gates, spans and piers: the ground beside them is not worn by them.
				if (role == PointOfInterestSlotRole.Perimeter || role == PointOfInterestSlotRole.Gate
					|| role == PointOfInterestSlotRole.Span || role == PointOfInterestSlotRole.Pier)
				{
					continue;
				}
				float width = Mathf.Max(0.1f, size.x), depth = Mathf.Max(0.1f, size.y);
				float longest = Mathf.Max(width, depth), shortest = Mathf.Min(width, depth);
				if (role == PointOfInterestSlotRole.WallTower)
				{
					if (!overgrown)
					{
						Add(at, at, shortest * 0.6f, 1f, -1, ScenePathPointFlags.Floor);
					}
					continue;
				}
				if (longest < 1.2f)
				{
					continue;
				}
				string t = tag ?? string.Empty;
				bool gathering = t.IndexOf("well", StringComparison.OrdinalIgnoreCase) >= 0 || t.IndexOf("fire", StringComparison.OrdinalIgnoreCase) >= 0
					|| t.IndexOf("stall", StringComparison.OrdinalIgnoreCase) >= 0 || t.IndexOf("market", StringComparison.OrdinalIgnoreCase) >= 0
					|| role == PointOfInterestSlotRole.Centre;
				if (gathering)
				{
					// Where people stand about: a trodden ring a pace and a half round it.
					Add(at, at, longest * 0.5f + 1.6f, 0.8f * kept, -1);
					continue;
				}
				bool building = longest >= 3f;
				// Under and round it, along its long side: a building's apron and its plan as a floor, anything else a capsule.
				Vector3 axis = turn * (width >= depth ? Vector3.right : Vector3.forward);
				float reach = Mathf.Max(0f, longest - shortest) * 0.5f;
				if (!building)
				{
					Add(at - axis * reach, at + axis * reach, shortest * 0.5f + 0.3f, 0.75f * kept, -1);
					continue;
				}
				// The apron is narrower round a ruin, but never under the half metre that, with the wall's own thickness,
				// keeps the frayed edge's thinning outside the floor (FishPathBare). Squared: end midpoint to end midpoint.
				float apron = Mathf.Lerp(0.3f, 0.6f, kept);
				Vector3 end = axis * (longest * 0.5f);
				Vector3 aproned = axis * (longest * 0.5f + apron);
				Add(at - aproned, at + aproned, shortest * 0.5f + apron, kept, -1, ScenePathPointFlags.Squared);
				if (!overgrown)
				{
					Add(at - end, at + end, shortest * 0.5f, 1f, -1, ScenePathPointFlags.Squared | ScenePathPointFlags.Floor);
				}
				// Out of its front door (a piece's front looks down its −z) a few paces toward the street.
				Vector3 front = turn * Vector3.back;
				Vector3 door = at + front * (depth * 0.5f);
				Add(door, door + front * 3.5f, 0.55f, 0.9f * kept, context.Record.Id);
			}
		}

		/// <summary>
		/// What gathers round lived-in buildings (2026-10-11: sites looked dated): by each front door a water butt, a
		/// bench or a low lantern; a woodpile against a side wall; a cart, hay or a washing line in the back yard; lantern
		/// posts along a town's streets. Round a camp's tents, drying and weapon racks, water and a log heap. Each is kept
		/// clear of the buildings, the streets and one another; a ruin gets none.
		/// </summary>
		/// <summary>
		/// Stairs up the inside of a stone curtain to its walk (2026-10-11: walkable castle walls): against the module two
		/// along from each gate, and one across the bailey from the first, where nothing already stands.
		/// </summary>
		/// <summary>
		/// A drawbridge over the site's moat (MoatShaper) before each gate: hinged on the ditch's inner lip on the line from
		/// the site's middle through the gate, as wide as the gate's passage and long enough to rest on the far bank, lowered
		/// to begin with. A real door (PointOfInterestDoors builds it, the server works it): raised, the ditch is a ditch.
		/// </summary>
		private static void AddDrawbridges(PointOfInterestSiteContext context, List<Vector3> gateways, float stature)
		{
			if (gateways.Count == 0 || context.Record == null
				|| !MoatShaper.TryMoat(context.Points, context.Record.Id, out Vector3 centre, out float inner, out float width, out _))
			{
				return;
			}
			const float Passage = 3.5f;
			foreach (Vector3 gate in gateways)
			{
				var outward = new Vector3(gate.x - centre.x, 0f, gate.z - centre.z);
				if (outward.sqrMagnitude < 1e-4f)
				{
					continue;
				}
				outward.Normalize();
				// The doors' convention: the hinge is the leaf's corner on one jamb, Across runs along it to the other.
				var across = new Vector3(outward.z, 0f, -outward.x);
				float leafWidth = Passage * stature + 0.3f;
				// The ditch's inner lip on the gate's own line: the ring follows the wall's outline (MoatShaper.InnerToward).
				float reach = MoatShaper.TryMoat(context.Points.Shapes, context.Record.Id, out PointOfInterestShape moat) ? MoatShaper.InnerToward(moat, outward.x, outward.z) : inner;
				var lip = new Vector3(centre.x + outward.x * reach, 0f, centre.z + outward.z * reach);
				lip.y = context.GroundAt != null ? context.GroundAt(lip.x, lip.z) : gate.y;
				context.Doors.Add(new PointOfInterestSiteContext.PlacedDoor
				{
					Hinge = lip - across * (0.5f * leafWidth),
					Across = across,
					Inward = -outward,
					Width = leafWidth,
					// Up, it is a door; down, it reaches over the ditch onto the far bank.
					Height = width + 1.2f,
					Kind = StructureDoorKind.Studded,
					Building = "Drawbridge",
					Scale = stature,
					Motion = StructureDoorMotion.Drawbridge,
					StartsOpen = true,
				});
			}
		}

		/// <summary>
		/// A fixed timber bridge (the kit's BridgeSpan, fitted end to end) over the site's moat wherever one of its ways crosses
		/// the ditch with no drawbridge there: no gate on that line (a site without a wall, a gate the layout could not fit), or
		/// a gate that laid nothing. Its deck runs from bank to bank, pitched to the line between them.
		/// </summary>
		private static void AddMoatBridges(PointOfInterestSiteContext context, List<Vector3> gateways, float stature)
		{
			if (context.Record == null || context.Points == null || context.Points.Paths == null
				|| !MoatShaper.TryMoat(context.Points, context.Record.Id, out Vector3 centre, out float inner, out float width, out _)
				|| !MoatShaper.TryMoat(context.Points.Shapes, context.Record.Id, out PointOfInterestShape moatShape))
			{
				return;
			}
			float leafWidth = 3.5f * stature + 0.3f;
			var covered = new List<Vector2>();
			foreach (Vector3 gate in gateways)
			{
				var outward = new Vector2(gate.x - centre.x, gate.z - centre.z);
				if (outward.sqrMagnitude > 1e-4f)
				{
					covered.Add(outward.normalized);
				}
			}
			var bridged = new List<Vector2>();
			foreach (ScenePath path in context.Points.Paths)
			{
				if (path == null || path.Class == ScenePathClass.Street || (path.FromId != context.Record.Id && path.ToId != context.Record.Id))
				{
					continue;
				}
				// Where it crosses the ditch: the mean bearing of its points over the ring.
				Vector2 sum = Vector2.zero;
				int over = 0;
				for (int i = 0; i < path.Count; i++)
				{
					var d = new Vector2(path.Points[i].x - centre.x, path.Points[i].z - centre.z);
					float r = d.magnitude, ring = MoatShaper.InnerToward(moatShape, d.x, d.y);
					if (r > ring - 0.5f && r < ring + width + 0.5f && r > 1e-3f)
					{
						sum += d / r;
						over++;
					}
				}
				if (over == 0 || sum.sqrMagnitude < 1e-6f)
				{
					continue;
				}
				Vector2 dir = sum.normalized;
				// Over the drawbridge's own width at the lip a gate's leaf covers it; so does a bridge already laid.
				float lipReach = MoatShaper.InnerToward(moatShape, dir.x, dir.y);
				bool Near(Vector2 other) => (dir - other).magnitude * lipReach < 0.75f * leafWidth;
				if (covered.Exists(Near) || bridged.Exists(Near))
				{
					continue;
				}
				if (LayMoatBridge(context, centre, lipReach, width, dir, stature))
				{
					bridged.Add(dir);
				}
			}
		}

		/// <summary>Lays BridgeSpan modules over a moat along <paramref name="dir"/>, bank to bank; false when the art is missing.</summary>
		private static bool LayMoatBridge(PointOfInterestSiteContext context, Vector3 centre, float inner, float width, Vector2 dir, float stature)
		{
			GameObject span = StructureKit.Prefab("BridgeSpan", 1);
			if (span == null || context.GroundAt == null)
			{
				PointOfInterestFeatureKit.Note(context, "a moat's bridge (Structure_BridgeSpan) is not generated yet: Biome Tools → Generate biome art, then regenerate.");
				return false;
			}
			const float Bearing = 0.6f, Module = 4f;
			float from = inner - Bearing, to = inner + width + Bearing;
			var a = new Vector3(centre.x + dir.x * from, 0f, centre.z + dir.y * from);
			var b = new Vector3(centre.x + dir.x * to, 0f, centre.z + dir.y * to);
			a.y = context.GroundAt(a.x, a.z);
			b.y = context.GroundAt(b.x, b.z);
			float length = to - from;
			int count = Mathf.Max(1, Mathf.CeilToInt(length / Module - 1e-3f));
			float module = length / count;
			Vector3 along = b - a;
			Quaternion turn = Quaternion.LookRotation(along.normalized, Vector3.up);
			for (int k = 0; k < count; k++)
			{
				Vector3 at = Vector3.Lerp(a, b, (k + 0.5f) / count);
				context.AddProp(span, at, turn, new Vector3(Mathf.Max(0.8f, stature), 1f, module / Module));
			}
			return true;
		}

		private static void AddWallStairs(PointOfInterestSiteContext context, List<(Vector3 at, Quaternion turn)> curtain, List<Vector3> gateways,
			List<(Vector3 at, Quaternion turn, Vector2 size, PointOfInterestSlotRole role, string tag)> grounds)
		{
			if (curtain.Count < 4)
			{
				return;
			}
			GameObject stair = StructureKit.Prefab("WallStair", 0);
			if (stair == null)
			{
				return;
			}
			StructurePiece piece = StructurePieces.Find("WallStair");
			Vector2 size = piece != null ? piece.Footprint : new Vector2(7.5f, 1.5f);
			Vector3 centre = context.Record.Position;
			float Bearing(Vector3 p) => Mathf.Atan2(p.x - centre.x, p.z - centre.z) * Mathf.Rad2Deg;
			var wanted = new List<float>();
			foreach (Vector3 g in gateways)
			{
				wanted.Add(Bearing(g) + 360f / curtain.Count * 2.2f);
			}
			wanted.Add((gateways.Count > 0 ? Bearing(gateways[0]) : context.Record.Yaw) + 180f);
			var used = new HashSet<int>();
			foreach (float bearing in wanted)
			{
				int best = -1;
				float bestGap = float.MaxValue;
				for (int i = 0; i < curtain.Count; i++)
				{
					float gap = Mathf.Abs(Mathf.DeltaAngle(Bearing(curtain[i].at), bearing));
					if (!used.Contains(i) && gap < bestGap)
					{
						bestGap = gap;
						best = i;
					}
				}
				if (best < 0)
				{
					continue;
				}
				// Its back to the wall's inner face (the wall faces out along its −z).
				(Vector3 at, Quaternion turn) wall = curtain[best];
				Vector3 foot = wall.at + wall.turn * new Vector3(0f, 0f, 1.08f);
				Vector3 middle = foot + wall.turn * new Vector3(0f, 0f, size.y * 0.5f);
				bool clear = true;
				foreach ((Vector3 at, Quaternion turn, Vector2 footprint, PointOfInterestSlotRole role, string _) in grounds)
				{
					if (role == PointOfInterestSlotRole.Perimeter || role == PointOfInterestSlotRole.Gate || role == PointOfInterestSlotRole.WallTower)
					{
						continue;
					}
					if (PointOfInterestLayouts.Overlaps(new Vector2(middle.x, middle.z), wall.turn.eulerAngles.y, size, new Vector2(at.x, at.z), turn.eulerAngles.y, footprint))
					{
						clear = false;
						break;
					}
				}
				if (!clear)
				{
					continue;
				}
				used.Add(best);
				context.AddProp(stair, new Vector3(foot.x, wall.at.y, foot.z), wall.turn, Vector3.one);
			}
		}

		/// <summary>How far over the highest water a land prop's ground must stand, metres (PointOfInterestSiteContext.IsWet).</summary>
		private const float ClutterFreeboard = 0.1f;

		private static void AddClutter(PointOfInterestSiteContext context,
			List<(Vector3 at, Quaternion turn, Vector2 size, PointOfInterestSlotRole role, string tag)> grounds,
			PointOfInterestLayoutRequest request, DeterministicRNG random, float stature, BiomeTemplate siteBiome)
		{
			PointOfInterestRecord record = context.Record;
			PointOfInterestKindInfo info = PointOfInterestKinds.Info(record.Kind);
			if (info.Group == PointOfInterestGroup.Ruins || info.Group == PointOfInterestGroup.Dead || info.Has(PointOfInterestTraits.Underwater))
			{
				return;
			}
			bool town = info.Has(PointOfInterestTraits.Settlement) || info.Group == PointOfInterestGroup.Settlement;
			bool camp = !town && (info.Group == PointOfInterestGroup.Wild || record.Kind == POIType.BossLair || record.Kind == POIType.PirateCove
				|| record.Kind == POIType.SmugglersCove || record.Kind == POIType.Oasis);
			if (!town && !camp)
			{
				return;
			}
			// What may not be stood on: every laid piece's footprint, and what has been placed since.
			var taken = new List<(Vector2 centre, float yaw, Vector2 size)>();
			foreach ((Vector3 at, Quaternion turn, Vector2 size, PointOfInterestSlotRole _, string _) in grounds)
			{
				taken.Add((new Vector2(at.x, at.z), turn.eulerAngles.y, size + Vector2.one * 0.6f));
			}
			var streets = new List<(Vector2 a, Vector2 b, float half)>();
			Quaternion siteTurn = Quaternion.Euler(0f, record.Yaw, 0f);
			var origin = new Vector2(record.Position.x, record.Position.z);
			foreach (PointOfInterestLayoutStreet street in request.Streets)
			{
				/* Each straight stretch of the street's line, world xz: the curve drawn as the fewest chords within a metre of it,
				 * so the lights along a street (and the wall lights' spans) see stretches long enough to be lit. */
				List<Vector2> chords = Simplified(street.Points, 1f);
				for (int i = 1; i < chords.Count; i++)
				{
					Vector2 p = chords[i - 1], q = chords[i];
					Vector3 a = siteTurn * new Vector3(p.x, 0f, p.y);
					Vector3 b = siteTurn * new Vector3(q.x, 0f, q.y);
					streets.Add((origin + new Vector2(a.x, a.z), origin + new Vector2(b.x, b.z), street.Width * 0.5f));
				}
			}
			bool OnStreet(Vector2 p, float pad)
			{
				foreach ((Vector2 a, Vector2 b, float half) in streets)
				{
					Vector2 ab = b - a;
					float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Mathf.Max(1e-4f, ab.sqrMagnitude));
					if (Vector2.Distance(a + ab * t, p) < half + pad)
					{
						return true;
					}
				}
				return false;
			}
			int placed = 0;
			bool Put(string query, Vector3 at, float yaw)
			{
				var pieceRequest = new PointOfInterestPieceRequest(query, "timber", (int)(random.NextDouble() * 1000000));
				GameObject prefab = PointOfInterestPieceResolver.Resolve(pieceRequest);
				if (prefab == null)
				{
					return false;
				}
				Vector2 size = PointOfInterestPieceResolver.Measure(pieceRequest).Footprint * stature;
				var c = new Vector2(at.x, at.z);
				if (OnStreet(c, Mathf.Max(size.x, size.y) * 0.5f))
				{
					return false;
				}
				foreach ((Vector2 centre, float takenYaw, Vector2 takenSize) in taken)
				{
					if (PointOfInterestLayouts.Overlaps(c, yaw, size, centre, takenYaw, takenSize))
					{
						return false;
					}
				}
				// Nor in water: the sea is no kind on the water grid, so the grid alone stood crates in the surf (context.IsWet).
				if (context.Water != null && context.Water.KindAt(at.x, at.z) != WaterKind.None || context.IsWet(at.x, at.z, ClutterFreeboard))
				{
					return false;
				}
				taken.Add((c, yaw, size + Vector2.one * 0.3f));
				context.AddProp(prefab, new Vector3(at.x, context.GroundAt(at.x, at.z), at.z), Quaternion.Euler(0f, yaw, 0f), Vector3.one * stature);
				placed++;
				return true;
			}
			string Pick(params string[] choices) => choices[(int)(random.NextDouble() * choices.Length) % choices.Length];

			foreach ((Vector3 at, Quaternion turn, Vector2 size, PointOfInterestSlotRole role, string tag) in grounds)
			{
				if (role == PointOfInterestSlotRole.Perimeter || role == PointOfInterestSlotRole.Gate || role == PointOfInterestSlotRole.WallTower
					|| role == PointOfInterestSlotRole.Span || role == PointOfInterestSlotRole.Pier)
				{
					continue;
				}
				float width = size.x, depth = size.y;
				float yaw = turn.eulerAngles.y;
				Vector3 front = turn * Vector3.back, right = turn * Vector3.right;
				if (town && tag == PointOfInterestTemplateSpecs.Church)
				{
					// The churchyard: headstones in rows behind and beside the church, the odd plain mound among them, all
					// facing the same way as the church; rows break where the ground is taken or a street runs.
					int rows = 2 + (int)(random.NextDouble() * 2.0);
					int across = 3 + (int)(random.NextDouble() * 3.0);
					for (int r = 0; r < rows; r++)
					{
						for (int c = 0; c < across; c++)
						{
							if (random.NextDouble() < 0.15)
							{
								continue; // a gap: an old grave gone, or room not yet used
							}
							float x = (c - (across - 1) * 0.5f) * 1.7f + (float)(random.NextDouble() - 0.5) * 0.3f;
							float z = depth * 0.5f + 2.2f + r * 2.3f;
							float lean = (float)(random.NextDouble() - 0.5) * 8f;
							Put(random.NextDouble() < 0.15 ? "=GraveMound" : "=Gravestone", at - front * z + right * x, yaw + lean);
						}
					}
					// And a few along the church's sunny side.
					float sideways = random.NextDouble() < 0.5 ? -1f : 1f;
					for (int g = 0; g < 3; g++)
					{
						Put("=Gravestone", at + right * sideways * (width * 0.5f + 2f) + front * ((g - 1) * 2.4f), yaw + (float)(random.NextDouble() - 0.5) * 8f);
					}
					continue;
				}
				if (town && tag == PointOfInterestTemplateSpecs.Temple)
				{
					// A temple's court is kept clear: no woodpiles or washing against its columns.
					continue;
				}
				if (town && Mathf.Max(width, depth) >= 3f)
				{
					// By the door.
					if (random.NextDouble() < 0.7)
					{
						float along = (random.NextDouble() < 0.5 ? -1f : 1f) * Mathf.Max(0.6f, width * 0.5f - 0.7f);
						Put(Pick("=WaterButt", "=Bench", "=LanternPost"), at + front * (depth * 0.5f + 0.55f) + right * along, yaw);
					}
					// Against a side wall.
					if (random.NextDouble() < 0.6)
					{
						float side = random.NextDouble() < 0.5 ? -1f : 1f;
						Put(Pick("=Woodpile", "=WaterButt", "=HayBales"), at + right * side * (width * 0.5f + 0.5f) - front * (float)(random.NextDouble() - 0.5) * depth * 0.6f, yaw + 90f);
					}
					// The back yard.
					if (random.NextDouble() < 0.45)
					{
						Put(Pick("=Cart", "=HayBales", "=WashingLine", "=Woodpile"), at - front * (depth * 0.5f + 2.5f) + right * (float)(random.NextDouble() - 0.5) * width, yaw + (float)(random.NextDouble() - 0.5) * 30f);
					}
				}
				else if (camp && Mathf.Max(width, depth) >= 1.8f && random.NextDouble() < 0.6)
				{
					bool hostile = info.Has(PointOfInterestTraits.Hostile);
					float a = (float)(random.NextDouble() * Math.PI * 2.0);
					var offset = new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a)) * (Mathf.Max(width, depth) * 0.5f + 1.4f);
					Put(hostile ? Pick("=WeaponRack", "=DryingRack", "=WaterButt", "=Woodpile") : Pick("=DryingRack", "=WaterButt", "=Woodpile"), at + offset, a * Mathf.Rad2Deg + 90f);
				}
			}
			// Lights along the streets, either side in turn (2026-10-12: "We need torches, light posts, braziers"): the light and
			// spacing the site biome's template gives the kind (its Way Lights, 2026-10-16), authored from the old rule by kind,
			// a town's iron lanterns every dozen paces or so and a village's torches on poles every twenty.
			// A street light stands on the ground: a wall light named for a kind (a hanging bracket) leaves its streets unlit.
			if (town && BiomeTemplateArt.WayLight(siteBiome, record.Kind, out BiomeWayLight light) && BiomeWayLights.MountOf(light.Light) == BiomeWayLightMount.Ground)
			{
				foreach ((Vector2 a, Vector2 b, float half) in streets)
				{
					Vector2 ab = b - a;
					float length = ab.magnitude;
					if (length < 10f)
					{
						continue;
					}
					Vector2 along = ab / length, across = new Vector2(-along.y, along.x);
					int side = 1;
					for (float t = 6f; t < length - 6f; t += light.Spacing, side = -side)
					{
						Vector2 p = a + along * t + across * side * (half + 0.7f);
						float yaw = Mathf.Atan2(-across.x * side, -across.y * side) * Mathf.Rad2Deg;
						var pieceRequest = new PointOfInterestPieceRequest(light.PieceQuery, "timber", 0);
						GameObject prefab = PointOfInterestPieceResolver.Resolve(pieceRequest);
						if (prefab != null && !taken.Exists(x => Vector2.Distance(x.centre, p) < Mathf.Max(x.size.x, x.size.y) * 0.5f + 0.5f)
							&& !context.IsWet(p.x, p.y, ClutterFreeboard))
						{
							context.AddProp(prefab, new Vector3(p.x, context.GroundAt(p.x, p.y), p.y), Quaternion.Euler(0f, yaw, 0f), Vector3.one * stature);
							taken.Add((p, yaw, new Vector2(0.4f, 0.8f)));
							placed++;
						}
					}
				}
			}
			// A wall light named for the kind (2026-10-18, I6g: a torch in a sconce, a lantern on a bracket): hung on the street-facing
			// walls of the buildings laid along the streets at the light's spacing, a post of its kind where no wall is near
			// (PointOfInterestWallLights). After the buildings and the clutter; each street's variant of the light (and of its post)
			// drawn from a stream of the pass's own (PointOfInterestWallLights.StreetSeeds), so the site's stream is never drawn on.
			else if (town && BiomeTemplateArt.WayLight(siteBiome, record.Kind, out light) && BiomeWayLights.MountOf(light.Light) == BiomeWayLightMount.Wall)
			{
				string postQuery = BiomeWayLights.PieceQuery(BiomeWayLights.PostFor(light.Light));
				int[] streetSeeds = PointOfInterestWallLights.StreetSeeds(record.Id, streets.Count);
				var hungOf = new GameObject[streets.Count];
				var postOf = new GameObject[streets.Count];
				bool artMissing = false;
				for (int s = 0; s < streets.Count; s++)
				{
					hungOf[s] = PointOfInterestPieceResolver.Resolve(new PointOfInterestPieceRequest(light.PieceQuery, "timber", streetSeeds[s]));
					postOf[s] = PointOfInterestPieceResolver.Resolve(new PointOfInterestPieceRequest(postQuery, "timber", streetSeeds[s]));
					artMissing |= hungOf[s] == null;
				}
				List<PointOfInterestWallLights.WallSpan> walls = PointOfInterestWallLights.StreetWalls(context.Laid);
				int onWalls = 0, posts = 0;
				foreach (PointOfInterestWallLights.WallLight spot in PointOfInterestWallLights.Plan(streets, walls, light.Spacing, PointOfInterestWallLights.Reach * stature))
				{
					GameObject hung = hungOf[spot.Street], post = postOf[spot.Street];
					if (spot.OnWall)
					{
						if (hung != null)
						{
							context.AddProp(hung, new Vector3(spot.At.x, spot.Height, spot.At.y), Quaternion.Euler(0f, spot.Yaw, 0f), Vector3.one * stature);
							onWalls++;
							placed++;
						}
					}
					else if (post != null && !taken.Exists(x => Vector2.Distance(x.centre, spot.At) < Mathf.Max(x.size.x, x.size.y) * 0.5f + 0.5f)
						&& !context.IsWet(spot.At.x, spot.At.y, ClutterFreeboard))
					{
						context.AddProp(post, new Vector3(spot.At.x, context.GroundAt(spot.At.x, spot.At.y), spot.At.y), Quaternion.Euler(0f, spot.Yaw, 0f), Vector3.one * stature);
						taken.Add((spot.At, spot.Yaw, new Vector2(0.4f, 0.8f)));
						posts++;
						placed++;
					}
				}
				if (artMissing && walls.Count > 0)
				{
					PointOfInterestFeatureKit.Note(context, $"street lights: no art for the wall light {light.Light} yet (Generate Biome Art)");
				}
				context.Notes?.Add($"{record.Name}: {onWalls} {light.Light} street light(s) hung on walls, {posts} {BiomeWayLights.PostFor(light.Light)} post(s) where no wall was near.");
			}
		}

		public override void Build(PointOfInterestSiteContext context)
		{
			PointOfInterestTemplate template = context.Template;
			if (template == null || template.Pieces == null || template.Pieces.Count == 0)
			{
				return;
			}
			DeterministicRNG random = context.Random;
			PointOfInterestRecord record = context.Record;
			bool allowLocal = context.Scene.IsValid() && LocalArtScope.IsLocalScenePath(context.Scene.path);
			/* The site's biome decides how the weather finishes its pieces and what its buildings are made of: its template's
			 * weathering and structure palette (2026-10-16), no longer keywords in its name. */
			BiomeTemplate siteBiome = context.BiomeAt?.Invoke(record.Position.x, record.Position.z);
			BiomeWeathering weathering = BiomeTemplateArt.Weathering(siteBiome);
			BiomeStructurePalette palette = BiomeTemplateArt.StructurePalette(siteBiome);

			PointOfInterestPad? sitePad = context.Points != null ? SiteEntrances.PadOf(context.Points.Pads, record.Id) : null;
			var request = new PointOfInterestLayoutRequest
			{
				Layout = template.Layout,
				Radius = record.Radius,
				Gap = Gap,
				// The site's own outline and terraces (one footprint, PointOfInterestOutline), its moat's inner bank, and its kind.
				Outline = record.Outline ?? Array.Empty<float>(),
				Yaw = record.Yaw,
				Pad = sitePad,
				Moat = context.Points != null && MoatShaper.TryMoat(context.Points.Shapes, record.Id, out PointOfInterestShape moat) ? moat : null,
				Kind = record.Kind,
				Seed = (int)(PointOfInterestPlanner.Mix(unchecked((uint)record.SiteSeed ^ 0x70A115u)) & 0x7FFFFFFFu),
			};
			if (Clearings != null)
			{
				request.Clearings.AddRange(Clearings);
			}
			int slots = template.Pieces.Count;
			var styles = new string[slots];
			// The site's race's stature: its buildings (and what stands round them) are built to it, a giant's hall two and a
			// half times a human's, a halfling's cottage three fifths (RaceStature). A big race's site keeps its ground, so it
			// holds fewer, bigger buildings.
			float stature = RaceStature.BuildingScale(record.Race);
			for (int s = 0; s < slots; s++)
			{
				PointOfInterestPieceSlot slot = template.Pieces[s];
				if (slot == null)
				{
					request.Slots.Add(new PointOfInterestLayoutSlot { Role = PointOfInterestSlotRole.Scatter });
					continue;
				}
				styles[s] = string.IsNullOrWhiteSpace(slot.Style) ? context.Style : slot.Style;
				PointOfInterestSlotRole role = PointOfInterestLayouts.RoleFor(template.Layout, s, slot.Role);
				int count = CountFor(slot, record.SizeClass, CountsFollowSize, random);
				if (stature > 1.05f && count > 0)
				{
					count = Mathf.Max(1, Mathf.RoundToInt(count / (stature * stature)));
				}
				PointOfInterestPieceMetrics metrics = PointOfInterestPieceResolver.Measure(new PointOfInterestPieceRequest(slot.Tag, styles[s], 0));
				// Laid out by what it sweeps as well as what it is (a tower mill's sails), seated by what it is.
				request.Slots.Add(new PointOfInterestLayoutSlot
				{
					Role = role,
					Footprint = Vector2.Max(metrics.Footprint, metrics.Sweep),
					Module = metrics.Module,
					Count = count,
					Landmark = slot.Tag == PointOfInterestTemplateSpecs.Church || slot.Tag == PointOfInterestTemplateSpecs.Temple,
				});
			}
			var draws = new List<Draws>();
			for (int s = 0; s < slots; s++)
			{
				PointOfInterestPieceSlot slot = template.Pieces[s];
				if (slot == null || PointOfInterestLayouts.Generates(request.Slots[s].Role))
				{
					continue;
				}
				for (int k = 0; k < request.Slots[s].Count; k++)
				{
					Draws d = Take(random, slot, false);
					d.Scale *= stature;
					PointOfInterestPieceMetrics metrics = PointOfInterestPieceResolver.Measure(new PointOfInterestPieceRequest(slot.Tag, styles[s], d.Seed, d.Decay));
					request.Items.Add(new PointOfInterestLayoutItem { Slot = s, Footprint = Vector2.Max(metrics.Footprint, metrics.Sweep) * d.Scale });
					draws.Add(d);
				}
			}
			float pierDeck = record.Position.y;
			Crossing crossing = default;
			if (request.Slots.Exists(s => s.Role == PointOfInterestSlotRole.Span))
			{
				crossing = MeasureCrossing(context);
				if (crossing.Unfooted)
				{
					context.Notes?.Add($"{record.Name}: the ground falls away from the water along the bridge's heading, so one end comes down on the highest ground beside the water rather than on a bank.");
				}
				request.SpanFrom = crossing.From;
				request.SpanTo = crossing.To;
				request.SpanHalf = Mathf.Max(-crossing.From, crossing.To);
			}
			if (request.Slots.Exists(s => s.Role == PointOfInterestSlotRole.Pier))
			{
				request.ShoreAhead = MeasureShore(context, out pierDeck, out request.PierFrom, out request.PierTo);
			}

			// The ways that come in: a walled site opens its gates toward them.
			if (context.Points != null && context.Points.Paths != null)
			{
				foreach (ScenePath path in context.Points.Paths)
				{
					if (path == null || path.Class == ScenePathClass.Street)
					{
						continue;
					}
					if (path.FromId == record.Id && !float.IsNaN(path.FromEntrance) && !request.Entrances.Contains(path.FromEntrance))
					{
						request.Entrances.Add(path.FromEntrance);
					}
					if (path.ToId == record.Id && !float.IsNaN(path.ToEntrance) && !request.Entrances.Contains(path.ToEntrance))
					{
						request.Entrances.Add(path.ToEntrance);
					}
				}
				request.Entrances.Sort();
				// Where each way ends inside, by the rule its planner used: the street in from its entrance passes through it.
				foreach (float entrance in request.Entrances)
				{
					request.EntranceDepths.Add(SiteEntrances.EndDistance(record, sitePad, entrance));
				}
			}

			List<PointOfInterestLayoutPiece> pieces = PointOfInterestLayouts.Place(request, random);
			AddStreets(context, request);
			var grounds = new List<(Vector3 at, Quaternion turn, Vector2 size, PointOfInterestSlotRole role, string tag)>();
			// The stone curtain's modules (their walk is climbable) and the gates, for the wall stairs.
			var curtain = new List<(Vector3 at, Quaternion turn)>();
			var gateways = new List<Vector3>();
			int laid = 0, missing = 0, ruined = 0, wet = 0;
			var ringSeeds = new Dictionary<int, int>();
			bool dryOnly = KeepOutOfWater && !PointOfInterestKinds.Info(record.Kind).Has(PointOfInterestTraits.Underwater);
			float sea = dryOnly ? SeaLevelOf(context) : float.NegativeInfinity;
			// A gorge is crossed by one piece rim to rim (a stone arch, or ropes where it is wide or wild), not plank spans
			// with nothing under them; the spans stand in only until that art is generated.
			bool gorgeLaid = crossing.Gorge && LayGorgeBridge(context, crossing, allowLocal, grounds);
			if (gorgeLaid)
			{
				laid++;
			}
			foreach (PointOfInterestLayoutPiece piece in pieces)
			{
				if (gorgeLaid && piece.Role == PointOfInterestSlotRole.Span)
				{
					continue;
				}
				PointOfInterestPieceSlot slot = template.Pieces[piece.Slot];
				Draws d = piece.Item >= 0 ? draws[piece.Item] : Take(random, slot, true);
				if (piece.Role == PointOfInterestSlotRole.Perimeter)
				{
					/* One piece and variant for the whole ring: each module drew its own, so a stone curtain changed part way round
					 * from the wall-walk to a coped field wall with no walk, and a palisade from straight logs to ragged ones that
					 * did not meet (2026-10-18). Its decay and finish stay each module's own. */
					if (ringSeeds.TryGetValue(piece.Slot, out int ringSeed))
					{
						d.Seed = ringSeed;
					}
					else
					{
						ringSeeds[piece.Slot] = d.Seed;
					}
				}
				if (d.LeaveOut < d.Decay * 0.5f)
				{
					ruined++;
					continue;
				}
				Vector3 at = context.OnGround(piece.Offset);
				bool deck = piece.Role == PointOfInterestSlotRole.Span || piece.Role == PointOfInterestSlotRole.Pier;
				if (dryOnly && !deck && !StandsInWater(slot.Tag) && WaterLevelAt(context, at.x, at.z, sea) > at.y + 0.25f)
				{
					wet++;
					continue;
				}
				StructureFinish finish = FinishFor(Finish, FinishChance, record.Kind, weathering, d.Decay, d.FinishRoll);
				var pieceRequest = new PointOfInterestPieceRequest(slot.Tag, styles[piece.Slot], d.Seed, d.Decay, finish, allowLocal) { Palette = palette };
				GameObject prefab = PointOfInterestPieceResolver.Resolve(pieceRequest);
				if (prefab == null)
				{
					missing++;
					continue;
				}
				if (piece.Role == PointOfInterestSlotRole.Perimeter)
				{
					prefab = CurtainVariant(prefab, pieceRequest);
				}
				PointOfInterestPieceMetrics metrics = PointOfInterestPieceResolver.Measure(pieceRequest);
				Quaternion pitch = Quaternion.identity;
				if (piece.Role == PointOfInterestSlotRole.Span)
				{
					// On the deck's profile, pitched to its own stretch of it: its two ends on the profile, so the modules
					// meet end to end, level over the water and ramping down to each bank.
					float half = piece.Footprint.y * 0.5f;
					float near = crossing.DeckAt(piece.Offset.y - half), far = crossing.DeckAt(piece.Offset.y + half);
					at.y = 0.5f * (near + far);
					pitch = Quaternion.Euler(-Mathf.Atan2(far - near, 2f * half) * Mathf.Rad2Deg, 0f, 0f);
					// Its trestle only where the legs reach the ground or the bed and have room to stand there.
					float trestle = piece.Offset.y - half + TrestleInset;
					prefab = SpanVariant(prefab, pieceRequest, crossing.DeckAt(trestle) - context.OnGround(new Vector2(piece.Offset.x, trestle)).y);
				}
				else if (piece.Role == PointOfInterestSlotRole.Pier)
				{
					at.y = pierDeck;
				}
				else
				{
					at.y = SeatHeight(context, piece.Offset, piece.Yaw, metrics.Footprint * d.Scale, metrics.Depth * d.Scale, metrics.WaterFront) - d.Decay * MaxSinkMetres;
				}
				float tilt = deck ? 0f : d.Decay * MaxTiltDegrees;
				Quaternion rotation = Quaternion.Euler(d.TiltX * tilt, record.Yaw + piece.Yaw, d.TiltZ * tilt) * pitch;
				Vector3 scale = Vector3.one * d.Scale;
				if (piece.Role == PointOfInterestSlotRole.Span)
				{
					// A span is fitted end to end: its own size, stretched along the deck to the module the layout gave it.
					float module = metrics.Module > 0f ? metrics.Module : Mathf.Max(0.01f, metrics.Footprint.y);
					scale = new Vector3(1f, 1f, piece.Footprint.y / module);
				}
				else if (piece.Role == PointOfInterestSlotRole.Perimeter && metrics.Module > 0f)
				{
					// A wall module is fitted to its chord of the ring (and run on past a corner to meet its neighbour's face).
					scale = new Vector3(piece.Footprint.x / metrics.Module, 1f, 1f);
				}
				context.AddProp(prefab, at, rotation, scale);
				grounds.Add((at, Quaternion.Euler(0f, record.Yaw + piece.Yaw, 0f), metrics.Footprint * d.Scale, piece.Role, slot.Tag));
				if (piece.Role == PointOfInterestSlotRole.Perimeter && prefab.name.StartsWith("Structure_WallSegment_0", StringComparison.Ordinal))
				{
					curtain.Add((at, Quaternion.Euler(0f, record.Yaw + piece.Yaw, 0f)));
				}
				else if (piece.Role == PointOfInterestSlotRole.Gate)
				{
					gateways.Add(at);
				}
				laid++;
			}
			AddYards(context, grounds);
			AddClutter(context, grounds, request, random, stature, siteBiome);
			AddWallStairs(context, curtain, gateways, grounds);
			AddDrawbridges(context, gateways, stature);
			AddMoatBridges(context, gateways, stature);
			int dropped = request.Items.Count;
			foreach (PointOfInterestLayoutPiece piece in pieces)
			{
				if (piece.Item >= 0)
				{
					dropped--;
				}
			}
			if (missing > 0)
			{
				context.Notes?.Add($"{record.Name}: {missing} piece(s) of '{template.name}' have no art yet (laid {laid}, {ruined} left out as ruin).");
			}
			if (dropped > 0)
			{
				context.Notes?.Add($"{record.Name}: {dropped} piece(s) of '{template.name}' found no room in its {record.Radius:0} m footprint.");
			}
			if (wet > 0)
			{
				context.Notes?.Add($"{record.Name}: {wet} piece(s) of '{template.name}' left out where its footprint is under water.");
			}
		}

		/// <summary>The longest gorge thrown over with a stone arch, lip to lip, metres: wider is crossed on ropes.</summary>
		public const float MaxArchSpan = 32f;

		/// <summary>
		/// How far from any settlement a gorge is the wild's (crossed on ropes by whoever goes that way, not arched by a town
		/// that keeps it), metres.
		/// </summary>
		public const float WildGorgeMetres = 600f;

		/// <summary>
		/// Lays a gorge's crossing as one piece from lip to lip (StructurePieces.Bridges): the stone arch nearest its length,
		/// stretched to it, where a settlement is within <see cref="WildGorgeMetres"/>, the gorge no wider than
		/// <see cref="MaxArchSpan"/> and its rims close enough in height for a level deck (<see cref="TryArchDeck"/>); the
		/// nearest rope bridge otherwise (<see cref="GorgeBridgeFor(Func{float, float}, in Crossing, bool, out float, out ArchDeck)"/>).
		/// An arch's deck is level or gently graded, its ends on the approaches the cut built up and dressed into the rims
		/// (<see cref="CarveArchApproaches"/>); a rope bridge's anchor posts stand on the rims as they are and it hangs
		/// pitched along the line between. False for a wild cleft too narrow for ropes, or when the art is not generated yet:
		/// the plank spans are laid instead.
		/// </summary>
		private static bool LayGorgeBridge(PointOfInterestSiteContext context, Crossing crossing, bool allowLocal,
			List<(Vector3 at, Quaternion turn, Vector2 size, PointOfInterestSlotRole role, string tag)> grounds)
		{
			PointOfInterestRecord record = context.Record;
			float length = crossing.To - crossing.From;
			if (length <= 0.5f)
			{
				return false;
			}
			bool kept = KeptBySettlement(context.Points != null ? context.Points.Points : null, record.Position);
			string id = GorgeBridgeFor(t => GroundAlong(record, context.GroundAt, t), crossing, kept, out float nominal, out ArchDeck arch);
			if (arch.Valid && !arch.Settled)
			{
				// The approaches were never built into the ground (it was not cut with this plan): an arch would stand a step
				// above one rim or below the other, so the gorge is hung with ropes, which take its rake as it is.
				context.Notes?.Add($"{record.Name}: the gorge's rims are not yet built up and cut to a stone arch's level deck ({arch.Fill:0.0} m fill, {arch.Cut:0.0} m cut wanted), so it is crossed on ropes; re-cut the scene for the arch.");
				id = GorgeBridgeFor(length, false, out nominal);
				arch = default;
			}
			StructurePiece piece = id != null ? StructurePieces.Find(id) : null;
			if (piece == null)
			{
				return false;
			}
			int variant = StructureKit.PickVariant(piece, record.SiteSeed);
			GameObject prefab = StructureKit.Prefab(id, variant, StructureFinish.None, allowLocal);
			if (prefab == null)
			{
				return false;
			}
			float middle = 0.5f * (crossing.From + crossing.To);
			Vector3 at = context.OnGround(new Vector2(0f, middle));
			// An arch at its own level deck; ropes along the line from rim to rim.
			float from = arch.Valid ? arch.DeckFrom : crossing.DeckFrom, to = arch.Valid ? arch.DeckTo : crossing.DeckTo;
			at.y = arch.Valid ? arch.DeckAt(middle) : crossing.DeckAt(middle);
			float pitch = Mathf.Atan2(to - from, length) * Mathf.Rad2Deg;
			Quaternion turn = Quaternion.Euler(0f, record.Yaw, 0f);
			context.AddProp(prefab, at, turn * Quaternion.Euler(-pitch, 0f, 0f), new Vector3(1f, 1f, length / nominal));
			grounds.Add((at, turn, new Vector2(piece.Footprint.x, length), PointOfInterestSlotRole.Span, "gorge"));
			return true;
		}

		/// <summary>The narrowest gorge hung with a rope bridge, metres: a cleft narrower is crossed on planks laid rim to rim.</summary>
		public const float MinRopeSpan = 10f;

		/// <summary>
		/// The kit's gorge crossing for a gorge <paramref name="length"/> metres lip to lip, and that piece's own length: the
		/// stone arch whose length is nearest it (the least stretch) when the gorge is <paramref name="kept"/> by a settlement
		/// near and no wider than <see cref="MaxArchSpan"/>; else the nearest rope bridge; null for a wild cleft under
		/// <see cref="MinRopeSpan"/>, which is crossed on plank spans.
		/// </summary>
		public static string GorgeBridgeFor(float length, bool kept, out float nominal)
		{
			bool arch = kept && length <= MaxArchSpan;
			if (!arch && length < MinRopeSpan)
			{
				nominal = 0f;
				return null;
			}
			float[] lengths = arch ? StructurePieces.ArchBridgeLengths : StructurePieces.RopeBridgeLengths;
			nominal = lengths[0];
			foreach (float l in lengths)
			{
				if (Mathf.Abs(Mathf.Log(length / l)) < Mathf.Abs(Mathf.Log(length / nominal)))
				{
					nominal = l;
				}
			}
			return arch ? StructurePieces.ArchBridgeId(nominal) : StructurePieces.RopeBridgeId(nominal);
		}

		/// <summary>
		/// The kit's gorge crossing for a gorge measured along its heading (<paramref name="groundAlong"/>, the crossing
		/// <see cref="TryGorgeCrossing"/> found): <see cref="GorgeBridgeFor(float, bool, out float)"/>'s, but a stone arch
		/// only where its deck can be laid level and its approaches built up and cut to it (<see cref="TryArchDeck"/>, given
		/// in <paramref name="arch"/>); a rake too steep for that is hung with ropes, which may slope, or crossed on planks
		/// where it is too narrow for them.
		/// </summary>
		public static string GorgeBridgeFor(Func<float, float> groundAlong, in Crossing crossing, bool kept, out float nominal, out ArchDeck arch)
		{
			arch = default;
			float length = crossing.To - crossing.From;
			string id = GorgeBridgeFor(length, kept, out nominal);
			if (id == null || !id.StartsWith("ArchBridge", StringComparison.Ordinal))
			{
				return id;
			}
			if (TryArchDeck(groundAlong, crossing, nominal, out arch))
			{
				return id;
			}
			return GorgeBridgeFor(length, false, out nominal);
		}

		/// <summary>True when a settlement (a town, a village, a city…) stands within <see cref="WildGorgeMetres"/> of a point: a gorge there is kept and arched.</summary>
		public static bool KeptBySettlement(IReadOnlyList<PointOfInterestRecord> records, Vector3 at)
		{
			if (records == null)
			{
				return false;
			}
			const float Metres = WildGorgeMetres;
			foreach (PointOfInterestRecord other in records)
			{
				if (other != null && PathNetworkPlanner.RankOf(other.Kind) > 0
					&& new Vector2(other.Position.x - at.x, other.Position.z - at.z).sqrMagnitude <= Metres * Metres)
				{
					return true;
				}
			}
			return false;
		}

		/// <summary>The kit's bridge span whose first variant stands a trestle under its start; the second has none.</summary>
		private const string TrestledSpan = "BridgeSpan";

		/// <summary>How far below the deck the span's trestle reaches, and how far in from the module's start it stands, metres.</summary>
		private const float TrestleDepth = 4f, TrestleInset = 0.3f;

		/// <summary>
		/// The span with its trestle where the legs reach the ground or the river bed under them (<paramref name="drop"/>, the
		/// deck's height over it, between half a metre and the legs' length) and without one elsewhere. The variant was a
		/// seeded pick, so half the modules hung four metres of legs in the air under a high deck, or stood them in a bank.
		/// Any other piece, or art not generated yet, is returned as it is.
		/// </summary>
		private static GameObject SpanVariant(GameObject prefab, in PointOfInterestPieceRequest request, float drop)
		{
			if (prefab == null || !StructurePieces.TryParsePrefab(prefab.name, out StructurePiece kit, out int variant)
				|| kit.Id != TrestledSpan || kit.Variants < 2)
			{
				return prefab;
			}
			int wanted = drop >= 0.5f && drop <= TrestleDepth - 0.1f ? 0 : 1;
			if (wanted == variant)
			{
				return prefab;
			}
			GameObject other = StructureKit.Prefab(kit.Id, wanted, request.Finish, request.AllowLocal);
			if (other == null && request.Finish != StructureFinish.None)
			{
				other = StructureKit.Prefab(kit.Id, wanted, StructureFinish.None, request.AllowLocal);
			}
			return other != null ? other : prefab;
		}

		/// <summary>The walkable curtain's variant (StructurePieces: the crenellated wall with its walk) the wall stairs climb to.</summary>
		private const string CurtainPiece = "WallSegment";

		/// <summary>
		/// A curtain's module as the variant with the wall-walk: a ring of a walled site keeps its walk all the way round, the
		/// towers' upper doors and the wall stairs meeting it, rather than whatever variant the ring's seed drew (the coped
		/// field wall has none). Any other piece, or art not generated yet, is returned as it is.
		/// </summary>
		private static GameObject CurtainVariant(GameObject prefab, in PointOfInterestPieceRequest request)
		{
			if (prefab == null || !StructurePieces.TryParsePrefab(prefab.name, out StructurePiece kit, out int variant) || kit.Id != CurtainPiece || variant == 0)
			{
				return prefab;
			}
			GameObject walk = StructureKit.Prefab(kit.Id, 0, request.Finish, request.AllowLocal);
			if (walk == null && request.Finish != StructureFinish.None)
			{
				walk = StructureKit.Prefab(kit.Id, 0, StructureFinish.None, request.AllowLocal);
			}
			return walk != null ? walk : prefab;
		}

		/// <summary>Pieces built to stand in water: stilts, piers, bridges.</summary>
		private static bool StandsInWater(string tag)
			=> !string.IsNullOrEmpty(tag) && (tag.Contains("stilt") || tag.Contains("dock") || tag.Contains("bridge") || tag.Contains("Stilt") || tag.Contains("Pier") || tag.Contains("Bridge"));

		/// <summary>The water's surface over a point (river, lake, or the sea where the ground is below it); negative infinity on dry land.</summary>
		internal static float WaterLevelAt(PointOfInterestSiteContext context, float x, float z, float sea)
		{
			float level = context.Water != null ? context.Water.SurfaceAt(x, z) : float.NegativeInfinity;
			return Mathf.Max(level, sea);
		}

		/// <summary>A slot's count: its whole range, or the third of it the size class names (0 small … 2 large).</summary>
		public static int CountFor(PointOfInterestPieceSlot slot, int sizeClass, bool followSize, DeterministicRNG random)
		{
			int min = Math.Max(0, slot.MinCount), max = Math.Max(min, slot.MaxCount);
			if (followSize && max > min)
			{
				int size = Mathf.Clamp(sizeClass, 0, 2);
				float third = (max - min) / 3f;
				int lo = min + Mathf.RoundToInt(third * size), hi = min + Mathf.RoundToInt(third * (size + 1));
				min = Mathf.Clamp(lo, slot.MinCount, max);
				max = Mathf.Clamp(hi, min, max);
			}
			return random.Range(min, max + 1);
		}

		/// <summary>
		/// A piece's finish. Auto: algae on everything under water and char on half a battlefield, whatever the biome; else the
		/// biome's weathering (BiomeTemplate.Weathering, 2026-10-16: until then keywords in the biome's name): its finish on
		/// <see cref="BiomeWeathering.ChanceAt"/> of the pieces by their decay, and where that is moss, a long-abandoned site's
		/// (ruins, the dead) on at least 0.55 + 0.4 × decay. A forced finish is laid on <paramref name="chance"/> of the pieces.
		/// Pure.
		/// </summary>
		public static StructureFinish FinishFor(PointOfInterestFinish mode, float chance, POIType kind, BiomeWeathering weathering, float decay, float roll)
		{
			switch (mode)
			{
				case PointOfInterestFinish.None:
					return StructureFinish.None;
				case PointOfInterestFinish.Mossy:
					return roll < chance ? StructureFinish.Mossy : StructureFinish.None;
				case PointOfInterestFinish.Charred:
					return roll < chance ? StructureFinish.Charred : StructureFinish.None;
				case PointOfInterestFinish.Algae:
					return roll < chance ? StructureFinish.Algae : StructureFinish.None;
			}
			if (PointOfInterestKinds.Info(kind).Has(PointOfInterestTraits.Underwater))
			{
				return StructureFinish.Algae;
			}
			if (kind == POIType.Battlefield && roll < 0.5f)
			{
				return StructureFinish.Charred;
			}
			switch (weathering.Finish)
			{
				case BiomeStructureFinish.None:
					return StructureFinish.None;
				case BiomeStructureFinish.Algae:
					return roll < weathering.ChanceAt(decay) ? StructureFinish.Algae : StructureFinish.None;
				case BiomeStructureFinish.Charred:
					return roll < weathering.ChanceAt(decay) ? StructureFinish.Charred : StructureFinish.None;
			}
			float moss = weathering.ChanceAt(decay);
			PointOfInterestGroup group = PointOfInterestKinds.Info(kind).Group;
			if (group == PointOfInterestGroup.Ruins || group == PointOfInterestGroup.Dead)
			{
				// Long abandoned: the moss has had the stones for years (2026-10-11: ruins looked freshly built).
				moss = Mathf.Max(moss, 0.55f + 0.4f * decay);
			}
			return roll < moss ? StructureFinish.Mossy : StructureFinish.None;
		}

		/// <summary>
		/// Where a ground piece's origin goes: its centre's ground, lowered until its foundation (<paramref name="depth"/>)
		/// reaches the lowest corner, so a slope shows plinth, never a gap.
		/// </summary>
		/// <param name="landward">
		/// The piece's front stands in a river (<see cref="PointOfInterestPieceMetrics.WaterFront"/>): only its back corners
		/// count. Its front corners are in the channel, and lowering toward them sank a watermill a metre into the bank behind
		/// it, its race walls' depth letting it drop that far (2026-10-15: "Watermills seem to sink into the ground").
		/// </param>
		public static float SeatHeight(PointOfInterestSiteContext context, Vector2 offset, float yaw, Vector2 footprint, float depth, bool landward = false)
		{
			float centre = context.OnGround(offset).y;
			var corners = new Vector2[4];
			PointOfInterestLayouts.Corners(offset, yaw, footprint, corners);
			float lowest = centre;
			for (int i = 0; i < corners.Length; i++)
			{
				// Corners 0 and 3 are the piece's back (+z); its front faces −z.
				if (landward && i != 0 && i != 3)
				{
					continue;
				}
				lowest = Mathf.Min(lowest, context.OnGround(corners[i]).y);
			}
			return Mathf.Min(centre, lowest + Mathf.Max(0f, depth) * 0.9f);
		}

		/// <summary>
		/// A bridge's deck along its heading: where it starts and ends, and its height everywhere between. Level at the
		/// water's clearance over the water, ramping down each side onto that bank's ground at the end, never under the
		/// straight line from end to end. Each span module is pitched to its own stretch of it (rotation, not steps,
		/// 2026-10-11), so both ends sit on their banks whatever their heights and the middle clears the water.
		/// </summary>
		public struct Crossing
		{
			/// <summary>Metres along the heading (signed) of the start and the end.</summary>
			public float From, To;
			/// <summary>World height of the deck at the start and at the end: the ground there.</summary>
			public float DeckFrom, DeckTo;
			/// <summary>Metres along the heading where the water (or a dry channel's bed) begins and ends.</summary>
			public float WaterFrom, WaterTo;
			/// <summary>The deck's height over the water: its surface and the clearance. NaN for none (a plain straight deck).</summary>
			public float Level;
			/// <summary>How steeply each ramp comes down from <see cref="Level"/> to its end (rise per metre).</summary>
			public float RampFrom, RampTo;
			/// <summary>
			/// True when an end found no ground the deck could come down onto past the water (it falls away along the
			/// heading: a fall's lip, a river crossed aslant on a hillside), so it ends on the highest ground beside it.
			/// </summary>
			public bool Unfooted;
			/// <summary>True for a gorge's crossing (<see cref="TryGorgeCrossing"/>): one straight deck lip to lip, high over the floor.</summary>
			public bool Gorge;

			/// <summary>The deck's height at a distance along the heading.</summary>
			public float DeckAt(float along)
			{
				float line = To > From ? Mathf.Lerp(DeckFrom, DeckTo, Mathf.Clamp01((along - From) / (To - From))) : DeckFrom;
				if (float.IsNaN(Level))
				{
					return line;
				}
				float raised = along < WaterFrom ? Level - (WaterFrom - along) * RampFrom
					: along > WaterTo ? Level - (along - WaterTo) * RampTo
					: Level;
				return Mathf.Max(line, raised);
			}

			/// <summary>The deck's pitch between two distances along it, degrees, rising toward <see cref="To"/>.</summary>
			public float SlopeDegreesBetween(float a, float b) => b > a ? Mathf.Atan2(DeckAt(b) - DeckAt(a), b - a) * Mathf.Rad2Deg : 0f;

			/// <summary>The straight line's slope end to end, degrees.</summary>
			public float SlopeDegrees => To > From ? Mathf.Atan2(DeckTo - DeckFrom, To - From) * Mathf.Rad2Deg : 0f;
		}

		/// <summary>The steepest the deck's line runs from one end to the other between banks of different heights, degrees.</summary>
		public const float MaxDeckSlopeDegrees = 20f;

		/// <summary>The gentlest a ramp comes down from the deck over the water to a low bank (rise per metre): about 14°.</summary>
		public const float MinRampGrade = 0.25f;

		/// <summary>
		/// The steepest a ramp comes down to an end that found no bank (rise per metre): about <see cref="MaxDeckSlopeDegrees"/>.
		/// It starts back over the water to stay this gentle.
		/// </summary>
		public const float MaxRampGrade = 0.36f;

		/// <summary>Clearance the deck keeps over the water, metres.</summary>
		public const float DeckClearance = 0.6f;

		/// <summary>
		/// How far the water's surface along the heading may differ from the site's and still be the water the bridge
		/// crosses, metres: a river crossed aslant falls a little across the deck; the foot of a fall, or a lower reach seen
		/// past a bank, is other water.
		/// </summary>
		public const float SameWaterMetres = 1.5f;

		/// <summary>
		/// The bridge's line across the water at this site: from where each bank first stands clear of the water by
		/// <see cref="DeckClearance"/>, carried on up a steep channel wall to its lip (never along a gently rising bank), to
		/// the same on the other side; a bank too low ever to clear the water is met where the gentlest ramp down from the
		/// deck reaches it. Both ends sit on their ground; the deck over the water stands at the clearance and ramps down to
		/// each end (<see cref="Crossing"/>).
		/// </summary>
		/// <remarks>
		/// <para>
		/// History. It first stopped at the first dry ground and laid one flat deck at the site's own height: one end buried,
		/// one in the air. Then it ran each end out to the bank's top (2026-10-10), which on a lowland river's long, gentle
		/// banks ran the deck far over the grass (2026-10-11). Then one straight deck from end to end, which over a low bank
		/// had to stand that end up in the air to clear the water ("aligns on one side and stands on the other",
		/// 2026-10-11): the deck is a profile now, and each module takes its own pitch.
		/// </para>
		/// <para>
		/// Then "still floating on one end" (2026-10-12): the water was taken as any ground under the site's water
		/// level, so where the ground fell away past a bank (a fall's lip, a river crossed aslant down a hillside, a gorge
		/// beyond it) the "water" ran on to the edge of the reach, the deck held its level over all of it, and the layout,
		/// which lays no further than the reach, cut it off with its ramp: Flo Monolith's bridges ended 3, 6, 18 and 64 m
		/// over the ground. The water is now the scene's own, at this crossing's level, along the heading; no end lies past
		/// the reach; and an end that finds no ground to come down onto takes the highest ground beside the water.
		/// </para>
		/// <para>A dry channel (no water at the site) is measured from its bed.</para>
		/// </remarks>
		public static Crossing MeasureCrossing(PointOfInterestSiteContext context)
			=> MeasureCrossing(context.Record, context.GroundAt, context.Water);

		/// <summary>
		/// <see cref="MeasureCrossing(PointOfInterestSiteContext)"/> for a record on the ground (<paramref name="groundAt"/>,
		/// the record's own height where there is none) and the scene's water: the cut measures a bridge's crossing with it
		/// before the site is built, to shape the ground its deck will land on (<see cref="CarveArchApproaches"/>).
		/// </summary>
		public static Crossing MeasureCrossing(PointOfInterestRecord record, Func<float, float, float> groundAt, SceneWater water)
		{
			float level = water != null ? water.SurfaceAt(record.Position.x, record.Position.z) : float.NegativeInfinity;
			Func<float, float> waterAlong = null;
			if (float.IsNegativeInfinity(level))
			{
				level = GroundAlong(record, groundAt, 0f);
			}
			else
			{
				waterAlong = t =>
				{
					Vector2 at = AlongHeading(record, t);
					return water.SurfaceAt(at.x, at.y);
				};
			}
			// The record stands at its banks' (or a gorge's rims') height: a gorge bridge is laid rim to rim at it.
			return MeasureCrossing(t => GroundAlong(record, groundAt, t), level, CrossingReach(record.Radius), waterAlong, record.Position.y);
		}

		/// <summary>How far either way along its heading a bridge site's crossing is looked for, from its radius, metres.</summary>
		public static float CrossingReach(float radius) => Mathf.Max(8f, radius * 2f);

		/// <summary>The world point (x, z) a distance along a site's heading from its centre: the site's frame (<see cref="PointOfInterestSiteContext.OnGround"/>).</summary>
		public static Vector2 AlongHeading(PointOfInterestRecord record, float along)
		{
			float yaw = record.Yaw * Mathf.Deg2Rad;
			return new Vector2(record.Position.x + Mathf.Sin(yaw) * along, record.Position.z + Mathf.Cos(yaw) * along);
		}

		private static float GroundAlong(PointOfInterestRecord record, Func<float, float, float> groundAt, float along)
		{
			Vector2 at = AlongHeading(record, along);
			return groundAt != null ? groundAt(at.x, at.y) : record.Position.y;
		}

		/// <summary>The steepest a bank may climb (rise per metre) and still be the channel's wall the deck reaches over.</summary>
		public const float ChannelWallGrade = 0.6f;

		/// <summary>
		/// <see cref="MeasureCrossing(PointOfInterestSiteContext)"/> on a profile: the ground's height at a signed distance
		/// along the heading, the water's surface at the site (or a dry bed's height), and the scene's water surface along
		/// the heading (negative infinity where there is none).
		/// </summary>
		/// <remarks>
		/// Without <paramref name="waterAlong"/> (a dry channel) the water is wherever the ground lies under
		/// <paramref name="waterLevel"/>; with it, only where that water's surface is within <see cref="SameWaterMetres"/> of
		/// the site's, so a fall's foot or a lower reach past a bank is not reckoned the water crossed here.
		/// <para>
		/// With <paramref name="rim"/> (the site's height) at least <see cref="PathRouter.GorgeDepth"/> over the water (or a
		/// dry floor), the crossing may be a gorge's (PathRouter's rim-to-rim steps): the deck runs straight from the lip of one
		/// wall to the lip of the other, high over the floor, never down into it. Where either lip is not found up a wall
		/// it is measured as any other crossing.
		/// </para>
		/// </remarks>
		public static Crossing MeasureCrossing(Func<float, float> groundAlong, float waterLevel, float reach, Func<float, float> waterAlong = null,
			float rim = float.NaN)
		{
			const float Step = 0.5f, MaxWallRun = 6f, Bearing = 0.5f;
			// Every end inside the reach: the layout lays no span past it, so a deck that ended beyond was cut short with
			// its level and hung in the air.
			float limit = Mathf.Max(Step, reach - Bearing);
			if (!float.IsNaN(rim) && rim >= waterLevel + PathRouter.GorgeDepth
				&& TryGorgeCrossing(groundAlong, rim, limit, out Crossing gorge))
			{
				return gorge;
			}
			float SurfaceAt(float t) => waterAlong != null ? waterAlong(t) : waterLevel;
			bool Crossed(float surface) => !float.IsNegativeInfinity(surface) && Mathf.Abs(surface - waterLevel) <= SameWaterMetres;
			// Under the water this bridge crosses; and under any water at all (no end stands in a lower reach).
			bool Under(float t)
			{
				float surface = SurfaceAt(t);
				return Crossed(surface) && groundAlong(t) <= surface + 0.05f;
			}
			bool Wet(float t)
			{
				float surface = SurfaceAt(t);
				return !float.IsNegativeInfinity(surface) && groundAlong(t) <= surface + 0.05f;
			}

			var edges = new float[2];
			for (int i = 0; i < 2; i++)
			{
				float side = i == 0 ? -1f : 1f;
				// The water's edge: the first ground not under it.
				float edge = Step;
				while (edge < limit && Under(side * edge))
				{
					edge += Step;
				}
				edges[i] = Mathf.Min(edge, limit);
			}
			// Clear of all of it: a river crossed aslant falls a little from one side of the deck to the other.
			float level = waterLevel;
			for (float t = -edges[0]; t <= edges[1]; t += Step)
			{
				float surface = SurfaceAt(t);
				if (Crossed(surface))
				{
					level = Mathf.Max(level, surface);
				}
			}
			float need = level + DeckClearance;

			var ends = new float[2];
			var decks = new float[2];
			var lips = new float[2];
			bool unfooted = false;
			for (int i = 0; i < 2; i++)
			{
				float side = i == 0 ? -1f : 1f;
				float t = edges[i];
				bool footed = false;
				for (; t <= limit; t += Step)
				{
					if (Wet(side * t))
					{
						continue;
					}
					float ground = groundAlong(side * t);
					if (ground >= need)
					{
						// Up a steep wall to its lip, not along a gentle rise.
						for (float run = 0f; t + Step <= limit && run < MaxWallRun && groundAlong(side * (t + Step)) - groundAlong(side * t) > ChannelWallGrade * Step; run += Step)
						{
							t += Step;
						}
						footed = true;
						break;
					}
					if (ground >= need - (t - edges[i]) * MinRampGrade)
					{
						// A bank too low ever to clear the water: where the gentlest ramp down from the deck meets it.
						footed = true;
						break;
					}
				}
				if (footed)
				{
					lips[i] = t;
					decks[i] = groundAlong(side * t);
					// Resting a little onto its bank where the bank is level there: not out over a drop, nor into a rise.
					ends[i] = t + (Mathf.Abs(groundAlong(side * (t + Bearing)) - decks[i]) <= 0.2f ? Bearing : 0f);
					continue;
				}
				// No ground the deck can come down onto: the ground falls away from the water faster than any ramp. It ends
				// on the highest ground on this side (the lip at the water's edge; ties go outward, across the water rather
				// than short of it), and its ramp starts back over the water so it comes down no steeper than MaxRampGrade
				// where there is room.
				unfooted = true;
				float best = Step;
				float highest = groundAlong(side * Step);
				for (float u = Step * 2f; u <= limit; u += Step)
				{
					float ground = groundAlong(side * u);
					if (ground >= highest - 0.05f)
					{
						best = u;
						highest = Mathf.Max(highest, ground);
					}
				}
				lips[i] = best;
				ends[i] = best;
				decks[i] = groundAlong(side * best);
				edges[i] = Mathf.Max(0f, Mathf.Min(edges[i], best - (need - decks[i]) / MaxRampGrade));
			}

			// Not too steep end to end: the higher end comes back down its wall (still clear of the water) until it is not.
			float tan = Mathf.Tan(MaxDeckSlopeDegrees * Mathf.Deg2Rad);
			for (int guard = 0; guard < 64; guard++)
			{
				float length = ends[0] + ends[1];
				float rise = decks[1] - decks[0];
				if (Mathf.Abs(rise) <= tan * length)
				{
					break;
				}
				int high = rise > 0f ? 1 : 0;
				float side = high == 0 ? -1f : 1f;
				float t = lips[high] - Step;
				if (t < edges[high] || groundAlong(side * t) < need)
				{
					// No lower footing that clears the water: the deck rotates to meet both ends as they are.
					break;
				}
				lips[high] = t;
				ends[high] = t + Bearing;
				decks[high] = groundAlong(side * t);
			}

			var crossing = new Crossing
			{
				From = -ends[0],
				To = ends[1],
				DeckFrom = decks[0],
				DeckTo = decks[1],
				WaterFrom = -edges[0],
				WaterTo = edges[1],
				Level = need,
				Unfooted = unfooted,
			};
			// Each ramp exactly long enough to bring the deck down from over the water onto its end.
			crossing.RampFrom = Mathf.Max(MinRampGrade, (need - decks[0]) / Mathf.Max(0.5f, ends[0] - edges[0]));
			crossing.RampTo = Mathf.Max(MinRampGrade, (need - decks[1]) / Mathf.Max(0.5f, ends[1] - edges[1]));
			return crossing;
		}

		/// <summary>How far under its rim a gorge's lip may stand and still be where the deck lands, metres.</summary>
		private const float RimTolerance = 0.75f;

		/// <summary>
		/// A gorge's deck: on each side the first ground within <see cref="RimTolerance"/> of the rim, carried on up the wall
		/// to its lip; one straight deck between, no steeper than <see cref="MaxDeckSlopeDegrees"/>, over a floor at least
		/// <see cref="PathRouter.GorgeDepth"/> under both lips. False when either side has no such lip or there is no such floor.
		/// </summary>
		private static bool TryGorgeCrossing(Func<float, float> groundAlong, float rim, float limit, out Crossing crossing)
		{
			const float Step = 0.5f, MaxWallRun = 6f, Bearing = 0.5f;
			crossing = default;
			var lips = new float[2];
			var ends = new float[2];
			var decks = new float[2];
			for (int i = 0; i < 2; i++)
			{
				float side = i == 0 ? -1f : 1f;
				float t = Step;
				while (t <= limit && groundAlong(side * t) < rim - RimTolerance)
				{
					t += Step;
				}
				if (t > limit)
				{
					return false;
				}
				for (float run = 0f; t + Step <= limit && run < MaxWallRun && groundAlong(side * (t + Step)) - groundAlong(side * t) > ChannelWallGrade * Step; run += Step)
				{
					t += Step;
				}
				lips[i] = t;
				decks[i] = groundAlong(side * t);
				ends[i] = t + (Mathf.Abs(groundAlong(side * (t + Bearing)) - decks[i]) <= 0.2f ? Bearing : 0f);
			}
			if (Mathf.Abs(decks[1] - decks[0]) > Mathf.Tan(MaxDeckSlopeDegrees * Mathf.Deg2Rad) * (ends[0] + ends[1]))
			{
				return false;
			}
			float floor = float.PositiveInfinity;
			for (float t = -lips[0]; t <= lips[1]; t += Step)
			{
				floor = Mathf.Min(floor, groundAlong(t));
			}
			if (floor > Mathf.Min(decks[0], decks[1]) - PathRouter.GorgeDepth)
			{
				return false;
			}
			crossing = new Crossing
			{
				From = -ends[0],
				To = ends[1],
				DeckFrom = decks[0],
				DeckTo = decks[1],
				WaterFrom = -lips[0],
				WaterTo = lips[1],
				Level = float.NaN,
				Gorge = true,
			};
			return true;
		}

		// ── A stone arch on raked rims ──────────────────────────

		/// <summary>The steepest a stone arch's deck is graded from rim to rim, degrees: a mason lays it level, or near it.</summary>
		public const float MaxArchGradeDegrees = 4f;

		/// <summary>The grade (rise per metre) of the ramps that bring each rim to an arch's deck: a cart's, about 8.5°.</summary>
		public const float ArchApproachGrade = 0.15f;

		/// <summary>The most the low rim is built up to an arch's deck, metres: a rake that needs more is hung with ropes.</summary>
		public const float MaxArchFill = 3f;

		/// <summary>The most the high rim's edge is dressed down to an arch's deck, metres, and the share of the rake it takes.</summary>
		public const float MaxArchCut = 1.5f, ArchCutShare = 1f / 3f;

		/// <summary>The most an arch's deck end may stand off its approach and still be stepped onto, metres.</summary>
		public const float ArchStepTolerance = 0.3f;

		/// <summary>The longest an approach ramp runs past the deck's end before it must have met its rim, metres.</summary>
		public const float MaxArchApproachMetres = 24f;

		/// <summary>How far under the deck's top its approaches are laid, metres: the deck's slab rests on them, not in them.</summary>
		public const float ArchSeat = 0.05f;

		/// <summary>Half an arch approach's width on top, metres: the arch's 4 m deck and a verge.</summary>
		public const float ArchApproachHalfWidth = 2.5f;

		/// <summary>The steepest a cutting's dressed sides stand (rise per metre), about 50°.</summary>
		private const float ArchCutSlope = 1.2f;

		/// <summary>How far under its lip's rim a built-up approach leaves the ground alone, metres: the gorge is never filled.</summary>
		private const float ArchGorgeGuard = 1f;

		/// <summary>
		/// A stone arch's deck over a gorge (<see cref="TryArchDeck"/>): level, or graded no steeper than
		/// <see cref="MaxArchGradeDegrees"/>, whatever the rake between the rims, and the approaches that bring each rim to it.
		/// Its lips are the crossing's ends; its deck runs on past them into the rims (the piece's abutments).
		/// </summary>
		public struct ArchDeck
		{
			/// <summary>True when the arch fits the gorge; nothing else is set otherwise.</summary>
			public bool Valid;
			/// <summary>Metres along the heading (signed) of the arch's lips (the crossing's ends) and of its deck's ends past them.</summary>
			public float From, To, EndFrom, EndTo;
			/// <summary>World height of the deck's top at each lip.</summary>
			public float DeckFrom, DeckTo;
			/// <summary>The natural ground at each lip, world height: the rims as they stood.</summary>
			public float RimFrom, RimTo;
			/// <summary>
			/// Each approach's ramp past its deck's end, rise per metre outward: negative where the rim is built up to the
			/// deck and the ramp comes back down onto it, positive where the rim is cut down to the deck and the ramp climbs
			/// out of the cutting; 0 where the rim already meets the deck within <see cref="ArchStepTolerance"/>.
			/// </summary>
			public float RiseFrom, RiseTo;
			/// <summary>How far each ramp runs past its deck's end before it meets the rim's ground, metres.</summary>
			public float RampFrom, RampTo;
			/// <summary>The most the approaches build the ground up (on the low rim) and cut it down (into the high one), metres.</summary>
			public float Fill, Cut;

			/// <summary>The deck's top at a distance along the heading: the straight line through both lips, on past them.</summary>
			public float DeckAt(float along) => To > From ? DeckFrom + (DeckTo - DeckFrom) * (along - From) / (To - From) : DeckFrom;

			/// <summary>The deck's grade, degrees, rising toward <see cref="To"/>.</summary>
			public float GradeDegrees => To > From ? Mathf.Atan2(DeckTo - DeckFrom, To - From) * Mathf.Rad2Deg : 0f;

			/// <summary>True when both rims already meet the deck within a step: nothing to build up or cut.</summary>
			public bool Settled => RiseFrom == 0f && RiseTo == 0f;

			/// <summary>How far from the crossing's centre its deck and the approaches built for it reach along the heading, metres.</summary>
			public float Reach => Mathf.Max(EndTo + (RiseTo != 0f ? RampTo : 0f), -EndFrom + (RiseFrom != 0f ? RampFrom : 0f));

			/// <summary>
			/// The top of one side's approach (−1 the <see cref="From"/> side, +1 the <see cref="To"/>), a distance
			/// <paramref name="outward"/> past its lip: <see cref="ArchSeat"/> under the deck as far as the deck's end, then the ramp.
			/// </summary>
			public float ApproachAt(int side, float outward)
			{
				float lip = side < 0 ? From : To, end = side < 0 ? EndFrom : EndTo;
				float run = Mathf.Abs(end - lip);
				if (outward <= run)
				{
					return DeckAt(lip + side * Mathf.Max(0f, outward)) - ArchSeat;
				}
				return DeckAt(end) - ArchSeat + (side < 0 ? RiseFrom : RiseTo) * (outward - run);
			}
		}

		/// <summary>
		/// A stone arch's deck over a gorge crossing (<see cref="TryGorgeCrossing"/>) whose rims may stand at different heights,
		/// built as a mason builds one: the deck level, or graded no steeper than <see cref="MaxArchGradeDegrees"/>, never
		/// tilted whole to reach lip to lip. What the grade does not take up of the rake is made up at the rims: the high
		/// rim's edge is dressed down a third of it (at most <see cref="MaxArchCut"/>), where the arch springs from its solid
		/// rock, and the low rim is built up the rest (at most <see cref="MaxArchFill"/>) behind a raised abutment, so the
		/// arch's ring stays bedded below both lips. Each rim's approach then ramps at <see cref="ArchApproachGrade"/> from the
		/// deck's end back onto its ground within <see cref="MaxArchApproachMetres"/>. False (the gorge is hung with ropes,
		/// which may slope) when the rake needs more fill than that, or a ramp never meets its rim (the ground falls away
		/// past the low lip, or climbs on past the high one).
		/// </summary>
		/// <param name="groundAlong">The ground's height a signed distance along the crossing's heading.</param>
		/// <param name="nominal">The arch piece's own length (<see cref="StructurePieces.ArchBridgeLengths"/>): it is stretched to the gorge.</param>
		public static bool TryArchDeck(Func<float, float> groundAlong, in Crossing crossing, float nominal, out ArchDeck deck)
		{
			const float Step = 0.5f;
			deck = default;
			float length = crossing.To - crossing.From;
			if (!crossing.Gorge || length <= 0.5f || nominal <= 0f)
			{
				return false;
			}
			// The deck each rim offers on its own (seated on its lip), the rake between them, and what the grade takes up.
			float rimFrom = crossing.DeckFrom + ArchSeat, rimTo = crossing.DeckTo + ArchSeat;
			float rake = rimTo - rimFrom;
			float most = Mathf.Tan(MaxArchGradeDegrees * Mathf.Deg2Rad) * length;
			float excess = Mathf.Abs(rake - Mathf.Clamp(rake, -most, most));
			float cut = Mathf.Min(excess * ArchCutShare, MaxArchCut), fill = excess - cut;
			if (fill > MaxArchFill)
			{
				return false;
			}
			bool toHigh = rake > 0f;
			// The piece's abutments run on into each rim, stretched with it.
			float run = StructurePieces.ArchAbutmentRun * length / nominal;
			var plan = new ArchDeck
			{
				Valid = true,
				From = crossing.From,
				To = crossing.To,
				EndFrom = crossing.From - run,
				EndTo = crossing.To + run,
				DeckFrom = toHigh ? rimFrom + fill : rimFrom - cut,
				DeckTo = toHigh ? rimTo - cut : rimTo + fill,
				RimFrom = crossing.DeckFrom,
				RimTo = crossing.DeckTo,
			};

			float above = 0f, below = 0f;
			for (int side = -1; side <= 1; side += 2)
			{
				float lip = side < 0 ? plan.From : plan.To, end = side < 0 ? plan.EndFrom : plan.EndTo;
				float span = Mathf.Abs(end - lip);
				// How far the ground under the deck's own run stands off it: within a step, this rim meets the deck as it is.
				float off = 0f;
				for (float o = 0f; ; o = Mathf.Min(o + Step, span))
				{
					float d = plan.ApproachAt(side, o) - groundAlong(lip + side * o);
					off = Mathf.Abs(d) > Mathf.Abs(off) ? d : off;
					above = Mathf.Max(above, d);
					below = Mathf.Max(below, -d);
					if (o >= span)
					{
						break;
					}
				}
				if (Mathf.Abs(off) <= ArchStepTolerance)
				{
					continue;
				}
				// Built up where the deck's end stands over its ground, a ramp coming back down; cut where it is under, a
				// ramp climbing out.
				float top = plan.ApproachAt(side, span);
				float atEnd = top - groundAlong(end);
				float rise = (Mathf.Abs(atEnd) > 0.05f ? atEnd : off) > 0f ? -ArchApproachGrade : ArchApproachGrade;
				float ramp = -1f;
				for (float r = 0f; r <= MaxArchApproachMetres; r += Step)
				{
					float g = groundAlong(end + side * r), p = top + rise * r;
					if (rise < 0f ? g >= p : g <= p)
					{
						ramp = r;
						break;
					}
					above = Mathf.Max(above, p - g);
					below = Mathf.Max(below, g - p);
				}
				if (ramp < 0f)
				{
					return false;
				}
				if (side < 0)
				{
					plan.RiseFrom = rise;
					plan.RampFrom = ramp;
				}
				else
				{
					plan.RiseTo = rise;
					plan.RampTo = ramp;
				}
			}
			// A rim that falls away under its approach, or rises on into a deepening cutting, asks more than the rake did.
			if (above > MaxArchFill + ArchStepTolerance || below > 2f * MaxArchCut)
			{
				return false;
			}
			plan.Fill = above;
			plan.Cut = below;
			deck = plan;
			return true;
		}

		/// <summary>
		/// The ground at a point near an arch (<paramref name="along"/> its heading from the crossing's centre,
		/// <paramref name="across"/> to its right) once its approaches are built (<see cref="TryArchDeck"/>): each approach's
		/// top on it (the deck's line under the deck's end, then the ramp), banked back to the natural ground past its edges,
		/// down a built-up approach's sides at <see cref="PointOfInterestTerrain.EmbankmentSlope"/> and up a cutting's at
		/// <see cref="ArchCutSlope"/>. A built-up approach never fills the gorge (it starts at the lip, on ground no lower
		/// than the rim); a cutting takes down whatever stands above it out to the gorge's middle, so the high rim's edge is
		/// dressed to the abutment rather than standing proud of the deck.
		/// </summary>
		public static float ApproachGround(in ArchDeck deck, float along, float across, float ground)
		{
			if (!deck.Valid)
			{
				return ground;
			}
			for (int side = -1; side <= 1; side += 2)
			{
				float rise = side < 0 ? deck.RiseFrom : deck.RiseTo;
				if (rise == 0f)
				{
					continue;
				}
				float lip = side < 0 ? deck.From : deck.To;
				float length = Mathf.Abs((side < 0 ? deck.EndFrom : deck.EndTo) - lip) + (side < 0 ? deck.RampFrom : deck.RampTo);
				float outward = side * (along - lip);
				bool built = rise < 0f;
				if (built ? outward < 0f : outward < -0.5f * (deck.To - deck.From))
				{
					continue;
				}
				float wide = Mathf.Max(0f, Mathf.Abs(across) - ArchApproachHalfWidth), past = Mathf.Max(0f, outward - length);
				float off = Mathf.Sqrt(wide * wide + past * past);
				float top = deck.ApproachAt(side, Mathf.Clamp(outward, 0f, length));
				if (!built)
				{
					ground = Mathf.Min(ground, top + off * ArchCutSlope);
				}
				else if (ground >= (side < 0 ? deck.RimFrom : deck.RimTo) - ArchGorgeGuard)
				{
					ground = Mathf.Max(ground, top - off * PointOfInterestTerrain.EmbankmentSlope);
				}
			}
			return ground;
		}

		/// <summary>
		/// The stone arch a bridge site will be built with, when it is a gorge's (<see cref="MeasureCrossing(PointOfInterestRecord, Func{float, float, float}, SceneWater)"/>,
		/// <see cref="GorgeBridgeFor(Func{float, float}, in Crossing, bool, out float, out ArchDeck)"/>): false for a river's
		/// bridge, a gorge hung with ropes or crossed on planks.
		/// </summary>
		public static bool PlanGorgeArch(PointOfInterestRecord record, Func<float, float, float> groundAt, SceneWater water,
			IReadOnlyList<PointOfInterestRecord> records, out ArchDeck deck)
		{
			deck = default;
			Crossing crossing = MeasureCrossing(record, groundAt, water);
			if (!crossing.Gorge)
			{
				return false;
			}
			GorgeBridgeFor(t => GroundAlong(record, groundAt, t), crossing, KeptBySettlement(records, record.Position), out _, out deck);
			return deck.Valid;
		}

		/// <summary>
		/// Builds a gorge arch's approaches into the ground before its site is built (<see cref="ApproachGround"/>): the low
		/// rim raised to the deck on a ramp between banked sides, the high rim's edge cut down to it. The site then measures
		/// rims that meet a level deck and lays the arch on them. Nothing for a river's bridge, a gorge hung with ropes, or an
		/// arch whose rims already meet its deck (so cutting again changes nothing). Returns the heightmap samples changed.
		/// </summary>
		public static int CarveArchApproaches(Terrain[,] terrains, TerrainTilePlan tiles, PointOfInterestRecord record,
			Func<float, float, float> groundAt, SceneWater water, IReadOnlyList<PointOfInterestRecord> records)
		{
			if (!PlanGorgeArch(record, groundAt, water, records, out ArchDeck deck) || deck.Settled)
			{
				return 0;
			}
			float yaw = record.Yaw * Mathf.Deg2Rad;
			var axis = new Vector2(Mathf.Sin(yaw), Mathf.Cos(yaw));
			var right = new Vector2(axis.y, -axis.x);
			var centre = new Vector2(record.Position.x, record.Position.z);
			// As far as the banks and the cutting's sides can reach back to the natural ground.
			float margin = Mathf.Max(MaxArchFill / PointOfInterestTerrain.EmbankmentSlope, 2f * MaxArchCut / ArchCutSlope) + 2f;
			float reach = deck.Reach + margin, wide = ArchApproachHalfWidth + margin;
			float minX = float.PositiveInfinity, minZ = float.PositiveInfinity, maxX = float.NegativeInfinity, maxZ = float.NegativeInfinity;
			for (int corner = 0; corner < 4; corner++)
			{
				Vector2 at = centre + axis * (corner < 2 ? -reach : reach) + right * (corner % 2 == 0 ? -wide : wide);
				minX = Mathf.Min(minX, at.x);
				minZ = Mathf.Min(minZ, at.y);
				maxX = Mathf.Max(maxX, at.x);
				maxZ = Mathf.Max(maxZ, at.y);
			}
			return PointOfInterestTerrain.EditHeights(terrains, tiles, Rect.MinMaxRect(minX, minZ, maxX, maxZ), (x, z, h) =>
			{
				var offset = new Vector2(x - centre.x, z - centre.y);
				return ApproachGround(deck, Vector2.Dot(offset, axis), Vector2.Dot(offset, right), h);
			});
		}

		/// <summary>
		/// How far from a gorge bridge's centre its stone arch's deck and approaches reach along its heading, metres, for the
		/// ways laid onto it: 0 where the gorge is hung with ropes or crossed on planks, or no gorge is found there.
		/// <paramref name="groundAlong"/> is the ground along the heading from the centre, <paramref name="rim"/> the
		/// site's height (the lower rim's), <paramref name="reach"/> as far as the site looks (<see cref="CrossingReach"/>).
		/// </summary>
		public static float GorgeArchReach(Func<float, float> groundAlong, float rim, float reach, bool kept)
		{
			const float Bearing = 0.5f;
			if (!TryGorgeCrossing(groundAlong, rim, Mathf.Max(0.5f, reach - Bearing), out Crossing crossing))
			{
				return 0f;
			}
			GorgeBridgeFor(groundAlong, crossing, kept, out _, out ArchDeck deck);
			return deck.Valid ? deck.Reach : 0f;
		}

		/// <summary>How far a pier's deck stands over the highest water under it, metres.</summary>
		public const float PierFreeboard = 0.8f;
		/// <summary>How far under its deck a pier's piles reach (the Pier piece's), metres: no module is laid over deeper water.</summary>
		public const float PierPileReach = 5.8f;
		/// <summary>The most a pier's deck may stand over the bank where it starts, metres: a step up, not a climb.</summary>
		private const float PierStep = 0.5f;
		/// <summary>How far back of the high-water line a pier may start to meet a bank at its deck's height, metres.</summary>
		private const float PierLandward = 8f;

		/// <summary>Metres ahead of the centre where the water begins, and the pier's deck over it; NaN where there is no shore.</summary>
		public static float MeasureShore(PointOfInterestSiteContext context, out float deck) => MeasureShore(context, out deck, out _, out _);

		/// <summary>
		/// Metres ahead of the centre where the water begins (NaN where there is no shore), the height of a pier's deck, and
		/// the stretch a pier runs: from the bank at the deck's height out to where its piles stop reaching the bed.
		/// </summary>
		/// <remarks>
		/// <para>
		/// <b>Over high water.</b> On the sea the deck stands <see cref="PierFreeboard"/> over the highest the tide comes (the
		/// scene's <c>WaterEnvironment.MaximumTideMetres</c>), not over mean sea level: a deck a metre over the mean was awash
		/// for hours of every high tide (2026-10-12: "high tide should be the measurement"). A lake or river has no tide
		/// and its deck stands a metre over its level.
		/// </para>
		/// <para>
		/// <b>From the bank.</b> A deck that high over a beach would start a step taller than a man where the water does, so the
		/// pier starts back up the shore where the ground reaches its deck (or, on a low beach, at the high-water line), and runs
		/// out as far past the waterline as it would have; it stops short where the bed falls deeper than its piles reach.
		/// </para>
		/// </remarks>
		public static float MeasureShore(PointOfInterestSiteContext context, out float deck, out float from, out float to)
		{
			PointOfInterestRecord record = context.Record;
			float sea = SeaLevelOf(context);
			float tide = float.IsNegativeInfinity(sea) ? 0f : HighestTideOf(context);
			float shore = MeasureShore(
				t => context.OnGround(new Vector2(0f, t)).y,
				t =>
				{
					Vector3 at = context.OnGround(new Vector2(0f, t));
					return context.Water != null ? context.Water.SurfaceAt(at.x, at.z) : float.NegativeInfinity;
				},
				sea, tide, record.Radius, out deck, out from, out to);
			if (float.IsNaN(shore))
			{
				deck = record.Position.y;
			}
			return shore;
		}

		/// <summary>
		/// The pure measure over a profile ahead of the site (see the overload above): <paramref name="groundAlong"/> and
		/// <paramref name="waterAlong"/> (the river or lake surface, negative infinity for none) at metres ahead,
		/// <paramref name="sea"/> the mean sea level (negative infinity for none) and <paramref name="highestTide"/> how far
		/// over it the tide comes.
		/// </summary>
		public static float MeasureShore(Func<float, float> groundAlong, Func<float, float> waterAlong, float sea, float highestTide, float radius,
			out float deck, out float from, out float to)
		{
			deck = float.NaN;
			from = to = float.NaN;
			float reach = Mathf.Max(10f, radius * 2.5f);
			for (float t = 0f; t <= reach; t += 1f)
			{
				float ground = groundAlong(t);
				float level = waterAlong != null ? waterAlong(t) : float.NegativeInfinity;
				if (float.IsNegativeInfinity(level) && !float.IsNegativeInfinity(sea) && ground < sea)
				{
					level = sea;
				}
				if (float.IsNegativeInfinity(level) || level <= ground + 0.3f)
				{
					continue;
				}
				bool tidal = !float.IsNegativeInfinity(sea) && Mathf.Abs(level - sea) < 0.3f;
				float high = tidal ? sea + Mathf.Max(0f, highestTide) : level;
				deck = high + (tidal ? PierFreeboard : 1f);
				// Back up the shore to where the bank stands at the deck (less a step); on a low beach, the high-water line.
				for (float b = t; b >= 0f; b -= 0.5f)
				{
					float bank = groundAlong(b);
					if (bank >= deck - PierStep)
					{
						from = b;
						break;
					}
					if (float.IsNaN(from) && bank >= high)
					{
						// The high-water line: kept unless the bank a few metres further back reaches the deck.
						from = b;
					}
					else if (!float.IsNaN(from) && b < from - PierLandward)
					{
						break;
					}
				}
				if (float.IsNaN(from))
				{
					from = t;
				}
				// Out as far past the waterline as a pier reaches, short of where the piles stop meeting the bed or the ground
				// comes up dry again.
				float outward = t + Mathf.Max(6f, radius * 0.6f);
				to = t;
				for (float o = t; o <= outward; o += 0.5f)
				{
					float bed = groundAlong(o);
					if (bed < deck - PierPileReach || (o > t + 3f && bed > level - 0.1f))
					{
						break;
					}
					to = o;
				}
				to = Mathf.Max(to, from + 6f);
				return t;
			}
			return float.NaN;
		}

		/// <summary>The most the scene's tide rises over mean sea level, metres: its water environment's limit, else the default.</summary>
		internal static float HighestTideOf(PointOfInterestSiteContext context)
		{
			if (context.Scene.IsValid())
			{
				foreach (GameObject root in context.Scene.GetRootGameObjects())
				{
					if (root.TryGetComponent(out FishMMO.Water.WaterEnvironment environment))
					{
						return environment.DriveTide ? environment.MaximumTideMetres : 0f;
					}
				}
			}
			return FishMMO.Water.WaterEnvironment.DefaultMaximumTideMetres;
		}

		/// <summary>The scene's sea level, scene metres; negative infinity for a scene with no sea.</summary>
		internal static float SeaLevelOf(PointOfInterestSiteContext context) => SeaLevelOf(context.Request, context.Water);

		/// <summary>The scene's sea level, scene metres; negative infinity for a scene with no sea.</summary>
		internal static float SeaLevelOf(SceneGenerationRequest request, SceneWater water)
		{
			if (water != null && !float.IsNegativeInfinity(water.SeaLevel))
			{
				return water.SeaLevel;
			}
			if (request == null || request.Body == null || (request.Layer != null && request.Layer.Underground))
			{
				return float.NegativeInfinity;
			}
			SurfaceLiquid liquid = SurfaceLiquids.For(SolarSystemProfile.Resolve(request.Body), request.Body, out float level);
			return liquid != SurfaceLiquid.None ? level * request.VerticalScale : float.NegativeInfinity;
		}
	}
}
#endif
