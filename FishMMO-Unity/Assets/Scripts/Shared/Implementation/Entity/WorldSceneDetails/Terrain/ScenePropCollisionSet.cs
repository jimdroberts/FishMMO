using System;
using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// The collidable half of a scene's baked props from one source: a shared collision mesh per prototype and
	/// where each collidable prop stands. <see cref="PropColliderStreamer"/> puts colliders only where they are
	/// needed — round every character on the server, round the camera on the client — from pools, so a scene's tens
	/// of thousands of trees and rocks never all exist in physics at once.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Server and client.</b> Unlike the render set (<see cref="ScenePropSet"/>, client only) this stays in server
	/// builds: the server stands players and NPCs on the same rocks the client draws, and the client predicts its own
	/// movement against the same colliders.
	/// </para>
	/// <para>
	/// <b>Only what blocks.</b> Props too small to matter (bushes, small stones) and props whose prefab has no
	/// collider are left out at the bake; they are walked through, as the detail layers' grass and flowers are.
	/// </para>
	/// </remarks>
	[PreferBinarySerialization]
	public sealed class ScenePropCollisionSet : ScriptableObject
	{
		/// <summary>One kind of collidable prop: its collision mesh (prop space, a simplified stand-in) and its layer.</summary>
		[Serializable]
		public struct Prototype
		{
			public Mesh Mesh;
			public int Layer;
		}

		/// <summary>One collidable prop.</summary>
		[Serializable]
		public struct Instance
		{
			public int Prototype;
			public Vector3 Position;
			public Quaternion Rotation;
			public Vector3 Scale;
		}

		[Tooltip("Where these came from (Scatter, Cliffs, Boulders).")]
		public string Source;

		public Prototype[] Prototypes = Array.Empty<Prototype>();
		public Instance[] Instances = Array.Empty<Instance>();
	}
}
