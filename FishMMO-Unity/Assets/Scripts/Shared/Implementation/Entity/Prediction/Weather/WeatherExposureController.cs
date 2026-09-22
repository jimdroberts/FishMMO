using System.Collections.Generic;
using FishNet.Managing.Timing;
using FishNet.Object.Prediction;
using FishNet.Transporting;
using UnityEngine;
using FishMMO.Logging;
using FishMMO.Shared.Core;
using FishMMO.Shared.Weather;

namespace FishMMO.Shared
{
	/// <summary>
	/// Turns the weather a character is standing in into buffs — wet, chilled, wind-burnt — and does
	/// it inside the prediction replicate, so the owner sees the state change at the moment it earns
	/// it rather than a round trip later.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Who simulates.</b> The server and the owning client, driven from
	/// <see cref="CharacterPredictionController"/>'s single <c>[Replicate]</c> like every other
	/// controller. An observer never runs it: state forwarding is off on every prefab, and the buffs
	/// it applies reach observers the way all buffs do.
	/// </para>
	/// <para>
	/// <b>Why it can be predicted at all.</b> Everything it reads is a pure function of the tick and
	/// the position. The weather timeline is deterministic from its seed and clock anchor, both of
	/// which the client already holds; shelter is a point-in-collider test
	/// (<see cref="WeatherVolume.Contains"/>, and so <c>RegionGeometry.ContainsPoint</c>) rather than
	/// a physics trigger, which matters because triggers poll in <c>OnPostPhysicsSimulation</c>,
	/// outside the replicate, are not rolled back, and re-fire their Enter/Exit on every replay
	/// (N12). So the owner can work out the same answer the server will, and a replay of the same
	/// tick gives the same answer again.
	/// </para>
	/// <para>
	/// <b>Which clock.</b> The weather is evaluated at a SYNCHRONISED tick, and the replicate's tick
	/// is the owner's unsynchronised counter: see <see cref="WeatherExposureTick"/>, which is where
	/// that conversion lives and is tested.
	/// </para>
	/// <para>
	/// <b>No new field in the input.</b> Nothing is sent up from the client; the whole step is
	/// derived from the tick, the character's own position and the timeline both sides already have
	/// (N12). What does ride the reconcile is the levels, because they are timers — see
	/// <see cref="CharacterReconcileData.Exposure"/>.
	/// </para>
	/// </remarks>
	[DisallowMultipleComponent]
	public class WeatherExposureController : CharacterBehaviour, IPredictableController
	{
		/// <summary>
		/// After movement (KCCPlayer, 80) so the position is this tick's, and before buffs
		/// (BuffController, 85) so a buff applied here is ticked in the same step rather than the
		/// next one.
		/// </summary>
		public int Order => 83;

		[Header("Exposure")]
		[Tooltip("The states this character can be put into by the weather. Empty: the controller does nothing.")]
		public List<WeatherExposureTemplate> States = new List<WeatherExposureTemplate>();

		[Tooltip("How often the weather is sampled, in seconds. The level is stepped by exactly this much each time, so a slower sample is cheaper without changing how fast a state builds.")]
		[Range(0.1f, 5f)] public float SampleSeconds = 1f;

		[Tooltip("Log every level change. Very noisy; for working on exposure only.")]
		public bool VerboseLogging;

		/// <summary>Level per state, by template ID. The mutable half; the templates hold none.</summary>
		private readonly Dictionary<int, float> levels = new Dictionary<int, float>();

		/// <summary>Which states currently hold their buff, so it is applied and released once, not every tick.</summary>
		private readonly HashSet<int> held = new HashSet<int>();

		/// <summary>Reused so a per-tick snapshot does not allocate.</summary>
		private ExposureReconcileEntry[] snapshot;
		private bool snapshotDirty = true;

		/// <summary>Sorted once on first use; the reconcile array's order must be stable.</summary>
		private List<WeatherExposureTemplate> ordered;

		/// <summary>The level of one state, 0..1. For the recipes, the UI and tests.</summary>
		public float LevelOf(WeatherExposureTemplate state)
		{
			if (state == null)
			{
				return 0f;
			}
			return levels.TryGetValue(state.ID, out float level) ? level : 0f;
		}

		/// <summary>True while the state's buff is held.</summary>
		public bool IsHolding(WeatherExposureTemplate state) => state != null && held.Contains(state.ID);

		/// <summary>Every state this controller can enter, in reconcile order.</summary>
		public IReadOnlyList<WeatherExposureTemplate> OrderedStates => EnsureOrdered();

