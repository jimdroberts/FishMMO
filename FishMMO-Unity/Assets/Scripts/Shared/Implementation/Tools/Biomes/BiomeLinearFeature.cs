using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;

namespace FishMMO.Shared.Biomes
{
	/// <summary>
	/// What a linear feature is (2026-10-18, phase I2f, decisions 23 and 29): the long, narrow things a farmed or grazed country is
	/// divided and watered by, laid by the cut along lines (a field's edges, a lane's verges, a wood's margin, a site's bounds, a
	/// sheepfold's round, a terrace's lip) as baked props (PointOfInterestLinearFeatures). Append-only (stored by value).
	/// </summary>
	public enum BiomeLinearKind : byte
	{
		/// <summary>A living hedge: a planted row of the biome's own bushes (laid, clipped or left wild), its standard trees among them.</summary>
		Hedge = 0,
		/// <summary>A wall: drystone, mortared stone or brick, an earth bank, a row of slabs set on edge.</summary>
		Wall = 1,
		/// <summary>A fence: post and rail, leaning poles, wattle hurdles, pickets, split slabs, a palisade.</summary>
		Fence = 2,
		/// <summary>A row of trees: a filare of poplars, a row of pollards, trees trained as a vine's props (piantata).</summary>
		TreeRow = 3,
		/// <summary>A water channel: an irrigation leat, bisse or roggia carried at a gentle fall from the water to the fields.</summary>
		Channel = 4,
		/// <summary>A clearance row: the stones picked off a field heaped along its edge (clapas, consumption dykes).</summary>
		ClearanceRow = 5,
	}

	/// <summary>
	/// How a linear feature is built (<see cref="BiomeLinearStyle.Form"/>): each kind's own forms, numbered by tens per kind (the kind
	/// is the form's tens, <see cref="BiomeLinearFeatures.KindOf"/>). Append-only within each kind's ten (stored by value).
	/// </summary>
	public enum BiomeLinearForm : byte
	{
		// ── Hedges ──
		/// <summary>Cut and laid: the stems split near the ground and bent along the line (pleachers), woven between stakes, bound along the top.</summary>
		Laid = 0,
		/// <summary>Clipped close into a dense, even wall of leaf over its stems.</summary>
		Clipped = 1,
		/// <summary>Grown out: tall, shaggy and gapped, standard trees left to grow up through it.</summary>
		Wild = 2,

		// ── Walls ──
		/// <summary>Drystone: two faces of unmortared field or quarry stone, hearted with rubble, through-stones, a coping of stones on edge.</summary>
		Drystone = 10,
		/// <summary>Mortared stone or brick under a capping, a pier every few metres (a garden's brolo, a park's wall).</summary>
		Mortared = 11,
		/// <summary>An earth bank, turfed over, with a ditch beside it where its earth came from.</summary>
		EarthBank = 12,
		/// <summary>Slabs of stone set upright on edge side by side (the Alpine Plattenzaun, the Caithness flagstone fence).</summary>
		UprightSlabs = 13,

		// ── Fences ──
		/// <summary>Posts and two or three rails (the structure kit's field fence).</summary>
		Rail = 20,
		/// <summary>Split poles leant at a slant between pairs of upright stakes and tied to them (the Carpathian gard).</summary>
		LeaningPole = 21,
		/// <summary>Wattle: hazel or willow rods woven between upright sails, in hurdles.</summary>
		Wattle = 22,
		/// <summary>Pointed pickets nailed or pegged to two rails (the Alpine Stackete).</summary>
		Picket = 23,
		/// <summary>Split slabs or boards set upright close together on rails.</summary>
		Slab = 24,
		/// <summary>Sharpened logs on rails (the structure kit's palisade).</summary>
		Palisade = 25,

		// ── Tree rows ──
		/// <summary>A filare: one species in a straight row at an even spacing (Lombardy poplars along a road).</summary>
		Filare = 30,
		/// <summary>Pollards: trees cut back to a head above browsing height, closer set (willows on a ditch bank).</summary>
		Pollard = 31,
		/// <summary>Piantata: trees trained as props with vines swagged in garlands from tree to tree.</summary>
		Piantata = 32,

		// ── Channels ──
		/// <summary>An earth-cut leat or ditch with a bank of its own spoil on the downhill side, plank sluices at its falls.</summary>
		EarthDitch = 40,
		/// <summary>A lined channel: its head and its falls built in stone or brick, stone partitions where it divides (the roggia).</summary>
		LinedChannel = 41,

