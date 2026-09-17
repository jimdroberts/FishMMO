using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FishMMO.Shared.Weather
{
	/// <summary>What a <see cref="WeatherVolume"/> does to the weather inside it.</summary>
	public enum WeatherVolumeKind : byte
	{
		/// <summary>Weather still shows outside, but nothing inside is exposed to it.</summary>
		Shelter = 0,
		/// <summary>Inside, the weather is always a preset (a volcano crater that is always ashfall).</summary>
		Override = 1,
		/// <summary>Inside, some kinds never happen (a city ward that keeps the rain out).</summary>
		Suppress = 2,
	}

	/// <summary>
	/// A region of a scene that changes the weather inside it. Tested by position, not by trigger
	/// callbacks, so NPCs, the camera and predicted characters all get the same answer every time
	/// a tick is replayed.
	/// </summary>
	/// <remarks>
	/// Can sit on a <see cref="Region"/>'s GameObject and reuse its collider; the Region keeps its
	/// own enter/exit actions for cosmetic effects.
	/// </remarks>
	[DisallowMultipleComponent]
	public class WeatherVolume : MonoBehaviour
	{
		public WeatherVolumeKind Kind = WeatherVolumeKind.Shelter;
		[Tooltip("The shape. Defaults to this GameObject's collider.")]
		public Collider Shape;
		[Tooltip("Shelter: how much it protects. 0.5 for a tree canopy, 1 for a building.")]
		[Range(0f, 1f)] public float ShelterStrength = 1f;
		[Tooltip("Override: the weather inside.")]
		public WeatherPreset OverridePreset;
		[Range(0f, 1f)] public float OverrideIntensity = 1f;
		[Tooltip("Suppress: the kinds that never happen inside.")]
		public WeatherKindMask Suppressed = WeatherKindMask.Precipitation;
		[Tooltip("Higher wins when overrides overlap.")]
		public int Priority;

		private int registeredScene;

		private void OnEnable()
		{
			if (Shape == null)
			{
				Shape = GetComponent<Collider>();
			}
			registeredScene = gameObject.scene.handle;
			WeatherVolumeRegistry.Add(registeredScene, this);
		}

		private void OnDisable()
		{
			WeatherVolumeRegistry.Remove(registeredScene, this);
		}

		public bool Contains(Vector3 worldPoint) => Shape != null && RegionGeometry.ContainsPoint(Shape, worldPoint);
	}

	/// <summary>Every enabled <see cref="WeatherVolume"/>, by scene.</summary>
	public static class WeatherVolumeRegistry
	{
		private static readonly Dictionary<int, List<WeatherVolume>> byScene = new Dictionary<int, List<WeatherVolume>>();
		private static readonly List<WeatherVolume> empty = new List<WeatherVolume>();

		public static void Add(int sceneHandle, WeatherVolume volume)
		{
			if (!byScene.TryGetValue(sceneHandle, out List<WeatherVolume> list))
			{
				byScene[sceneHandle] = list = new List<WeatherVolume>();
			}
			if (!list.Contains(volume))
			{
				list.Add(volume);
			}
		}

		public static void Remove(int sceneHandle, WeatherVolume volume)
		{
			if (byScene.TryGetValue(sceneHandle, out List<WeatherVolume> list))
			{
				list.Remove(volume);
				if (list.Count == 0)
				{
					byScene.Remove(sceneHandle);
				}
			}
		}

		public static IReadOnlyList<WeatherVolume> InScene(Scene scene) => byScene.TryGetValue(scene.handle, out List<WeatherVolume> list) ? list : empty;

		/// <summary>
		/// Applies every volume containing a point: the highest-priority override replaces the
		/// frame, suppressions remove kinds, and the strongest shelter sets the exposure.
		/// </summary>
		public static void Apply(Scene scene, Vector3 position, ref WeatherFrame frame, ref float shelter)
		{
			IReadOnlyList<WeatherVolume> volumes = InScene(scene);
			WeatherVolume topOverride = null;
			WeatherKindMask suppressed = WeatherKindMask.None;
			for (int i = 0; i < volumes.Count; i++)
			{
				WeatherVolume v = volumes[i];
				if (v == null || !v.Contains(position))
				{
					continue;
				}
				switch (v.Kind)
				{
					case WeatherVolumeKind.Shelter:
						shelter = Mathf.Max(shelter, v.ShelterStrength);
						break;
					case WeatherVolumeKind.Override:
						if (v.OverridePreset != null && (topOverride == null || v.Priority > topOverride.Priority))
						{
							topOverride = v;
						}
						break;
					case WeatherVolumeKind.Suppress:
						suppressed |= v.Suppressed;
						break;
				}
			}
			if (topOverride != null)
			{
				frame = topOverride.OverridePreset.Evaluate(topOverride.OverrideIntensity);
			}
			if (suppressed != WeatherKindMask.None)
			{
				frame.Suppress(suppressed);
			}
		}

		public static void Clear() => byScene.Clear();
	}
}
