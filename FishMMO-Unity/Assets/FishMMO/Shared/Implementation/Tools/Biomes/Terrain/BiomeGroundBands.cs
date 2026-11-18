using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace FishMMO.Shared.Biomes
{
	/// <summary>
	/// Which way a ground faces for a <see cref="BiomeFacingBand"/> (2026-10-26, phase GROUND-BANDS, decision 71): the scene's compass
	/// (<see cref="BiomeScatterCompass"/>) gives each direction. Append-only: authored layers store the number.
	/// </summary>
	public enum BiomeFacing
	{
		/// <summary>Away from the equator: the shaded faces of an airless moon or a cold slope (frost lies there).</summary>
		Poleward = 0,
		/// <summary>Toward the equator: the sunny faces.</summary>
		Equatorward = 1,
		/// <summary>Toward the scene's east (a tidally locked moon's plasma wake side).</summary>
		East = 2,
		West = 3,
		/// <summary>The way the prevailing wind blows to: a lee face. With no wind known the band passes.</summary>
		Downwind = 4,
		/// <summary>Into the prevailing wind: a windward face. With no wind known the band passes.</summary>
		Upwind = 5,
	}

	/// <summary>
	/// A FACING band (2026-10-26, phase GROUND-BANDS, decision 71: "a FACING band on ground layers (frost on shaded/poleward faces)"):
	/// a ground layer or spawn rule only on slopes whose fall line points within <see cref="HalfWidth"/> degrees of one compass
	/// direction, softened over <see cref="Falloff"/> degrees past it, and only once the slope passes <see cref="MinSlope"/> (flat ground
	/// faces nothing). All zero (the default) is off: every layer and rule paints and grows as before.
	/// </summary>
	[Serializable]
	public struct BiomeFacingBand
	{
		/// <summary>Defaults of <see cref="Of"/>: ±60°, a 15° soft edge, fully facing from 4° of slope.</summary>
		public const float DefaultHalfWidth = 60f, DefaultFalloff = 15f, DefaultMinSlope = 4f;

		[Tooltip("Paint this ground (or grow this rule) only on slopes facing one way. Off = every way, as before.")]
		public bool On;
		[Tooltip("The way the face looks (its fall line): Poleward (shade), Equatorward (sun), East, West, Downwind (lee), Upwind (windward).")]
		public BiomeFacing Toward;
		[Tooltip("Degrees either side of that way the face counts fully.")]
		[Range(0f, 180f)] public float HalfWidth;
		[Tooltip("Degrees past the half width over which it fades out; 0 = a hard edge.")]
		[Range(0f, 90f)] public float Falloff;
		[Tooltip("Degrees of slope from which a face counts fully (eased in from flat); 0 = any slope that has a fall line.")]
		[Range(0f, 45f)] public float MinSlope;

		/// <summary>A band facing <paramref name="toward"/> within ±<paramref name="halfWidth"/>°.</summary>
		public static BiomeFacingBand Of(BiomeFacing toward, float halfWidth = DefaultHalfWidth, float falloff = DefaultFalloff, float minSlope = DefaultMinSlope)
			=> new BiomeFacingBand { On = true, Toward = toward, HalfWidth = Mathf.Clamp(halfWidth, 0f, 180f), Falloff = Mathf.Clamp(falloff, 0f, 90f), MinSlope = Mathf.Clamp(minSlope, 0f, 45f) };

		/// <summary>
		/// The direction (world xz, unit) a face must look along, from the scene's compass; false with none known (a wind band on a windless
		/// scene: the band then passes). A compass with no equator or east (a hand-made scene) reads the plain axes: east +x, equator −z.
		/// </summary>
		public static bool DirectionOf(BiomeFacing toward, in BiomeScatterCompass compass, out Vector2 direction)
		{
			Vector2 equator = compass.Equator.sqrMagnitude > 1e-8f ? compass.Equator.normalized : Vector2.down;
			Vector2 east = compass.East.sqrMagnitude > 1e-8f ? compass.East.normalized : Vector2.right;
			Vector2 wind = compass.WindTo.sqrMagnitude > 1e-8f ? compass.WindTo.normalized : Vector2.zero;
			switch (toward)
			{
				case BiomeFacing.Poleward: direction = -equator; break;
				case BiomeFacing.Equatorward: direction = equator; break;
				case BiomeFacing.East: direction = east; break;
				case BiomeFacing.West: direction = -east; break;
				case BiomeFacing.Downwind: direction = wind; break;
				case BiomeFacing.Upwind: direction = -wind; break;
				default: direction = Vector2.zero; break;
			}
			return direction.sqrMagnitude > 1e-8f;
		}

		/// <summary>
		/// How fully a face counts, 0 … 1: 1 when off. <paramref name="downhill"/> is the fall line's direction (world xz; zero on flat
		/// ground, which faces nothing), <paramref name="slopeDegrees"/> its steepness.
		/// </summary>
		public float Weight(Vector2 downhill, float slopeDegrees, in BiomeScatterCompass compass)
		{
			if (!On)
			{
				return 1f;
			}
			if (!DirectionOf(Toward, in compass, out Vector2 way))
			{
				return 1f;
			}
			float length = downhill.magnitude;
			if (!(length > 1e-6f) || !(slopeDegrees > 0f))
			{
				return 0f;
			}
			float tilt = MinSlope > 0f ? Smooth(slopeDegrees / MinSlope) : 1f;
			float cos = Mathf.Clamp((downhill.x * way.x + downhill.y * way.y) / (length * way.magnitude), -1f, 1f);
			float angle = Mathf.Acos(cos) * Mathf.Rad2Deg;
			float beyond = angle - HalfWidth;
			float facing = beyond <= 0f ? 1f : Falloff > 0f ? 1f - Smooth(beyond / Falloff) : 0f;
			return tilt * facing;
		}

		/// <summary>The band as the authoring marks it ("" when off, so a mark written before the band existed still matches).</summary>
		public string Signature() => On
			? string.Format(CultureInfo.InvariantCulture, "face {0} {1:0.####},{2:0.####},{3:0.####}", (int)Toward, HalfWidth, Falloff, MinSlope)
			: string.Empty;

		public override string ToString() => On ? Signature() : "off";

		/// <summary>Smoothstep of a clamped 0 … 1.</summary>
		private static float Smooth(float t)
		{
			t = Mathf.Clamp01(t);
			return t * t * (3f - 2f * t);
		}
	}

	/// <summary>
	/// A SITE band (2026-10-26, phase GROUND-BANDS: the drafts' "sites paint their own layer" — Abyssal Plain's blackened whale-fall
	/// halo, Wasteland's old burn at its waste vills): a detail ground painted only on and round the planned points of interest of the
	/// named kinds, fully within their footprints and fading out over <see cref="Metres"/> past them
	/// (<see cref="BiomeScatterModes.NearSite"/>, the NearSite scatter anchor's own reach). Off (the default) paints as before.
	/// </summary>
	[Serializable]
	public struct BiomeSiteBand
	{
		/// <summary>The reach past a site's footprint when none is given, metres.</summary>
		public const float DefaultMetres = 10f;

		[Tooltip("Paint this ground only on and round points of interest of the named kinds. Off = everywhere, as before.")]
		public bool On;
		[Tooltip("The site kinds, comma-separated words matched in the kind's name (SunkenWhaleFall, Battlefield…); empty = every site.")]
		public string Kinds;
		[Tooltip("Metres past a site's footprint over which the ground fades out.")]
		[Range(0f, 200f)] public float Metres;

		/// <summary>A band round the sites whose kind names one of <paramref name="kinds"/>.</summary>
		public static BiomeSiteBand Of(string kinds, float metres = DefaultMetres)
			=> new BiomeSiteBand { On = true, Kinds = string.IsNullOrWhiteSpace(kinds) ? string.Empty : kinds.Trim(), Metres = Mathf.Clamp(metres, 0f, 200f) };

		/// <summary>How fully the ground is painted at (x, z) among <paramref name="sites"/>: 1 when off; 0 on a scene with no site of its kinds.</summary>
		public float Weight(IReadOnlyList<BiomeScatterSite> sites, float x, float z)
		{
			return On ? BiomeScatterModes.NearSite(sites, Kinds, x, z, Metres) : 1f;
		}

		/// <summary>The band as the authoring marks it ("" when off).</summary>
		public string Signature() => On
			? string.Format(CultureInfo.InvariantCulture, "near {0} {1:0.####}", Kinds ?? string.Empty, Metres)
			: string.Empty;

		public override string ToString() => On ? Signature() : "off";
	}

	/// <summary>Which clock a <see cref="BiomeGroundClock"/> follows. Append-only: the layer table stores the number.</summary>
	public enum BiomeClockKind
	{
		None = 0,
		/// <summary>The local year: 0 midwinter, 0.25 spring, 0.5 midsummer, 0.75 autumn (turned half a year in the south).</summary>
		Year = 1,
		/// <summary>The local day: 0 midnight, 0.25 morning, 0.5 noon, 0.75 evening (the sky's local solar time).</summary>
		Day = 2,
	}

	/// <summary>
	/// A ground that comes and goes on the SHARED CLOCK (2026-10-26, phase GROUND-BANDS: AbyssalPlain Q5 "seasonal ground-cover strength
	/// from the shared clock", decision 84 "daily ground band (SO₂ frost at dawn, gone by noon)"). Painted at the cut like any detail; on
	/// the GPU its weight is scaled each frame by a window of the local year or day (<see cref="Factor"/>, mirrored in FishGroundClock.hlsl)
	/// and what it gives up goes to its biome's main ground, so every viewer sees the same ground at the same moment. None (the default)
	/// draws as before.
	/// </summary>
	/// <remarks>
	/// The window runs from <see cref="From"/> to <see cref="To"/> round the clock (a window may wrap past 0: a night frost from 0.8 to
	/// 0.45), easing out over <see cref="Falloff"/> either side; outside it the ground keeps <see cref="Floor"/> of its strength. A From
	/// equal to To is the whole clock. With no clock known (no sky yet) the ground is drawn as painted.
	/// </remarks>
	[Serializable]
	public struct BiomeGroundClock
	{
		/// <summary>The soft edge when none is given, as a share of the clock (0.04 of a day ≈ 1 h; of a year ≈ 2 weeks).</summary>
		public const float DefaultFalloff = 0.04f;

		[Tooltip("Which clock the ground follows: None (always, as before), Year (the local season) or Day (the local hour).")]
		public BiomeClockKind Kind;
		[Tooltip("Where on the clock the ground is full, 0 … 1 (Year: 0 midwinter, 0.25 spring, 0.5 midsummer; Day: 0 midnight, 0.5 noon).")]
		[Range(0f, 1f)] public float From;
		[Tooltip("Where on the clock it stops being full; a window may wrap past 0 (From 0.8, To 0.45: overnight to late morning).")]
		[Range(0f, 1f)] public float To;
		[Tooltip("The soft edge either side of the window, as a share of the clock; 0 = a hard switch.")]
		[Range(0f, 0.5f)] public float Falloff;
		[Tooltip("How much of the ground stays outside the window, 0 … 1 (0: gone; 0.4: a thin cover all year).")]
		[Range(0f, 1f)] public float Floor;

		/// <summary>True when the ground follows no clock.</summary>
		public bool IsNone => Kind == BiomeClockKind.None;

		/// <summary>A ground full through part of the local year.</summary>
		public static BiomeGroundClock Year(float from, float to, float floor = 0f, float falloff = DefaultFalloff) => Of(BiomeClockKind.Year, from, to, floor, falloff);

		/// <summary>A ground full through part of the local day.</summary>
		public static BiomeGroundClock Day(float from, float to, float floor = 0f, float falloff = DefaultFalloff) => Of(BiomeClockKind.Day, from, to, floor, falloff);

		private static BiomeGroundClock Of(BiomeClockKind kind, float from, float to, float floor, float falloff)
			=> new BiomeGroundClock { Kind = kind, From = Wrap(from), To = Wrap(to), Floor = Mathf.Clamp01(floor), Falloff = Mathf.Clamp(falloff, 0f, 0.5f) };

		/// <summary>
		/// The window at <paramref name="phase"/> (0 … 1 round the clock): 1 inside [from, to] (wrapping), easing to 0 over
		/// <paramref name="falloff"/> outside it by the nearer end. Exactly FishGroundClockWindow.
		/// </summary>
		public static float Window(float phase, float from, float to, float falloff)
		{
			phase = Wrap(phase);
			from = Wrap(from);
			to = Wrap(to);
			float span = Wrap(to - from);
			if (span <= 0f)
			{
				return 1f;
			}
			if (Wrap(phase - from) <= span)
			{
				return 1f;
			}
			float d = Mathf.Min(Round(phase - from), Round(phase - to));
			if (falloff <= 0f)
			{
				return 0f;
			}
			float t = Mathf.Clamp01(d / falloff);
			return 1f - t * t * (3f - 2f * t);
		}

		/// <summary>How much of the ground is drawn at <paramref name="phase"/> of its clock: 1 when it follows none.</summary>
		public float Factor(float phase)
		{
			if (IsNone)
			{
				return 1f;
			}
			float w = Window(phase, From, To, Falloff);
			return Floor + (1f - Floor) * w;
		}

		/// <summary>The layer table's clock row (TerrainLayerTable.RowClock): x kind, y from, z to, w falloff; zero for none.</summary>
		public Vector4 Pack() => IsNone ? Vector4.zero : new Vector4((int)Kind, Wrap(From), Wrap(To), Mathf.Clamp(Falloff, 0f, 0.5f));

		/// <summary>The clock as the authoring marks it ("" for none).</summary>
		public string Signature() => IsNone
			? string.Empty
			: string.Format(CultureInfo.InvariantCulture, "clock {0} {1:0.####},{2:0.####},{3:0.####},{4:0.####}", (int)Kind, Wrap(From), Wrap(To), Falloff, Floor);

		public override string ToString() => IsNone ? "none" : Signature();

		/// <summary>The clocks the GPU reads (_FishGroundClock): x the local year phase, y 1 when known, z the local time of day, w 1 when known.</summary>
		public static Vector4 ShaderClock(float yearPhase, bool yearKnown, float localTime, bool dayKnown)
			=> new Vector4(yearKnown ? Wrap(yearPhase) : 0f, yearKnown ? 1f : 0f, dayKnown ? Wrap(localTime) : 0f, dayKnown ? 1f : 0f);

		/// <summary>The local year phase from the astronomical season (CelestialMath.Season01, 0 the northern midwinter): turned half a year in the south.</summary>
		public static float LocalYear(float season01, float latitudeDegrees) => Wrap(season01 + (latitudeDegrees < 0f ? 0.5f : 0f));

		/// <summary>
		/// How much of a packed clock (<see cref="Pack"/>, floor apart) is drawn under the shader's clocks (<see cref="ShaderClock"/>): the
		/// C# twin of FishGroundClockFactor. 1 for none, or for a clock not known.
		/// </summary>
		public static float FactorOf(Vector4 packed, float floor, Vector4 clocks)
		{
			int kind = Mathf.RoundToInt(packed.x);
			float phase, known;
			if (kind == (int)BiomeClockKind.Year)
			{
				phase = clocks.x;
				known = clocks.y;
			}
			else if (kind == (int)BiomeClockKind.Day)
			{
				phase = clocks.z;
				known = clocks.w;
			}
			else
			{
				return 1f;
			}
			if (known < 0.5f)
			{
				return 1f;
			}
			float w = Window(phase, packed.y, packed.z, packed.w);
			return floor + (1f - floor) * w;
		}

		/// <summary>
		/// The C# twin of FishGroundClockShift: a pixel's four strongest layers (<paramref name="weights"/> normalised, their
		/// <paramref name="ids"/>) after each clocked one has given what its clock takes to the layer its share row names (that layer's
		/// slot among the four, else the weakest other slot, which gives way), renormalised. <paramref name="clockRow"/> and
		/// <paramref name="shareRow"/> are the layer table's rows by layer id (TerrainLayerTable.RowClock, RowClockShare).
		/// </summary>
		public static void Shift(float[] weights, int[] ids, Vector4[] clockRow, Vector4[] shareRow, Vector4 clocks)
		{
			Vector4 Row(Vector4[] row, int layer) => row != null && row.Length > 0 ? row[Mathf.Clamp(layer, 0, row.Length - 1)] : Vector4.zero;
			if (Row(shareRow, ids[0]).w < 0.5f)
			{
				return;
			}
			var w = new float[4];
			var id = new int[4];
			for (int i = 0; i < 4; i++)
			{
				w[i] = weights[i];
				id[i] = ids[i];
			}
			for (int k = 0; k < 4; k++)
			{
				if (w[k] <= 0f)
				{
					continue;
				}
				Vector4 packed = Row(clockRow, id[k]);
				if (packed.x < 0.5f)
				{
					continue;
				}
				Vector4 share = Row(shareRow, id[k]);
				float moved = w[k] * (1f - FactorOf(packed, share.x, clocks));
				if (moved <= 0f)
				{
					continue;
				}
				w[k] -= moved;
				int to = Mathf.RoundToInt(share.y);
				if (to < 0)
				{
					continue;
				}
				int at = -1, weak = -1;
				float weakest = 2f;
				for (int j = 0; j < 4; j++)
				{
					if (j == k)
					{
						continue;
					}
					if (at < 0 && id[j] == to)
					{
						at = j;
					}
					if (w[j] < weakest)
					{
						weakest = w[j];
						weak = j;
					}
				}
				if (at < 0)
				{
					at = weak;
				}
				w[at] = (id[at] == to ? w[at] : 0f) + moved;
				id[at] = to;
			}
			float total = w[0] + w[1] + w[2] + w[3];
			if (total > 1e-4f)
			{
				for (int i = 0; i < 4; i++)
				{
					weights[i] = w[i] / total;
					ids[i] = id[i];
				}
			}
		}

		/// <summary>0 … 1 round the clock (x − ⌊x⌋, as HLSL frac).</summary>
		public static float Wrap(float x) => x - Mathf.Floor(x);

		/// <summary>The distance round the clock between two phases, 0 … 0.5.</summary>
		private static float Round(float d)
		{
			d = Wrap(d);
			return Mathf.Min(d, 1f - d);
		}
	}
}