		private List<WeatherExposureTemplate> EnsureOrdered()
		{
			if (ordered != null)
			{
				return ordered;
			}
			ordered = new List<WeatherExposureTemplate>();
			for (int i = 0; i < States.Count; i++)
			{
				if (States[i] != null && !ordered.Contains(States[i]))
				{
					ordered.Add(States[i]);
				}
			}
			// By ID, matching every other reconcile array: the index-delta serializer compares
			// position by position, so an unstable order would resend every entry every tick.
			ordered.Sort((a, b) => a.ID.CompareTo(b.ID));
			return ordered;
		}

		/// <inheritdoc />
		public void PopulateInput(ref CharacterReplicateData input)
		{
			// Nothing. The step needs no client input — see the class remarks (N12).
		}

		/// <inheritdoc />
		public void OnReplicate(ref CharacterReplicateData input, ReplicateState state, Channel channel)
		{
			List<WeatherExposureTemplate> states = EnsureOrdered();
			if (states.Count == 0 || Character == null)
			{
				return;
			}

			uint weatherTick = ResolveWeatherTick(input.GetTick());
			double tickDelta = base.TimeManager != null ? base.TimeManager.TickDelta : 1.0 / 30.0;

			// Sampled on a tick boundary derived from the WEATHER tick, so the server and the owner
			// sample on exactly the same ticks and a replay repeats the same ones. A wall clock, or
			// a counter of this controller's own, would drift between the two and put the buff on at
			// a different moment on each.
			uint interval = (uint)Mathf.Max(1, Mathf.RoundToInt(SampleSeconds / Mathf.Max(1e-4f, (float)tickDelta)));
			if (weatherTick % interval != 0u)
			{
				return;
			}

			float seconds = (float)(interval * tickDelta);
			Step(weatherTick, seconds, input.GetPredictionTick());
		}

		/// <summary>
		/// One exposure step: what the weather is doing here, what that drives each state to, and
		/// which buffs go on or come off as a result.
		/// </summary>
		private void Step(uint weatherTick, float seconds, PredictionTick predictionTick)
		{
			List<WeatherExposureTemplate> states = EnsureOrdered();
			bool dead = Character.IsFlagged(CharacterFlags.IsDead);

			// A dead character stops gathering exposure but still sheds it, so a corpse is not
			// frozen solid for ever and a raise does not start in a hole.
			WeatherSample sample = default;
			bool sampled = false;
			if (!dead && Character.GameObject != null)
			{
				sample = WeatherQuery.Sample(Character.GameObject.scene, Character.Transform.position, weatherTick);
				sampled = true;
			}

			float exposure = sampled ? sample.Exposure : 0f;

			for (int i = 0; i < states.Count; i++)
			{
				WeatherExposureTemplate template = states[i];
				float level = levels.TryGetValue(template.ID, out float current) ? current : 0f;
				// The whole sample: the blended channels, the driver's air, the physical temperature
				// and how sheltered this spot is, all from the one reading taken above.
				float drive = sampled ? template.Drive(sample) : 0f;
				float next = template.Step(level, drive, seconds, exposure);

				if (!Mathf.Approximately(next, level))
				{
					levels[template.ID] = next;
					snapshotDirty = true;
					if (VerboseLogging)
					{
						Log.Debug("WeatherExposureController", $"{template.DisplayName}: {level:0.000} -> {next:0.000} (drive {drive:0.000}, exposure {exposure:0.00})");
					}
				}

				ApplyOrRelease(template, next, predictionTick);
			}
		}

		/// <summary>
		/// Puts the state's buff on when the level rises past its threshold and takes it off when it
		/// falls past the lower one, and not on any tick between.
		/// </summary>
		/// <remarks>
		/// Two thresholds, deliberately apart: with one, a level sitting on it would apply and remove
		/// the buff on alternate ticks for as long as the weather held there, which is a stream of
		/// observer messages and a flickering icon for weather that is not actually changing.
		/// </remarks>
		private void ApplyOrRelease(WeatherExposureTemplate template, float level, PredictionTick predictionTick)
		{
			if (template.Buff == null)
			{
				// A state with no buff is still tracked: it can be an ingredient in a recipe.
				return;
			}

			bool holding = held.Contains(template.ID);
			if (!holding && level >= template.ApplyAt)
			{
				held.Add(template.ID);
				if (Character.TryGet(out IBuffController buffs))
				{
					// The PREDICTED apply: the tick is already in the replicate domain, which is the
					// domain the buff's own expiry is evaluated against.
					buffs.Apply(template.Buff, predictionTick);
				}
			}
			else if (holding && level <= template.ReleaseAt)
			{
				held.Remove(template.ID);
				if (Character.TryGet(out IBuffController buffs))
				{
					buffs.Remove(template.Buff.ID);
				}
			}
		}

