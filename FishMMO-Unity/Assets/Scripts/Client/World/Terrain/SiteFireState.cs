using System.Collections.Generic;
using UnityEngine;
using FishMMO.Shared;
using FishMMO.Shared.Weather;

namespace FishMMO.Client
{
	/// <summary>
	/// Whether an outdoor fire is burning: rain puts out what it falls on, soaked ground keeps a doused fire smouldering
	/// until it dries, heavy snowfall beats a fire down and fresh deep snow smothers it, a gale gutters a torch — and none
	/// of it reaches a fire under cover (<see cref="SceneShelterMap"/>: a roof, a rock shelter, a cave).
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>A function of the place and the shared clock.</b> The weather is read at the fire's own position at whole world
	/// minutes (<see cref="WeatherField.SampleAtSeconds"/>, the same on every peer) and eased between them; the ground is
	/// the cover map's, itself a pure function of world time. So every player sees the same fire out, and nothing is sent.
	/// A fire relights by itself once the rain stops and the ground dries, as if tended.
	/// </para>
	/// <para>
	/// Only <see cref="SceneEmitterKind.Fire"/> (campfires, braziers, cressets) and <see cref="SceneEmitterKind.Torch"/>:
	/// hearths, forges, lamps and room lights are indoors or enclosed.
	/// </para>
	/// </remarks>
	public static class SiteFireState
	{
		/// <summary>World seconds between the weather readings a fire is eased between.</summary>
		public const double StepSeconds = 60.0;

		/// <summary>How a fire is doing: <see cref="Burn"/> 1 ablaze … 0 out; <see cref="Doused"/> how much water has it (steam, red embers).</summary>
		public struct State
		{
			public float Burn;
			public float Doused;
			/// <summary>How far round it the fire melts the snow, metres; 0 when it is out.</summary>
			public float MeltRadius;
			public static State Lit => new State { Burn = 1f, MeltRadius = 2.4f };
		}

		private struct Reading
		{
			public long Step;
			public float Douse;
			public float Water;
		}

		private static readonly Dictionary<uint, (Reading a, Reading b)> readings = new Dictionary<uint, (Reading, Reading)>();
		private static readonly Dictionary<uint, float> exposures = new Dictionary<uint, float>();
		private static SceneShelterMap exposureMap;

		/// <summary>The state of a site's fire now.</summary>
		public static State Of(in SceneEmitter e)
		{
			if (e.Kind != SceneEmitterKind.Fire && e.Kind != SceneEmitterKind.Torch)
			{
				return State.Lit;
			}
			WeatherContext context = WeatherClient.LastContext;
			if (context.Timeline == null || context.Settings == null)
			{
				return State.Lit;
			}
			double seconds = context.Timeline.WorldSecondsAt((uint)System.Math.Max(0.0, context.Tick));
			long step = (long)System.Math.Floor(seconds / StepSeconds);
			if (!readings.TryGetValue(e.Seed, out (Reading a, Reading b) pair) || pair.a.Step != step)
			{
				Reading a = pair.b.Step == step ? pair.b : Read(e, context, step);
				pair = (a, Read(e, context, step + 1));
				if (readings.Count > 256)
				{
					readings.Clear();
				}
				readings[e.Seed] = pair;
			}
			float t = Mathf.Clamp01((float)(seconds / StepSeconds - step));
			float douse = Mathf.Lerp(pair.a.Douse, pair.b.Douse, t);
			float water = Mathf.Lerp(pair.a.Water, pair.b.Water, t);
			float burn = 1f - douse;
			return new State
			{
				Burn = burn,
				Doused = Mathf.Clamp01(water * douse),
				MeltRadius = burn > 0.2f ? Mathf.Lerp(1.2f, 2.8f, burn) : 0f,
			};
		}