		// ── Clearance rows ──
		/// <summary>Field stones heaped loose in a long low ridge.</summary>
		Heaped = 50,
		/// <summary>A clearance bank grown over with turf, only its stones' tops showing.</summary>
		Turfed = 51,
	}

	/// <summary>Where a linear feature is laid (<see cref="BiomeLinearStyle.Where"/>). Append-only (stored by value).</summary>
	public enum BiomeLinearPlace : byte
	{
		/// <summary>Round the fields the settlements lay (a field's boundary, with a gate on its side toward the settlement); for a channel, the leat that waters them.</summary>
		FieldEdges = 0,
		/// <summary>Along the routed ways (cart tracks, roads, footpaths), on their verges.</summary>
		Lanes = 1,
		/// <summary>Along a wood's margin, where its stands give way to open ground.</summary>
		ClearingEdges = 2,
		/// <summary>Round a small site's ground (a farmstead, a monastery's precinct, a mill, a hermitage), with gaps where its ways come in.</summary>
		SiteBounds = 3,
		/// <summary>A sheepfold or pen beside a pastoral site (a camp, a village, a farm).</summary>
		Folds = 4,
		/// <summary>Along the lip of a terrace (laid by the terraced fields' own builder, PointOfInterestLinearFeatures.LayAlong).</summary>
		TerraceEdges = 5,
	}

	/// <summary>Which ways a lane style follows (<see cref="BiomeLinearStyle.Ways"/>); none set: every routed way.</summary>
	[Flags]
	public enum BiomeLinearWays : byte
	{
		/// <summary>Every routed way.</summary>
		All = 0,
		/// <summary>Cart tracks.</summary>
		Tracks = 1 << 0,
		/// <summary>Roads, highways and ancient roads.</summary>
		Roads = 1 << 1,
		/// <summary>Footpaths and trails.</summary>
		Paths = 1 << 2,
	}

	/// <summary>Which side of a lane a style is laid on (<see cref="BiomeLinearStyle.Sides"/>).</summary>
	public enum BiomeLinearSides : byte
	{
		/// <summary>Both verges (a hedged lane).</summary>
		Both = 0,
		/// <summary>One verge, the same along a way's run (drawn by the way).</summary>
		One = 1,
	}

	/// <summary>
	/// One of a biome's linear-feature styles (<see cref="BiomeTemplate.LinearFeatures"/>, 2026-10-18, phase I2f): what is built
	/// (<see cref="Kind"/>, <see cref="Form"/>), where (<see cref="Where"/>), how much of it (<see cref="Density"/>), how high and how
	/// close its plants stand, its gates and gaps, the species it is grown of, and the material its stone or timber is laid in.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Nothing by default.</b> Every biome's list is empty until its round names its own (the biome's file, <c>.Lines(...)</c>),
	/// so no scene changes: fields keep the kit's post-and-rail fence and no lane, wood, site or fold gains anything.
	/// </para>
	/// <para>
	/// <b>A field's edge</b> is taken by the first field-edge style (in list order, channels apart) the field's draw admits by
	/// <see cref="Density"/>; that field is then not fenced with the kit's fence. A field none admits keeps the kit's fence.
	/// </para>
	/// </remarks>
	[Serializable]
	public struct BiomeLinearStyle
	{
		[Tooltip("What is built: a hedge, a wall, a fence, a row of trees, a water channel, a clearance row of field stones.")]
		public BiomeLinearKind Kind;
		[Tooltip("How it is built (the kind's own forms: a laid, clipped or wild hedge; a drystone, mortared, earth-bank or upright-slab wall; a rail, leaning-pole, wattle, picket, slab or palisade fence; a filare, pollard or piantata row; an earth or lined channel; a heaped or turfed clearance row).")]
		public BiomeLinearForm Form;
		[Tooltip("Where it is laid: round the fields, along the lanes, along a wood's margin, round a small site's ground, as a fold beside a pastoral site, along a terrace's lip.")]
		public BiomeLinearPlace Where;
		[Tooltip("How much of the places it may go it is laid on (each field, lane run, wood edge, site or fold drawn alone): 1 everywhere, 0 nowhere.")]
		[Range(0f, 1f)] public float Density;
		[Tooltip("How high it stands, metres. 0: the form's own (BiomeLinearFeatures.DefaultHeight).")]
		[Min(0f)] public float Height;
		[Tooltip("How far apart its plants stand, metres: a hedge's bushes, a row's trees. 0: the form's own.")]
		[Min(0f)] public float Spacing;
		[Tooltip("How wide its gates and gaps are, metres. 0: the form's own.")]
		[Min(0f)] public float GateWidth;
		[Tooltip("The share of its length broken or missing (a tumbled wall, a gapped hedge, a fence with rails down): 0 kept whole.")]
		[Range(0f, 1f)] public float Breaks;
		[Tooltip("Lanes only: both verges or one.")]
		public BiomeLinearSides Sides;
		[Tooltip("Lanes only: which ways (none ticked: every routed way).")]
		public BiomeLinearWays Ways;
		[Tooltip("Lanes and channels only: how far from a settlement it is laid, metres (0: anywhere in the biome).")]
		[Min(0f)] public float Reach;
		[Tooltip("What it is grown of: a hedge's bushes, a row's trees (one is drawn per plant). A hedge with none is not laid; walls, fences and channels ignore it.")]
		public List<GameObject> Species;
		[Tooltip("A hedge's standard trees, one every few times its spacing (none: no standards).")]
		public List<GameObject> Standards;
		[Tooltip("Piantata only: the vine swagged from tree to tree, by species name (Ivy, Creeper…). Empty: the biome's own vines.")]
		public string Vine;
		[Tooltip("Lay its stone, brick or timber in a material of its own (below) instead of the biome's structure palette's.")]
		public bool OwnMaterial;
		[Tooltip("With Own Material: what its stone, brick or timber is.")]
		public BiomeStructureMaterial Material;
		[Tooltip("With Own Material: the material's colour over its surface (white keeps the surface's own).")]
		public Color Tint;

