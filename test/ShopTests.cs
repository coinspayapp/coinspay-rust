using System.Collections.Generic;
using System.Linq;
using CoinsPayRust;
using Newtonsoft.Json.Linq;
using Xunit;

namespace CoinsPayRust.Tests
{
    public class ShopTests
    {
        private const string Bob = "76561198000000001";
        private const string Ann = "76561198000000002";
        private const long Day = 86400000L;

        private readonly FakeHost host = new FakeHost();
        private readonly Shop shop;
        private readonly FakePlayer bob = new FakePlayer(Bob, "Bob");
        private readonly FakePlayer admin = new FakePlayer(null, "Console", admin: true);

        public ShopTests()
        {
            host.Online[Bob] = bob;
            shop = new Shop(host, "0.1.0");
            shop.Enable(Config());
            host.Run();
        }

        private static Settings Config(int lifetime = 60)
        {
            var file = new ConfigFile
            {
                Api = new ConfigFile.ApiConfig { Url = FakeApi.Base, Key = "cpk_test_0123456789" },
                LifetimeMinutes = lifetime,
                CooldownSeconds = 10,
                Packages = new Dictionary<string, PackageConfig>
                {
                    ["vip"] = new PackageConfig { Name = "VIP", Price = "4.99", Duration = "30d", Groups = new List<string> { "vip" }, Permissions = new List<string> { "skipqueue.use" }, ExpireCommands = new List<string> { "say vip ended for {steamid}" } },
                    ["vip90"] = new PackageConfig { Name = "VIP 90", Price = "9.99", Duration = "90d", Groups = new List<string> { "vip" } },
                    ["vipforever"] = new PackageConfig { Name = "VIP forever", Price = "49", Groups = new List<string> { "vip" } },
                    ["wood"] = new PackageConfig { Name = "Wood", Price = "1", Items = new List<ItemSpec> { new ItemSpec { Shortname = "wood", Amount = 10000 } } },
                    ["coins"] = new PackageConfig { Name = "Coins", Price = "2", Commands = new List<string> { "deposit {steamid} 1000", "say thanks {name}" } },
                },
            };
            return Settings.From(file, null);
        }

        private string Buy(string package, FakePlayer who = null)
        {
            shop.Buy(who ?? bob, package, null);
            host.Run();
            return (string)host.Api.Last["id"];
        }

        private void Tick(long advance = 0)
        {
            host.Time += advance;
            shop.Tick();
            host.Run();
        }

        [Fact]
        public void BuyingCreatesAnInvoiceAndShowsHowToPay()
        {
            Buy("coins");
            var create = host.Api.Requests.Single(r => r.Method == "POST");
            JObject body = JObject.Parse(create.Body);
            Assert.Equal("2", (string)body["amount"]);
            Assert.Equal("USD", (string)body["currency"]);
            Assert.Equal("Coins", (string)body["title"]);
            Assert.Equal(60, (int)body["lifetime_minutes"]);
            string reference = (string)body["reference"];
            Assert.Matches("^rust:main:" + Bob + ":coins:[0-9a-f]{10}$", reference);
            Assert.Equal(reference, create.Headers["Idempotency-Key"]);
            Assert.Equal("Bearer cpk_test_0123456789", create.Headers["Authorization"]);
            Assert.StartsWith("CoinsPay-Rust/0.1.0 (Fake 1.0)", create.Headers["User-Agent"]);

            Assert.True(bob.Saw("Your invoice for 2.00 USD for Coins is ready"));
            Assert.True(bob.Saw("coinspay.app/pay/k7m2-x9pq-0001"));
            Assert.True(bob.Saw("F1 console"));
            Assert.True(bob.Saw("Test mode"));
            Assert.Contains(host.Console, l => l == Bob + " CoinsPay payment link for Coins: https://coinspay.app/pay/k7m2-x9pq-0001?lang=en");
            Assert.True(host.Windows.ContainsKey(Bob + " " + Ui.PayRoot));
            Assert.Contains("\"PlayerId\": \"" + Bob + "\"", host.Data);
        }

        [Fact]
        public void PaidCommandsRunOnce()
        {
            string id = Buy("coins");
            Tick();
            Assert.Empty(host.Commands);
            host.Api.Pay(id);
            Tick();
            Assert.Equal(new[] { "deposit " + Bob + " 1000", "say thanks Bob" }, host.Commands);
            Assert.True(bob.Saw("Payment received"));
            Assert.False(host.Windows.ContainsKey(Bob + " " + Ui.PayRoot));
            Tick(60000);
            Tick(Day);
            Assert.Equal(2, host.Commands.Count);
            Assert.Equal(OrderState.Delivered, shop.Store.Order(id).State);
        }

