using System;
using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// Serializable dictionary mapping chat channels to their display colors in the UI.
	/// </summary>
	[Serializable]
	public class UIChatChannelColorDictionary : SerializableDictionary<ChatChannel, Color> { }
}