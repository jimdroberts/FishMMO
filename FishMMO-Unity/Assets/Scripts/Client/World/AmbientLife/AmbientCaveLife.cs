using System;
using System.Collections.Generic;
using FishMMO.Shared;
using FishMMO.Shared.Biomes;
using UnityEngine;

namespace FishMMO.Client
{
	/// <summary>
	/// The caves' animals (2026-10-18: "Caves need critters, bats, lizards, etc."): bat colonies hanging wing-wrapped from the
	/// high roofs, circling their chambers at dusk and streaming out of the mouth into the night; crickets, spiders and geckos on
	/// the walls and floors; rats in from the mouth; salamanders on the wet rock and olms in the pools; moths in the twilight
	/// and fungus gnats glowing under the glow-worm roofs.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Placed from the cut's spots, kept to them.</b> The terrain's one height a place is the hill over a cave, so nothing
	/// underground is placed from it. The cut measured each system's flat patches of floor, wall and roof, its clear air, its
	/// water and its mouths (<see cref="SceneCaveLifeSpot"/>, PointOfInterestCaveDressing.Life); a group is hashed from its
	/// scene, its spot's index and its kind, and every member stays within its spot's flat radius on its plane (or inside its
	/// box of air, under its pool's surface): on the rock without a ray asked of the scene, the same for every player.
	/// </para>
	/// <para>
	/// <b>No sky, no weather, no season's heat.</b> Below the twilight there is no day (<see cref="Activity"/> darkens the
	/// moment's light by a spot's <see cref="SceneCaveLifeSpot.Dark"/>), rain, snow, wind and storms do not reach, and the air
	/// is the ground's year mean (<see cref="CaveAir"/>), not the open air's. Only the bats read the night outside, for when to go out to
	/// feed and when to come back (<see cref="BatHours"/>); in winter they hang asleep.
	/// </para>
	/// </remarks>
	public static class AmbientCaveLife
	{
		// ── Where ─────────────────────────────────────────────────────

		/// <summary>The niche bit a spot of a kind is.</summary>
		public static AmbientCaveNiche NicheOf(SceneCaveLifeKind kind) => (AmbientCaveNiche)(1 << (int)kind);

		/// <summary>The form bit a cave of a form is.</summary>
		public static AmbientCaveForms FormOf(SceneCaveForm form) => (AmbientCaveForms)(1 << (int)form);

		/// <summary>
		/// How much a cave of a form holds against a limestone cave's: a mine's dry drifts most of it, a lava tube's bare basalt
		/// little, an ice cave next to nothing (no food comes into it, and nothing lives on ice).
		/// </summary>
		public static float FormShare(SceneCaveForm form)
		{
			switch (form)
			{
				case SceneCaveForm.Mine: return 0.7f;
				case SceneCaveForm.LavaTube: return 0.45f;
				case SceneCaveForm.IceCave: return 0.18f;
				// A sea hall's dry ice holds as little as an ice cave's; its life is its sea's (SeaLifeSystem, its Pool spot).
				case SceneCaveForm.SeaHall: return 0.18f;
				default: return 1f;
			}
		}

		/// <summary>
		/// The share of the air's frost the ground feels under deep snow on wet country (a winter n-factor: the snow keeps the
		/// cold off the ground; 0.2-0.4 measured under deep snow).
		/// </summary>
		public const float DeepSnowFrost = 0.35f;

		/// <summary>The same under thin, wind-scoured snow on dry country (0.5-0.9 measured on tundra).</summary>
		public const float ThinSnowFrost = 0.85f;

		/// <summary>
		/// A cave's air on the climate scale (-1..1): the rock's, which is the ground's year mean at the place
		/// (<see cref="GroundYearMean"/>) — a cave's air is steady because the rock around it is. Wet country's winters bury
		/// the ground in deeper snow than dry country's. Where the seasons are not known, the air's own year mean.
		/// </summary>
		/// <param name="yearMean">The open air's year mean (the painted climate).</param>
		/// <param name="warmest">The warmest season's temperature, same scale.</param>
		/// <param name="humidity">The country's humidity, -1 driest .. 1 wettest.</param>
		public static float CaveAir(float yearMean, float warmest, bool seasonKnown, float humidity)
		{
			if (!seasonKnown)
			{
				return yearMean;
			}
			float snow = Mathf.Lerp(ThinSnowFrost, DeepSnowFrost, Mathf.InverseLerp(-0.6f, 0.4f, humidity));
			return GroundYearMean(yearMean, warmest, snow);
		}

		/// <summary>
		/// The ground's year mean under a year of air: the air's own where it never freezes; where it does, warmer by the share
		/// of the frost the snow keeps off (<paramref name="snowFrost"/> of it reaches the ground). The year is a sine about
		/// <paramref name="yearMean"/> peaking at <paramref name="warmest"/>, and its frost (the year's mean of the air below
		/// freezing) is that sine's, exactly: with α = acos(−mean / amplitude), (mean·(π − α) − amplitude·sin α) / π.
		/// </summary>
		/// <remarks>
		/// Why caves under cold mountains are not frozen through: a −3 °C year with a +10 °C summer under deep snow keeps
		/// its ground at about 0 °C, the edge of permafrost — as measured, where the year's air is between about −1 and −4 °C.
		/// </remarks>
		public static float GroundYearMean(float yearMean, float warmest, float snowFrost)
		{
			float amplitude = Mathf.Max(0f, warmest - yearMean);
			float frost;
			if (yearMean >= amplitude)
			{
				frost = 0f;
			}
			else if (yearMean <= -amplitude)
			{
				frost = yearMean;
			}
			else
			{
				float alpha = Mathf.Acos(-yearMean / amplitude);
				frost = (yearMean * (Mathf.PI - alpha) - amplitude * Mathf.Sin(alpha)) / Mathf.PI;
			}
			return yearMean - (1f - Mathf.Clamp01(snowFrost)) * frost;
		}

