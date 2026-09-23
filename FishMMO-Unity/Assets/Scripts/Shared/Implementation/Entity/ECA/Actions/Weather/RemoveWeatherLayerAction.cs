using System;
using UnityEngine;
using UnityEngine.SceneManagement;
using FishMMO.Shared.Core;
using FishMMO.Shared.Weather;

namespace FishMMO.Shared
{
	/// <summary>
	/// Takes away a layer an <see cref="AddWeatherLayerAction"/> put up, by the name it was given —
	/// or clears every scene layer at once.
	/// </summary>
	[Serializable]
	public class RemoveWeatherLayerAction : BaseAction
	{
		[Tooltip("The name an AddWeatherLayerAction gave the layer. Ignored when Clear every layer is on.")]
		public string Handle;

		[Tooltip("Fade every scene layer out instead of just the named one. Storm cells are left alone either way.")]
		public bool ClearEveryLayer;

		[Tooltip("Seconds to fade out.")]
		[Min(0f)] public float TransitionSeconds = 10f;

		/// <inheritdoc />
		public override void Execute(ICharacter initiator, EventData eventData)
		{
			IWeatherService weather = WeatherActionGate.Resolve(initiator, eventData, out Scene scene);
			if (weather == null)
			{
				return;
			}

			if (ClearEveryLayer)
			{
				weather.ClearLayers(scene, TransitionSeconds);
				WeatherLayerHandles.Forget(initiator);
				return;
			}

			if (WeatherLayerHandles.TryTake(initiator, Handle, out ushort handle))
			{
				weather.RemoveLayer(scene, handle, TransitionSeconds);
			}
			/* Nothing under that name. Not an error worth logging: a trigger that removes a layer it
			 * never added is how "stop the storm" is written for a storm that may not be running. */
		}
	}
}
