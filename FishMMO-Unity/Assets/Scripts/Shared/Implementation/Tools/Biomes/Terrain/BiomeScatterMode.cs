using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;

namespace FishMMO.Shared.Biomes
{
	/// <summary>
	/// Which way a spawn rule's instances are turned (2026-10-26, phase SCATTER-MODES, decision 34): the prefab's +Z is laid along the
	/// heading. APPEND-ONLY (the value is serialised).
	/// </summary>
	public enum BiomeScatterFacing : byte
	{
		/// <summary>Any heading, by each instance's own draw (as before the modes existed).</summary>
		Random = 0,
		/// <summary>Down the fall line (sastrugi, decision 64; wall plants facing out of their wall).</summary>
		Downslope = 1,
		/// <summary>Along the nearest stored landform line of the rule's biome (decision 90: Tidal Fracture's ridges, a canyon's floor).</summary>
		AlongLine = 2,
		/// <summary>Along the axis of the rule's biome's landform under it (decision 85: a tessera weave's strike).</summary>
		Strike = 3,
		/// <summary>Downwind, with the scene's prevailing wind (drift tails, decisions 81/82).</summary>
		Wind = 4,
		/// <summary>With the giant planet's corotating plasma (decision 71): prograde, the moon's local east.</summary>
		Plasma = 5,
		/// <summary>Toward the equator, where the noon sun stands (decision 90's meridian: penitentes).</summary>
		Sunward = 6,
	}

	/// <summary>Which way a spawn rule's instances are tilted off the vertical (SCATTER-MODES). APPEND-ONLY.</summary>
	public enum BiomeScatterLean : byte
	{
		None = 0,
		/// <summary>Toward the equator (decision 77: barrel cacti lean south; decision 90: penitentes to the meridian).</summary>
		Sunward = 1,
		/// <summary>Down the fall line.</summary>
		Downslope = 2,
		/// <summary>Downwind.</summary>
		Wind = 3,
		/// <summary>Conformed to the ground's normal, whatever its slope (decision 72: details laid flat; wall-mounted plants).</summary>
		Ground = 4,
	}

	/// <summary>What a spawn rule's instances gather by (SCATTER-MODES). APPEND-ONLY.</summary>
	public enum BiomeScatterAnchor : byte
	{
		None = 0,
		/// <summary>Downwind of an obstacle: trees, boulders, the scene's structures and ways (decisions 81/82: drift tails, tumbleweeds).</summary>
		Lee = 1,
		/// <summary>Under a named tree's crown (decision 77: young saguaros under their nurse trees).</summary>
		Nurse = 2,
		/// <summary>Within reach of a named shrub (decision 77: grass sheltered under the bushes).</summary>
		Shelter = 3,
		/// <summary>At the foot of a cliff: gentle ground with a steep face just uphill (decision 61: a notch at plain level).</summary>
		CliffFoot = 4,
		/// <summary>On the steep face itself (decision 61: wall-mounted flowers and ferns).</summary>
		Wall = 5,
		/// <summary>On a cliff's seep lines, the strata where water weeps out (decision 50: the hanging garden).</summary>
		SeepLine = 6,
		/// <summary>Near the scene's points of interest of named kinds (round barns by farms).</summary>
		NearSite = 7,
	}

	/// <summary>How a spawn rule's instances are laid out across the ground (SCATTER-MODES). APPEND-ONLY.</summary>
	public enum BiomeScatterPattern : byte
	{
		None = 0,
		/// <summary>Evenly, one to a jittered lattice cell (decision 77: creosote's root-war spacing).</summary>
		Even = 1,
		/// <summary>In broad patches of the rule's own (chimney fields, dogtown colonies).</summary>
		Fields = 2,
		/// <summary>In the rain cells every rain-cell rule of the scene shares (ephemeral flowers after a storm).</summary>
		RainCells = 3,
		/// <summary>On rings and arcs (fairy rings: waxcaps).</summary>
		Rings = 4,
	}

