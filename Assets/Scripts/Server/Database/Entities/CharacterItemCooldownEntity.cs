using System;
using SQLite;

namespace Server.Entities
{
    
    //[Table("account", Schema = "mmo")]
    [Table("character_itemcooldowns")]
    public class CharacterItemCooldownEntity
    {
        //[PrimaryKey] // important for performance: O(log n) instead of O(n)
        public string character { get; set; }
        public string category { get; set; }
        public float cooldownEnd { get; set; }
        
    }
}