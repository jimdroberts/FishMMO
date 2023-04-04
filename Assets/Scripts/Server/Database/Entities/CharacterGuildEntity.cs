using System;
using SQLite;

namespace Server.Entities
{
    
    //[Table("account", Schema = "mmo")]
    [Table("character_guild")]
    public class CharacterGuildEntity
    {
        //[PrimaryKey]
        public string character { get; set; }
        // add index on guild to avoid full scans when loading guild members
        //[Indexed]
        public string guild { get; set; }
        public int rank { get; set; }
    }
}