using System;
using System.Collections.Generic;
using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>
	/// OnInteract triggers a module attaches to every interactable of a kind, at run time, so the interactable's prefab
	/// names nothing of the module (removing the module removes the behaviour and leaves the prefab whole).
	/// </summary>
	/// <remarks>
	/// The Travel module attaches its "Waypoint Interact" trigger (discover the waypoint, count it) to every
	/// <see cref="Waypoint"/>. Attached triggers run after the interactable's own authored ones; a kind matches its
	/// subclasses too. Server side, like every OnInteract trigger.
	/// </remarks>
	public static class InteractableTriggers
	{
		private static readonly Dictionary<Type, List<Trigger>> attached = new Dictionary<Type, List<Trigger>>();
		private static readonly Dictionary<Type, List<Trigger>> resolved = new Dictionary<Type, List<Trigger>>();
		private static readonly List<Trigger> none = new List<Trigger>(0);

		/// <summary>Attaches <paramref name="trigger"/> to every interactable of <paramref name="interactableType"/> (and its subclasses).</summary>
		public static void Attach(Type interactableType, Trigger trigger)
		{
			if (interactableType == null || trigger == null)
			{
				return;
			}
			if (!attached.TryGetValue(interactableType, out List<Trigger> list))
			{
				attached[interactableType] = list = new List<Trigger>(1);
			}
			if (!list.Contains(trigger))
			{
				list.Add(trigger);
			}
			resolved.Clear();
		}

		/// <summary>Detaches what <see cref="Attach"/> attached; false when it was not attached.</summary>
		public static bool Detach(Type interactableType, Trigger trigger)
		{
			bool removed = interactableType != null && attached.TryGetValue(interactableType, out List<Trigger> list) && list.Remove(trigger);
			if (removed)
			{
				resolved.Clear();
			}
			return removed;
		}

		/// <summary>The triggers attached to <paramref name="interactableType"/> and its base types, in attach order. Do not modify.</summary>
		public static List<Trigger> For(Type interactableType)
		{
			if (interactableType == null || attached.Count == 0)
			{
				return none;
			}
			if (resolved.TryGetValue(interactableType, out List<Trigger> cached))
			{
				return cached;
			}
			var result = new List<Trigger>();
			for (Type type = interactableType; type != null; type = type.BaseType)
			{
				if (attached.TryGetValue(type, out List<Trigger> list))
				{
					result.AddRange(list);
				}
			}
			resolved[interactableType] = result.Count > 0 ? result : none;
			return resolved[interactableType];
		}
	}
}
