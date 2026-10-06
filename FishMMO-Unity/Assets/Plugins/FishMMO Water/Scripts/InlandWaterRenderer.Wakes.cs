using System.Collections.Generic;
using UnityEngine;
using FishMMO.Shared;
using FishMMO.Shared.Core;
using FishMMO.Shared.Weather;

namespace FishMMO.Water
{
	/// <summary>
	/// Wakes on the rivers (Jim: rivers only): every character and drifting body in a river near the camera, its place,
	/// size and velocity handed to the inland water's shader, which draws its Kelvin wake, the rings round it and the white
	/// at its bow from the water moving past it (the current less its own velocity). Analytic, not simulated: nothing to
	/// step, nothing to store, and the same at any frame rate.
	/// </summary>
	public sealed partial class InlandWaterRenderer
	{
		/// <summary>The most wakes drawn at once: the nearest to the camera.</summary>
		public const int MaxWakes = 16;

		[Header("Wakes")]
		[Tooltip("How far from the camera a wake is drawn, metres.")]
		public float WakeRange = 120f;

		private static readonly int WakesId = Shader.PropertyToID("_FishWakes");
		private static readonly int WakeVelocitiesId = Shader.PropertyToID("_FishWakeVelocities");
		private static readonly int WakeCountId = Shader.PropertyToID("_FishWakeCount");
		private readonly Vector4[] wakes = new Vector4[MaxWakes];
		private readonly Vector4[] wakeVelocities = new Vector4[MaxWakes];
		private readonly Dictionary<int, (Vector3 position, float time)> lastSeen = new Dictionary<int, (Vector3, float)>();
		private readonly List<(float distance, Vector4 at, Vector4 velocity)> candidates = new List<(float, Vector4, Vector4)>();
		private readonly List<int> prune = new List<int>();

		/// <summary>Gathers the wakes near the camera and hands them to the shader: once a frame, on the client.</summary>
		private void UpdateWakes()
		{
#if !UNITY_SERVER
			Camera camera = Camera.main;
			candidates.Clear();
			if (camera != null)
			{
				Vector3 eye = camera.transform.position;
				foreach (ICharacter character in BaseCharacter.ClientCharacters.Values)
				{
					Transform t = character != null ? character.Transform : null;
					if (t != null)
					{
						Consider(t, 0.45f, eye);
					}
				}
				foreach (RiverDrift drift in RiverDrift.Active)
				{
					if (drift != null)
					{
						Consider(drift.transform, drift.Radius, eye);
					}
				}
			}
			// Bodies gone for a few seconds (despawned, out of range) are forgotten.
			if (Time.frameCount % 120 == 0)
			{
				prune.Clear();
				foreach (KeyValuePair<int, (Vector3 position, float time)> pair in lastSeen)
				{
					if (Time.time - pair.Value.time > 5f)
					{
						prune.Add(pair.Key);
					}
				}
				foreach (int id in prune)
				{
					lastSeen.Remove(id);
				}
			}
			candidates.Sort((a, b) => a.distance.CompareTo(b.distance));
			int count = Mathf.Min(MaxWakes, candidates.Count);
			for (int k = 0; k < MaxWakes; k++)
			{
				wakes[k] = k < count ? candidates[k].at : Vector4.zero;
				wakeVelocities[k] = k < count ? candidates[k].velocity : Vector4.zero;
			}
			Shader.SetGlobalVectorArray(WakesId, wakes);
			Shader.SetGlobalVectorArray(WakeVelocitiesId, wakeVelocities);
			Shader.SetGlobalFloat(WakeCountId, count);
#endif
		}

		/// <summary>Takes a body as a wake when it stands in a river near the camera: where it is, how big, how it moves.</summary>
		private void Consider(Transform body, float radius, Vector3 eye)
		{
			Vector3 p = body.position;
			float distance = Vector3.Distance(p, eye);
			int id = body.GetInstanceID();
			float now = Time.time;
			Vector3 velocity = Vector3.zero;
			if (lastSeen.TryGetValue(id, out (Vector3 position, float time) seen) && now > seen.time)
			{
				velocity = (p - seen.position) / (now - seen.time);
			}
			lastSeen[id] = (p, now);
			if (distance > WakeRange || !SurfaceWater.TryGetInlandSurfaceAt(p.x, p.z, out float level))
			{
				return;
			}
			// In the water: its body crossing the surface, from wading in to swimming.
			float above = p.y - level;
			if (above > 0.3f || above < -2.5f)
			{
				return;
			}
			if (velocity.sqrMagnitude > 400f)
			{
				velocity = Vector3.zero; // a teleport, not a swim
			}
			candidates.Add((distance, new Vector4(p.x, level, p.z, radius), new Vector4(velocity.x, velocity.z, 0f, 0f)));
		}
	}
}
