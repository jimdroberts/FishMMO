// Saves Character Data in a SQLite database. We use SQLite for several reasons
//
// - SQLite is file based and works without having to setup a database server
//   - We can 'remove all ...' or 'modify all ...' easily via SQL queries
//   - A lot of people requested a SQL database and weren't comfortable with XML
//   - We can allow all kinds of character names, even chinese ones without
//     breaking the file system.
// - We will need MYSQL or similar when using multiple server instances later
//   and upgrading is trivial
// - XML is easier, but:
//   - we can't easily read 'just the class of a character' etc., but we need it
//     for character selection etc. often
//   - if each account is a folder that contains players, then we can't save
//     additional account info like password, banned, etc. unless we use an
//     additional account.xml file, which over-complicates everything
//   - there will always be forbidden file names like 'COM', which will cause
//     problems when people try to create accounts or characters with that name
//
// About item mall coins:
//   The payment provider's callback should add new orders to the
//   character_orders table. The server will then process them while the player
//   is ingame. Don't try to modify 'coins' in the character table directly.
//
// Tools to open sqlite database files:
//   Windows/OSX program: http://sqlitebrowser.org/
//   Firefox extension: https://addons.mozilla.org/de/firefox/addon/sqlite-manager/
//   Webhost: Adminer/PhpLiteAdmin
//
// About performance:
// - It's recommended to only keep the SQLite connection open while it's used.
//   MMO Servers use it all the time, so we keep it open all the time. This also
//   allows us to use transactions easily, and it will make the transition to
//   MYSQL easier.
// - Transactions are definitely necessary:
//   saving 100 players without transactions takes 3.6s
//   saving 100 players with transactions takes    0.38s
// - Using tr = conn.BeginTransaction() + tr.Commit() and passing it through all
//   the functions is ultra complicated. We use a BEGIN + END queries instead.
//
// Some benchmarks:
//   saving 100 players unoptimized: 4s
//   saving 100 players always open connection + transactions: 3.6s
//   saving 100 players always open connection + transactions + WAL: 3.6s
//   saving 100 players in 1 'using tr = ...' transaction: 380ms
//   saving 100 players in 1 BEGIN/END style transactions: 380ms
//   saving 100 players with XML: 369ms
//   saving 1000 players with mono-sqlite @ 2019-10-03: 843ms
//   saving 1000 players with sqlite-net  @ 2019-10-03:  90ms (!)
//
// Build notes:
// - requires Player settings to be set to '.NET' instead of '.NET Subset',
//   otherwise System.Data.dll causes ArgumentException.
// - requires sqlite3.dll x86 and x64 version for standalone (windows/mac/linux)
//   => found on sqlite.org website
// - requires libsqlite3.so x86 and armeabi-v7a for android
//   => compiled from sqlite.org amalgamation source with android ndk r9b linux

// database layout via .NET classes:
// https://github.com/praeclarum/sqlite-net/wiki/GettingStarted

using UnityEngine;
using System;
using System.IO;
using SQLite; // from https://github.com/praeclarum/sqlite-net

namespace Server
{
    public partial class Database
    {
        // file name
        public string databaseFile = "/Database.sqlite";

        // connection (public so it can be used by addons)
        public SQLiteConnection connection;

        private static Database _instance;
        public static Database Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = new Database();
                    _instance.Connect();
                }
                return _instance;
            }
        }

        public void Connect()
        {
			// database path: Application.dataPath is always relative to the project,
			// but we don't want it inside the Assets folder in the Editor (git etc.),
			// instead we put it above that.
			// we also use Path.Combine for platform independent paths
			// and we need persistentDataPath on android

			string path = "";
#if UNITY_EDITOR
			path = Directory.GetParent(Application.dataPath).FullName;
#elif UNITY_ANDROID
			path = Directory.GetParent(Application.persistentDataPath.FullName;
#elif UNITY_IOS
			path = Directory.GetParent(Application.persistentDataPath.FullName;
#else
			path = Directory.GetParent(Application.dataPath).FullName;
#endif

			// open connection
			// note: automatically creates database file if not created yet
			connection = new SQLiteConnection(path);

            // create tables if they don't exist yet or were deleted
            connection.CreateTable<worldServers>();
            connection.CreateTable<accounts>();
            connection.CreateTable<characters>();
            connection.CreateTable<character_inventory>();
            connection.CreateIndex(nameof(character_inventory), new[] { "character", "slot" });
            connection.CreateTable<character_equipment>();
            connection.CreateIndex(nameof(character_equipment), new[] { "character", "slot" });
            connection.CreateTable<character_itemcooldowns>();
            connection.CreateTable<character_skills>();
            connection.CreateIndex(nameof(character_skills), new[] { "character", "name" });
            connection.CreateTable<character_buffs>();
            connection.CreateIndex(nameof(character_buffs), new[] { "character", "name" });
            connection.CreateTable<character_quests>();
            connection.CreateIndex(nameof(character_quests), new[] { "character", "name" });
            connection.CreateTable<character_guild>();
            connection.CreateTable<guild_info>();

            Debug.Log("Database: Connected");
        }

        // close connection when Unity closes to prevent locking
        void OnApplicationQuit()
        {
            connection?.Close();
        }

        /* item mall ///////////////////////////////////////////////////////////////
        public List<long> GrabCharacterOrders(string characterName)
        {
            // grab new orders from the database and delete them immediately
            //
            // note: this requires an orderid if we want someone else to write to
            // the database too. otherwise deleting would delete all the new ones or
            // updating would update all the new ones. especially in sqlite.
            //
            // note: we could just delete processed orders, but keeping them in the
            // database is easier for debugging / support.
            List<long> result = new List<long>();
            List<character_orders> rows = connection.Query<character_orders>("SELECT * FROM character_orders WHERE character=? AND processed=0", characterName);
            foreach (character_orders row in rows)
            {
                result.Add(row.coins);
                connection.Execute("UPDATE character_orders SET processed=1 WHERE orderid=?", row.orderid);
            }
            return result;
        }*/
	}
}