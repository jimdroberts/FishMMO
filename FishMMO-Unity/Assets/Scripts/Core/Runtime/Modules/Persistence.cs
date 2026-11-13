using System;
using System.Collections.Generic;

namespace FishMMO.Core
{
	/// <summary>One saved component: its key, the version of its shape, and its text.</summary>
	public readonly struct PersistedComponent
	{
		/// <summary>The key the component is saved under (<c>items.inventory</c>).</summary>
		public readonly string Key;

		/// <summary>The version of the payload's shape when it was written.</summary>
		public readonly int Version;

		/// <summary>The component as text (JSON, by convention), written by its module.</summary>
		public readonly string Payload;

		/// <summary>Builds one.</summary>
		public PersistedComponent(string key, int version, string payload)
		{
			Key = key;
			Version = version;
			Payload = payload;
		}
	}

	/// <summary>
	/// The components a world saves with an entity (a character), each under a key its module chose and in a
	/// text form its module writes and reads. A storage adapter saves each as one row per entity and key, so a
	/// module adds saved data with no change to the save and load code and no new table.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Core has no serialiser of its own; it stays engine-free by letting each module turn its component into
	/// text and back. The read is given the version the text was written at, so a module can upgrade old
	/// saves.
	/// </para>
	/// <para>
	/// A saved key no installed module reads is kept as it is by <see cref="Restore"/>'s caller (the returned
	/// list): uninstalling a module must not delete what players earned with it.
	/// </para>
	/// </remarks>
	public sealed class PersistenceRegistry
	{
		private readonly List<IEntry> entries = new List<IEntry>();
		private readonly Dictionary<string, IEntry> byKey = new Dictionary<string, IEntry>(StringComparer.Ordinal);

		/// <summary>Every registered key with its module, version and component type.</summary>
		public IReadOnlyList<(string Key, string Module, int Version, Type Component)> Keys
		{
			get
			{
				var list = new List<(string, string, int, Type)>(entries.Count);
				foreach (IEntry entry in entries)
				{
					list.Add((entry.Key, entry.Module, entry.Version, entry.Component));
				}
				return list;
			}
		}

		/// <summary>Saves component <typeparamref name="T"/> under <paramref name="key"/>.</summary>
		public void Register<T>(string key, int version, Func<T, string> write, Func<string, int, T> read, string module = null) where T : struct
		{
			if (string.IsNullOrEmpty(key))
			{
				throw new ArgumentException("A saved component needs a key.", nameof(key));
			}
			if (byKey.TryGetValue(key, out IEntry existing))
			{
				throw new InvalidOperationException($"Key '{key}' is already saved by {existing.Module ?? "another module"} for {existing.Component.Name}.");
			}
			var entry = new Entry<T>(key, version, module, write ?? throw new ArgumentNullException(nameof(write)), read ?? throw new ArgumentNullException(nameof(read)));
			entries.Add(entry);
			byKey[key] = entry;
		}

		/// <summary>Every saved component <paramref name="entity"/> has, in registration order.</summary>
		public List<PersistedComponent> Capture(World world, Entity entity)
		{
			var saved = new List<PersistedComponent>();
			for (int i = 0; i < entries.Count; i++)
			{
				if (entries[i].TryWrite(world, entity, out string payload))
				{
					saved.Add(new PersistedComponent(entries[i].Key, entries[i].Version, payload));
				}
			}
			return saved;
		}

		/// <summary>
		/// Gives <paramref name="entity"/> each saved component a registered key reads; returns the ones no
		/// installed module reads, for the caller to keep. At once: not while a system walks the stores.
		/// </summary>
		public List<PersistedComponent> Restore(World world, Entity entity, IEnumerable<PersistedComponent> saved)
		{
			var unread = new List<PersistedComponent>();
			foreach (PersistedComponent component in saved)
			{
				if (component.Key != null && byKey.TryGetValue(component.Key, out IEntry entry))
				{
					entry.Read(world, entity, component.Payload, component.Version);
				}
				else
				{
					unread.Add(component);
				}
			}
			return unread;
		}

		private interface IEntry
		{
			string Key { get; }
			int Version { get; }
			string Module { get; }
			Type Component { get; }
			bool TryWrite(World world, Entity entity, out string payload);
			void Read(World world, Entity entity, string payload, int version);
		}

		private sealed class Entry<T> : IEntry where T : struct
		{
			private readonly Func<T, string> write;
			private readonly Func<string, int, T> read;

			public Entry(string key, int version, string module, Func<T, string> write, Func<string, int, T> read)
			{
				Key = key;
				Version = version;
				Module = module;
				this.write = write;
				this.read = read;
			}

			public string Key { get; }
			public int Version { get; }
			public string Module { get; }
			public Type Component => typeof(T);

			public bool TryWrite(World world, Entity entity, out string payload)
			{
				ComponentStore<T> store = world.Store<T>();
				if (!store.TryGetIndex(entity, out int index))
				{
					payload = null;
					return false;
				}
				payload = write(store.At(index));
				return payload != null;
			}

			public void Read(World world, Entity entity, string payload, int version)
			{
				world.Add(entity, read(payload, version));
			}
		}
	}
}
