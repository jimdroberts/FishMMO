using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// Marks the root a scene generator's cliff placer made: every cliff-face piece in the scene is
	/// a child of the one object carrying this.
	/// </summary>
	/// <remarks>
	/// <para>
	/// The placer finds its previous work by this marker (and the root's name) and replaces it, so
	/// re-painting a scene's biomes never doubles its cliffs. Nothing else should carry it.
	/// </para>
	/// <para>
	/// <b>Not client-only.</b> Cliff pieces stand up to two metres proud of the terrain, with real
	/// overhangs, and each piece object carries a mesh collider on the Ground layer, so the server
	/// needs them exactly as the client has them. Only each piece's "Visual" child — its LOD group
	/// and renderers — is a <see cref="ClientOnlyObject"/>, stripped from server builds.
	/// </para>
	/// </remarks>
	[DisallowMultipleComponent]
	public sealed class GeneratedCliffs : MonoBehaviour
	{
	}
}