		/// <summary>
		/// The tick to read the weather at, from whichever clock this peer has. See
		/// <see cref="WeatherExposureTick"/> for why the replicate's own tick will not do.
		/// </summary>
		private uint ResolveWeatherTick(uint inputTick)
		{
			TimeManager time = base.TimeManager;
			bool isServer = base.IsServerStarted;

			uint serverTick = time != null ? time.Tick : WeatherExposureTick.Unset;
			uint clientSyncTick = serverTick;
			uint clientStateTick = WeatherExposureTick.Unset;
			uint serverStateTick = WeatherExposureTick.Unset;

			if (!isServer && base.PredictionManager != null)
			{
				clientStateTick = base.PredictionManager.ClientStateTick;
				serverStateTick = base.PredictionManager.ServerStateTick;
			}

			return WeatherExposureTick.Resolve(isServer, serverTick, clientSyncTick, clientStateTick, serverStateTick, inputTick);
		}

		/// <inheritdoc />
		public void OnCreateReconcile(ref CharacterReconcileData reconcileData)
		{
			reconcileData.Exposure = CreateReconcileSnapshot();
		}

		/// <summary>
		/// The levels as they go on the wire, sorted by template ID and quantised.
		/// </summary>
		/// <remarks>
		/// The cached array is returned unchanged when nothing moved, so the delta serializer's
		/// <c>ReferenceEquals</c> shortcut skips the comparison entirely — the same trick the buff
		/// and attribute snapshots use.
		/// </remarks>
		public ExposureReconcileEntry[] CreateReconcileSnapshot()
		{
			List<WeatherExposureTemplate> states = EnsureOrdered();
			if (states.Count == 0)
			{
				return null;
			}

			if (!snapshotDirty && snapshot != null)
			{
				return snapshot;
			}

			/* A FRESH array every time, never the last one refilled. The delta serializer keeps the
			 * array it was given last tick as its baseline and shortcuts on ReferenceEquals, so
			 * writing new levels into that same instance would change the baseline and the new value
			 * together — the comparison would find them equal and the change would never be sent.
			 * The buff and attribute snapshots allocate fresh for the same reason. */
			snapshot = new ExposureReconcileEntry[states.Count];

			for (int i = 0; i < states.Count; i++)
			{
				WeatherExposureTemplate template = states[i];
				levels.TryGetValue(template.ID, out float level);
				snapshot[i] = new ExposureReconcileEntry
				{
					TemplateID = template.ID,
					Level = ExposureReconcileEntry.Quantise(level),
				};
			}
			snapshotDirty = false;
			return snapshot;
		}

		/// <inheritdoc />
		public void OnReconcile(CharacterReconcileData rd, Channel channel)
		{
			// The owner reconciles its own simulation. A non-owner only does so on a forwarded
			// object, which is the mode where the reconcile is how state reaches observers at all.
			if (!base.IsOwner && !ObserverSyncMode.ObserversConsumeReconcile(base.NetworkObject))
			{
				return;
			}

			RestoreFromReconcile(rd.Exposure);
		}

		/// <summary>Takes the server's levels as the truth, and re-derives which buffs are held.</summary>
		/// <remarks>
		/// The held set is rebuilt from the restored levels rather than carried across the
		/// reconcile, because the levels have just been overwritten: a buff whose level the server
		/// says never reached the threshold must not still be marked as held, or it would never be
		/// applied again. Dequantising here — the same function the server quantised with — leaves
		/// both peers on bit-identical levels, so the replay that follows starts from the server's
		/// state exactly.
		/// </remarks>
		public void RestoreFromReconcile(ExposureReconcileEntry[] entries)
		{
			if (entries == null)
			{
				return;
			}

			for (int i = 0; i < entries.Length; i++)
			{
				ExposureReconcileEntry entry = entries[i];
				float level = ExposureReconcileEntry.Dequantise(entry.Level);
				levels[entry.TemplateID] = level;

				// Re-derive the hold. Between the two thresholds the state is ambiguous — it depends
				// on which way the level was travelling — and the high threshold is the safe reading:
				// a buff that should be on is applied again on the next step, whereas one wrongly
				// believed held would never be re-applied at all.
				WeatherExposureTemplate template = WeatherExposureTemplate.Get<WeatherExposureTemplate>(entry.TemplateID);
				if (template == null)
				{
					continue;
				}
				if (level >= template.ApplyAt)
				{
					held.Add(entry.TemplateID);
				}
				else if (level <= template.ReleaseAt)
				{
					held.Remove(entry.TemplateID);
				}
			}
			snapshotDirty = true;
		}

		/// <inheritdoc />
		public override void ResetState(bool asServer)
		{
			base.ResetState(asServer);
			// Pooled objects are reused for a different character: nothing of the last one's weather
			// may survive into the next.
			levels.Clear();
			held.Clear();
			snapshot = null;
			snapshotDirty = true;
			ordered = null;
		}
	}
}
