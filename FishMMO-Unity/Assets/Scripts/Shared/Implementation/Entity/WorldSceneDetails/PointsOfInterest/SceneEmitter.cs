using System;
using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>What a site's piece gives off where it stands (StructurePieces.EmittersOf).</summary>
	/// <remarks>Append only; the ordinal is stored in scene assets.</remarks>
	public enum SceneEmitterKind : byte
	{
		/// <summary>A chimney's top: hearth smoke.</summary>
		Smoke,
		/// <summary>An open fire's bed: firelight, flicker and smoke.</summary>
		Fire,
		/// <summary>A forge's hearth: a hot, low glow.</summary>
		Forge,
		/// <summary>A lantern's pane: a steady warm light at night.</summary>
		Lamp,
	}

	/// <summary>
	/// One place in a scene that gives off smoke or light, baked by the cut from the pieces its sites laid: the client's
	/// site lights and hearth smoke read these, nothing else (SceneSiteEmitters).
	/// </summary>
	[Serializable]
	public struct SceneEmitter
	{
		public Vector3 Position;
		public SceneEmitterKind Kind;
		/// <summary>A stable number for its own look (which chimneys smoke, how a fire flickers).</summary>
		public uint Seed;
	}
}
