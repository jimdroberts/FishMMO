using System;
using System.Collections.Generic;

namespace FishMMO.Core
{
	/// <summary>An effect's kind (damage, heal, apply modifiers, teleport), registered by name.</summary>
	public readonly struct EffectKind : IEquatable<EffectKind>
	{
		/// <summary>The registered number; 0 is no kind.</summary>
		public readonly int Value;

		internal EffectKind(int value)
		{
			Value = value;
		}

		/// <summary>The kind named <paramref name="name"/>, registered on first use.</summary>
		public static EffectKind Of(string name) => EffectKindRegistry.Get(name);

		/// <inheritdoc />
		public bool Equals(EffectKind other) => Value == other.Value;

		/// <inheritdoc />
		public override bool Equals(object obj) => obj is EffectKind other && Equals(other);

		/// <inheritdoc />
		public override int GetHashCode() => Value;

		/// <inheritdoc />
		public override string ToString() => EffectKindRegistry.NameOf(this);
	}

	/// <summary>Effect kind names to numbers, for the whole process.</summary>
	public static class EffectKindRegistry
	{
		private static readonly object gate = new object();
		private static readonly Dictionary<string, int> byName = new Dictionary<string, int>(StringComparer.Ordinal);
		private static readonly List<string> names = new List<string> { "(none)" };

		/// <summary>The kind named <paramref name="name"/>, registered on first use.</summary>
		public static EffectKind Get(string name)
		{
			if (string.IsNullOrEmpty(name))
			{
				throw new ArgumentException("An effect kind needs a name.", nameof(name));
			}
			lock (gate)
			{
				if (!byName.TryGetValue(name, out int id))
				{
					id = names.Count;
					names.Add(name);
					byName[name] = id;
				}
				return new EffectKind(id);
			}
		}

		/// <summary>The name <paramref name="kind"/> was registered with.</summary>
		public static string NameOf(EffectKind kind)
		{
			lock (gate)
			{
				return kind.Value >= 0 && kind.Value < names.Count ? names[kind.Value] : $"(effect {kind.Value})";
			}
		}
	}

	/// <summary>
	/// One thing done to an entity: take 20 health, apply a slow for 4 seconds. Data, not code: content names a
	/// kind and fills the fields that kind reads, and the module that owns the kind handles it.
	/// </summary>
	public struct Effect
	{
		/// <summary>What is done.</summary>
		public EffectKind Kind;

		/// <summary>Who did it (for threat, kill credit, logs); <see cref="Entity.Null"/> for the world.</summary>
		public Entity Source;

		/// <summary>Who it is done to.</summary>
		public Entity Target;

		/// <summary>The stat it reads or changes, for kinds that name one.</summary>
		public StatId Stat;

		/// <summary>How much, for kinds that take an amount.</summary>
		public float Amount;

		/// <summary>How long it lasts in seconds, for kinds that last; 0 or less is until removed.</summary>
		public double Duration;

		/// <summary>The modifiers it applies, for kinds that apply them. Shared, not copied.</summary>
		public StatModifier[] Modifiers;
	}

	/// <summary>Handles one kind of effect. May change the world at once, queue commands, raise events and queue further effects.</summary>
	public delegate void EffectHandler(World world, in Effect effect, in TickContext tick);

	/// <summary>
	/// The world's effects waiting to happen and the handlers that make them happen. Anything may queue an
	/// effect at any time; <see cref="EffectSystem"/> applies them in the Effects phase, in the order queued.
	/// </summary>
	public sealed class EffectQueue
	{
		private Effect[] pending = new Effect[64];
		private int count;
		private EffectHandler[] handlers = new EffectHandler[16];

		/// <summary>How many effects wait.</summary>
		public int Count => count;

		/// <summary>Effects dropped because no handler was registered for their kind, since the world began.</summary>
		public long Unhandled { get; internal set; }

		/// <summary>Effects dropped because one tick's effects kept queueing more past <see cref="EffectSystem.MaxPerTick"/>.</summary>
		public long Overflowed { get; internal set; }

		/// <summary>Queues an effect.</summary>
		public void Enqueue(in Effect effect)
		{
			if (count == pending.Length)
			{
				Array.Resize(ref pending, count * 2);
			}
			pending[count++] = effect;
		}

		/// <summary>Makes <paramref name="handler"/> the one that handles <paramref name="kind"/>, replacing any before it.</summary>
		public void Register(EffectKind kind, EffectHandler handler)
		{
			if (kind.Value <= 0)
			{
				throw new ArgumentException("No effect kind.", nameof(kind));
			}
			if (kind.Value >= handlers.Length)
			{
				Array.Resize(ref handlers, Math.Max(handlers.Length * 2, kind.Value + 1));
			}
			handlers[kind.Value] = handler;
		}

		/// <summary>Whether <paramref name="kind"/> has a handler.</summary>
		public bool IsHandled(EffectKind kind) => HandlerFor(kind) != null;

