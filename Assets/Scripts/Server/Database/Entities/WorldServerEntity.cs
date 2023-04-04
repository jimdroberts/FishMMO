using System;
using SQLite;

namespace Server.Entities
{
    
    //[Table("account", Schema = "mmo")]
    [Table("world_server")]
    public class WorldServerEntity
    {
        //[PrimaryKey]
        public string name { get; set; }
        public DateTime lastPulse { get; set; }
        public string address { get; set; }
        public ushort port { get; set; }
        public int characterCount { get; set; }
        public bool locked { get; set; }
    }
}