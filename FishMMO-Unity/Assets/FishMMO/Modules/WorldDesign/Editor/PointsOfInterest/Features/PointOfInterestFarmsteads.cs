#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using FishMMO.Shared.Biomes;
using FishMMO.Shared.NameGeneration;
using UnityEngine;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>
	/// The working farmland round a settlement (2026-10-19: "barns and farmland (we have fenced areas but they aren't really farm
	/// land areas)"): its farmsteads beside its fields, and the fields themselves dressed as worked, laid by PointOfInterestFields
	/// after every field, fence and old terrace of the site.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Farmsteads.</b> A house and its barn, a rick or two, a sty and a hen house, a well or a water butt, a cart and a
	/// woodpile gathered round a yard (<see cref="Layout"/>): the barn down one side with its doors to the yard, the house across
	/// the back looking out to the gate, the ricks and the stock on the other side; the yard fenced or walled in its biome's own
	/// field-edge style (PointOfInterestLinearFeatures, else the kit's field fence), its gate toward the nearest way. Laid beside
	/// the site's fields on gentle, dry ground outside every site, clear of the ways, the water and every field: a village one or
	/// two, a town, a city or the capital two (2026-10-20: one or two barns to a settlement, PointOfInterestStructureCaps; its other
	/// fields are worked from those, and a farmstead past its settlement's two sties and two hen houses keeps neither, past its two
	/// wells a water butt), a windmill's miller at most one, a monastery its grange (the stone tithe barn). An abandoned farm
	/// keeps the charred shell of its barn.
	/// </para>
	/// <para>
	/// <b>Buildings are buildings.</b> Each is laid through PointOfInterestSiteContext.AddProp, so its doors become server-run doors
	/// and its rooms are furnished (PointOfInterestInteriors, after this builder) and dug out of the slope as a settlement's are.
	/// The doors builder runs before the fields' (site builders run in registration order), so the doors a farmstead adds are hung
	/// here (<see cref="HangLateDoors"/>).
	/// </para>
	/// <para>
	/// <b>The fields.</b> Some grain fields are reaped (<see cref="Reaped"/>): their crop thinned to stubble (PointOfInterestFields
	/// paints it at <see cref="StubbleCover"/>) and stood in rows of stooks, a rick in a corner; a scarecrow stands over some of the
	/// growing corn; a pasture has its trough and perhaps a rick.
	/// </para>
	/// <para>
	/// <b>Nothing that was laid moves.</b> Every draw is a hash of the site's seed and a salt of this file's own
	/// (<see cref="PointOfInterestPlanner.Hash"/>), never the site's sequence; the fields, fences and terraces before are as they
	/// were. A farmstead claims its ground (PointOfInterestFieldClaims) under its site's complement id, so a later site's fields
	/// keep off it and a leat is never carried to it as one of the site's fields.
	/// </para>
	/// </remarks>
	public static class PointOfInterestFarmsteads
	{
		/// <summary>What a laid field is used for, as PointOfInterestFields laid it.</summary>
		public enum FieldUse : byte
		{
			/// <summary>A grain crop drawn by the grass blades (wheat, barley, rye…): reaped or growing.</summary>
			Grain,
			/// <summary>Any other sown ground: an orchard, a paddy, terraces, a crop the scatter draws.</summary>
			Crop,
			Ploughed,
			Pasture,
			Fallow,
		}

		/// <summary>One field as laid: where it lies, what it is, and whether it was reaped.</summary>
		public struct LaidField
		{
			public Vector2 Centre;
			public float Yaw;
			public Vector2 Size;
			public FieldUse Use;
			public bool Reaped;
			/// <summary>Its index in its site's draw (its own draws are keyed by it).</summary>
			public int Index;
			/// <summary>An orchard's rows of trees stand in it (2026-10-19: its keeper's bothy is beside it, PointOfInterestOutskirts).</summary>
			public bool Orchard;
		}

		// Salts of this file's own hash streams.
		private const int ReapSalt = 0x52454150, DressSalt = 0x44524553, FarmSalt = 0x4641524D, MixSalt = 0x4D495846;

		/// <summary>The share of grain fields reaped: harvest under way, some corn standing and some in stooks.</summary>
		public const float ReapedShare = 0.4f;

		/// <summary>A reaped field's crop as a share of its standing coverage: the stubble.</summary>
		public const float StubbleCover = 0.22f;

		/// <summary>Whether a site's grain field is reaped (pure: the site's seed and the field's index).</summary>
		public static bool Reaped(int siteSeed, int field) => PointOfInterestPlanner.Unit(PointOfInterestPlanner.Hash(siteSeed, ReapSalt, field, 0)) < ReapedShare;

		/// <summary>The steepest ground a farmstead's yard stands on, degrees: gentler than a field's.</summary>
		public const float MaxSlopeDegrees = 5f;

		/// <summary>How much the ground may fall across one building's footprint, metres: more, and the next spot is tried.</summary>
		public const float MaxFall = 1.2f;

		/// <summary>The capped types a farmstead lays (PointOfInterestStructureCaps): its barn, its sty, its hen house and its well.</summary>
		public const string BarnType = "Barn", StyType = "Pigsty", HenType = "HenHouse", WellType = "Well";

		/// <summary>
		/// The fewest and most farmsteads round a site of a kind, before the settlement's cap on its barns (one to a farmstead,
		/// PointOfInterestStructureCaps) cuts them.
		/// </summary>
		public static (int min, int max) CountFor(POIType kind)
		{
			switch (kind)
			{
				case POIType.Village: return (1, 2);
				case POIType.Town:
				case POIType.City:
				case POIType.Capital: return (2, 2);
				case POIType.Windmill: return (0, 1);
				case POIType.Monastery: return (1, 1);
				case POIType.AbandonedFarm: return (1, 1);
				default: return (0, 0);
			}
		}

		// ── The layout (pure) ─────────────────────────────────────────

		/// <summary>The parts of a farmstead.</summary>
		public enum Part : byte { House, Barn, Rick, Sty, HenHouse, Well, Cart, Woodpile }

		/// <summary>One part laid in the farmstead's frame (its gate at −z): its centre, its yaw (a quarter turn's multiple), and the ground it covers there.</summary>
		public struct Placed
		{
			public Part Part;
			public Vector2 At;
			public float Yaw;
			public Vector2 Extent;
		}

		/// <summary>The footprints the parts are laid by (each piece's own x and z, unturned), metres.</summary>
		public struct Sizes
		{
			public Vector2 House, Barn, Rick, Sty, Hen, Well, Cart, Woodpile;

			public Sizes Scaled(float s) => new Sizes
			{
				House = House * s, Barn = Barn * s, Rick = Rick * s, Sty = Sty * s, Hen = Hen * s, Well = Well * s, Cart = Cart * s, Woodpile = Woodpile * s,
			};
		}

		/// <summary>Which parts a farmstead has.</summary>
		public struct Mix
		{
			public int Ricks;
			public bool Sty, Hen;
		}

		/// <summary>A farmstead's mix by its seed: one or two ricks, a sty and/or a hen house (at least one), as farms varied.</summary>
		public static Mix MixFor(int seed)
		{
			float a = PointOfInterestPlanner.Unit(PointOfInterestPlanner.Hash(seed, MixSalt, 1, 0));
			float b = PointOfInterestPlanner.Unit(PointOfInterestPlanner.Hash(seed, MixSalt, 2, 0));
			float c = PointOfInterestPlanner.Unit(PointOfInterestPlanner.Hash(seed, MixSalt, 3, 0));
			var mix = new Mix { Ricks = a < 0.45f ? 2 : 1, Sty = b < 0.75f, Hen = c < 0.75f };
			if (!mix.Sty && !mix.Hen)
			{
				mix.Hen = true;
			}
			return mix;
		}

		/// <summary>A farmstead laid out: its yard's size (inside its fence) and its parts.</summary>
		public sealed class Plan
		{
			public Vector2 Size;
			/// <summary>The open yard between the barn's band and the stock's, its front at the gate (x half-width, z from, z to).</summary>
			public float YardHalf, YardFrom, YardTo;
			public readonly List<Placed> Parts = new List<Placed>();
		}

		/// <summary>How far in from the fence the parts stand, metres.</summary>
		public const float Margin = 1.5f;

		/// <summary>The gap kept between neighbouring parts, metres.</summary>
		public const float Gap = 1.2f;

		/// <summary>The ground kept open before a barn's doors across the yard, metres: a wain turns in it.</summary>
		public const float BarnApron = 3.2f;

		/// <summary>
		/// A farmstead's layout in its own frame (pure; its gate in the middle of its −z side): three bands across it, the barn in the
		/// left one turned a quarter so its doors face the yard; the open yard in the middle, the house across its back looking out
		/// to the gate, the well to one side of the way in; the ricks along the front of the right band and the sty and hen house
		/// behind them, their runs and pop holes to the yard; the cart by the barn's doors, the woodpile against the house's side.
		/// The bands are as wide as the barn, so the gate comes in down the middle of the yard.
		/// </summary>
		public static Plan Layout(Sizes sizes, Mix mix, int seed)
		{
			float U(int k) => PointOfInterestPlanner.Unit(PointOfInterestPlanner.Hash(seed, FarmSalt, k, 7));
			var plan = new Plan();
			// Turned a quarter: the barn's long side, the sty's and the hen house's fronts, to the yard.
			var barn = new Vector2(sizes.Barn.y, sizes.Barn.x);
			var sty = new Vector2(sizes.Sty.y, sizes.Sty.x);
			var hen = new Vector2(sizes.Hen.y, sizes.Hen.x);
			Vector2 rick = sizes.Rick;
			int ricks = Mathf.Max(0, mix.Ricks);
			float ricksRow = ricks > 0 ? ricks * rick.x + (ricks - 1) * Gap : 0f;
			float stockRow = (mix.Sty ? sty.x : 0f) + (mix.Hen ? hen.x : 0f) + (mix.Sty && mix.Hen ? Gap : 0f);
			float band = Mathf.Max(barn.x, Mathf.Max(ricksRow, stockRow));
			// Room for the house clear of the barn's doors (a cart's turn before them) with the woodpile on its far side.
			float yard = Mathf.Max(10.5f + U(1) * 2.5f, Mathf.Max(sizes.House.x + BarnApron + 0.3f + sizes.Woodpile.y + 0.5f, sizes.Cart.x + sizes.Well.x + 6f));
			float stockDepth = Mathf.Max(mix.Sty ? sty.y : 0f, mix.Hen ? hen.y : 0f);
			float right = (ricks > 0 ? rick.y : 0f) + (ricks > 0 && stockDepth > 0f ? Gap : 0f) + stockDepth;
			// Deep enough for the barn, for the house with open yard before it, and for the right band.
			float inner = Mathf.Max(barn.y, Mathf.Max(sizes.House.y + 9f + U(2) * 3f, right));
			plan.Size = new Vector2(2f * Margin + 2f * band + yard, 2f * Margin + inner);
			float x0 = -plan.Size.x * 0.5f + Margin, z0 = -plan.Size.y * 0.5f + Margin, z1 = plan.Size.y * 0.5f - Margin;

			void Add(Part part, Vector2 at, float yaw, Vector2 extent) => plan.Parts.Add(new Placed { Part = part, At = at, Yaw = yaw, Extent = extent });

			// The barn against the yard's side of its band, toward the back.
			float barnX = x0 + band - barn.x * 0.5f, barnZ = z1 - barn.y * 0.5f - (inner - barn.y) * U(3) * 0.5f;
			Add(Part.Barn, new Vector2(barnX, barnZ), -90f, barn);
			// The house across the back of the yard, facing the gate, clear of the barn's doors, its woodpile on its far side.
			float houseFrom = -yard * 0.5f + BarnApron + sizes.House.x * 0.5f;
			float houseTo = yard * 0.5f - 0.3f - sizes.Woodpile.y - 0.5f - sizes.House.x * 0.5f;
			var houseAt = new Vector2(Mathf.Lerp(houseFrom, Mathf.Max(houseFrom, houseTo), U(4)), z1 - sizes.House.y * 0.5f);
			Add(Part.House, houseAt, 0f, sizes.House);
			// The right band: the ricks along its front, the stock behind them, all against the yard's side of it.
			float xr = plan.Size.x * 0.5f - Margin - band;
			float x = xr;
			for (int i = 0; i < ricks; i++)
			{
				Add(Part.Rick, new Vector2(x + rick.x * 0.5f, z0 + rick.y * 0.5f), U(10 + i) < 0.5f ? 0f : 180f, rick);
				x += rick.x + Gap;
			}
			float stockZ = z0 + (ricks > 0 ? rick.y + Gap : 0f);
			x = xr;
			if (mix.Sty)
			{
				Add(Part.Sty, new Vector2(x + sty.x * 0.5f, stockZ + sty.y * 0.5f), 90f, sty);
				x += sty.x + Gap;
			}
			if (mix.Hen)
			{
				Add(Part.HenHouse, new Vector2(x + hen.x * 0.5f, stockZ + hen.y * 0.5f), 90f, hen);
			}
			// The cart beside the barn's doors toward the gate, its shafts down along the barn; the well to the other side of the
			// way in; the woodpile against the house's side.
			float doorFace = x0 + band;
			var cart = new Vector2(sizes.Cart.x, sizes.Cart.y);
			Add(Part.Cart, new Vector2(doorFace + 0.6f + cart.x * 0.5f, Mathf.Max(z0 + cart.y * 0.5f, barnZ - 3.2f - cart.y * 0.5f)), 0f, cart);
			float wellX = yard * 0.5f - 1.4f - sizes.Well.x * 0.5f;
			Add(Part.Well, new Vector2(wellX, z0 + (houseAt.y - sizes.House.y * 0.5f - z0) * 0.45f), 0f, sizes.Well);
			var wood = new Vector2(sizes.Woodpile.y, sizes.Woodpile.x);
			Add(Part.Woodpile, new Vector2(houseAt.x + sizes.House.x * 0.5f + 0.3f + wood.x * 0.5f, houseAt.y - sizes.House.y * 0.25f), -90f, wood);
			plan.YardHalf = yard * 0.5f;
			plan.YardFrom = z0;
			plan.YardTo = houseAt.y - sizes.House.y * 0.5f;
			return plan;
		}

		/// <summary>True when two placed parts' grounds overlap, kept <paramref name="gap"/> apart (pure; the parts are square to the frame).</summary>
		public static bool Overlap(in Placed a, in Placed b, float gap)
		{
			return Mathf.Abs(a.At.x - b.At.x) < (a.Extent.x + b.Extent.x) * 0.5f + gap && Mathf.Abs(a.At.y - b.At.y) < (a.Extent.y + b.Extent.y) * 0.5f + gap;
		}

		// ── The scene ─────────────────────────────────────────────────

		/// <summary>The claim a site's farmsteads are made under: its id's complement, so they are another site's ground to its own fields' tests and never one of its fields.</summary>
		public static int ClaimSite(int siteId) => ~siteId;

		/// <summary>
		/// Lays a site's farmsteads beside its laid fields, then hangs the doors they added where the doors builder has run.
		/// Returns how many were laid.
		/// </summary>
		public static int Lay(PointOfInterestSiteContext context, IReadOnlyList<LaidField> fields)
		{
			PointOfInterestRecord record = context?.Record;
			if (record == null || fields == null || fields.Count == 0 || context.GroundAt == null)
			{
				return 0;
			}
			(int min, int max) = CountFor(record.Kind);
			if (max <= 0)
			{
				return 0;
			}
			string style = context.Style ?? string.Empty;
			if (string.Equals(style, PointOfInterestTemplateSpecs.Hide, StringComparison.OrdinalIgnoreCase))
			{
				// Herders under hide keep their beasts on the open range: no barns.
				return 0;
			}
			float stature = RaceStature.BuildingScale(record.Race);
			int wanted = min + (int)(PointOfInterestPlanner.Hash(record.SiteSeed, FarmSalt, 0, 0) % (uint)(max - min + 1));
			if (stature > 1.05f && wanted > 0)
			{
				wanted = Mathf.Max(1, Mathf.RoundToInt(wanted / (stature * stature)));
			}
			// A barn to a farmstead, and one or two barns to a settlement (2026-10-20, PointOfInterestStructureCaps): its other fields
			// are worked from those, however many it has.
			PointOfInterestStructureCaps.Tally caps = PointOfInterestStructureCaps.Tally.Of(context);
			wanted = Mathf.Min(wanted, caps.Room(BarnType));
			if (wanted <= 0)
			{
				return 0;
			}
			int firstDoor = context.Doors.Count;
			var lay = new Farm(context, fields, stature, caps);
			int laid = record.Kind == POIType.AbandonedFarm ? lay.Ruin() : 0;
			if (record.Kind != POIType.AbandonedFarm)
			{
				for (int k = 0; k < wanted; k++)
				{
					laid += lay.Farmstead(k) ? 1 : 0;
				}
			}
			HangLateDoors(context, firstDoor);
			if (laid > 0 || lay.Missing > 0)
			{
				context.Notes?.Add($"{record.Name}: {laid} farmstead(s) laid beside its fields" +
					(lay.Missing > 0 ? $"; {lay.Missing} piece(s) have no art yet (regenerate the structure art)." : "."));
			}
			return laid;
		}

		/// <summary>
		/// Hangs the doors laid from index <paramref name="first"/> on, where the doors builder has already run for this site (it is
		/// registered before the fields builder): the same builder, given only those doors, its objects then numbered on from the
		/// site's own. Where it runs after, it hangs them itself and this does nothing. The outskirts' holdings, laid after the
		/// farmsteads, hang theirs through it too (PointOfInterestOutskirts).
		/// </summary>
		internal static void HangLateDoors(PointOfInterestSiteContext context, int first)
		{
			int count = context.Doors.Count - first;
			if (count <= 0)
			{
				return;
			}
			IReadOnlyList<IPointOfInterestSiteBuilder> builders = PointOfInterestSiteBuilders.All;
			int doorsAt = -1, fieldsAt = -1;
			for (int i = 0; i < builders.Count; i++)
			{
				if (builders[i] is PointOfInterestDoors)
				{
					doorsAt = i;
				}
				else if (builders[i] is PointOfInterestFields)
				{
					fieldsAt = i;
				}
			}
			if (doorsAt < 0 || fieldsAt < 0 || doorsAt > fieldsAt)
			{
				return;
			}
			PointOfInterestSiteContext late = PointOfInterestLinearFeatures.Copy(context);
			late.Doors = context.Doors.GetRange(first, count);
			late.Rooms = new List<PointOfInterestSiteContext.PlacedRoom>();
			late.Laid = context.Laid;
			int before = context.Root != null ? context.Root.childCount : 0;
			builders[doorsAt].Build(late);
			if (context.Root == null)
			{
				return;
			}
			// Numbered on from the site's own doors ("Door 1" .. ), not from one again.
			for (int i = 0; i < count && before + i < context.Root.childCount; i++)
			{
				Transform door = context.Root.GetChild(before + i);
				if (door.name == PointOfInterestFeatureKit.UniqueName(context, $"Door {i + 1}"))
				{
					door.name = PointOfInterestFeatureKit.UniqueName(context, $"Door {first + i + 1}");
				}
			}
		}

		/// <summary>One site's farmsteads as they are laid: the ground's tests, the pieces and what was claimed.</summary>
		private sealed class Farm
		{
			private readonly PointOfInterestSiteContext context;
			private readonly PointOfInterestRecord record;
			private readonly IReadOnlyList<LaidField> fields;
			private readonly float stature;
			private readonly PathExclusion ways;
			private readonly List<ScenePath> routes = new List<ScenePath>();
			private readonly List<(Vector2 Centre, float Yaw, Vector2 Size)> placed = new List<(Vector2, float, Vector2)>();
			/// <summary>The wells standing at the site and its farmsteads so far: a farmstead near one keeps a water butt.</summary>
			private readonly List<Vector2> wells = new List<Vector2>();
			private readonly string style;
			private readonly bool allowLocal, stone;
			private readonly StructureFinish finish;
			private readonly BiomeStructurePalette palette;
			/// <summary>The settlement's capped buildings laid so far, its farmsteads' among them as they are laid.</summary>
			private readonly PointOfInterestStructureCaps.Tally caps;
			public int Missing;

			public Farm(PointOfInterestSiteContext context, IReadOnlyList<LaidField> fields, float stature, PointOfInterestStructureCaps.Tally caps)
			{
				this.context = context;
				this.caps = caps;
				record = context.Record;
				this.fields = fields;
				this.stature = stature;
				style = context.Style ?? string.Empty;
				if (context.Points?.Paths != null)
				{
					foreach (ScenePath path in context.Points.Paths)
					{
						if (path != null && path.Class != ScenePathClass.Yard && path.Class != ScenePathClass.Street && path.Count >= 2)
						{
							routes.Add(path);
						}
					}
				}
				ways = new PathExclusion(routes);
				if (context.Laid != null)
				{
					foreach (PointOfInterestSiteContext.PlacedProp laid in context.Laid)
					{
						if (laid.Prefab != null && StructurePieces.TryParsePrefab(laid.Prefab.name, out StructurePiece piece, out _) && piece.HasTag("well"))
						{
							wells.Add(new Vector2(laid.Position.x, laid.Position.z));
						}
					}
				}
				allowLocal = context.Scene.IsValid() && LocalArtScope.IsLocalScenePath(context.Scene.path);
				stone = record.Kind == POIType.Monastery || string.Equals(style, PointOfInterestTemplateSpecs.Stone, StringComparison.OrdinalIgnoreCase);
				BiomeTemplate biome = context.BiomeAt?.Invoke(record.Position.x, record.Position.z);
				palette = BiomeTemplateArt.StructurePalette(biome);
				// Weathered as the site's own pieces are, by its props feature's finish.
				finish = StructureFinish.None;
				if (context.Template?.Features != null)
				{
					foreach (PointOfInterestFeature feature in context.Template.Features)
					{
						if (feature is PropsFeature props)
						{
							finish = PropsFeature.FinishFor(props.Finish, props.FinishChance, record.Kind, BiomeTemplateArt.Weathering(biome), 0f,
								PointOfInterestPlanner.Unit(PointOfInterestPlanner.Hash(record.SiteSeed, FarmSalt, 0x46494E, 0)));
							break;
						}
					}
				}
			}

			private float U(int a, int b, int c) => PointOfInterestPlanner.Unit(PointOfInterestPlanner.Hash(record.SiteSeed, FarmSalt, a * 4099 + b, c));

			private GameObject Kit(string id, int variant, StructureFinish withFinish)
			{
				GameObject prefab = StructureKit.Prefab(id, variant, withFinish, allowLocal);
				if (prefab == null && withFinish != StructureFinish.None)
				{
					prefab = StructureKit.Prefab(id, variant, StructureFinish.None, allowLocal);
				}
				if (prefab == null)
				{
					Missing++;
				}
				return prefab;
			}

			private static Vector2 FootprintOf(string id)
			{
				StructurePiece piece = StructurePieces.Find(id);
				return piece != null ? piece.Footprint : new Vector2(3f, 3f);
			}

			/// <summary>An abandoned farm's barn: its burnt-out shell beside one of its fields.</summary>
			public int Ruin()
			{
				if (U(9, 0, 0) > 0.85f)
				{
					return 0;
				}
				Vector2 size = FootprintOf("RuinedBarn") * stature + Vector2.one * (2f * Margin);
				for (int attempt = 0; attempt < 24; attempt++)
				{
					if (!Candidate(0, attempt, size, out Vector2 centre, out float yaw))
					{
						continue;
					}
					GameObject prefab = Kit("RuinedBarn", U(9, attempt, 1) < 0.5f ? 0 : 1, StructureFinish.Charred);
					if (prefab == null)
					{
						return 0;
					}
					Seat(prefab, "RuinedBarn", centre, yaw);
					Claim(centre, yaw, size);
					return 1;
				}
				return 0;
			}

			/// <summary>Lays the <paramref name="k"/>th farmstead where one fits; false where none does.</summary>
			public bool Farmstead(int k)
			{
				if (caps.Room(BarnType) <= 0)
				{
					return false;
				}
				int seed = unchecked((int)PointOfInterestPlanner.Hash(record.SiteSeed, FarmSalt, k, 1));
				// The house as the site's own houses are, by its style.
				var houseRequest = new PointOfInterestPieceRequest(PointOfInterestTemplateSpecs.House, style, seed, 0f, finish, allowLocal) { Palette = palette };
				GameObject house = PointOfInterestPieceResolver.Resolve(houseRequest);
				PointOfInterestPieceMetrics houseMetrics = PointOfInterestPieceResolver.Measure(houseRequest);
				int barnVariant = stone ? 1 : U(k, 0, 2) < 0.2f ? 1 : 0;
				var sizes = new Sizes
				{
					House = houseMetrics.Footprint,
					Barn = FootprintOf("Barn"),
					Rick = FootprintOf("HayRick"),
					Sty = FootprintOf("Pigsty"),
					Hen = FootprintOf("HenHouse"),
					Well = Vector2.Max(FootprintOf("Well"), FootprintOf("WaterButt")),
					Cart = FootprintOf("Cart"),
					Woodpile = FootprintOf("Woodpile"),
				}.Scaled(stature);
				Mix mix = MixFor(seed);
				if (record.Kind == POIType.Windmill)
				{
					// The miller's holding: his house, his barn and a rick.
					mix = new Mix { Ricks = 1, Sty = false, Hen = mix.Hen };
				}
				// Only the stock the settlement still has room for: past its two sties and two hen houses a farmstead keeps neither.
				mix.Sty &= caps.Room(StyType) > 0;
				mix.Hen &= caps.Room(HenType) > 0;
				Plan plan = Layout(sizes, mix, seed);
				// The fence stands a little outside the yard.
				Vector2 claimed = plan.Size + Vector2.one * 2f;
				for (int attempt = 0; attempt < 30; attempt++)
				{
					if (!Candidate(k + 1, attempt, claimed, out Vector2 centre, out float yaw) || !BuildingsFit(plan, centre, yaw))
					{
						continue;
					}
					Build(plan, centre, yaw, house, houseMetrics, barnVariant, seed, k);
					Claim(centre, yaw, claimed);
					return true;
				}
				return false;
			}

			/// <summary>
			/// A spot for a farmstead's ground (its <paramref name="size"/>) beside one of the site's fields, drawn by
			/// <paramref name="k"/> and <paramref name="attempt"/>: square to the field, a field's gap off one of its sides, its gate
			/// (−z) turned toward the nearest way, on ground that <see cref="Fits"/>.
			/// </summary>
			private bool Candidate(int k, int attempt, Vector2 size, out Vector2 centre, out float yaw)
			{
				centre = default;
				yaw = 0f;
				LaidField field = fields[(int)(PointOfInterestPlanner.Hash(record.SiteSeed, FarmSalt, k * 131 + attempt, 3) % (uint)fields.Count)];
				int side = (int)(PointOfInterestPlanner.Hash(record.SiteSeed, FarmSalt, k * 131 + attempt, 4) & 3);
				float along = U(k * 131 + attempt, 5, 0) - 0.5f;
				Vector2 ax = PointOfInterestLayouts.Rotate(Vector2.right, field.Yaw), az = PointOfInterestLayouts.Rotate(Vector2.up, field.Yaw);
				Vector2 normal = side == 0 ? az : side == 1 ? -az : side == 2 ? ax : -ax;
				Vector2 tangent = side < 2 ? ax : az;
				float halfNormal = side < 2 ? field.Size.y * 0.5f : field.Size.x * 0.5f;
				float halfTangent = side < 2 ? field.Size.x * 0.5f : field.Size.y * 0.5f;
				Vector2 edge = field.Centre + normal * halfNormal + tangent * (along * halfTangent);
				// Its gate toward the nearest way (or the settlement): the quarter turn of the field's frame whose −z looks that way.
				Vector2 toward = NearestWay(edge, 150f, out Vector2 way) ? way : new Vector2(record.Position.x, record.Position.z);
				Vector2 to = toward - edge;
				float best = float.NegativeInfinity;
				for (int q = 0; q < 4; q++)
				{
					float y = field.Yaw + q * 90f;
					float facing = Vector2.Dot(-PointOfInterestLayouts.Rotate(Vector2.up, y), to);
					if (facing > best)
					{
						best = facing;
						yaw = y;
					}
				}
				Vector2 fx = PointOfInterestLayouts.Rotate(Vector2.right, yaw), fz = PointOfInterestLayouts.Rotate(Vector2.up, yaw);
				float reach = Mathf.Abs(Vector2.Dot(normal, fx)) * size.x * 0.5f + Mathf.Abs(Vector2.Dot(normal, fz)) * size.y * 0.5f;
				centre = edge + normal * (PointOfInterestFieldClaims.Gap + 1f + reach);
				return Fits(centre, yaw, size);
			}

			/// <summary>Whether a farmstead's ground lies clear: in the scene, dry, gentle, open, off the ways, outside every site, off every field and farmstead.</summary>
			private bool Fits(Vector2 centre, float yaw, Vector2 size)
			{
				if (context.FieldClaims != null && !context.FieldClaims.Clear(ClaimSite(record.Id), centre, yaw, size))
				{
					return false;
				}
				// The site's own fields and farmsteads (a harness has no claims), kept a field's gap apart.
				Vector2 grown = size + Vector2.one * PointOfInterestFieldClaims.Gap;
				foreach (LaidField f in fields)
				{
					if (PointOfInterestLayouts.Overlaps(centre, yaw, grown, f.Centre, f.Yaw, f.Size + Vector2.one * PointOfInterestFieldClaims.Gap))
					{
						return false;
					}
				}
				foreach ((Vector2 c, float y, Vector2 s) in placed)
				{
					if (PointOfInterestLayouts.Overlaps(centre, yaw, grown, c, y, s + Vector2.one * PointOfInterestFieldClaims.Gap))
					{
						return false;
					}
				}
				var site = new Vector2(record.Position.x, record.Position.z);
				Vector2 ax = PointOfInterestLayouts.Rotate(Vector2.right, yaw), az = PointOfInterestLayouts.Rotate(Vector2.up, yaw);
				int nx = Mathf.Max(2, Mathf.CeilToInt(size.x / 4f)), nz = Mathf.Max(2, Mathf.CeilToInt(size.y / 4f));
				for (int i = 0; i <= nx; i++)
				{
					for (int j = 0; j <= nz; j++)
					{
						Vector2 p = centre + ax * ((i / (float)nx - 0.5f) * size.x) + az * ((j / (float)nz - 0.5f) * size.y);
						if (!context.InScene(p.x, p.y))
						{
							return false;
						}
						float ground = context.GroundAt(p.x, p.y);
						if (float.IsNaN(ground) || float.IsInfinity(ground) || context.SkyAt != null && context.SkyAt(p.x, p.y))
						{
							return false;
						}
						if (context.SlopeAt != null && context.SlopeAt(p.x, p.y) > MaxSlopeDegrees)
						{
							return false;
						}
						if (context.Water != null && context.Water.KindAt(p.x, p.y) != WaterKind.None || context.IsWet(p.x, p.y, 0.6f))
						{
							return false;
						}
						if (context.Points != null && context.Points.IsKeptOut(p.x, p.y) || ways.Trees(p.x, p.y))
						{
							return false;
						}
						Vector2 d = p - site;
						float bearing = Mathf.Atan2(d.x, d.y) * Mathf.Rad2Deg;
						if (d.magnitude < PointOfInterestOutline.ReachToward(record.Outline, record.Radius, bearing) * 1.05f + 6f)
						{
							return false;
						}
					}
				}
				return true;
			}

			/// <summary>Whether each building of a plan stands on ground falling no more than <see cref="MaxFall"/> across it.</summary>
			private bool BuildingsFit(Plan plan, Vector2 centre, float yaw)
			{
				var corners = new Vector2[4];
				foreach (Placed part in plan.Parts)
				{
					if (part.Part != Part.Barn && part.Part != Part.House && part.Part != Part.Sty)
					{
						continue;
					}
					Vector2 at = centre + PointOfInterestLayouts.Rotate(part.At, yaw);
					PointOfInterestLayouts.Corners(at, yaw, part.Extent, corners);
					float lo = context.GroundAt(at.x, at.y), hi = lo;
					foreach (Vector2 c in corners)
					{
						float g = context.GroundAt(c.x, c.y);
						lo = Mathf.Min(lo, g);
						hi = Mathf.Max(hi, g);
					}
					if (hi - lo > MaxFall * stature)
					{
						return false;
					}
				}
				return true;
			}

			/// <summary>Lays a planned farmstead at <paramref name="centre"/> turned to <paramref name="yaw"/>: its pieces, the ground they wear, its fence and gate, and the track out to the nearest way.</summary>
			private void Build(Plan plan, Vector2 centre, float yaw, GameObject house, PointOfInterestPieceMetrics houseMetrics, int barnVariant, int seed, int k)
			{
				int n = 0;
				foreach (Placed part in plan.Parts)
				{
					Vector2 at = centre + PointOfInterestLayouts.Rotate(part.At, yaw);
					float partYaw = yaw + part.Yaw;
					int v(int pieces, int salt) => (int)(PointOfInterestPlanner.Hash(seed, FarmSalt, 0x50 + n, salt) % (uint)pieces);
					switch (part.Part)
					{
						case Part.House:
							if (house == null)
							{
								Missing++;
								break;
							}
							SeatPrefab(house, at, partYaw, houseMetrics.Footprint * stature, houseMetrics.Depth * stature);
							Floor(at, partYaw, houseMetrics.Footprint * stature);
							break;
						case Part.Barn:
							caps.TakeOne(BarnType);
							Floor(at, partYaw, FootprintOf("Barn") * stature);
							Seat(Kit("Barn", barnVariant, finish), "Barn", at, partYaw);
							break;
						case Part.Rick:
							Seat(Kit("HayRick", v(2, 1), StructureFinish.None), "HayRick", at, partYaw);
							break;
						case Part.Sty:
							caps.TakeOne(StyType);
							Floor(at, partYaw, FootprintOf("Pigsty") * stature);
							Seat(Kit("Pigsty", stone ? 0 : v(2, 1), finish), "Pigsty", at, partYaw);
							break;
						case Part.HenHouse:
							caps.TakeOne(HenType);
							Seat(Kit("HenHouse", v(2, 1), finish), "HenHouse", at, partYaw);
							break;
						case Part.Well:
							// A well of its own unless one stands within PointOfInterestLayouts.WellSpacing, or the settlement has its two
							// wells (PointOfInterestStructureCaps); a water butt then.
							bool well = v(2, 1) == 0 && !NearAWell(at) && caps.TakeOne(WellType);
							if (well)
							{
								wells.Add(at);
							}
							Seat(Kit(well ? "Well" : "WaterButt", v(2, 2), finish), well ? "Well" : "WaterButt", at, partYaw + v(4, 3) * 90f);
							break;
						case Part.Cart:
							Seat(Kit("Cart", v(2, 1), StructureFinish.None), "Cart", at, partYaw + (v(2, 2) == 0 ? 0f : 180f) + (U(k, n, 9) - 0.5f) * 14f);
							break;
						case Part.Woodpile:
							Seat(Kit("Woodpile", v(2, 1), StructureFinish.None), "Woodpile", at, partYaw);
							break;
					}
					n++;
				}
				// The yard trodden bare between the barn and the stock, from the gate to the house's door.
				Vector2 from = centre + PointOfInterestLayouts.Rotate(new Vector2(0f, plan.YardFrom), yaw);
				Vector2 to = centre + PointOfInterestLayouts.Rotate(new Vector2(0f, plan.YardTo), yaw);
				Yard(from, to, plan.YardHalf, 0.7f, ScenePathPointFlags.Squared);
				// Fenced in its biome's field-edge style, its gate in the middle of the front, else the kit's fence and a gate.
				Vector2 gate = centre + PointOfInterestLayouts.Rotate(new Vector2(0f, -plan.Size.y * 0.5f - 1f), yaw);
				Vector2 outward = PointOfInterestLayouts.Rotate(Vector2.down, yaw);
				Vector2 claimed = plan.Size + Vector2.one * 2f;
				if (!PointOfInterestLinearFeatures.LayFieldEdges(context, centre, yaw, claimed, gate + outward * 1000f, false, 0x46A00 + k))
				{
					KitFence(centre, yaw, claimed, seed);
				}
				// Out of the gate to the nearest way, where it is near and the ground between is open; else a few paces of track.
				Vector2 end = gate + outward * 6f;
				if (NearestWay(gate, 80f, out Vector2 way) && OpenBetween(gate, way, centre))
				{
					end = way;
				}
				Yard(gate, end, 1.2f, 0.75f, ScenePathPointFlags.None);
			}

			private bool NearAWell(Vector2 at)
			{
				foreach (Vector2 w in wells)
				{
					if ((w - at).sqrMagnitude < PointOfInterestLayouts.WellSpacing * PointOfInterestLayouts.WellSpacing)
					{
						return true;
					}
				}
				return false;
			}

			/// <summary>A kit piece seated on the ground at <paramref name="at"/>, built to the site's stature.</summary>
			private void Seat(GameObject prefab, string id, Vector2 at, float yaw)
			{
				if (prefab == null)
				{
					return;
				}
				StructurePiece piece = StructurePieces.Find(id);
				Vector2 footprint = piece != null ? piece.Footprint : Vector2.one;
				float depth = piece != null ? piece.Depth : 0.3f;
				SeatPrefab(prefab, at, yaw, footprint * stature, depth * stature);
			}

			/// <summary>
			/// A piece seated as a settlement's are (PropsFeature.SeatHeight): no higher than its centre's ground and low enough that
			/// its foundation reaches its lowest corner's.
			/// </summary>
			private void SeatPrefab(GameObject prefab, Vector2 at, float yaw, Vector2 footprint, float depth)
			{
				var corners = new Vector2[4];
				PointOfInterestLayouts.Corners(at, yaw, footprint, corners);
				float centreGround = context.GroundAt(at.x, at.y), lowest = centreGround;
				foreach (Vector2 c in corners)
				{
					lowest = Mathf.Min(lowest, context.GroundAt(c.x, c.y));
				}
				float y = Mathf.Min(centreGround, lowest + Mathf.Max(0f, depth) * 0.9f);
				context.AddProp(prefab, new Vector3(at.x, y, at.y), Quaternion.Euler(0f, yaw, 0f), Vector3.one * stature);
			}

			/// <summary>A building's plan as a floor (its grass kept out of it) and an apron round it, as a settlement's buildings wear their ground.</summary>
			private void Floor(Vector2 at, float yaw, Vector2 footprint)
			{
				bool alongX = footprint.x >= footprint.y;
				Vector2 axis = PointOfInterestLayouts.Rotate(alongX ? Vector2.right : Vector2.up, yaw);
				float longest = Mathf.Max(footprint.x, footprint.y), shortest = Mathf.Min(footprint.x, footprint.y);
				Yard(at - axis * (longest * 0.5f + 0.5f), at + axis * (longest * 0.5f + 0.5f), shortest * 0.5f + 0.5f, 0.85f, ScenePathPointFlags.Squared);
				Yard(at - axis * (longest * 0.5f), at + axis * (longest * 0.5f), shortest * 0.5f, 1f, ScenePathPointFlags.Squared | ScenePathPointFlags.Floor);
			}

			/// <summary>Worn yard earth from <paramref name="a"/> to <paramref name="b"/>, <paramref name="half"/> metres either side.</summary>
			private void Yard(Vector2 a, Vector2 b, float half, float wear, ScenePathPointFlags shape)
			{
				if (context.Points?.Paths == null)
				{
					return;
				}
				int id = 0;
				foreach (ScenePath existing in context.Points.Paths)
				{
					id = Math.Max(id, existing != null ? existing.Id : 0);
				}
				byte flags = (byte)(ScenePathPointFlags.InSite | shape);
				context.Points.Paths.Add(new ScenePath
				{
					Id = id + 1,
					Class = ScenePathClass.Yard,
					FromId = -1,
					ToId = record.Id,
					Points = new[] { new Vector3(a.x, context.GroundAt(a.x, a.y), a.y), new Vector3(b.x, context.GroundAt(b.x, b.y), b.y) },
					HalfWidth = new[] { half, half },
					Wear = new[] { wear, wear },
					Surface = new[] { (byte)ScenePathSurface.Earth, (byte)ScenePathSurface.Earth },
					Flags = new[] { flags, flags },
				});
			}

			/// <summary>
			/// The kit's field fence round a farmstead and a timber field gate in the middle of its front, where its biome lines none:
			/// each module between its two ends on the ground, round the yard the same way so every corner takes one post, a closing
			/// post wherever a run stops with none (PointOfInterestLinearFeatures.Joins, 2026-10-19).
			/// </summary>
			private void KitFence(Vector2 centre, float yaw, Vector2 size, int seed)
			{
				GameObject gatePiece = Kit("LinearGate", 0, StructureFinish.None);
				var sides = PointOfInterestLinearFeatures.RectangleSides(centre, yaw, size, centre + PointOfInterestLayouts.Rotate(new Vector2(0f, -1000f), yaw));
				var joins = new PointOfInterestLinearFeatures.Joins();
				for (int s = 0; s < sides.Length; s++)
				{
					PointOfInterestLinearFeatures.Side side = sides[s];
					Vector2 run = side.To - side.From;
					float length = run.magnitude;
					int count = Mathf.Max(1, Mathf.RoundToInt(length / 3f));
					int gate = side.Gate ? PointOfInterestLinearFeatures.GateModule(count) : -1;
					for (int m = 0; m < count; m++)
					{
						Vector2 a = side.From + run * ((float)m / count), b = side.From + run * ((float)(m + 1) / count);
						Vector2 p = 0.5f * (a + b);
						if (context.IsWet(p.x, p.y, 0f) || ways.Trees(p.x, p.y))
						{
							continue;
						}
						Vector3 ja = PointOfInterestLinearFeatures.Joint(context, a), jb = PointOfInterestLinearFeatures.Joint(context, b);
						if (m == gate)
						{
							if (gatePiece != null)
							{
								joins.Gate(context, context.Palette, gatePiece, ja, jb, StructurePieces.GateModule);
							}
							continue;
						}
						int variant = (int)(PointOfInterestPlanner.Hash(seed, FarmSalt, s * 64 + m, 11) & 1);
						GameObject prefab = Kit("FieldFence", variant, StructureFinish.None);
						if (prefab != null)
						{
							joins.Module(context, context.Palette, prefab, "FieldFence", variant, ja, jb, 3f, 0f, 1f, 0f);
						}
					}
				}
				joins.Close();
			}

			/// <summary>The nearest point on a way within <paramref name="reach"/> metres of <paramref name="p"/>.</summary>
			private bool NearestWay(Vector2 p, float reach, out Vector2 nearest)
			{
				nearest = p;
				float best = reach;
				bool found = false;
				foreach (ScenePath path in routes)
				{
					for (int i = 0; i + 1 < path.Count; i++)
					{
						Vector3 a3 = path.Points[i], b3 = path.Points[i + 1];
						var a = new Vector2(a3.x, a3.z);
						var b = new Vector2(b3.x, b3.z);
						Vector2 ab = b - a;
						float t = ab.sqrMagnitude > 1e-6f ? Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude) : 0f;
						Vector2 q = a + ab * t;
						float d = (q - p).magnitude;
						if (d < best)
						{
							best = d;
							nearest = q;
							found = true;
						}
					}
				}
				return found;
			}

			/// <summary>Whether a track may run straight from a farmstead's gate to a way: dry, gentle, off every field and the farmstead itself.</summary>
			private bool OpenBetween(Vector2 a, Vector2 b, Vector2 own)
			{
				float length = (b - a).magnitude;
				int steps = Mathf.Max(1, Mathf.CeilToInt(length / 2f));
				for (int i = 1; i < steps; i++)
				{
					Vector2 p = Vector2.Lerp(a, b, i / (float)steps);
					if (context.IsWet(p.x, p.y, 0.2f) || context.SlopeAt != null && context.SlopeAt(p.x, p.y) > 14f)
					{
						return false;
					}
					foreach (LaidField f in fields)
					{
						if (PointOfInterestPaddies.Inside(f.Centre, f.Yaw, f.Size, p.x, p.y) > -1f)
						{
							return false;
						}
					}
					if (context.FieldClaims != null)
					{
						foreach (PointOfInterestFieldClaims.Claim claim in context.FieldClaims.All)
						{
							if (claim.Centre != own && PointOfInterestPaddies.Inside(claim.Centre, claim.Yaw, claim.Size, p.x, p.y) > -1f)
							{
								return false;
							}
						}
					}
				}
				return true;
			}

			private void Claim(Vector2 centre, float yaw, Vector2 size)
			{
				placed.Add((centre, yaw, size));
				context.FieldClaims?.Add(ClaimSite(record.Id), centre, yaw, size);
				// Its yard kept as a yard (2026-10-27, PointOfInterestEnclosureClearance): the scatter's trees, bushes and rocks off it, its grass kept.
				context.FieldClaims?.Enclose(centre, yaw, size, turf: true);
			}
		}

		// ── The fields dressed ────────────────────────────────────────

		/// <summary>
		/// Dresses a site's laid fields as worked (2026-10-19): a reaped field's stubble stood in rows of stooks and a rick in a
		/// corner; a scarecrow over some growing corn; a pasture's trough by its fence and perhaps a rick. Seeded by the site and
		/// each field's index (never the site's sequence). Returns the pieces laid.
		/// </summary>
		public static int Dress(PointOfInterestSiteContext context, IReadOnlyList<LaidField> fields, bool abandoned)
		{
			if (context?.Record == null || fields == null || abandoned || context.GroundAt == null)
			{
				return 0;
			}
			int seed = context.Record.SiteSeed, laid = 0;
			bool allowLocal = context.Scene.IsValid() && LocalArtScope.IsLocalScenePath(context.Scene.path);
			float stature = Mathf.Min(1f, RaceStature.BuildingScale(context.Record.Race));
			float U(int f, int a, int b) => PointOfInterestPlanner.Unit(PointOfInterestPlanner.Hash(seed, DressSalt, f * 8191 + a, b));
			void Put(string id, int variant, Vector2 p, float yaw, bool seat)
			{
				GameObject prefab = StructureKit.Prefab(id, variant, StructureFinish.None, allowLocal);
				if (prefab == null || context.IsWet(p.x, p.y, 0.05f))
				{
					return;
				}
				float y = context.GroundAt(p.x, p.y);
				StructurePiece piece = StructurePieces.Find(id);
				if (seat && piece != null)
				{
					var corners = new Vector2[4];
					PointOfInterestLayouts.Corners(p, yaw, piece.Footprint * stature, corners);
					float lowest = y;
					foreach (Vector2 c in corners)
					{
						lowest = Mathf.Min(lowest, context.GroundAt(c.x, c.y));
					}
					y = Mathf.Min(y, lowest + piece.Depth * stature * 0.9f);
				}
				context.AddProp(prefab, new Vector3(p.x, y, p.y), Quaternion.Euler(0f, yaw, 0f), Vector3.one * stature);
				laid++;
			}
			foreach (LaidField field in fields)
			{
				int f = field.Index;
				Vector2 ax = PointOfInterestLayouts.Rotate(Vector2.right, field.Yaw), az = PointOfInterestLayouts.Rotate(Vector2.up, field.Yaw);
				Vector2 Local(float u, float v) => field.Centre + ax * u + az * v;
				float hx = field.Size.x * 0.5f, hz = field.Size.y * 0.5f;
				switch (field.Use)
				{
					case FieldUse.Grain when field.Reaped:
					{
						List<(Vector2 At, float Yaw)> stooks = StookRows(field.Size, U(f, 1, 0), U(f, 2, 0));
						for (int i = 0; i < stooks.Count; i++)
						{
							Put("Stooks", U(f, 10 + i, 1) < 0.7f ? 0 : 1, Local(stooks[i].At.x, stooks[i].At.y), field.Yaw + stooks[i].Yaw + (U(f, 10 + i, 2) - 0.5f) * 24f, false);
						}
						if (U(f, 3, 0) < 0.5f)
						{
							Put("HayRick", U(f, 3, 1) < 0.5f ? 0 : 1, Corner(field, U(f, 3, 2), 6f, out float rickYaw), rickYaw, true);
						}
						break;
					}
					case FieldUse.Grain:
						if (U(f, 4, 0) < 0.55f)
						{
							Put("Scarecrow", U(f, 4, 1) < 0.5f ? 0 : 1, Local((U(f, 4, 2) - 0.5f) * hx * 0.6f, (U(f, 4, 3) - 0.5f) * hz * 0.6f), field.Yaw + U(f, 4, 4) * 360f, false);
						}
						break;
					case FieldUse.Pasture:
					{
						if (U(f, 5, 0) < 0.7f)
						{
							// By the middle of a side, a couple of metres in from the fence, along it.
							int side = (int)(U(f, 5, 1) * 4f) & 3;
							bool alongX = side < 2;
							float inset = 2.2f;
							Vector2 at = side == 0 ? new Vector2(0f, hz - inset) : side == 1 ? new Vector2(0f, -hz + inset) : side == 2 ? new Vector2(hx - inset, 0f) : new Vector2(-hx + inset, 0f);
							at += (alongX ? Vector2.right : Vector2.up) * ((U(f, 5, 2) - 0.5f) * (alongX ? hx : hz));
							Put("Trough", U(f, 5, 3) < 0.6f ? 0 : 1, Local(at.x, at.y), field.Yaw + (alongX ? 0f : 90f), true);
						}
						if (U(f, 6, 0) < 0.4f)
						{
							Put("HayRick", 0, Corner(field, U(f, 6, 1), 6f, out float rickYaw), rickYaw, true);
						}
						break;
					}
				}
			}
			if (laid > 0)
			{
				context.Notes?.Add($"{context.Record.Name}: its fields dressed as worked with {laid} piece(s): stooks on the reaped corn, ricks, scarecrows and troughs.");
			}
			return laid;
		}

		/// <summary>A point a rick stands at in one of a field's corners (drawn by <paramref name="roll"/>), <paramref name="inset"/> metres in from both sides, and a yaw square to the field.</summary>
		private static Vector2 Corner(LaidField field, float roll, float inset, out float yaw)
		{
			int corner = (int)(roll * 4f) & 3;
			float u = (corner & 1) == 0 ? -1f : 1f, v = (corner & 2) == 0 ? -1f : 1f;
			var at = new Vector2(u * Mathf.Max(0f, field.Size.x * 0.5f - inset), v * Mathf.Max(0f, field.Size.y * 0.5f - inset));
			yaw = field.Yaw + (corner & 1) * 90f;
			return field.Centre + PointOfInterestLayouts.Rotate(Vector2.right, field.Yaw) * at.x + PointOfInterestLayouts.Rotate(Vector2.up, field.Yaw) * at.y;
		}

		/// <summary>The headland round a reaped field kept free of stooks, metres.</summary>
		public const float StookHeadland = 3f;

		/// <summary>How far apart the rows of stooks stand, and the groups along a row, metres.</summary>
		public const float StookRowSpacing = 7f, StookSpacing = 6.5f;

		/// <summary>
		/// Where a reaped field's stooks stand (pure; the field's own frame): rows down its length <see cref="StookRowSpacing"/>
		/// apart inside a <see cref="StookHeadland"/>, a group of stooks every <see cref="StookSpacing"/> along each, each group's
		/// yaw along the row (its piece runs along x); the rows shifted by <paramref name="shift"/> and the groups by
		/// <paramref name="stagger"/> (each 0..1), so no two fields stand alike.
		/// </summary>
		public static List<(Vector2 At, float Yaw)> StookRows(Vector2 size, float shift, float stagger)
		{
			var rows = new List<(Vector2, float)>();
			bool alongX = size.x >= size.y;
			float length = Mathf.Max(size.x, size.y) - 2f * StookHeadland, width = Mathf.Min(size.x, size.y) - 2f * StookHeadland;
			// Each group's piece is about 5.5 m along its row and 1.3 across it.
			length -= 0.6f;
			width -= 1.4f;
			if (length < 5f || width < 0f)
			{
				return rows;
			}
			int across = Mathf.Max(1, Mathf.FloorToInt(width / StookRowSpacing) + 1);
			float used = (across - 1) * StookRowSpacing;
			float first = -used * 0.5f + (shift - 0.5f) * Mathf.Max(0f, width - used);
			int groups = Mathf.Max(1, Mathf.FloorToInt((length - 5f) / StookSpacing) + 1);
			float run = (groups - 1) * StookSpacing;
			for (int r = 0; r < across; r++)
			{
				float c = first + r * StookRowSpacing;
				float offset = ((r & 1) == 0 ? stagger : 1f - stagger) - 0.5f;
				float slack = Mathf.Max(0f, length - 5f - run);
				for (int g = 0; g < groups; g++)
				{
					float a = -run * 0.5f + g * StookSpacing + offset * slack;
					rows.Add((alongX ? new Vector2(a, c) : new Vector2(c, a), alongX ? 0f : 90f));
				}
			}
			return rows;
		}
	}
}
#endif
