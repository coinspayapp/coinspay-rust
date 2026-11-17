# CoinsPay for Rust: installation

The plugin sells VIP, kits, items and in-game currency on your Rust server for crypto. A player
types `/donate`, picks an item and pays on the CoinsPay page. The plugin then delivers the item
itself, and the money goes straight to your wallets.

It runs on **Oxide (uMod)** and **Carbon**, as one file: `CoinsPay.cs`.

## 1. API key

1. Sign up at [coinspay.app](https://coinspay.app) and create a shop.
2. Add your payout wallets in the shop settings.
3. Open the shop → "API keys" → "Create key", access **"Invoices only"**.

A key with that access cannot make payouts, so a leaked config puts no money at risk.

For a dry run, tick "Test key" (`cpk_test_…`). The payment page of a test invoice has a "Pay"
button and charges no real money.

## 2. Installation

1. Put `CoinsPay.cs` into `oxide/plugins` (Carbon: `carbon/plugins`). There is no need to restart:
   the framework compiles the plugin and loads it within seconds.
2. Open `oxide/config/CoinsPay.json` (Carbon: `carbon/configs/CoinsPay.json`) and put the key into
   `"api"` → `"key"`.
3. Describe your items under `"packages"` (below).
4. Run `coinspay.reload` in the server console or RCON, then `coinspay.status`. It should show your
   shop and a "test" or "live" key with `invoices` access.

## 3. Items

```json
"vip-30": {
  "name": "VIP for 30 days",
  "description": ["Skip the queue, the vip kit"],
  "price": "4.99",
  "duration": "30d",
  "groups": ["vip"],
  "permissions": ["skipqueue.use"]
}
```

- **The id** (`vip-30`) may hold letters, digits, `_` and `-`. Players type it in `/donate vip-30`.
- **`name`** and **`description`** show in the store; the name is also the invoice title.
- **`price`** is in `"currency"` (USD by default). An item may set its own `"currency"`.

### VIP and permissions

`"groups"` and `"permissions"` go through the permission system of Oxide or Carbon, so the player
does not need to be online.

- Give the group its permissions as usual, for example `oxide.grant group vip kits.vip`
  (Carbon: `c.grant group vip kits.vip`). If the group does not exist, the plugin creates it at
  the first purchase and warns in the console.
- A permission must belong to a loaded plugin, or the framework does not grant it.
- With `"duration"` (`30d`, `12h`, `90m`, `2w`, `1mo`) the plugin takes the groups and permissions
  back when the time ends, even if the player is offline. Buying again extends the time.
- What the player had before the purchase, for example a group you gave by hand, is never taken
  back. Neither is what another of their purchases still gives.

### Items

```json
"wood": {
  "name": "10 000 wood",
  "price": "1.00",
  "items": [{ "shortname": "wood", "amount": 10000 }]
}
```

Items need the player in the game, awake and alive. If the player pays and leaves, the items arrive
when they come back. What does not fit in the inventory drops at the player's feet.

The plugin refuses DLC items and paid skins at load and says so in the console: Facepunch forbids
giving them to players who did not buy them.

### Other plugins: currency, kits

`"commands"` run in the server console after payment:

| Plugin | Command |
|---|---|
| Economics | `deposit {steamid} 1000` |
| Server Rewards | `rp add {steamid} 1000` |
| Kits | `kit give {steamid} starter`, with `"require-online": true` |

Placeholders: `{steamid}`, `{name}`, `{item}` (the item id), `{amount}` and `{currency}` (the price),
`{invoice}` (the invoice number). Prefer `{steamid}`: Rust names may hold any characters, so `{name}`
loses quotes and semicolons.

`"expire-commands"` run when the `"duration"` ends.

## 4. How a player buys

1. `/donate` opens the store window. With `"menu": "chat"` it prints a list in chat instead.
2. A click creates an invoice and opens the payment window. Rust chat has no clickable links, so:
   - the window shows a QR code to scan with a phone;
   - the window and the chat show a short address such as `coinspay.app/pay/k7m2-x9pq-4tza` to type
     into any browser;
   - the F1 console gets the full link: press F1 and click the time of the message to copy it.
     Shift+Tab opens the Steam browser.
3. The player pays on the CoinsPay page in USDT, BTC, ETH, TRX, BNB, LTC, SOL and other coins.
4. Seconds after the payment the plugin says "Payment received", delivers the item and closes the
   window.

The plugin asks CoinsPay about payments every `"poll-seconds"`, so the server needs no public
address or webhook.

## 5. Commands

| Command | What it does |
|---|---|
| `/donate [item]` | the store |
| `coinspay.status` | key, shop, queue, last API error |
| `coinspay.pending` | unpaid and waiting invoices, running timed items |
| `coinspay.check <invoice>` | check an invoice now |
| `coinspay.redeliver <invoice>` | deliver an item again |
| `coinspay.markdone <invoice>` | mark delivered without delivering |
| `coinspay.give <player or steamid> <item>` | create an invoice for a player |
| `coinspay.reload` | reread the config and the texts |

The `coinspay.*` commands work in the server console and RCON. Give admins the permission with
`oxide.grant user <steamid> coinspay.admin` (Carbon: `c.grant user <steamid> coinspay.admin`):
they can then run them in the F1 console, or in chat as `/coinspay status` and so on.

`coinspay.give` with the Steam ID of a player who is offline writes the payment link to the
console. You can send it to the player yourself, for example in Discord.

## Good to know

- **Each item is delivered once.** If the server crashes in the middle of a delivery, the plugin
  does not deliver the item again. It writes the invoice number to the console. Check whether the
  player got the item, then run `coinspay.redeliver <invoice>` or `coinspay.markdone <invoice>`.
- **Wipes.** Groups, permissions and timed items survive a map wipe: the plugin keeps them in
  `oxide/data/CoinsPay.json`. Items given into the inventory go with the map, as usual.
- **Facepunch's rules.** The [server guidelines](https://facepunch.com/legal/servers) allow selling
  access, cosmetics and your own server currency, and accepting donations. They forbid giving
  Facepunch DLC or paid skins to players who did not buy them. You are responsible for what your
  server sells.
- **Texts.** Player texts are in `oxide/lang/en/CoinsPay.json` and `oxide/lang/ru/CoinsPay.json`
  (Carbon: `carbon/lang`). The language follows the player's; `"language": "ru"` or `"en"` pins one.
- **No window?** Set `"menu": "chat"`: the store works in chat alone, and the link still goes to
  chat and the F1 console.
