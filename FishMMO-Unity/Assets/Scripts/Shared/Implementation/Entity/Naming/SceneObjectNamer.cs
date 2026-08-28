using System.Collections.Generic;
using UnityEngine;
using FishNet.Object;
using FishNet.Connection;
using FishNet.Serializing;
using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>
	/// Name caches for a specific generated character gender.
	/// </summary>
	[System.Serializable]
	public class GenderedNameCacheSet
	{
		/// <summary>
		/// Gender this name set applies to.
		/// </summary>
		public CharacterGender Gender = CharacterGender.Unspecified;

		/// <summary>
		/// Cache of possible first names.
		/// </summary>
		public NameCache FirstNames;

		/// <summary>
		/// Cache of possible last names.
		/// </summary>
		public NameCache LastNames;
	}

	/// <summary>
	/// Assigns a generated name to a scene object using gender-specific name caches.
	/// Handles network payloads for name synchronization.
	/// </summary>
	public class SceneObjectNamer : NetworkBehaviour
	{
		/// <summary>
		/// Gender-specific name cache sets.
		/// </summary>
		public List<GenderedNameCacheSet> GenderedNames = new List<GenderedNameCacheSet>();

		/// <summary>
		/// The selected first name index for this object's name.
		/// </summary>
		private int firstNameID = -1;
		/// <summary>
		/// The selected last name index for this object's name.
		/// </summary>
		private int lastNameID = -1;

		/// <summary>
		/// The selected gender for this object's generated name.
		/// </summary>
		private CharacterGender selectedGender = CharacterGender.Unspecified;

		/// <summary>
		/// True once this object has selected name payload values.
		/// </summary>
		private bool nameGenerated;

		/// <summary>
		/// The selected gender for this object's generated name.
		/// </summary>
		public CharacterGender SelectedGender => selectedGender;

		/// <summary>
		/// Called when the server starts. Randomizes name IDs for the object name.
		/// </summary>
		public override void OnStartServer()
		{
			base.OnStartServer();

			GenerateNameIfNeeded();
		}

		/// <summary>
		/// Ensures name payload values have been generated and returns the selected gender.
		/// </summary>
		/// <returns>The selected generated name gender.</returns>
		public CharacterGender EnsureGeneratedGender()
		{
			GenerateNameIfNeeded();
			return selectedGender;
		}

		/// <summary>
		/// Reads the name IDs from the network and sets the object name accordingly.
		/// </summary>
		/// <param name="connection">Network connection.</param>
		/// <param name="reader">Network reader for payload.</param>
		public override void ReadPayload(NetworkConnection connection, Reader reader)
		{
			firstNameID = reader.ReadInt32();
			lastNameID = reader.ReadInt32();
			selectedGender = (CharacterGender)reader.ReadUInt8Unpacked();
			nameGenerated = true;

			ApplyGeneratedName();
		}

		/// <summary>
		/// Writes the name IDs to the network for synchronization.
		/// </summary>
		/// <param name="connection">Network connection.</param>
		/// <param name="writer">Network writer for payload.</param>
		public override void WritePayload(NetworkConnection connection, Writer writer)
		{
			GenerateNameIfNeeded();

			writer.WriteInt32(firstNameID);
			writer.WriteInt32(lastNameID);
			writer.WriteUInt8Unpacked((byte)selectedGender);
		}

		/// <summary>
		/// Generates name payload values once on the server.
		/// </summary>
		private void GenerateNameIfNeeded()
		{
			if (nameGenerated)
			{
				return;
			}

			GenderedNameCacheSet nameSet = SelectRandomGenderedNameSet();
			NameCache firstNames = nameSet == null ? null : nameSet.FirstNames;
			NameCache lastNames = nameSet == null ? null : nameSet.LastNames;

			selectedGender = nameSet == null ? CharacterGender.Unspecified : nameSet.Gender;
			firstNameID = GetRandomNameIndex(firstNames);
			lastNameID = GetRandomNameIndex(lastNames);
			nameGenerated = true;
		}

		/// <summary>
		/// Applies the generated name to the object and client label.
		/// </summary>
		private void ApplyGeneratedName()
		{
			GenderedNameCacheSet nameSet = GetNameSet(selectedGender);
			NameCache firstNames = nameSet == null ? null : nameSet.FirstNames;
			NameCache lastNames = nameSet == null ? null : nameSet.LastNames;

			if (firstNameID < 0 ||
				firstNames == null ||
				firstNames.Names == null ||
				firstNames.Names.Count < 1 ||
				firstNameID >= firstNames.Names.Count)
			{
				return;
			}
			string characterName = firstNames.Names[firstNameID];

			if (lastNameID < 0 ||
				lastNames == null ||
				lastNames.Names == null ||
				lastNames.Names.Count < 1 ||
				lastNameID >= lastNames.Names.Count)
			{
				return;
			}
			characterName += $" {lastNames.Names[lastNameID]}";

			// Assign the generated name to the GameObject
			this.gameObject.name = characterName.Trim();

#if !UNITY_SERVER
			/* "if present" applies to the label as well as the character.
			 *
			 * CharacterNameLabel is assigned from a prefab's label object and is left null
			 * on a character that has none — every NPC prefab in the project ships that way,
			 * and three of them (Banker, General Merchant, Ability Crafter) carry this
			 * component. Testing only the character therefore threw a NullReferenceException
			 * per named NPC on the client, out of the middle of naming.
			 *
			 * This is the same omission Interactable.Awake had against CharacterGuildLabel. */
			ICharacter character = transform.GetComponent<ICharacter>();
			if (character != null &&
				character.CharacterNameLabel != null)
			{
				character.CharacterNameLabel.text = this.gameObject.name;
			}
#endif
		}

		/// <summary>
		/// Selects a random valid gendered name set.
		/// </summary>
		/// <returns>A random valid gendered name set, or null when none are configured.</returns>
		private GenderedNameCacheSet SelectRandomGenderedNameSet()
		{
			if (GenderedNames == null || GenderedNames.Count < 1)
			{
				return null;
			}

			int validCount = 0;
			for (int i = 0; i < GenderedNames.Count; i++)
			{
				if (IsValidNameSet(GenderedNames[i]))
				{
					validCount++;
				}
			}

			if (validCount < 1)
			{
				return null;
			}

			int selectedIndex = DeterministicRNG.Shared.Range(0, validCount);
			int currentIndex = 0;
			for (int i = 0; i < GenderedNames.Count; i++)
			{
				GenderedNameCacheSet nameSet = GenderedNames[i];
				if (!IsValidNameSet(nameSet))
				{
					continue;
				}

				if (currentIndex == selectedIndex)
				{
					return nameSet;
				}
				currentIndex++;
			}

			return null;
		}

		/// <summary>
		/// Gets the name set for the selected gender.
		/// </summary>
		/// <param name="gender">The selected gender.</param>
		/// <returns>The matching valid name set, or null when none exists.</returns>
		private GenderedNameCacheSet GetNameSet(CharacterGender gender)
		{
			if (gender == CharacterGender.Unspecified || GenderedNames == null)
			{
				return null;
			}

			for (int i = 0; i < GenderedNames.Count; i++)
			{
				GenderedNameCacheSet nameSet = GenderedNames[i];
				if (nameSet != null && nameSet.Gender == gender && IsValidNameSet(nameSet))
				{
					return nameSet;
				}
			}

			return null;
		}

		/// <summary>
		/// Returns true if a gendered name set has usable name caches.
		/// </summary>
		/// <param name="nameSet">The name set to validate.</param>
		/// <returns>True if the name set can generate a complete name.</returns>
		private bool IsValidNameSet(GenderedNameCacheSet nameSet)
		{
			return nameSet != null &&
				HasNames(nameSet.FirstNames) &&
				HasNames(nameSet.LastNames);
		}

		/// <summary>
		/// Gets a random name index from a cache.
		/// </summary>
		/// <param name="nameCache">The name cache.</param>
		/// <returns>A valid random index, or -1 when no names exist.</returns>
		private int GetRandomNameIndex(NameCache nameCache)
		{
			return HasNames(nameCache) ? DeterministicRNG.Shared.Range(0, nameCache.Names.Count) : -1;
		}

		/// <summary>
		/// Returns true if a name cache has at least one name.
		/// </summary>
		/// <param name="nameCache">The name cache.</param>
		/// <returns>True if the cache has names.</returns>
		private bool HasNames(NameCache nameCache)
		{
			return nameCache != null && nameCache.Names != null && nameCache.Names.Count > 0;
		}
	}
}