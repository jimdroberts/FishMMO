using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// Marks a generated spawner the generator left empty ON PURPOSE, for a designer to fill: a world boss lair's boss
	/// (Jim, 2026-10-10: the designer assigns the boss), or a role no NPC exists for yet.
	/// </summary>
	/// <remarks>
	/// An empty spawner is otherwise a defect the spawn-table checks fail on, because it stands in the scene and never
	/// spawns without a word. This says the emptiness is known and why, so those checks can list it as work to do
	/// instead. It sits on the spawner's EditorOnly object and so never reaches a build. Remove it once filled.
	/// </remarks>
	[DisallowMultipleComponent]
	public class PendingSpawnerContent : MonoBehaviour
	{
		[TextArea(1, 3)]
		public string Reason;
	}
}
