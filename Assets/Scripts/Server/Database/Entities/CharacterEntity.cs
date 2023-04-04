using System;
using SQLite;

namespace Server.Entities
{
    
    //[Table("account", Schema = "mmo")]
    [Table("character")]
    public class CharacterEntity
    {
        //[PrimaryKey] // important for performance: O(log n) instead of O(n)
        //[Collation("NOCASE")] // [COLLATE NOCASE for case insensitive compare. this way we can't both create 'Archer' and 'archer' as characters]
        public string name { get; set; }
        //[Indexed] // add index on account to avoid full scans when loading characters
        public string account { get; set; }
        public string raceName { get; set; }
        public string sceneName { get; set; }
        public float x { get; set; }
        public float y { get; set; }
        public float z { get; set; }
        public float rotX { get; set; }
        public float rotY { get; set; }
        public float rotZ { get; set; }
        public float rotW { get; set; }
        public bool isGameMaster { get; set; }
        public bool selected { get; set; }
        // online status can be checked from external programs with either just
        // just 'online', or 'online && (DateTime.UtcNow - lastsaved) <= 1min)
        // which is robust to server crashes too.
        public bool online { get; set; }
        public DateTime lastSaved { get; set; }
        public DateTime timeDeleted { get; set; }
        public bool deleted { get; set; }
    }
}