        [Fact]
        public void ItemsWaitUntilThePlayerIsBack()
        {
            string id = Buy("wood");
            host.Online.Remove(Bob);
            host.Api.Pay(id);
            Tick();
            Assert.Equal(OrderState.Waiting, shop.Store.Order(id).State);
            Assert.Empty(host.Given);

            host.Online[Bob] = bob;
            host.NotReady.Add(Bob); // sleeping after joining
            shop.PlayerReady(Bob);
            Tick();
            Assert.Empty(host.Given);

            host.NotReady.Remove(Bob);
            shop.PlayerReady(Bob);
            Assert.Equal(new[] { Bob + " wood 10000" }, host.Given);
            Assert.Equal(OrderState.Delivered, shop.Store.Order(id).State);
            Tick();
            Assert.Single(host.Given);
        }

        [Fact]
        public void APaidItemForADeadPlayerTellsThemToWait()
        {
            string id = Buy("wood");
            host.NotReady.Add(Bob);
            host.Api.Pay(id);
            Tick();
            Assert.True(bob.Saw("when you are in the game and alive"));
            host.NotReady.Remove(Bob);
            Tick();
            Assert.Single(host.Given);
        }

        [Fact]
        public void PartialPaymentIsAnnouncedOnce()
        {
            string id = Buy("coins");
            host.Api.PayPart(id, "1.5", "0.5");
            Tick(130000);
            Tick(70000);
            Assert.Single(bob.Chat, c => c.Contains("Received 1.50 of 2.00 USD. Pay the remaining 0.50 USD"));
            Assert.Empty(host.Commands);
        }

        [Fact]
        public void ExpiredThenPaidLateStillDelivers()
        {
            string id = Buy("coins");
            host.Api.SetStatus(id, "expired");
            Tick(130000);
            Assert.Equal(OrderState.Expired, shop.Store.Order(id).State);
            Assert.True(bob.Saw("has expired"));
            Assert.False(host.Windows.ContainsKey(Bob + " " + Ui.PayRoot));
            host.Api.Pay(id, late: true);
            Tick(60000);
            Assert.Equal(OrderState.Delivered, shop.Store.Order(id).State);
            Assert.Equal(2, host.Commands.Count);
        }

        [Fact]
        public void CancelledIsClosed()
        {
            string id = Buy("coins");
            host.Api.SetStatus(id, "cancelled");
            Tick(130000);
            Assert.Equal(OrderState.Cancelled, shop.Store.Order(id).State);
            Assert.True(bob.Saw("was cancelled"));
        }

        [Fact]
        public void AnUnderpaidOrForeignInvoiceIsNotDelivered()
        {
            string id = Buy("coins");
            host.Api.Pay(id);
            host.Api.Invoices[id]["amount"] = "1.99";
            Tick();
            Tick(60000);
            Assert.Empty(host.Commands);
            Assert.Single(host.Log, l => l.Contains("is NOT delivered: amount 1.99 below the price 2"));

            string other = Buy("coins", new FakePlayer(Ann, "Ann"));
            host.Api.Pay(other);
            host.Api.Invoices[other]["reference"] = "rust:main:" + Bob + ":coins:0000000000";
            Tick(60000);
            Assert.True(host.Warned("reference rust:main:" + Bob));
            Assert.Empty(host.Commands);
        }

        [Fact]
        public void TimedGroupsEndAndRenew()
        {
            string first = Buy("vip");
            host.Api.Pay(first);
            Tick();
            Assert.Contains("vip", host.Groups[Bob]);
            Assert.Contains("skipqueue.use", host.Perms[Bob]);
            Assert.True(bob.Saw("VIP lasts another 30d"));

            host.Time += 20 * Day;
            string second = Buy("vip");
            host.Api.Pay(second);
            Tick();
            Grant g = shop.Store.Grants().Single();
            Assert.Equal(host.Time + 40 * Day, g.ExpiresAt);

            Tick(39 * Day);
            Assert.Contains("vip", host.Groups[Bob]);
            Tick(Day + 1000);
            Assert.DoesNotContain("vip", host.Groups[Bob]);
            Assert.DoesNotContain("skipqueue.use", host.Perms[Bob]);
            Assert.Contains("say vip ended for " + Bob, host.Commands);
            Assert.Empty(shop.Store.Grants());
            Assert.True(bob.Saw("VIP has ended"));
        }

