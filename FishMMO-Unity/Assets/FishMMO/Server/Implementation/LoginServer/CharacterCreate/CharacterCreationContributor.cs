using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using FishMMO.Database;
using FishMMO.Shared;

namespace FishMMO.Server.Implementation.LoginServer
{
	/// <summary>
	/// A module's share of a new character: rows prepared from templates on the main thread, written inside the creation's
	/// transaction. The Factions module's starting standings are one.
	/// </summary>
	/// <remarks>
	/// Declared with <c>[assembly: CharacterCreationContributor(typeof(MyContributor))]</c>, so character creation names no
	/// module: removing one removes its rows from new characters and nothing else.
	/// </remarks>
	public abstract class CharacterCreationContributor
	{
		/// <summary>For logs.</summary>
		public abstract string Name { get; }

		/// <summary>
		/// Main thread: what a character of <paramref name="race"/> starts with, as plain data (no Unity objects: it is read
		/// on a worker); null when it starts with nothing of this module's.
		/// </summary>
		public abstract object Prepare(RaceTemplate race);

		/// <summary>
		/// Worker, inside the creation's transaction: writes <paramref name="prepared"/> for the new character. False fails
		/// the creation, which then rolls back (a character missing part of what it starts with is worse than a retry).
		/// </summary>
		public abstract Task<bool> PersistAsync(IDatabaseServiceRegistry services, long characterID, object prepared);
	}

	/// <summary>Declares a <see cref="CharacterCreationContributor"/> (public parameterless constructor).</summary>
	[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
	public sealed class CharacterCreationContributorAttribute : Attribute
	{
		public Type ContributorType { get; }

		public CharacterCreationContributorAttribute(Type contributorType)
		{
			ContributorType = contributorType;
		}
	}

	/// <summary>The declared contributors, found once in the loaded assemblies, in name order.</summary>
	public static class CharacterCreationContributors
	{
		private static List<CharacterCreationContributor> all;

		public static IReadOnlyList<CharacterCreationContributor> All
		{
			get
			{
				if (all != null)
				{
					return all;
				}
				var found = new List<CharacterCreationContributor>();
				var seen = new HashSet<Type>();
				foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
				{
					object[] attributes;
					try
					{
						attributes = assembly.GetCustomAttributes(typeof(CharacterCreationContributorAttribute), false);
					}
					catch
					{
						continue;
					}
					foreach (CharacterCreationContributorAttribute attribute in attributes)
					{
						Type type = attribute.ContributorType;
						if (type == null || !typeof(CharacterCreationContributor).IsAssignableFrom(type) || !seen.Add(type))
						{
							continue;
						}
						found.Add((CharacterCreationContributor)Activator.CreateInstance(type));
					}
				}
				found.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));
				all = found;
				return all;
			}
		}
	}
}
