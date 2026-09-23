using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using FishMMO.Shared.Weather;

namespace FishMMO.Shared
{
	/// <summary>
	/// A region of a scene that buffs whoever is standing in it, tested by position inside the
	/// prediction replicate so the owner gets the buff at the step they walk in.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Why not a trigger.</b> A <c>NetworkTrigger</c> would have been the obvious way to build
	/// this and is the wrong one (N13). Trigger callbacks are raised by the physics scene in
	/// <c>OnPostPhysicsSimulation</c> — outside the replicate, so they cannot be predicted; they are
	/// not rolled back, so a reconcile leaves them stale; and they re-fire Enter and Exit on every
	/// replayed tick, so one reconcile of thirty ticks would apply and remove the buff thirty times.
	/// A point-in-collider test has none of those problems: it is a pure function of a position the
	/// replicate already has, it gives the same answer every time that tick is replayed, and the
	/// server computes it from the same position the owner did.
	/// </para>
	/// <para>
	/// <b>Nothing of it rides the wire.</b> Whether a character is inside is derivable from its
	/// position, and the position already reconciles — so the volume adds nothing to the reconcile
	/// payload, and both peers reach the same answer from the same coordinates. The same property
	/// that makes exposure recipes free.
	/// </para>
	/// <para>
	/// Can sit on a <see cref="Region"/>'s GameObject and share its collider; the Region keeps its
	/// own enter and exit actions for anything cosmetic.
	/// </para>
	/// </remarks>
	[DisallowMultipleComponent]
	public class BuffVolume : MonoBehaviour
	{
		[Tooltip("The buff applied while a character is inside.")]
		public BaseBuffTemplate Buff;

		[Tooltip("The shape. Defaults to this GameObject's collider.")]
		public Collider Shape;

		[Header("Weather gate (optional)")]
		[Tooltip("Only buff while the weather here is doing one of the chosen things. None: always.")]
		public WeatherKindMask RequiredWeather = WeatherKindMask.None;

		[Tooltip("Only buff while a character standing here is this exposed to the sky. 0 lets it work under any roof.")]
		[Range(0f, 1f)] public float MinimumExposure;

		private int registeredScene;

		private void OnEnable() => Register();

		private void OnDisable() => Unregister();

		/// <summary>
		/// Puts this volume into its scene's registry, defaulting the shape to this GameObject's
		/// collider first.
		/// </summary>
		/// <remarks>
		/// Separate from <c>OnEnable</c> rather than inlined into it so that it can be driven
		/// directly. Edit mode does not run MonoBehaviour lifecycle callbacks on a component that is
		/// not <c>[ExecuteAlways]</c>, and this one deliberately is not — a volume has no business
		/// registering itself while somebody is merely editing the scene. That leaves the one-line
		/// callback as the only part a test cannot reach, instead of the whole body of it.
		/// </remarks>
		public void Register()
		{
			if (Shape == null)
			{
				Shape = GetComponent<Collider>();
			}
			registeredScene = gameObject.scene.handle;
			BuffVolumeRegistry.Add(registeredScene, this);
		}

		/// <summary>Takes this volume back out of the registry it last registered with.</summary>
		public void Unregister() => BuffVolumeRegistry.Remove(registeredScene, this);

		public bool Contains(Vector3 worldPoint) => Shape != null && RegionGeometry.ContainsPoint(Shape, worldPoint);

		/// <summary>
		/// Whether this volume's buff should be on a character at that point, weather gate included.
		/// </summary>
		/// <remarks>
		/// The gate is evaluated from the weather at the character's own position rather than the
		/// volume's centre, so a long valley whose far end is under the storm behaves as the two
		/// different places it is.
		/// </remarks>
		/// <param name="worldPoint">Where the character is.</param>
		/// <param name="weather">The weather there, already sampled by the caller.</param>
		public bool AppliesAt(Vector3 worldPoint, in WeatherSample weather)
		{
			if (Buff == null || !Contains(worldPoint))
			{
				return false;
			}
			if (weather.Exposure < MinimumExposure)
			{
				return false;
			}
			return RequiredWeather == WeatherKindMask.None || weather.Matches(RequiredWeather);
		}

		/// <summary>True when this volume needs the weather sampled to answer at all.</summary>
		public bool NeedsWeather => RequiredWeather != WeatherKindMask.None || MinimumExposure > 0f;
	}

	/// <summary>Every enabled <see cref="BuffVolume"/>, by scene.</summary>
	public static class BuffVolumeRegistry
	{
		private static readonly Dictionary<int, List<BuffVolume>> byScene = new Dictionary<int, List<BuffVolume>>();
		private static readonly List<BuffVolume> empty = new List<BuffVolume>();

		public static void Add(int sceneHandle, BuffVolume volume)
		{
			if (!byScene.TryGetValue(sceneHandle, out List<BuffVolume> list))
			{
				byScene[sceneHandle] = list = new List<BuffVolume>();
			}
			if (!list.Contains(volume))
			{
				list.Add(volume);
			}
		}

		public static void Remove(int sceneHandle, BuffVolume volume)
		{
			if (byScene.TryGetValue(sceneHandle, out List<BuffVolume> list))
			{
				list.Remove(volume);
				if (list.Count == 0)
				{
					byScene.Remove(sceneHandle);
				}
			}
		}

		public static IReadOnlyList<BuffVolume> InScene(Scene scene) => byScene.TryGetValue(scene.handle, out List<BuffVolume> list) ? list : empty;

		/// <summary>True when any volume in the scene needs the weather to decide.</summary>
		public static bool AnyNeedsWeather(Scene scene)
		{
			IReadOnlyList<BuffVolume> volumes = InScene(scene);
			for (int i = 0; i < volumes.Count; i++)
			{
				if (volumes[i] != null && volumes[i].NeedsWeather)
				{
					return true;
				}
			}
			return false;
		}

		/// <summary>
		/// Every volume in the scene whose buff should be on a character standing at that point,
		/// keyed by buff template ID.
		/// </summary>
		/// <remarks>
		/// <para>
		/// The whole verdict, in one pure pass over a position: no history, no enter/exit bookkeeping,
		/// nothing carried from the previous tick. That is what makes a replayed tick reach the same
		/// answer as the first run of it, and what a trigger could never have given (N13).
		/// </para>
		/// <para>
		/// Two volumes granting the same buff collapse to one entry, so overlapping regions do not
		/// apply it twice — and, because the answer is recomputed from scratch, walking out of one
		/// while still inside the other simply keeps it on.
		/// </para>
		/// </remarks>
		/// <param name="into">Cleared and refilled. Supplied by the caller so the per-tick pass does not allocate.</param>
		public static void Applicable(Scene scene, Vector3 position, in WeatherSample weather, Dictionary<int, BuffVolume> into)
		{
			into.Clear();
			IReadOnlyList<BuffVolume> volumes = InScene(scene);
			for (int i = 0; i < volumes.Count; i++)
			{
				BuffVolume volume = volumes[i];
				if (volume == null || !volume.AppliesAt(position, weather))
				{
					continue;
				}
				if (!into.ContainsKey(volume.Buff.ID))
				{
					into[volume.Buff.ID] = volume;
				}
			}
		}

		public static void Clear() => byScene.Clear();
	}
}
