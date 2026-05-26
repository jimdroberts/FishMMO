using UnityEngine;
using System.Collections.Generic;
using FishMMO.Logging;
using FishMMO.Shared;
using UnityEngine.Serialization;

namespace FishMMO.Shared.Core
{
	/// <summary>
	/// Shared helpers for evaluating trigger conditions and executing trigger action lists.
	/// All helpers honor optional per-condition / per-action <see cref="BaseAction.TargetSelector"/>
	/// and <see cref="BaseCondition.TargetSelector"/> fan-out.
	/// </summary>
	public static class TriggerExecution
	{
		/// <summary>
		/// Evaluates a condition list against the supplied event data, honoring per-condition
		/// <see cref="BaseCondition.TargetSelector"/> fan-out. When <paramref name="filter"/>
		/// is null, falls back to <see cref="EventData.ConditionFilter"/> so the same
		/// filter applied at the top level propagates into composites and selector-scoped
		/// condition lists.
		/// </summary>
		/// <param name="conditions">The conditions to evaluate.</param>
		/// <param name="eventData">The event data passed to each condition.</param>
		/// <param name="filter">Optional explicit filter override (rarely needed — prefer
		/// setting <see cref="EventData.ConditionFilter"/> at the top of a trigger fire).</param>
		/// <returns>True when every (non-skipped) condition passes; otherwise false.</returns>
		public static bool AreConditionsMet(List<BaseCondition> conditions, EventData eventData, System.Func<BaseCondition, bool> filter = null)
		{
			if (eventData == null)
			{
				return false;
			}
			if (conditions == null)
			{
				return true;
			}

			System.Func<BaseCondition, bool> effective = filter ?? eventData.ConditionFilter;

			for (int i = 0; i < conditions.Count; ++i)
			{
				BaseCondition condition = conditions[i];
				if (condition == null)
				{
					continue;
				}
				if (effective != null && !effective(condition))
				{
					continue;
				}
				bool passed;
				try
				{
					passed = EvaluateCondition(condition, eventData);
				}
				catch (System.Exception ex)
				{
					Log.Error("Trigger", $"Condition '{condition.GetType().Name}' threw: {ex}");
					passed = false;
				}
				if (!passed)
				{
					return false;
				}
			}
			return true;
		}

		/// <summary>
		/// Evaluates a single condition, fanning out across <see cref="BaseCondition.TargetSelector"/>
		/// when set and combining results per <see cref="BaseCondition.Combine"/>.
		/// </summary>
		/// <param name="condition">The condition to evaluate.</param>
		/// <param name="eventData">The event data passed to the condition.</param>
		/// <returns>True when the condition is satisfied.</returns>
		public static bool EvaluateCondition(BaseCondition condition, EventData eventData)
		{
			if (condition.TargetSelector == null)
			{
				return condition.Check(eventData.Initiator, eventData);
			}

			foreach (GameObject target in condition.TargetSelector.SelectTargets(eventData))
			{
				if (target == null)
				{
					continue;
				}
				EventData scoped = eventData.Fork(target);
				bool passed = condition.Check(scoped.Initiator, scoped);
				if (passed && condition.Combine == ConditionTargetCombine.Any)
				{
					return true;
				}
				if (!passed && condition.Combine == ConditionTargetCombine.All)
				{
					return false;
				}
			}

			// Loop ended without short-circuiting:
			//   All  → no failure observed (or empty selection) → true (vacuous).
			//   Any  → no success observed → false.
			return condition.Combine == ConditionTargetCombine.All;
		}

