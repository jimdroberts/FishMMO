using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// Marks the root a scene generator's ice placer made: every iceberg, bergy bit, growler and sea
	/// ice floe it put on a generated scene's sea is a child of the one object carrying this.
	/// </summary>
	/// <remarks>
	/// <para>
	/// The placer finds its previous work by this marker (and the root's name, "Sea Ice") and
	/// replaces it, so generating or re-cutting a scene never doubles its ice. Nothing else should
	/// carry it.
	/// </para>
	/// <para>
	/// <b>Not client-only.</b> Each piece's root carries a static mesh collider a player can stand
	/// on or swim into, so the server needs it exactly where the client has it. Only each piece's
	/// "Visual" child — its LOD group and renderers, the part that rides the waves — is a
	/// <see cref="ClientOnlyObject"/>, stripped from server builds.
	/// </para>
	/// </remarks>
	[DisallowMultipleComponent]
	public sealed class GeneratedSeaIce : MonoBehaviour
	{
	}
}
