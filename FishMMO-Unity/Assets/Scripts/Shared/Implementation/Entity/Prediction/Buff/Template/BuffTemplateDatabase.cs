using System;
using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// ScriptableObject database for storing and retrieving buff templates by name.
	/// </summary>
	[CreateAssetMenu(fileName = "New Buff Database", menuName = "FishMMO/Character/Buff/Database", order = 1)]
	public class BuffTemplateDatabase : ScriptableObject
	{
		/// <summary>
		/// Serializable dictionary mapping buff names to their templates.
		/// </summary>
		[Serializable]
		public class BuffDictionary : SerializableDictionary<string, BaseBuffTemplate> { }

		/// <summary>
		/// The backing field for the buffs dictionary.
		/// </summary>
		[SerializeField]
		private BuffDictionary buffs = new BuffDictionary();

		/// <summary>
		/// Public accessor for the buffs dictionary.
		/// </summary>
		public BuffDictionary Buffs { get { return this.buffs; } }

		/// <summary>
		/// Retrieves a buff template by name, or null if not found.
		/// </summary>
		/// <param name="name">The name of the buff to retrieve.</param>
		/// <returns>The <see cref="BaseBuffTemplate"/> if found, otherwise null.</returns>
		public BaseBuffTemplate GetBuff(string name)
		{
			this.buffs.TryGetValue(name, out BaseBuffTemplate buff);
			return buff;
		}
	}
}