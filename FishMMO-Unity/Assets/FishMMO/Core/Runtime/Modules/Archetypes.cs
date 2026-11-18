using System;
using System.Collections.Generic;

namespace FishMMO.Core
{
	/// <summary>
	/// The kinds of entity a world makes (player, NPC, ability object, interactable) and the components each
	/// starts with. Core names the kinds; installed modules give them their components, so installing a module
	/// gives every new entity of a kind what the module needs, with no prefab edited.
	/// </summary>
	public sealed class Archetypes
	{
		/// <summary>A player character.</summary>
		public const string Player = "player";

		/// <summary>A non-player character.</summary>
		public const string Npc = "npc";

		/// <summary>A spawned ability object: a projectile, a field.</summary>
		public const string AbilityObject = "ability-object";

		/// <summary>Something players use: a door, a merchant, a chest.</summary>
		public const string Interactable = "interactable";

		private readonly Dictionary<string, List<Part>> parts = new Dictionary<string, List<Part>>(StringComparer.Ordinal);
		private readonly List<string> names = new List<string>();

		/// <summary>Every archetype, in the order defined.</summary>
		public IReadOnlyList<string> Names => names;

		/// <summary>Defines <paramref name="name"/> with no components, if it is new.</summary>
		public void Define(string name)
		{
			if (string.IsNullOrEmpty(name))
			{
				throw new ArgumentException("An archetype needs a name.", nameof(name));
			}
			if (!parts.ContainsKey(name))
			{
				parts[name] = new List<Part>();
				names.Add(name);
			}
		}

		/// <summary>Whether <paramref name="name"/> is defined.</summary>
		public bool Has(string name) => name != null && parts.ContainsKey(name);

		/// <summary>Adds a starting component to <paramref name="name"/>, from <paramref name="module"/>.</summary>
		public void Add(string name, string module, string description, Action<World, Entity> initialize)
		{
			Define(name);
			parts[name].Add(new Part(module, description, initialize ?? throw new ArgumentNullException(nameof(initialize))));
		}

		/// <summary>What <paramref name="name"/> starts with: each part's module and what it adds.</summary>
		public IReadOnlyList<(string Module, string Description)> PartsOf(string name)
		{
			var list = new List<(string, string)>();
			if (name != null && parts.TryGetValue(name, out List<Part> found))
			{
				foreach (Part part in found)
				{
					list.Add((part.Module, part.Description));
				}
			}
			return list;
		}

		/// <summary>
		/// A new entity of <paramref name="name"/> with every starting component. While the world ticks, the
		/// components arrive when the phase ends (<see cref="World.IsTicking"/>).
		/// </summary>
		public Entity Spawn(World world, string name)
		{
			if (!parts.TryGetValue(name ?? string.Empty, out List<Part> found))
			{
				throw new InvalidOperationException($"No archetype '{name}' is defined.");
			}
			Entity entity = world.Create();
			for (int i = 0; i < found.Count; i++)
			{
				found[i].Initialize(world, entity);
			}
			return entity;
		}

		private readonly struct Part
		{
			public readonly string Module;
			public readonly string Description;
			public readonly Action<World, Entity> Initialize;

			public Part(string module, string description, Action<World, Entity> initialize)
			{
				Module = module;
				Description = description;
				Initialize = initialize;
			}
		}
	}

	/// <summary>Spawning by archetype on a <see cref="World"/>.</summary>
	public static class ArchetypeWorld
	{
		/// <summary>A new entity of archetype <paramref name="name"/> (<see cref="Archetypes.Spawn"/>).</summary>
		public static Entity Spawn(this World world, string name) => world.Resource<Archetypes>().Spawn(world, name);
	}
}
