using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace FishMMO_DB.Entities
{
    [Table("accounts", Schema = "fish_mmo_postgresql")]
    public class AccountEntity
    {
        [Key]
        public string Name { get; set; }
        public string Password { get; set; }
        public DateTime Created { get; set; }
        public DateTime Lastlogin { get; set; }
        public bool Banned { get; set; }
    }
}