        [Fact]
        public void TimedGrantsLeaveWhatThePlayerHadAndWhatOthersGive()
        {
            host.Groups[Bob] = new HashSet<string> { "vip" }; // given by the admin before
            string id = Buy("vip");
            host.Api.Pay(id);
            Tick();
            Tick(31 * Day);
            Assert.Contains("vip", host.Groups[Bob]);
            Assert.DoesNotContain("skipqueue.use", host.Perms[Bob]);

            // Two packages with the vip group: it stays until the longer one ends.
            var ann = new FakePlayer(Ann, "Ann");
            host.Online[Ann] = ann;
            string a = Buy("vip", ann);
            host.Api.Pay(a);
            Tick();
            host.Time += 20000;
            string b = Buy("vip90", ann);
            host.Api.Pay(b);
            Tick();
            Tick(31 * Day);
            Assert.Contains("vip", host.Groups[Ann]);
            Tick(60 * Day);
            Assert.DoesNotContain("vip", host.Groups[Ann]);
        }

        [Fact]
        public void APermanentPurchaseIsNeverTakenBack()
        {
            var ann = new FakePlayer(Ann, "Ann");
            host.Online[Ann] = ann;
            string timed = Buy("vip", ann);
            host.Api.Pay(timed);
            Tick();
            host.Time += 20000;
            string forever = Buy("vipforever", ann);
            host.Api.Pay(forever);
            Tick();
            Tick(31 * Day);
            Assert.Contains("vip", host.Groups[Ann]);
        }

        [Fact]
        public void ACrashMidDeliveryWaitsForTheAdmin()
        {
            string id = Buy("coins");
            string data = host.Data.Replace("\"State\": \"Open\"", "\"State\": \"Delivering\"");
            var host2 = new FakeHost { Data = data };
            var shop2 = new Shop(host2, "0.1.0");
            shop2.Enable(Config());
            host2.Run();
            Assert.Equal(OrderState.Review, shop2.Store.Order(id).State);
            Assert.True(host2.Warned("was being delivered when the server stopped"));
            Assert.Contains("\"Review\"", host2.Data);

            host2.Api.Invoices[id] = host.Api.Invoices[id];
            host.Api.Pay(id);
            host2.Time = host.Time + 60000;
            shop2.Tick();
            host2.Run();
            Assert.Empty(host2.Commands);

            var console = new FakePlayer(null, "Console", admin: true);
            shop2.Admin(console, "markdone", new string[0]);
            Assert.True(console.Saw("coinspay.status"));
            shop2.Admin(console, "redeliver", new[] { "T-1" });
            Assert.Equal(2, host2.Commands.Count);
            Assert.Equal(OrderState.Delivered, shop2.Store.Order(id).State);
            shop2.Admin(console, "markdone", new[] { "T-1" });
            Assert.True(console.Saw("Invoice T-1 is Delivered: this action does not apply."));
        }

        [Fact]
        public void NothingIsDeliveredIfTheStateCannotBeSaved()
        {
            string id = Buy("coins");
            host.Api.Pay(id);
            host.FailWrite = true;
            Tick();
            Assert.Empty(host.Commands);
            Assert.Equal(OrderState.Open, shop.Store.Order(id).State);
            host.FailWrite = false;
            Tick(1000);
            Assert.Equal(2, host.Commands.Count);
        }

        [Fact]
        public void LimitsOpenInvoicesAndSpam()
        {
            Buy("coins");
            shop.Buy(bob, "coins", null);
            Assert.True(bob.Saw("Wait 10s before a new invoice"));
            host.Time += 11000;
            Buy("coins");
            host.Time += 11000;
            shop.Buy(bob, "coins", null);
            Assert.True(bob.Saw("You already have 2 unpaid invoices"));
            Assert.Equal(2, host.Api.Invoices.Count);
            shop.Buy(bob, "nope", null);
            Assert.True(bob.Saw("No such item: nope"));
        }

        [Fact]
        public void AdminGivesAnInvoiceToAnOfflinePlayer()
        {
            const string gone = "76561198000000099";
            shop.Admin(admin, "give", new[] { gone, "coins" });
            host.Run();
            Assert.True(admin.Saw("Invoice T-1 for Gone, Coins, 2.00 USD: https://coinspay.app/pay/k7m2-x9pq-0001"));
            Assert.Equal("Gone", shop.Store.Order("inv_1").PlayerName);

            shop.Admin(admin, "give", new[] { "Bob", "coins" });
            host.Run();
            Assert.True(admin.Saw("An invoice for Coins was sent to Bob."));
            Assert.True(bob.Saw("Your invoice for 2.00 USD"));

            shop.Admin(admin, "give", new[] { "Nobody", "coins" });
            Assert.True(admin.Saw("Player Nobody is not on the server"));

            Assert.False(bob.IsAdmin);
            shop.Admin(bob, "status", new string[0]);
            Assert.True(bob.Saw("You do not have permission"));
        }

