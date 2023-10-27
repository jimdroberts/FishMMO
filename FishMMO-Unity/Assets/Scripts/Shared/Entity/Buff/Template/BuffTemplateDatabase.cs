using System;
using UnityEngine;

namespace FishMMO.Shared
{
	[CreateAssetMenu(fileName = "New Buff Database", menuName = "Character/Buff/Database", order = 1)]
	public class BuffTemplateDatabase : ScriptableObject
	{
		[Serializable]
		public class BuffDictionary : SerializableDictionary<string, BuffTemplate> { }

		[SerializeField]
		private BuffDictionary buffs = new BuffDictionary();
		public BuffDictionary Buffs { get { return this.buffs; } }

		public BuffTemplate GetBuff(string name)
		{
			this.buffs.TryGetValue(name, out BuffTemplate buff);
			return buff;
		}
	}
}