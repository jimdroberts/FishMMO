using System;
using UnityEngine;
using UnityEngine.SceneManagement;
using FishMMO.Shared.Core;
using FishMMO.Shared.Weather;

namespace FishMMO.Shared
{
	/// <summary>
	/// Replaces the scene's weather with a preset, over a transition. The scripted storm.
	/// </summary>
	/// <remarks>
	/// Whole-scene and lasting: this is for a scripted beat — the ritual that brings the storm, the
	/// boss phase that darkens the sky. For weather in one place, use
	/// <see cref="SpawnStormCellAction"/>; for something to layer on top of what is already
	/// happening, <see cref="AddWeatherLayerAction"/>.
	/// </remarks>
	[Serializable]
	public class ApplyWeatherPresetAction : BaseAction
	{
		[Tooltip("The weather to put over the scene. Without one this action does nothing.")]
		public WeatherPreset Preset;

		[Tooltip("How strongly. 1 is the preset as authored.")]
		[Range(0f, 1f)] public float Intensity = 1f;

		[Tooltip("Seconds to move into it. 0 snaps, which is rarely what anybody wants to look at.")]
		[Min(0f)] public float TransitionSeconds = 20f;

		/// <inheritdoc />
		public override void Execute(ICharacter initiator, EventData eventData)
		{
			if (Preset == null)
			{
				return;
			}
			IWeatherService weather = WeatherActionGate.Resolve(initiator, eventData, out Scene scene);
			weather?.ApplyPreset(scene, Preset, Intensity, TransitionSeconds);
		}
	}
}
