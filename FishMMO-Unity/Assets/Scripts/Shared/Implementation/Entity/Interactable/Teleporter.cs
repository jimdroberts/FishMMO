using FishMMO.Shared.Core;
using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// Represents a teleporter interactable that can transport players to a target location.
	/// Inherits from Interactable and provides a target Transform for teleportation.
	/// </summary>
	public class Teleporter : Interactable, ITeleporter
	{
		/// <summary>
		/// The target location to which players will be teleported.
		/// </summary>
		public Transform Target;

		/// <summary>
		/// Achievement to increment when a player uses this teleporter.
		/// </summary>
		public AchievementTemplate AchievementTemplate;

		/// <inheritdoc />
		Transform ITeleporter.Target => Target;

		/// <inheritdoc />
		AchievementTemplate ITeleporter.AchievementTemplate => AchievementTemplate;

		/// <summary>
		/// Gets the display title for this teleporter, used in UI elements.
		/// </summary>
		public override string Title { get { return "Teleporter"; } }

#if UNITY_EDITOR
		/// <summary>
		/// Draws gizmos in the editor to visualize the teleporter's target location and connection.
		/// Wire cube is drawn at the target position, and a line connects the teleporter to the target.
		/// </summary>
		void OnDrawGizmos()
		{
			// Only draw gizmos if a target is assigned.
			if (Target == null)
			{
				return;
			}

			Gizmos.color = GizmoColor;
			Gizmos.DrawWireCube(Target.position, Vector3.one);
			Gizmos.DrawLine(transform.position, Target.position);
		}
#endif
	}
}