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

		public uint LeadTicks => (uint)Mathf.CeilToInt((float)(LeadSeconds / Math.Max(1e-6, TickDelta)));

		public uint SecondsToTicks(float seconds) => (uint)Math.Max(0, Math.Round(seconds / Math.Max(1e-6, TickDelta)));

		// ── Evaluation ────────────────────────────────────────────────

		/// <summary>Adds the scene-wide layers (and a fixed preset) to an accumulator.</summary>
		public void AccumulateSceneLayers(uint tick, ref WeatherAccumulator accumulator)
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
				if (template != null)
				{
					accumulator.Add(template.Evaluate(intensity), 1f);
				}
			}
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
