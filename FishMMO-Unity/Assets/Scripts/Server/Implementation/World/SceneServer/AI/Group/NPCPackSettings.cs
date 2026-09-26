using System;
using UnityEngine;

namespace FishMMO.Server.Implementation.World.SceneServer.AI
{
	/// <summary>
	/// How a spawner's NPCs fight as a pack: whether they are one at all, and the tactic, focus and
	/// ring they share. Authored on an <c>ObjectSpawner</c>, baked into its <c>SpawnerDefinition</c>,
	/// and read by the running spawner when it founds an <see cref="NPCGroup"/>.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Off by default, so every spawner authored before packs existed runs unchanged.</b> A table
	/// or scene serialized without this block deserializes it with <see cref="Enabled"/> false, and
	/// a spawner whose pack is off never creates a group.
	/// </para>
	/// <para>
	/// Inline rather than a separate asset. A pack's tuning is four numbers that belong to one
	/// placement; an asset would be a second thing to author, register with the server's
	/// addressables and keep out of client groups, for no reuse anyone has asked for. The members
	/// and their roles are the spawner's own entries (<c>NPCSpawnableSettings.PackRole</c>).
	/// </para>
	/// </remarks>
	[Serializable]
	public class NPCPackSettings
	{
		/// <summary>
		/// When true, every NPC this spawner produces joins the spawner's pack.
		/// </summary>
		[Tooltip("Every NPC this spawner produces fights as one pack. Roles come from each NPC entry's Pack Role.")]
		public bool Enabled;

		/// <summary>
		/// How the pack arranges itself around the enemy it is fighting. See <see cref="PackTactic"/>.
		/// </summary>
		[Tooltip("How the pack arranges itself around the enemy it focuses, while members orbit it.")]
		public PackTactic Tactic = PackTactic.None;

		/// <summary>
		/// When true, the pack's focus follows the target of its living, fighting tank.
		/// </summary>
		[Tooltip("The pack focuses whoever its tank is fighting. DPS and Support members follow the focus.")]
		public bool FocusTargeting = true;

		/// <summary>
		/// Radius, in metres, of the ring a tactic puts its members on while they orbit.
		/// </summary>
		[Tooltip("Metres from the focused enemy at which an orbiting member takes its tactic slot.")]
		[Min(0f)]
		public float TacticOrbitRadius = 5f;

		/// <summary>
		/// Degrees per second the <see cref="PackTactic.Kite"/> ring turns.
		/// </summary>
		[Tooltip("Degrees per second the Kite tactic turns the pack's ring.")]
		public float KiteRotationSpeed = 30f;

		/// <summary>
		/// A detached copy, for a baked table that must not change when its scene is edited.
		/// </summary>
		/// <returns>The copy.</returns>
		public NPCPackSettings Clone()
		{
			return (NPCPackSettings)MemberwiseClone();
		}
	}
}
