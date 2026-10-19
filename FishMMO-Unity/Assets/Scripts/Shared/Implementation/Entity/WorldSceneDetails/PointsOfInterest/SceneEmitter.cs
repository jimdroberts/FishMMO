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
		/// <summary>A fire indoors (a hearth in a room): flicker and sparks, and no smoke — its smoke goes up the chimney. Its light is the room's (<see cref="Indoor"/>).</summary>
		Hearth,
		/// <summary>
		/// A lived-in room's light (its hearth and candles), at the room's middle and reaching only its walls
		/// (<see cref="SceneEmitter.Reach"/>): the outside of the walls faces away from it, so a house is not lit through them.
		/// </summary>
		Indoor,
		/// <summary>
		/// A cluster of glowing crystals deep in a cave: a cold, steady light, day and night alike (no sky reaches it), its
		/// hue from bits 8–9 of the seed (violet, blue, green, blue), reaching <see cref="SceneEmitter.Reach"/>.
		/// </summary>
		Crystal,
		/// <summary>A lantern or torch underground (a mine's lamps, a cave camp's): a warm flicker, lit day and night, reaching <see cref="SceneEmitter.Reach"/>.</summary>
		Torch,
		/// <summary>
		/// A cold glow of faerie, rune or crystal light (2026-10-18, I6g: a glowjar, a rune stone, a crystal post): a cool tint
		/// from bits 8–10 of the seed (<see cref="SceneGlow"/>: a crystal's hue, rune-light or faerie-light), a soft slow pulse
		/// on the shared world clock, faintly showing from dusk and full at night (<c>_FishGlowLights</c>), day and night alike
		/// underground. Its piece's glowing parts pulse with it (FishStructureWeathering.hlsl, a material marked _FishStructure = 3).
		/// </summary>
		Glow,
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
		/// <summary>How far its light reaches, metres; 0 for its kind's own.</summary>
		public float Reach;
	}

	/// <summary>
	/// One room of a site's buildings as a box, baked by the cut (PointOfInterestInteriors): inside it the sky's light only
	/// comes through the windows and doors, so the client darkens what is drawn in it (IndoorRooms, FishIndoor.hlsl).
	/// </summary>
	[Serializable]
	public struct SceneRoom
	{
		/// <summary>The box's middle, scene metres.</summary>
		public Vector3 Centre;
		/// <summary>Its heading, degrees about +y.</summary>
		public float Yaw;
		/// <summary>Half its width, height and depth, metres (inside the walls, under the ceiling).</summary>
		public Vector3 HalfSize;
	}
}
