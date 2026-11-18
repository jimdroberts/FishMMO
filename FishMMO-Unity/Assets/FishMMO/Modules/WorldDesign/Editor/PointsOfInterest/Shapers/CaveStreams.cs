#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>What water a cave holds: a stream down a passage's floor (its falls part of it), or a still pool.</summary>
	public enum CaveStreamKind : byte
	{
		Stream = 0,
		Pool = 1,
	}

	/// <summary>One body of water in a cave system, in the system's frame.</summary>
	public struct CaveStreamBody
	{
		public CaveStreamKind Kind;
		/// <summary>
		/// A stream's line, downstream, each point's y its water's surface there (system frame): a spring's crack in the wall,
		/// then the water down its channel; a pool's one point at its middle. In a <see cref="CaveStreamPlan.Frozen"/> plan, the
		/// floor the ice lies flush on instead.
		/// </summary>
		public Vector3[] Path;
		/// <summary>
		/// Per point of a stream's <see cref="Path"/>, whether the water reaches it falling from the point before (a fall: a spring
		/// out of a wall, over a chasm's lip, down from a roof's aven); null where it never falls.
		/// </summary>
		public bool[] Falls;
		/// <summary>A stream's width across, a pool's radius, metres.</summary>
		public float Width;
		/// <summary>How deep the water stands over its bed (a stream's in its channel, a pool's at its middle), metres.</summary>
		public float Depth;
		/// <summary>
		/// The walking floor beside each point of <see cref="Path"/> (system frame y, as the field cuts it without the water; a
		/// pool's one value the lowest floor round its rim), where the layout cut the water's bed into the rock: a stream's
		/// channel, a pool's basin. NaN at a point it cut nothing for (a fall's points); null where it cut nothing at all (a
		/// pool in a basin the variety pass sank, a flooded winze, a flooded dead end).
		/// </summary>
		public float[] Banks;
		/// <summary>How high the bed is cut at each point of <see cref="Path"/> (over the highest floor across it), where <see cref="Banks"/> is set.</summary>
		internal float[] Tops;
		/// <summary>The passage a stream runs along (index in <see cref="CaveSystem.Passages"/>); −1 for none.</summary>
		public int Passage;
		/// <summary>The chamber a pool lies in, or a stream's fall pours into (index in <see cref="CaveSystem.Chambers"/>); −1 for none.</summary>
		public int Chamber;
		/// <summary>The pit a pool fills (index in <see cref="CaveSystem.Pits"/>: a basin, a gour, a winze); −1 for none.</summary>
		public int Pit;
		/// <summary>The pool a stream runs into (index in the plan's bodies); −1 where it sinks into its bed.</summary>
		public int Into;

		/// <summary>Whether any of a stream's points is reached falling.</summary>
		public bool HasFall
		{
			get
			{
				if (Falls == null)
				{
					return false;
				}
				foreach (bool f in Falls)
				{
					if (f)
					{
						return true;
					}
				}
				return false;
			}
		}
	}

	/// <summary>A cave system's water: whether it has any, whether it is frozen, and its bodies.</summary>
	public sealed class CaveStreamPlan
	{
		/// <summary>A dry system: no water at all (as every cave but a grotto's pool was before 2026-10-20).</summary>
		public static readonly CaveStreamPlan Dry = new CaveStreamPlan();

		/// <summary>Whether the system holds water (frozen or not).</summary>
		public bool Active;
		/// <summary>
		/// An ice cave's: where its water would run, frozen. Nothing is cut for it and nothing is written into the scene's
		/// water; each body's <see cref="CaveStreamBody.Path"/> lies on the walking floor (the ice is dressed flush on it).
		/// </summary>
		public bool Frozen;
		public readonly List<CaveStreamBody> Bodies = new List<CaveStreamBody>();
	}

	/// <summary>
	/// Water in the caves (2026-10-20, caves livened: "underground streams & waterfalls & pools"): which systems hold water and
	/// where it lies in them — streams down passage floors fed by springs pouring out of the walls, falls over chasms' lips and
	/// down from the roofs' avens into plunge pools, still pools in chambers, water standing in the basins and rimstone gours
	/// the layout sank, a mine's flooded winzes, and the flooded dead ends of passages (sumps).
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>A pure function of the system,</b> as the lava is (<see cref="CaveLava"/>): <see cref="Plan"/> reads only the system,
	/// so the shaper that cuts the water's beds and writes it into the scene's hydrology (CaveStreamWater) and the dresser that
	/// dresses its banks work out the same plan; nothing new is saved in the cave's values. It measures the floors without the
	/// water's beds (CaveSystem.Streams.cs), which are cut once the system stands.
	/// </para>
	/// <para>
	/// <b>Never in the way.</b> The rules the lava keeps (the same ways, kept places and features, CaveSystem.Lava.cs): a
	/// stream's channel runs along one wall of its passage under the bench beside it, uncut where any way comes near (it runs
	/// on under the rock there and wells up past it); pools lie off every way with floor all round them; nothing lies near a
	/// mouth, nor over a way another level down (the scene's water is read in plan: a passage under a pool would read as
	/// drowned). Unlike lava, water is waded and swum: a stream is knee deep, a pool deeper at its middle.
	/// </para>
	/// <para>
	/// <b>Never with lava.</b> A lava tube that still runs molten has no water at all; a cooled one only a little. An ice cave's
	/// water is frozen (<see cref="CaveStreamPlan.Frozen"/>), dressed as ice by its own pass, never cut nor written as water.
	/// </para>
	/// </remarks>
	public static class CaveStreams
	{
		/// <summary>How far the water's surface lies under the walking floor beside a stream, metres (a bench to step down from; the ribbon past its banks stays under rock).</summary>
		public const float Drop = 0.5f;
		/// <summary>How far a pool's surface lies under the lowest of its rim, metres.</summary>
		public const float PoolDrop = 0.35f;
		/// <summary>How deep a stream runs over its bed, metres: knee deep, waded.</summary>
		public const float StreamDepth = 0.5f;
		/// <summary>How deep a pool stands at its middle, metres (least and most): swum.</summary>
		public const float PoolDepthMin = 1.1f, PoolDepthMax = 1.9f;
		/// <summary>How far under its bank a stream's surface may sink where its passage's floor rises before it, metres; past it, it runs on under the rock.</summary>
		public const float MaxSink = 1.6f;
		/// <summary>The narrowest and the widest channel cut, metres.</summary>
		public const float MinWidth = 1f, MaxWidth = 2.4f;
		/// <summary>The least drop drawn as a fall (the inland water's MinFallMetres, with a little to spare), metres.</summary>
		public const float MinFall = 1.8f;
		/// <summary>The most bodies a system's plan holds (the scene's water ids keep four bits for a body, the grotto's own pool the zero: CaveStreamWater).</summary>
		public const int MaxBodies = 15;

		/// <summary>The share of each form's systems that hold water (an ice cave's frozen), by the seed; a molten lava tube never.</summary>
		public static float WetShare(CaveForm form)
		{
			switch (form)
			{
				case CaveForm.Cave: return 0.7f;
				case CaveForm.Grotto: return 0.8f;
				case CaveForm.IceCave: return 0.6f;
				case CaveForm.LavaTube: return 0.25f;
				case CaveForm.Mine: return 0.6f;
				default: return 0f;
			}
		}

		/// <summary>Whether a system holds water (an ice cave's frozen): about <see cref="WetShare"/> of its form, by its seed; never a molten lava tube.</summary>
		public static bool Wet(CaveSystem system)
		{
			return system != null && !CaveLava.Molten(system) && CaveSystem.StreamRoll(system.Seed) < WetShare(system.Form);
		}

		/// <summary>Whether a system's water is frozen (an ice cave's).</summary>
		public static bool Frozen(CaveSystem system) => system != null && system.Form == CaveForm.IceCave;

		/// <summary>The system's water; <see cref="CaveStreamPlan.Dry"/> where it holds none.</summary>
		public static CaveStreamPlan Plan(CaveSystem system) => Wet(system) ? system.PlanStreams(Frozen(system)) : CaveStreamPlan.Dry;
	}
}
#endif
