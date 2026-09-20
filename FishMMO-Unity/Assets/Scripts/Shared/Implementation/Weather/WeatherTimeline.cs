using System;
using System.Collections.Generic;
using UnityEngine;

namespace FishMMO.Shared.Weather
{
	/// <summary>
	/// A scene's weather as data: what changes and when, never the per-frame state. Owned by the
	/// server, mirrored on each client, and evaluated the same way on both.
	/// </summary>
	/// <remarks>
	/// Every change the server makes takes effect <see cref="LeadTicks"/> after it is sent, so a
	/// client has it before it matters and predicted weather effects match the server's.
	/// </remarks>
	public sealed class WeatherTimeline
	{
		/// <summary>How far ahead of "now" an edit is scheduled: 1.5 s at 30 Hz.</summary>
		public const float LeadSeconds = 1.5f;

		public string SceneName = string.Empty;
		public uint Revision;
		public uint Seed;
		public WeatherSceneMode SceneMode = WeatherSceneMode.Own;
		public int FixedPresetID;
		public float FixedIntensity = 1f;
		public double TickDelta = 1.0 / 30.0;
		public readonly List<WeatherLayerEntry> Layers = new List<WeatherLayerEntry>();
		public readonly List<StormCell> Cells = new List<StormCell>();
		public WeatherClimateEntry Climate;
		public WeatherCover Cover;
		public uint CoverTick;

		/// <summary>
		/// Where and when this scene is, for the weather driver.
		/// </summary>
		/// <remarks>
		/// The driver is a pure function of the world clock and the place, so it needs both — and
		/// both have to be the same number on the server and on every client or they compute
		/// different weather. They live here because the timeline is the one piece of weather state
		/// both sides already hold and keep in step.
		/// </remarks>
		/// <summary>
		/// Whether the drifting weather field runs in this scene.
		/// </summary>
		/// <remarks>
		/// On for a living world. Off where the weather has to be exactly what was asked for and
		/// nothing else: a scene pinned to a fixed preset, and the probe stages that check a preset
		/// looks right — a driver quietly adding its own cloud to those would make them test the
		/// weather of whatever moment they happened to run at.
		/// </remarks>
		public bool Driver = true;

		public double WorldSecondsAtTick;
		/// <summary>The tick <see cref="WorldSecondsAtTick"/> was taken at.</summary>
		public uint WorldSecondsTick;
		/// <summary>The scene's latitude: it decides which way the weather comes from, and how hard.</summary>
		public float LatitudeDegrees;
		/// <summary>The scene's longitude, for working out its local hour from the world clock.</summary>
		public float LongitudeDegrees;

		/// <summary>World time at a tick, carried forward from the last anchor the server sent.</summary>
		public double WorldSecondsAt(uint tick) =>
			WorldSecondsAtTick + (double)((long)tick - WorldSecondsTick) * TickDelta;

		public uint LeadTicks => (uint)Mathf.CeilToInt((float)(LeadSeconds / Math.Max(1e-6, TickDelta)));

		public uint SecondsToTicks(float seconds) => (uint)Math.Max(0, Math.Round(seconds / Math.Max(1e-6, TickDelta)));

		// ── Evaluation ────────────────────────────────────────────────

		/// <summary>Adds the scene-wide layers (and a fixed preset) to an accumulator.</summary>
		/// <summary>
		/// How long the weather going out should hold on while the weather coming in rises, as a
		/// share of the transition.
		/// </summary>
		/// <remarks>
		/// Cloud cover and fog blend by taking the greater of the layers over them. Fade one layer
		/// out while another fades in and the two ramps cross halfway down, so a sky going from
		/// overcast to storm passes through half-clear on the way — weather, then nothing, then
		/// weather. Holding the outgoing layer up while the incoming one rises hands the sky over
		/// instead: the greater of the two is always most of one of them.
		/// </remarks>
		public const float HandoverHold = 0.55f;

		/// <summary>The window the weather going out fades over, given the whole transition.</summary>
		public static void OutgoingWindow(uint now, uint end, out uint start, out uint finish)
		{
			uint span = end > now ? end - now : 0u;
			start = now + (uint)(span * HandoverHold);
			finish = end;
		}

