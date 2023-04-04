using System;
using SQLite;

namespace Server.Entities
{
    
    //[Table("account", Schema = "mmo")]
    [Table("character_equipment")]
    public class CharacterEquipmentEntity : CharacterInventoryEntity
    {
    }
}