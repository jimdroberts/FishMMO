using System;
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

		[Tooltip("States that exist only where two or more of the above do at once: soaked AND chilled is frozen. Evaluated from the levels, so they cost nothing on the wire.")]
		public List<WeatherExposureRecipe> Recipes = new List<WeatherExposureRecipe>();

		[Tooltip("How often the weather is sampled, in seconds. The level is stepped by exactly this much each time, so a slower sample is cheaper without changing how fast a state builds.")]
		[Range(0.1f, 5f)] public float SampleSeconds = 1f;

		[Tooltip("Log every level change. Very noisy; for working on exposure only.")]
		public bool VerboseLogging;

		/// <summary>Level per state, by template ID. The mutable half; the templates hold none.</summary>
		private readonly Dictionary<int, float> levels = new Dictionary<int, float>();

		/// <summary>Which states currently hold their buff, so it is applied and released once, not every tick.</summary>
		private readonly HashSet<int> held = new HashSet<int>();

		/// <summary>Which recipes currently hold. Derived from the levels, so never sent.</summary>
		private readonly HashSet<int> recipesHeld = new HashSet<int>();

		/// <summary>States whose own buff is off because a recipe that uses them is holding it instead.</summary>
		private readonly HashSet<int> suppressed = new HashSet<int>();

		/// <summary>Bound once so evaluating a recipe does not allocate a closure every tick.</summary>
		private Func<WeatherExposureTemplate, float> levelReader;

		/// <summary>Recipes with their nulls and duplicates removed, in a stable order.</summary>
		private List<WeatherExposureRecipe> orderedRecipes;

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

		/// <summary>True while the recipe is satisfied and holding its buff.</summary>
		public bool IsHolding(WeatherExposureRecipe recipe) => recipe != null && recipesHeld.Contains(recipe.ID);

		/// <summary>True while a recipe is holding this state's buff in its place.</summary>
		public bool IsSuppressed(WeatherExposureTemplate state) => state != null && suppressed.Contains(state.ID);

		/// <summary>Every recipe this controller can satisfy, nulls and duplicates removed.</summary>
		public IReadOnlyList<WeatherExposureRecipe> OrderedRecipes => EnsureRecipes();

		private List<WeatherExposureRecipe> EnsureRecipes()
		{
			if (orderedRecipes != null)
			{
				return orderedRecipes;
			}
			orderedRecipes = new List<WeatherExposureRecipe>();
			for (int i = 0; i < Recipes.Count; i++)
			{
				if (Recipes[i] != null && !orderedRecipes.Contains(Recipes[i]))
				{
					EnsureCached(Recipes[i], Recipes[i].name);
					orderedRecipes.Add(Recipes[i]);
				}
			}
			// By ID. Nothing of a recipe goes on the wire, so this is not the serializer's stable
			// order — it is only so two peers evaluate the same recipes in the same sequence, which
			// matters the moment two recipes suppress the same state.
			orderedRecipes.Sort((a, b) => a.ID.CompareTo(b.ID));
			return orderedRecipes;
		}

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
					EnsureCached(States[i], States[i].name);
					ordered.Add(States[i]);
				}
			}
			// By ID, matching every other reconcile array: the index-delta serializer compares
			// position by position, so an unstable order would resend every entry every tick.
			ordered.Sort((a, b) => a.ID.CompareTo(b.ID));
			return ordered;
		}

		/// <summary>
		/// Gives a template its deterministic ID if nothing has yet.
		/// </summary>
		/// <remarks>
		/// <para>
		/// A cached template's ID comes from <c>AddToCache</c>, which the addressables loader calls on
		/// everything it loads. These templates need not be addressable — the prefab references them
		/// directly, so Unity loads them with it — and an asset that arrives that way has an ID of
		/// ZERO. That is not a harmless zero. Every state would share it, so the reconcile array's
		/// sort would be arbitrary and its entries indistinguishable; and
		/// <see cref="WeatherExposureTemplate.Get{T}"/> could never find one again, so no buff would
		/// survive a reconcile. <see cref="WeatherHost"/> guards its layer templates the same way and
		/// for the same reason.
		/// </para>
		/// <para>
		/// The ID is a hash of the type name and the asset name, so every peer derives the same one
		/// from the same asset without anything being sent.
		/// </para>
		/// </remarks>
		private static void EnsureCached(ICachedObject cached, string assetName)
		{
			if (cached.ID == 0)
			{
				cached.AddToCache(assetName);
			}
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
				// Recipes read state levels and nothing else, so with no states there is nothing for
				// one to be made of either.
				return;
			}

			uint weatherTick = WeatherExposureTick.Resolve(this, input.GetTick());
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

			/* Three passes, and the order is the point.
			 *
			 * Every level moves first, because a recipe is a verdict on the levels and all of them
			 * have to be this tick's before any verdict is taken. Then the recipes are judged, which
			 * is also what decides which states are having their buff held for them. Only then do
			 * buffs move. Done in one pass instead, a state would apply its own buff and a recipe
			 * would take it straight off again in the same tick — a spurious apply and remove on
			 * every single tick of the combined state, each one a message to every observer. */
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
			}

			EvaluateRecipes();

			for (int i = 0; i < states.Count; i++)
			{
				WeatherExposureTemplate template = states[i];
				levels.TryGetValue(template.ID, out float level);
				ApplyOrRelease(template, level, predictionTick);
			}

			List<WeatherExposureRecipe> recipes = EnsureRecipes();
			for (int i = 0; i < recipes.Count; i++)
			{
				ApplyOrReleaseRecipe(recipes[i], predictionTick);
			}
		}

		/// <summary>
		/// Decides which recipes hold from the levels as they now stand, and which states are having
		/// their buff held by one.
		/// </summary>
		/// <remarks>
		/// Rebuilt from scratch each time rather than patched, so the suppressed set cannot drift out
		/// of step with the recipes that caused it — a state stops being suppressed the moment the
		/// last recipe using it lets go, with no bookkeeping to forget.
		/// </remarks>
		private void EvaluateRecipes()
		{
			List<WeatherExposureRecipe> recipes = EnsureRecipes();
			suppressed.Clear();
			if (recipes.Count == 0)
			{
				recipesHeld.Clear();
				return;
			}

			levelReader ??= LevelOf;

			for (int i = 0; i < recipes.Count; i++)
			{
				WeatherExposureRecipe recipe = recipes[i];
				bool wasHolding = recipesHeld.Contains(recipe.ID);
				bool satisfied = recipe.IsSatisfied(wasHolding, levelReader);

				if (satisfied)
				{
					recipesHeld.Add(recipe.ID);
					if (recipe.SuppressIngredients)
					{
						for (int j = 0; j < recipe.Ingredients.Count; j++)
						{
							WeatherExposureIngredient ingredient = recipe.Ingredients[j];
							if (ingredient?.State != null)
							{
								suppressed.Add(ingredient.State.ID);
							}
						}
					}
				}
				else
				{
					recipesHeld.Remove(recipe.ID);
				}

				if (VerboseLogging && satisfied != wasHolding)
				{
					Log.Debug("WeatherExposureController", $"recipe {recipe.DisplayName}: {(satisfied ? "held" : "released")}");
				}
			}
		}

		/// <summary>Puts a recipe's buff on while it holds and takes it off when it lets go.</summary>
		/// <remarks>
		/// The hysteresis lives in <see cref="WeatherExposureRecipe.IsSatisfied"/>, which has already
		/// run by the time this is called, so there is only the one verdict to act on here.
		/// </remarks>
		private void ApplyOrReleaseRecipe(WeatherExposureRecipe recipe, PredictionTick predictionTick)
		{
			if (recipe.Buff == null)
			{
				// A recipe with no buff of its own still suppresses, which is enough to be useful: it
				// is how a combined state can simply silence its parts.
				return;
			}

			bool holding = recipesHeld.Contains(recipe.ID);
			bool applied = Character.TryGet(out IBuffController buffs) && buffs.Buffs.ContainsKey(recipe.Buff.ID);

			if (holding && !applied && buffs != null)
			{
				buffs.Apply(recipe.Buff, predictionTick);
			}
			else if (!holding && applied)
			{
				buffs.Remove(recipe.Buff.ID);
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

			/* A recipe is holding this state's buff in its place: soaked-and-chilled shows as frozen,
			 * not as frozen plus soaked plus chilled. The LEVEL keeps running underneath — the
			 * character is still getting wetter, and still has to dry off — because suppression is
			 * about what is shown and what it does, not about the weather stopping. */
			if (suppressed.Contains(template.ID))
			{
				if (holding)
				{
					held.Remove(template.ID);
					if (Character.TryGet(out IBuffController owner))
					{
						owner.Remove(template.Buff.ID);
					}
				}
				return;
			}

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

			/* And the recipes, from the levels that have just landed. Nothing about a recipe is sent,
			 * because nothing about one needs to be: it is a function of the levels, and the levels
			 * are now the server's. The one thing genuinely not derivable is which side of the
			 * hysteresis band the recipe was on, and that is resolved the same way a single state's
			 * hold is — by reading it as NOT held, so the stricter apply threshold decides. A recipe
			 * that should be on is applied again on the very next step, whereas one wrongly believed
			 * held would never be applied again at all. */
			recipesHeld.Clear();
			EvaluateRecipes();

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
			recipesHeld.Clear();
			suppressed.Clear();
			snapshot = null;
			snapshotDirty = true;
			ordered = null;
			orderedRecipes = null;
		}
	}
}
