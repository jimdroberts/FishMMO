using System;
using FishMMO.Core;

[assembly: GameModule(typeof(FishMMO.Shared.NpcCorpseModule))]

namespace FishMMO.Shared
{
	/// <summary>
	/// NPC corpse decay as a module: on the server, a dead NPC's body stays for its decay time (less once it
	/// is empty) and then returns to its pool. Removing this module's declaration sends corpses back to the
	/// per-NPC tick they used before.
	/// </summary>
	public sealed class NpcCorpseModule : GameModule
	{
		/// <summary>The module's id.</summary>
		public const string ModuleId = "npc.corpses";

		/// <inheritdoc />
		public override string Id => ModuleId;

		/// <inheritdoc />
		public override void Install(ModuleBuilder builder)
		{
			builder.AddSystem<CorpseDecaySystem>(SimulationSide.Server);
			builder.AddSystem<CorpseExpirySystem>(SimulationSide.Server);
		}
	}

	/// <summary>
	/// A corpse waiting to decay, as a Core entity: the NPC it belongs to, beside a <see cref="Lifetime"/> that
	/// ends when the body should go. Only corpses have one, so only corpses cost anything per tick.
	/// </summary>
	public struct CorpseDecay
	{
		/// <summary>The dead NPC.</summary>
		public NPC Npc;

		/// <summary>True once the decay was cut short because the corpse was found empty.</summary>
		public bool Shortened;
	}

	/// <summary>
	/// Cuts a corpse's decay short the first tick it is found empty, to the NPC's
	/// <see cref="NPC.EmptyCorpseDecayDuration"/> (shortened, never lengthened), and drops corpses whose NPC has
	/// gone or come back. Runs before <see cref="LifetimeSystem"/>, so an empty corpse whose new end has passed
	/// goes in the same tick.
	/// </summary>
	/// <remarks>
	/// Checked here on the tick, not where loot is taken: every way a corpse empties (an item, the currency,
	/// everything, a roll that produced nothing, never lootable at all) is covered by asking.
	/// </remarks>
	public sealed class CorpseDecaySystem : GameSystem
	{
		/// <inheritdoc />
		public override SystemPhase Phase => SystemPhase.Death;

		/// <inheritdoc />
		public override int Order => -100;

		/// <inheritdoc />
		public override void Update(World world, in TickContext tick)
		{
			ComponentStore<CorpseDecay> corpses = world.Store<CorpseDecay>();
			ComponentStore<Lifetime> lifetimes = world.Store<Lifetime>();
			for (int i = 0; i < corpses.Count; i++)
			{
				ref CorpseDecay corpse = ref corpses.At(i);
				Entity entity = corpses.EntityAt(i);
				NPC npc = corpse.Npc;
				if (npc == null || !npc.IsCorpse)
				{
					world.Commands.Destroy(entity);
					continue;
				}
				if (corpse.Shortened || npc.HasLoot)
				{
					continue;
				}
				corpse.Shortened = true;
				if (lifetimes.TryGetIndex(entity, out int at))
				{
					double emptyEnd = tick.Time + Math.Max(0f, npc.EmptyCorpseDecayDuration);
					ref Lifetime lifetime = ref lifetimes.At(at);
					if (lifetime.EndsAt > emptyEnd)
					{
						lifetime.EndsAt = emptyEnd;
					}
				}
			}
		}
	}

	/// <summary>
	/// Returns each corpse whose decay ran out to its pool. Reads <see cref="Expired"/> after
	/// <see cref="LifetimeSystem"/> raises it, in the same phase, while the entity's components can still be read.
	/// </summary>
	public sealed class CorpseExpirySystem : GameSystem
	{
		/// <inheritdoc />
		public override SystemPhase Phase => SystemPhase.Death;

		/// <inheritdoc />
		public override int Order => 100;

		/// <inheritdoc />
		public override void Update(World world, in TickContext tick)
		{
			ReadOnlySpan<Expired> ended = world.Events<Expired>().Current;
			ComponentStore<CorpseDecay> corpses = world.Store<CorpseDecay>();
			for (int i = 0; i < ended.Length; i++)
			{
				// Looked up afresh each time: returning one corpse to its pool destroys its entity at once.
				if (!corpses.TryGetIndex(ended[i].Entity, out int at))
				{
					continue;
				}
				NPC npc = corpses.At(at).Npc;
				if (npc != null && npc.IsCorpse)
				{
					npc.ReturnToPool();
				}
			}
		}
	}
}