		/// <summary>How much the weather of one step puts the fire out, 0..1, and how much of that is water.</summary>
		private static Reading Read(in SceneEmitter e, in WeatherContext context, long step)
		{
			float exposure = ExposureOf(e, context.Settings);
			var reading = new Reading { Step = step };
			if (exposure <= 0f)
			{
				return reading;
			}
			WeatherSample sample = WeatherField.SampleAtSeconds(context.Timeline, context.Settings, context.Scene, e.Position, step * StepSeconds);
			WeatherFrame frame = sample.Frame;
			float falling = frame[WeatherChannel.Precipitation];
			float rain = falling * (frame[WeatherChannel.RainWeight] + frame[WeatherChannel.HailWeight]) * exposure;
			float snow = falling * frame[WeatherChannel.SnowWeight] * exposure;
			float wind = Mathf.Max(frame[WeatherChannel.WindSpeed], frame[WeatherChannel.WindGust]) * exposure;
			// Each fire on its own threshold: a row of braziers does not go out as one.
			float own = 0.85f + 0.3f * ((e.Seed >> 4) & 0xFF) / 255f;

			float wet = 0f, depth = 0f;
			WeatherPresentation presentation = WeatherPresentation.Instance;
			if (presentation != null && presentation.CoverMap != null && presentation.CoverMap.TryWeatherGround(e.Position, out WeatherCover cover, out float fresh))
			{
				wet = cover.Wet * exposure;
				depth = fresh * exposure;
			}

			float water, smother = 0f;
			if (e.Kind == SceneEmitterKind.Torch)
			{
				// Pitch sheds a shower; a downpour or a gale puts it out, and it is relit the moment either passes.
				water = Mathf.Max(Smooth(0.45f * own, 0.85f * own, rain), Smooth(0.7f, 1f, snow) * 0.7f);
				smother = Smooth(0.7f * own, 1f, wind);
			}
			else
			{
				// Steady rain douses a campfire; soaked ground keeps it smouldering until it dries; heavy snow beats it down.
				water = Mathf.Max(Smooth(0.25f * own, 0.6f * own, rain), Mathf.Max(Smooth(0.6f, 0.9f, wet) * 0.8f, Smooth(0.5f, 1f, snow) * 0.6f));
				// Fresh deep snow buries a fire on the ground; a brazier's bowl stands clear of it.
				float raised = TerrainSet.Current.TryHeight(e.Position.x, e.Position.z, out float ground) ? Mathf.Max(0f, e.Position.y - ground) : 0f;
				smother = Mathf.Max(Smooth(0.2f, 0.4f, depth - raised), Smooth(0.8f * own, 1f, wind) * 0.5f);
			}
			reading.Douse = Mathf.Clamp01(Mathf.Max(water, smother));
			reading.Water = reading.Douse > 0f ? Mathf.Clamp01(water / reading.Douse) : 0f;
			return reading;
		}

		/// <summary>How open the fire is to the weather, 0..1: the scene's shelter map, else a room or a hillside over it.</summary>
		private static float ExposureOf(in SceneEmitter e, WorldSceneSettings settings)
		{
			SceneShelterMap map = settings.Shelter;
			if (!ReferenceEquals(map, exposureMap))
			{
				exposures.Clear();
				exposureMap = map;
			}
			if (exposures.TryGetValue(e.Seed, out float known))
			{
				return known;
			}
			float exposure = map != null ? map.Exposure(e.Position) : 1f;
			if (IndoorRooms.IndoorAt(e.Position) > 0.5f
				|| (TerrainSet.Current.TryHeight(e.Position.x, e.Position.z, out float ground) && e.Position.y < ground - 3f))
			{
				exposure = 0f;
			}
			exposures[e.Seed] = exposure;
			return exposure;
		}

		private static float Smooth(float from, float to, float x)
		{
			float t = Mathf.Clamp01((x - from) / Mathf.Max(1e-4f, to - from));
			return t * t * (3f - 2f * t);
		}
	}
}
