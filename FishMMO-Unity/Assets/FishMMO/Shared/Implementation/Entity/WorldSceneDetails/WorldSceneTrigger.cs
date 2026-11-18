using System;
using System.Collections.Generic;
using FishMMO.Logging;
using FishMMO.Shared.Core;
using UnityEngine;
using UnityEngine.Serialization;

namespace FishMMO.Shared
{
	/// <summary>
	/// Inline ECA trigger data for world-scene events (scene load, day start, night start, …).
	/// <para>
	/// Unlike the asset-based <see cref="Core.Trigger"/>, this type is <c>[Serializable]</c> and
	/// lives directly on a host MonoBehaviour so designers can author scene-specific responses
	/// inline. Execution semantics — selector fan-out, condition branching, action dispatch —
	/// are delegated to <see cref="TriggerExecution.RunInline"/> so this type stays in lock-step
	/// with any future <see cref="Core.Trigger"/> behaviour changes.
	/// </para>
	/// </summary>
	[Serializable]
	public class WorldSceneTrigger
	{
		/// <summary>
		/// Display name for this trigger entry. Surfaced by a custom property drawer so the
		/// Inspector list shows meaningful element labels instead of "Element 0".
		/// </summary>
		public string Name = "New Trigger";

		[Header("Targeting")]
		/// <summary>
		/// Optional selector used to choose scene targets for this trigger entry.
		/// </summary>
		[Tooltip("Optional selector used to choose scene targets. When null, the trigger executes once against the world-level event data.")]
		[SerializeReference, SubclassSelector]
		public TargetSelector TargetSelector;

		[Header("Conditions")]
		/// <summary>
		/// Conditions that must pass before actions execute.
		/// </summary>
		[Tooltip("Conditions that must pass before actions execute. Evaluated top-to-bottom; the first failure short-circuits the rest. Wrap children in a CompositeCondition for OR / NOT semantics.")]
		[SerializeReference, SubclassSelector]
		public List<BaseCondition> Conditions = new List<BaseCondition>();

		[Header("On Conditions Met")]
		/// <summary>
		/// Actions to execute when all conditions pass.
		/// </summary>
		[Tooltip("Actions to execute when all conditions pass. Executed top-to-bottom. Order matters: an IAbortableAction with StopChainOnFailure set will abort every action below it in this list (and the rest of its fan-out targets) when its TryExecute returns false.")]
		[FormerlySerializedAs("Actions")]
		[SerializeReference, SubclassSelector]
		public List<BaseAction> OnConditionsMetActions = new List<BaseAction>();

		[Header("On Conditions Not Met")]
		/// <summary>
		/// Actions to execute when one or more conditions fail.
		/// </summary>
		[Tooltip("Actions to execute when any condition fails. Executed top-to-bottom with the same StopChainOnFailure semantics as the met branch.")]
		[SerializeReference, SubclassSelector]
		public List<BaseAction> OnConditionsNotMetActions = new List<BaseAction>();

		/// <summary>
		/// Raised after every <see cref="Execute(EventData)"/> call (mirrors
		/// <see cref="Core.Trigger.OnExecuted"/>). Subscribers receive the firing trigger
		/// and the original event data. Wrapped in try/catch so instrumentation cannot
		/// poison gameplay.
		/// </summary>
		public static event Action<WorldSceneTrigger, EventData> OnExecuted;

		/// <summary>
		/// Executes this trigger against the supplied event data, delegating to the shared
		/// <see cref="TriggerExecution.RunInline"/> helper so this type benefits from every
		/// fix made to the ECA core (fault isolation, ambient ConditionFilter, fan-out rules).
		/// </summary>
		/// <param name="eventData">The event data used by conditions and actions.</param>
		public void Execute(EventData eventData)
		{
			if (eventData == null)
			{
				return;
			}

			TriggerExecution.RunInline(TargetSelector, Conditions, OnConditionsMetActions, OnConditionsNotMetActions, eventData);
			RaiseOnExecuted(eventData);
		}

		/// <summary>
		/// Invokes <see cref="OnExecuted"/> with per-subscriber try/catch.
		/// </summary>
		private void RaiseOnExecuted(EventData eventData)
		{
			Action<WorldSceneTrigger, EventData> handlers = OnExecuted;
			if (handlers == null)
			{
				return;
			}

			foreach (Delegate handler in handlers.GetInvocationList())
			{
				try
				{
					((Action<WorldSceneTrigger, EventData>)handler)(this, eventData);
				}
				catch (Exception ex)
				{
					Log.Error("WorldSceneTrigger", $"OnExecuted subscriber threw for trigger '{Name}': {ex}");
				}
			}
		}

#if UNITY_EDITOR
		/// <summary>
		/// Editor-only null-strip for <c>SubclassSelector</c> remnants. Mirrors
		/// <see cref="Core.Trigger.OnValidate"/> on a per-entry basis so host MonoBehaviours
		/// can sanitise their <see cref="WorldSceneTrigger"/> lists from their own
		/// <c>OnValidate</c>. Returns the total number of nulls removed across all three
		/// inner lists.
		/// </summary>
		public int Sanitize()
		{
			int removed = 0;
			removed += StripNulls(Conditions);
			removed += StripNulls(OnConditionsMetActions);
			removed += StripNulls(OnConditionsNotMetActions);
			return removed;
		}

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