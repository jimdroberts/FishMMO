using System;
using System.Collections.Generic;
using SQLite;

namespace Server
{
	public partial class Database
	{
		class worldServers
		{
			[PrimaryKey]
			public string name { get; set; }
			public DateTime lastPulse { get; set; }
			public string address { get; set; }
			public ushort port { get; set; }
			public int characterCount { get; set; }
			public bool locked { get; set; }
		}

		public void AddWorldServer(string name, string address, ushort port, int characterCount, bool locked)
		{
			if (!string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(address))
			{
				// demo feature: create account if it doesn't exist yet.
				// note: sqlite-net has no InsertOrIgnore so we do it in two steps
				if (connection.FindWithQuery<worldServers>("SELECT * FROM worldServers WHERE name=?", name) == null)
				{
					connection.Insert(new worldServers
					{
						name = name,
						lastPulse = DateTime.UtcNow,
						address = address,
						port = port,
						characterCount = characterCount,
						locked = locked
					});
				}
			}
		}

		public void WorldServerPulse(string name)
		{
			// check account name, password, banned status
			/*if (connection.FindWithQuery<accounts>("SELECT * FROM worldServers WHERE name=?", name) != null)
			{
				// save last login time and return true
				connection.Execute("UPDATE worldServers SET lastPulse=? WHERE name=?", DateTime.UtcNow, name);
			}*/

			connection.BeginTransaction(); // transaction for performance
			connection.Execute("UPDATE worldServers SET lastPulse=? WHERE name=?", DateTime.UtcNow, name);
			connection.Commit(); // end transaction
		}

		public void DeleteWorldServer(string name)
		{
			connection.BeginTransaction(); // transaction for performance
			connection.Execute("DELETE FROM worldServers WHERE name=?", name);
			connection.Commit(); // end transaction
		}

		public List<WorldServerDetails> GetWorldServerList()
		{
			List<WorldServerDetails> result = new List<WorldServerDetails>();
			foreach (worldServers server in connection.Query<worldServers>("SELECT * FROM worldServers"))
			{
				// if the servers last pulse is greater than 10 seconds we skip it because it is probably offline
				if ((server.lastPulse - DateTime.UtcNow) > TimeSpan.FromSeconds(15))
					continue;

				result.Add(new WorldServerDetails
				{
					name = server.name,
					lastPulse = server.lastPulse,
					address = server.address,
					port = server.port,
					characterCount = server.characterCount,
					locked = server.locked,
				});
			}
			return result;
		}
	}
}