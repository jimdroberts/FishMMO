using System;
using System.Collections.Generic;
using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// Tunables and pure decision functions for per-observer streaming: how far a character is
	/// visible from (scaled by local density), which observed characters a client receives at
	/// full rate, and what reduced rate the rest get.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Two levers, both server-side.</b> The first is the observer <i>range</i>: FishNet's
	/// <c>DistanceCondition</c> is cloned per object, so its distance can be changed at runtime
	/// per character. In a crowd, every extra metre of range multiplies the number of characters
	/// each client streams, so range is shrunk as local density rises and restored as it falls.
	/// The second is the observer <i>cap</i>: of everything a client can see, only the
	/// <see cref="FullRateObserverCap"/> most relevant characters send every unreliable update;
	/// the rest are sent every Nth, with N chosen by distance. Relevance favours characters in
	/// combat, then party and guild members, then proximity.
	/// </para>
	/// <para>
	/// Everything here is pure and static so it can be unit-tested without a network, and so the
	/// scene server can override the numbers from its configuration at startup
	/// (<see cref="ApplySetting"/>). Reliable sends are never rate limited — see
	/// <c>IObserverSendFilter</c>.
	/// </para>
	/// </remarks>
	public static class ObserverStreamingPolicy
	{
		/// <summary>A distance band and the send interval (in ticks) applied inside it.</summary>
		public readonly struct LodBand
		{
			/// <summary>Band applies to distances up to and including this, in metres.</summary>
			public readonly float MaxDistance;
			/// <summary>Send every Nth unreliable update. 1 is full rate.</summary>
			public readonly byte Interval;

			public LodBand(float maxDistance, byte interval)
			{
				MaxDistance = maxDistance;
				Interval = interval < 1 ? (byte)1 : interval;
			}
		}

		// ── Observer cap ──

		/// <summary>
		/// How many observed characters a single client receives at full rate. Everyone else it
		/// can see is rate limited by <see cref="LodBands"/>.
		/// </summary>
		public static int FullRateObserverCap { get; set; } = 24;

		/// <summary>
		/// Relevance weight for a character currently in combat. Kept above
		/// <see cref="PartyWeight"/> + <see cref="DistanceWeight"/> so a fighter at the edge of
		/// range still outranks an idle party member standing next to the viewer.
		/// </summary>
		public static float CombatWeight { get; set; } = 120f;

		/// <summary>Relevance weight for a character in the viewer's party.</summary>
		public static float PartyWeight { get; set; } = 60f;

		/// <summary>Relevance weight for a character in the viewer's guild.</summary>
		public static float GuildWeight { get; set; } = 30f;

		/// <summary>
		/// Relevance weight for proximity: a character at distance 0 scores this, one at the
		/// viewer's full observer range scores 0.
		/// </summary>
		public static float DistanceWeight { get; set; } = 50f;

		// ── LOD rates ──

		private static readonly List<LodBand> lodBands = new List<LodBand>
		{
			new LodBand(20f, 2),
			new LodBand(45f, 4),
			new LodBand(float.PositiveInfinity, 8),
		};

		/// <summary>
		/// Distance bands applied to characters beyond the cap, ascending by distance. The last
		/// band should have an infinite distance so every character matches one.
		/// </summary>
		public static IReadOnlyList<LodBand> LodBands => lodBands;

		/// <summary>Replaces the LOD bands. Bands are sorted by distance; an empty list means "never limit".</summary>
		public static void SetLodBands(IEnumerable<LodBand> bands)
		{
			lodBands.Clear();
			if (bands != null)
			{
				lodBands.AddRange(bands);
				lodBands.Sort((a, b) => a.MaxDistance.CompareTo(b.MaxDistance));
			}
		}

		// ── Density-scaled range ──

		/// <summary>Radius, in metres, within which other characters count towards local density.</summary>
		public static float DensityRadius { get; set; } = 40f;

		/// <summary>Neighbour count at or below which a character keeps its full configured range.</summary>
		public static int LowDensity { get; set; } = 8;

		/// <summary>Neighbour count at or above which a character's range is fully scaled down.</summary>
		public static int HighDensity { get; set; } = 40;

		/// <summary>Fraction of the configured range applied at <see cref="HighDensity"/>.</summary>
		public static float RangeScaleAtHighDensity { get; set; } = 0.5f;

		/// <summary>Absolute floor on any scaled range, in metres, so combat never happens out of sight.</summary>
		public static float MinimumRange { get; set; } = 25f;

		/// <summary>Range changes smaller than this, in metres, are not applied — avoids churning the observer rebuild.</summary>
		public static float RangeChangeThreshold { get; set; } = 2f;

		// ── Scheduling ──

		/// <summary>Ticks between scheduling passes. 15 is half a second at 30 Hz.</summary>
		public static uint RescheduleIntervalTicks { get; set; } = 15;

		/// <summary>
		/// Relevance of an observed character to a viewer. Higher is more relevant.
		/// </summary>
		/// <param name="inCombat">Observed character is in combat.</param>
		/// <param name="sameParty">Observed character shares the viewer's party.</param>
		/// <param name="sameGuild">Observed character shares the viewer's guild.</param>
		/// <param name="distance">Distance from viewer to observed, in metres.</param>
		/// <param name="maxRange">Distance at which proximity contributes nothing.</param>
		public static float Score(bool inCombat, bool sameParty, bool sameGuild, float distance, float maxRange)
		{
			float score = 0f;
			if (inCombat) score += CombatWeight;
			if (sameParty) score += PartyWeight;
			if (sameGuild) score += GuildWeight;

			float proximity = maxRange > 0f ? 1f - Mathf.Clamp01(distance / maxRange) : 0f;
			score += DistanceWeight * proximity;
			return score;
		}

		/// <summary>
		/// Send interval, in ticks, for a character beyond the cap at the given distance.
		/// Returns 1 (full rate) when no band matches.
		/// </summary>
		public static byte LodInterval(float distance)
		{
			for (int i = 0; i < lodBands.Count; ++i)
			{
				if (distance <= lodBands[i].MaxDistance)
				{
					return lodBands[i].Interval;
				}
			}
			return 1;
		}

		/// <summary>
		/// Observer range for a character with <paramref name="neighbourCount"/> other characters
		/// within <see cref="DensityRadius"/>: the full <paramref name="baseRange"/> at or below
		/// <see cref="LowDensity"/>, scaled linearly to <see cref="RangeScaleAtHighDensity"/> at
		/// <see cref="HighDensity"/>, never below <see cref="MinimumRange"/> (or the base range,
		/// whichever is smaller).
		/// </summary>
		public static float ScaledRange(float baseRange, int neighbourCount)
		{
			if (baseRange <= 0f)
			{
				return baseRange;
			}

			float t;
			if (HighDensity <= LowDensity)
			{
				t = neighbourCount > LowDensity ? 1f : 0f;
			}
			else
			{
				t = Mathf.Clamp01((neighbourCount - LowDensity) / (float)(HighDensity - LowDensity));
			}

			float scale = Mathf.Lerp(1f, Mathf.Clamp01(RangeScaleAtHighDensity), t);
			float scaled = baseRange * scale;
			float floor = Mathf.Min(MinimumRange, baseRange);
			return Mathf.Max(scaled, floor);
		}

		/// <summary>
		/// True when an update should be sent on <paramref name="tick"/> to an observer whose
		/// interval is <paramref name="interval"/>. <paramref name="phase"/> (typically the
		/// connection id) spreads different observers' send ticks so a cap of limited observers
		/// does not all fire on the same tick.
		/// </summary>
		public static bool ShouldSendThisTick(uint tick, byte interval, int phase)
		{
			if (interval <= 1)
			{
				return true;
			}
			return ((tick + (uint)(phase & 0xFFFF)) % interval) == 0u;
		}

		/// <summary>
		/// Applies one <c>key=value</c> server setting. Unknown keys are ignored; malformed
		/// values are rejected. Returns true when a setting was applied.
		/// </summary>
		/// <remarks>
		/// Keys: <c>ObserverFullRateCap</c>, <c>ObserverCombatWeight</c>, <c>ObserverPartyWeight</c>,
		/// <c>ObserverGuildWeight</c>, <c>ObserverDistanceWeight</c>, <c>ObserverDensityRadius</c>,
		/// <c>ObserverLowDensity</c>, <c>ObserverHighDensity</c>, <c>ObserverRangeScaleAtHighDensity</c>,
		/// <c>ObserverMinimumRange</c>, <c>ObserverRescheduleTicks</c>, and
		/// <c>ObserverLodBands</c> as <c>distance:interval,distance:interval,...</c>
		/// (e.g. <c>20:2,45:4,inf:8</c>).
		/// </remarks>
		public static bool ApplySetting(string key, string value)
		{
			if (string.IsNullOrEmpty(key) || value == null)
			{
				return false;
			}

			switch (key)
			{
				case "ObserverFullRateCap": return TryInt(value, v => FullRateObserverCap = Math.Max(0, v));
				case "ObserverCombatWeight": return TryFloat(value, v => CombatWeight = v);
				case "ObserverPartyWeight": return TryFloat(value, v => PartyWeight = v);
				case "ObserverGuildWeight": return TryFloat(value, v => GuildWeight = v);
				case "ObserverDistanceWeight": return TryFloat(value, v => DistanceWeight = v);
				case "ObserverDensityRadius": return TryFloat(value, v => DensityRadius = Mathf.Max(1f, v));
				case "ObserverLowDensity": return TryInt(value, v => LowDensity = Math.Max(0, v));
				case "ObserverHighDensity": return TryInt(value, v => HighDensity = Math.Max(0, v));
				case "ObserverRangeScaleAtHighDensity": return TryFloat(value, v => RangeScaleAtHighDensity = Mathf.Clamp01(v));
				case "ObserverMinimumRange": return TryFloat(value, v => MinimumRange = Mathf.Max(0f, v));
				case "ObserverRescheduleTicks": return TryInt(value, v => RescheduleIntervalTicks = (uint)Math.Max(1, v));
				case "ObserverLodBands": return TryParseLodBands(value);
				default: return false;
			}
		}

		private static bool TryInt(string value, Action<int> apply)
		{
			if (int.TryParse(value, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out int v))
			{
				apply(v);
				return true;
			}
			return false;
		}

		private static bool TryFloat(string value, Action<float> apply)
		{
			if (float.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float v))
			{
				apply(v);
				return true;
			}
			return false;
		}

		private static bool TryParseLodBands(string value)
		{
			List<LodBand> bands = new List<LodBand>();
			foreach (string part in value.Split(','))
			{
				string[] pair = part.Trim().Split(':');
				if (pair.Length != 2)
				{
					return false;
				}
				float distance;
				string d = pair[0].Trim();
				if (string.Equals(d, "inf", StringComparison.OrdinalIgnoreCase))
				{
					distance = float.PositiveInfinity;
				}
				else if (!float.TryParse(d, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out distance))
				{
					return false;
				}
				if (!byte.TryParse(pair[1].Trim(), out byte interval))
				{
					return false;
				}
				bands.Add(new LodBand(distance, interval));
			}
			SetLodBands(bands);
			return true;
		}
	}
}
