namespace CoinsPayRust
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Security.Cryptography;
    using System.Text;
    using Newtonsoft.Json;
    using Newtonsoft.Json.Linq;

    /// <summary>
    /// The plugin without the game: purchase, polling, delivery, timed grants and the admin
    /// commands. The framework glue only wires commands, hooks and timers to it. Everything runs
    /// on the server's main thread.
    /// </summary>
    public sealed class Shop
    {
        public const string AdminPermission = "coinspay.admin";
        private const long CheckOpenAfterMs = 120000L;
        private const long CheckOpenEveryMs = 60000L;
        private const long CheckExpiredForMs = 86400000L;
        private const long CheckExpiredEveryMs = 15 * 60000L;
        private const long PollStaleMs = 120000L;
        private const int ChecksPerTick = 10;

        private readonly IHost host;
        private readonly string version;
        private readonly Store store = new Store();
        private readonly Dictionary<string, long> lastCreated = new Dictionary<string, long>();
        private readonly Dictionary<string, int> inFlight = new Dictionary<string, int>();
        private readonly HashSet<string> reportedMismatch = new HashSet<string>();
        /// <summary>The order whose payment window a player has open.</summary>
        private readonly Dictionary<string, string> payWindow = new Dictionary<string, string>();

        private Settings settings = new Settings();
        private ApiClient api;
        private ShopInfo shop;
        private string lastError;
        private bool dataOk;
        private bool polling;
        private long pollStartedAt;

        public Shop(IHost host, string version)
        {
            this.host = host;
            this.version = version;
        }

        public Settings Settings => settings;

        public Store Store => store;

        public string LastError => lastError;

        // ------------------------------------------------------------------ lifecycle

        /// <summary>Loads the data file and the settings. A damaged data file turns the store off until it is fixed.</summary>
        public void Enable(Settings s)
        {
            LoadData();
            Apply(s);
        }

        public void Disable()
        {
            if (dataOk) Save();
        }

        private void LoadData()
        {
            string json;
            try
            {
                json = host.ReadData();
            }
            catch (Exception e)
            {
                dataOk = false;
                host.Error("Cannot read the CoinsPay data file: " + e.Message + ". The store is off until it can be read.");
                return;
            }
            List<Order> crashed;
            try
            {
                crashed = store.Load(json);
            }
            catch (JsonException e)
            {
                dataOk = false;
                string copy = host.KeepBrokenData();
                host.Error("The CoinsPay data file is damaged, copied to " + copy + ": " + e.Message
                    + ". The store is off: fix or remove the file, then run coinspay.reload.");
                return;
            }
            dataOk = true;
            foreach (Order o in crashed)
            {
                host.Warn("Invoice " + o.Label + " (" + o.PackageId + " for " + o.PlayerName + ") was being delivered when the server stopped."
                    + " Check whether the player got it, then run coinspay.redeliver " + o.Label + " or coinspay.markdone " + o.Label);
            }
            if (crashed.Count > 0) Save();
        }

        private void Apply(Settings s)
        {
            settings = s;
            foreach (string p in s.Problems) host.Warn("Config: " + p);
            api = s.Configured ? new ApiClient(host, s.ApiUrl, s.ApiKey, "CoinsPay-Rust/" + version + " (" + host.Describe() + ")") : null;
            shop = null;
            polling = false;
            host.Info("Loaded " + s.Packages.Count + " item(s); server-id " + s.ServerId + (s.TestKey ? "; TEST key" : ""));
            CheckKey();
        }

        private void CheckKey()
        {
            ApiClient client = api;
            if (client == null) return;
            client.Shop((info, error) =>
            {
                if (client != api) return;
                if (error != null)
                {
                    lastError = error.ToString();
                    host.Warn("Cannot check the API key: " + error + (error.Status == 401 ? " (the key is wrong or revoked)" : ""));
                    return;
                }
                shop = info;
                host.Info("Connected to CoinsPay shop \"" + info.Name + "\" with a " + (info.Test ? "test" : "live") + " key");
                if (info.Access == "full")
                {
                    host.Warn("This API key has full access, payouts included. The plugin needs only invoices:"
                        + " create an \"Invoices only\" key in the dashboard and put it in the config.");
                }
            });
        }

        // ------------------------------------------------------------------ the store

        /// <summary>/donate [item]: the window, the chat list, or straight to an invoice.</summary>
        public void Donate(ISender player, string command, string[] args)
        {
            if (player.Id == null)
            {
                Say(player, "PlayersOnly");
                return;
            }
            if (args.Length > 0)
            {
                Buy(player, args[0], null);
                return;
            }
            if (settings.ChatMenu) ChatMenu(player, command);
            else ShowMenu(player, 0);
        }

        /// <summary>Clicks in the windows: "buy id", "page n", "close", "closepay".</summary>
        public void UiClick(ISender player, string[] args)
        {
            if (player.Id == null || args.Length == 0) return;
            switch (args[0])
            {
                case "buy":
                    host.CloseUi(player.Id, Ui.MenuRoot);
                    if (args.Length > 1) Buy(player, args[1], null);
                    break;
                case "page":
                    int page;
                    if (args.Length > 1 && int.TryParse(args[1], NumberStyles.None, CultureInfo.InvariantCulture, out page)) ShowMenu(player, page);
                    break;
                case "close":
                    host.CloseUi(player.Id, Ui.MenuRoot);
                    break;
                case "closepay":
                    host.CloseUi(player.Id, Ui.PayRoot);
                    payWindow.Remove(player.Id);
                    break;
            }
        }

        private void ShowMenu(ISender player, int page)
        {
            string lang = Lang(player.Id);
            host.ShowUi(player.Id, Ui.MenuRoot, Ui.Menu(TextsFor(lang), settings.Packages, page, lang == "ru"));
        }

        private void ChatMenu(ISender player, string command)
        {
            if (settings.Packages.Count == 0)
            {
                Say(player, "MenuEmpty");
                return;
            }
            string lang = Lang(player.Id);
            var lines = new StringBuilder(Msg(lang, "Prefix") + Text.Fill(Msg(lang, "ChatHeader"), Text.Vars("command", command)));
            foreach (PackageDef p in settings.Packages)
            {
                string duration = p.DurationMs > 0 ? Text.Fill(Msg(lang, "ChatFor"), Text.Vars("duration", Text.Duration(p.DurationMs, lang == "ru"))) : "";
                lines.Append('\n').Append(Text.Fill(Msg(lang, "ChatLine"), Text.Vars("id", p.Id, "name", p.Name, "price", Text.Amount(p.Price), "currency", p.Currency, "duration", duration)));
            }
            player.Reply(lines.ToString());
        }

        // ------------------------------------------------------------------ purchase

        /// <summary>
        /// Creates an invoice for buyer. also is the admin of coinspay.give: it gets a copy of the
        /// outcome, and the link itself when the buyer is not in the game.
        /// </summary>
        public void Buy(ISender buyer, string packageId, ISender also)
        {
            ISender report = also ?? buyer;
            if (buyer.Id == null)
            {
                Say(report, "PlayersOnly");
                return;
            }
            if (!dataOk)
            {
                Say(report, "Error");
                return;
            }
            PackageDef p = settings.Package(packageId);
            if (p == null)
            {
                Say(report, "UnknownPackage", Text.Vars("id", Text.Escape(packageId)));
                return;
            }
            ApiClient client = api;
            if (client == null)
            {
                Say(report, "NotConfigured");
                return;
            }
            string playerId = buyer.Id;
            int open = store.OpenFor(playerId) + Count(inFlight, playerId);
            if (open >= settings.MaxOpenPerPlayer)
            {
                Say(report, "TooManyOpen", Text.Vars("count", open.ToString(CultureInfo.InvariantCulture)));
                return;
            }
            long now = host.Now();
            long last;
            if (also == null && lastCreated.TryGetValue(playerId, out last) && now - last < settings.CooldownSeconds * 1000L)
            {
                long wait = (settings.CooldownSeconds * 1000L - (now - last) + 999) / 1000;
                Say(buyer, "Cooldown", Text.Vars("seconds", wait.ToString(CultureInfo.InvariantCulture)));
                return;
            }
            lastCreated[playerId] = now;
            inFlight[playerId] = Count(inFlight, playerId) + 1;
            if (also == null) Say(buyer, "Creating", Text.Vars("name", p.Name));

            Settings s = settings;
            string name = buyer.Name ?? playerId;
            string reference = "rust:" + s.ServerId + ":" + playerId + ":" + p.Id + ":" + Nonce();
            var body = new JObject
            {
                ["currency"] = p.Currency,
                ["amount"] = Text.Amount(p.Price),
                ["title"] = Text.Cut(Text.Plain(p.Name), 200),
                ["description"] = Text.Cut("Rust: " + Text.Plain(name) + " (" + playerId + ", " + s.ServerId + ")", 1000),
                ["reference"] = reference,
                ["lifetime_minutes"] = s.LifetimeMinutes,
                ["buyer_pays_fee"] = s.BuyerPaysFee,
                ["buyer_pays_network"] = s.BuyerPaysNetwork,
            };
            if (s.ReturnUrl != null) body["return_url"] = s.ReturnUrl;

            client.CreateInvoice(body, reference, (invoice, error) =>
            {
                inFlight[playerId] = Math.Max(0, Count(inFlight, playerId) - 1);
                ISender online = host.Player(playerId);
                if (error != null)
                {
                    lastError = error.ToString();
                    host.Warn("Cannot create an invoice for " + name + " (" + p.Id + "): " + error);
                    if (online != null) Say(online, error.RateLimited ? "RateLimited" : "Error");
                    also?.Reply(error.ToString());
                    return;
                }
                long created = host.Now();
                var o = new Order
                {
                    Id = invoice.Id,
                    Number = invoice.Number,
                    Reference = reference,
                    PlayerId = playerId,
                    PlayerName = name,
                    PackageId = p.Id,
                    PackageName = p.Name,
                    Price = Text.Amount(p.Price),
                    Currency = p.Currency,
                    Groups = new List<string>(p.Groups),
                    Permissions = new List<string>(p.Permissions),
                    Items = Copy(p.Items),
                    Commands = new List<string>(p.Commands),
                    RequireOnline = p.RequireOnline,
                    DurationMs = p.DurationMs,
                    ExpireCommands = new List<string>(p.ExpireCommands),
                    PayUrl = invoice.PayUrl ?? "",
                    Test = invoice.Test,
                    CreatedAt = created,
                    ExpiresAt = created + s.LifetimeMinutes * 60000L,
                };
                store.Add(o);
                Save();
                host.Info("Invoice " + o.Label + " for " + o.PlayerName + ": " + o.PackageId + ", " + o.Price + " " + o.Currency);
                if (online != null) ShowPayment(online, o);
                if (also != null)
                {
                    var v = Text.Vars("id", o.Label, "name", o.PackageName, "player", o.PlayerName, "price", o.Price, "currency", o.Currency, "url", o.PayUrl);
                    Say(also, online != null ? "AdminGave" : "AdminGaveLink", v);
                }
            });
        }

        private void ShowPayment(ISender player, Order o)
        {
            string lang = Lang(player.Id);
            string url = Text.WithLang(o.PayUrl, lang);
            var v = Text.Vars("name", o.PackageName, "price", o.Price, "currency", o.Currency, "url", url,
                "short", Text.ShortUrl(o.PayUrl), "minutes", settings.LifetimeMinutes.ToString(CultureInfo.InvariantCulture));
            Say(player, "Created", v);
            player.Reply(Text.Fill(Msg(lang, "ShortLink"), v));
            host.ConsoleLine(player.Id, Text.Plain(Text.Fill(Msg(lang, "ConsoleLink"), v)));
            Say(player, "F1Hint");
            Say(player, "LinkLife", v);
            if (o.Test) Say(player, "TestMode");
            if (settings.ChatMenu) return;
            host.ShowUi(player.Id, Ui.PayRoot, Ui.Payment(TextsFor(lang), o, o.PayUrl, settings.LifetimeMinutes));
            payWindow[player.Id] = o.Id;
        }

        private void ClosePayment(Order o)
        {
            string open;
            if (!payWindow.TryGetValue(o.PlayerId, out open) || open != o.Id) return;
            payWindow.Remove(o.PlayerId);
            if (host.Player(o.PlayerId) != null) host.CloseUi(o.PlayerId, Ui.PayRoot);
        }

        // ------------------------------------------------------------------ polling

        /// <summary>Runs every poll-seconds: timed grants, waiting deliveries, then the API.</summary>
        public void Tick()
        {
            if (!dataOk) return;
            long now = host.Now();
            ExpireGrants(now);
            if (store.Prune(now)) Save();
            foreach (Order o in store.Orders())
            {
                if (o.State == OrderState.Waiting && host.Ready(o.PlayerId)) Deliver(o, null);
            }
            ApiClient client = api;
            if (client == null) return;
            if (polling && now - pollStartedAt < PollStaleMs) return;
            var watched = new List<Order>();
            foreach (Order o in store.Orders()) if (o.Watched(now)) watched.Add(o);
            if (watched.Count == 0) return;

            polling = true;
            pollStartedAt = now;
            client.PaidInvoices(100, (list, error) =>
            {
                if (client != api)
                {
                    polling = false;
                    return;
                }
                if (error != null)
                {
                    NoteError(error);
                    polling = false;
                    return;
                }
                var paid = new Dictionary<string, Invoice>();
                foreach (Invoice i in list) if (i.Id != null) paid[i.Id] = i;
                long at = host.Now();
                foreach (Order o in watched)
                {
                    Invoice i;
                    if (paid.TryGetValue(o.Id, out i)) Handle(o, i, at);
                }
                var due = new List<Order>();
                foreach (Order o in watched)
                {
                    if (due.Count >= ChecksPerTick) break;
                    if (!o.Watched(at) || paid.ContainsKey(o.Id)) continue;
                    bool isDue = o.State == OrderState.Open
                        ? at - o.CreatedAt >= CheckOpenAfterMs && at - o.LastCheckAt >= CheckOpenEveryMs
                        : at - o.ClosedAt < CheckExpiredForMs && at - o.LastCheckAt >= CheckExpiredEveryMs;
                    if (isDue) due.Add(o);
                }
                CheckNext(client, due, 0, false);
            });
        }

        private void CheckNext(ApiClient client, List<Order> due, int index, bool failed)
        {
            if (index >= due.Count)
            {
                if (!failed) lastError = null;
                polling = false;
                return;
            }
            Order o = due[index];
            o.LastCheckAt = host.Now();
            client.Invoice(o.Id, (invoice, error) =>
            {
                if (client != api)
                {
                    polling = false;
                    return;
                }
                if (error != null)
                {
                    NoteError(error);
                    if (error.Status == 404) host.Warn("Invoice " + o.Label + " is not found with the current key: was the key changed to another shop?");
                    if (error.Retryable)
                    {
                        polling = false;
                        return;
                    }
                    CheckNext(client, due, index + 1, true);
                    return;
                }
                Handle(o, invoice, host.Now());
                CheckNext(client, due, index + 1, failed);
            });
        }

        private void NoteError(ApiError error)
        {
            string text = error.ToString();
            if (text != lastError) host.Warn("CoinsPay API: " + text);
            lastError = text;
        }

        private void Handle(Order o, Invoice i, long now)
        {
            Decision d = Decision.Decide(o, i, host.Ready(o.PlayerId));
            ISender player = host.Player(o.PlayerId);
            var v = Text.Vars("name", o.PackageName, "amount", Text.Amount(i.Amount), "paid", Text.Amount(i.AmountPaid),
                "due", Text.Amount(i.AmountDue), "currency", i.Currency ?? o.Currency);
            switch (d.Action)
            {
                case DecisionAction.Deliver:
                    Deliver(o, i);
                    break;
                case DecisionAction.WaitPlayer:
                    o.State = OrderState.Waiting;
                    o.ClosedAt = now;
                    Save();
                    host.Info("Invoice " + o.Label + " is paid; " + o.PlayerName + " gets " + o.PackageId + " when in the game and alive");
                    if (player != null) Say(player, "PaidWaiting", v);
                    ClosePayment(o);
                    break;
                case DecisionAction.Partial:
                    o.ReportedPaid = Text.Amount(i.AmountPaid);
                    Save();
                    if (player != null) Say(player, "Partial", v);
                    break;
                case DecisionAction.Expired:
                case DecisionAction.Cancelled:
                    bool expired = d.Action == DecisionAction.Expired;
                    o.State = expired ? OrderState.Expired : OrderState.Cancelled;
                    o.ClosedAt = now;
                    Save();
                    if (player != null) Say(player, expired ? "Expired" : "Cancelled", v);
                    ClosePayment(o);
                    break;
                case DecisionAction.Mismatch:
                    if (reportedMismatch.Add(o.Id)) host.Warn("Invoice " + o.Label + " is NOT delivered: " + d.Reason);
                    break;
            }
        }

        /// <summary>A player joined, woke up or respawned: paid items that waited for them go out now.</summary>
        public void PlayerReady(string playerId)
        {
            if (!dataOk || !host.Ready(playerId)) return;
            foreach (Order o in store.Orders())
            {
                if (o.State == OrderState.Waiting && o.PlayerId == playerId) Deliver(o, null);
            }
        }

        public void PlayerLeft(string playerId) => payWindow.Remove(playerId);

        // ------------------------------------------------------------------ delivery

        private void Deliver(Order o, Invoice invoice)
        {
            if (o.State == OrderState.Delivering || o.State == OrderState.Delivered) return;
            long now = host.Now();
            if (o.NeedsPlayer && !host.Ready(o.PlayerId))
            {
                if (o.State != OrderState.Waiting)
                {
                    o.State = OrderState.Waiting;
                    o.ClosedAt = now;
                    Save();
                }
                return;
            }
            OrderState before = o.State;
            o.State = OrderState.Delivering;
            // Saved before anything is handed out: a crash from here on leaves Review, never a second delivery.
            if (!Save())
            {
                o.State = before;
                return;
            }

            var tokens = new List<string>();
            foreach (string g in o.Groups) tokens.Add("group:" + g);
            foreach (string p in o.Permissions) tokens.Add("perm:" + p);
            // Decided before granting: what the player already had from elsewhere is not ours to take back.
            var ours = new List<string>();
            foreach (string t in tokens) if (!Has(o.PlayerId, t) || OwnedByUs(o.PlayerId, t)) ours.Add(t);

            foreach (string g in o.Groups) Report(host.AddGroup(o.PlayerId, o.PlayerName, g), "group " + g, o);
            foreach (string p in o.Permissions) Report(host.GrantPermission(o.PlayerId, o.PlayerName, p), "permission " + p, o);
            foreach (ItemSpec item in o.Items) Report(host.GiveItem(o.PlayerId, item), "item " + item.Shortname + " x" + item.Amount, o);
            var vars = CommandVars(o, invoice);
            foreach (string command in o.Commands) RunCommand(Text.Fill(command, vars), o);

            Grant grant = null;
            if (o.DurationMs > 0) grant = ExtendGrant(o, tokens, ours, now);
            else foreach (string t in tokens) store.AddPermanent(o.PlayerId, t);
            o.State = OrderState.Delivered;
            o.ClosedAt = now;
            Save();
            host.Info("Delivered " + o.PackageId + " to " + o.PlayerName + " (invoice " + o.Label + ")");

            ISender player = host.Player(o.PlayerId);
            if (player == null) return;
            Say(player, "Paid", Text.Vars("name", o.PackageName));
            if (grant != null) Say(player, "PaidUntil", Text.Vars("name", o.PackageName, "left", Text.Duration(grant.ExpiresAt - now, Lang(player.Id) == "ru")));
            ClosePayment(o);
        }

        private Grant ExtendGrant(Order o, List<string> tokens, List<string> ours, long now)
        {
            Grant g = store.Grant(o.PlayerId, o.PackageId);
            if (g == null)
            {
                g = new Grant { PlayerId = o.PlayerId, PackageId = o.PackageId, ExpiresAt = now + o.DurationMs };
                store.AddGrant(g);
            }
            else
            {
                g.ExpiresAt = Math.Max(now, g.ExpiresAt) + o.DurationMs;
            }
            g.PlayerName = o.PlayerName;
            g.PackageName = o.PackageName;
            g.Grants = tokens;
            foreach (string t in ours) if (!g.Owned.Contains(t)) g.Owned.Add(t);
            g.ExpireCommands = new List<string>(o.ExpireCommands);
            g.LastOrder = o.Id;
            return g;
        }

        private void ExpireGrants(long now)
        {
            foreach (Grant g in store.Grants())
            {
                if (g.ExpiresAt > now) continue;
                foreach (string token in g.Owned)
                {
                    if (store.IsPermanent(g.PlayerId, token)) continue;
                    Grant keeper = null;
                    foreach (Grant other in store.Grants())
                    {
                        if (other != g && other.PlayerId == g.PlayerId && other.ExpiresAt > now && other.Grants.Contains(token)) keeper = other;
                    }
                    if (keeper != null)
                    {
                        // Another timed package still gives it: that one takes it back when it ends.
                        if (!keeper.Owned.Contains(token)) keeper.Owned.Add(token);
                        continue;
                    }
                    bool group = token.StartsWith("group:", StringComparison.Ordinal);
                    string value = token.Substring(token.IndexOf(':') + 1);
                    string error = group ? host.RemoveGroup(g.PlayerId, value) : host.RevokePermission(g.PlayerId, value);
                    if (error != null) host.Warn("Cannot take back " + (group ? "group " : "permission ") + value + " from " + g.PlayerName + ": " + error);
                }
                var vars = Text.Vars("steamid", g.PlayerId, "name", Text.CommandSafe(g.PlayerName), "item", g.PackageId);
                foreach (string command in g.ExpireCommands) RunCommand(Text.Fill(command, vars), null);
                store.RemoveGrant(g);
                Save();
                host.Info("Ended " + g.PackageId + " for " + g.PlayerName);
                ISender player = host.Player(g.PlayerId);
                if (player != null) Say(player, "GrantExpired", Text.Vars("name", g.PackageName));
            }
        }

        private bool Has(string playerId, string token)
        {
            string value = token.Substring(token.IndexOf(':') + 1);
            return token.StartsWith("group:", StringComparison.Ordinal) ? host.HasGroup(playerId, value) : host.HasOwnPermission(playerId, value);
        }

        private bool OwnedByUs(string playerId, string token)
        {
            if (store.IsPermanent(playerId, token)) return true;
            foreach (Grant g in store.Grants()) if (g.PlayerId == playerId && g.Owned.Contains(token)) return true;
            return false;
        }

        private void RunCommand(string command, Order o)
        {
            string c = command.StartsWith("/", StringComparison.Ordinal) ? command.Substring(1) : command;
            Report(host.Command(c), "command " + c, o);
        }

        private void Report(string error, string what, Order o)
        {
            if (error != null) host.Warn("Cannot deliver " + what + (o != null ? " (invoice " + o.Label + ")" : "") + ": " + error);
        }

        private static Dictionary<string, string> CommandVars(Order o, Invoice i) =>
            Text.Vars("steamid", o.PlayerId, "name", Text.CommandSafe(o.PlayerName), "item", o.PackageId, "amount", o.Price,
                "currency", o.Currency, "invoice", i != null && i.Number != null ? i.Number : o.Label);

        // ------------------------------------------------------------------ admin

        public void Admin(ISender s, string sub, string[] args)
        {
            if (!s.IsAdmin)
            {
                Say(s, "NoPermission");
                return;
            }
            string arg = args.Length > 0 ? args[0] : null;
            switch ((sub ?? "").ToLowerInvariant())
            {
                case "reload":
                    Reload(s);
                    return;
                case "status":
                    Status(s);
                    return;
                case "pending":
                    Pending(s);
                    return;
                case "check":
                    if (arg != null)
                    {
                        Check(s, arg);
                        return;
                    }
                    break;
                case "redeliver":
                case "markdone":
                    if (arg != null)
                    {
                        Resolve(s, arg, sub.ToLowerInvariant() == "redeliver");
                        return;
                    }
                    break;
                case "give":
                    if (args.Length >= 2)
                    {
                        Give(s, args[0], args[1]);
                        return;
                    }
                    break;
            }
            Reply(s, "AdminHelp", Text.Vars("version", version));
        }

        private void Reload(ISender s)
        {
            Settings next;
            try
            {
                next = host.LoadSettings();
            }
            catch (Exception e)
            {
                Reply(s, "AdminReloadFailed", Text.Vars("error", e.Message));
                return;
            }
            if (!dataOk) LoadData();
            Apply(next);
            Reply(s, "AdminReloaded", Text.Vars("count", next.Packages.Count.ToString(CultureInfo.InvariantCulture)));
        }

        private void Give(ISender s, string target, string packageId)
        {
            ISender buyer = host.FindPlayer(target);
            if (buyer == null && Text.IsSteamId(target)) buyer = new Offline(target, host.KnownName(target) ?? target);
            if (buyer == null)
            {
                Reply(s, "PlayerNotFound", Text.Vars("player", Text.Escape(target)));
                return;
            }
            Buy(buyer, packageId, s);
        }

        private void Status(ISender s)
        {
            string lang = Lang(s.Id);
            int open = 0, waiting = 0, review = 0;
            foreach (Order o in store.Orders())
            {
                if (o.State == OrderState.Open) open++;
                if (o.State == OrderState.Waiting) waiting++;
                if (o.State == OrderState.Review) review++;
            }
            ShopInfo info = shop;
            string key = info == null ? Msg(lang, "KeyUnknown") : Msg(lang, info.Test ? "KeyTest" : "KeyLive") + ", " + info.Access;
            Reply(s, "AdminStatus", Text.Vars(
                "version", version, "platform", host.Describe(),
                "shop", info != null ? info.Name : "?", "key", key,
                "server", settings.ServerId, "packages", settings.Packages.Count.ToString(CultureInfo.InvariantCulture),
                "open", open.ToString(CultureInfo.InvariantCulture), "waiting", waiting.ToString(CultureInfo.InvariantCulture),
                "review", review.ToString(CultureInfo.InvariantCulture), "grants", store.Grants().Count.ToString(CultureInfo.InvariantCulture),
                "error", !dataOk ? "the data file cannot be read" : lastError ?? Msg(lang, "None")));
        }

        private void Pending(ISender s)
        {
            bool ru = Lang(s.Id) == "ru";
            long now = host.Now();
            int shown = 0;
            foreach (Order o in store.Orders())
            {
                if (o.State != OrderState.Open && o.State != OrderState.Waiting && o.State != OrderState.Review) continue;
                if (shown++ >= 30) break;
                Reply(s, "AdminPendingOrder", Text.Vars("id", o.Label, "player", o.PlayerName, "name", o.PackageName,
                    "state", o.State.ToString(), "age", Text.Duration(now - o.CreatedAt, ru)));
            }
            foreach (Grant g in store.Grants())
            {
                if (shown++ >= 60) break;
                Reply(s, "AdminPendingGrant", Text.Vars("player", g.PlayerName, "name", g.PackageName, "left", Text.Duration(g.ExpiresAt - now, ru)));
            }
            if (shown == 0) Reply(s, "AdminPendingEmpty");
        }

        private void Check(ISender s, string id)
        {
            Order o = store.Order(id);
            ApiClient client = api;
            if (o == null || client == null)
            {
                Reply(s, "AdminNotFound", Text.Vars("id", Text.Escape(id)));
                return;
            }
            client.Invoice(o.Id, (invoice, error) =>
            {
                if (error != null)
                {
                    Reply(s, "AdminCheckFailed", Text.Vars("id", o.Label, "error", error.ToString()));
                    return;
                }
                o.LastCheckAt = host.Now();
                Handle(o, invoice, o.LastCheckAt);
                Reply(s, "AdminChecked", Text.Vars("id", o.Label, "status", invoice.Status, "state", o.State.ToString()));
            });
        }

        private void Resolve(ISender s, string id, bool run)
        {
            Order o = store.Order(id);
            if (o == null)
            {
                Reply(s, "AdminNotFound", Text.Vars("id", Text.Escape(id)));
                return;
            }
            bool allowed = o.State == OrderState.Review || (run && (o.State == OrderState.Delivered || o.State == OrderState.Waiting));
            if (!allowed)
            {
                Reply(s, "AdminWrongState", Text.Vars("id", o.Label, "state", o.State.ToString()));
                return;
            }
            if (run)
            {
                o.State = OrderState.Open;
                Deliver(o, null);
                Reply(s, "AdminRedelivered", Text.Vars("id", o.Label));
            }
            else
            {
                o.State = OrderState.Delivered;
                o.ClosedAt = host.Now();
                Save();
                Reply(s, "AdminMarked", Text.Vars("id", o.Label));
            }
            host.Info(s.Name + " " + (run ? "redelivered" : "marked delivered") + " invoice " + o.Label);
        }

        // ------------------------------------------------------------------ helpers

        private bool Save()
        {
            try
            {
                host.WriteData(store.Serialize());
                return true;
            }
            catch (Exception e)
            {
                host.Error("Cannot save the CoinsPay data file: " + e.Message);
                return false;
            }
        }

        /// <summary>"ru" or "en": the config's language, or the player's.</summary>
        public string Lang(string playerId)
        {
            if (settings.Language != "auto") return settings.Language;
            if (playerId == null) return "en";
            string l = host.Language(playerId) ?? "en";
            return l.StartsWith("ru", StringComparison.OrdinalIgnoreCase) ? "ru" : "en";
        }

        private string Msg(string lang, string key)
        {
            string text = host.Message(key, lang);
            if (!string.IsNullOrEmpty(text)) return text;
            Dictionary<string, string> defaults = lang == "ru" ? Texts.Ru : Texts.En;
            return defaults.TryGetValue(key, out text) ? text : key;
        }

        private Ui.Texts TextsFor(string lang) => (key, vars) => Text.Fill(Msg(lang, key), vars);

        /// <summary>A chat line with the prefix; the server console names the plugin itself.</summary>
        private void Say(ISender to, string key, Dictionary<string, string> vars = null)
        {
            if (to == null) return;
            string lang = Lang(to.Id);
            to.Reply((to.Id != null ? Msg(lang, "Prefix") : "") + Text.Fill(Msg(lang, key), vars));
        }

        /// <summary>An admin answer: no prefix, it often goes to the console.</summary>
        private void Reply(ISender to, string key, Dictionary<string, string> vars = null)
        {
            to.Reply(Text.Fill(Msg(Lang(to.Id), key), vars));
        }

        private static int Count(Dictionary<string, int> map, string key)
        {
            int n;
            return map.TryGetValue(key, out n) ? n : 0;
        }

        private static List<ItemSpec> Copy(List<ItemSpec> items)
        {
            var list = new List<ItemSpec>();
            foreach (ItemSpec i in items) list.Add(new ItemSpec { Shortname = i.Shortname, Amount = i.Amount });
            return list;
        }

        private static string Nonce()
        {
            var bytes = new byte[5];
            using (RandomNumberGenerator rng = RandomNumberGenerator.Create()) rng.GetBytes(bytes);
            var sb = new StringBuilder(10);
            foreach (byte b in bytes) sb.Append(b.ToString("x2", CultureInfo.InvariantCulture));
            return sb.ToString();
        }

        /// <summary>The buyer of coinspay.give who is not on the server: the link goes to the admin.</summary>
        private sealed class Offline : ISender
        {
            public Offline(string id, string name)
            {
                Id = id;
                Name = name;
            }

            public string Id { get; }

            public string Name { get; }

            public bool IsAdmin => false;

            public void Reply(string text)
            {
            }
        }
    }
}
