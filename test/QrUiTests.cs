using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using CoinsPayRust;
using Xunit;
using ZXing;
using ZXing.Common;
using ZXing.QrCode;

namespace CoinsPayRust.Tests
{
    public class QrTests
    {
        [Theory]
        [InlineData("https://coinspay.app/pay/k7m2-x9pq-4tza")]
        [InlineData("https://coinspay.app/pay/k7m2-x9pq-4tza?lang=ru")]
        [InlineData("a")]
        [InlineData("Оплата: https://coinspay.app/pay/abcd-efgh-jkmn?lang=en&utm=very-long-campaign-name-to-need-a-bigger-version-of-the-code-0123456789")]
        public void DecodesBack(string text)
        {
            Qr qr = Qr.Encode(text);
            var pixels = new bool[qr.Size, qr.Size];
            for (int y = 0; y < qr.Size; y++)
                for (int x = 0; x < qr.Size; x++) pixels[y, x] = qr[x, y];
            Assert.Equal(text, Decode(pixels, 4));
        }

        [Fact]
        public void ATypicalLinkIsSmall()
        {
            Qr qr = Qr.Encode("https://coinspay.app/pay/k7m2-x9pq-4tza");
            Assert.True(qr.Version <= 3, "version " + qr.Version);
        }

        [Fact]
        public void TheWindowPanelsDrawAReadableCode()
        {
            const string link = "https://coinspay.app/pay/k7m2-x9pq-4tza";
            var ui = new List<UiElement>();
            Ui.AddQr(ui, "w", Qr.Encode(link), 24, 52, 264);
            UiElement square = ui[0];
            int[] min = Ints(square.OffsetMin), max = Ints(square.OffsetMax);
            int size = max[0] - min[0];
            Assert.Equal(size, max[1] - min[1]);
            Assert.True(size <= 264 && size >= 200, "size " + size);
            Assert.True(ui.Count < 400, "panels " + ui.Count);

            // Each panel's offsets are pixels from the square's bottom left corner.
            var pixels = new bool[size, size];
            foreach (UiElement e in ui.Skip(1))
            {
                Assert.Equal(square.Name, e.Parent);
                int[] a = Ints(e.OffsetMin), b = Ints(e.OffsetMax);
                for (int y = a[1]; y < b[1]; y++)
                    for (int x = a[0]; x < b[0]; x++) pixels[size - 1 - y, x] = true;
            }
            Assert.Equal(link, Decode(pixels, 1));
        }

        internal static string Decode(bool[,] dark, int scale)
        {
            int h = dark.GetLength(0), w = dark.GetLength(1);
            int border = 4 * scale;
            int width = w * scale + border * 2, height = h * scale + border * 2;
            var bytes = Enumerable.Repeat((byte)255, width * height).ToArray();
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    if (dark[y, x])
                        for (int dy = 0; dy < scale; dy++)
                            for (int dx = 0; dx < scale; dx++) bytes[(border + y * scale + dy) * width + border + x * scale + dx] = 0;
            var source = new RGBLuminanceSource(bytes, width, height, RGBLuminanceSource.BitmapFormat.Gray8);
            var hints = new Dictionary<DecodeHintType, object> { [DecodeHintType.TRY_HARDER] = true, [DecodeHintType.CHARACTER_SET] = "UTF-8" };
            Result result = new QRCodeReader().decode(new BinaryBitmap(new HybridBinarizer(source)), hints);
            return result?.Text;
        }

        internal static int[] Ints(string pair) => pair.Split(' ').Select(s => int.Parse(s, CultureInfo.InvariantCulture)).ToArray();
    }

    public class UiTests
    {
        private static string T(string key, Dictionary<string, string> vars) => Text.Fill(Texts.En[key], vars);

        private static List<PackageDef> Packages(int n) => Enumerable.Range(1, n).Select(i => new PackageDef
        {
            Id = "p" + i,
            Name = "Package " + i,
            Price = 1.5m * i,
            Currency = "USD",
            DurationMs = i % 2 == 0 ? 7L * 86400000L : 0,
            Description = new List<string> { "line " + i },
            Commands = new List<string> { "say " + i },
        }).ToList();