		/// <summary>The window the weather coming in rises over. It starts at once and finishes early.</summary>
		public static void IncomingWindow(uint now, uint end, out uint start, out uint finish)
		{
			uint span = end > now ? end - now : 0u;
			start = now;
			finish = now + (uint)(span * (1f - HandoverHold * 0.5f));
		}

		/// <summary>
		/// Adds the scene's own layers to an accumulator.
		/// </summary>
		/// <param name="temperature">
		/// The local temperature, or NaN not to care. A layer whose template says it only applies
		/// within a range of temperatures is left out when the scene is outside it: snow does not
		/// fall on the desert, whatever a script asks for. Callers that only want to know what the
		/// scene was told — a map, the lightning schedule — pass NaN.
		/// </param>
		public void AccumulateSceneLayers(uint tick, ref WeatherAccumulator accumulator, float temperature = float.NaN)
		{
			if (SceneMode == WeatherSceneMode.Fixed)
			{
				WeatherPreset preset = FixedPresetID != 0 ? WeatherPreset.Get<WeatherPreset>(FixedPresetID) : null;
				if (preset != null)
				{
					accumulator.Add(preset.Evaluate(FixedIntensity), 1f);
				}
			}
			for (int i = 0; i < Layers.Count; i++)
			{
				WeatherLayerEntry entry = Layers[i];
				float intensity = entry.IntensityAt(tick);
				if (intensity <= 0f)
				{
					continue;
				}
				WeatherLayerTemplate template = WeatherLayerTemplate.Get<WeatherLayerTemplate>(entry.TemplateID);
				if (template != null && (float.IsNaN(temperature) || template.AllowsTemperature(temperature)))
				{
					accumulator.Add(template.Evaluate(intensity), 1f);
				}
			}
		}

		/// <summary>
		/// The strongest scene layer running at a tick, 0..1: how far the scene's own weather has
		/// been overridden. A preset or a layer is a request for *this* weather, not for this weather
		/// on top of whatever the field was doing — so the driver's background is weighted by one
		/// minus this, and returns as the layer fades out.
		/// </summary>
		/// <remarks>
		/// Every channel blends by taking the greater, so with the driver left at full weight a
		/// preset could only ever add: Clear could not clear a cloudy field, an overcast's cover was
		/// whichever of the two was larger, and the sky's formations went on being taken out at the
		/// camera as if the field owned a cover the preset did. Layers the temperature rules out are
		/// not counted, for the same reason they are not applied.
		/// </remarks>
		public float OverrideAt(uint tick, float temperature = float.NaN)
		{
			float strongest = SceneMode == WeatherSceneMode.Fixed && FixedPresetID != 0 ? 1f : 0f;
			for (int i = 0; i < Layers.Count; i++)
			{
				WeatherLayerEntry entry = Layers[i];
				float intensity = entry.IntensityAt(tick);
				if (intensity <= strongest)
				{
					continue;
				}
				WeatherLayerTemplate template = WeatherLayerTemplate.Get<WeatherLayerTemplate>(entry.TemplateID);
				if (template != null && (float.IsNaN(temperature) || template.AllowsTemperature(temperature)))
				{
					strongest = intensity;
				}
			}
			return Mathf.Clamp01(strongest);
		}

		/// <summary>The scene-wide climate shift at a tick, including temperature/humidity channels from scene layers.</summary>
		public void ClimateAt(uint tick, out float temperature, out float humidity)
		{
			Climate.At(tick, out temperature, out humidity);
			var accumulator = new WeatherAccumulator();
			AccumulateSceneLayers(tick, ref accumulator);
			if (accumulator.HasAny)
			{
				WeatherFrame frame = accumulator.Resolve();
				temperature += frame[WeatherChannel.TemperatureOffset];
				humidity += frame[WeatherChannel.HumidityOffset];
			}
		}

		/// <summary>Forgets finished removals and dead cells. Both sides run it, so they stay equal without messages.</summary>
		public void Prune(uint tick)
		{
			Layers.RemoveAll(l => l.IsFinished(tick));
			Cells.RemoveAll(c => c.IsDead(tick));
		}

		public bool TryGetLayer(ushort handle, out int index)
		{
			for (index = 0; index < Layers.Count; index++)
			{
				if (Layers[index].Handle == handle)
				{
					return true;
				}
			}
			index = -1;
			return false;
		}

