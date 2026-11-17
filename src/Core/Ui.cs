namespace CoinsPayRust
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;

    /// <summary>
    /// One rectangle of a window. The plugin turns these into the framework's CUI elements; here
    /// they stay plain data, so the layout has unit tests.
    /// </summary>
    public sealed class UiElement
    {
        public const string Panel = "panel";
        public const string Label = "text";
        public const string Button = "button";
        public const string Input = "input";

        public string Name = "";
        public string Parent = "";
        public string Kind = Panel;
        /// <summary>Anchors are fractions of the parent; offsets are pixels from the anchors.</summary>
        public string AnchorMin = "0 0";
        public string AnchorMax = "1 1";
        public string OffsetMin = "0 0";
        public string OffsetMax = "0 0";
        /// <summary>"r g b a", each 0 to 1.</summary>
        public string Color = "0 0 0 0";
        public string Text;
        public int FontSize = 14;
        /// <summary>A UnityEngine.TextAnchor name: MiddleLeft, MiddleCenter, UpperLeft...</summary>
        public string Align = "MiddleLeft";
        public bool Bold;
        /// <summary>Button: the console command the click runs.</summary>
        public string Command;
        /// <summary>Root: the window takes the mouse cursor.</summary>
        public bool Cursor;
    }

    /// <summary>The store and payment windows.</summary>
    public static class Ui
    {
        public const string MenuRoot = "CoinsPay.Menu";
        public const string PayRoot = "CoinsPay.Pay";
        public const string Command = "coinspay.ui";
        public const int PageSize = 5;

        private const string Dim = "0 0 0 0.6";
        private const string Window = "0.08 0.09 0.1 0.98";
        private const string Row = "1 1 1 0.05";
        private const string Green = "0.06 0.62 0.45 1";
        private const string Grey = "0.25 0.27 0.29 1";
        private const string White = "1 1 1 1";
        private const string Black = "0 0 0 1";
        private const string TextMain = "0.95 0.95 0.95 1";
        private const string TextDim = "0.65 0.67 0.7 1";
        private const string Accent = "0.98 0.75 0.14 1";

        /// <summary>Texts of one player: a key and placeholders in, the player's language out.</summary>
        public delegate string Texts(string key, Dictionary<string, string> vars);

        public static int Pages(int count) => Math.Max(1, (count + PageSize - 1) / PageSize);

        /// <summary>The store: five packages a page, a Buy button each.</summary>
        public static List<UiElement> Menu(Texts t, IList<PackageDef> packages, int page, bool russian)
        {
            int pages = Pages(packages.Count);
            page = Math.Max(0, Math.Min(page, pages - 1));
            var ui = new List<UiElement>();
            string window = Root(ui, MenuRoot, 330, 235);

            ui.Add(TextAt(window, MenuRoot + ".title", t("MenuTitle", null), 24, -60, 400, -16, 22, true, TextMain, "MiddleLeft"));
            ui.Add(TextAt(window, MenuRoot + ".subtitle", t("MenuSubtitle", null), 24, -84, 520, -60, 12, false, TextDim, "MiddleLeft"));
            ui.Add(ButtonAt(window, MenuRoot + ".close", t("MenuClose", null), -124, -52, -24, -20, Command + " close", Grey));

            if (packages.Count == 0)
            {
                ui.Add(TextAt(window, MenuRoot + ".empty", t("MenuEmpty", null), 24, -200, 636, -120, 16, false, TextDim, "MiddleCenter"));
                return ui;
            }

            int top = -100;
            for (int i = page * PageSize; i < Math.Min(packages.Count, (page + 1) * PageSize); i++)
            {
                PackageDef p = packages[i];
                string row = MenuRoot + ".row" + i;
                ui.Add(new UiElement
                {
                    Name = row,
                    Parent = window,
                    AnchorMin = "0 1",
                    AnchorMax = "1 1",
                    OffsetMin = Px(16, top - 56),
                    OffsetMax = Px(-16, top),
                    Color = Row,
                });
                ui.Add(TextIn(row, row + ".name", p.Name, 14, -30, 340, -6, 15, true, TextMain, "MiddleLeft"));
                string description = p.Description.Count > 0 ? p.Description[0] : "";
                if (description.Length > 0) ui.Add(TextIn(row, row + ".desc", description, 14, -52, 340, -30, 11, false, TextDim, "MiddleLeft"));
                ui.Add(TextIn(row, row + ".price", Text.Amount(p.Price) + " " + p.Currency, 340, -30, 498, -6, 15, true, Accent, "MiddleRight"));
                if (p.DurationMs > 0)
                {
                    string duration = t("MenuFor", Text.Vars("duration", Text.Duration(p.DurationMs, russian)));
                    ui.Add(TextIn(row, row + ".duration", duration, 340, -52, 498, -30, 11, false, TextDim, "MiddleRight"));
                }
                ui.Add(new UiElement
                {
                    Name = row + ".buy",
                    Parent = row,
                    Kind = UiElement.Button,
                    AnchorMin = "1 0.5",
                    AnchorMax = "1 0.5",
                    OffsetMin = Px(-112, -17),
                    OffsetMax = Px(-12, 17),
                    Color = Green,
                    Text = t("MenuBuy", null),
                    FontSize = 14,
                    Bold = true,
                    Align = "MiddleCenter",
                    Command = Command + " buy " + p.Id,
                });
                top -= 64;
            }

            if (pages > 1)
            {
                if (page > 0) ui.Add(ButtonAt(window, MenuRoot + ".prev", "<", 24, 16, 64, 48, Command + " page " + (page - 1).ToString(CultureInfo.InvariantCulture), Grey, bottom: true));
                ui.Add(TextAt(window, MenuRoot + ".page", t("MenuPage", Text.Vars("page", (page + 1).ToString(CultureInfo.InvariantCulture), "pages", pages.ToString(CultureInfo.InvariantCulture))),
                    72, 16, 152, 48, 13, false, TextDim, "MiddleCenter", bottom: true));
                if (page < pages - 1) ui.Add(ButtonAt(window, MenuRoot + ".next", ">", 160, 16, 200, 48, Command + " page " + (page + 1).ToString(CultureInfo.InvariantCulture), Grey, bottom: true));
            }
            return ui;
        }

        /// <summary>
        /// The payment window: the QR code of the link on the left, the address to type or copy on
        /// the right. Rust has no clickable links, so the address sits in a read-only field.
        /// </summary>
        public static List<UiElement> Payment(Texts t, Order order, string link, int minutes)
        {
            var ui = new List<UiElement>();
            string window = Root(ui, PayRoot, 330, 210);
            var v = Text.Vars("name", order.PackageName, "price", order.Price, "currency", order.Currency, "minutes", minutes.ToString(CultureInfo.InvariantCulture));

            AddQr(ui, window, Qr.Encode(link), 24, 52, 264);
            ui.Add(TextAt(window, PayRoot + ".scan", t("PayScan", null), 24, 20, 288, 48, 12, false, TextDim, "MiddleCenter", bottom: true));

            ui.Add(TextAt(window, PayRoot + ".title", t("PayTitle", v), 312, -64, 636, -16, 18, true, TextMain, "MiddleLeft"));
            ui.Add(TextAt(window, PayRoot + ".price", order.Price + " " + order.Currency, 312, -104, 636, -64, 26, true, Accent, "MiddleLeft"));
            ui.Add(TextAt(window, PayRoot + ".open", t("PayOpen", null), 312, -136, 636, -112, 12, false, TextDim, "MiddleLeft"));
            ui.Add(new UiElement
            {
                Name = PayRoot + ".linkbox",
                Parent = window,
                AnchorMin = "0 1",
                AnchorMax = "0 1",
                OffsetMin = Px(312, -180),
                OffsetMax = Px(636, -140),
                Color = "1 1 1 0.08",
            });
            ui.Add(new UiElement
            {
                Name = PayRoot + ".link",
                Parent = PayRoot + ".linkbox",
                Kind = UiElement.Input,
                OffsetMin = Px(10, 0),
                OffsetMax = Px(-10, 0),
                Color = Accent,
                Text = Text.ShortUrl(link),
                FontSize = 16,
                Bold = true,
                Align = "MiddleLeft",
            });
            ui.Add(TextAt(window, PayRoot + ".copy", t("PayCopy", null), 312, -228, 636, -186, 11, false, TextDim, "UpperLeft"));
            ui.Add(TextAt(window, PayRoot + ".f1", t("F1Hint", null), 312, -270, 636, -228, 11, false, TextDim, "UpperLeft"));
            ui.Add(TextAt(window, PayRoot + ".life", t("LinkLife", v), 312, -312, 636, -270, 11, false, TextDim, "UpperLeft"));
            if (order.Test) ui.Add(TextAt(window, PayRoot + ".test", t("TestMode", null), 312, -340, 636, -314, 12, false, Accent, "MiddleLeft"));
            ui.Add(TextAt(window, PayRoot + ".auto", t("PayAuto", null), 312, 20, 520, 48, 11, false, TextDim, "MiddleLeft", bottom: true));
            ui.Add(ButtonAt(window, PayRoot + ".close", t("MenuClose", null), 536, 16, 636, 48, Command + " closepay", Grey, bottom: true));
            return ui;
        }

        /// <summary>
        /// The QR code as a white square with dark runs: each row's neighbouring dark modules are one
        /// panel, so a typical link needs a few hundred panels rather than a thousand.
        /// </summary>
        public static void AddQr(List<UiElement> ui, string parent, Qr qr, int left, int bottom, int maxSize)
        {
            const int quiet = 4;
            int units = qr.Size + quiet * 2;
            int module = Math.Max(1, maxSize / units);
            int size = module * units;
            int shift = (maxSize - size) / 2;
            string square = parent + ".qr";
            ui.Add(new UiElement
            {
                Name = square,
                Parent = parent,
                AnchorMin = "0 0",
                AnchorMax = "0 0",
                OffsetMin = Px(left + shift, bottom + shift),
                OffsetMax = Px(left + shift + size, bottom + shift + size),
                Color = White,
            });
            int n = 0;
            for (int y = 0; y < qr.Size; y++)
            {
                int x = 0;
                while (x < qr.Size)
                {
                    if (!qr[x, y])
                    {
                        x++;
                        continue;
                    }
                    int start = x;
                    while (x < qr.Size && qr[x, y]) x++;
                    int x0 = (quiet + start) * module;
                    int y0 = (quiet + qr.Size - 1 - y) * module;
                    ui.Add(new UiElement
                    {
                        Name = square + "." + (n++).ToString(CultureInfo.InvariantCulture),
                        Parent = square,
                        AnchorMin = "0 0",
                        AnchorMax = "0 0",
                        OffsetMin = Px(x0, y0),
                        OffsetMax = Px(x0 + (x - start) * module, y0 + module),
                        Color = Black,
                    });
                }
            }
        }

        /// <summary>A dimmed screen with the window in the middle; returns the window's name.</summary>
        private static string Root(List<UiElement> ui, string root, int halfWidth, int halfHeight)
        {
            ui.Add(new UiElement { Name = root, Parent = "Overlay", Color = Dim, Cursor = true });
            string window = root + ".window";
            ui.Add(new UiElement
            {
                Name = window,
                Parent = root,
                AnchorMin = "0.5 0.5",
                AnchorMax = "0.5 0.5",
                OffsetMin = Px(-halfWidth, -halfHeight),
                OffsetMax = Px(halfWidth, halfHeight),
                Color = Window,
            });
            return window;
        }

        /// <summary>A label placed from the window's top left (or bottom left), in pixels.</summary>
        private static UiElement TextAt(string parent, string name, string text, int x0, int y0, int x1, int y1, int size, bool bold, string color, string align,
            bool bottom = false)
        {
            string anchor = bottom ? "0 0" : "0 1";
            return new UiElement
            {
                Name = name,
                Parent = parent,
                Kind = UiElement.Label,
                AnchorMin = anchor,
                AnchorMax = anchor,
                OffsetMin = Px(x0, y0),
                OffsetMax = Px(x1, y1),
                Text = text,
                FontSize = size,
                Bold = bold,
                Color = color,
                Align = align,
            };
        }

        /// <summary>A label in a row, from the row's top left.</summary>
        private static UiElement TextIn(string parent, string name, string text, int x0, int y0, int x1, int y1, int size, bool bold, string color, string align) =>
            TextAt(parent, name, text, x0, y0, x1, y1, size, bold, color, align);

        /// <summary>A button from the window's top left; negative x counts from the right edge.</summary>
        private static UiElement ButtonAt(string parent, string name, string text, int x0, int y0, int x1, int y1, string command, string color, bool bottom = false)
        {
            bool fromRight = x0 < 0;
            string anchor = (fromRight ? "1" : "0") + (bottom ? " 0" : " 1");
            return new UiElement
            {
                Name = name,
                Parent = parent,
                Kind = UiElement.Button,
                AnchorMin = anchor,
                AnchorMax = anchor,
                OffsetMin = Px(x0, y0),
                OffsetMax = Px(x1, y1),
                Color = color,
                Text = text,
                FontSize = 13,
                Bold = true,
                Align = "MiddleCenter",
                Command = command,
            };
        }

        private static string Px(int x, int y) => x.ToString(CultureInfo.InvariantCulture) + " " + y.ToString(CultureInfo.InvariantCulture);
    }
}