		/// <summary>
		/// Executes an action list against the supplied event data, fanning out across
		/// per-action <see cref="BaseAction.TargetSelector"/> when set. Actions implementing
		/// <see cref="IAbortableAction"/> with <see cref="BaseAction.StopChainOnFailure"/> set
		/// will abort the rest of the chain when their <see cref="IAbortableAction.TryExecute"/>
		/// returns false (e.g. a resource cost that couldn't be paid).
		/// </summary>
		/// <param name="actions">The actions to execute.</param>
		/// <param name="eventData">The event data passed to each action.</param>
		public static void ExecuteActions(List<BaseAction> actions, EventData eventData)
		{
			if (actions == null || eventData == null)
			{
				return;
			}

			for (int i = 0; i < actions.Count; ++i)
			{
				BaseAction action = actions[i];
				if (action == null)
				{
					continue;
				}

				if (action.TargetSelector == null)
				{
					if (!RunOne(action, eventData.Initiator, eventData))
					{
						return;
					}
					continue;
				}

				foreach (GameObject target in action.TargetSelector.SelectTargets(eventData))
				{
					if (target == null)
					{
						continue;
					}
					EventData scoped = eventData.Fork(target);
					if (!RunOne(action, scoped.Initiator, scoped))
					{
						return;
					}
				}
			}
		}

		/// <summary>
		/// Invokes a single action instance, routing through <see cref="IAbortableAction"/> when
		/// the action opts in. Exceptions thrown by an action are caught and logged so a single
		/// malformed designer-authored action cannot poison the rest of the action chain or
		/// sibling targets in a fan-out. Returns false only when an abortable action signals
		/// failure AND has <see cref="BaseAction.StopChainOnFailure"/> set — in which case the
		/// caller should stop processing further actions/targets in the current list.
		/// </summary>
		private static bool RunOne(BaseAction action, ICharacter initiator, EventData eventData)
		{
			try
			{
				if (action is IAbortableAction abortable)
				{
					bool ok = abortable.TryExecute(initiator, eventData);
					return ok || !action.StopChainOnFailure;
				}
				action.Execute(initiator, eventData);
				return true;
			}
			catch (System.Exception ex)
			{
				Log.Error("Trigger", $"Action '{action.GetType().Name}' threw: {ex}");
				// Fault isolation: a throwing action does not abort sibling actions.
				return true;
			}
		}

		/// <summary>
		/// Shared selector-fanout + condition-branch helper used by both the asset-based
		/// <see cref="Trigger"/> and inline trigger types (e.g. <c>WorldSceneTrigger</c>). When
		/// <paramref name="selector"/> is null the event data is processed as-is; otherwise the
		/// event is forked per selected target. For each scope <paramref name="conditions"/> is
		/// evaluated and the matching action branch is dispatched. Centralising this loop keeps
		/// inline triggers in lock-step with future <see cref="Trigger"/> behaviour changes
		/// (fault isolation, ambient <see cref="EventData.ConditionFilter"/>, …).
		/// </summary>
		/// <param name="selector">Optional selector that fans the event out across multiple targets.</param>
		/// <param name="conditions">Conditions that gate which action branch fires.</param>
		/// <param name="onConditionsMetActions">Actions to dispatch when conditions pass.</param>
		/// <param name="onConditionsNotMetActions">Actions to dispatch when conditions fail.</param>
		/// <param name="eventData">The event data driving execution.</param>
		public static void RunInline(
			TargetSelector selector,
			List<BaseCondition> conditions,
			List<BaseAction> onConditionsMetActions,
			List<BaseAction> onConditionsNotMetActions,
			EventData eventData)
		{
			if (eventData == null)
			{
				return;
			}

			if (selector == null)
			{
				RunBranch(conditions, onConditionsMetActions, onConditionsNotMetActions, eventData);
				return;
			}

			foreach (GameObject target in selector.SelectTargets(eventData))
			{
				if (target == null)
				{
					continue;
				}
				RunBranch(conditions, onConditionsMetActions, onConditionsNotMetActions, eventData.Fork(target));
			}
		}

		/// <summary>
		/// Evaluates <paramref name="conditions"/> and dispatches the matching action list.
		/// </summary>
		private static void RunBranch(
			List<BaseCondition> conditions,
			List<BaseAction> onConditionsMetActions,
			List<BaseAction> onConditionsNotMetActions,
			EventData eventData)
		{
			if (AreConditionsMet(conditions, eventData))
			{
				ExecuteActions(onConditionsMetActions, eventData);
			}
			else
			{
				ExecuteActions(onConditionsNotMetActions, eventData);
			}
		}
	}