		/// <summary>
		/// The habitat at a cave-life spot (2026-10-25, phase I2n): a linked cave's spot carries its interior biome
		/// (<see cref="SceneCaveLifeSpot.Biome"/>), whose habitat replaces the land's over it but for its Barren bit, which stays the
		/// land's (the land over a cave is its air and water; BiomeCaveInteriors.LinkedHabitat). A spot of none, or of a biome this
		/// client does not hold, keeps the land's.
		/// </summary>
		public static AmbientHabitat SpotHabitat(in SceneCaveLifeSpot spot, AmbientHabitat land)
		{
			if (spot.Biome == 0 || !BiomeRegistry.TryGetByID(spot.Biome, out BiomeTemplate inside) || inside == null)
			{
				return land;
			}
			AmbientHabitat own = AmbientGating.HabitatOfTemplate(inside);
			// A sea hall keeps its interior's habitat whole (2026-10-26, I2n-b, decision 83 Q7: not the airless ice's Barren bit).
			return own == AmbientHabitat.None ? land
				: (AmbientHabitat)BiomeCaveInteriors.LinkedHabitat((BiomeCaveForm)spot.Form, (int)land, (int)own, (int)AmbientHabitat.Barren);
		}

		/// <summary>Whether a kind lives underground at all.</summary>
		public static bool LivesInCaves(AmbientCreatureKind kind) => kind != null && kind.Enabled && kind.CaveNiche != AmbientCaveNiche.None && kind.CavePerSpot > 0f;

		/// <summary>
		/// How much at home a kind is at a cave-life spot, 0 (not at all) .. 1: a spot of its niche, in a cave of its forms, as far
		/// from the daylight as it lives, wet where it needs the wet, fresh water or salt as it needs; the cave's air (the year's
		/// mean, <paramref name="temperature"/>) in its climate band (soft edges a tenth wide, as on the land); the form's share.
		/// Nothing under barren ground (the land over the cave is the cave's air and water). Which world it is on is asked
		/// first, by placement.
		/// </summary>
		public static float Weight(AmbientCreatureKind kind, in SceneCaveLifeSpot spot, AmbientHabitat habitat, float temperature)
		{
			if (!LivesInCaves(kind) || (habitat & AmbientHabitat.Barren) != 0)
			{
				return 0f;
			}
			if ((kind.CaveNiche & NicheOf(spot.Kind)) == 0 || (kind.CaveForms & FormOf(spot.Form)) == 0)
			{
				return 0f;
			}
			if (spot.Dark < kind.CaveDark.x - 1e-4f || spot.Dark > kind.CaveDark.y + 1e-4f)
			{
				return 0f;
			}
			bool damp = (spot.Flags & SceneCaveLifeFlags.Damp) != 0, salt = (spot.Flags & SceneCaveLifeFlags.Salt) != 0;
			if (kind.CaveNeedsDamp && !damp)
			{
				return 0f;
			}
			// A sea creature by the sea's water; the fresh water's (olms, salamanders) never by salt.
			if (kind.NeedsSea ? !salt : salt && kind.CaveNeedsDamp)
			{
				return 0f;
			}
			float warm = kind.Climate.x <= -1f ? 1f : Mathf.Clamp01((temperature - kind.Climate.x) / 0.1f + 0.5f);
			float cool = kind.Climate.y >= 1f ? 1f : Mathf.Clamp01((kind.Climate.y - temperature) / 0.1f + 0.5f);
			return Mathf.Min(warm, cool) * FormShare(spot.Form);
		}

		// ── When ──────────────────────────────────────────────────────

		/// <summary>
		/// The share of a kind's animals about underground, 0..1 (<see cref="AmbientGating.Activity"/> in the cave's own moment):
		/// the day's light only in the twilight (none past <see cref="SceneCaveLifeSpot.Dark"/> 0.6), no rain, snow, wind or storm,
		/// and the cave's steady air for the temperature. The season still counts: what hibernates sleeps the winter through.
		/// </summary>
		public static float Activity(AmbientCreatureKind kind, in AmbientConditions outside, float steady, float dark)
		{
			float open = 1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(dark / 0.6f));
			var under = new AmbientConditions
			{
				Light = outside.Light * open,
				Twilight = outside.Twilight * open,
				Evening = outside.Evening,
				Temperature = steady,
				Summer = outside.Summer,
			};
			return AmbientGating.Activity(kind, under);
		}

		/// <summary>What a cave's bat colony is doing at a moment, as shares of it.</summary>
		public struct CaveBatHours
		{
			/// <summary>Out of the cave feeding (none of these are drawn in it).</summary>
			public float Out;
			/// <summary>Circling the cave's chambers (light-sampling at dusk, the night's stragglers).</summary>
			public float Stirring;
			/// <summary>How thick the stream out of the mouth is (dusk) and back into it (before dawn), 0..1.</summary>
			public float Leaving, Returning;
		}

