namespace CoinsPayRust
{
    using System.Collections.Generic;

    /// <summary>
    /// The default texts. The plugin registers them with the framework's Lang API, so owners
    /// edit them in oxide/lang/en/CoinsPay.json and oxide/lang/ru/CoinsPay.json.
    /// </summary>
    public static class Texts
    {
        public static readonly Dictionary<string, string> En = new Dictionary<string, string>
        {
            ["Prefix"] = "<color=#10b981>CoinsPay</color> ",
            ["MenuTitle"] = "Store",
            ["MenuSubtitle"] = "Pay with USDT, BTC, ETH, TRX, SOL and other coins",
            ["MenuBuy"] = "Buy",
            ["MenuClose"] = "Close",
            ["MenuPage"] = "{page} / {pages}",
            ["MenuFor"] = "for {duration}",
            ["MenuEmpty"] = "The store is empty for now.",
            ["ChatHeader"] = "Store, pay with crypto. Type <color=#fbbf24>/{command} item</color>:",
            ["ChatLine"] = "<color=#fbbf24>{id}</color>: {name}, {price} {currency}{duration}",
            ["ChatFor"] = " for {duration}",

            ["Creating"] = "Creating an invoice for {name}...",
            ["Created"] = "Your invoice for {price} {currency} for {name} is ready. Open it on your phone or computer:",
            ["ShortLink"] = "<size=16><color=#fbbf24>{short}</color></size>",
            ["F1Hint"] = "The full link is in your F1 console: press F1 and click the time of the message to copy it.",
            ["LinkLife"] = "The link works for {minutes} min. You get {name} as soon as the payment arrives.",
            ["TestMode"] = "<color=#fbbf24>Test mode: no real money is charged.</color>",
            ["ConsoleLink"] = "CoinsPay payment link for {name}: {url}",

            ["PayTitle"] = "Payment for {name}",
            ["PayScan"] = "Scan with your phone",
            ["PayOpen"] = "Or open this address in any browser:",
            ["PayCopy"] = "Select the link and press Ctrl+C. Shift+Tab opens the Steam browser.",
            ["PayAuto"] = "The window closes when the payment arrives.",

            ["Paid"] = "<color=#34d399>Payment received!</color> {name} is yours.",
            ["PaidUntil"] = "{name} lasts another {left}.",
            ["PaidWaiting"] = "<color=#34d399>Payment received!</color> You get {name} when you are in the game and alive.",
            ["Partial"] = "Received {paid} of {amount} {currency}. Pay the remaining {due} {currency} using the same link.",
            ["Expired"] = "The invoice for {name} has expired. If you already sent the payment, you get the item when it arrives.",
            ["Cancelled"] = "The invoice for {name} was cancelled.",
            ["GrantExpired"] = "{name} has ended.",

            ["TooManyOpen"] = "<color=#f87171>You already have {count} unpaid invoices.</color> Pay them or wait until they expire.",
            ["Cooldown"] = "<color=#f87171>Wait {seconds}s before a new invoice.</color>",
            ["UnknownPackage"] = "<color=#f87171>No such item: {id}</color>",
            ["NotConfigured"] = "<color=#f87171>The store is not set up yet:</color> the admin needs to add an API key.",
            ["Error"] = "<color=#f87171>Could not create the invoice.</color> Try again a bit later.",
            ["RateLimited"] = "<color=#f87171>Too many requests.</color> Try again in a minute.",
            ["NoPermission"] = "<color=#f87171>You do not have permission.</color>",
            ["PlayerNotFound"] = "Player {player} is not on the server. For a player who is offline, use the Steam ID.",
            ["PlayersOnly"] = "This command is for players. From the console: coinspay.give <steamid> <item>",

            ["AdminHelp"] = "CoinsPay {version} commands:\n"
                + "coinspay.status - key, shop, queue\n"
                + "coinspay.pending - unpaid, waiting and timed items\n"
                + "coinspay.check <invoice> - ask CoinsPay about an invoice now\n"
                + "coinspay.redeliver <invoice> - deliver an item again\n"
                + "coinspay.markdone <invoice> - mark delivered without delivering\n"
                + "coinspay.give <player or steamid> <item> - create an invoice for a player\n"
                + "coinspay.reload - reread the config",
            ["AdminReloaded"] = "Settings reloaded. Items: {count}.",
            ["AdminReloadFailed"] = "Could not reload the settings: {error}",
            ["AdminNotFound"] = "Invoice {id} is not in this server's data.",
            ["AdminRedelivered"] = "Invoice {id} is delivered again.",
            ["AdminMarked"] = "Invoice {id} is marked delivered.",
            ["AdminWrongState"] = "Invoice {id} is {state}: this action does not apply.",
            ["AdminChecked"] = "Invoice {id}: {status} in CoinsPay, {state} in the plugin.",
            ["AdminCheckFailed"] = "Could not check invoice {id}: {error}",
            ["AdminGave"] = "An invoice for {name} was sent to {player}.",
            ["AdminGaveLink"] = "Invoice {id} for {player}, {name}, {price} {currency}: {url}",
            ["AdminPendingEmpty"] = "No unpaid or waiting invoices, no timed items.",
            ["AdminPendingOrder"] = "{id} {player} - {name}: {state}, {age} ago",
            ["AdminPendingGrant"] = "{player} - {name}: {left} left",
            ["AdminStatus"] = "CoinsPay {version} on {platform}\n"
                + "Shop: {shop}, key: {key}\n"
                + "Server: {server}, items: {packages}\n"
                + "Invoices: open {open}, waiting for the player {waiting}, to review {review}, timed {grants}\n"
                + "Last API error: {error}",
            ["KeyTest"] = "test",
            ["KeyLive"] = "live",
            ["KeyUnknown"] = "not checked",
            ["None"] = "none",
        };

