using System.Collections.Generic;
using FishNet.Object.Prediction;
using FishNet.Transporting;
using UnityEngine;
using UnityEngine.SceneManagement;
using FishMMO.Logging;
using FishMMO.Shared.Core;
using FishMMO.Shared.Weather;

namespace FishMMO.Shared
{
	/// <summary>
	/// Applies and removes <see cref="BuffVolume"/> buffs from inside the prediction replicate, so a
	/// character gets a region's buff on the step it walks in rather than a round trip later.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>It keeps no state that has to be sent.</b> Which volumes contain a character is a pure
	/// function of that character's position, and the position already reconciles — so this controller
	/// adds nothing to <see cref="CharacterReconcileData"/>. The set it holds is rebuilt from the
	/// position every tick rather than carried forward, which is what makes a replay of that tick
	/// produce the same answer as the first run of it.
	/// </para>
	/// <para>
	/// <b>Only volumes it put on come off.</b> The controller remembers which buffs it applied and
	/// removes only those, so a region buff never strips a buff of the same template that an ability
	/// or an item granted. The buffs themselves live in <see cref="BuffController"/> and reconcile
	/// the way every other buff does.
	/// </para>
	/// <para>
	/// See <see cref="BuffVolume"/> for why this is a position test and not a trigger (N13).
	/// </para>
	/// </remarks>
	[DisallowMultipleComponent]
	public class BuffVolumeController : CharacterBehaviour, IPredictableController
	{
		/// <summary>
		/// After movement (KCCPlayer, 80) so the position tested is this tick's, after weather
		/// exposure (83) so the two agree about the weather, and before buffs (BuffController, 85) so
		/// anything applied here is ticked in the same step.
		/// </summary>
		public int Order => 84;

		[Tooltip("Log every volume entered and left. Noisy; for working on region buffs only.")]
		public bool VerboseLogging;

		/// <summary>Buff template IDs this controller currently has on, and the volume each came from.</summary>
		private readonly Dictionary<int, BuffVolume> applied = new Dictionary<int, BuffVolume>();

		/// <summary>Rebuilt each tick. A field so the per-tick pass does not allocate.</summary>
		private readonly Dictionary<int, BuffVolume> wanted = new Dictionary<int, BuffVolume>();

		/// <summary>Buffs to drop this tick, collected before removing so the dictionary is not mutated mid-walk.</summary>
		private readonly List<int> departed = new List<int>();

		/// <summary>True while this volume's buff is on because of this controller.</summary>
		public bool IsInside(BuffVolume volume) => volume != null && volume.Buff != null &&
			applied.TryGetValue(volume.Buff.ID, out BuffVolume from) && from == volume;

		/// <summary>How many volume buffs are currently held. For tests and the debug overlay.</summary>
		public int AppliedCount => applied.Count;

		/// <inheritdoc />
		public void PopulateInput(ref CharacterReplicateData input)
		{
			// Nothing. The whole step is derived from the position the replicate already carries.
		}

		/// <inheritdoc />
		public void OnReplicate(ref CharacterReplicateData input, ReplicateState state, Channel channel)
		{
			if (Character == null || Character.GameObject == null)
			{
				return;
			}

			Scene scene = Character.GameObject.scene;
			IReadOnlyList<BuffVolume> volumes = BuffVolumeRegistry.InScene(scene);
			if (volumes.Count == 0 && applied.Count == 0)
			{
				return;
			}

			Vector3 position = Character.Transform.position;

			/* Sampled once, and only when something actually asks for it. A weather sample is the
			 * expensive part of this tick and most volumes are a plain box with no gate on them at
			 * all, so a scene full of ungated volumes costs nothing but the collider tests. */
			WeatherSample weather = default;
			if (BuffVolumeRegistry.AnyNeedsWeather(scene))
			{
				weather = WeatherQuery.Sample(scene, position, WeatherExposureTick.Resolve(this, input.GetTick()));
			}
			else
			{
				// An ungated volume must still see a character in the open as exposed, or a
				// MinimumExposure of 0 would be the only setting that ever worked.
				weather.Shelter = 0f;
			}

			// A dead character keeps nothing: a corpse should not be sitting in a shrine's blessing.
			bool dead = Character.IsFlagged(CharacterFlags.IsDead);

			if (dead)
			{
				wanted.Clear();
			}
			else
			{
				BuffVolumeRegistry.Applicable(scene, position, weather, wanted);
			}

			Reconcile(wanted, input.GetPredictionTick());
		}

		/// <summary>Brings the applied set in line with what the position says it should be.</summary>
		private void Reconcile(Dictionary<int, BuffVolume> target, PredictionTick predictionTick)
		{
			if (!Character.TryGet(out IBuffController buffs))
			{
				return;
			}

			departed.Clear();
			foreach (KeyValuePair<int, BuffVolume> entry in applied)
			{
				if (!target.ContainsKey(entry.Key))
				{
					departed.Add(entry.Key);
				}
			}

			for (int i = 0; i < departed.Count; i++)
			{
				applied.Remove(departed[i]);
				buffs.Remove(departed[i]);
				if (VerboseLogging)
				{
					Log.Debug("BuffVolumeController", $"left a volume granting buff {departed[i]}");
				}
			}

			foreach (KeyValuePair<int, BuffVolume> entry in target)
			{
				if (applied.ContainsKey(entry.Key))
				{
					// Still inside; keep the volume it is credited to current so the one that is
					// actually containing the character is the one recorded.
					applied[entry.Key] = entry.Value;
					continue;
				}
				applied[entry.Key] = entry.Value;
				// The PREDICTED apply: the tick is already in the replicate domain, which is the
				// domain the buff's own expiry is evaluated against.
				buffs.Apply(entry.Value.Buff, predictionTick);
				if (VerboseLogging)
				{
					Log.Debug("BuffVolumeController", $"entered {entry.Value.name}, applying {entry.Value.Buff.name}");
				}
			}
		}

		/// <inheritdoc />
		public void OnCreateReconcile(ref CharacterReconcileData reconcileData)
		{
			// Nothing to send: see the class remarks.
		}

		/// <inheritdoc />
		public void OnReconcile(CharacterReconcileData rd, Channel channel)
		{
			/* Nothing to restore either. The buffs themselves have just been set to the server's by
			 * BuffController; the applied set is rebuilt from the position on the first replayed
			 * tick, which is the very next thing FishNet does. Clearing it here would be wrong, not
			 * merely redundant: a buff this controller applied would stop being credited to the
			 * volume that granted it, and would then never be taken off when the character walked
			 * out. */
		}

		/// <inheritdoc />
		public override void ResetState(bool asServer)
		{
			base.ResetState(asServer);
			// Pooled objects are reused for a different character: nothing of the last one's regions
			// may survive into the next.
			applied.Clear();
			wanted.Clear();
			departed.Clear();
		}
	}
}
