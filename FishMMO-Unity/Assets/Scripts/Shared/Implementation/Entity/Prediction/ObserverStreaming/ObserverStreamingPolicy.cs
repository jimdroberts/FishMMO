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
	/// per character. It is left at the authored value by default — a crowd is bounded by
	/// <see cref="VisibilityBudget"/>, which cuts by relevance rather than by distance — but can
	/// still be shrunk with local density; see <see cref="RangeScaleAtHighDensity"/>.
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

		/// <summary>
		/// Largest send interval, in ticks, any observer may be handed — by the cap bands here, by
		/// the engaged overflow, or by <c>NetworkTransformDistanceLod</c>.
		/// </summary>
		/// <remarks>
		/// <para>
		/// Mirrors <c>NetworkTransform._interpolation</c>, which is 2 on every prefab. The client
		/// buffers that many received goals before it starts moving and waits for that many again
		/// whenever the buffer runs dry, so an observer fed every Nth tick stands still for up to
		/// 2N ticks at every restart; and when the buffer overflows the client drops to the newest
		/// goals and snaps, a jump of everything it skipped. Both scale with the interval, which is
		/// how the 4- and 8-tick bands this table used to carry rendered as characters "teleporting"
		/// beyond the near field while the distance LOD's own table was being retuned to no effect.
		/// <c>NetworkTransformLodBufferTests</c> pins this value against the prefabs.
		/// </para>
		/// <para>
		/// Every interval read out of this class is clamped to it. Raise it only together with the
		/// prefab interpolation (config key <c>ObserverMaxSendInterval</c>).
		/// </para>
		/// </remarks>
		public static byte MaxSendInterval { get; set; } = 2;

		private static readonly List<LodBand> lodBands = new List<LodBand>
		{
			new LodBand(float.PositiveInfinity, 2),
		};

		/// <summary>
		/// Distance bands applied to characters beyond the cap, ascending by distance. The last
		/// band should have an infinite distance so every character matches one. Intervals are
		/// clamped to <see cref="MaxSendInterval"/> when read.
		/// </summary>
		/// <remarks>
		/// One band by default: with the ceiling at 2 there is no room for the table to get coarser
		/// with distance, and bandwidth beyond the near field is <see cref="VisibilityBudget"/>'s
		/// job. The banding stays configurable (<c>ObserverLodBands</c>) for a deployment that
		/// raises the interpolation buffer.
		/// </remarks>
		public static IReadOnlyList<LodBand> LodBands => lodBands;

		/// <summary>Clamps a configured interval into [1, <see cref="MaxSendInterval"/>].</summary>
		public static byte ClampInterval(byte interval)
		{
			byte max = MaxSendInterval < 1 ? (byte)1 : MaxSendInterval;
			if (interval < 1)
			{
				return 1;
			}
			return interval > max ? max : interval;
		}

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
		/// <remarks>
		/// The count is taken from a 3×3 block of cells this size — a deliberate superset of the
		/// disc, but roughly 2.9× its area, so <see cref="LowDensity"/> and <see cref="HighDensity"/>
		/// are reached by a crowd about a third as dense as their names suggest. That bias is part of
		/// why the shrink is off by default; anyone re-enabling it should calibrate against the box,
		/// not the radius.
		/// </remarks>
		public static float DensityRadius { get; set; } = 40f;

		/// <summary>Neighbour count at or below which a character keeps its full configured range.</summary>
		public static int LowDensity { get; set; } = 8;

		/// <summary>Neighbour count at or above which a character's range is fully scaled down.</summary>
		public static int HighDensity { get; set; } = 40;

		/// <summary>
		/// Fraction of the configured range applied at <see cref="HighDensity"/>. <b>1 disables the
		/// density shrink</b>, which is the default.
		/// </summary>
		/// <remarks>
		/// <para>
		/// This was a third existence cull, and the weakest of the three. It hides by DISTANCE and
		/// per OBJECT — a character in a crowd loses range symmetrically towards everyone, including
		/// the viewers who most needed to see it — whereas <see cref="VisibilityBudget"/> hides by
		/// RELEVANCE and per VIEWER, bounding exactly the same quantity (pairs = viewers × budget)
		/// while keeping the party members, targets and opponents the shrink was blind to.
		/// </para>
		/// <para>
		/// With the budget in place the shrink bought no bandwidth a crowd could notice and cost the
		/// authored 100 m range: at scale 0.5 a busy town quietly became a 50 m world, then a 25 m
		/// one at the old floor, which is the culling the 2026-09-08 audit was asked to remove. The
		/// machinery stays — set <c>ObserverRangeScaleAtHighDensity</c> below 1 to bring it back for
		/// a deployment that measures a need for it.
		/// </para>
		/// </remarks>
		public static float RangeScaleAtHighDensity { get; set; } = 1f;

		/// <summary>
		/// Absolute floor on any scaled range, in metres, so combat never happens out of sight.
		/// Inert while <see cref="RangeScaleAtHighDensity"/> is 1.
		/// </summary>
		/// <remarks>
		/// Raised from 25 m with the shrink's retirement: 25 m was below every ability reach and
		/// below the engagement radius that lag compensation assumes, so a re-enabled shrink hitting
		/// its floor would have despawned characters from inside the range they were being shot at.
		/// The floor is now half the authored range rather than a quarter of it.
		/// </remarks>
		public static float MinimumRange { get; set; } = 50f;

		/// <summary>
		/// Radius, in metres, inside which a character's transform is sent to an observer at FULL
		/// rate regardless of any distance or cap throttling.
		/// </summary>
		/// <remarks>
		/// <para>
		/// This is what makes lag compensation honest. A throttled transform reaches the observer
		/// every 3, 6 or 8 ticks, and the client interpolates across that gap — so what it renders is
		/// a position that existed on no server tick, and no rewind can reproduce it. Inside this
		/// radius the observer receives every tick, so the pose it saw IS a tick sample and the
		/// rewind lands exactly on it.
		/// </para>
		/// <para>
		/// It is a floor, not the whole rule: <see cref="ResolveEngagementRange"/> widens it to cover
		/// a character's own longest ability, up to <see cref="EngagementRangeCeiling"/>. Everything
		/// beyond keeps the bandwidth saving, which is where most of it lives — exempting a 40 m disc
		/// out of a 100 m observer range leaves 84% of the observed area still throttled.
		/// </para>
		/// </remarks>
		public static float EngagementRange { get; set; } = 40f;

		/// <summary>
		/// Hard cap on how many CHARACTERS one viewer may observe at once.
		/// </summary>
		/// <remarks>
		/// <para>
		/// The difference between this and <see cref="FullRateObserverCap"/> is existence versus
		/// fidelity. The full-rate cap slows the 25th character down; this one makes the 41st not
		/// exist on that client at all — no transform, no vitals, no buffs, no spawn. It is what
		/// bounds a client's cost in a town, where distance alone bounds nothing.
		/// </para>
		/// <para>
		/// Applies only to registered characters. Interactables, world items and scene objects are
		/// governed by their own distance conditions and are not budgeted — they are cheap and their
		/// absence is far more confusing than a missing distant stranger.
		/// </para>
		/// </remarks>
		public static int VisibilityBudget { get; set; } = 40;

		private static readonly Dictionary<ObserverClassification, int> visibilityBudgetOverrides =
			new Dictionary<ObserverClassification, int>();

		/// <summary>
		/// The budget in force for <paramref name="classification"/>: the server override when one
		/// is configured, otherwise <paramref name="authored"/> from the condition asset.
		/// 0 means unlimited.
		/// </summary>
		/// <remarks>
		/// Every read of a classification's budget goes through here, so an operator's
		/// <c>ObserverVisibilityBudgets</c> entry and a designer's asset value can never disagree
		/// about which one is in force.
		/// </remarks>
		/// <param name="classification">The classification being budgeted.</param>
		/// <param name="authored">The value authored on the classification's condition asset.</param>
		public static int ResolveVisibilityBudget(ObserverClassification classification, int authored)
		{
			if (visibilityBudgetOverrides.TryGetValue(classification, out int configured))
			{
				return configured < 0 ? 0 : configured;
			}
			return authored < 0 ? 0 : authored;
		}

		/// <summary>Overrides one classification's budget. 0 is unlimited.</summary>
		public static void SetVisibilityBudget(ObserverClassification classification, int budget)
		{
			visibilityBudgetOverrides[classification] = budget < 0 ? 0 : budget;
		}

		/// <summary>Drops every per-classification override, restoring the authored asset values.</summary>
		public static void ClearVisibilityBudgetOverrides() => visibilityBudgetOverrides.Clear();

		/// <summary>True when <paramref name="classification"/> has a configured override.</summary>
		public static bool HasVisibilityBudgetOverride(ObserverClassification classification)
			=> visibilityBudgetOverrides.ContainsKey(classification);

		/// <summary>
		/// How far past <see cref="VisibilityBudget"/> an ALREADY VISIBLE character keeps its slot,
		/// as a fraction of the budget.
		/// </summary>
		/// <remarks>
		/// Rank hysteresis, not distance hysteresis. Two characters of near-identical score sitting
		/// either side of the boundary would otherwise swap every pass, and each swap is a spawn and
		/// a despawn — far more expensive than any rate change, and visible as flicker.
		/// </remarks>
		public static float VisibilityBudgetHysteresis { get; set; } = 0.25f;

		/// <summary>
		/// How many characters inside the engagement radius receive every tick.
		/// </summary>
		/// <remarks>
		/// The engagement exemption exists so lag compensation can rewind to a real tick sample, and
		/// without a budget it is unbounded: thirty players inside 40 m is thirty full-rate streams.
		/// The top entries by relevance keep every tick; the rest inside the radius fall to every
		/// second tick, which costs them a compensation accuracy of one tick — around 3 cm at walking
		/// speed, against the six ticks the distance band would otherwise have given them.
		/// </remarks>
		public static int EngagedFullRateBudget { get; set; } = 12;

		/// <summary>Interval applied to engaged characters beyond <see cref="EngagedFullRateBudget"/>. Clamped to <see cref="MaxSendInterval"/>.</summary>
		public static byte EngagedOverflowInterval
		{
			get => ClampInterval(engagedOverflowInterval);
			set => engagedOverflowInterval = value;
		}
		private static byte engagedOverflowInterval = 2;

		/// <summary>
		/// Hard ceiling on the engagement radius: no attack or ability in this project reaches
		/// further, so nothing beyond it can ever need tick-exact compensation.
		/// </summary>
		public static float EngagementRangeCeiling { get; set; } = 100f;

		/// <summary>
		/// Metres added to a character's longest ability range when resolving its engagement radius,
		/// covering the ground both parties can close during the compensation window.
		/// </summary>
		/// <remarks>
		/// The rewind reaches up to <c>LagCompensationTick.MaximumCompensationTicks</c> into the past;
		/// at a closing speed of roughly 12 m/s that is a few metres, and a target that is about to
		/// come into range needs to already be at full rate when it does.
		/// </remarks>
		public static float EngagementRangeMargin { get; set; } = 10f;

		/// <summary>
		/// The full-rate radius for an observer whose longest usable ability reaches
		/// <paramref name="longestAbilityRange"/>.
		/// </summary>
		/// <remarks>
		/// Melee characters keep almost all of the LOD saving; a long-range caster pays for exactly
		/// the reach it has. Note that every ability authored today resolves to a range of 0
		/// (<c>Ability.Range</c> is <c>Speed * LifeTime</c>, and no template sets Speed), so the
		/// floor is currently doing all the work — this widens automatically once ranges are authored.
		/// </remarks>
		/// <param name="longestAbilityRange">Longest range among the character's known abilities.</param>
		/// <returns>The radius inside which throttling is suspended.</returns>
		public static float ResolveEngagementRange(float longestAbilityRange)
		{
			float wanted = longestAbilityRange > 0f ? longestAbilityRange + EngagementRangeMargin : 0f;
			float range = wanted > EngagementRange ? wanted : EngagementRange;
			if (range > EngagementRangeCeiling)
			{
				range = EngagementRangeCeiling;
			}
			return range < 0f ? 0f : range;
		}

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
					return ClampInterval(lodBands[i].Interval);
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
		/// <c>ObserverMinimumRange</c>, <c>ObserverRescheduleTicks</c>, <c>ObserverEngagementRange</c>,
		/// <c>ObserverVisibilityBudget</c>, <c>ObserverVisibilityBudgetHysteresis</c>,
		/// <c>ObserverEngagedFullRateBudget</c>, <c>ObserverEngagementRangeCeiling</c>,
		/// <c>ObserverEngagementRangeMargin</c>, <c>ObserverMaxSendInterval</c>, and
		/// <c>ObserverLodBands</c> as <c>distance:interval,distance:interval,...</c>
		/// (e.g. <c>60:1,inf:2</c>; intervals above <see cref="MaxSendInterval"/> are clamped).
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
				case "ObserverEngagementRange": return TryFloat(value, v => EngagementRange = Mathf.Max(0f, v));
				case "ObserverVisibilityBudget": return TryInt(value, v => VisibilityBudget = Math.Max(0, v));
				case "ObserverVisibilityBudgetHysteresis": return TryFloat(value, v => VisibilityBudgetHysteresis = Mathf.Max(0f, v));
				case "ObserverEngagedFullRateBudget": return TryInt(value, v => EngagedFullRateBudget = Math.Max(0, v));
				case "ObserverMaxSendInterval": return TryInt(value, v => MaxSendInterval = (byte)Mathf.Clamp(v, 1, 255));
				case "ObserverEngagementRangeCeiling": return TryFloat(value, v => EngagementRangeCeiling = Mathf.Max(0f, v));
				case "ObserverEngagementRangeMargin": return TryFloat(value, v => EngagementRangeMargin = Mathf.Max(0f, v));
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
				case "ObserverVisibilityBudgets": return TryParseVisibilityBudgets(value);
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

		/// <summary>
		/// Parses <c>Classification:budget,Classification:budget,...</c> — e.g.
		/// <c>Player:40,Monster:30,Titan:0</c> — into per-classification overrides. 0 is unlimited.
		/// </summary>
		/// <remarks>
		/// All or nothing: a malformed entry leaves every override untouched, so a typo in one
		/// classification cannot silently apply the others and leave the server running a
		/// configuration nobody wrote.
		/// </remarks>
		private static bool TryParseVisibilityBudgets(string value)
		{
			Dictionary<ObserverClassification, int> parsed = new Dictionary<ObserverClassification, int>();
			foreach (string part in value.Split(','))
			{
				string trimmed = part.Trim();
				if (trimmed.Length == 0)
				{
					continue;
				}
				string[] pair = trimmed.Split(':');
				if (pair.Length != 2)
				{
					return false;
				}
				if (!Enum.TryParse(pair[0].Trim(), true, out ObserverClassification classification) ||
					!Enum.IsDefined(typeof(ObserverClassification), classification))
				{
					return false;
				}
				if (!int.TryParse(pair[1].Trim(), System.Globalization.NumberStyles.Integer,
						System.Globalization.CultureInfo.InvariantCulture, out int budget) ||
					budget < 0)
				{
					return false;
				}
				parsed[classification] = budget;
			}

			foreach (KeyValuePair<ObserverClassification, int> entry in parsed)
			{
				SetVisibilityBudget(entry.Key, entry.Value);
			}
			return true;
		}
	}
}
