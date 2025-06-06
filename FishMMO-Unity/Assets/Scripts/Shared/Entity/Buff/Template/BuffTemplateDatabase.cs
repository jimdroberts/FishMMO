using System;
using UnityEngine;

namespace FishMMO.Shared
{
	[CreateAssetMenu(fileName = "New Buff Database", menuName = "FishMMO/Character/Buff/Database", order = 1)]
	public class BuffTemplateDatabase : ScriptableObject
	{
		[Serializable]
		public class BuffDictionary : SerializableDictionary<string, BaseBuffTemplate> { }

		[SerializeField]
		private BuffDictionary buffs = new BuffDictionary();
		public BuffDictionary Buffs { get { return this.buffs; } }

		public BaseBuffTemplate GetBuff(string name)
		{
			this.buffs.TryGetValue(name, out BaseBuffTemplate buff);
			return buff;
		}
	}
}