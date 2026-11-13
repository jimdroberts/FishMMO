using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using FishMMO.Database;
using FishMMO.Database.Data;
using FishMMO.Shared.Core;

namespace FishMMO.Server.Implementation.World.SceneServer
{
	/// <summary>
	/// What the character save and load lend a <see cref="CharacterPersistenceParticipant"/>: the database
	/// services, the main thread, and the characters resident on this server.
	/// </summary>
	public interface ICharacterPersistenceHost
	{
		/// <summary>The database services, or null when the server has no database.</summary>
		IDatabaseServiceRegistry Services { get; }

		/// <summary>Runs <paramref name="action"/> on the main thread; false when the queue refused it.</summary>
		bool TryEnqueueMainThread(Action action);

		/// <summary>The character with <paramref name="characterID"/>, while it is resident here.</summary>
		bool TryGetResidentCharacter(long characterID, out IPlayerCharacter character);
	}

	/// <summary>One participant's rows in a save snapshot: rows of one or many characters, each row knowing its character.</summary>
	public abstract class CharacterRows
	{
		/// <summary>How many rows there are.</summary>
		public abstract int Count { get; }

		/// <summary>Appends <paramref name="other"/>'s rows, leaving out those of the characters in <paramref name="skip"/>.</summary>
		internal abstract void AddFrom(CharacterRows other, HashSet<long> skip);
	}

	/// <summary>Rows of type <typeparamref name="T"/>.</summary>
	public sealed class CharacterRows<T> : CharacterRows
	{
		private readonly Func<T, long> characterOf;

		/// <summary>Rows that name their character through <paramref name="characterOf"/>.</summary>
		public CharacterRows(Func<T, long> characterOf, int capacity = 4)
		{
			this.characterOf = characterOf ?? throw new ArgumentNullException(nameof(characterOf));
			Items = new List<T>(capacity);
		}

		/// <summary>The rows.</summary>
		public List<T> Items { get; }

		/// <inheritdoc />
		public override int Count => Items.Count;

		internal override void AddFrom(CharacterRows other, HashSet<long> skip)
		{
			if (!(other is CharacterRows<T> rows))
			{
				return;
			}
			for (int i = 0; i < rows.Items.Count; i++)
			{
				if (skip == null || !skip.Contains(characterOf(rows.Items[i])))
				{
					Items.Add(rows.Items[i]);
				}
			}
		}
	}

	/// <summary>What a participant fetched for one character as it loads, or why it could not.</summary>
	public readonly struct CharacterFetch
	{
		private CharacterFetch(bool isSuccess, object data, string errorCode, string errorMessage)
		{
			IsSuccess = isSuccess;
			Data = data;
			ErrorCode = errorCode;
			ErrorMessage = errorMessage;
		}

		/// <summary>Whether the fetch worked; a failed fetch abandons the load.</summary>
		public bool IsSuccess { get; }

		/// <summary>What was fetched, handed back to the participant's apply; may be null.</summary>
		public object Data { get; }

		/// <summary>The error code of a failed fetch.</summary>
		public string ErrorCode { get; }

		/// <summary>The error message of a failed fetch.</summary>
		public string ErrorMessage { get; }

		/// <summary>A fetch that worked.</summary>
		public static CharacterFetch Ok(object data) => new CharacterFetch(true, data, null, null);

		/// <summary>A fetch that failed.</summary>
		public static CharacterFetch Failed(string errorCode, string errorMessage) => new CharacterFetch(false, null, errorCode, errorMessage);
	}

	/// <summary>
	/// One feature's part of a character's save and load: what it fetches as the character loads, how it puts
	/// that on the character, what it captures when the character is saved, and how it writes that. The
	/// character save and load call every installed participant and name none, so a feature that saves data
	/// brings its participant with it and leaves the save path alone.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Installed by declaring it on its assembly: <c>[assembly: CharacterPersistence(typeof(MyParticipant))]</c>.
	/// </para>
	/// <para>
	/// The save path's guarantees hold for every participant without each restating them: rows are captured
	/// only with the character's session claim, every write quotes the claims its rows were captured under,
	/// the hand-off paths write sequentially with bounded retry before the release, and the periodic path
	/// keeps a row's dirty mark until a write is confirmed. A participant's part is to capture only dirty
	/// rows, bump their versions as it captures, and clear the marks only after a confirmed write.
	/// </para>
	/// </remarks>
	public abstract class CharacterPersistenceParticipant
	{
		/// <summary>The feature's name, for the log ("achievements").</summary>
		public abstract string Name { get; }

