namespace FishMMO.Server.Implementation.World.SceneServer.AI
{
	/// <summary>
	/// One brain in an <see cref="NPCGroup"/>, and the role it plays there.
	/// </summary>
	/// <remarks>
	/// A runtime record, not authored data: a pack's composition is authored on its spawner (each
	/// NPC entry's <c>PackRole</c>) and a member is added when the spawner spawns it. It used to be
	/// a serialized inspector row on a scene component, which is how groups were meant to be built
	/// before brains stopped existing on prefabs at all.
	/// </remarks>
	public readonly struct NPCGroupMember
	{
		/// <summary>
		/// The member's brain.
		/// </summary>
		public readonly AIController Controller;

		/// <summary>
		/// The member's combat role (Tank, Healer, DPS, Support, or None).
		/// </summary>
		public readonly NPCGroupRole Role;

		/// <summary>
		/// Records a member.
		/// </summary>
		/// <param name="controller">The member's brain.</param>
		/// <param name="role">The role it plays in the pack.</param>
		public NPCGroupMember(AIController controller, NPCGroupRole role)
		{
			Controller = controller;
			Role = role;
		}
	}
}
