using System;
using System.Collections.Generic;
using UnityEngine;
using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>
	/// Selects the <see cref="EventData.Target"/> already carried on the event — without
	/// running any spatial query. This is the intended default for triggers fired in
	/// response to an event that already resolved its own target, such as:
	/// <list type="bullet">
	///   <item><description>Ability <c>OnHit</c> triggers (collision target).</description></item>
	///   <item><description>Region enter / exit triggers.</description></item>
	///   <item><description>Dialogue, item-use, or interaction triggers.</description></item>
	/// </list>
	/// <para>
	/// Falls back to the initiator's GameObject when the event has no Target — so an
	/// OnHit trigger fired as a self-cast (initiator == hit) still resolves correctly.
	/// </para>
	/// </summary>
	[Serializable]
	public class EventTargetSelector : TargetSelector
	{
		/// <summary>
		/// When true, falls back to the initiator's GameObject if <see cref="EventData.Target"/>
		/// is null. Default true to match the implicit fallback used by actions/conditions.
		/// Disable when the trigger must not run unless an explicit Target was supplied.
		/// </summary>
		[Tooltip("Fall back to the initiator when the event has no Target. Disable to require an explicit event target.")]
		public bool FallbackToInitiator = true;

		/// <summary>
		/// Yields the event's existing <see cref="EventData.Target"/> (or initiator fallback).
		/// </summary>
		/// <param name="eventData">The event data driving the selection.</param>
		/// <returns>An enumerable containing the resolved target, or empty when none.</returns>
		public override IEnumerable<GameObject> SelectTargets(EventData eventData)
		{
			if (eventData == null)
			{
				yield break;
			}

			GameObject candidate = eventData.Target;
			if (candidate == null && FallbackToInitiator)
			{
				candidate = eventData.Initiator?.GameObject;
			}

			if (candidate != null && AreConditionsMet(candidate, eventData))
			{
				yield return candidate;
			}
		}
	}
}