using System;
using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// A scene's baked props from one source (its scatter's trees and large props, its cliffs' rocks, its rivers'
	/// boulders): the prefabs they are made of and where each one stands. The client draws them instanced on the
	/// GPU; no prop is a scene object.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Data, not objects.</b> As scene objects a generated scene's props were hundreds of thousands of game
	/// objects, renderers and LOD groups, culled and levelled on the main thread every frame and filling the scene
	/// file. Here a prop is a prototype index and a transform (40 bytes), the prototypes are shared prefab assets,
	/// and the collision the server and client stand on is baked separately into one merged collider per chunk,
	/// saved in the scene.
	/// </para>
	/// <para>
	/// <b>Client only.</b> Referenced from a <see cref="SceneProps"/> under a <see cref="ClientOnlyObject"/>, so a
	/// server build leaves the set and its prefabs out: the server has only the chunk colliders.
	/// </para>
	/// </remarks>
	[PreferBinarySerialization]
	public sealed class ScenePropSet : ScriptableObject
	{
		/// <summary>One kind of prop: the prefab drawn (its LOD group, meshes and materials) and the layer it draws on.</summary>
		[Serializable]
		public struct Prototype
		{
			public GameObject Prefab;
			[Tooltip("The layer the prop draws on; -1 keeps each renderer's own.")]
			public int Layer;
		}

		/// <summary>One placed prop.</summary>
		[Serializable]
		public struct Prop
		{
			/// <summary>Index into <see cref="Prototypes"/>.</summary>
			public int Prototype;
			public Vector3 Position;
			public Quaternion Rotation;
			public Vector3 Scale;

			/// <summary>The prop's world transform: T·R·S, shear-free, as the instanced path requires.</summary>
			public Matrix4x4 Matrix => Matrix4x4.TRS(Position, Rotation, Scale);
		}

		[Tooltip("Where these props came from, for tools and logs (Scatter, Cliffs, Boulders).")]
		public string Source;

		public Prototype[] Prototypes = Array.Empty<Prototype>();
		public Prop[] Props = Array.Empty<Prop>();
	}
}
