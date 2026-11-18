using System;
using FishMMO.Core;
using FishMMO.Shared.Core;
using FishNet.Managing;

namespace FishMMO.Shared
{
	/// <summary>An object that stands for a Core entity: a character, on each side it is spawned on.</summary>
	public interface IEntityLinked
	{
		/// <summary>The entity, or <see cref="EntityLink.None"/> while it has none.</summary>
		EntityLink CoreLink { get; }
	}

	/// <summary>On a character's entity: the character it stands for, so systems can reach the engine object.</summary>
	public struct CharacterRef
	{
		/// <summary>The character.</summary>
		public ICharacter Character;
	}

	/// <summary>
	/// Making and reaching characters' Core entities. Feature state lives as components on the entity; the
	/// archetype (player, NPC) gives each new entity what the installed modules add to it.
	/// </summary>
	public static class CharacterLinks
	{
		/// <summary>
		/// A new entity of <paramref name="archetype"/> in <paramref name="networkManager"/>'s
		/// <paramref name="side"/> world, standing for <paramref name="character"/>. False when that side runs no
		/// world (it is not started).
		/// </summary>
		public static bool TryCreate(NetworkManager networkManager, SimulationSide side, string archetype, ICharacter character, out EntityLink link)
		{
			link = EntityLink.None;
			if (!CoreWorldHost.TryGetOrStart(networkManager, side, out CoreWorldHost host))
			{
				return false;
			}
			World world = host.World;
			Entity entity = world.Spawn(archetype);
			var reference = new CharacterRef { Character = character };
			if (world.IsTicking)
			{
				world.Commands.Add(entity, reference);
			}
			else
			{
				world.Add(entity, reference);
			}
			link = new EntityLink(world, entity);
			return true;
		}

		/// <summary>Ends <paramref name="link"/>'s entity: at once, or at the end of the phase while its world ticks.</summary>
		public static void Destroy(EntityLink link)
		{
			if (!link.IsValid)
			{
				return;
			}
			if (link.World.IsTicking)
			{
				link.World.Commands.Destroy(link.Entity);
			}
			else
			{
				link.World.Destroy(link.Entity);
			}
		}

		/// <summary><paramref name="character"/>'s entity, or <see cref="EntityLink.None"/>.</summary>
		public static EntityLink CoreLink(this ICharacter character) =>
			character is IEntityLinked linked ? linked.CoreLink : EntityLink.None;

		/// <summary>
		/// <paramref name="character"/>'s <typeparamref name="T"/>, by reference, given it when it has none (the
		/// module that owns <typeparamref name="T"/> normally adds it through the archetype). Throws when the
		/// character has no entity: it is not spawned and was not loaded, or no Core world runs on its side.
		/// </summary>
		public static ref T Component<T>(this ICharacter character) where T : struct
		{
			EntityLink link = character.CoreLink();
			if (!link.IsValid)
			{
				throw new InvalidOperationException($"{(character as UnityEngine.Object)?.name ?? character?.GetType().Name ?? "null"} has no Core entity, so it has no {typeof(T).Name}.");
			}
			ComponentStore<T> store = link.World.Store<T>();
			if (!store.TryGetIndex(link.Entity, out int index))
			{
				store.Set(link.Entity, default);
				store.TryGetIndex(link.Entity, out index);
			}
			return ref store.At(index);
		}

		/// <summary>Whether <paramref name="character"/> has an entity, so <see cref="Component{T}"/> can be asked.</summary>
		public static bool HasCoreEntity(this ICharacter character) => character.CoreLink().IsValid;
	}
}