		public bool TryGetCell(ushort id, out int index)
		{
			for (index = 0; index < Cells.Count; index++)
			{
				if (Cells[index].ID == id)
				{
					return true;
				}
			}
			index = -1;
			return false;
		}

		public void UpsertLayer(WeatherLayerEntry entry)
		{
			if (TryGetLayer(entry.Handle, out int i)) Layers[i] = entry; else Layers.Add(entry);
		}

		public void UpsertCell(StormCell cell)
		{
			if (TryGetCell(cell.ID, out int i)) Cells[i] = cell; else Cells.Add(cell);
		}

		// ── Wire ──────────────────────────────────────────────────────

		public WeatherTimelineBroadcast ToBroadcast()
		{
			return new WeatherTimelineBroadcast
			{
				SceneName = SceneName,
				Revision = Revision,
				Seed = Seed,
				SceneMode = (byte)SceneMode,
				FixedPresetID = FixedPresetID,
				FixedIntensity = FixedIntensity,
				Layers = new List<WeatherLayerEntry>(Layers),
				Cells = new List<StormCell>(Cells),
				Climate = Climate,
				Cover = Cover,
				CoverTick = CoverTick,
				// Where and when, so the client's driver computes the server's weather rather than
				// the weather of world-time zero on the equator.
				Driver = Driver,
				WorldSecondsAtTick = WorldSecondsAtTick,
				WorldSecondsTick = WorldSecondsTick,
				LatitudeDegrees = LatitudeDegrees,
				LongitudeDegrees = LongitudeDegrees,
			};
		}

		/// <summary>Replaces this timeline with a full one from the server.</summary>
		public void Apply(in WeatherTimelineBroadcast msg)
		{
			SceneName = msg.SceneName ?? string.Empty;
			Revision = msg.Revision;
			Seed = msg.Seed;
			SceneMode = (WeatherSceneMode)msg.SceneMode;
			FixedPresetID = msg.FixedPresetID;
			FixedIntensity = msg.FixedIntensity;
			Layers.Clear();
			if (msg.Layers != null) Layers.AddRange(msg.Layers);
			Cells.Clear();
			if (msg.Cells != null) Cells.AddRange(msg.Cells);
			Climate = msg.Climate;
			Cover = msg.Cover;
			CoverTick = msg.CoverTick;
			Driver = msg.Driver;
			WorldSecondsAtTick = msg.WorldSecondsAtTick;
			WorldSecondsTick = msg.WorldSecondsTick;
			LatitudeDegrees = msg.LatitudeDegrees;
			LongitudeDegrees = msg.LongitudeDegrees;
		}

		/// <summary>
		/// Applies a delta. An old or duplicate delta is ignored (true). A delta that skips a revision
		/// changes nothing and returns false: the caller must then ask for the whole timeline.
		/// </summary>
		public bool TryApply(in WeatherDeltaBroadcast msg)
		{
			if (msg.Revision != Revision + 1)
			{
				return msg.Revision <= Revision;   // an old or duplicate delta is harmless; a gap is not
			}
			Revision = msg.Revision;
			if (msg.RemovedLayers != null)
			{
				foreach (ushort handle in msg.RemovedLayers)
				{
					if (TryGetLayer(handle, out int i)) Layers.RemoveAt(i);
				}
			}
			if (msg.Layers != null)
			{
				foreach (WeatherLayerEntry entry in msg.Layers) UpsertLayer(entry);
			}
			if (msg.RemovedCells != null)
			{
				foreach (ushort id in msg.RemovedCells)
				{
					if (TryGetCell(id, out int i)) Cells.RemoveAt(i);
				}
			}
			if (msg.Cells != null)
			{
				foreach (StormCell cell in msg.Cells) UpsertCell(cell);
			}
			if (msg.HasClimate)
			{
				Climate = msg.Climate;
			}
			if (msg.HasCover)
			{
				Cover = msg.Cover;
				CoverTick = msg.CoverTick;
			}
			return true;
		}

		/// <summary>True when a delta's revision is past the next one, meaning something was missed.</summary>
		public bool IsGap(in WeatherDeltaBroadcast msg) => msg.Revision > Revision + 1;
	}
}
