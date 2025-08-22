#if UNITY_EDITOR
using System;

namespace MeshyAI
{
	/// <summary>
	/// Represents the response from the Meshy API containing the user's credit balance.
	/// </summary>
	[Serializable]
	public class MeshyBalanceResponse
	{
		/// <summary>
		/// The number of credits available in the user's Meshy account.
		/// </summary>
		public int balance;
	}
}
#endif