        [Fact]
        public void RetriesACreateWithTheSameKey()
        {
            host.Api.Fail.Enqueue(502);
            host.Api.Fail.Enqueue(0);
            Buy("coins");
            var posts = host.Api.Requests.Where(r => r.Method == "POST").ToList();
            Assert.Equal(3, posts.Count);
            Assert.Single(posts.Select(p => p.Headers["Idempotency-Key"]).Distinct());
            Assert.Single(host.Api.Invoices);
            Assert.True(bob.Saw("Your invoice"));
        }

        [Fact]
        public void ARateLimitIsNotRetried()
        {
            host.Api.Fail.Enqueue(429);
            shop.Buy(bob, "coins", null);
            host.Run();
            Assert.Single(host.Api.Requests, r => r.Method == "POST");
            Assert.Empty(host.Api.Invoices);
            Assert.True(bob.Saw("Too many requests"));
        }

        [Fact]
        public void WarnsAboutAFullAccessKey()
        {
            var h = new FakeHost();
            h.Api.Access = "full";
            new Shop(h, "0.1.0").Enable(Config());
            h.Run();
            Assert.True(h.Warned("full access"));
            Assert.False(host.Warned("full access"));
        }

        [Fact]
        public void ADamagedDataFileTurnsTheStoreOff()
        {
            var h = new FakeHost { Data = "{ broken" };
            h.Online[Bob] = bob;
            var s = new Shop(h, "0.1.0");
            s.Enable(Config());
            h.Run();
            Assert.Contains(h.Log, l => l.StartsWith("ERROR The CoinsPay data file is damaged, copied to CoinsPay.broken.json"));
            s.Buy(bob, "coins", null);
            Assert.True(bob.Saw("Could not create the invoice"));
            Assert.Equal("{ broken", h.Data);

            h.Data = null;
            h.Next = Config();
            s.Admin(admin, "reload", new string[0]);
            h.Run();
            Assert.True(admin.Saw("Settings reloaded. Items: 5."));
            s.Buy(bob, "coins", null);
            h.Run();
            Assert.Single(h.Api.Invoices);
        }

        [Fact]
        public void StatusAndPendingDescribeTheQueue()
        {
            string id = Buy("vip");
            host.Api.Pay(id);
            Tick();
            Buy("coins", new FakePlayer(Ann, "Ann"));
            shop.Admin(admin, "status", new string[0]);
            Assert.True(admin.Saw("Shop: Test shop, key: test, invoices"));
            Assert.True(admin.Saw("Invoices: open 1, waiting for the player 0, to review 0, timed 1"));
            shop.Admin(admin, "pending", new string[0]);
            Assert.True(admin.Saw("T-2 Ann - Coins: Open"));
            Assert.True(admin.Saw("Bob - VIP: 30d left"));
            shop.Admin(admin, "check", new[] { "T-2" });
            host.Run();
            Assert.True(admin.Saw("Invoice T-2: open in CoinsPay, Open in the plugin."));
        }

        [Fact]
        public void TheMenuAndItsClicks()
        {
            shop.Donate(bob, "donate", new string[0]);
            Assert.True(host.Windows.ContainsKey(Bob + " " + Ui.MenuRoot));
            shop.UiClick(bob, new[] { "page", "1" });
            shop.UiClick(bob, new[] { "buy", "coins" });
            host.Run();
            Assert.False(host.Windows.ContainsKey(Bob + " " + Ui.MenuRoot));
            Assert.True(host.Windows.ContainsKey(Bob + " " + Ui.PayRoot));
            shop.UiClick(bob, new[] { "closepay" });
            Assert.False(host.Windows.ContainsKey(Bob + " " + Ui.PayRoot));

            shop.Donate(admin, "donate", new string[0]);
            Assert.True(admin.Saw("This command is for players"));
        }

        [Fact]
        public void TheChatMenuInRussian()
        {
            host.Language = "ru-RU";
            shop.Enable(Settings.From(new ConfigFile
            {
                Api = new ConfigFile.ApiConfig { Url = FakeApi.Base, Key = "cpk_test_0123456789" },
                Menu = "chat",
                Packages = new Dictionary<string, PackageConfig> { ["vip"] = new PackageConfig { Name = "VIP", Price = "4.99", Duration = "30d", Groups = new List<string> { "vip" } } },
            }, null));
            host.Run();
            shop.Donate(bob, "donate", new string[0]);
            Assert.True(bob.Saw("vip: VIP, 4.99 USD на 30 дн."));
            Assert.Empty(host.Windows);
            shop.Donate(bob, "donate", new[] { "vip" });
            host.Run();
            Assert.True(bob.Saw("Счёт на 4.99 USD за VIP готов"));
            Assert.Contains(host.Console, l => l.EndsWith("?lang=ru"));
            Assert.Empty(host.Windows);
        }
    }
}
