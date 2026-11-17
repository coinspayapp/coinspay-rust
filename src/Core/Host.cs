namespace CoinsPayRust
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// What the core needs from the game and the mod framework. Every call comes on the server's
    /// main thread, and every callback must come back on it too.
    /// </summary>
    public interface IHost
    {
        /// <summary>"Oxide 2.0.7900, Rust 2600" and the like: goes to the User-Agent and coinspay.status.</summary>
        string Describe();

        long Now();

        void Info(string message);

        void Warn(string message);

        void Error(string message);

        /// <summary>
        /// An HTTP request. done gets the status (0 for a network or TLS failure) and the body or
        /// the error text, later, on the main thread.
        /// </summary>
        void Http(string method, string url, string body, Dictionary<string, string> headers, Action<int, string> done);

        void After(float seconds, Action action);

        /// <summary>Reads and checks the config file again; throws with the reason when it cannot be read.</summary>
        Settings LoadSettings();

        /// <summary>The data file, or null when there is none yet.</summary>
        string ReadData();

        /// <summary>Writes the data file in one piece; throws on failure.</summary>
        void WriteData(string json);

        /// <summary>Copies a damaged data file aside for the admin; returns the copy's name.</summary>
        string KeepBrokenData();

        /// <summary>A text template from the framework's lang files ("en" or "ru"), or null to use the built-in one.</summary>
        string Message(string key, string lang);

        /// <summary>"ru" or "en", from the player's language in the framework.</summary>
        string Language(string playerId);

        /// <summary>An online player, or null.</summary>
        ISender Player(string playerId);

        /// <summary>An online player by Steam ID or exact name (ignoring case), or null.</summary>
        ISender FindPlayer(string nameOrId);

        /// <summary>The last known name of any player who has been on the server, or null.</summary>
        string KnownName(string playerId);

        /// <summary>In the game, awake and alive: items can go into the inventory.</summary>
        bool Ready(string playerId);

        bool HasGroup(string playerId, string group);

        /// <summary>The permission is granted to the player directly, not through a group.</summary>
        bool HasOwnPermission(string playerId, string permission);

        /// <summary>Each returns null on success or what went wrong.</summary>
        string AddGroup(string playerId, string playerName, string group);

        string RemoveGroup(string playerId, string group);

        string GrantPermission(string playerId, string playerName, string permission);

        string RevokePermission(string playerId, string permission);

        string GiveItem(string playerId, ItemSpec item);

        string Command(string command);

        /// <summary>Draws a window: the elements replace any window with the same root name.</summary>
        void ShowUi(string playerId, string root, List<UiElement> elements);

        void CloseUi(string playerId, string root);

        /// <summary>A line in the player's F1 console: Rust chat has no clickable links, the console can be copied from.</summary>
        void ConsoleLine(string playerId, string text);
    }

    /// <summary>Who typed a command: a player or the server console (Id null).</summary>
    public interface ISender
    {
        string Id { get; }

        string Name { get; }

        bool IsAdmin { get; }

        /// <summary>Rich text; the console gets it without tags.</summary>
        void Reply(string text);
    }
}
