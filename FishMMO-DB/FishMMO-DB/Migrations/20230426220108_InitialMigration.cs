using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

namespace FishMMO_DB.Migrations
{
    public partial class InitialMigration : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "fishMMO");

            migrationBuilder.CreateTable(
                name: "accounts",
                schema: "fishMMO",
                columns: table => new
                {
                    name = table.Column<string>(type: "text", nullable: false),
                    password = table.Column<string>(type: "text", nullable: false),
                    created = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    lastlogin = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    banned = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_accounts", x => x.name);
                });

            migrationBuilder.CreateTable(
                name: "characters",
                schema: "fishMMO",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    name = table.Column<string>(type: "text", nullable: false),
                    name_lowercase = table.Column<string>(type: "text", nullable: false, computedColumnSql: "LOWER(\"name\")", stored: true),
                    account = table.Column<string>(type: "text", nullable: false),
                    race_id = table.Column<int>(type: "integer", nullable: false),
                    scene_name = table.Column<string>(type: "text", nullable: false),
                    x = table.Column<float>(type: "real", nullable: false),
                    y = table.Column<float>(type: "real", nullable: false),
                    z = table.Column<float>(type: "real", nullable: false),
                    rot_x = table.Column<float>(type: "real", nullable: false),
                    rot_y = table.Column<float>(type: "real", nullable: false),
                    rot_z = table.Column<float>(type: "real", nullable: false),
                    rot_w = table.Column<float>(type: "real", nullable: false),
                    is_game_master = table.Column<bool>(type: "boolean", nullable: false),
                    selected = table.Column<bool>(type: "boolean", nullable: false),
                    online = table.Column<bool>(type: "boolean", nullable: false),
                    time_created = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    last_saved = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    time_deleted = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    deleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_characters", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "guild_info",
                schema: "fishMMO",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    name = table.Column<string>(type: "text", nullable: false),
                    notice = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_guild_info", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "world_servers",
                schema: "fishMMO",
                columns: table => new
                {
                    name = table.Column<string>(type: "text", nullable: false),
                    last_pulse = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    address = table.Column<string>(type: "text", nullable: false),
                    port = table.Column<int>(type: "integer", nullable: false),
                    character_count = table.Column<int>(type: "integer", nullable: false),
                    locked = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_world_servers", x => x.name);
                });

            migrationBuilder.CreateTable(
                name: "character_buffs",
                schema: "fishMMO",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    character_id = table.Column<long>(type: "bigint", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    level = table.Column<int>(type: "integer", nullable: false),
                    buff_time_end = table.Column<float>(type: "real", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_character_buffs", x => x.id);
                    table.ForeignKey(
                        name: "fk_character_buffs_characters_character_id",
                        column: x => x.character_id,
                        principalSchema: "fishMMO",
                        principalTable: "characters",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "character_equipment",
                schema: "fishMMO",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    character_id = table.Column<long>(type: "bigint", nullable: false),
                    instance_id = table.Column<long>(type: "bigint", nullable: false),
                    template_id = table.Column<int>(type: "integer", nullable: false),
                    seed = table.Column<int>(type: "integer", nullable: false),
                    slot = table.Column<int>(type: "integer", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    amount = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_character_equipment", x => x.id);
                    table.ForeignKey(
                        name: "fk_character_equipment_characters_character_id",
                        column: x => x.character_id,
                        principalSchema: "fishMMO",
                        principalTable: "characters",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "character_inventory",
                schema: "fishMMO",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    character_id = table.Column<long>(type: "bigint", nullable: false),
                    instance_id = table.Column<long>(type: "bigint", nullable: false),
                    template_id = table.Column<int>(type: "integer", nullable: false),
                    seed = table.Column<int>(type: "integer", nullable: false),
                    slot = table.Column<int>(type: "integer", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    amount = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_character_inventory", x => x.id);
                    table.ForeignKey(
                        name: "fk_character_inventory_characters_character_id",
                        column: x => x.character_id,
                        principalSchema: "fishMMO",
                        principalTable: "characters",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "character_itemcooldowns",
                schema: "fishMMO",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    character_id = table.Column<long>(type: "bigint", nullable: false),
                    category = table.Column<string>(type: "text", nullable: false),
                    cooldown_end = table.Column<float>(type: "real", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_character_itemcooldowns", x => x.id);
                    table.ForeignKey(
                        name: "fk_character_itemcooldowns_characters_character_id",
                        column: x => x.character_id,
                        principalSchema: "fishMMO",
                        principalTable: "characters",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "character_quests",
                schema: "fishMMO",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    character_id = table.Column<long>(type: "bigint", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    progress = table.Column<int>(type: "integer", nullable: false),
                    completed = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_character_quests", x => x.id);
                    table.ForeignKey(
                        name: "fk_character_quests_characters_character_id",
                        column: x => x.character_id,
                        principalSchema: "fishMMO",
                        principalTable: "characters",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "character_skills",
                schema: "fishMMO",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    character_id = table.Column<long>(type: "bigint", nullable: false),
                    hash = table.Column<int>(type: "integer", nullable: false),
                    level = table.Column<int>(type: "integer", nullable: false),
                    cast_time_end = table.Column<float>(type: "real", nullable: false),
                    cooldown_end = table.Column<float>(type: "real", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_character_skills", x => x.id);
                    table.ForeignKey(
                        name: "fk_character_skills_characters_character_id",
                        column: x => x.character_id,
                        principalSchema: "fishMMO",
                        principalTable: "characters",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "character_guild",
                schema: "fishMMO",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    character_id = table.Column<long>(type: "bigint", nullable: false),
                    guild_id = table.Column<long>(type: "bigint", nullable: false),
                    rank = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_character_guild", x => x.id);
                    table.ForeignKey(
                        name: "fk_character_guild_characters_character_id",
                        column: x => x.character_id,
                        principalSchema: "fishMMO",
                        principalTable: "characters",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_character_guild_guild_info_guild_id",
                        column: x => x.guild_id,
                        principalSchema: "fishMMO",
                        principalTable: "guild_info",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_character_buffs_character_id",
                schema: "fishMMO",
                table: "character_buffs",
                column: "character_id");

            migrationBuilder.CreateIndex(
                name: "ix_character_buffs_character_id_name",
                schema: "fishMMO",
                table: "character_buffs",
                columns: new[] { "character_id", "name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_character_equipment_character_id",
                schema: "fishMMO",
                table: "character_equipment",
                column: "character_id");

            migrationBuilder.CreateIndex(
                name: "ix_character_equipment_character_id_slot",
                schema: "fishMMO",
                table: "character_equipment",
                columns: new[] { "character_id", "slot" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_character_guild_character_id",
                schema: "fishMMO",
                table: "character_guild",
                column: "character_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_character_guild_character_id_guild_id",
                schema: "fishMMO",
                table: "character_guild",
                columns: new[] { "character_id", "guild_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_character_guild_guild_id",
                schema: "fishMMO",
                table: "character_guild",
                column: "guild_id");

            migrationBuilder.CreateIndex(
                name: "ix_character_inventory_character_id",
                schema: "fishMMO",
                table: "character_inventory",
                column: "character_id");

            migrationBuilder.CreateIndex(
                name: "ix_character_inventory_character_id_slot",
                schema: "fishMMO",
                table: "character_inventory",
                columns: new[] { "character_id", "slot" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_character_itemcooldowns_character_id",
                schema: "fishMMO",
                table: "character_itemcooldowns",
                column: "character_id");

            migrationBuilder.CreateIndex(
                name: "ix_character_itemcooldowns_character_id_category",
                schema: "fishMMO",
                table: "character_itemcooldowns",
                columns: new[] { "character_id", "category" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_character_quests_character_id",
                schema: "fishMMO",
                table: "character_quests",
                column: "character_id");

            migrationBuilder.CreateIndex(
                name: "ix_character_quests_character_id_name",
                schema: "fishMMO",
                table: "character_quests",
                columns: new[] { "character_id", "name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_character_skills_character_id",
                schema: "fishMMO",
                table: "character_skills",
                column: "character_id");

            migrationBuilder.CreateIndex(
                name: "ix_character_skills_character_id_hash",
                schema: "fishMMO",
                table: "character_skills",
                columns: new[] { "character_id", "hash" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CharacterEntity_NameLowercase",
                schema: "fishMMO",
                table: "characters",
                column: "name_lowercase",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_characters_account",
                schema: "fishMMO",
                table: "characters",
                column: "account");

            migrationBuilder.CreateIndex(
                name: "ix_characters_name",
                schema: "fishMMO",
                table: "characters",
                column: "name");

            migrationBuilder.CreateIndex(
                name: "ix_guild_info_name",
                schema: "fishMMO",
                table: "guild_info",
                column: "name");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "accounts",
                schema: "fishMMO");

            migrationBuilder.DropTable(
                name: "character_buffs",
                schema: "fishMMO");

            migrationBuilder.DropTable(
                name: "character_equipment",
                schema: "fishMMO");

            migrationBuilder.DropTable(
                name: "character_guild",
                schema: "fishMMO");

            migrationBuilder.DropTable(
                name: "character_inventory",
                schema: "fishMMO");

            migrationBuilder.DropTable(
                name: "character_itemcooldowns",
                schema: "fishMMO");

            migrationBuilder.DropTable(
                name: "character_quests",
                schema: "fishMMO");

            migrationBuilder.DropTable(
                name: "character_skills",
                schema: "fishMMO");

            migrationBuilder.DropTable(
                name: "world_servers",
                schema: "fishMMO");

            migrationBuilder.DropTable(
                name: "guild_info",
                schema: "fishMMO");

            migrationBuilder.DropTable(
                name: "characters",
                schema: "fishMMO");
        }
    }
}
