using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// The main object of a scene's baked binary asset (its NavMesh, for one): the data it holds are its sub-assets,
	/// which the scene references. Binary, because the project serializes assets as text, which writes binary data
	/// as hex at twice its size: a generated scene's NavMesh is tens of megabytes of it.
	/// </summary>
	[PreferBinarySerialization]
	public sealed class BakedSceneData : ScriptableObject
	{
		[Tooltip("What the asset holds, for tools and logs.")]
		public string What;
	}
}