		/// <summary>A style of a kind and form laid at a place, everything else the form's own (density 1).</summary>
		public BiomeLinearStyle(BiomeLinearForm form, BiomeLinearPlace where, float density = 1f)
		{
			Kind = BiomeLinearFeatures.KindOf(form);
			Form = form;
			Where = where;
			Density = density;
			Height = 0f;
			Spacing = 0f;
			GateWidth = 0f;
			Breaks = 0f;
			Sides = BiomeLinearSides.Both;
			Ways = BiomeLinearWays.All;
			Reach = 0f;
			Species = new List<GameObject>();
			Standards = new List<GameObject>();
			Vine = string.Empty;
			OwnMaterial = false;
			Material = BiomeStructureMaterial.Timber;
			Tint = Color.white;
		}

		/// <summary>
		/// Every value, exactly, for the authoring's marks: "Kind/Form@Where ×density h… s… g… b… sides ways reach [species] [standards]
		/// vine material", prefabs by name.
		/// </summary>
		public override string ToString()
		{
			var text = new StringBuilder(128);
			text.Append(Kind).Append('/').Append(Form).Append('@').Append(Where)
				.Append(" ×").Append(R(Density)).Append(" h").Append(R(Height)).Append(" s").Append(R(Spacing)).Append(" g").Append(R(GateWidth))
				.Append(" b").Append(R(Breaks)).Append(' ').Append(Sides).Append(' ').Append((byte)Ways).Append(" r").Append(R(Reach));
			text.Append(" [").Append(Names(Species)).Append("] [").Append(Names(Standards)).Append("] ").Append(Vine ?? string.Empty);
			if (OwnMaterial)
			{
				text.Append(" =").Append(Material).Append('(').Append(R(Tint.r)).Append(',').Append(R(Tint.g)).Append(',').Append(R(Tint.b)).Append(',').Append(R(Tint.a)).Append(')');
			}
			return text.ToString();
		}

		private static string R(float value) => value.ToString("R", CultureInfo.InvariantCulture);

		private static string Names(List<GameObject> prefabs)
		{
			if (prefabs == null || prefabs.Count == 0)
			{
				return string.Empty;
			}
			var names = new List<string>(prefabs.Count);
			foreach (GameObject prefab in prefabs)
			{
				names.Add(prefab != null ? prefab.name : "-");
			}
			return string.Join(",", names);
		}
	}

	/// <summary>
	/// The linear-feature rules that do not depend on a template asset (pure): each form's kind and its own height, spacing and gate,
	/// which places a kind may be laid at, a style's identity, and the deterministic draws every placement uses.
	/// </summary>
	public static class BiomeLinearFeatures
	{
		/// <summary>An empty list: no linear feature anywhere (every biome's default). A fresh list each call.</summary>
		public static List<BiomeLinearStyle> None() => new List<BiomeLinearStyle>();