		/// <summary>
		/// A colony's night, from the night outside: out from dusk to before dawn (most of it in the first hours of dark), streaming
		/// out of the mouth as the light goes and back in the last of the dark; never out in the cold (under about 5 °C nothing
		/// flies for them to eat), in rain, a strong wind or a storm, and through the winter none at all — they hang in the cave
		/// asleep. A few always stir in the roost; at dusk many circle the cave first.
		/// </summary>
		public static CaveBatHours BatHours(in AmbientConditions c)
		{
			float cold = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(AmbientLifeCatalogue.Celsius(3f), AmbientLifeCatalogue.Celsius(8f), c.Temperature));
			float wet = Mathf.Clamp01(1f - c.Rain / 0.15f) * (1f - 0.85f * Mathf.Clamp01(c.Snow * 1.5f));
			float calm = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.3f, 0.55f, c.Wind));
			float storm = 1f - Mathf.InverseLerp(0.35f, AmbientGating.StormShelter, c.Storm);
			float awake = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.3f, 0.65f, Mathf.Clamp01(-c.Summer)));
			float fit = cold * wet * calm * storm * awake;
			float night = Mathf.Clamp01((1f - c.Light) * (c.Evening ? 0.75f + 0.35f * c.Twilight : 0.6f));
			float edge = c.Twilight * (1f - 0.6f * c.Light);
			return new CaveBatHours
			{
				Out = 0.9f * night * fit,
				Stirring = awake * (0.1f + (c.Evening ? 0.35f * c.Twilight : 0f) + 0.08f * (1f - c.Light) * (1f - fit)),
				Leaving = c.Evening ? edge * fit : 0f,
				Returning = c.Evening ? 0f : edge * fit,
			};
		}

		// ── Placement ─────────────────────────────────────────────────

		/// <summary>
		/// A kind's groups at one cave-life spot, appended to <paramref name="groups"/>: hashed from the scene, the spot's index in
		/// its scene and the kind, so the same for everyone whenever the spot's cell is placed.
		/// </summary>
		/// <param name="slot">The spot's index + 1 in its cell's cave spots (<see cref="AmbientGroup.Cave"/>).</param>
		/// <returns>How many groups it added.</returns>
		public static int TryPlace(AmbientCreatureKind kind, int kindIndex, float density, uint sceneSeed, int spotIndex, in SceneCaveLifeSpot spot, int slot,
			IAmbientWorld world, List<AmbientGroup> groups)
		{
			if (!LivesInCaves(kind) || density <= 0f || world == null || !AmbientGating.LivesOn(kind, world.World))
			{
				return 0;
			}
			if ((kind.CaveNiche & NicheOf(spot.Kind)) == 0 || (kind.CaveForms & FormOf(spot.Form)) == 0)
			{
				return 0;
			}
			world.CaveHabitat(spot.Position, out AmbientHabitat habitat, out float temperature);
			habitat = SpotHabitat(in spot, habitat);
			float expected = kind.CavePerSpot * density * Weight(kind, spot, habitat, temperature);
			int added = 0;
			int candidates = Mathf.Min(3, Mathf.CeilToInt(expected));
			for (int i = 0; i < candidates; i++)
			{
				uint seed = SeaLifePlacement.Hash(sceneSeed ^ 0xCA7E11F0u, spotIndex, kindIndex, i);
				if (Unit(Mix(seed ^ 0x5u)) >= Mathf.Clamp01(expected - i))
				{
					continue;
				}
				groups.Add(new AmbientGroup
				{
					Kind = kindIndex,
					Seed = seed,
					Home = spot.Position,
					Radius = Mathf.Lerp(kind.Roam.x, kind.Roam.y, Unit(Mix(seed ^ 0x6u))),
					Episode = Mathf.Lerp(kind.Episode.x, kind.Episode.y, Unit(Mix(seed ^ 0x8u))),
					Count = CountAt(kind, spot.Kind, Unit(Mix(seed ^ 0x9u))),
					Length = Mathf.Lerp(kind.Length.x, kind.Length.y, Unit(Mix(seed ^ 0xAu))),
					Rank = Unit(Mix(seed ^ 0xBu)),
					Cave = slot,
					Steady = temperature,
				});
				added++;
				// One colony a roost, one stream a mouth, one cloud a glow: a second would stand in the first.
				if (spot.Kind == SceneCaveLifeKind.Roost || spot.Kind == SceneCaveLifeKind.Mouth || spot.Kind == SceneCaveLifeKind.Glow)
				{
					break;
				}
			}
			return added;
		}

		/// <summary>
		/// Members of a group at a spot: a roost's whole colony; as many that may wheel in a box of air (a roost is usually high
		/// in the dark, 20 m up in a hall: the ones flying are the ones seen); a mouth's stream half as many again (each a place
		/// in the stream, not a bat of the roost); anything else its kind's own number.
		/// </summary>
		public static int CountAt(AmbientCreatureKind kind, SceneCaveLifeKind spot, float roll)
		{
			int count = Mathf.Max(1, Mathf.RoundToInt(Mathf.Lerp(kind.GroupSize.x, kind.GroupSize.y + 0.999f, roll) - 0.5f));
			if (kind.Shape != AmbientShape.Bat)
			{
				return count;
			}
			switch (spot)
			{
				case SceneCaveLifeKind.Flight: return Mathf.Max(6, count);
				case SceneCaveLifeKind.Mouth: return Mathf.Clamp(count * 3 / 2, 12, 48);
				default: return count;
			}
		}

		/// <summary>The farthest any member of a cave group strays from its spot's position, metres.</summary>
		public static float Reach(AmbientCreatureKind kind, in AmbientGroup g, in SceneCaveLifeSpot spot)
		{
			switch (spot.Kind)
			{
				case SceneCaveLifeKind.Flight:
				case SceneCaveLifeKind.Glow:
					return Mathf.Max(spot.Extent.x, Mathf.Max(spot.Extent.y, spot.Extent.z)) + kind.Spread + 0.5f;
				case SceneCaveLifeKind.Mouth:
					return spot.Extent.z + StreamOutside + 4f;
				default:
					return spot.Extent.x + 0.5f;
			}
		}

		// ── Members ───────────────────────────────────────────────────

		/// <summary>Metres a mouth's stream is drawn past the mouth, climbing away into the night.</summary>
		public const float StreamOutside = 30f;

		/// <summary>Every member of a cave group at the frame's moment, appended to <paramref name="into"/>.</summary>
		public static void Members(in AmbientGroup g, AmbientCreatureKind kind, in SceneCaveLifeSpot spot, ref AmbientLifeMotion.Frame f, List<AmbientInstance> into)
		{
			switch (spot.Kind)
			{
				case SceneCaveLifeKind.Roost:
					Roost(g, kind, spot, ref f, into);
					break;
				case SceneCaveLifeKind.Flight:
					Wheel(g, kind, spot, ref f, into);
					break;
				case SceneCaveLifeKind.Mouth:
					Stream(g, kind, spot, ref f, into);
					break;
				case SceneCaveLifeKind.Glow:
					// A swarm hangs in its glow's air as one cloud; a wisp moth wheels in it as in any box of air.
					if (kind.Shape == AmbientShape.Swarm)
					{
						Glow(g, kind, spot, ref f, into);
					}
					else
					{
						Wheel(g, kind, spot, ref f, into);
					}
					break;
				case SceneCaveLifeKind.Pool:
					for (int m = 0; m < g.Count; m++)
					{
						if (kind.Shape == AmbientShape.Jelly)
						{
							Drift(g, kind, spot, m, ref f, into);
						}
						else
						{
							Swim(g, kind, spot, m, ref f, into);
						}
					}
					break;
				default:
					for (int m = 0; m < g.Count; m++)
					{
						Crawl(g, kind, spot, m, ref f, into);
					}
					break;
			}
		}

		private static uint Mix(uint x) => SeaLifePlacement.Mix(x);
		private static float Unit(uint h) => SeaLifePlacement.Unit(h);

		/// <summary>A point in a disc (x, y on its plane), uniform over its area.</summary>
		private static Vector2 Disc(uint h, float radius)
		{
			float a = Unit(h) * Mathf.PI * 2f;
			float r = Mathf.Sqrt(Unit(Mix(h ^ 0x51u))) * radius;
			return new Vector2(Mathf.Cos(a) * r, Mathf.Sin(a) * r);
		}

		/// <summary>A patch's plane: its normal (out of the rock) and two directions along it.</summary>
		private static void Plane(in SceneCaveLifeSpot spot, out Vector3 n, out Vector3 t1, out Vector3 t2)
		{
			n = spot.Normal.sqrMagnitude > 1e-6f ? spot.Normal.normalized : Vector3.up;
			t1 = Vector3.Cross(Mathf.Abs(n.y) > 0.9f ? Vector3.forward : Vector3.up, n).normalized;
			t2 = Vector3.Cross(n, t1);
		}

		/// <summary>A box's axes (along its yaw, up, across).</summary>
		private static void Axes(float yaw, out Vector3 along, out Vector3 across)
		{
			float r = yaw * Mathf.Deg2Rad;
			along = new Vector3(Mathf.Sin(r), 0f, Mathf.Cos(r));
			across = new Vector3(along.z, 0f, -along.x);
		}

		// ── On the rock ───────────────────────────────────────────────

		/// <summary>
		/// A crawler on its patch of floor or wall: resting, then off to another spot within its run in an episode's last
		/// moments (on the patch's plane, its back to the air), as a ground animal on the land does; frightened, it runs a step
		/// and is gone into a crack until the coast has been clear a while.
		/// </summary>
		private static void Crawl(in AmbientGroup g, AmbientCreatureKind kind, in SceneCaveLifeSpot spot, int m, ref AmbientLifeMotion.Frame f, List<AmbientInstance> into)
		{
			double t = f.Seconds;
			Plane(spot, out Vector3 n, out Vector3 t1, out Vector3 t2);
			float patch = Mathf.Max(0.15f, spot.Extent.x * 0.9f);
			uint own = AmbientLifeMotion.MemberSeed(g.Seed, m);
			float size = g.Length * Mathf.Lerp(0.85f, 1.15f, Unit(Mix(own ^ 0x81u)));
			// Its own corner of the patch, and its run about it: both inside the patch, so every spot it goes to is on the rock.
			Vector2 home = Disc(Mix(own ^ 0x80u), patch * 0.45f);
			float run = Mathf.Min(g.Radius, patch * 0.45f);
			double length = g.Episode * Mathf.Lerp(0.75f, 1.25f, Unit(Mix(own ^ 0x82u)));
			long episode = AmbientLifeMotion.EpisodeAt(t, length, Unit(Mix(own ^ 0x83u)), out double start);
			float tau = (float)(t - start), len = (float)length;
			Vector2 here = home + Disc(AmbientLifeMotion.EpisodeSeed(own, episode), run);
			Vector2 there = home + Disc(AmbientLifeMotion.EpisodeSeed(own, episode + 1), run);
			bool lizard = kind.Shape == AmbientShape.Lizard;
			float dash = lizard && !kind.RainLover ? 3f : 1f;
			float move = Mathf.Clamp(Vector2.Distance(here, there) / Mathf.Max(0.01f, kind.Speed * dash), 0.3f, len * 0.6f);
			float gaitHz = kind.StrokeHz * (lizard ? 1.6f : 1f);

			Vector2 at, heading;
			float stride;
			if (tau > len - move)
			{
				float u = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((tau - (len - move)) / move));
				at = Vector2.Lerp(here, there, u);
				heading = there - here;
				stride = 1f;
			}
			else
			{
				at = here;
				float yaw = Unit(AmbientLifeMotion.EpisodeSeed(own ^ 0x84u, episode)) * 6.2831853f + 0.3f * Mathf.Sin(AmbientLifeMotion.Angle(t, 0.15, Unit(own) * 6.28f));
				heading = new Vector2(Mathf.Cos(yaw), Mathf.Sin(yaw));
				stride = 0f;
			}
			Vector3 position = spot.Position + t1 * at.x + t2 * at.y;
			Vector3 forward = t1 * heading.x + t2 * heading.y;
			float visible = f.Visible;

			ref AmbientMemberState state = ref f.States[g.StateFirst + m];
			if (state.Refuge == AmbientMemberState.None && f.Threats != null && f.Threats.Frightens(kind, position, out Vector3 threat))
			{
				Vector3 flee = AmbientGating.FleeDirection(position, threat, AmbientLifeMotion.EpisodeSeed(own, (long)Math.Floor(t)));
				flee -= n * Vector3.Dot(flee, n);
				state.Refuge = AmbientMemberState.Gone;
				state.At = t;
				state.Return = 0.0;
				state.From = position;
				state.Flee = flee.sqrMagnitude > 1e-6f ? flee.normalized : t1;
			}
			if (state.Refuge != AmbientMemberState.None)
			{
				float away = (float)(t - state.At);
				float clear = AmbientGating.FlightDistance(kind, 0f) * 2.2f;
				if (state.Return <= 0.0 && away > AmbientLifeMotion.HideSeconds && (f.Threats == null || f.Threats.NearestSq(spot.Position) > clear * clear))
				{
					state.Return = t;
				}
				float back = state.Return > 0.0 ? (float)(t - state.Return) : -1f;
				if (back >= 2f * AmbientLifeMotion.FadeSeconds)
				{
					state = default;
				}
				else if (back >= AmbientLifeMotion.FadeSeconds)
				{
					visible *= (back - AmbientLifeMotion.FadeSeconds) / AmbientLifeMotion.FadeSeconds;
				}
				else
				{
					// A dash across the rock (never off the patch), then into a crack.
					const float dashSeconds = 0.6f;
					float dashed = Mathf.Min(away, dashSeconds) * Mathf.Max(0.2f, kind.FleeSpeed);
					Vector3 offset = state.From + state.Flee * dashed - spot.Position;
					Vector2 onPlane = new Vector2(Vector3.Dot(offset, t1), Vector3.Dot(offset, t2));
					if (onPlane.sqrMagnitude > patch * patch)
					{
						onPlane = onPlane.normalized * patch;
					}
					float gone = (away - dashSeconds) / AmbientLifeMotion.FadeSeconds;
					if (gone >= 1f || back >= 0f)
					{
						return; // in its crack
					}
					visible *= 1f - Mathf.Clamp01(gone);
					float fleePhase = SeaLifePlacement.Cycle(t, 1.0 / Math.Max(0.1, gaitHz * 1.8f), Unit(own));
					AmbientLifeMotion.Emit(into, kind, own, spot.Position + t1 * onPlane.x + t2 * onPlane.y, Quaternion.LookRotation(state.Flee, n), size,
						new Vector4(fleePhase, 1f, 0f, 0f), visible);
					return;
				}
			}

			float alert = 0f;
			if (stride <= 0f && (kind.Shape == AmbientShape.Rat || (lizard && !kind.RainLover)))
			{
				// A rat rears to sniff the air; a gecko does push-ups.
				float c = SeaLifePlacement.Cycle(t, 5.0 + 4.0 * Unit(Mix(own ^ 0x85u)), Unit(Mix(own ^ 0x86u)));
				alert = c < 0.5f ? Mathf.Sin(c / 0.5f * Mathf.PI) : 0f;
			}
			if (forward.sqrMagnitude < 1e-8f)
			{
				forward = t1;
			}
			// A crab goes sideways: its body turned square to the way it walks.
			Quaternion rotation = Quaternion.LookRotation(kind.Shape == AmbientShape.Crab ? Vector3.Cross(n, forward) : forward, n);
			if (alert > 0f && kind.Shape == AmbientShape.Rat)
			{
				rotation *= Quaternion.Euler(-30f * alert, 0f, 0f);
			}
			float phase = SeaLifePlacement.Cycle(t, 1.0 / Math.Max(0.1, gaitHz * (stride > 0f ? 1f : 0.35f)), Unit(Mix(own ^ 0x87u)));
			AmbientLifeMotion.Emit(into, kind, own, position, rotation, size, new Vector4(phase, stride, 0f, alert), visible);
		}

		// ── In the water ──────────────────────────────────────────────

		/// <summary>An olm: a slow loop under its pool's surface, its body waving as it swims, never past the pool's deep edge.</summary>
		private static void Swim(in AmbientGroup g, AmbientCreatureKind kind, in SceneCaveLifeSpot spot, int m, ref AmbientLifeMotion.Frame f, List<AmbientInstance> into)
		{
			double t = f.Seconds;
			uint own = AmbientLifeMotion.MemberSeed(g.Seed, m);
			float size = g.Length * Mathf.Lerp(0.85f, 1.15f, Unit(Mix(own ^ 0x81u)));
			float r = Mathf.Max(0.2f, spot.Extent.x * 0.75f * Mathf.Lerp(0.5f, 1f, Unit(Mix(own ^ 0x90u))) - 0.5f * size);
			float depth = Mathf.Clamp(spot.Extent.y * 0.5f, 0.08f, 0.35f) * Mathf.Lerp(0.7f, 1f, Unit(Mix(own ^ 0x91u)));
			double w = Math.Max(0.02, kind.Speed) / r;
			Vector3 surface = spot.Position;
			float p1 = Unit(Mix(own ^ 0x92u)) * 6.28f, p2 = Unit(Mix(own ^ 0x93u)) * 6.28f, p3 = Unit(Mix(own ^ 0x94u)) * 6.28f;
			Vector3 Position(double at)
			{
				float x = r * (0.65f * Mathf.Sin(AmbientLifeMotion.Angle(at, w, p1)) + 0.3f * Mathf.Sin(AmbientLifeMotion.Angle(at, w * 2.3, p2)));
				float z = r * (0.65f * Mathf.Cos(AmbientLifeMotion.Angle(at, w * 0.9, p2)) + 0.3f * Mathf.Cos(AmbientLifeMotion.Angle(at, w * 2.1, p3)));
				float y = -depth + 0.03f * Mathf.Sin(AmbientLifeMotion.Angle(at, w * 1.7, p3));
				return surface + new Vector3(x, y, z);
			}
			Vector3 now = Position(t), soon = Position(t + 0.5);
			Vector3 heading = soon - now;
			heading.y = 0f;
			float phase = SeaLifePlacement.Cycle(t, 1.0 / Math.Max(0.1, kind.StrokeHz), Unit(Mix(own ^ 0x95u)));
			AmbientLifeMotion.Emit(into, kind, own, now, Quaternion.LookRotation(heading.sqrMagnitude > 1e-8f ? heading : Vector3.forward, Vector3.up), size,
				new Vector4(phase, 1f, 0f, 0f), f.Visible);
		}

		/// <summary>
		/// A jelly: hanging in its pool's water bell up, rowing by its bell's beat (each squeeze lifts it a little, and it sinks
		/// back as the bell relaxes), wandering a slow loop and turning slowly about itself. Its beat's phase is its light's
		/// (<see cref="AmbientGlow.PhaseAt"/>: the same seed), so the light runs out from the crown as the bell squeezes.
		/// </summary>
		private static void Drift(in AmbientGroup g, AmbientCreatureKind kind, in SceneCaveLifeSpot spot, int m, ref AmbientLifeMotion.Frame f, List<AmbientInstance> into)
		{
			double t = f.Seconds;
			uint own = AmbientLifeMotion.MemberSeed(g.Seed, m);
			float size = g.Length * Mathf.Lerp(0.85f, 1.15f, Unit(Mix(own ^ 0x81u)));
			float r = Mathf.Max(0.15f, spot.Extent.x * 0.7f * Mathf.Lerp(0.4f, 1f, Unit(Mix(own ^ 0x90u))) - size);
			// Deep enough that the tentacles (a body's length under the bell) stay in the water, never past the bed.
			float depth = Mathf.Clamp(spot.Extent.y * Mathf.Lerp(0.3f, 0.6f, Unit(Mix(own ^ 0x91u))), Mathf.Min(0.12f + 0.5f * size, spot.Extent.y * 0.5f),
				Mathf.Max(0.12f, spot.Extent.y - 1.6f * size));
			double w = Math.Max(0.01, kind.Speed) / r;
			float p1 = Unit(Mix(own ^ 0x92u)) * 6.28f, p2 = Unit(Mix(own ^ 0x93u)) * 6.28f, p3 = Unit(Mix(own ^ 0x94u)) * 6.28f;
			float phase = SeaLifePlacement.Cycle(t, 1.0 / Math.Max(0.1, kind.StrokeHz), Unit(Mix(own ^ 0x95u)));
			float x = r * (0.7f * Mathf.Sin(AmbientLifeMotion.Angle(t, w, p1)) + 0.25f * Mathf.Sin(AmbientLifeMotion.Angle(t, w * 2.1, p2)));
			float z = r * (0.7f * Mathf.Cos(AmbientLifeMotion.Angle(t, w * 0.87, p2)) + 0.25f * Mathf.Cos(AmbientLifeMotion.Angle(t, w * 1.9, p3)));
			float y = -depth + 0.04f * Mathf.Sin(AmbientLifeMotion.Angle(t, 0.11, p3)) + 0.25f * size * AmbientGlow.Squeeze(phase);
			float yaw = Unit(Mix(own ^ 0x96u)) * 360f + AmbientLifeMotion.Angle(t, 0.05, p1) * Mathf.Rad2Deg;
			AmbientLifeMotion.Emit(into, kind, own, spot.Position + new Vector3(x, y, z), Quaternion.Euler(0f, yaw, 0f), size, new Vector4(phase, 1f, 0f, 0f), f.Visible);
		}

		// ── Bats ──────────────────────────────────────────────────────

		/// <summary>A member's rank in its colony: the same bats are out (or hanging) for everyone, and go and come back in the same order.</summary>
		private static float ColonyRank(uint own) => Unit(Mix(own ^ 0xB1u));

		/// <summary>
		/// A colony hanging from its patch of roof, head down, wings wrapped, packed close; those of it out feeding or circling the
		/// cave gone from the roost, each going and coming back by its rank. Now and then one stirs: a shuffle of its wings.
		/// </summary>
		private static void Roost(in AmbientGroup g, AmbientCreatureKind kind, in SceneCaveLifeSpot spot, ref AmbientLifeMotion.Frame f, List<AmbientInstance> into)
		{
			double t = f.Seconds;
			CaveBatHours hours = BatHours(f.Conditions);
			float away = hours.Out + hours.Stirring;
			Plane(spot, out Vector3 n, out Vector3 t1, out Vector3 t2);
			// A colony packs a hand's breadth apart: its cluster no wider than that needs, nor than the patch.
			float cluster = Mathf.Min(Mathf.Max(0.12f, spot.Extent.x * 0.85f), 0.09f * Mathf.Sqrt(g.Count) + 0.1f);
			for (int m = 0; m < g.Count; m++)
			{
				uint own = AmbientLifeMotion.MemberSeed(g.Seed, m);
				float visible = f.Visible * Mathf.Clamp01((ColonyRank(own) - away) / 0.05f);
				if (visible <= 0.002f)
				{
					continue;
				}
				float size = g.Length * Mathf.Lerp(0.88f, 1.12f, Unit(Mix(own ^ 0xB3u)));
				Vector2 d = Disc(Mix(own ^ 0xB2u), cluster);
				Vector3 rock = spot.Position + t1 * d.x + t2 * d.y;
				float yaw = Unit(Mix(own ^ 0xB4u)) * 6.2831853f;
				Vector3 back = t1 * Mathf.Cos(yaw) + t2 * Mathf.Sin(yaw);
				// Hung by its feet: its middle half a body under the rock, its nose to the floor.
				Vector3 at = rock + n * (0.5f * size);
				float stir = SeaLifePlacement.Cycle(t, 18.0 + 30.0 * Unit(Mix(own ^ 0xB5u)), Unit(Mix(own ^ 0xB6u)));
				float shuffle = stir < 0.06f ? Mathf.Sin(stir / 0.06f * Mathf.PI) : 0f;
				float phase = SeaLifePlacement.Cycle(t, 0.4, Unit(Mix(own ^ 0xB7u)));
				float head = AmbientLifeMotion.HeadTurn(own, t) * 0.4f;
				AmbientLifeMotion.Emit(into, kind, own, at, Quaternion.LookRotation(n, back), size, new Vector4(phase, 0.35f * shuffle, 1f - 0.6f * shuffle, head), visible);
			}
		}

		/// <summary>
		/// Bats (or moths) wheeling in a box of clear air: three incommensurate turns about its middle that never leave it (their
		/// weights sum under one). A bat flies while its colony stirs (thickest at dusk; near the mouth the ones about to go out
		/// too); a moth by its kind's hours in the twilight.
		/// </summary>
		private static void Wheel(in AmbientGroup g, AmbientCreatureKind kind, in SceneCaveLifeSpot spot, ref AmbientLifeMotion.Frame f, List<AmbientInstance> into)
		{
			double t = f.Seconds;
			bool bat = kind.Shape == AmbientShape.Bat;
			float share = 1f;
			if (bat)
			{
				CaveBatHours hours = BatHours(f.Conditions);
				share = Mathf.Clamp01(hours.Stirring * 3f + (spot.Dark < 0.6f ? 1.5f * hours.Leaving + hours.Returning : 0f));
			}
			Axes(spot.Yaw, out Vector3 along, out Vector3 across);
			Vector3 half = spot.Extent, middle = spot.Position;
			AmbientLifeMotion.Rhythm(kind.Shape, out float period, out float duty);
			for (int m = 0; m < g.Count; m++)
			{
				uint own = AmbientLifeMotion.MemberSeed(g.Seed, m);
				float visible = f.Visible * Mathf.Clamp01((share - ColonyRank(own)) / 0.05f);
				if (visible <= 0.002f)
				{
					continue;
				}
				float scale = Mathf.Lerp(0.6f, 1f, Unit(Mix(own ^ 0x74u)));
				double w = Math.Max(0.05, kind.Speed) / (Mathf.Max(1f, Mathf.Max(half.x, half.z)) * scale * 0.9f);
				float p1 = Unit(Mix(own ^ 0x75u)) * 6.28f, p2 = Unit(Mix(own ^ 0x76u)) * 6.28f, p3 = Unit(Mix(own ^ 0x77u)) * 6.28f;
				Vector3 Position(double at)
				{
					float x = half.x * scale * (0.6f * Mathf.Sin(AmbientLifeMotion.Angle(at, w, p1)) + 0.25f * Mathf.Sin(AmbientLifeMotion.Angle(at, w * 2.7, p2))
						+ 0.12f * Mathf.Sin(AmbientLifeMotion.Angle(at, w * 6.1, p3)));
					float z = half.z * scale * (0.6f * Mathf.Cos(AmbientLifeMotion.Angle(at, w * 0.93, p2)) + 0.25f * Mathf.Cos(AmbientLifeMotion.Angle(at, w * 2.3, p3))
						+ 0.12f * Mathf.Cos(AmbientLifeMotion.Angle(at, w * 5.3, p1)));
					float y = half.y * (0.6f * Mathf.Sin(AmbientLifeMotion.Angle(at, w * 1.3, p3)) + 0.3f * Mathf.Sin(AmbientLifeMotion.Angle(at, w * 4.7, p1)));
					return middle + along * x + Vector3.up * y + across * z;
				}
				Vector3 now = Position(t), soon = Position(t + 0.05), after = Position(t + 0.1);
				Vector3 v = (soon - now) / 0.05f, v2 = (after - soon) / 0.05f;
				float bank = Mathf.Clamp(Vector3.SignedAngle(new Vector3(v.x, 0f, v.z), new Vector3(v2.x, 0f, v2.z), Vector3.up) * 2f, -60f, 60f);
				float flap = AmbientLifeMotion.FlapStrength(t, period, duty, Unit(Mix(own ^ 0x78u)));
				float phase = SeaLifePlacement.Cycle(t, 1.0 / Math.Max(0.1, kind.StrokeHz * Mathf.Lerp(0.85f, 1.15f, Unit(Mix(own ^ 0x79u)))), Unit(Mix(own ^ 0x7Au)));
				AmbientLifeMotion.Emit(into, kind, own, now, AmbientLifeMotion.Flight(v, bank), g.Length * Mathf.Lerp(0.9f, 1.1f, Unit(Mix(own ^ 0x7Bu))),
					new Vector4(phase, flap, 0f, 0f), visible);
			}
		}

		/// <summary>
		/// A mouth's stream: at dusk the colony pours out of it, a bat after another along the run in and out into the open air,
		/// climbing away; before dawn back in the other way. Each member is a place in the stream going round on the shared
		/// clock, on in a lap by its own hash while the stream is thick enough: the stream's thickness, not any one bat's memory.
		/// </summary>
		private static void Stream(in AmbientGroup g, AmbientCreatureKind kind, in SceneCaveLifeSpot spot, ref AmbientLifeMotion.Frame f, List<AmbientInstance> into)
		{
			CaveBatHours hours = BatHours(f.Conditions);
			bool outward = hours.Leaving >= hours.Returning;
			float rate = Mathf.Max(hours.Leaving, hours.Returning);
			if (rate <= 0.01f || f.Visible <= 0.002f)
			{
				return;
			}
			double t = f.Seconds;
			Vector3 n = spot.Normal.sqrMagnitude > 1e-6f ? new Vector3(spot.Normal.x, 0f, spot.Normal.z).normalized : Vector3.forward;
			var side = new Vector3(n.z, 0f, -n.x);
			float run = spot.Extent.z, bore = Mathf.Max(0.5f, spot.Extent.x);
			float total = run + StreamOutside;
			Vector3 mouth = spot.Position, inner = mouth - n * run;
			int count = g.Count;
			AmbientLifeMotion.Rhythm(kind.Shape, out float period, out float duty);
			for (int k = 0; k < g.Count; k++)
			{
				uint own = AmbientLifeMotion.MemberSeed(g.Seed, k);
				float speed = kind.Speed * Mathf.Lerp(0.85f, 1.2f, Unit(Mix(own ^ 0xC1u)));
				double lap = total / Math.Max(0.5, speed);
				double e = t / lap + (k + 0.5 * Unit(Mix(own ^ 0xC2u))) / g.Count;
				long cycle = (long)Math.Floor(e);
				if (Unit(AmbientLifeMotion.EpisodeSeed(own, cycle)) >= rate * 1.3f)
				{
					continue;
				}
				float p1 = Unit(Mix(own ^ 0xC3u)) * 6.28f, p2 = Unit(Mix(own ^ 0xC4u)) * 6.28f;
				Vector3 Position(double at)
				{
					float s = (float)((at / lap + (k + 0.5 * Unit(Mix(own ^ 0xC2u))) / count - cycle) * total);
					if (!outward)
					{
						s = total - s;
					}
					float wobble = 1f + Mathf.Max(0f, s - run) * 0.12f;
					Vector3 lateral = side * (0.35f * bore * wobble * Mathf.Sin(AmbientLifeMotion.Angle(at, 1.3, p1)))
						+ Vector3.up * (0.25f * bore * Mathf.Cos(AmbientLifeMotion.Angle(at, 1.1, p2)));
					if (s <= run)
					{
						return inner + n * s + lateral;
					}
					float d = s - run;
					// Out of the mouth and up away into the dark, spreading as it goes.
					return mouth + n * d + Vector3.up * (0.35f * d + 0.01f * d * d) + lateral;
				}
				Vector3 now = Position(t), soon = Position(t + 0.05);
				float u = (float)(e - cycle);
				float along = outward ? u : 1f - u;
				// Fades in from the dark at the run's inner end, out into the night past the mouth.
				float visible = f.Visible * Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(along / 0.08f)) * (1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((along - 0.75f) / 0.25f)));
				float phase = SeaLifePlacement.Cycle(t, 1.0 / Math.Max(0.1, kind.StrokeHz * Mathf.Lerp(0.85f, 1.15f, Unit(Mix(own ^ 0xC5u)))), Unit(Mix(own ^ 0xC6u)));
				float flap = AmbientLifeMotion.FlapStrength(t, period, duty, Unit(Mix(own ^ 0xC7u)));
				AmbientLifeMotion.Emit(into, kind, own, now, AmbientLifeMotion.Flight((soon - now) / 0.05f, 0f), g.Length * Mathf.Lerp(0.9f, 1.1f, Unit(Mix(own ^ 0xC8u))),
					new Vector4(phase, flap, 0f, 0f), visible);
			}
		}

		// ── Glow ──────────────────────────────────────────────────────

		/// <summary>A cloud of fungus gnats hanging in its box of still air under the roof, drifting a little about it.</summary>
		private static void Glow(in AmbientGroup g, AmbientCreatureKind kind, in SceneCaveLifeSpot spot, ref AmbientLifeMotion.Frame f, List<AmbientInstance> into)
		{
			if (f.Visible <= 0.002f)
			{
				return;
			}
			double t = f.Seconds;
			uint own = AmbientLifeMotion.MemberSeed(g.Seed, 0);
			Axes(spot.Yaw, out Vector3 along, out Vector3 across);
			float radius = Mathf.Min(kind.Spread * Mathf.Lerp(0.8f, 1.2f, Unit(Mix(own ^ 0x95u))), Mathf.Max(0.3f, Mathf.Min(spot.Extent.x, spot.Extent.z)));
			float p1 = Unit(Mix(own ^ 0x91u)) * 6.28f, p2 = Unit(Mix(own ^ 0x92u)) * 6.28f;
			// Room to drift: what the box leaves round the cloud.
			float driftX = Mathf.Max(0f, spot.Extent.x - radius) * 0.5f, driftZ = Mathf.Max(0f, spot.Extent.z - radius) * 0.5f;
			Vector3 at = spot.Position + along * (driftX * Mathf.Sin(AmbientLifeMotion.Angle(t, 0.03, p1))) + across * (driftZ * Mathf.Cos(AmbientLifeMotion.Angle(t, 0.025, p2)))
				+ Vector3.up * (0.1f * Mathf.Sin(AmbientLifeMotion.Angle(t, 0.2, p2)));
			float yaw = Unit(Mix(own ^ 0x94u)) * 360f + AmbientLifeMotion.Angle(t, 0.03, p1) * Mathf.Rad2Deg;
			float share = Mathf.Clamp01((float)g.Count / Mathf.Max(1, kind.GroupSize.y)) * Mathf.Clamp01(f.Visible);
			double clock = t - Math.Floor(t / AmbientLifeMotion.SwarmClockWrap) * AmbientLifeMotion.SwarmClockWrap;
			AmbientLifeMotion.Emit(into, kind, own, at, Quaternion.Euler(0f, yaw, 0f), radius, new Vector4((float)clock, share, 0f, Unit(Mix(own ^ 0x96u))), 1f);
		}
	}
}
