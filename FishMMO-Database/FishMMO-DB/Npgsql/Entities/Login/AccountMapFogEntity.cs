using System;

namespace FishMMO.Database.Npgsql.Entities
{
	/// <summary>
	/// An account's explored map of one scene (fog of war, account-wide). The encoding belongs to the game
	/// (<c>FogCodec</c>); the database stores it opaquely and never reads inside it.
	/// </summary>
	public class AccountMapFogEntity
	{
		/// <summary>Owning account. Part of the primary key.</summary>
		public string AccountName { get; set; }
		public AccountEntity Account { get; set; }

		/// <summary>The scene. Part of the primary key.</summary>
		public string SceneName { get; set; }

		/// <summary>The encoded explored chunks.</summary>
		public byte[] Data { get; set; }

		/// <summary>How many chunks are explored, readable without decoding (the map's scene list shows it).</summary>
		public int ExploredCount { get; set; }

		public DateTime TimeUpdated { get; set; }
	}
}