		/// <summary>The kind a form belongs to: its tens.</summary>
		public static BiomeLinearKind KindOf(BiomeLinearForm form) => (BiomeLinearKind)((int)form / 10);

		/// <summary>Every form of a kind, in order.</summary>
		public static List<BiomeLinearForm> FormsOf(BiomeLinearKind kind)
		{
			var forms = new List<BiomeLinearForm>();
			foreach (BiomeLinearForm form in (BiomeLinearForm[])Enum.GetValues(typeof(BiomeLinearForm)))
			{
				if (KindOf(form) == kind)
				{
					forms.Add(form);
				}
			}
			return forms;
		}

		/// <summary>
		/// Whether a kind may be laid at a place: every kind round fields and along lanes; a channel nowhere else (it needs water and
		/// fields to carry it from and to); a row of trees never as a fold.
		/// </summary>
		public static bool Allows(BiomeLinearKind kind, BiomeLinearPlace where)
		{
			switch (where)
			{
				case BiomeLinearPlace.FieldEdges:
				case BiomeLinearPlace.Lanes:
					return true;
				case BiomeLinearPlace.Folds:
					return kind != BiomeLinearKind.Channel && kind != BiomeLinearKind.TreeRow;
				default:
					return kind != BiomeLinearKind.Channel;
			}
		}

		/// <summary>True when a style can be laid: its form is its kind's, its kind allowed at its place, and it is not drawn away (density over 0).</summary>
		public static bool IsLaid(in BiomeLinearStyle style) => KindOf(style.Form) == style.Kind && Allows(style.Kind, style.Where) && style.Density > 0f;

		/// <summary>A style's identity in its list (one mark per entry): its kind, form and place.</summary>
		public static string Id(in BiomeLinearStyle style) => style.Kind + "/" + style.Form + "/" + style.Where;

		/// <summary>True when a list lays anything (an entry <see cref="IsLaid"/>).</summary>
		public static bool Any(IReadOnlyList<BiomeLinearStyle> styles)
		{
			if (styles != null)
			{
				foreach (BiomeLinearStyle style in styles)
				{
					if (IsLaid(style))
					{
						return true;
					}
				}
			}
			return false;
		}

		/// <summary>True when a list lays anything at a place.</summary>
		public static bool Any(IReadOnlyList<BiomeLinearStyle> styles, BiomeLinearPlace where)
		{
			if (styles != null)
			{
				foreach (BiomeLinearStyle style in styles)
				{
					if (style.Where == where && IsLaid(style))
					{
						return true;
					}
				}
			}
			return false;
		}

		/// <summary>
		/// A form's own height, metres: a laid hedge 1.5, a clipped one 1.8, a wild one 3; a drystone wall 1.3, a mortared one 2.2, an
		/// earth bank 1.2, upright slabs 1.0; fences about a metre and a quarter (a palisade three); a row's trees their own (1: the
		/// prefab as grown); a channel's depth below its banks 0.45; a clearance row 0.8.
		/// </summary>
		public static float DefaultHeight(BiomeLinearForm form)
		{
			switch (form)
			{
				case BiomeLinearForm.Laid: return 1.5f;
				case BiomeLinearForm.Clipped: return 1.8f;
				case BiomeLinearForm.Wild: return 3f;
				case BiomeLinearForm.Drystone: return 1.3f;
				case BiomeLinearForm.Mortared: return 2.2f;
				case BiomeLinearForm.EarthBank: return 1.2f;
				case BiomeLinearForm.UprightSlabs: return 1f;
				case BiomeLinearForm.Rail: return 1.25f;
				case BiomeLinearForm.LeaningPole: return 1.3f;
				case BiomeLinearForm.Wattle: return 1.2f;
				case BiomeLinearForm.Picket: return 1.2f;
				case BiomeLinearForm.Slab: return 1.4f;
				case BiomeLinearForm.Palisade: return 3f;
				case BiomeLinearForm.EarthDitch:
				case BiomeLinearForm.LinedChannel: return 0.45f;
				case BiomeLinearForm.Heaped:
				case BiomeLinearForm.Turfed: return 0.8f;
				default: return 1f;
			}
		}

