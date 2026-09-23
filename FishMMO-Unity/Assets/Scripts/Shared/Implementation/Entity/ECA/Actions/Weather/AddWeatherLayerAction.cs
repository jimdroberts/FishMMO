using System;
using UnityEngine;
using UnityEngine.SceneManagement;
using FishMMO.Shared.Core;
using FishMMO.Shared.Weather;

namespace FishMMO.Shared
{
	/// <summary>
	/// Adds a weather layer over whatever the scene is already doing, and remembers its handle so
	/// <see cref="RemoveWeatherLayerAction"/> can take that same layer away again.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Why the handle is named and not returned.</b> A layer is removed by the handle the server
	/// gave it, which is a number that did not exist when the trigger was authored — and an ECA
	/// action has nowhere to hand a value to the next one. So the author names the layer instead,
	/// and this action records the handle under that name on the character it ran for. The remove
	/// action asks for the same name and gets the right handle back.
	/// </para>
	/// <para>
	/// <b>Per character, not global.</b> Two players lighting the same brazier in two different
	/// scenes must each be able to put it out. Keying the record by the character who caused it
	/// means neither can cancel the other's.
	/// </para>
	/// </remarks>
	[Serializable]
	public class AddWeatherLayerAction : BaseAction
	{
		[Tooltip("The layer to add. Without one this action does nothing.")]
		public WeatherLayerTemplate Layer;

		[Tooltip("How strongly.")]
		[Range(0f, 1f)] public float Intensity = 1f;

		[Tooltip("Seconds to fade in.")]
		[Min(0f)] public float TransitionSeconds = 10f;

		[Tooltip("A name for this layer, so a RemoveWeatherLayerAction can take this one away later. Leave empty for a layer nothing will ever remove.")]
		public string Handle;

		/// <inheritdoc />
		public override void Execute(ICharacter initiator, EventData eventData)
		{
			if (Layer == null)
			{
				return;
			}
			IWeatherService weather = WeatherActionGate.Resolve(initiator, eventData, out Scene scene);
			if (weather == null)
			{
				return;
			}

			ushort handle = weather.AddLayer(scene, Layer, Intensity, TransitionSeconds);
			if (handle != 0 && !string.IsNullOrWhiteSpace(Handle))
			{
				WeatherLayerHandles.Remember(initiator, Handle, handle);
			}
		}
	}
}
