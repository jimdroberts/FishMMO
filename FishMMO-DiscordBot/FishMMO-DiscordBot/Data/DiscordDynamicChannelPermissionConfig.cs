using System.Collections.Generic;

namespace FishMMO.DiscordBot.Data
{
	public class DiscordDynamicChannelPermissionConfig
	{
		public string RoleName { get; set; }
		public List<string> AllowPermissions { get; set; } = new List<string>();
		public List<string> DenyPermissions { get; set; } = new List<string>();
	}
}