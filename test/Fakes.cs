using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using CoinsPayRust;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace CoinsPayRust.Tests
{
    /// <summary>The CoinsPay merchant API in memory.</summary>
    public sealed class FakeApi
    {
        public const string Base = "https://coinspay.test/api/v1";
        public readonly Dictionary<string, JObject> Invoices = new Dictionary<string, JObject>();
        public readonly Dictionary<string, string> ByIdempotencyKey = new Dictionary<string, string>();
        public readonly List<(string Method, string Url, string Body, Dictionary<string, string> Headers)> Requests = new List<(string, string, string, Dictionary<string, string>)>();
        /// <summary>Statuses to answer the next requests with: 0 is a network failure.</summary>
        public readonly Queue<int> Fail = new Queue<int>();
        public string Access = "invoices";
        public bool TestKey = true;
        private int counter;

        public (int Status, string Body) Handle(string method, string url, string body, Dictionary<string, string> headers)
        {
            Requests.Add((method, url, body, headers));
            if (Fail.Count > 0)
            {
                int status = Fail.Dequeue();
                return status == 0 ? (0, "connection refused") : (status, "{\"error\":{\"code\":\"FAIL\",\"message\":\"failed\"}}");
            }
            string path = url.Substring(Base.Length);
            if (method == "GET" && path == "/shop")
            {
                return (200, new JObject { ["shop"] = new JObject { ["name"] = "Test shop" }, ["key"] = new JObject { ["test"] = TestKey, ["access"] = Access } }.ToString());
            }
            if (method == "POST" && path == "/invoices")
            {
                string key;
                headers.TryGetValue("Idempotency-Key", out key);
                string existing;
                if (key != null && ByIdempotencyKey.TryGetValue(key, out existing)) return (200, Wrap(Invoices[existing]));
                var req = JObject.Parse(body);
                counter++;
                string id = "inv_" + counter;
                var inv = new JObject
                {
                    ["id"] = id,
                    ["number"] = "T-" + counter,
                    ["status"] = "open",
                    ["currency"] = req["currency"],
                    ["amount"] = req["amount"],
                    ["amount_paid"] = "0",
                    ["amount_due"] = req["amount"],
                    ["reference"] = req["reference"],
                    ["pay_url"] = "https://coinspay.app/pay/k7m2-x9pq-" + counter.ToString("0000", CultureInfo.InvariantCulture),
                    ["test"] = TestKey,
                    ["paid_late"] = false,
                };
                Invoices[id] = inv;
                if (key != null) ByIdempotencyKey[key] = id;
                return (201, Wrap(inv));
            }
            if (method == "GET" && path.StartsWith("/invoices?status=paid", StringComparison.Ordinal))
            {
                var list = new JArray(Invoices.Values.Where(i => (string)i["status"] == "paid"));
                return (200, new JObject { ["invoices"] = list }.ToString());
            }
            if (method == "GET" && path.StartsWith("/invoices/", StringComparison.Ordinal))
            {
                string id = Uri.UnescapeDataString(path.Substring("/invoices/".Length));
                JObject inv;
                return Invoices.TryGetValue(id, out inv) ? (200, Wrap(inv)) : (404, "{\"error\":{\"code\":\"NOT_FOUND\",\"message\":\"no invoice\"}}");
            }
            return (404, "{}");
        }

        public JObject Last => Invoices["inv_" + counter];

        public void Pay(string id, string amount = null, bool late = false)
        {
            JObject inv = Invoices[id];
            inv["status"] = "paid";
            inv["amount_paid"] = amount ?? (string)inv["amount"];
            inv["amount_due"] = "0";
            inv["paid_late"] = late;
        }

        public void PayPart(string id, string paid, string due)
        {
            JObject inv = Invoices[id];
            inv["amount_paid"] = paid;
            inv["amount_due"] = due;
        }

        public void SetStatus(string id, string status) => Invoices[id]["status"] = status;

        private static string Wrap(JObject inv) => new JObject { ["invoice"] = inv }.ToString(Formatting.None);
    }

    public sealed class FakePlayer : ISender
    {
        public readonly List<string> Chat = new List<string>();

        public FakePlayer(string id, string name, bool admin = false)
        {
            Id = id;
            Name = name;
            IsAdmin = admin;
        }

        public string Id { get; }

        public string Name { get; }

        public bool IsAdmin { get; }

        public void Reply(string text) => Chat.Add(Text.Plain(text));

        public bool Saw(string fragment) => Chat.Any(c => c.Contains(fragment));
    }

    /// <summary>A server in memory: players, groups, permissions, items, commands, windows, files.</summary>
    public sealed class FakeHost : IHost
    {
        public readonly FakeApi Api = new FakeApi();
        public readonly Queue<Action> Pending = new Queue<Action>();
        public readonly List<string> Log = new List<string>();
        public readonly Dictionary<string, FakePlayer> Online = new Dictionary<string, FakePlayer>();
        public readonly HashSet<string> NotReady = new HashSet<string>();
        public readonly Dictionary<string, HashSet<string>> Groups = new Dictionary<string, HashSet<string>>();
        public readonly Dictionary<string, HashSet<string>> Perms = new Dictionary<string, HashSet<string>>();
        public readonly List<string> Commands = new List<string>();
        public readonly List<string> Given = new List<string>();
        public readonly Dictionary<string, List<UiElement>> Windows = new Dictionary<string, List<UiElement>>();
        public readonly List<string> Console = new List<string>();
        public long Time = 1000000000L;
        public string Data;
        public bool FailWrite;
        public string Language = "en";
        public Settings Next;

        public string Describe() => "Fake 1.0";

        public long Now() => Time;

        public void Info(string message) => Log.Add("INFO " + message);

        public void Warn(string message) => Log.Add("WARN " + message);

        public void Error(string message) => Log.Add("ERROR " + message);

        public void Http(string method, string url, string body, Dictionary<string, string> headers, Action<int, string> done)
        {
            Pending.Enqueue(() =>
            {
                var (status, text) = Api.Handle(method, url, body, headers);
                done(status, text);
            });
        }

        public void After(float seconds, Action action) => Pending.Enqueue(action);

        /// <summary>Answers every queued request, including the ones the answers start.</summary>
        public void Run()
        {
            for (int guard = 0; Pending.Count > 0 && guard < 1000; guard++) Pending.Dequeue()();
        }

        public Settings LoadSettings() => Next ?? throw new InvalidOperationException("no config");

        public string ReadData() => Data;

        public void WriteData(string json)
        {
            if (FailWrite) throw new System.IO.IOException("disk full");
            Data = json;
        }

        public string KeepBrokenData() => "CoinsPay.broken.json";

        public string Message(string key, string lang) => null;

        string IHost.Language(string playerId) => Language;

        public ISender Player(string playerId)
        {
            FakePlayer p;
            return playerId != null && Online.TryGetValue(playerId, out p) ? p : null;
        }

        public ISender FindPlayer(string nameOrId)
        {
            foreach (FakePlayer p in Online.Values) if (p.Id == nameOrId || string.Equals(p.Name, nameOrId, StringComparison.OrdinalIgnoreCase)) return p;
            return null;
        }

        public string KnownName(string playerId) => playerId == "76561198000000099" ? "Gone" : null;

        public bool Ready(string playerId) => Online.ContainsKey(playerId) && !NotReady.Contains(playerId);

        public bool HasGroup(string playerId, string group) => Set(Groups, playerId).Contains(group);

        public bool HasOwnPermission(string playerId, string permission) => Set(Perms, playerId).Contains(permission);

        public string AddGroup(string playerId, string playerName, string group)
        {
            Set(Groups, playerId).Add(group);
            return null;
        }

        public string RemoveGroup(string playerId, string group)
        {
            Set(Groups, playerId).Remove(group);
            return null;
        }

        public string GrantPermission(string playerId, string playerName, string permission)
        {
            Set(Perms, playerId).Add(permission);
            return null;
        }

        public string RevokePermission(string playerId, string permission)
        {
            Set(Perms, playerId).Remove(permission);
            return null;
        }

        public string GiveItem(string playerId, ItemSpec item)
        {
            if (!Ready(playerId)) return "not in the game";
            Given.Add(playerId + " " + item.Shortname + " " + item.Amount);
            return null;
        }

        public string Command(string command)
        {
            Commands.Add(command);
            return command.StartsWith("unknown", StringComparison.Ordinal) ? "no such command" : null;
        }

        public void ShowUi(string playerId, string root, List<UiElement> elements) => Windows[playerId + " " + root] = elements;

        public void CloseUi(string playerId, string root) => Windows.Remove(playerId + " " + root);

        public void ConsoleLine(string playerId, string text) => Console.Add(playerId + " " + text);

        public bool Warned(string fragment) => Log.Any(l => l.StartsWith("WARN", StringComparison.Ordinal) && l.Contains(fragment));

        private static HashSet<string> Set(Dictionary<string, HashSet<string>> map, string key)
        {
            HashSet<string> set;
            if (!map.TryGetValue(key, out set)) map[key] = set = new HashSet<string>();
            return set;
        }
    }
}