		internal EffectHandler HandlerFor(EffectKind kind) =>
			kind.Value > 0 && kind.Value < handlers.Length ? handlers[kind.Value] : null;

		internal ref Effect At(int index) => ref pending[index];

		internal void Clear()
		{
			Array.Clear(pending, 0, count);   // drop references to modifier arrays
			count = 0;
		}
	}

	/// <summary>Raised when an effect changes how much of a resource an entity has.</summary>
	public readonly struct ResourceChanged
	{
		/// <summary>Who changed it.</summary>
		public readonly Entity Source;

		/// <summary>Whose resource changed.</summary>
		public readonly Entity Target;

		/// <summary>Which resource.</summary>
		public readonly StatId Stat;

		/// <summary>The change actually made, after holding the resource between 0 and its value.</summary>
		public readonly float Change;

		/// <summary>How much is left.</summary>
		public readonly float Current;

		/// <summary>Builds the event.</summary>
		public ResourceChanged(Entity source, Entity target, StatId stat, float change, float current)
		{
			Source = source;
			Target = target;
			Stat = stat;
			Change = change;
			Current = current;
		}
	}

	/// <summary>The effect kinds Core handles itself.</summary>
	public static class CoreEffects
	{
		/// <summary>Adds <see cref="Effect.Amount"/> (negative to take) to the target's resource <see cref="Effect.Stat"/>.</summary>
		public static readonly EffectKind ChangeResource = EffectKind.Of("core.change-resource");

		/// <summary>
		/// Applies <see cref="Effect.Modifiers"/> to the target as a new modifier-set entity, ending after
		/// <see cref="Effect.Duration"/> seconds when that is above 0.
		/// </summary>
		public static readonly EffectKind ApplyModifiers = EffectKind.Of("core.apply-modifiers");

		/// <summary>Registers Core's handlers on <paramref name="queue"/>.</summary>
		public static void Register(EffectQueue queue)
		{
			queue.Register(ChangeResource, HandleChangeResource);
			queue.Register(ApplyModifiers, HandleApplyModifiers);
		}

		private static void HandleChangeResource(World world, in Effect effect, in TickContext tick)
		{
			if (!world.IsAlive(effect.Target) || !world.TryStatsOf(effect.Target, out StatTable table))
			{
				return;
			}
			float change = table.ChangeCurrent(effect.Stat, effect.Amount);
			if (change != 0f)
			{
				world.Events<ResourceChanged>().Raise(new ResourceChanged(effect.Source, effect.Target, effect.Stat, change, table.Current(effect.Stat)));
			}
		}

		private static void HandleApplyModifiers(World world, in Effect effect, in TickContext tick)
		{
			if (effect.Modifiers == null || effect.Modifiers.Length == 0 || !world.IsAlive(effect.Target))
			{
				return;
			}
			Entity set = world.Create();
			world.Commands.Add(set, new ModifierSet { Target = effect.Target, Modifiers = effect.Modifiers });
			if (effect.Duration > 0d)
			{
				world.Commands.Add(set, new Lifetime { EndsAt = tick.Time + effect.Duration });
			}
		}
	}

	/// <summary>
	/// Applies the world's queued effects (<see cref="EffectQueue"/>) in the Effects phase, in the order
	/// queued. An effect whose kind has no handler is counted (<see cref="EffectQueue.Unhandled"/>) and
	/// dropped: content naming a module that is not installed does nothing, rather than breaking the tick.
	/// </summary>
	public sealed class EffectSystem : GameSystem
	{
		/// <summary>
		/// The most effects one tick applies, counting effects that queue further effects. Past it the rest
		/// are dropped and counted (<see cref="EffectQueue.Overflowed"/>), so a pair of effects that trigger
		/// each other ends instead of hanging the server.
		/// </summary>
		public int MaxPerTick { get; set; } = 100_000;

		/// <inheritdoc />
		public override SystemPhase Phase => SystemPhase.Effects;

		/// <inheritdoc />
		public override void OnAdded(World world)
		{
			CoreEffects.Register(world.Resource<EffectQueue>());
		}

		/// <inheritdoc />
		public override void Update(World world, in TickContext tick)
		{
			EffectQueue queue = world.Resource<EffectQueue>();
			int applied = 0;
			// Effects queued by a handler run in this same tick, after the ones already waiting.
			for (int i = 0; i < queue.Count; i++)
			{
				if (applied >= MaxPerTick)
				{
					queue.Overflowed += queue.Count - i;
					break;
				}
				Effect effect = queue.At(i);   // copied: a handler may queue more and move the array
				EffectHandler handler = queue.HandlerFor(effect.Kind);
				if (handler == null)
				{
					queue.Unhandled++;
					continue;
				}
				handler(world, effect, tick);
				applied++;
			}
			queue.Clear();
		}
	}
}