	/// <summary>
	/// A trigger executes a list of actions against a set of targets selected by <see cref="TargetSelector"/>,
	/// branching on whether <see cref="Conditions"/> are satisfied for each selected target.
	/// <para>
	/// Lifecycle (per fire):
	/// <list type="number">
	///   <item><description>Caller builds an <see cref="EventData"/> and invokes <see cref="Execute(EventData)"/>.</description></item>
	///   <item><description><see cref="TargetSelector"/> yields N targets (using <see cref="EventData.Initiator"/> / <see cref="EventData.Target"/> as context).</description></item>
	///   <item><description>For each target, the trigger forks <paramref name="eventData"/> with the new target, evaluates conditions, and runs the matching action branch.</description></item>
	/// </list>
	/// The <see cref="EventData.Initiator"/> never changes during the trigger's lifecycle.
	/// </para>
	/// </summary>
	[CreateAssetMenu(fileName = "New Trigger", menuName = "FishMMO/ECA/Trigger", order = 0)]
	public class Trigger : CachedScriptableObject<Trigger>, ICachedObject
	{
		[Header("Targeting")]
		/// <summary>
		/// Selector that produces the targets this trigger fires for. When null, the trigger
		/// fires once against the event data as-is (i.e. against whatever <see cref="EventData.Target"/>
		/// the caller already set) — equivalent to using <see cref="EventTargetSelector"/> but
		/// without per-target conditions or override slots. Set a concrete selector for
		/// spatial fan-out (Area, Cone, Chain, …) or for explicit per-target conditions.
		/// </summary>
		[Tooltip("Selector that produces the targets this trigger fires for. Leave null to act on the event's existing Target. Use InitiatorTargetSelector for self-only, EventTargetSelector for explicit hit-target semantics.")]
		[SerializeReference, SubclassSelector]
		public TargetSelector TargetSelector;

		[Header("Conditions")]
		/// <summary>
		/// Conditions that must be met for actions to execute. Each condition may have its own
		/// optional <see cref="BaseCondition.TargetSelector"/> for refined per-target evaluation.
		/// </summary>
		[Tooltip("Conditions that must be met for actions to execute. Evaluated top-to-bottom; the first failure short-circuits the rest. Wrap children in a CompositeCondition for OR / NOT semantics.")]
		[SerializeReference, SubclassSelector]
		public List<BaseCondition> Conditions = new List<BaseCondition>();

		[Header("On Conditions Met")]
		/// <summary>
		/// Actions to execute when all conditions are met for the current target.
		/// Each action may have its own optional <see cref="BaseAction.TargetSelector"/> for additional fan-out.
		/// </summary>
		[Tooltip("Actions to execute when all conditions pass. Executed top-to-bottom. Order matters: an IAbortableAction with StopChainOnFailure set will abort every action below it in this list (and the rest of its fan-out targets) when its TryExecute returns false. Put resource-cost / can-afford gates first.")]
		[FormerlySerializedAs("Actions")]
		[SerializeReference, SubclassSelector]
		public List<BaseAction> OnConditionsMetActions = new List<BaseAction>();

		[Header("On Conditions Not Met")]
		/// <summary>
		/// Actions to execute when one or more conditions fail for the current target.
		/// </summary>
		[Tooltip("Actions to execute when any condition fails. Executed top-to-bottom with the same StopChainOnFailure semantics as the met branch.")]
		[SerializeReference, SubclassSelector]
		public List<BaseAction> OnConditionsNotMetActions = new List<BaseAction>();

