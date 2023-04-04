using System;
using SQLite;

namespace Server.Entities
{
    
    //[Table("account", Schema = "mmo")]
    [Table("character_inventory")]
    public class CharacterInventoryEntity
    {
        public string character { get; set; }
        public long instanceID { get; set; }
        public int templateID { get; set; }
        public int seed { get; set; }
        public int slot { get; set; }
        public string name { get; set; }
        public int amount { get; set; }
    }
}