		/// <summary>
		/// A form's own spacing, metres: a hedge's bushes (laid 1.1, clipped 0.9, wild 1.6), a row's trees (filare 6, pollards 5,
		/// piantata 7); 0 for what is laid in modules.
		/// </summary>
		public static float DefaultSpacing(BiomeLinearForm form)
		{
			switch (form)
			{
				case BiomeLinearForm.Laid: return 1.1f;
				case BiomeLinearForm.Clipped: return 0.9f;
				case BiomeLinearForm.Wild: return 1.6f;
				case BiomeLinearForm.Filare: return 6f;
				case BiomeLinearForm.Pollard: return 5f;
				case BiomeLinearForm.Piantata: return 7f;
				default: return 0f;
			}
		}

		/// <summary>A form's own gate, metres: a field gate's 3 m in a hedge, wall, fence or clearance row; a row of trees and a channel have none.</summary>
		public static float DefaultGate(BiomeLinearForm form)
		{
			switch (KindOf(form))
			{
				case BiomeLinearKind.TreeRow:
				case BiomeLinearKind.Channel:
					return 0f;
				default:
					return 3f;
			}
		}

		/// <summary>How far past a lane's trodden edge a kind is laid, metres: inside the way's tree clearance for a hedge, a wall or a fence; a row's trees and a ditch a little farther out.</summary>
		public static float LaneMargin(BiomeLinearKind kind)
		{
			switch (kind)
			{
				case BiomeLinearKind.Hedge: return 1.1f;
				case BiomeLinearKind.Wall: return 0.8f;
				case BiomeLinearKind.Fence: return 0.6f;
				case BiomeLinearKind.TreeRow: return 2.5f;
				case BiomeLinearKind.Channel: return 1.4f;
				case BiomeLinearKind.ClearanceRow: return 1f;
				default: return 1f;
			}
		}

		/// <summary>A style's height: its own, else its form's.</summary>
		public static float HeightOf(in BiomeLinearStyle style) => style.Height > 0f ? style.Height : DefaultHeight(style.Form);

		/// <summary>A style's spacing: its own, else its form's.</summary>
		public static float SpacingOf(in BiomeLinearStyle style) => style.Spacing > 0f ? style.Spacing : DefaultSpacing(style.Form);

		/// <summary>A style's gate width: its own, else its form's.</summary>
		public static float GateOf(in BiomeLinearStyle style) => style.GateWidth > 0f ? style.GateWidth : DefaultGate(style.Form);

		/// <summary>Whether a lane style follows a way of this class (pure): its Ways flags, none set every routed way; streets and yards never.</summary>
		public static bool Follows(BiomeLinearWays ways, ScenePathClass way)
		{
			if (way == ScenePathClass.Street || way == ScenePathClass.Yard)
			{
				return false;
			}
			if (ways == BiomeLinearWays.All)
			{
				return true;
			}
			switch (way)
			{
				case ScenePathClass.CartTrack: return (ways & BiomeLinearWays.Tracks) != 0;
				case ScenePathClass.Road:
				case ScenePathClass.Highway:
				case ScenePathClass.AncientRoad: return (ways & BiomeLinearWays.Roads) != 0;
				default: return (ways & BiomeLinearWays.Paths) != 0;
			}
		}

		// ── Draws ─────────────────────────────────────────────────────

		/// <summary>A well-mixed 32-bit hash of four values (pure): every linear feature's own draws, never another placement's sequence.</summary>
		public static uint Hash(int seed, int a, int b, int c)
		{
			unchecked
			{
				uint h = (uint)seed * 0x9E3779B1u;
				h = Mix(h ^ (uint)a * 0x85EBCA77u);
				h = Mix(h ^ (uint)b * 0xC2B2AE3Du);
				h = Mix(h ^ (uint)c * 0x27D4EB2Fu);
				return h;
			}
		}

		/// <summary>The murmur finaliser.</summary>
		public static uint Mix(uint h)
		{
			unchecked
			{
				h ^= h >> 16;
				h *= 0x85EBCA6Bu;
				h ^= h >> 13;
				h *= 0xC2B2AE35u;
				h ^= h >> 16;
				return h;
			}
		}

		/// <summary>A hash as a number in [0, 1).</summary>
		public static float Unit(uint h) => (h >> 8) * (1f / 16777216f);

		/// <summary>Whether a place drawn by <paramref name="h"/> is given a style of <paramref name="density"/> (pure): density 1 always, 0 never.</summary>
		public static bool Admits(float density, uint h) => density >= 1f || density > 0f && Unit(h) < density;
	}
}
