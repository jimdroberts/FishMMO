using System;
using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// Associates an <see cref="AIController"/> with a role in an <see cref="NPCGroup"/>.
	/// Serialized so designers can configure group composition in the inspector.
	/// </summary>
	[Serializable]
	public class NPCGroupMember
	{
		/// <summary>
		/// The AI controller of the NPC that belongs to this group.
		/// </summary>
		[Tooltip("The NPC's AI controller.")]
		public AIController Controller;

		/// <summary>
		/// This member's combat role (Tank, Healer, DPS, Support).
		/// </summary>
		[Tooltip("This member's combat role.")]
		public NPCGroupRole Role = NPCGroupRole.DPS;
	}
}