        public static readonly Dictionary<string, string> Ru = new Dictionary<string, string>
        {
            ["Prefix"] = "<color=#10b981>CoinsPay</color> ",
            ["MenuTitle"] = "Магазин",
            ["MenuSubtitle"] = "Оплата в USDT, BTC, ETH, TRX, SOL и других монетах",
            ["MenuBuy"] = "Купить",
            ["MenuClose"] = "Закрыть",
            ["MenuPage"] = "{page} / {pages}",
            ["MenuFor"] = "на {duration}",
            ["MenuEmpty"] = "В магазине пока пусто.",
            ["ChatHeader"] = "Магазин, оплата криптовалютой. Напишите <color=#fbbf24>/{command} товар</color>:",
            ["ChatLine"] = "<color=#fbbf24>{id}</color>: {name}, {price} {currency}{duration}",
            ["ChatFor"] = " на {duration}",

            ["Creating"] = "Выставляю счёт за {name}...",
            ["Created"] = "Счёт на {price} {currency} за {name} готов. Откройте его на телефоне или компьютере:",
            ["ShortLink"] = "<size=16><color=#fbbf24>{short}</color></size>",
            ["F1Hint"] = "Полная ссылка в консоли F1: нажмите F1 и щёлкните по времени сообщения, чтобы скопировать её.",
            ["LinkLife"] = "Ссылка действует {minutes} мин. {name} придёт, как только поступит оплата.",
            ["TestMode"] = "<color=#fbbf24>Тестовый режим: настоящие деньги не списываются.</color>",
            ["ConsoleLink"] = "Ссылка на оплату CoinsPay за {name}: {url}",

            ["PayTitle"] = "Оплата: {name}",
            ["PayScan"] = "Отсканируйте телефоном",
            ["PayOpen"] = "Или откройте адрес в любом браузере:",
            ["PayCopy"] = "Выделите ссылку и нажмите Ctrl+C. Shift+Tab открывает браузер Steam.",
            ["PayAuto"] = "Окно закроется, когда поступит оплата.",

            ["Paid"] = "<color=#34d399>Оплата получена!</color> {name} ваш.",
            ["PaidUntil"] = "{name} действует ещё {left}.",
            ["PaidWaiting"] = "<color=#34d399>Оплата получена!</color> {name} придёт, когда вы будете в игре и живы.",
            ["Partial"] = "Получено {paid} из {amount} {currency}. Доплатите {due} {currency} по той же ссылке.",
            ["Expired"] = "Счёт за {name} истёк. Если вы уже отправили оплату, товар придёт, когда она поступит.",
            ["Cancelled"] = "Счёт за {name} отменён.",
            ["GrantExpired"] = "{name}: срок закончился.",

            ["TooManyOpen"] = "<color=#f87171>У вас уже {count} неоплаченных счёта.</color> Оплатите их или дождитесь, пока они истекут.",
            ["Cooldown"] = "<color=#f87171>Подождите {seconds} с перед новым счётом.</color>",
            ["UnknownPackage"] = "<color=#f87171>Нет такого товара: {id}</color>",
            ["NotConfigured"] = "<color=#f87171>Магазин ещё не настроен:</color> администратору нужно добавить ключ API.",
            ["Error"] = "<color=#f87171>Не удалось выставить счёт.</color> Попробуйте чуть позже.",
            ["RateLimited"] = "<color=#f87171>Слишком много запросов.</color> Попробуйте через минуту.",
            ["NoPermission"] = "<color=#f87171>Нет прав.</color>",
            ["PlayerNotFound"] = "Игрока {player} нет на сервере. Для игрока не в сети укажите Steam ID.",
            ["PlayersOnly"] = "Эта команда для игроков. Из консоли: coinspay.give <steamid> <товар>",

            ["AdminHelp"] = "Команды CoinsPay {version}:\n"
                + "coinspay.status - ключ, магазин, очередь\n"
                + "coinspay.pending - неоплаченные, ждущие и временные товары\n"
                + "coinspay.check <счёт> - спросить CoinsPay о счёте сейчас\n"
                + "coinspay.redeliver <счёт> - выдать товар ещё раз\n"
                + "coinspay.markdone <счёт> - отметить выданным без выдачи\n"
                + "coinspay.give <игрок или steamid> <товар> - выставить счёт игроку\n"
                + "coinspay.reload - перечитать настройки",
            ["AdminReloaded"] = "Настройки перечитаны. Товаров: {count}.",
            ["AdminReloadFailed"] = "Не удалось перечитать настройки: {error}",
            ["AdminNotFound"] = "Счёта {id} нет в данных этого сервера.",
            ["AdminRedelivered"] = "Счёт {id} выдан ещё раз.",
            ["AdminMarked"] = "Счёт {id} отмечен выданным.",
            ["AdminWrongState"] = "Счёт {id} в состоянии {state}: действие к нему не применимо.",
            ["AdminChecked"] = "Счёт {id}: {status} в CoinsPay, {state} в плагине.",
            ["AdminCheckFailed"] = "Не удалось проверить счёт {id}: {error}",
            ["AdminGave"] = "Счёт за {name} отправлен игроку {player}.",
            ["AdminGaveLink"] = "Счёт {id} для {player}, {name}, {price} {currency}: {url}",
            ["AdminPendingEmpty"] = "Нет неоплаченных и ждущих счетов, нет временных товаров.",
            ["AdminPendingOrder"] = "{id} {player} - {name}: {state}, {age} назад",
            ["AdminPendingGrant"] = "{player} - {name}: осталось {left}",
            ["AdminStatus"] = "CoinsPay {version} на {platform}\n"
                + "Магазин: {shop}, ключ: {key}\n"
                + "Сервер: {server}, товаров: {packages}\n"
                + "Счета: открыто {open}, ждут игрока {waiting}, на проверку {review}, временных {grants}\n"
                + "Последняя ошибка API: {error}",
            ["KeyTest"] = "тестовый",
            ["KeyLive"] = "боевой",
            ["KeyUnknown"] = "не проверен",
            ["None"] = "нет",
        };
    }
}
