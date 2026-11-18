namespace FishMMO.Core
{
	/// <summary>
	/// Recalculates every entity's stats from the modifier sets aimed at it: value = (base + every Add) ×
	/// (1 + every Multiply), and a resource's current amount is held within its new value.
	/// </summary>
	/// <remarks>
	/// One pass over the stats and one over the modifier sets, whatever their number. A modifier for a stat its
	/// target does not have, or aimed at an entity that no longer exists, changes nothing.
	/// </remarks>
	public sealed class StatSystem : GameSystem
	{
		/// <inheritdoc />
		public override SystemPhase Phase => SystemPhase.Stats;

		/// <inheritdoc />
		public override void Update(World world, in TickContext tick)
		{
			StatPool pool = StatPool.Of(world);
			StatEntry[] entries = pool.Entries;
			int[] starts = pool.Starts;
			int[] counts = pool.Counts;
			int[] generations = pool.Generations;
			ComponentStore<Stats> stats = world.Store<Stats>();

			for (int i = 0; i < stats.Count; i++)
			{
				ref Stats owner = ref stats.At(i);
				if (generations[owner.Slot] != owner.Generation)
				{
					continue;   // a stale copy; a default Stats names slot 0, which is always empty
				}
				int end = starts[owner.Slot] + counts[owner.Slot];
				for (int s = starts[owner.Slot]; s < end; s++)
				{
					entries[s].AddSum = 0f;
					entries[s].MultiplySum = 0f;
				}
			}

			ComponentStore<ModifierSet> sets = world.Store<ModifierSet>();
			for (int i = 0; i < sets.Count; i++)
			{
				ref ModifierSet set = ref sets.At(i);
				if (set.Modifiers == null || !stats.TryGetIndex(set.Target, out int targetIndex))
				{
					continue;
				}
				ref Stats target = ref stats.At(targetIndex);
				if (generations[target.Slot] != target.Generation)
				{
					continue;
				}
				for (int m = 0; m < set.Modifiers.Length; m++)
				{
					ref StatModifier modifier = ref set.Modifiers[m];
					int at = pool.Find(target.Slot, modifier.Stat, ref modifier.Hint);
					if (at < 0)
					{
						continue;
					}
					if (modifier.Op == ModifierOp.Add)
					{
						entries[at].AddSum += modifier.Amount;
					}
					else
					{
						entries[at].MultiplySum += modifier.Amount;
					}
				}
			}

			for (int i = 0; i < stats.Count; i++)
			{
				ref Stats owner = ref stats.At(i);
				if (generations[owner.Slot] != owner.Generation)
				{
					continue;   // a stale copy; a default Stats names slot 0, which is always empty
				}
				int end = starts[owner.Slot] + counts[owner.Slot];
				for (int s = starts[owner.Slot]; s < end; s++)
				{
					ref StatEntry entry = ref entries[s];
					entry.Value = (entry.Base + entry.AddSum) * (1f + entry.MultiplySum);
					if (entry.IsResource && entry.Current > entry.Value)
					{
						entry.Current = entry.Value < 0f ? 0f : entry.Value;
					}
				}
			}
		}
	}

	/// <summary>When an entity ends: a timed modifier's expiry, a corpse's decay, a projectile's range.</summary>
	public struct Lifetime
	{
		/// <summary>Simulation time (<see cref="TickContext.Time"/>) at which the entity ends.</summary>
		public double EndsAt;
	}

	/// <summary>Raised when an entity's <see cref="Lifetime"/> runs out, in the tick it is destroyed.</summary>
	public readonly struct Expired
	{
		/// <summary>The entity that ended. Destroyed when the phase ends; read its components before then.</summary>
		public readonly Entity Entity;

		/// <summary>Builds the event.</summary>
		public Expired(Entity entity)
		{
			Entity = entity;
		}
	}

	/// <summary>
	/// Ends every entity whose <see cref="Lifetime"/> has run out: raises <see cref="Expired"/> and destroys it
	/// when the Death phase ends. Systems that must act on an ending (despawning a corpse's network object,
	/// telling a client its buff is gone) read the event in the same phase, at a higher order.
	/// </summary>
	public sealed class LifetimeSystem : GameSystem
	{
		/// <inheritdoc />
		public override SystemPhase Phase => SystemPhase.Death;

		/// <inheritdoc />
		public override void Update(World world, in TickContext tick)
		{
			ComponentStore<Lifetime> lifetimes = world.Store<Lifetime>();
			EventStream<Expired> expired = world.Events<Expired>();
			for (int i = 0; i < lifetimes.Count; i++)
			{
				if (tick.Time >= lifetimes.At(i).EndsAt)
				{
					Entity entity = lifetimes.EntityAt(i);
					expired.Raise(new Expired(entity));
					world.Commands.Destroy(entity);
				}
			}
		}
	}

	/// <summary>One resource that refills on its own, at a rate that is itself a stat (so modifiers can change it).</summary>
	public struct RegenerationRate
	{
		internal int ResourceHint;
		internal int RateHint;

		/// <summary>The resource refilled.</summary>
		public readonly StatId Resource;

		/// <summary>The stat holding how much it refills per second.</summary>
		public readonly StatId PerSecond;

		/// <summary>Builds a rate.</summary>
		public RegenerationRate(StatId resource, StatId perSecond)
		{
			Resource = resource;
			PerSecond = perSecond;
			ResourceHint = 0;
			RateHint = 0;
		}
	}

	/// <summary>The resources an entity refills on its own.</summary>
	public struct Regeneration
	{
		/// <summary>The rates. Shared, not copied: build a new array rather than editing one in use.</summary>
		public RegenerationRate[] Rates;
	}

	/// <summary>
	/// Refills every entity's regenerating resources each tick, by its rate stat times the tick's length, held
	/// at the resource's value. After <see cref="StatSystem"/>, so this tick's modifiers apply.
	/// </summary>
	public sealed class RegenerationSystem : GameSystem
	{
		/// <inheritdoc />
		public override SystemPhase Phase => SystemPhase.Stats;

		/// <inheritdoc />
		public override int Order => 100;

		/// <inheritdoc />
		public override void Update(World world, in TickContext tick)
		{
			StatPool pool = StatPool.Of(world);
			StatEntry[] entries = pool.Entries;
			ComponentStore<Regeneration> regenerations = world.Store<Regeneration>();
			ComponentStore<Stats> stats = world.Store<Stats>();
			float dt = tick.DeltaTime;
			for (int i = 0; i < regenerations.Count; i++)
			{
				RegenerationRate[] rates = regenerations.At(i).Rates;
				if (rates == null || !stats.TryGetIndex(regenerations.EntityAt(i), out int statsIndex))
				{
					continue;
				}
				ref Stats owner = ref stats.At(statsIndex);
				if (pool.Generations[owner.Slot] != owner.Generation)
				{
					continue;
				}
				for (int r = 0; r < rates.Length; r++)
				{
					ref RegenerationRate rateOf = ref rates[r];
					int resource = pool.Find(owner.Slot, rateOf.Resource, ref rateOf.ResourceHint);
					if (resource < 0)
					{
						continue;
					}
					ref StatEntry entry = ref entries[resource];
					if (!entry.IsResource || entry.Current >= entry.Value)
					{
						continue;
					}
					int rate = pool.Find(owner.Slot, rateOf.PerSecond, ref rateOf.RateHint);
					if (rate < 0)
					{
						continue;
					}
					entry.Current = StatTable.Clamp(entry.Current + entries[rate].Value * dt, 0f, entry.Value);
				}
			}
		}
	}
}