	/// <summary>
	/// A spawn rule's SCATTER MODES (2026-10-26, phase SCATTER-MODES, decision 34; the queued modes of decisions 50, 61, 64, 71, 72, 77, 81,
	/// 82, 85, 90): how its instances are turned, tilted, sized, anchored and laid out. Every field at its default is OFF and the rule scatters
	/// exactly as before, drawing nothing more (<see cref="IsOff"/>; <see cref="Signature"/> is empty so no mark or fingerprint changes).
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Deterministic.</b> Every mode is a pure function of the instance's own hash, its place, the scene's stored data (landform lines and
	/// sites, points of interest, the prevailing wind) and the instances the rule's own pass could see: a re-cut places the same.
	/// </para>
	/// <para>
	/// <b>Where each one acts.</b> Placement (anchors, patterns) weights a rule's chance in the cut's scatter (both the instance and the carpet
	/// path; a carpet ignores <see cref="BiomeScatterPattern.Even"/>), so the GPU detail scatter, which draws the detail maps, follows it.
	/// Facing and power-law sizes are written into a tree instance; for a detail they ride the terrain's mode record to the GPU detail scatter
	/// (<see cref="BiomeScatterModeWord"/>). A lean is the client's (a tree instance has no tilt): the tree renderer and the GPU detail
	/// scatter read it from the same record. Colliders stand upright (a lean is visual only).
	/// </para>
	/// </remarks>
	[Serializable]
	public struct BiomeScatterMode
	{
		[Tooltip("Which way instances are turned (their +Z laid along the heading). Random = any heading, as before.")]
		public BiomeScatterFacing Facing;
		[Tooltip("Degrees added to the facing's heading (decision 64: sastrugi 30° off the fall line).")]
		public float FacingOffset;
		[Tooltip("Half-width, degrees, each instance strays round the heading (from its own turn draw).")]
		public float FacingSpread;
		[Tooltip("Which way instances tilt off the vertical (visual only: colliders stand upright). None = upright, as before.")]
		public BiomeScatterLean Lean;
		[Tooltip("How far they tilt, degrees (each between 0.6× and 1×). Ground conforms to the ground's slope whatever this says.")]
		public float LeanDegrees;
		[Tooltip("What instances gather by. None = no anchor, as before.")]
		public BiomeScatterAnchor Anchor;
		[Tooltip("Lee: tail length past the obstacle, m. Nurse/Shelter: reach past the crown, m. CliffFoot: how far uphill the face may stand, m. Wall: the least slope, degrees. SeepLine: the strata's spacing, m. NearSite: reach past the site's footprint, m. 0 = the anchor's default.")]
		public float AnchorMetres;
		[Tooltip("Share of the chance kept away from the anchor (0 = only by it; 1 = the anchor changes nothing).")]
		[Range(0f, 1f)] public float AnchorFloor;
		[Tooltip("Nurse/Shelter: words a nurse's or shrub's prefab name must contain (any of them, comma separated; empty = every tree). NearSite: point-of-interest kinds (any of them).")]
		public string AnchorNames;
		[Tooltip("How instances are laid out. None = as before.")]
		public BiomeScatterPattern Pattern;
		[Tooltip("Even: lattice pitch, m (0 = from the density). Fields/RainCells: patch radius, m. Rings: ring radius, m. 0 = the pattern's default.")]
		public float PatternMetres;
		[Tooltip("Even: each point's jitter as a share of the pitch (≤ 0.45). Fields/RainCells/Rings: share of lattice places holding a patch or ring (0 = the default).")]
		[Range(0f, 1f)] public float PatternShare;
		[Tooltip("Tree channel and GPU-scattered details: a power-law size draw over the scale range, N(> s) ∝ s^−α (decision 72: craterlets). 0 = off, as before.")]
		[Range(0f, 3.9f)] public float SizePower;

		/// <summary>True when every mode is off: the rule scatters exactly as before.</summary>
		public bool IsOff => Facing == BiomeScatterFacing.Random && Lean == BiomeScatterLean.None && Anchor == BiomeScatterAnchor.None
			&& Pattern == BiomeScatterPattern.None && !(SizePower > 0f);

		/// <summary>True when the rule must run after the rules it stands by (a lee, a nurse, a shelter).</summary>
		public bool NeedsObstacles => Anchor == BiomeScatterAnchor.Lee || Anchor == BiomeScatterAnchor.Nurse || Anchor == BiomeScatterAnchor.Shelter;

		/// <summary>True when an anchor or a weighting pattern multiplies the rule's chance.</summary>
		public bool Weighs => Anchor != BiomeScatterAnchor.None || Pattern == BiomeScatterPattern.Fields || Pattern == BiomeScatterPattern.RainCells
			|| Pattern == BiomeScatterPattern.Rings;

		/// <summary>The modes as one mark ("" when off, so every mark written before the modes existed still matches).</summary>
		public string Signature()
		{
			if (IsOff)
			{
				return string.Empty;
			}
			var s = new StringBuilder();
			if (Facing != BiomeScatterFacing.Random)
			{
				s.Append("face ").Append(Facing).Append(Signed(FacingOffset)).Append('±').Append(Q(FacingSpread));
			}
			if (Lean != BiomeScatterLean.None)
			{
				Sep(s).Append("lean ").Append(Lean).Append(' ').Append(Q(LeanDegrees));
			}
			if (Anchor != BiomeScatterAnchor.None)
			{
				Sep(s).Append("by ").Append(Anchor).Append(' ').Append(Q(AnchorMetres)).Append(" f").Append(Q(AnchorFloor));
				if (!string.IsNullOrEmpty(AnchorNames))
				{
					s.Append(" [").Append(AnchorNames).Append(']');
				}
			}
			if (Pattern != BiomeScatterPattern.None)
			{
				Sep(s).Append("in ").Append(Pattern).Append(' ').Append(Q(PatternMetres)).Append(" s").Append(Q(PatternShare));
			}
			if (SizePower > 0f)
			{
				Sep(s).Append("pow ").Append(Q(SizePower));
			}
			return s.ToString();
		}

		public override string ToString() => IsOff ? "off" : Signature();

		private static StringBuilder Sep(StringBuilder s) => s.Length > 0 ? s.Append('|') : s;
		private static string Q(float v) => v.ToString("0.###", CultureInfo.InvariantCulture);
		private static string Signed(float v) => (v >= 0f ? "+" : string.Empty) + Q(v);

		// ── Building one (the spec's hooks use these) ─────────────────

		public BiomeScatterMode Faced(BiomeScatterFacing facing, float offset = 0f, float spread = 0f)
		{
			BiomeScatterMode m = this;
			m.Facing = facing;
			m.FacingOffset = offset;
			m.FacingSpread = Mathf.Max(0f, spread);
			return m;
		}

		public BiomeScatterMode Leaning(BiomeScatterLean lean, float degrees)
		{
			BiomeScatterMode m = this;
			m.Lean = lean;
			m.LeanDegrees = Mathf.Clamp(degrees, 0f, 90f);
			return m;
		}

		public BiomeScatterMode Anchored(BiomeScatterAnchor anchor, float metres = 0f, float floor = 0f, string names = null)
		{
			BiomeScatterMode m = this;
			m.Anchor = anchor;
			m.AnchorMetres = Mathf.Max(0f, metres);
			m.AnchorFloor = Mathf.Clamp01(floor);
			m.AnchorNames = names;
			return m;
		}

		public BiomeScatterMode Patterned(BiomeScatterPattern pattern, float metres = 0f, float share = 0f)
		{
			BiomeScatterMode m = this;
			m.Pattern = pattern;
			m.PatternMetres = Mathf.Max(0f, metres);
			m.PatternShare = Mathf.Clamp01(share);
			return m;
		}

		public BiomeScatterMode PowerSized(float alpha)
		{
			BiomeScatterMode m = this;
			m.SizePower = Mathf.Clamp(alpha, 0f, 3.9f);
			return m;
		}

		// ── The drafts' modes by name (infra map M3, Z3–Z21) ─────────

		/// <summary>Z3 (decisions 81/82): tails and tumbleweeds downwind of trees, boulders, walls and ruts, laid along the wind.</summary>
		public static BiomeScatterMode LeeDrift(float tailMetres = 8f, float floor = 0f) => default(BiomeScatterMode).Faced(BiomeScatterFacing.Wind, 0f, 8f)
			.Anchored(BiomeScatterAnchor.Lee, tailMetres, floor);
		/// <summary>Z4 (decision 64): along the fall line, offset (sastrugi 30°).</summary>
		public static BiomeScatterMode DownslopeAligned(float offset = 30f, float spread = 6f) => default(BiomeScatterMode).Faced(BiomeScatterFacing.Downslope, offset, spread);
		/// <summary>Z5 (decision 90): along the nearest stored landform line.</summary>
		public static BiomeScatterMode AlongLandformLine(float spread = 6f) => default(BiomeScatterMode).Faced(BiomeScatterFacing.AlongLine, 0f, spread);
		/// <summary>Z6 (decision 85): along the strike of the landform under it (TesseraWeave).</summary>
		public static BiomeScatterMode AlongStrike(float spread = 5f) => default(BiomeScatterMode).Faced(BiomeScatterFacing.Strike, 0f, spread);
		/// <summary>Z7 (decision 90): penitentes leaning to the meridian, their blades across it.</summary>
		public static BiomeScatterMode MeridianLean(float degrees = 18f) => default(BiomeScatterMode).Faced(BiomeScatterFacing.Sunward, 90f, 6f)
			.Leaning(BiomeScatterLean.Sunward, degrees);
		/// <summary>Z8 (decision 71): turned to the plasma wind.</summary>
		public static BiomeScatterMode PlasmaFacing(float spread = 10f) => default(BiomeScatterMode).Faced(BiomeScatterFacing.Plasma, 180f, spread);
		/// <summary>Z9 (decision 77): young plants under named nurse trees' crowns.</summary>
		public static BiomeScatterMode Nursed(string nurses, float reachMetres = 1.5f) => default(BiomeScatterMode).Anchored(BiomeScatterAnchor.Nurse, reachMetres, 0f, nurses);
		/// <summary>Z10 (decision 77): grass within a metre of named bushes.</summary>
		public static BiomeScatterMode UnderShrub(string shrubs, float reachMetres = 1f) => default(BiomeScatterMode).Anchored(BiomeScatterAnchor.Shelter, reachMetres, 0.05f, shrubs);
		/// <summary>Z11 (decision 77): even spacing (pitch from the density when 0).</summary>
		public static BiomeScatterMode EvenSpacing(float pitchMetres = 0f, float jitter = 0.2f) => default(BiomeScatterMode).Patterned(BiomeScatterPattern.Even, pitchMetres, jitter);
		/// <summary>Z12 (decision 77): leaning toward the sun (barrels lean south in the north).</summary>
		public static BiomeScatterMode LeanSunward(float degrees = 14f) => default(BiomeScatterMode).Leaning(BiomeScatterLean.Sunward, degrees);
		/// <summary>Z13 (decision 72): power-law sizes.</summary>
		public static BiomeScatterMode PowerLaw(float alpha = 1.6f) => default(BiomeScatterMode).PowerSized(alpha);
		/// <summary>Z14 (decision 72): conformed flat on the ground (a detail rule also keeps alignToTerrainNormal on).</summary>
		public static BiomeScatterMode LaidFlat() => default(BiomeScatterMode).Leaning(BiomeScatterLean.Ground, 0f);
		/// <summary>Z16 (decision 61): at the foot of a cliff.</summary>
		public static BiomeScatterMode CliffFoot(float reachMetres = 12f, float floor = 0f) => default(BiomeScatterMode).Anchored(BiomeScatterAnchor.CliffFoot, reachMetres, floor);
		/// <summary>Z17 (decision 61): on the steep face, facing out of it and conformed to it.</summary>
		public static BiomeScatterMode WallMounted(float minSlope = 58f) => default(BiomeScatterMode).Faced(BiomeScatterFacing.Downslope, 0f, 12f)
			.Leaning(BiomeScatterLean.Ground, 0f).Anchored(BiomeScatterAnchor.Wall, minSlope);
		/// <summary>Z17 (decision 50): on a cliff's seep lines (the hanging garden), facing out of the face.</summary>
		public static BiomeScatterMode SeepLine(float spacingMetres = 14f) => default(BiomeScatterMode).Faced(BiomeScatterFacing.Downslope, 0f, 15f)
			.Leaning(BiomeScatterLean.Ground, 0f).Anchored(BiomeScatterAnchor.SeepLine, spacingMetres);
		/// <summary>Z18 (Desert): in the scene's shared rain cells.</summary>
		public static BiomeScatterMode RainCellClusters(float radiusMetres = 900f, float share = 0.4f) => default(BiomeScatterMode).Patterned(BiomeScatterPattern.RainCells, radiusMetres, share);
		/// <summary>Z19 (Castle): on fairy rings and arcs.</summary>
		public static BiomeScatterMode FairyRings(float radiusMetres = 6f, float share = 0.35f) => default(BiomeScatterMode).Patterned(BiomeScatterPattern.Rings, radiusMetres, share);
		/// <summary>Z20 (Badlands, Plains): in clustered fields (chimney fields, dogtowns), evenly spaced inside when paired with nothing else.</summary>
		public static BiomeScatterMode ClusteredFields(float radiusMetres = 220f, float share = 0.45f) => default(BiomeScatterMode).Patterned(BiomeScatterPattern.Fields, radiusMetres, share);
		/// <summary>Z21 (Valley): near points of interest of the named kinds.</summary>
		public static BiomeScatterMode NearSites(string kinds, float reachMetres = 60f, float floor = 0f) => default(BiomeScatterMode).Anchored(BiomeScatterAnchor.NearSite, reachMetres, floor, kinds);
	}

	/// <summary>
	/// The scene's directions the modes read, in scene axes (x east-ish, z north-ish as the footprint lays them; unit vectors, zero for none):
	/// where the prevailing wind blows TO (zero on an airless world), the moon's local east (the corotating plasma's way), toward the equator.
	/// </summary>
	public struct BiomeScatterCompass
	{
		public Vector2 WindTo;
		public Vector2 East;
		public Vector2 Equator;

		/// <summary>A compass of plain scene axes: east +x, the equator −z (a northern scene), with the wind given.</summary>
		public static BiomeScatterCompass Plain(Vector2 windTo) => new BiomeScatterCompass { WindTo = windTo, East = Vector2.right, Equator = Vector2.down };
	}

	/// <summary>A point of interest as the NearSite anchor reads it: its centre (world xz), footprint radius and kind's name.</summary>
	public struct BiomeScatterSite
	{
		public Vector2 Centre;
		public float Radius;
		public string Kind;
	}

	/// <summary>
	/// The modes' arithmetic (SCATTER-MODES), pure and thread-safe: what the cut's scatter, the tests and (by mirror) the GPU detail scatter
	/// compute. With a mode off each function is the identity (a factor of 1, the heading or size it was given).
	/// </summary>
	public static class BiomeScatterModes
	{
		/// <summary>Defaults when a field is 0.</summary>
		public const float DefaultLeeTail = 8f, DefaultNurseReach = 1.5f, DefaultShelterReach = 1f, DefaultCliffReach = 12f, DefaultWallSlope = 58f,
			DefaultSeepSpacing = 14f, DefaultSiteReach = 60f, DefaultFieldRadius = 220f, DefaultRainCellRadius = 900f, DefaultRingRadius = 6f,
			DefaultPatternShare = 0.4f, DefaultRingShare = 0.35f;

		/// <summary>How far an AlongLine instance looks for its biome's line, metres; farther it turns at random.</summary>
		public const float LineReach = 250f;

		/// <summary>Unity's turn (radians about +Y) that lays +Z along a direction (x, z).</summary>
		public static float YawOf(Vector2 direction) => Mathf.Atan2(direction.x, direction.y);

		/// <summary>A landform heading (degrees from +x toward +z) as Unity's turn, radians.</summary>
		public static float YawOfLandform(float degrees) => YawOf(new Vector2(Mathf.Cos(degrees * Mathf.Deg2Rad), Mathf.Sin(degrees * Mathf.Deg2Rad)));

		/// <summary>
		/// The turn of an instance whose facing found <paramref name="heading"/> (radians): plus the mode's offset, strayed by the spread from
		/// <paramref name="turnUnit"/> (the instance's own turn draw, 0 … 1).
		/// </summary>
		public static float Facing(in BiomeScatterMode mode, float heading, float turnUnit)
			=> heading + mode.FacingOffset * Mathf.Deg2Rad + mode.FacingSpread * Mathf.Deg2Rad * (2f * turnUnit - 1f);

		/// <summary>The fixed direction a facing or lean takes from the compass; false for one that varies by place (or a zero one).</summary>
		public static bool CompassDirection(BiomeScatterFacing facing, in BiomeScatterCompass compass, out Vector2 direction)
		{
			direction = facing == BiomeScatterFacing.Wind ? compass.WindTo : facing == BiomeScatterFacing.Plasma ? compass.East
				: facing == BiomeScatterFacing.Sunward ? compass.Equator : Vector2.zero;
			return direction.sqrMagnitude > 1e-8f;
		}

		/// <summary>The fixed direction a lean takes from the compass; false for a lean that varies by place or none.</summary>
		public static bool CompassDirection(BiomeScatterLean lean, in BiomeScatterCompass compass, out Vector2 direction)
		{
			direction = lean == BiomeScatterLean.Sunward ? compass.Equator : lean == BiomeScatterLean.Wind ? compass.WindTo : Vector2.zero;
			return direction.sqrMagnitude > 1e-8f;
		}

		/// <summary>
		/// A POWER-LAW size draw (decision 72): the share along a scale range [<paramref name="lo"/>, <paramref name="hi"/>] at which a size
		/// drawn from a truncated Pareto, N(&gt; s) ∝ s^−α, stands for the uniform draw <paramref name="t"/>; t itself when α ≤ 0 or the range is
		/// empty. Monotone in t, 0 at 0, 1 at 1.
		/// </summary>
		public static float PowerLawShare(float t, float alpha, float lo, float hi)
		{
			if (!(alpha > 0f) || !(hi > lo) || !(lo > 0f))
			{
				return t;
			}
			t = Mathf.Clamp01(t);
			double ratio = Math.Pow(lo / (double)hi, alpha);
			double s = lo * Math.Pow(1.0 - t * (1.0 - ratio), -1.0 / alpha);
			return Mathf.Clamp01((float)((s - lo) / (hi - lo)));
		}

		/// <summary>The chance kept at an anchor's weight <paramref name="weight"/> (0 … 1) with <paramref name="floor"/> kept away from it.</summary>
		public static float Anchored(float floor, float weight) => floor + (1f - floor) * Mathf.Clamp01(weight);

		/// <summary>1 within <paramref name="reach"/> of a disc's edge, easing to 0 over a quarter reach past it.</summary>
		public static float Near(float distance, float radius, float reach)
		{
			float edge = radius + reach;
			float fade = Mathf.Max(0.25f, 0.25f * reach);
			return 1f - Smooth((distance - edge) / fade);
		}

		/// <summary>
		/// A LEE tail's weight at <paramref name="rel"/> from an obstacle of radius <paramref name="radius"/> with the wind blowing to
		/// <paramref name="windTo"/> (unit): 1 just behind it, fading downwind to 0 at the tail's end (radius + <paramref name="tail"/>), narrowing
		/// from the obstacle's width to 40 % of it; 0 upwind and beside it.
		/// </summary>
		public static float Lee(Vector2 rel, float radius, Vector2 windTo, float tail)
		{
			float along = rel.x * windTo.x + rel.y * windTo.y;
			float length = radius + tail;
			if (along <= -radius * 0.5f || along >= length)
			{
				return 0f;
			}
			float across = Mathf.Abs(rel.x * windTo.y - rel.y * windTo.x);
			float t = Mathf.Clamp01(along / length);
			float width = Mathf.Max(0.2f, radius * (1f - 0.6f * t));
			float x = across / width;
			return (1f - t) * Mathf.Exp(-x * x);
		}

		/// <summary>The share of an anchor's reach each of <see cref="CliffFoot"/>'s four uphill samples stands at.</summary>
		public static readonly float[] CliffSamples = { 0.25f, 0.5f, 0.75f, 1f };

		/// <summary>
		/// A CLIFF FOOT's weight (decision 61): the steepest face (45° → 60°) among the slopes (degrees) found uphill at
		/// <see cref="CliffSamples"/> of the reach, a nearer one counting more (a far one half), times how gentle the ground itself is
		/// (none past 42°): a notch at plain level, not a ledge on the face.
		/// </summary>
		public static float CliffFoot(float ownSlope, float s1, float s2, float s3, float s4)
		{
			float best = Mathf.Max(Mathf.Max(Face(s1, 0.25f), Face(s2, 0.5f)), Mathf.Max(Face(s3, 0.75f), Face(s4, 1f)));
			return best * (1f - Smooth((ownSlope - 28f) / 14f));
		}

		private static float Face(float slope, float share) => Smooth((slope - 45f) / 15f) * (1f - 0.5f * share);

		/// <summary>A WALL's weight: 0 below 12° under the least slope, full from it.</summary>
		public static float Wall(float slope, float minSlope) => Smooth((slope - (minSlope - 12f)) / 12f);

		/// <summary>The seep lines' phase for a biome, metres (every rule of a biome weeps from the same strata).</summary>
		public static float SeepPhase(int biomeID, float spacing) => Unit(Mix((uint)biomeID ^ 0x5EE91AE5u)) * spacing;

		/// <summary>
		/// A SEEP LINE's weight (decision 50): on steep ground only, peaking on the contours phase + k·spacing where a stratum weeps, across a
		/// band of 8 % of the spacing (at least 0.5 m).
		/// </summary>
		public static float SeepLine(float worldY, float slope, float spacing, float phase)
		{
			spacing = Mathf.Max(2f, spacing);
			float steep = Smooth((slope - 38f) / 16f);
			if (steep <= 0f)
			{
				return 0f;
			}
			float d = worldY - phase;
			d -= Mathf.Floor(d / spacing + 0.5f) * spacing;
			float half = Mathf.Max(0.5f, 0.08f * spacing);
			float x = d / half;
			return steep * Mathf.Exp(-x * x);
		}

		/// <summary>A NEAR SITE weight: the nearest site of a named kind, by <see cref="Near"/>; 0 with none.</summary>
		public static float NearSite(IReadOnlyList<BiomeScatterSite> sites, string kinds, float x, float z, float reach)
		{
			if (sites == null)
			{
				return 0f;
			}
			float best = 0f;
			for (int i = 0; i < sites.Count; i++)
			{
				BiomeScatterSite site = sites[i];
				if (!Names(kinds, site.Kind))
				{
					continue;
				}
				float d = Vector2.Distance(site.Centre, new Vector2(x, z));
				best = Mathf.Max(best, Near(d, site.Radius, reach));
				if (best >= 1f)
				{
					break;
				}
			}
			return best;
		}

		/// <summary>True when <paramref name="candidate"/> contains any of the comma-separated words (case aside); every candidate when there are none.</summary>
		public static bool Names(string words, string candidate)
		{
			if (string.IsNullOrWhiteSpace(words))
			{
				return true;
			}
			if (string.IsNullOrEmpty(candidate))
			{
				return false;
			}
			foreach (string word in words.Split(','))
			{
				string w = word.Trim();
				if (w.Length > 0 && candidate.IndexOf(w, StringComparison.OrdinalIgnoreCase) >= 0)
				{
					return true;
				}
			}
			return false;
		}

		// ── Patterns ───────────────────────────────────────────────────

		/// <summary>The EVEN lattice's pitch: the mode's, else one instance per pitch² at the rule's density (per 100 m²), never under a cell.</summary>
		public static float EvenPitch(in BiomeScatterMode mode, float densityPer100m2, float cellMetres)
		{
			float pitch = mode.PatternMetres > 0f ? mode.PatternMetres : Mathf.Sqrt(100f / Mathf.Max(1e-3f, densityPer100m2));
			return Mathf.Max(cellMetres, pitch);
		}

		/// <summary>
		/// The EVEN lattice's point in the cell [x0, x1) × [z0, z1), if one falls there (decision 77: creosote): a lattice of
		/// <paramref name="pitch"/> turned by the seed, each node jittered by up to <paramref name="jitter"/> of the pitch. False when the
		/// cell holds none (the pitch is never under a cell, so it holds at most one, the first found in node order).
		/// </summary>
		public static bool EvenPoint(uint seed, float pitch, float jitter, float x0, float z0, float x1, float z1, out Vector2 point)
		{
			jitter = Mathf.Clamp(jitter, 0f, 0.45f);
			float angle = Unit(Mix(seed ^ 0xE7E1A77Eu)) * (Mathf.PI * 0.5f);
			float c = Mathf.Cos(angle), s = Mathf.Sin(angle);
			float cx = 0.5f * (x0 + x1), cz = 0.5f * (z0 + z1);
			// Into the lattice's frame.
			float a = (cx * c + cz * s) / pitch, b = (-cx * s + cz * c) / pitch;
			int na = Mathf.FloorToInt(a), nb = Mathf.FloorToInt(b);
			for (int db = -1; db <= 1; db++)
			{
				for (int da = -1; da <= 1; da++)
				{
					int ia = na + da, ib = nb + db;
					uint h = Mix(Mix(seed ^ 0xE7E1B00Bu ^ (uint)ia * 0x9E3779B1u) ^ (uint)ib * 0x85EBCA6Bu);
					float la = (ia + 0.5f + (Unit(Mix(h + 1u)) - 0.5f) * 2f * jitter) * pitch;
					float lb = (ib + 0.5f + (Unit(Mix(h + 2u)) - 0.5f) * 2f * jitter) * pitch;
					float px = la * c - lb * s, pz = la * s + lb * c;
					if (px >= x0 && px < x1 && pz >= z0 && pz < z1)
					{
						point = new Vector2(px, pz);
						return true;
					}
				}
			}
			point = default;
			return false;
		}

		/// <summary>
		/// A weighting pattern's factor at (x, z), its mean over the ground 1 (so the rule's density is still the average, only gathered):
		/// Fields and RainCells discs of radius 0.6–1.4 × the mode's on a lattice of 3 radii, each lattice place holding one at the share;
		/// Rings 0.5–1.5 × the radius, an arc of 35–100 % of the turn, on a lattice of 4 radii. 1 for no pattern (and for Even, which places
		/// rather than weights).
		/// </summary>
		public static float Pattern(in BiomeScatterMode mode, uint seed, float x, float z)
		{
			switch (mode.Pattern)
			{
				case BiomeScatterPattern.Fields:
				case BiomeScatterPattern.RainCells:
				{
					float radius = mode.PatternMetres > 0f ? mode.PatternMetres : mode.Pattern == BiomeScatterPattern.Fields ? DefaultFieldRadius : DefaultRainCellRadius;
					float share = mode.PatternShare > 0f ? mode.PatternShare : DefaultPatternShare;
					return Discs(seed, radius, share, x, z) / (share * DiscCover);
				}
				case BiomeScatterPattern.Rings:
				{
					float radius = mode.PatternMetres > 0f ? mode.PatternMetres : DefaultRingRadius;
					float share = mode.PatternShare > 0f ? mode.PatternShare : DefaultRingShare;
					return Rings(seed, radius, share, x, z) / (share * RingCover(radius));
				}
				default:
					return 1f;
			}
		}

		/// <summary>The share of the ground one disc place covers (its mask's mean over a 3-radius lattice cell, every place holding one).</summary>
		public const float DiscCover = 0.3166f;

		/// <summary>The share of the ground one ring place covers (its mask's mean over a 4-radius lattice cell).</summary>
		public static float RingCover(float radius)
		{
			// E[r] = R, E[arc] = 0.675, a gaussian band of half width w covers w√π across: 2π R · 0.675 · w √π / (4R)².
			float w = Mathf.Max(0.35f, 0.06f * radius);
			return 2f * Mathf.PI * radius * 0.675f * w * 1.7724539f / (16f * radius * radius);
		}

		private static float Discs(uint seed, float radius, float share, float x, float z)
		{
			float pitch = 3f * radius;
			int ia = Mathf.FloorToInt(x / pitch), ib = Mathf.FloorToInt(z / pitch);
			float best = 0f;
			for (int db = -1; db <= 1; db++)
			{
				for (int da = -1; da <= 1; da++)
				{
					uint h = Mix(Mix(seed ^ 0xF1E1D5u ^ (uint)(ia + da) * 0x9E3779B1u) ^ (uint)(ib + db) * 0x85EBCA6Bu);
					if (Unit(h) >= share)
					{
						continue;
					}
					float r = radius * (0.6f + 0.8f * Unit(Mix(h + 3u)));
					float cx = (ia + da + 0.5f + (Unit(Mix(h + 1u)) - 0.5f) * 0.6f) * pitch;
					float cz = (ib + db + 0.5f + (Unit(Mix(h + 2u)) - 0.5f) * 0.6f) * pitch;
					float d = Mathf.Sqrt((x - cx) * (x - cx) + (z - cz) * (z - cz));
					best = Mathf.Max(best, 1f - Smooth((d - 0.85f * r) / (0.15f * r)));
				}
			}
			return best;
		}

		private static float Rings(uint seed, float radius, float share, float x, float z)
		{
			float pitch = 4f * radius;
			int ia = Mathf.FloorToInt(x / pitch), ib = Mathf.FloorToInt(z / pitch);
			float best = 0f;
			for (int db = -1; db <= 1; db++)
			{
				for (int da = -1; da <= 1; da++)
				{
					uint h = Mix(Mix(seed ^ 0xF4127E5u ^ (uint)(ia + da) * 0x9E3779B1u) ^ (uint)(ib + db) * 0x85EBCA6Bu);
					if (Unit(h) >= share)
					{
						continue;
					}
					float r = radius * (0.5f + Unit(Mix(h + 3u)));
					float cx = (ia + da + 0.5f + (Unit(Mix(h + 1u)) - 0.5f) * 0.4f) * pitch;
					float cz = (ib + db + 0.5f + (Unit(Mix(h + 2u)) - 0.5f) * 0.4f) * pitch;
					float dx = x - cx, dz = z - cz;
					float d = Mathf.Sqrt(dx * dx + dz * dz);
					float w = Mathf.Max(0.35f, 0.06f * radius);
					float band = (d - r) / w;
					float ring = Mathf.Exp(-band * band);
					if (ring < 1e-3f)
					{
						continue;
					}
					// The arc it keeps: from a hashed start, 35–100 % of the turn.
					float start = Unit(Mix(h + 4u));
					float length = 0.35f + 0.65f * Unit(Mix(h + 5u));
					float at = Mathf.Atan2(dz, dx) / (2f * Mathf.PI) + 0.5f;
					float into = at - start;
					into -= Mathf.Floor(into);
					if (into > length)
					{
						continue;
					}
					best = Mathf.Max(best, ring);
				}
			}
			return best;
		}

		// ── Hashing (the scatter's lowbias32) ─────────────────────────

		public static uint Mix(uint h)
		{
			unchecked
			{
				h ^= h >> 16;
				h *= 0x7FEB352Du;
				h ^= h >> 15;
				h *= 0x846CA68Bu;
				h ^= h >> 16;
				return h;
			}
		}

		public static float Unit(uint h) => (h >> 8) * (1f / 16777216f);

		/// <summary>A hash of a world position, 0 … 1: an instance's own number where only its place is known (the client's tree lean).</summary>
		public static float PositionUnit(float x, float z)
		{
			unchecked
			{
				uint hx = (uint)BitConverter.SingleToInt32Bits(x);
				uint hz = (uint)BitConverter.SingleToInt32Bits(z);
				return (Mix(hx ^ Mix(hz + 0x632BE5ABu)) & 0xFFFFFFu) / 16777216f;
			}
		}

		private static float Smooth(float t)
		{
			t = Mathf.Clamp01(t);
			return t * t * (3f - 2f * t);
		}
	}

	/// <summary>
	/// What the modes' anchors stand by on one tile (SCATTER-MODES): discs (trees, boulders, shrubs, a shrub's detail cells) with their
	/// prefab's name, bucketed for the lee, nurse and shelter queries. Built once before the anchored rules run; read-only (thread-safe) after.
	/// </summary>
	public sealed class BiomeScatterObstacles
	{
		public const float BucketMetres = 16f;

		private readonly List<Vector3> discs = new List<Vector3>();
		private readonly List<int> nameOf = new List<int>();
		private readonly List<string> names = new List<string>();
		private readonly Dictionary<long, List<int>> buckets = new Dictionary<long, List<int>>();
		private float largest;

		public int Count => discs.Count;

		/// <summary>Adds a disc at (x, z) of radius <paramref name="radius"/> made by a prefab named <paramref name="name"/>.</summary>
		public void Add(float x, float z, float radius, string name)
		{
			int n = names.IndexOf(name);
			if (n < 0)
			{
				n = names.Count;
				names.Add(name);
			}
			int index = discs.Count;
			discs.Add(new Vector3(x, z, Mathf.Max(0.05f, radius)));
			nameOf.Add(n);
			largest = Mathf.Max(largest, radius);
			long key = Key(Mathf.FloorToInt(x / BucketMetres), Mathf.FloorToInt(z / BucketMetres));
			if (!buckets.TryGetValue(key, out List<int> list))
			{
				buckets[key] = list = new List<int>();
			}
			list.Add(index);
		}

		/// <summary>The NURSE/SHELTER weight at (x, z): <see cref="BiomeScatterModes.Near"/> to the nearest disc whose name the words match.</summary>
		public float Near(float x, float z, float reach, string words)
		{
			float best = 0f;
			float look = reach + largest + 0.25f * reach + 1f;
			bool any = string.IsNullOrWhiteSpace(words);
			int x0 = Mathf.FloorToInt((x - look) / BucketMetres), x1 = Mathf.FloorToInt((x + look) / BucketMetres);
			int z0 = Mathf.FloorToInt((z - look) / BucketMetres), z1 = Mathf.FloorToInt((z + look) / BucketMetres);
			for (int bz = z0; bz <= z1; bz++)
			{
				for (int bx = x0; bx <= x1; bx++)
				{
					if (!buckets.TryGetValue(Key(bx, bz), out List<int> list))
					{
						continue;
					}
					foreach (int i in list)
					{
						if (!any && !BiomeScatterModes.Names(words, names[nameOf[i]]))
						{
							continue;
						}
						Vector3 disc = discs[i];
						float dist = Mathf.Sqrt((disc.x - x) * (disc.x - x) + (disc.y - z) * (disc.y - z));
						best = Mathf.Max(best, BiomeScatterModes.Near(dist, disc.z, reach));
						if (best >= 1f)
						{
							return best;
						}
					}
				}
			}
			return best;
		}

		/// <summary>The LEE weight at (x, z): the strongest tail of any disc the words match (every disc with no words).</summary>
		public float Lee(float x, float z, Vector2 windTo, float tail, string words)
		{
			if (windTo.sqrMagnitude < 1e-8f)
			{
				return 0f;
			}
			float best = 0f;
			float look = tail + 2f * largest + 1f;
			bool any = string.IsNullOrWhiteSpace(words);
			int x0 = Mathf.FloorToInt((x - look) / BucketMetres), x1 = Mathf.FloorToInt((x + look) / BucketMetres);
			int z0 = Mathf.FloorToInt((z - look) / BucketMetres), z1 = Mathf.FloorToInt((z + look) / BucketMetres);
			for (int bz = z0; bz <= z1; bz++)
			{
				for (int bx = x0; bx <= x1; bx++)
				{
					if (!buckets.TryGetValue(Key(bx, bz), out List<int> list))
					{
						continue;
					}
					foreach (int i in list)
					{
						if (!any && !BiomeScatterModes.Names(words, names[nameOf[i]]))
						{
							continue;
						}
						Vector3 disc = discs[i];
						best = Mathf.Max(best, BiomeScatterModes.Lee(new Vector2(x - disc.x, z - disc.y), disc.z, windTo, tail));
						if (best >= 1f)
						{
							return best;
						}
					}
				}
			}
			return best;
		}

		private static long Key(int x, int z) => ((long)z << 32) ^ (uint)x;
	}

	/// <summary>
	/// A rule's modes as the GPU detail scatter and the client's tree renderer read them (SCATTER-MODES): two words per prototype in the
	/// terrain's record (TerrainScatterModes), the second two 16-bit headings as shares of a turn. 0 and 0 = no mode: every instance exactly as
	/// before. MIRRORED by FishDetailScatter.compute (ScatterModeYaw, ScatterModeLeanUp, ScatterModeSize): change both together.
	/// </summary>
	/// <remarks>
	/// Word: bits 0–1 facing (0 the instance's own turn, 1 a fixed heading = angles' low half, 2 down the fall line + the low half), 2–9 the
	/// spread (× 180/255 °), 10–12 lean (0 none, 1 toward the angles' high half, 2 down the fall line, 3 conformed to the ground), 13–19 its
	/// degrees (× 90/127), 20–25 the power-law α (× 1/16). AlongLine and Strike vary by place and have no GPU form: a detail rule naming
	/// one turns at random there (the scatter warns).
	/// </remarks>
	public static class BiomeScatterModeWord
	{
		public const int FacingMask = 0x3, SpreadShift = 2, LeanShift = 10, LeanDegreesShift = 13, PowerShift = 20;
		public const uint FacingFixed = 1u, FacingDownslope = 2u;
		public const uint LeanToward = 1u, LeanDownslope = 2u, LeanGround = 3u;
		/// <summary>The GPU's hash streams for a lean's share and a power-law size (FishDetailScatter.compute SCATTER_MODE_LEAN_STREAM, _SIZE_STREAM).</summary>
		public const int LeanStream = 8, SizeStream = 9;

		/// <summary>
		/// The two words of a rule's modes for the GPU and the client (facing only when <paramref name="withFacing"/>: a tree's facing is
		/// written into its instance); false in <paramref name="faced"/> when a facing could not be carried (it varies by place).
		/// </summary>
		public static void Pack(in BiomeScatterMode mode, in BiomeScatterCompass compass, bool withFacing, out uint word, out uint angles, out bool faced)
		{
			word = 0u;
			angles = 0u;
			faced = true;
			if (mode.IsOff)
			{
				return;
			}
			float facingTurn = 0f;
			if (withFacing && mode.Facing != BiomeScatterFacing.Random)
			{
				if (mode.Facing == BiomeScatterFacing.Downslope)
				{
					word |= FacingDownslope;
					facingTurn = mode.FacingOffset / 360f;
				}
				else if (BiomeScatterModes.CompassDirection(mode.Facing, compass, out Vector2 direction))
				{
					word |= FacingFixed;
					facingTurn = (BiomeScatterModes.YawOf(direction) * Mathf.Rad2Deg + mode.FacingOffset) / 360f;
				}
				else
				{
					faced = false;
				}
				if ((word & FacingMask) != 0u)
				{
					word |= (uint)Mathf.Clamp(Mathf.RoundToInt(mode.FacingSpread / 180f * 255f), 0, 255) << SpreadShift;
				}
			}
			float leanTurn = 0f;
			uint lean = 0u;
			switch (mode.Lean)
			{
				case BiomeScatterLean.Ground:
					lean = LeanGround;
					break;
				case BiomeScatterLean.Downslope:
					lean = LeanDownslope;
					break;
				case BiomeScatterLean.Sunward:
				case BiomeScatterLean.Wind:
					if (BiomeScatterModes.CompassDirection(mode.Lean, compass, out Vector2 toward))
					{
						lean = LeanToward;
						leanTurn = BiomeScatterModes.YawOf(toward) * Mathf.Rad2Deg / 360f;
					}
					break;
			}
			if (lean != 0u)
			{
				word |= lean << LeanShift;
				word |= (uint)Mathf.Clamp(Mathf.RoundToInt(mode.LeanDegrees / 90f * 127f), 0, 127) << LeanDegreesShift;
			}
			if (mode.SizePower > 0f)
			{
				word |= (uint)Mathf.Clamp(Mathf.RoundToInt(mode.SizePower * 16f), 1, 63) << PowerShift;
			}
			angles = Turn16(facingTurn) | (Turn16(leanTurn) << 16);
		}

		private static uint Turn16(float turn)
		{
			turn -= Mathf.Floor(turn);
			return (uint)Mathf.Clamp(Mathf.RoundToInt(turn * 65536f), 0, 65536) & 0xFFFFu;
		}

		/// <summary>A 16-bit heading as Unity's turn, radians.</summary>
		public static float Radians(uint half16) => (half16 & 0xFFFFu) / 65536f * 2f * Mathf.PI;

		/// <summary>
		/// An instance's turn, radians (ScatterModeYaw): its own draw (<paramref name="turnUnit"/> × 2π) with no facing; else the fixed heading
		/// or the fall line (from the ground's <paramref name="normal"/>; flat ground keeps the own turn) plus the offset, strayed by the spread.
		/// </summary>
		public static float Yaw(uint word, uint angles, float turnUnit, Vector3 normal)
		{
			uint facing = word & FacingMask;
			float own = turnUnit * 2f * Mathf.PI;
			if (facing == 0u)
			{
				return own;
			}
			float heading = Radians(angles);
			if (facing == FacingDownslope)
			{
				if (normal.x * normal.x + normal.z * normal.z < 1e-8f)
				{
					return own;
				}
				heading += Mathf.Atan2(normal.x, normal.z);
			}
			float spread = ((word >> SpreadShift) & 0xFFu) / 255f * Mathf.PI;
			return heading + spread * (2f * turnUnit - 1f);
		}

		/// <summary>
		/// The up an instance is tilted to (ScatterModeLeanUp): straight up with no lean; the ground's normal for Ground; else tilted by the
		/// degrees × (0.6 + 0.4 × <paramref name="leanUnit"/>) toward the fixed heading or down the fall line (flat ground: upright).
		/// </summary>
		public static Vector3 LeanUp(uint word, uint angles, Vector3 normal, float leanUnit)
		{
			uint lean = (word >> LeanShift) & 0x7u;
			if (lean == 0u)
			{
				return Vector3.up;
			}
			if (lean == LeanGround)
			{
				return normal;
			}
			Vector2 toward;
			if (lean == LeanDownslope)
			{
				toward = new Vector2(normal.x, normal.z);
				float m = toward.magnitude;
				if (m < 1e-4f)
				{
					return Vector3.up;
				}
				toward /= m;
			}
			else
			{
				float a = Radians(angles >> 16);
				toward = new Vector2(Mathf.Sin(a), Mathf.Cos(a));
			}
			float degrees = ((word >> LeanDegreesShift) & 0x7Fu) / 127f * 90f * (0.6f + 0.4f * leanUnit);
			float t = degrees * Mathf.Deg2Rad;
			float s = Mathf.Sin(t);
			return new Vector3(toward.x * s, Mathf.Cos(t), toward.y * s);
		}

		/// <summary>The share along the scale range an instance's size takes (ScatterModeSize): <paramref name="t"/> with no power law.</summary>
		public static float SizeShare(uint word, float t, float lo, float hi)
		{
			uint power = (word >> PowerShift) & 0x3Fu;
			return power == 0u ? t : BiomeScatterModes.PowerLawShare(t, power / 16f, lo, hi);
		}
	}
}