		/// <summary>An empty set of this participant's rows.</summary>
		public abstract CharacterRows CreateRows();

		/// <summary>Fetches the character's saved state. Worker thread, during the load.</summary>
		public abstract Task<CharacterFetch> FetchAsync(ICharacterPersistenceHost host, long characterID);

		/// <summary>
		/// Puts what <see cref="FetchAsync"/> returned on the character, restored as persisted (nothing dirty).
		/// Main thread, after the character's Core entity exists and before it spawns.
		/// </summary>
		public abstract void Apply(ICharacter character, object fetched);

		/// <summary>Captures the character's dirty rows into <paramref name="rows"/>, bumping their versions. Main thread.</summary>
		public abstract void Capture(ICharacter character, CharacterRows rows);

		/// <summary>
		/// Writes <paramref name="rows"/>, quoting <paramref name="claims"/>, and clears the written rows' dirty
		/// marks on the main thread once the write is confirmed. Worker thread.
		/// </summary>
		/// <returns>False only when the write failed in a way worth another attempt.</returns>
		public abstract Task<bool> SaveAsync(ICharacterPersistenceHost host, CharacterRows rows, IReadOnlyCollection<CharacterSessionLeaseData> claims);
	}

	/// <summary>Installs a <see cref="CharacterPersistenceParticipant"/>: <c>[assembly: CharacterPersistence(typeof(MyParticipant))]</c>.</summary>
	[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
	public sealed class CharacterPersistenceAttribute : Attribute
	{
		/// <summary>The participant's type.</summary>
		public Type ParticipantType { get; }

		/// <summary>Installs <paramref name="participantType"/>.</summary>
		public CharacterPersistenceAttribute(Type participantType)
		{
			ParticipantType = participantType;
		}
	}

	/// <summary>Every installed <see cref="CharacterPersistenceParticipant"/>, found once per process.</summary>
	public static class CharacterPersistenceParticipants
	{
		private static List<CharacterPersistenceParticipant> all;

		/// <summary>Every installed participant, ordered by name so the save writes tables in the same order everywhere.</summary>
		public static IReadOnlyList<CharacterPersistenceParticipant> All
		{
			get
			{
				if (all == null)
				{
					var found = new List<CharacterPersistenceParticipant>();
					var seen = new HashSet<Type>();
					foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
					{
						object[] attributes;
						try
						{
							attributes = assembly.GetCustomAttributes(typeof(CharacterPersistenceAttribute), false);
						}
						catch (Exception)
						{
							continue;
						}
						foreach (CharacterPersistenceAttribute attribute in attributes)
						{
							Type type = attribute.ParticipantType;
							if (type == null || type.IsAbstract || !typeof(CharacterPersistenceParticipant).IsAssignableFrom(type) || !seen.Add(type))
							{
								continue;
							}
							try
							{
								found.Add((CharacterPersistenceParticipant)Activator.CreateInstance(type));
							}
							catch (Exception ex)
							{
								FishMMO.Logging.Log.Error("CharacterPersistence", $"{type.FullName} could not be created; its data will not load or save: {ex}");
							}
						}
					}
					found.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));
					all = found;
				}
				return all;
			}
		}
	}

	/// <summary>The character save and load, as the host of every <see cref="CharacterPersistenceParticipant"/>.</summary>
	public partial class CharacterSystem : ICharacterPersistenceHost
	{
		IDatabaseServiceRegistry ICharacterPersistenceHost.Services => Server?.Database?.ServiceRegistry;

		bool ICharacterPersistenceHost.TryEnqueueMainThread(Action action) => TryEnqueueMainThread(action);

		bool ICharacterPersistenceHost.TryGetResidentCharacter(long characterID, out IPlayerCharacter character) =>
			TryGetResidentCharacter(characterID, out character);
	}
}