        private static void WellFormed(List<UiElement> ui, string root)
        {
            Assert.Equal(root, ui[0].Name);
            Assert.Equal("Overlay", ui[0].Parent);
            Assert.True(ui[0].Cursor);
            var seen = new HashSet<string>();
            foreach (UiElement e in ui)
            {
                Assert.True(seen.Add(e.Name), "duplicate " + e.Name);
                if (e != ui[0]) Assert.Contains(e.Parent, seen);
                Assert.Equal(2, QrTests.Ints(e.OffsetMin).Length);
                Assert.Equal(2, QrTests.Ints(e.OffsetMax).Length);
                foreach (string anchor in new[] { e.AnchorMin, e.AnchorMax })
                {
                    float[] f = anchor.Split(' ').Select(s => float.Parse(s, CultureInfo.InvariantCulture)).ToArray();
                    Assert.Equal(2, f.Length);
                    Assert.All(f, v => Assert.InRange(v, 0f, 1f));
                }
                Assert.Equal(4, e.Color.Split(' ').Length);
                if (e.Kind == UiElement.Button) Assert.StartsWith(Ui.Command + " ", e.Command);
            }
        }

        [Fact]
        public void TheMenuPagesFiveItems()
        {
            List<UiElement> first = Ui.Menu(T, Packages(7), 0, false);
            WellFormed(first, Ui.MenuRoot);
            var buys = first.Where(e => e.Command != null && e.Command.Contains(" buy ")).Select(e => e.Command).ToList();
            Assert.Equal(new[] { "coinspay.ui buy p1", "coinspay.ui buy p2", "coinspay.ui buy p3", "coinspay.ui buy p4", "coinspay.ui buy p5" }, buys);
            Assert.Contains(first, e => e.Command == "coinspay.ui page 1");
            Assert.DoesNotContain(first, e => e.Command == "coinspay.ui page -1");
            Assert.Contains(first, e => e.Text == "1 / 2");
            Assert.Contains(first, e => e.Text == "for 7d");
            Assert.Contains(first, e => e.Text == "3 USD");

            List<UiElement> second = Ui.Menu(T, Packages(7), 9, false);
            WellFormed(second, Ui.MenuRoot);
            Assert.Equal(new[] { "coinspay.ui buy p6", "coinspay.ui buy p7" }, second.Where(e => e.Command != null && e.Command.Contains(" buy ")).Select(e => e.Command));
            Assert.Contains(second, e => e.Command == "coinspay.ui page 0");
        }

        [Fact]
        public void AnEmptyMenuSaysSo()
        {
            List<UiElement> ui = Ui.Menu(T, new List<PackageDef>(), 0, false);
            WellFormed(ui, Ui.MenuRoot);
            Assert.Contains(ui, e => e.Text == Texts.En["MenuEmpty"]);
        }

        [Fact]
        public void ThePaymentWindowHasTheCodeAndTheAddress()
        {
            var order = new Order { Id = "inv_1", PackageName = "VIP", Price = "4.99", Currency = "USD", Test = true };
            List<UiElement> ui = Ui.Payment(T, order, "https://coinspay.app/pay/k7m2-x9pq-4tza", 60);
            WellFormed(ui, Ui.PayRoot);
            UiElement link = ui.Single(e => e.Kind == UiElement.Input);
            Assert.Equal("coinspay.app/pay/k7m2-x9pq-4tza", link.Text);
            Assert.Contains(ui, e => e.Text == "Payment for VIP");
            Assert.Contains(ui, e => e.Text == "4.99 USD");
            Assert.Contains(ui, e => e.Text == Text.Fill(Texts.En["TestMode"], null));
            Assert.Contains(ui, e => e.Command == "coinspay.ui closepay");
            Assert.True(ui.Count(e => e.Parent == Ui.PayRoot + ".window.qr") > 50);

            order.Test = false;
            Assert.DoesNotContain(Ui.Payment(T, order, "https://coinspay.app/pay/k7m2-x9pq-4tza", 60), e => e.Name == Ui.PayRoot + ".test");
        }

        [Fact]
        public void RussianTextsHaveEveryKey()
        {
            Assert.Equal(Texts.En.Keys.OrderBy(k => k), Texts.Ru.Keys.OrderBy(k => k));
            foreach (KeyValuePair<string, string> e in Texts.En)
            {
                var en = System.Text.RegularExpressions.Regex.Matches(e.Value, @"\{[a-z_]+\}").Select(m => m.Value).OrderBy(x => x);
                var ru = System.Text.RegularExpressions.Regex.Matches(Texts.Ru[e.Key], @"\{[a-z_]+\}").Select(m => m.Value).OrderBy(x => x);
                Assert.True(en.SequenceEqual(ru), e.Key);
            }
        }
    }
}
