using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using FishNet.Connection;
using FishNet.Managing.Client;
using FishNet.Managing.Server;
using FishNet.Managing.Timing;
using FishNet.Object;
using FishNet.Serializing;
using FishMMO.Logging;
using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>
	/// A module's piece of a character, added when the character wakes rather than authored on its prefab, so removing the
	/// module removes the piece and leaves every prefab whole (no missing script, no gap in FishNet's behaviour list).
	/// </summary>
	/// <remarks>
	/// <para>Declared with <c>[assembly: CharacterPart(typeof(MyPart), typeof(IPlayerCharacter))]</c>: every character that is
	/// (or derives from, or implements) the second type gets one, on every peer, in <c>BaseCharacter.Awake</c> before the
	/// character's behaviours are initialised, so it is found by <see cref="ICharacter.TryGet{T}"/> like any behaviour.</para>
	/// <para>It is not a NetworkBehaviour: it talks over broadcasts, and its spawn-time state rides the character's own spawn
	/// payload (<see cref="WritePayload"/>, framed by part, so a reader skips a part it does not have). The character
	/// forwards its lifecycle: <see cref="OnStartServer"/>, <see cref="OnStopServer"/>, <see cref="ResetState"/> (pooling),
	/// and <see cref="OnStartCharacter"/>/<see cref="OnStopCharacter"/> on the client as for every behaviour.</para>
	/// </remarks>
	public abstract class CharacterPart : MonoBehaviour, ICharacterBehaviour
	{
		/// <summary>The character this part belongs to.</summary>
		public ICharacter Character { get; private set; }

		/// <summary>The character as a player, or null.</summary>
		public IPlayerCharacter PlayerCharacter { get; private set; }

		/// <summary>The character's network behaviour, whose network state the part reads.</summary>
		protected BaseCharacter Host { get; private set; }

		public bool Initialized { get; private set; }

		protected NetworkObject NetworkObject => NetworkObjectOverride != null ? NetworkObjectOverride : (Host != null ? Host.NetworkObject : null);

		/// <summary>
		/// Tests only: the network object a part with no spawned character answers with (an owner-shaped payload written by
		/// a part built outside a live session).
		/// </summary>
		internal NetworkObject NetworkObjectOverride { get; set; }
		protected bool IsOwner => Host != null && Host.IsOwner;
		protected bool IsServerInitialized => Host != null && Host.IsServerInitialized;
		protected bool IsServerStarted => Host != null && Host.IsServerStarted;
		protected bool IsClientInitialized => Host != null && Host.IsClientInitialized;
		protected bool IsClientStarted => Host != null && Host.IsClientStarted;
		protected FishNet.Managing.NetworkManager NetworkManager => Host != null ? Host.NetworkManager : null;
		protected bool IsSpawned => Host != null && Host.IsSpawned;
		protected ClientManager ClientManager => Host != null ? Host.ClientManager : null;
		protected ServerManager ServerManager => Host != null ? Host.ServerManager : null;
		protected TimeManager TimeManager => Host != null ? Host.TimeManager : null;

		/// <summary>Unity Awake: runs as the character adds the part (in its own Awake), before InitializeOnce.</summary>
		protected void Awake()
		{
			OnAwake();
		}

		/// <summary>Early setup, before InitializeOnce: the character is not set yet.</summary>
		public virtual void OnAwake() { }

		/// <summary>The connection that owns the character, as the network behaviour's <c>Owner</c>.</summary>
		protected NetworkConnection Owner => Host != null ? Host.Owner : null;

		/// <summary>The character's prediction manager, as the network behaviour's <c>PredictionManager</c>.</summary>
		protected FishNet.Managing.Predicting.PredictionManager PredictionManager => Host != null ? Host.PredictionManager : null;

		public void InitializeOnce(ICharacter character)
		{
			if (Initialized || character == null)
			{
				return;
			}
			Initialized = true;
			Character = character;
			PlayerCharacter = character as IPlayerCharacter;
			Host = character as BaseCharacter;
			Character.RegisterCharacterBehaviour(this);
			InitializeOnce();
		}

		/// <summary>Called once, when the character initialises its behaviours.</summary>
		public virtual void InitializeOnce() { }

		private void OnDestroy()
		{
			OnDestroying();
			Character?.UnregisterCharacterBehaviour(this);
			Character = null;
			PlayerCharacter = null;
			Host = null;
		}

		public virtual void OnDestroying() { }

		/// <summary>Client: the character started (players: after the payload; see <c>PlayerCharacter</c>).</summary>
		public virtual void OnStartCharacter() { }

		/// <summary>Client: the character is stopping.</summary>
		public virtual void OnStopCharacter() { }

		/// <summary>Both peers: the character's network object started (a NetworkBehaviour's OnStartNetwork).</summary>
		public virtual void OnStartNetwork() { }

		/// <summary>Both peers: the character's network object is stopping (a NetworkBehaviour's OnStopNetwork).</summary>
		public virtual void OnStopNetwork() { }

		/// <summary>Client: the character started on this client (forwarded by the character, after its own start).</summary>
		public virtual void OnStartClient() { }

		/// <summary>Client: the character is stopping on this client (forwarded before the character's own stop).</summary>
		public virtual void OnStopClient() { }

		/// <summary>Client: the character's owner changed on this client (forwarded by the character).</summary>
		public virtual void OnOwnershipClient(NetworkConnection prevOwner) { }

		/// <summary>Server: the character started on the server.</summary>
		public virtual void OnStartServer() { }

		/// <summary>Server: the character is stopping on the server.</summary>
		public virtual void OnStopServer() { }

		/// <summary>The character is being reset for reuse from the pool.</summary>
		public virtual void ResetState(bool asServer) { }

		/// <summary>
		/// Stable id of this part's block in the spawn payload; 0 (the default) for a part with no spawn state. Derive it
		/// from something that never changes, such as <c>CharacterParts.IdOf("travel.waypoints")</c>.
		/// </summary>
		public virtual uint PayloadId => 0u;

		/// <summary>Server: this part's spawn state for <paramref name="conn"/> (its block is framed by the character).</summary>
		public virtual void WritePayload(NetworkConnection conn, Writer writer) { }

		/// <summary>Client: reads what <see cref="WritePayload"/> wrote (the character seeks past the block afterwards).</summary>
		public virtual void ReadPayload(NetworkConnection conn, Reader reader) { }
	}

	/// <summary>Declares a <see cref="CharacterPart"/> and which characters get it.</summary>
	[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
	public sealed class CharacterPartAttribute : Attribute
	{
		/// <summary>The part's type (a <see cref="CharacterPart"/>).</summary>
		public Type PartType { get; }

		/// <summary>Characters that are this type (class or interface) get the part, for example <c>IPlayerCharacter</c>.</summary>
		public Type AppliesTo { get; }

		public CharacterPartAttribute(Type partType, Type appliesTo)
		{
			PartType = partType;
			AppliesTo = appliesTo;
		}
	}

	/// <summary>The declared <see cref="CharacterPart"/>s, found in the loaded assemblies, and the character's use of them.</summary>
	public static class CharacterParts
	{
		private struct Declaration
		{
			public Type Part;
			public Type AppliesTo;
		}

		private static List<Declaration> declarations;
		private static readonly object gate = new object();

		/// <summary>A stable payload id for a part, from a name that never changes (FNV-1a; never 0).</summary>
		public static uint IdOf(string name)
		{
			uint hash = 2166136261u;
			foreach (char c in name ?? string.Empty)
			{
				hash = (hash ^ c) * 16777619u;
			}
			return hash == 0u ? 1u : hash;
		}

		/// <summary>The part types a character of <paramref name="characterType"/> gets, in a fixed order.</summary>
		public static IEnumerable<Type> PartsFor(Type characterType)
		{
			foreach (Declaration declaration in Declarations())
			{
				if (declaration.AppliesTo == null || declaration.AppliesTo.IsAssignableFrom(characterType))
				{
					yield return declaration.Part;
				}
			}
		}

		/// <summary>Adds the parts <paramref name="character"/> should have and does not (called from its Awake).</summary>
		public static void Attach(BaseCharacter character)
		{
			if (character == null)
			{
				return;
			}
			foreach (Type part in PartsFor(character.GetType()))
			{
				if (character.GetComponent(part) == null)
				{
					character.gameObject.AddComponent(part);
				}
			}
		}

		/// <summary>The character's parts, in the order they were added.</summary>
		public static void Collect(BaseCharacter character, List<CharacterPart> results)
		{
			results.Clear();
			if (character != null)
			{
				character.GetComponents(results);
			}
		}

		private const int MaxPayloadParts = 64;

		/// <summary>
		/// Writes every part's spawn state: a count, then per part its id and a length-framed block. Called at the end of the
		/// character's own payload (<c>PlayerCharacter</c>, <c>NPC</c>).
		/// </summary>
		public static void WritePayload(BaseCharacter character, NetworkConnection conn, Writer writer)
		{
			List<CharacterPart> parts = Scratch();
			Collect(character, parts);
			int count = 0;
			foreach (CharacterPart part in parts)
			{
				if (part.PayloadId != 0u)
				{
					++count;
				}
			}
			writer.WriteUInt8Unpacked((byte)Math.Min(count, MaxPayloadParts));
			int written = 0;
			foreach (CharacterPart part in parts)
			{
				if (part.PayloadId == 0u || written >= MaxPayloadParts)
				{
					continue;
				}
				++written;
				writer.WriteUInt32Unpacked(part.PayloadId);
				writer.Skip(4);
				int start = writer.Position;
				part.WritePayload(conn, writer);
				writer.InsertUInt32Unpacked((uint)(writer.Position - start), start - 4);
			}
			parts.Clear();
		}

		/// <summary>Reads what <see cref="WritePayload"/> wrote; a block for a part this peer does not have is skipped.</summary>
		public static void ReadPayload(BaseCharacter character, NetworkConnection conn, Reader reader)
		{
			List<CharacterPart> parts = Scratch();
			Collect(character, parts);
			int count = reader.ReadUInt8Unpacked();
			for (int i = 0; i < count; ++i)
			{
				uint id = reader.ReadUInt32Unpacked();
				uint length = reader.ReadUInt32Unpacked();
				if (length > (uint)reader.Remaining)
				{
					Log.Error("CharacterParts", $"A part block of {length} bytes exceeds the {reader.Remaining} left in the spawn payload; discarding the rest.");
					reader.Position += reader.Remaining;
					break;
				}
				int end = reader.Position + (int)length;
				CharacterPart owner = null;
				foreach (CharacterPart part in parts)
				{
					if (part.PayloadId == id)
					{
						owner = part;
						break;
					}
				}
				if (owner != null)
				{
					try
					{
						owner.ReadPayload(conn, reader);
					}
					catch (Exception ex)
					{
						Log.Error("CharacterParts", $"{owner.GetType().Name} failed to read its spawn state.", ex);
					}
				}
				if (reader.Position != end)
				{
					if (owner != null)
					{
						Log.Error("CharacterParts", $"{owner.GetType().Name} read {reader.Position - (end - (int)length)} of its {length} bytes; seeking to the end of its block.");
					}
					reader.Position = end;
				}
			}
			parts.Clear();
		}

		/// <summary>Forwards a lifecycle call to every part of the character.</summary>
		public static void Forward(BaseCharacter character, Action<CharacterPart> call)
		{
			List<CharacterPart> parts = Scratch();
			Collect(character, parts);
			foreach (CharacterPart part in parts)
			{
				try
				{
					call(part);
				}
				catch (Exception ex)
				{
					Log.Error("CharacterParts", $"{part.GetType().Name} threw during a lifecycle call.", ex);
				}
			}
			parts.Clear();
		}

		private static List<CharacterPart> Scratch()
		{
			// Lifecycle calls can nest (a part spawning something); each level gets its own list.
			return new List<CharacterPart>(4);
		}

		private static List<Declaration> Declarations()
		{
			List<Declaration> current = declarations;
			if (current != null)
			{
				return current;
			}
			lock (gate)
			{
				declarations ??= Discover();
				return declarations;
			}
		}

		private static List<Declaration> Discover()
		{
			var found = new List<Declaration>();
			foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
			{
				object[] attributes;
				try
				{
					attributes = assembly.GetCustomAttributes(typeof(CharacterPartAttribute), false);
				}
				catch
				{
					continue;
				}
				foreach (CharacterPartAttribute attribute in attributes)
				{
					if (attribute.PartType == null || !typeof(CharacterPart).IsAssignableFrom(attribute.PartType) || attribute.PartType.IsAbstract)
					{
						continue;
					}
					found.Add(new Declaration { Part = attribute.PartType, AppliesTo = attribute.AppliesTo });
				}
			}
			// A fixed order on every peer: payload blocks and component order must agree.
			found.Sort((a, b) => string.CompareOrdinal(a.Part.FullName, b.Part.FullName));
			return found;
		}
	}
}