		[Header("Debug")]
		/// <summary>
		/// When true, this trigger promotes its own <c>Log.Debug</c> diagnostics to <c>Log.Info</c>
		/// so its lifecycle is visible without flipping the global log level. Use for diagnosing
		/// a single misbehaving trigger asset in a noisy build.
		/// </summary>
		[Tooltip("When checked, this trigger's own lifecycle logs (conditions met / not met / no targets) are promoted from Debug to Info so they show up without enabling global Debug logging. Diagnostic use only.")]
		public bool Verbose;

		/// <summary>
		/// Raised after <see cref="Execute(EventData)"/> finishes processing its targets, regardless
		/// of which action branch ran (or whether any targets were produced). Subscribers receive
		/// the firing <see cref="Trigger"/> and the top-level <see cref="EventData"/> passed in by
		/// the caller (post-fan-out, but the caller's data is not the per-target forked copy).
		/// <para>
		/// Intended for instrumentation, replay/recording tools, editor breakpoints, and
		/// server-side audit. Subscribers should be cheap and non-throwing — exceptions are
		/// caught and logged so a bad subscriber cannot poison gameplay. Handlers are invoked
		/// on the calling thread immediately after execution completes.
		/// </para>
		/// </summary>
		public static event System.Action<Trigger, EventData> OnExecuted;

		/// <summary>
		/// Fires this trigger. The trigger fans out across <see cref="TargetSelector"/> and runs
		/// the condition/action branches once per selected target. The initiator on
		/// <paramref name="eventData"/> is never reassigned.
		/// </summary>
		/// <param name="eventData">The event data driving execution.</param>
		public virtual void Execute(EventData eventData)
		{
			if (eventData == null)
			{
				Log.Warning("Trigger", $"Trigger '{name}' executed without event data.");
				return;
			}

			if (TargetSelector == null)
			{
				// Intentional fallback: act on the event's existing Target (or initiator
				// for events with no target). This is the common path for OnHit / region /
				// dialogue triggers whose caller already resolved the target.
				ExecuteForTarget(eventData);
				RaiseOnExecuted(eventData);
				return;
			}

			bool any = false;
			foreach (GameObject target in TargetSelector.SelectTargets(eventData))
			{
				if (target == null)
				{
					continue;
				}
				any = true;
				EventData scoped = eventData.Fork(target);
				ExecuteForTarget(scoped);
			}

			if (!any)
			{
				LogLifecycle($"Trigger '{name}' produced no targets for {eventData.Initiator?.Name}.");
			}

			RaiseOnExecuted(eventData);
		}

		/// <summary>
		/// Writes a lifecycle diagnostic at Debug level by default, or Info when <see cref="Verbose"/>
		/// is enabled on this trigger asset. Keeps the global log level untouched.
		/// </summary>
		private void LogLifecycle(string message)
		{
			if (Verbose)
			{
				Log.Info("Trigger", message);
			}
			else
			{
				Log.Debug("Trigger", message);
			}
		}

		/// <summary>
		/// Invokes the static <see cref="OnExecuted"/> event. Each subscriber is invoked in a
		/// try/catch so a throwing instrumentation subscriber cannot poison gameplay.
		/// </summary>
		private void RaiseOnExecuted(EventData eventData)
		{
			System.Action<Trigger, EventData> handlers = OnExecuted;
			if (handlers == null)
			{
				return;
			}

			foreach (System.Delegate handler in handlers.GetInvocationList())
			{
				try
				{
					((System.Action<Trigger, EventData>)handler)(this, eventData);
				}
				catch (System.Exception ex)
				{
					Log.Error("Trigger", $"OnExecuted subscriber threw for trigger '{name}': {ex}");
				}
			}
		}

