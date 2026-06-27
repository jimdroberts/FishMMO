using System;
using UnityEngine;
using FishMMO.Logging;
using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>
	/// Action that destroys a specified game object in the scene.
	/// </summary>
	[Serializable]
	public class DestroyObjectAction : BaseAction
	{
		/// <summary>
		/// Destroys the target game object if present in the event data.
		/// </summary>
		/// <param name="initiator">The character initiating the action.</param>
		/// <param name="eventData">The event data containing the target object.</param>
		/// <remarks>
		/// Reads <see cref="EventData.Target"/>; logs a warning when no target is present.
		/// Object destruction is gated behind <c>UNITY_SERVER</c> to ensure only the
		/// authoritative server can destroy objects.
		/// </remarks>
		public override void Execute(ICharacter initiator, EventData eventData)
		{
		#if UNITY_SERVER
			if (eventData != null && eventData.Target != null)
			{
				GameObject target = eventData.Target;
				target.SetActive(false);
				UnityEngine.Object.Destroy(target);
			}
			else
			{
				Log.Warning("DestroyObjectAction", "Expected an EventData with a non-null Target.");
			}
		#endif
		}
	}
}
