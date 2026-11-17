namespace CoinsPayRust
{
    using System;
    using System.Collections.Generic;
    using System.Text.RegularExpressions;
    using Newtonsoft.Json;

    /// <summary>
    /// oxide/config/CoinsPay.json as written on disk. Lists and maps replace the defaults rather
    /// than merge into them, or Json.NET would add the sample packages to the owner's own.
    /// </summary>
    public sealed class ConfigFile
    {
        [JsonProperty("api", ObjectCreationHandling = ObjectCreationHandling.Replace)] public ApiConfig Api = new ApiConfig();
        [JsonProperty("server-id")] public string ServerId = "main";
        [JsonProperty("language")] public string Language = "auto";
        [JsonProperty("currency")] public string Currency = "USD";
        [JsonProperty("buyer-pays-fee")] public bool BuyerPaysFee;
        [JsonProperty("buyer-pays-network")] public bool BuyerPaysNetwork;
        [JsonProperty("invoice-lifetime-minutes")] public int LifetimeMinutes = 60;
        [JsonProperty("max-open-per-player")] public int MaxOpenPerPlayer = 2;
        [JsonProperty("cooldown-seconds")] public int CooldownSeconds = 10;
        [JsonProperty("poll-seconds")] public int PollSeconds = 10;
        [JsonProperty("chat-commands", ObjectCreationHandling = ObjectCreationHandling.Replace)] public List<string> ChatCommands = new List<string> { "donate" };
        [JsonProperty("menu")] public string Menu = "ui";
        [JsonProperty("return-url")] public string ReturnUrl = "";
        [JsonProperty("packages", ObjectCreationHandling = ObjectCreationHandling.Replace)] public Dictionary<string, PackageConfig> Packages;

        public sealed class ApiConfig
        {
            [JsonProperty("url")] public string Url = Settings.DefaultUrl;
            [JsonProperty("key")] public string Key = "";
        }

        /// <summary>A new config: the sample packages show every way to deliver.</summary>
        public static ConfigFile Defaults()
        {
            return new ConfigFile
            {
                Packages = new Dictionary<string, PackageConfig>
                {
                    ["vip-30"] = new PackageConfig
                    {
                        Name = "VIP for 30 days",
                        Description = new List<string> { "The vip group: its kits and perks" },
                        Price = "4.99",
                        Duration = "30d",
                        Groups = new List<string> { "vip" },
                    },
                    ["wood"] = new PackageConfig
                    {
                        Name = "10 000 wood",
                        Price = "1.00",
                        Items = new List<ItemSpec> { new ItemSpec { Shortname = "wood", Amount = 10000 } },
                    },
                    ["coins"] = new PackageConfig
                    {
                        Name = "1 000 coins",
                        Description = new List<string> { "Needs the Economics plugin" },
                        Price = "2.00",
                        Commands = new List<string> { "deposit {steamid} 1000" },
                    },
                },
            };
        }
    }

    public sealed class PackageConfig
    {
        [JsonProperty("name")] public string Name;
        [JsonProperty("description", NullValueHandling = NullValueHandling.Ignore, ObjectCreationHandling = ObjectCreationHandling.Replace)] public List<string> Description;
        [JsonProperty("price")] public string Price;
        [JsonProperty("currency", NullValueHandling = NullValueHandling.Ignore)] public string Currency;
        [JsonProperty("duration", NullValueHandling = NullValueHandling.Ignore)] public string Duration;
        [JsonProperty("groups", NullValueHandling = NullValueHandling.Ignore, ObjectCreationHandling = ObjectCreationHandling.Replace)] public List<string> Groups;
        [JsonProperty("permissions", NullValueHandling = NullValueHandling.Ignore, ObjectCreationHandling = ObjectCreationHandling.Replace)] public List<string> Permissions;
        [JsonProperty("items", NullValueHandling = NullValueHandling.Ignore, ObjectCreationHandling = ObjectCreationHandling.Replace)] public List<ItemSpec> Items;
        [JsonProperty("commands", NullValueHandling = NullValueHandling.Ignore, ObjectCreationHandling = ObjectCreationHandling.Replace)] public List<string> Commands;
        [JsonProperty("expire-commands", NullValueHandling = NullValueHandling.Ignore, ObjectCreationHandling = ObjectCreationHandling.Replace)] public List<string> ExpireCommands;
        [JsonProperty("require-online", DefaultValueHandling = DefaultValueHandling.Ignore)] public bool RequireOnline;
    }

    /// <summary>A package from the config, checked.</summary>
    public sealed class PackageDef
    {
        public string Id = "";
        public string Name = "";
        public List<string> Description = new List<string>();
        public decimal Price;
        public string Currency = "USD";
        public List<string> Groups = new List<string>();
        public List<string> Permissions = new List<string>();
        public List<ItemSpec> Items = new List<ItemSpec>();
        public List<string> Commands = new List<string>();
        public bool RequireOnline;
        /// <summary>0 for a permanent package.</summary>
        public long DurationMs;
        public List<string> ExpireCommands = new List<string>();

        public bool NeedsPlayer => Items.Count > 0 || RequireOnline;
    }

    /// <summary>The config, parsed and checked. Problems go to Problems: the plugin logs them on load.</summary>
    public sealed class Settings
    {
        public const string DefaultUrl = "https://coinspay.app/api/v1";
        private static readonly Regex Id = new Regex("^[A-Za-z0-9_-]{1,32}$");
        private static readonly Regex GroupName = new Regex(@"^[A-Za-z0-9_.\-]{1,64}$");
        private static readonly Regex PermissionName = new Regex(@"^[A-Za-z0-9_.\-]{1,128}$");
        private static readonly Regex ChatCommand = new Regex("^[a-z0-9_]{1,32}$");
        private static readonly Regex CurrencyCode = new Regex("^[A-Z]{3,5}$");

        public string ApiUrl = DefaultUrl;
        public string ApiKey = "";
        public string ServerId = "main";
        /// <summary>"auto", "ru" or "en".</summary>
        public string Language = "auto";
        public string Currency = "USD";
        public bool BuyerPaysFee;
        public bool BuyerPaysNetwork;
        public int LifetimeMinutes = 60;
        public int MaxOpenPerPlayer = 2;
        public int CooldownSeconds = 10;
        public int PollSeconds = 10;
        public List<string> ChatCommands = new List<string>();
        /// <summary>The store as a chat list instead of a window.</summary>
        public bool ChatMenu;
        public string ReturnUrl;
        public List<PackageDef> Packages = new List<PackageDef>();
        public List<string> Problems = new List<string>();

        public bool Configured => ApiKey.StartsWith("cpk_", StringComparison.Ordinal) && ApiKey.Length > 10;

        public bool TestKey => ApiKey.StartsWith("cpk_test_", StringComparison.Ordinal);

        public PackageDef Package(string id)
        {
            if (id == null) return null;
            foreach (PackageDef p in Packages) if (string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase)) return p;
            return null;
        }

        /// <summary>
        /// Checks the config. checkItem says what is wrong with an item shortname (unknown, DLC), or
        /// null when it is fine; without a game, tests pass null.
        /// </summary>
        public static Settings From(ConfigFile c, Func<string, string> checkItem)
        {
            var s = new Settings();
            if (c == null) c = new ConfigFile();
            var p = s.Problems;

            string url = (c.Api?.Url ?? DefaultUrl).Trim();
            if (!url.StartsWith("https://", StringComparison.Ordinal) && !url.StartsWith("http://", StringComparison.Ordinal))
            {
                p.Add("api.url must start with https://");
                url = DefaultUrl;
            }
            s.ApiUrl = url.TrimEnd('/');
            s.ApiKey = (c.Api?.Key ?? "").Trim();
            if (!s.Configured) p.Add("api.key is empty: create an \"Invoices only\" key in the CoinsPay dashboard");

            string server = (c.ServerId ?? "main").Trim();
            if (!Id.IsMatch(server))
            {
                p.Add("server-id may hold only letters, digits, _ and -, up to 32 characters");
                server = "main";
            }
            s.ServerId = server;
            string lang = (c.Language ?? "auto").Trim().ToLowerInvariant();
            s.Language = lang == "ru" || lang == "en" ? lang : "auto";
            s.Currency = ParseCurrency(c.Currency, "USD", "currency", p);
            s.BuyerPaysFee = c.BuyerPaysFee;
            s.BuyerPaysNetwork = c.BuyerPaysNetwork;
            s.LifetimeMinutes = Clamp(c.LifetimeMinutes, 15, 43200);
            s.MaxOpenPerPlayer = Clamp(c.MaxOpenPerPlayer, 1, 10);
            s.CooldownSeconds = Clamp(c.CooldownSeconds, 0, 3600);
            s.PollSeconds = Clamp(c.PollSeconds, 5, 300);
            s.ChatMenu = string.Equals((c.Menu ?? "ui").Trim(), "chat", StringComparison.OrdinalIgnoreCase);
            string ret = (c.ReturnUrl ?? "").Trim();
            s.ReturnUrl = ret.StartsWith("https://", StringComparison.Ordinal) || ret.StartsWith("http://", StringComparison.Ordinal) ? ret : null;

            foreach (string raw in c.ChatCommands ?? new List<string>())
            {
                string name = (raw ?? "").Trim().TrimStart('/').ToLowerInvariant();
                if (!ChatCommand.IsMatch(name)) p.Add("chat-commands: \"" + raw + "\" may hold only a-z, 0-9 and _");
                else if (!s.ChatCommands.Contains(name)) s.ChatCommands.Add(name);
            }
            if (s.ChatCommands.Count == 0) s.ChatCommands.Add("donate");

            if (c.Packages != null)
            {
                foreach (KeyValuePair<string, PackageConfig> e in c.Packages)
                {
                    PackageDef def = ParsePackage(e.Key, e.Value, s.Currency, checkItem, p);
                    if (def == null) continue;
                    if (s.Package(def.Id) != null) p.Add("packages." + def.Id + ": the id repeats (ids ignore case)");
                    else s.Packages.Add(def);
                }
            }
            return s;
        }

        private static PackageDef ParsePackage(string id, PackageConfig m, string currency, Func<string, string> checkItem, List<string> p)
        {
            string where = "packages." + id;
            if (id == null || !Id.IsMatch(id))
            {
                p.Add(where + ": the id may hold only letters, digits, _ and -, up to 32 characters");
                return null;
            }
            if (m == null)
            {
                p.Add(where + " is empty");
                return null;
            }
            decimal price;
            if (!Text.TryParsePrice(m.Price, out price))
            {
                p.Add(where + ".price must be a number above zero, like \"4.99\"");
                return null;
            }
            var def = new PackageDef
            {
                Id = id,
                Name = string.IsNullOrWhiteSpace(m.Name) ? id : m.Name.Trim(),
                Description = Clean(m.Description),
                Price = price,
                Currency = ParseCurrency(m.Currency, currency, where + ".currency", p),
                Commands = Clean(m.Commands),
                ExpireCommands = Clean(m.ExpireCommands),
                RequireOnline = m.RequireOnline,
            };
            foreach (string g in Clean(m.Groups))
            {
                if (GroupName.IsMatch(g)) def.Groups.Add(g.ToLowerInvariant());
                else p.Add(where + ".groups: \"" + g + "\" is not a group name");
            }
            foreach (string perm in Clean(m.Permissions))
            {
                if (PermissionName.IsMatch(perm)) def.Permissions.Add(perm.ToLowerInvariant());
                else p.Add(where + ".permissions: \"" + perm + "\" is not a permission name");
            }
            foreach (ItemSpec item in m.Items ?? new List<ItemSpec>())
            {
                string shortname = item?.Shortname?.Trim() ?? "";
                if (shortname.Length == 0) continue;
                if (item.Amount < 1 || item.Amount > 1000000)
                {
                    p.Add(where + ".items: " + shortname + " needs an amount from 1 to 1000000");
                    return null;
                }
                string problem = checkItem != null ? checkItem(shortname) : null;
                if (problem != null)
                {
                    p.Add(where + ".items: " + shortname + " " + problem);
                    return null;
                }
                def.Items.Add(new ItemSpec { Shortname = shortname, Amount = item.Amount });
            }
            if (def.Groups.Count + def.Permissions.Count + def.Items.Count + def.Commands.Count == 0)
            {
                p.Add(where + ": nothing to deliver, add groups, permissions, items or commands");
                return null;
            }
            long duration = Text.ParseDuration(m.Duration);
            if (duration < 0)
            {
                p.Add(where + ".duration: use 30d, 12h, 90m, 2w or 1mo");
                return null;
            }
            def.DurationMs = duration;
            if (duration == 0 && def.ExpireCommands.Count > 0) p.Add(where + ": expire-commands without duration never run");
            if (duration > 0 && def.Groups.Count + def.Permissions.Count + def.ExpireCommands.Count == 0)
            {
                p.Add(where + ": duration ends nothing: only groups, permissions and expire-commands are taken back");
            }
            return def;
        }

        private static string ParseCurrency(string value, string fallback, string where, List<string> p)
        {
            if (string.IsNullOrWhiteSpace(value)) return fallback;
            string v = value.Trim().ToUpperInvariant();
            if (CurrencyCode.IsMatch(v)) return v;
            p.Add(where + ": \"" + value + "\" is not a currency code like USD");
            return fallback;
        }

        private static List<string> Clean(List<string> list)
        {
            var result = new List<string>();
            if (list == null) return result;
            foreach (string s in list) if (!string.IsNullOrWhiteSpace(s)) result.Add(s.Trim());
            return result;
        }

        private static int Clamp(int v, int min, int max) => Math.Max(min, Math.Min(max, v));
    }
}