		/// <summary>
		/// Evaluates conditions and runs the matching action branch for a single (already-scoped) event.
		/// </summary>
		/// <param name="eventData">The event data scoped to one target.</param>
		private void ExecuteForTarget(EventData eventData)
		{
			// Publish the per-fire condition filter on EventData so it propagates into
			// nested CompositeCondition.Evaluate and selector-scoped AreConditionsMet
			// without having to be threaded through every call site. Fork() carries it
			// across target fan-outs.
			eventData.ConditionFilter = ShouldEvaluateCondition;
			if (TriggerExecution.AreConditionsMet(Conditions, eventData))
			{
				LogLifecycle($"Trigger '{name}' conditions met for {eventData.Initiator?.Name}, target {eventData.Target?.name}.");
				TriggerExecution.ExecuteActions(OnConditionsMetActions, eventData);
			}
			else
			{
				LogLifecycle($"Trigger '{name}' conditions not met for {eventData.Initiator?.Name}, target {eventData.Target?.name}.");
				TriggerExecution.ExecuteActions(OnConditionsNotMetActions, eventData);
			}
		}

		/// <summary>
		/// Determines whether a condition should participate in this trigger execution.
		/// Subclasses may override to skip categories of conditions (e.g. resource cost conditions
		/// already paid at activation time).
		/// </summary>
		/// <param name="condition">The condition being considered.</param>
		/// <returns>True when the condition should be evaluated; otherwise, false.</returns>
		protected virtual bool ShouldEvaluateCondition(BaseCondition condition)
		{
			return true;
		}

#if UNITY_EDITOR
		/// <summary>
		/// Designer-invoked re-run of <see cref="OnValidate"/>. Unity normally only calls
		/// OnValidate on edit; this lets a designer re-run the null-strip and warning checks
		/// without touching a field (e.g. after bulk-renaming SubclassSelector types and
		/// reimporting).
		/// </summary>
		[ContextMenu("Validate Now")]
		private void ValidateNow()
		{
			OnValidate();
			UnityEditor.EditorUtility.SetDirty(this);
			Debug.Log($"[Trigger] '{name}': Validate Now complete.", this);
		}

		/// <summary>
		/// Editor-only sanity checks for designer authoring. Surfaces common misconfigurations
		/// as console warnings without blocking play (Unity's <c>OnValidate</c> contract).
		/// Strips null entries from action/condition lists that Unity occasionally leaves
		/// behind after class renames so designers don't accidentally hit logged "null action"
		/// warnings at runtime.
		/// </summary>
		protected virtual void OnValidate()
		{
			int strippedConditions = StripNulls(Conditions);
			int strippedMet = StripNulls(OnConditionsMetActions);
			int strippedNotMet = StripNulls(OnConditionsNotMetActions);

			if (strippedConditions + strippedMet + strippedNotMet > 0)
			{
				Debug.LogWarning(
					$"[Trigger] '{name}': stripped null entries (Conditions={strippedConditions}, OnConditionsMetActions={strippedMet}, OnConditionsNotMetActions={strippedNotMet}). " +
					"These are usually leftovers from a class rename or removed SubclassSelector type.", this);
			}

			bool noMet = OnConditionsMetActions == null || OnConditionsMetActions.Count == 0;
			bool noNotMet = OnConditionsNotMetActions == null || OnConditionsNotMetActions.Count == 0;
			if (noMet && noNotMet)
			{
				Debug.LogWarning(
					$"[Trigger] '{name}': both OnConditionsMetActions and OnConditionsNotMetActions are empty. " +
					"This trigger has no observable effect when fired.", this);
			}

			bool hasConditions = Conditions != null && Conditions.Count > 0;
			if (!hasConditions && OnConditionsNotMetActions != null && OnConditionsNotMetActions.Count > 0)
			{
				Debug.LogWarning(
					$"[Trigger] '{name}': OnConditionsNotMetActions has entries but Conditions is empty. " +
					"With no conditions, the met branch always runs and the not-met branch is unreachable.", this);
			}
		}

		/// <summary>
		/// Removes null entries from a SerializeReference list. Returns the number of entries removed.
		/// </summary>
		private static int StripNulls<T>(List<T> list) where T : class
		{
			if (list == null) return 0;
			int removed = 0;
			for (int i = list.Count - 1; i >= 0; --i)
			{
				if (list[i] == null)
				{
					list.RemoveAt(i);
					++removed;
				}
			}
			return removed;
		}
#endif
	}
}