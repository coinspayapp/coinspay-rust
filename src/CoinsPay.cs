// CoinsPay for Rust: sell VIP, kits and items on your server for crypto.
// Oxide (uMod) and Carbon. Docs: https://github.com/coinspayapp/coinspay-rust
//
// A player types /donate, picks an item and pays on the CoinsPay payment page. The plugin asks
// the CoinsPay API about the payment and delivers: groups, permissions, items, console commands.
// The plugin talks only to the CoinsPay API address in the config, and only for invoices.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Security;
using System.Text;
using System.Threading;
using CoinsPayRust;
using Newtonsoft.Json;
using Oxide.Core;
using Oxide.Game.Rust.Cui;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("CoinsPay", "CoinsPay", "0.1.1")]
    [Description("Sell VIP, kits and items for crypto: USDT, BTC, ETH and more, paid straight to your wallets")]
    public class CoinsPay : RustPlugin
    {
        private const string DataFile = "CoinsPay.json";
        private const int HttpTimeoutMs = 15000;
        private const int MaxBody = 2 * 1024 * 1024;

        private readonly ConcurrentQueue<Action> mainThread = new ConcurrentQueue<Action>();
        private readonly Dictionary<string, Dictionary<string, string>> texts = new Dictionary<string, Dictionary<string, string>>();
        private readonly List<string> chatCommands = new List<string>();
        private Shop shop;
        private Host host;
        private Timer pump;
        private Timer ticker;
        private long lastTick;
        private bool unloading;

        // ------------------------------------------------------------------ framework hooks

        protected override void LoadDefaultConfig() => Config.WriteObject(ConfigFile.Defaults(), true);

        protected override void LoadDefaultMessages()
        {
            lang.RegisterMessages(Texts.En, this, "en");
            lang.RegisterMessages(Texts.Ru, this, "ru");
        }

        private void Init()
        {
            permission.RegisterPermission(Shop.AdminPermission, this);
        }

        private void OnServerInitialized()
        {
            host = new Host(this);
            shop = new Shop(host, Version.ToString());
            CacheTexts();
            Settings settings = ReadSettingsOrEmpty();
            shop.Enable(settings);
            RegisterChatCommands(settings);
            foreach (string sub in new[] { "status", "pending", "check", "redeliver", "markdone", "give", "reload" })
            {
                string name = sub;
                cmd.AddConsoleCommand("coinspay." + name, this, arg => ConsoleAdmin(arg, name));
            }
            cmd.AddConsoleCommand(Ui.Command, this, UiCommand);
            cmd.AddChatCommand("coinspay", this, (player, command, args) => ChatAdmin(player, args));
            pump = timer.Every(0.2f, Pump);
            ticker = timer.Every(1f, Tick);
        }

        private void Unload()
        {
            unloading = true;
            pump?.Destroy();
            ticker?.Destroy();
            foreach (BasePlayer player in BasePlayer.activePlayerList)
            {
                CuiHelper.DestroyUi(player, Ui.MenuRoot);
                CuiHelper.DestroyUi(player, Ui.PayRoot);
            }
            shop?.Disable();
            shop = null;
            host = null;
        }

        private void OnPlayerConnected(BasePlayer player) => Ready(player);

        private void OnPlayerSleepEnded(BasePlayer player) => Ready(player);

        private void OnPlayerRespawned(BasePlayer player) => Ready(player);

        private void OnPlayerDisconnected(BasePlayer player, string reason)
        {
            if (player != null) shop?.PlayerLeft(player.UserIDString);
        }

        private void Ready(BasePlayer player)
        {
            if (player == null || shop == null) return;
            string id = player.UserIDString;
            // Hooks fire a moment before the player can take items: look again next frame.
            NextTick(() => shop?.PlayerReady(id));
        }

        private void Tick()
        {
            if (shop == null) return;
            long now = host.Now();
            if (now - lastTick < shop.Settings.PollSeconds * 1000L) return;
            lastTick = now;
            try
            {
                shop.Tick();
            }
            catch (Exception e)
            {
                PrintError("Poll failed: " + e);
            }
        }

        /// <summary>Runs HTTP answers on the main thread, where the game and the core live.</summary>
        private void Pump()
        {
            Action action;
            for (int n = 0; n < 50 && mainThread.TryDequeue(out action); n++)
            {
                try
                {
                    action();
                }
                catch (Exception e)
                {
                    PrintError("CoinsPay callback failed: " + e);
                }
            }
        }

        // ------------------------------------------------------------------ commands

        private void RegisterChatCommands(Settings settings)
        {
            foreach (string name in settings.ChatCommands)
            {
                if (chatCommands.Contains(name)) continue;
                chatCommands.Add(name);
                string command = name;
                cmd.AddChatCommand(command, this, (player, c, args) => shop?.Donate(new PlayerSender(this, player, false), command, args));
            }
        }

        private bool ConsoleAdmin(ConsoleSystem.Arg arg, string sub)
        {
            if (shop == null) return true;
            BasePlayer player = arg.Connection != null ? arg.Player() : null;
            if (arg.Connection != null && player == null) return true;
            ISender sender = player != null ? new PlayerSender(this, player, true) : (ISender)new ConsoleSender(this);
            shop.Admin(sender, sub, ArgsOf(arg));
            if (sub == "reload") RegisterChatCommands(shop.Settings);
            return true;
        }

        private void ChatAdmin(BasePlayer player, string[] args)
        {
            if (shop == null) return;
            var sender = new PlayerSender(this, player, false);
            string sub = args.Length > 0 ? args[0] : "";
            var rest = new string[Math.Max(0, args.Length - 1)];
            if (rest.Length > 0) Array.Copy(args, 1, rest, 0, rest.Length);
            shop.Admin(sender, sub, rest);
            if (sub == "reload") RegisterChatCommands(shop.Settings);
        }

        private bool UiCommand(ConsoleSystem.Arg arg)
        {
            BasePlayer player = arg.Player();
            if (player == null || shop == null) return true;
            shop.UiClick(new PlayerSender(this, player, false), ArgsOf(arg));
            return true;
        }

        /// <summary>The arguments as strings: Rust keeps them as StringView now.</summary>
        private static string[] ArgsOf(ConsoleSystem.Arg arg)
        {
            int count = arg.Args != null ? arg.Args.Length : 0;
            var args = new string[count];
            for (int i = 0; i < count; i++) args[i] = arg.GetString(i);
            return args;
        }

        // ------------------------------------------------------------------ settings and texts

        private Settings ReadSettings()
        {
            Config.Load();
            ConfigFile file = Config.ReadObject<ConfigFile>();
            CacheTexts();
            return Settings.From(file, CheckItem);
        }

        private Settings ReadSettingsOrEmpty()
        {
            try
            {
                return ReadSettings();
            }
            catch (Exception e)
            {
                PrintError("Cannot read oxide/config/CoinsPay.json: " + e.Message + ". Fix the file, then run coinspay.reload.");
                return Settings.From(new ConfigFile(), null);
            }
        }

        /// <summary>Unknown shortnames and paid Facepunch content (DLC, store skins) cannot be sold.</summary>
        private static string CheckItem(string shortname)
        {
            ItemDefinition def = ItemManager.FindItemDefinition(shortname);
            if (def == null) return "is not a Rust item shortname";
            if (def.steamDlc != null || def.steamItem != null) return "is paid Facepunch content (DLC or a store skin): the Facepunch server rules forbid giving it to players who did not buy it";
            return null;
        }

        private void CacheTexts()
        {
            texts["en"] = lang.GetMessages("en", this) ?? new Dictionary<string, string>();
            texts["ru"] = lang.GetMessages("ru", this) ?? new Dictionary<string, string>();
        }

        // ------------------------------------------------------------------ the host

        /// <summary>The core's view of the server.</summary>
        private sealed class Host : IHost
        {
            private readonly CoinsPay plugin;

            public Host(CoinsPay plugin)
            {
                this.plugin = plugin;
            }

            public string Describe()
            {
#if CARBON
                string framework = "Carbon";
#else
                string framework = "Oxide " + OxideMod.Version;
#endif
                return framework + ", Rust " + global::Rust.Protocol.printable;
            }

            public long Now() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

            public void Info(string message) => plugin.Puts(message);

            public void Warn(string message) => plugin.PrintWarning(message);

            public void Error(string message) => plugin.PrintError(message);

            public void Http(string method, string url, string body, Dictionary<string, string> headers, Action<int, string> done)
            {
                ConcurrentQueue<Action> queue = plugin.mainThread;
                ThreadPool.QueueUserWorkItem(_ =>
                {
                    int status;
                    string text;
                    try
                    {
                        Send(method, url, body, headers, out status, out text);
                    }
                    catch (Exception e)
                    {
                        // Nothing may escape a pool thread: on Mono it can take the server down.
                        status = 0;
                        text = e.Message;
                    }
                    queue.Enqueue(() => done(status, text));
                });
            }

            /// <summary>
            /// Oxide and Carbon accept any TLS certificate for the whole server. A payment plugin cannot:
            /// a forged "paid" answer would hand out items. So this request checks the certificate itself.
            /// </summary>
            private static void Send(string method, string url, string body, Dictionary<string, string> headers, out int status, out string text)
            {
                string seen = null;
                try
                {
                    var request = (HttpWebRequest)WebRequest.Create(url);
                    request.Method = method;
                    request.Timeout = HttpTimeoutMs;
                    request.ReadWriteTimeout = HttpTimeoutMs;
                    request.AllowAutoRedirect = false;
                    request.ServerCertificateValidationCallback = (sender, certificate, chain, errors) =>
                    {
                        if (errors != SslPolicyErrors.None) seen = errors + " for " + certificate?.Subject;
                        return errors == SslPolicyErrors.None;
                    };
                    foreach (KeyValuePair<string, string> h in headers)
                    {
                        switch (h.Key)
                        {
                            case "Accept": request.Accept = h.Value; break;
                            case "User-Agent": request.UserAgent = h.Value; break;
                            case "Content-Type": request.ContentType = h.Value; break;
                            default: request.Headers[h.Key] = h.Value; break;
                        }
                    }
                    if (body != null)
                    {
                        byte[] bytes = Encoding.UTF8.GetBytes(body);
                        request.ContentLength = bytes.Length;
                        using (Stream stream = request.GetRequestStream()) stream.Write(bytes, 0, bytes.Length);
                    }
                    using (var response = (HttpWebResponse)request.GetResponse())
                    {
                        status = (int)response.StatusCode;
                        text = Read(response);
                    }
                }
                catch (WebException e) when (e.Response is HttpWebResponse)
                {
                    using (var response = (HttpWebResponse)e.Response)
                    {
                        status = (int)response.StatusCode;
                        text = Read(response);
                    }
                }
                catch (Exception e)
                {
                    status = 0;
                    text = seen != null
                        ? "the TLS certificate of the server is not trusted: " + seen
                        : e.Message + (e.InnerException != null ? ": " + e.InnerException.Message : "");
                }
            }

            private static string Read(HttpWebResponse response)
            {
                using (Stream stream = response.GetResponseStream())
                {
                    if (stream == null) return "";
                    var buffer = new MemoryStream();
                    var chunk = new byte[8192];
                    int n;
                    while ((n = stream.Read(chunk, 0, chunk.Length)) > 0)
                    {
                        buffer.Write(chunk, 0, n);
                        if (buffer.Length > MaxBody) throw new IOException("the response is too large");
                    }
                    return Encoding.UTF8.GetString(buffer.ToArray());
                }
            }

            public void After(float seconds, Action action) => plugin.timer.Once(seconds, () =>
            {
                if (!plugin.unloading) action();
            });

            public Settings LoadSettings()
            {
                Settings s = plugin.ReadSettings();
                return s;
            }

            private static string DataPath => Path.Combine(Interface.Oxide.DataDirectory, DataFile);

            public string ReadData() => File.Exists(DataPath) ? File.ReadAllText(DataPath, Encoding.UTF8) : null;

            public void WriteData(string json)
            {
                string path = DataPath;
                string tmp = path + ".tmp";
                File.WriteAllText(tmp, json, new UTF8Encoding(false));
                if (!File.Exists(path))
                {
                    File.Move(tmp, path);
                    return;
                }
                try
                {
                    File.Replace(tmp, path, null);
                }
                catch (Exception)
                {
                    File.Copy(tmp, path, true);
                    File.Delete(tmp);
                }
            }

            public string KeepBrokenData()
            {
                string name = "CoinsPay.broken-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + ".json";
                File.Copy(DataPath, Path.Combine(Interface.Oxide.DataDirectory, name), true);
                return name;
            }

            public string Message(string key, string lang)
            {
                Dictionary<string, string> map;
                string text;
                return plugin.texts.TryGetValue(lang, out map) && map.TryGetValue(key, out text) ? text : null;
            }

            public string Language(string playerId) => plugin.lang.GetLanguage(playerId);

            public ISender Player(string playerId)
            {
                BasePlayer p = Online(playerId);
                return p != null ? new PlayerSender(plugin, p, false) : null;
            }

            public ISender FindPlayer(string nameOrId)
            {
                BasePlayer found = Online(nameOrId);
                if (found == null)
                {
                    foreach (BasePlayer p in BasePlayer.activePlayerList)
                    {
                        if (string.Equals(p.displayName, nameOrId, StringComparison.OrdinalIgnoreCase)) found = p;
                    }
                }
                return found != null ? new PlayerSender(plugin, found, false) : null;
            }

            public string KnownName(string playerId)
            {
                string name = plugin.covalence.Players.FindPlayerById(playerId)?.Name;
                return string.IsNullOrEmpty(name) || name == playerId ? null : name;
            }

            public bool Ready(string playerId)
            {
                BasePlayer p = Online(playerId);
                return p != null && !p.IsDead() && !p.IsSleeping() && !p.IsReceivingSnapshot;
            }

            public bool HasGroup(string playerId, string group) => plugin.permission.UserHasGroup(playerId, group);

            public bool HasOwnPermission(string playerId, string perm)
            {
                var data = plugin.permission.GetUserData(playerId);
                return data != null && data.Perms.Contains(perm);
            }

            public string AddGroup(string playerId, string playerName, string group)
            {
                if (!plugin.permission.GroupExists(group))
                {
                    plugin.permission.CreateGroup(group, group, 0);
                    plugin.PrintWarning("Created the missing group \"" + group + "\": give it its permissions");
                }
                plugin.permission.UpdateNickname(playerId, playerName);
                plugin.permission.AddUserGroup(playerId, group);
                return plugin.permission.UserHasGroup(playerId, group) ? null : "the framework did not add the group";
            }

            public string RemoveGroup(string playerId, string group)
            {
                plugin.permission.RemoveUserGroup(playerId, group);
                return null;
            }

            public string GrantPermission(string playerId, string playerName, string perm)
            {
                if (!plugin.permission.PermissionExists(perm)) return "no loaded plugin registers this permission";
                plugin.permission.UpdateNickname(playerId, playerName);
                plugin.permission.GrantUserPermission(playerId, perm, null);
                return HasOwnPermission(playerId, perm) ? null : "the framework did not grant the permission";
            }

            public string RevokePermission(string playerId, string perm)
            {
                plugin.permission.RevokeUserPermission(playerId, perm);
                return null;
            }

            public string GiveItem(string playerId, ItemSpec item)
            {
                BasePlayer p = Online(playerId);
                if (p == null) return "the player is not on the server";
                string problem = CheckItem(item.Shortname);
                if (problem != null) return item.Shortname + " " + problem;
                ItemDefinition def = ItemManager.FindItemDefinition(item.Shortname);
                int stack = Math.Max(1, def.stackable);
                for (int left = item.Amount; left > 0; left -= stack)
                {
                    Item created = ItemManager.Create(def, Math.Min(stack, left), 0UL);
                    if (created == null) return "the game could not create " + item.Shortname;
                    // Into the inventory; what does not fit drops at the player's feet.
                    p.GiveItem(created, BaseEntity.GiveItemReason.PickedUp);
                }
                return null;
            }

            public string Command(string command)
            {
                plugin.Puts("Running: " + command);
                ConsoleSystem.Run(ConsoleSystem.Option.Server, command);
                return null;
            }

            public void ShowUi(string playerId, string root, List<UiElement> elements)
            {
                BasePlayer p = Online(playerId);
                if (p == null) return;
                CuiHelper.DestroyUi(p, root);
                CuiHelper.AddUi(p, ToCui(elements));
            }

            public void CloseUi(string playerId, string root)
            {
                BasePlayer p = Online(playerId);
                if (p != null) CuiHelper.DestroyUi(p, root);
            }

            public void ConsoleLine(string playerId, string text) => Online(playerId)?.ConsoleMessage(text);

            private static BasePlayer Online(string playerId)
            {
                ulong id;
                if (string.IsNullOrEmpty(playerId) || !ulong.TryParse(playerId, out id)) return null;
                BasePlayer p = BasePlayer.FindByID(id);
                return p != null && p.IsConnected ? p : null;
            }
        }

        /// <summary>The core's plain rectangles as the framework's CUI elements.</summary>
        private static List<CuiElement> ToCui(List<UiElement> elements)
        {
            var cui = new List<CuiElement>();
            foreach (UiElement e in elements)
            {
                var rect = new CuiRectTransformComponent { AnchorMin = e.AnchorMin, AnchorMax = e.AnchorMax, OffsetMin = e.OffsetMin, OffsetMax = e.OffsetMax };
                string font = e.Bold ? "robotocondensed-bold.ttf" : "robotocondensed-regular.ttf";
                TextAnchor align;
                if (!Enum.TryParse(e.Align, out align)) align = TextAnchor.MiddleLeft;
                var element = new CuiElement { Name = e.Name, Parent = e.Parent };
                switch (e.Kind)
                {
                    case UiElement.Label:
                        element.Components.Add(new CuiTextComponent { Text = e.Text ?? "", FontSize = e.FontSize, Font = font, Align = align, Color = e.Color });
                        element.Components.Add(rect);
                        cui.Add(element);
                        break;
                    case UiElement.Input:
                        element.Components.Add(new CuiInputFieldComponent
                        {
                            Text = e.Text ?? "",
                            FontSize = e.FontSize,
                            Font = font,
                            Align = align,
                            Color = e.Color,
                            ReadOnly = true,
                            CharsLimit = 200,
                        });
                        element.Components.Add(rect);
                        cui.Add(element);
                        break;
                    case UiElement.Button:
                        element.Components.Add(new CuiButtonComponent { Command = e.Command, Color = e.Color });
                        element.Components.Add(rect);
                        cui.Add(element);
                        cui.Add(new CuiElement
                        {
                            Name = e.Name + ".text",
                            Parent = e.Name,
                            Components =
                            {
                                new CuiTextComponent { Text = e.Text ?? "", FontSize = e.FontSize, Font = font, Align = align, Color = "1 1 1 1" },
                                new CuiRectTransformComponent { AnchorMin = "0 0", AnchorMax = "1 1" },
                            },
                        });
                        break;
                    default:
                        element.Components.Add(new CuiImageComponent { Color = e.Color });
                        element.Components.Add(rect);
                        if (e.Cursor) element.Components.Add(new CuiNeedsCursorComponent());
                        cui.Add(element);
                        break;
                }
            }
            return cui;
        }

        /// <summary>A player: answers go to chat, or to the F1 console for console commands.</summary>
        private sealed class PlayerSender : ISender
        {
            private readonly CoinsPay plugin;
            private readonly BasePlayer player;
            private readonly bool console;

            public PlayerSender(CoinsPay plugin, BasePlayer player, bool console)
            {
                this.plugin = plugin;
                this.player = player;
                this.console = console;
            }

            public string Id => player.UserIDString;

            public string Name => player.displayName;

            public bool IsAdmin => plugin.permission.UserHasPermission(Id, Shop.AdminPermission);

            public void Reply(string text)
            {
                if (player == null || !player.IsConnected) return;
                if (console) player.ConsoleMessage(CoinsPayRust.Text.Plain(text));
                else player.ChatMessage(text);
            }
        }

        /// <summary>The server console and RCON: answers go to the log, which RCON clients see.</summary>
        private sealed class ConsoleSender : ISender
        {
            private readonly CoinsPay plugin;

            public ConsoleSender(CoinsPay plugin)
            {
                this.plugin = plugin;
            }

            public string Id => null;

            public string Name => "Console";

            public bool IsAdmin => true;

            public void Reply(string text)
            {
                foreach (string line in CoinsPayRust.Text.Plain(text).Split('\n')) plugin.Puts(line);
            }
        }
    }
}
