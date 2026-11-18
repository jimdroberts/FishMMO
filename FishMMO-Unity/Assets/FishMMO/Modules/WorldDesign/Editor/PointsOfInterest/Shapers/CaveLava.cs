#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>What molten lava a cave holds: a river down a tube's floor, a lake or pit in a chamber, a fall between levels.</summary>
	public enum CaveLavaKind : byte
	{
		River = 0,
		Lake = 1,
		Fall = 2,
	}

	/// <summary>One body of molten lava in a cave system, in the system's frame.</summary>
	public struct CaveLavaBody
	{
		public CaveLavaKind Kind;
		/// <summary>
		/// The body's centre line, downstream, each point's y the molten surface there (system frame): a river's or a fall's
		/// run (a fall's first point at its lip, its last where it lands), a lake's one point at its centre.
		/// </summary>
		public Vector3[] Path;
		/// <summary>A river's or a fall's width across, a lake's radius, metres.</summary>
		public float Width;
		/// <summary>
		/// The walking floor beside each point of <see cref="Path"/> (system frame y, as the field cuts it; a lake's one value the
		/// lowest floor round its rim), where the layout cut the body's bed into the rock below it: a river's channel, a lake's
		/// basin. Null where it cut none: a fall (it pours down the rock as it stands), a lake in a pit the variety pass had
		/// already sunk (<see cref="Pit"/>).
		/// </summary>
		public float[] Banks;
		/// <summary>How high the bed is cut at each point of <see cref="Path"/> (over the highest floor across it), where <see cref="Banks"/> is set.</summary>
		internal float[] Tops;
		/// <summary>The passage a river runs along, or a fall pours into the head of (index in <see cref="CaveSystem.Passages"/>); −1 for none.</summary>
		public int Passage;
		/// <summary>The chamber a lake lies in (index in <see cref="CaveSystem.Chambers"/>); −1 for none.</summary>
		public int Chamber;
		/// <summary>The pit a lake fills (index in <see cref="CaveSystem.Pits"/>, a basin the variety pass sank); −1 for none.</summary>
		public int Pit;
	}

	/// <summary>A cave system's lava: whether it still runs molten, and its bodies.</summary>
	public sealed class CaveLavaPlan
	{
		/// <summary>A cooled system: no molten lava at all (as every lava tube was before 2026-10-20).</summary>
		public static readonly CaveLavaPlan Cooled = new CaveLavaPlan();

		/// <summary>Whether the system still runs molten.</summary>
		public bool Active;
		public readonly List<CaveLavaBody> Bodies = new List<CaveLavaBody>();
	}

	/// <summary>
	/// Molten lava in the caves (2026-10-20: "Lava caves/tunnels/tubes should have more lava? lava pits? magma? etc?"): which
	/// systems still run molten (most lava tubes, some cooled) and where the lava lies in them.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>A pure function of the system.</b> <see cref="Plan"/> reads only the system (its form, seed, passages, chambers,
	/// features), so the shaper that carves the channels and writes the lava into the scene's hydrology and the dresser that
	/// lights and dresses it work out the same plan without anything new saved in the cave's values. It reads the system's
	/// shapes and its floors as the field cuts them WITHOUT the lava (a tube's walked floor lies up to a metre and a half
	/// under its nodes' own): the field's channels and basins are cut from the plan (CaveSystem.Lava.cs), never read by it.
	/// </para>
	/// <para>
	/// <b>Never in the way.</b> A river runs in a channel cut along one wall of its passage, under the floor beside it, so the
	/// way down the passage's middle stays a dry bench; where any other way, a chamber's middle, a climb, a bridge, a chasm,
	/// a pit or anything else the layout laid comes near, the channel is left uncut (the rock stands over the lava there as
	/// a natural bridge) and the river is two bodies, sinking under it and welling up past it. Lakes lie in chambers off every
	/// way, with floor all round them. Nothing molten lies near a mouth, nor over any way another level down (the scene's
	/// water is read in plan: the highest surface over a point answers).
	/// </para>
	/// <para>
	/// <b>Falls.</b> Each river's head is fed by a fall from a vent in its wall; a chasm's free end (away from the way on
	/// from its floor) takes a fall from the river ending in its chamber, or from a vent in its own wall, into a plunge pool.
	/// A fall reaches forward at least half as far as it drops (as the inland water draws one).
	/// </para>
	/// </remarks>
	public static class CaveLava
	{
		/// <summary>The share of lava tubes that still run molten (the rest cooled, as before).</summary>
		public const float ActiveShare = 0.7f;
		/// <summary>How far the molten surface lies under the walking floor beside it, metres.</summary>
		public const float Drop = 1f;
		/// <summary>How deep the molten lava stands in a channel or a basin the layout cut, metres (its bed this far under its surface).</summary>
		public const float Depth = 1.2f;
		/// <summary>How far under its bank a river's surface may sink where its passage's floor rises before it, metres; past it, it runs on under the rock.</summary>
		public const float MaxSink = 2.2f;
		/// <summary>The narrowest channel cut (the mesher's cells are up to a metre), metres.</summary>
		public const float MinWidth = 1.2f;
		/// <summary>
		/// How far a channel's near edge keeps from its passage's centre line (the lane every way is walked on and kept clear along,
		/// and past a room's kept middle) where the bore is wide enough, metres.
		/// </summary>
		public const float LaneClear = CaveSystem.WayLane + 0.65f;
		/// <summary>The nearest a narrow tube's channel comes to its centre line (no nearer than the lane and a walker's shoulder), metres.</summary>
		public const float LaneClearLeast = CaveSystem.WayLane + 0.35f;
		/// <summary>
		/// The most bodies a system's plan holds (2026-10-21: 16 before, filled by a long tube's rivers before any lake; the scene's
		/// water ids keep eight bits for a body, CaveLavaWater).
		/// </summary>
		public const int MaxBodies = 64;

		/// <summary>Whether a system still runs molten: about <see cref="ActiveShare"/> of lava tubes, by their seed.</summary>
		public static bool Molten(CaveSystem system) => system != null && system.Form == CaveForm.LavaTube && CaveSystem.LavaRoll(system.Seed) < ActiveShare;

		/// <summary>The system's lava; <see cref="CaveLavaPlan.Cooled"/> where it holds none.</summary>
		public static CaveLavaPlan Plan(CaveSystem system) => Molten(system) ? system.PlanLava() : CaveLavaPlan.Cooled;
	}
}
#endif
