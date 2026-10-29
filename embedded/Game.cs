// Embedded offline "server": port of server/server.py.  Request path + JSON body in, JSON response out.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace MonsuOffline
{
    using Dict = Dictionary<string, object>;

    public class ApiError : Exception
    {
        public string Code;
        public ApiError(string code, string message) : base(message) { Code = code; }
    }

    public delegate Dict Handler(Dict body);

    public class Game
    {
        readonly string savePath;
        readonly Random rng = new Random();
        readonly Dictionary<string, Handler> routes = new Dictionary<string, Handler>();
        Dict s;   // the save
        public bool LastUnhandled;

        public Game(string savePath)
        {
            this.savePath = savePath;
            Load();
            Register();
        }

        // ------------------------------------------------------------------ save

        static string Now()
        {
            return DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture);
        }

        static string Today()
        {
            return DateTime.UtcNow.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }

        static string NewId()
        {
            return Guid.NewGuid().ToString("N");
        }

        Dict NewSave()
        {
            Dict cards = J.Map();
            foreach (CardDef c in Content.Cards)
            {
                int amount;
                if (c.T == "c" || c.T == "r" || c.T == "e") amount = 5;
                else if (c.T == "u" || c.T == "j" || c.T == "h") amount = 1;
                else amount = 0;
                Dict e = J.Map();
                e["amount"] = amount; e["progress"] = 0; e["unlocked"] = true;
                cards[c.Id] = e;
            }
            Dict d = J.Map();
            d["user"] = "local-" + NewId().Substring(0, 8);
            d["accessToken"] = NewId();
            d["newUser"] = true;
            d["coins"] = 200000;
            d["gems"] = 5000;
            d["hearts"] = Content.HeartsDefault;
            d["heartTime"] = Now();
            d["bestScore"] = 0;
            d["bestDistance"] = 0;
            d["totalDistance"] = 0;
            d["totalScore"] = 0;
            d["cards"] = cards;
            List<object> owned = J.List();
            foreach (CustDef c in Content.Customization) owned.Add(c.Name);
            d["owned_customization"] = owned;
            d["selected"] = J.Map();
            d["equippedCard"] = "";
            d["lastUsedCards"] = J.List();
            Dict cons = J.Map();
            foreach (string n in Content.Consumables) cons[n] = 5;
            d["consumables"] = cons;
            d["achievements"] = J.Map();
            Dict m = J.Map(); m["index"] = 0; m["packStart"] = 0;
            d["mission"] = m;
            Dict ids = J.Map();
            foreach (string n in Content.TutorialSequences) ids[n] = 0;
            d["customIds"] = ids;
            Dict ci = J.Map(); ci["chain"] = 0; ci["last"] = "";
            d["checkin"] = ci;
            d["monthCard"] = false;
            return d;
        }

        void Load()
        {
            try
            {
                if (savePath != null && File.Exists(savePath))
                {
                    StreamReader rd = new StreamReader(savePath);
                    string text = rd.ReadToEnd();
                    rd.Dispose();
                    s = Json.Parse(text) as Dict;
                    if (s != null && s.ContainsKey("cards")) return;
                }
            }
            catch (Exception) { }
            s = NewSave();
            Save();
        }

        void Save()
        {
            if (savePath == null) return;
            try
            {
                // write a temp file first, then copy over the save, so a crash never leaves a half-written save
                string tmp = savePath + ".tmp";
                StreamWriter w = new StreamWriter(tmp);
                w.Write(Json.Write(s));
                w.Close();
                File.Copy(tmp, savePath, true);
            }
            catch (Exception) { }
        }

        public void Reset()
        {
            s = NewSave();
            Save();
        }

        // ------------------------------------------------------------------ dispatch

        /// <summary>Run one API call.  `path` may be a full URL; only the part after "/api/" matters.</summary>
        public string Handle(string path, string body)
        {
            int i = path.IndexOf("/api/");
            if (i >= 0) path = path.Substring(i + 5);
            int q = path.IndexOf('?');
            if (q >= 0) path = path.Substring(0, q);
            path = path.Trim('/');
            Dict req = null;
            try { req = Json.Parse(body) as Dict; } catch (Exception) { }
            if (req == null) req = J.Map();
            return Json.Write(Dispatch(path, req));
        }

        public Dict Dispatch(string path, Dict body)
        {
            LastUnhandled = false;
            Handler h;
            if (!routes.TryGetValue(path, out h))
            {
                LastUnhandled = true;
                Dict ok = J.Map();
                ok["success"] = true;
                return ok;
            }
            try
            {
                return h(body);
            }
            catch (ApiError e)
            {
                Dict err = J.Map();
                Dict inner = J.Map();
                inner["code"] = e.Code; inner["message"] = e.Message;
                err["error"] = inner;
                return err;
            }
        }

        // ------------------------------------------------------------------ state builders

        int N(string k) { return J.Int(s, k, 0); }
        Dict Cards() { return (Dict)s["cards"]; }
        Dict CardEntry(string id) { return (Dict)Cards()[id]; }

        int CardAmount(string id)
        {
            object e;
            if (Cards().TryGetValue(id, out e)) return J.Int((Dict)e, "amount", 0);
            return 0;
        }

        int CardLevel(string id) { return Math.Min(CardAmount(id), Content.MaxLevel); }

        List<object> CardList()
        {
            List<object> l = J.List();
            foreach (CardDef c in Content.Cards)
            {
                Dict e = J.Map();
                Dict src = Cards().ContainsKey(c.Id) ? CardEntry(c.Id) : null;
                e["id"] = c.Id;
                e["amount"] = J.Int(src, "amount", 0);
                e["unlocked"] = J.Bool(src, "unlocked", true);
                e["upgradeProgress"] = J.Int(src, "progress", 0);
                l.Add(e);
            }
            return l;
        }

        Dict MissionStatus(List<object> rewards)
        {
            Dict m = (Dict)s["mission"];
            int start = J.Int(m, "packStart", 0);
            List<object> cur = J.List();
            for (int i = 0; i < Content.MissionPackSize; i++) cur.Add(Content.Mission(start + i));
            Dict st = J.Map();
            st["currentMissions"] = cur;
            st["missionIndex"] = J.Int(m, "index", 0);
            st["packStartIndex"] = start;
            st["buffs"] = J.List();
            st["consecutiveBonusMissions"] = 0;
            if (rewards != null && rewards.Count > 0) st["reward"] = rewards;
            return st;
        }

        Dict GameStateDict()
        {
            Dict g = J.Map();
            g["GEMS"] = N("gems");
            g["COINS"] = N("coins");
            g["hearts"] = N("hearts");
            g["heartTime"] = s["heartTime"];
            g["score"] = N("bestScore");
            g["serverTime"] = Now();
            g["equippedCard"] = s["equippedCard"];
            g["lastUsedCards"] = s["lastUsedCards"];
            Dict sel = (Dict)s["selected"];
            foreach (string cat in Content.Categories) g[cat] = J.Str(sel, cat, "");
            return g;
        }

        Dict FullState()
        {
            Dict r = J.Map();
            r["user"] = s["user"];
            r["gameState"] = GameStateDict();
            r["cards"] = CardList();
            Dict ci = J.Map();
            foreach (object n in (List<object>)s["owned_customization"]) ci[(string)n] = true;
            r["customizationItems"] = ci;
            List<object> oc = J.List();
            foreach (KeyValuePair<string, object> kv in (Dict)s["consumables"])
            {
                Dict e = J.Map(); e["consumableId"] = kv.Key; e["amount"] = kv.Value; oc.Add(e);
            }
            r["ownedConsumables"] = oc;
            List<object> ach = J.List();
            foreach (KeyValuePair<string, object> kv in (Dict)s["achievements"])
            {
                Dict a = (Dict)kv.Value;
                Dict e = J.Map();
                e["achievement_name"] = kv.Key; e["progress"] = a["left"]; e["claimed"] = a["claimed"];
                ach.Add(e);
            }
            r["achievements"] = ach;
            r["missionStatus"] = MissionStatus(null);
            r["messages"] = J.List();
            return r;
        }

        // ------------------------------------------------------------------ helpers

        void Spend(Dict used)
        {
            if (used == null || used.Count == 0) return;
            int coins = J.Int(used, "COINS", 0), gems = J.Int(used, "GEMS", 0);
            if (coins < 0 || gems < 0 || coins > N("coins") || gems > N("gems")) throw new ApiError("notEnoughFunds", "not enough currency");
            s["coins"] = N("coins") - coins;
            s["gems"] = N("gems") - gems;
        }

        void Charge(int price, string currency)
        {
            string key = currency == "COINS" ? "coins" : "gems";
            if (N(key) < price) throw new ApiError("notEnoughFunds", "not enough " + currency);
            s[key] = N(key) - price;
        }

        List<string> DrawCards(string pack, int n)
        {
            int wc, wr, we;
            if (pack == "cardPackCommon") { wc = 75; wr = 22; we = 3; }
            else if (pack == "cardPackRare") { wc = 20; wr = 65; we = 15; }
            else if (pack == "cardPackEpic") { wc = 5; wr = 35; we = 60; }
            else { wc = 60; wr = 30; we = 10; }
            List<string> pool = new List<string>();
            List<int> w = new List<int>();
            int total = 0;
            foreach (CardDef c in Content.Cards)
            {
                int wt;
                if (c.T == "c") wt = wc; else if (c.T == "r") wt = wr; else if (c.T == "e") wt = we; else continue;
                pool.Add(c.Id); w.Add(wt); total += wt;
            }
            List<string> res = new List<string>();
            for (int k = 0; k < n; k++)
            {
                int roll = rng.Next(0, total);
                for (int i = 0; i < pool.Count; i++)
                {
                    roll -= w[i];
                    if (roll < 0) { res.Add(pool[i]); break; }
                }
            }
            return res;
        }

        void AddCards(List<string> ids)
        {
            foreach (string id in ids)
            {
                if (!Cards().ContainsKey(id))
                {
                    Dict e = J.Map(); e["amount"] = 0; e["progress"] = 0; e["unlocked"] = true;
                    Cards()[id] = e;
                }
                CardEntry(id)["amount"] = CardAmount(id) + 1;
            }
        }

        static List<object> ToList(List<string> l)
        {
            List<object> r = J.List();
            foreach (string x in l) r.Add(x);
            return r;
        }

        void Grant(string type, int amount)
        {
            if (type == "COINS") s["coins"] = N("coins") + amount;
            else if (type == "GEMS") s["gems"] = N("gems") + amount;
            else if (type == "HEARTS") s["hearts"] = N("hearts") + amount;
            else
            {
                Dict cons = (Dict)s["consumables"];
                if (cons.ContainsKey(type)) cons[type] = J.Int(cons, type, 0) + (amount == 0 ? 1 : amount);
            }
        }

        Dict WithExtra(string k, object v)
        {
            Dict r = FullState();
            r[k] = v;
            return r;
        }

        Dict Ok()
        {
            Dict r = J.Map();
            r["success"] = true;
            return r;
        }

        // ------------------------------------------------------------------ handlers

        void Register()
        {
            routes["newSession"] = NewSession;
            routes["newSessionSdk"] = NewSession;
            routes["level/start"] = LevelStart;
            routes["level/end"] = LevelEnd;
            routes["revive"] = delegate(Dict b) { Spend(J.Obj(b, "currencyUsed")); Save(); return FullState(); };
            routes["card/upgrade"] = CardUpgrade;
            routes["card/upgrade/full"] = CardUpgradeFull;
            routes["card/sacrifice"] = CardSacrifice;
            routes["card/evolve"] = CardEvolve;
            routes["card/equip"] = CardEquip;
            routes["card/getPack"] = GetPack;
            routes["card/slot/unlock"] = SlotUnlock;
            routes["card/slot/buyUnlock"] = SlotBuyUnlock;
            routes["card/slot/keys"] = delegate(Dict b) { Dict r = J.Map(); r["keys"] = J.List(); return r; };
            routes["purchaseCoins"] = PurchaseCoins;
            routes["purchaseconsumables"] = PurchaseConsumables;
            routes["customization/set"] = CustomizationSet;
            routes["customization/purchase"] = CustomizationPurchase;
            routes["mission/bonusReset"] = delegate(Dict b) { Spend(J.Obj(b, "currencyUsed")); Save(); return FullState(); };
            routes["achievement/updateProgress"] = AchUpdate;
            routes["achievement/claim"] = AchClaim;
            routes["customIds/add"] = CustomIds;
            routes["tutorial/heartRefill"] = delegate(Dict b) { s["hearts"] = Math.Max(N("hearts"), Content.HeartsDefault); Save(); return FullState(); };
            routes["tutorial/luckyPack"] = delegate(Dict b) { List<string> n = DrawCards("cardPackCommon", 3); AddCards(n); Save(); return WithExtra("newCards", ToList(n)); };
            routes["tutorial/consumable"] = TutorialConsumable;
            routes["tutorial/upgradeCard"] = StateOnly;
            routes["tutorial/legendarySlot"] = StateOnly;
            routes["iap/query"] = IapQuery;
            routes["iap/cardPack"] = delegate(Dict b) { List<string> n = DrawCards(J.Str(b, "name", ""), 3); AddCards(n); Save(); return WithExtra("newCards", ToList(n)); };
            routes["iap/offerBundle"] = IapOfferBundle;
            routes["messages/get"] = delegate(Dict b) { Dict r = J.Map(); r["messages"] = J.List(); return r; };
            routes["messages/claim"] = StateOnly;
            routes["opevent/get"] = delegate(Dict b) { Dict r = J.Map(); r["events"] = J.List(); return r; };
            routes["opevent/getcheckin"] = CheckinGet;
            routes["opevent/claimcheckin"] = CheckinClaim;
            routes["opevent/getmonthcard"] = MonthCardGet;
            routes["opevent/claimmonthcard"] = StateOnly;
            routes["opevent/claim"] = StateOnly;
            routes["ranking/top"] = RankingTop;
            routes["userMetaProfile/get"] = delegate(Dict b) { Dict r = J.Map(); r["donePurchases"] = false; return r; };
            routes["GetTime.ashx"] = TimeNow;
            routes["time"] = TimeNow;
            routes["up"] = delegate(Dict b) { return Ok(); };
            routes["gamestate"] = delegate(Dict b) { return Ok(); };
            routes["bundleshown"] = delegate(Dict b) { return Ok(); };
            routes["gamecenter/name"] = delegate(Dict b) { return Ok(); };
            string[] pvp = "arena/get,arena/status,arena/entry,arena/match,arena/breathe,arena/checkResult,arena/claim".Split(',');
            foreach (string p in pvp)
                routes[p] = delegate(Dict b) { throw new ApiError("pvpUnavailable", "PvP is not available offline"); };
        }

        Dict StateOnly(Dict b) { return FullState(); }

        Dict TimeNow(Dict b)
        {
            Dict r = J.Map();
            r["time"] = Now();
            return r;
        }

        Dict NewSession(Dict b)
        {
            Dict r = FullState();
            r["sessionToken"] = NewId();
            r["accessToken"] = s["accessToken"];
            r["newUser"] = J.Bool(s, "newUser", false);
            s["newUser"] = false;
            r["settings"] = Content.Settings();
            r["customIdentifiers"] = s["customIds"];
            r["pvpEnabled"] = false;
            r["announcements"] = J.List();
            r["testingGroup"] = "default";
            Save();
            return r;
        }

        Dict LevelStart(Dict b)
        {
            List<object> used = J.List();
            foreach (object o in J.Arr(b, "chosenCards"))
                if (o is string && Cards().ContainsKey((string)o)) used.Add(o);
            s["lastUsedCards"] = used;
            Dict cons = (Dict)s["consumables"];
            foreach (object o in J.Arr(b, "usedConsumables"))
            {
                Dict u = J.M(o);
                string n = J.Str(u, "consumableId", "");
                if (cons.ContainsKey(n)) cons[n] = Math.Max(0, J.Int(cons, n, 0) - J.Int(u, "amount", 1));
            }
            Save();
            return WithExtra("cardsUsedFirstTime", J.List());
        }

        Dict LevelEnd(Dict b)
        {
            double distance = J.Dbl(b, "distance", 0);
            int diamonds = J.Int(b, "diamonds", 0);
            int gems = J.Int(b, "gems", 0);
            int destruction = J.Int(b, "destruction", 0);
            double scoreExtra = J.Dbl(b, "scoreExtra", 1); if (scoreExtra == 0) scoreExtra = 1;
            double coinExtra = J.Dbl(b, "coinExtra", 1); if (coinExtra == 0) coinExtra = 1;
            int distanceScore = (int)Math.Ceiling(distance * 1);
            int diamondScore = (int)Math.Ceiling(10.0 * diamonds);
            int score = (int)((distanceScore + diamondScore + destruction) * scoreExtra);
            int coins = (int)Math.Ceiling(diamonds / 1.0 * coinExtra);
            bool highscore = score > N("bestScore");
            if (highscore) s["bestScore"] = score;
            s["bestDistance"] = Math.Max(N("bestDistance"), (int)distance);
            s["totalDistance"] = N("totalDistance") + (int)distance;
            s["totalScore"] = N("totalScore") + score;
            s["coins"] = N("coins") + coins;
            s["gems"] = N("gems") + gems;

            List<object> rewards = J.List();
            Dict m = (Dict)s["mission"];
            object po = J.Get(b, "missionProgress");
            if (po != null && !(po is string) && !(po is bool))
            {
                int progress = (int)J.ToD(po);
                int idx = J.Int(m, "index", 0);
                if (progress > idx)
                {
                    for (int i = idx; i < progress; i++)
                    {
                        string[] rw = Content.MissionReward(i);
                        int amt = int.Parse(rw[1]);
                        Grant(rw[0], amt);
                        Dict e = J.Map(); e["rewardType"] = rw[0]; e["rewardAmount"] = amt; rewards.Add(e);
                    }
                    m["index"] = progress;
                    if (progress >= J.Int(m, "packStart", 0) + Content.MissionPackSize) m["packStart"] = progress;
                }
            }
            Save();
            Dict r = FullState();
            r["missionStatus"] = MissionStatus(rewards);
            Dict got = J.Map();
            got["distance"] = (int)Math.Ceiling(distance); got["gems"] = gems; got["coins"] = coins; got["diamonds"] = diamonds;
            got["destruction"] = destruction; got["score"] = score; got["destructionScore"] = destruction;
            got["distanceScore"] = distanceScore; got["diamondScore"] = diamondScore; got["highscore"] = highscore;
            got["accumulatedDistance"] = N("totalDistance"); got["accumulatedScore"] = N("totalScore");
            r["got"] = got;
            return r;
        }

        void RequireOwned(string id)
        {
            if (id == null || CardAmount(id) < 1) throw new ApiError("noCard", "card not owned");
        }

        Dict CardUpgrade(Dict b)
        {
            string id = J.Str(b, "cardId", null);
            RequireOwned(id);
            if (CardLevel(id) >= Content.MaxLevel) throw new ApiError("maxLevel", "card already at max level");
            Spend(J.Obj(b, "currencyUsed"));
            CardEntry(id)["amount"] = CardAmount(id) + 1;
            Save();
            return FullState();
        }

        Dict CardUpgradeFull(Dict b)
        {
            string id = J.Str(b, "cardId", null);
            RequireOwned(id);
            Spend(J.Obj(b, "currencyUsed"));
            CardEntry(id)["amount"] = Math.Max(CardAmount(id), Content.MaxLevel);
            Save();
            return FullState();
        }

        Dict CardSacrifice(Dict b)
        {
            string id = J.Str(b, "cardId", null);
            if (id == null || !Cards().ContainsKey(id)) throw new ApiError("noCard", "unknown card");
            Dict entry = CardEntry(id);
            int points = J.Int(entry, "progress", 0);
            foreach (object o in J.Arr(b, "sacrificedCardIds"))
            {
                string sid = o as string;
                if (sid == null || CardAmount(sid) < 1) throw new ApiError("noCard", "sacrificed card not owned");
                CardEntry(sid)["amount"] = CardAmount(sid) - 1;
                points += Content.SacrificeValue(Content.CardsById[sid].T);
            }
            string t = Content.CardsById[id].T;
            while (CardLevel(id) < Content.MaxLevel)
            {
                int cost = Content.UpgradeCost(CardLevel(id), t);
                if (cost < 0 || points < cost) break;
                points -= cost;
                entry["amount"] = CardAmount(id) + 1;
            }
            entry["progress"] = points;
            Save();
            return FullState();
        }

        Dict CardEvolve(Dict b)
        {
            string id = J.Str(b, "cardId", null);
            CardDef def;
            if (id == null || !Content.CardsById.TryGetValue(id, out def) || def.Requires == null) throw new ApiError("badCard", "card cannot be evolved");
            if (CardAmount(id) > 0) throw new ApiError("owned", "already owned");
            foreach (string r in def.Requires)
                if (CardAmount(r) < 5) throw new ApiError("requirements", "requirements not met");
            CardEntry(id)["amount"] = 1;
            Save();
            return FullState();
        }

        Dict CardEquip(Dict b)
        {
            string id = J.Str(b, "cardId", "");
            if (id.Length > 0 && !Cards().ContainsKey(id)) throw new ApiError("noCard", "unknown hero");
            s["equippedCard"] = id;
            Save();
            return FullState();
        }

        Dict GetPack(Dict b)
        {
            string pack = J.Str(b, "cardPack", "");
            Price p;
            if (!pack.StartsWith("cardPack") || !Content.NonIapByName.TryGetValue(pack, out p)) throw new ApiError("badPack", "unknown pack " + pack);
            Charge(p.Cost, p.Currency);
            List<string> n = DrawCards(pack, p.Amount);
            AddCards(n);
            Save();
            return WithExtra("newCards", ToList(n));
        }

        Dict SlotUnlock(Dict b)
        {
            string id = J.Str(b, "cardId", "");
            if (Cards().ContainsKey(id)) CardEntry(id)["unlocked"] = true;
            Save();
            return FullState();
        }

        Dict SlotBuyUnlock(Dict b)
        {
            Price p = Content.NonIapByName["cardSlotUnlock"];
            Charge(p.Cost, p.Currency);
            return SlotUnlock(b);
        }

        Dict PurchaseCoins(Dict b)
        {
            string pack = J.Str(b, "coinPack", "");
            Price p;
            if (!pack.StartsWith("coins") || !Content.NonIapByName.TryGetValue(pack, out p)) throw new ApiError("badPack", "unknown coin pack");
            Charge(p.Cost, p.Currency);
            s["coins"] = N("coins") + p.Amount;
            Save();
            return FullState();
        }

        Dict PurchaseConsumables(Dict b)
        {
            string pid = J.Str(b, "purchaseId", "");
            string cname = Content.ConsumableOfPurchase(pid);
            if (cname == null) throw new ApiError("badItem", "unknown consumable");
            Price p = Content.NonIapByName[pid];
            Charge(p.Cost, p.Currency);
            Dict cons = (Dict)s["consumables"];
            cons[cname] = J.Int(cons, cname, 0) + p.Amount;
            Save();
            return FullState();
        }

        Dict CustomizationSet(Dict b)
        {
            Dict sel = (Dict)s["selected"];
            foreach (object o in J.Arr(b, "customizations"))
            {
                Dict c = J.M(o);
                string part = J.Str(c, "part", "");
                if (Array.IndexOf(Content.Categories, part) >= 0) sel[part] = J.Str(c, "skin", "");
            }
            Save();
            return FullState();
        }

        Dict CustomizationPurchase(Dict b)
        {
            string item = J.Str(b, "item", "");
            if (!Content.CustomizationByName.ContainsKey(item)) throw new ApiError("badItem", "unknown item");
            List<object> owned = (List<object>)s["owned_customization"];
            if (!owned.Contains(item))
            {
                Spend(J.Obj(b, "currencyUsed"));
                owned.Add(item);
            }
            Save();
            return FullState();
        }

        Dict AchUpdate(Dict b)
        {
            Dict achs = (Dict)s["achievements"];
            foreach (object o in J.Arr(b, "progressUpdateList"))
            {
                Dict u = J.M(o);
                string n = J.Str(u, "achievementId", "");
                if (Content.RewardOfAchievement(n) == null) continue;
                if (!achs.ContainsKey(n))
                {
                    Dict a0 = J.Map(); a0["left"] = 1; a0["claimed"] = false;
                    achs[n] = a0;
                }
                Dict a = (Dict)achs[n];
                if (!J.Bool(a, "claimed", false)) a["left"] = J.Int(u, "progressToSet", J.Int(a, "left", 1));
            }
            Save();
            return Ok();
        }

        Dict AchClaim(Dict b)
        {
            Dict achs = (Dict)s["achievements"];
            foreach (object o in J.Arr(b, "achievementsToClaim"))
            {
                string n = o as string;
                Dict a = n != null && achs.ContainsKey(n) ? (Dict)achs[n] : null;
                if (a == null || J.Int(a, "left", 1) > 0 || J.Bool(a, "claimed", false)) throw new ApiError("notClaimable", "achievement not claimable");
                string[] rw = Content.RewardOfAchievement(n);
                Grant(rw[0], int.Parse(rw[1]));
                a["claimed"] = true;
            }
            Save();
            return FullState();
        }

        Dict CustomIds(Dict b)
        {
            Dict ids = (Dict)s["customIds"];
            foreach (KeyValuePair<string, object> kv in J.Obj(b, "customIdentifiers"))
                ids[kv.Key] = (int)J.ToD(kv.Value);
            Save();
            return WithExtra("customIdentifiers", ids);
        }

        Dict TutorialConsumable(Dict b)
        {
            string n = J.Str(b, "consumableId", "");
            Dict cons = (Dict)s["consumables"];
            if (cons.ContainsKey(n)) cons[n] = J.Int(cons, n, 0) + 1;
            Save();
            return FullState();
        }

        Dict IapQuery(Dict b)
        {
            string name = J.Str(b, "name", "");
            Price p;
            if (!Content.IapByName.TryGetValue(name, out p)) throw new ApiError("badItem", "unknown iap " + name);
            if (name == "MonthCard") s["monthCard"] = true;
            else if (name.StartsWith("gem")) s["gems"] = N("gems") + p.Amount;
            else if (name.StartsWith("energy")) s["hearts"] = N("hearts") + p.Amount;
            else throw new ApiError("badItem", "unknown iap " + name);
            Save();
            return WithExtra("count", J.Int(b, "count", 0));
        }

        Dict IapOfferBundle(Dict b)
        {
            string name = J.Str(b, "name", "");
            List<object> outl = J.List();
            foreach (object bo in Content.BundleOffers())
            {
                Dict bundle = J.M(bo);
                if (J.Str(bundle, "name", "") != name) continue;
                foreach (object ro in J.Arr(bundle, "rewards"))
                {
                    Dict r = J.M(ro);
                    Grant(J.Str(r, "Type", ""), J.Int(r, "Amount", 0));
                    Dict e = J.Map(); e["rewardType"] = r["Type"]; e["rewardAmount"] = r["Amount"]; outl.Add(e);
                }
            }
            Save();
            return WithExtra("rewards", outl);
        }

        Dict CheckinGet(Dict b)
        {
            Dict c = (Dict)s["checkin"];
            Dict r = J.Map();
            r["chainCount"] = J.Int(c, "chain", 0);
            r["todayChecked"] = J.Str(c, "last", "") == Today();
            return r;
        }

        Dict CheckinClaim(Dict b)
        {
            Dict c = (Dict)s["checkin"];
            if (J.Str(c, "last", "") == Today()) throw new ApiError("alreadyChecked", "already checked in today");
            List<object> rewards = Content.SignRewards();
            Dict r = J.M(rewards[J.Int(c, "chain", 0) % rewards.Count]);
            Grant(J.Str(r, "Type", ""), J.Int(r, "Amount", 0));
            c["chain"] = J.Int(c, "chain", 0) + 1;
            c["last"] = Today();
            Save();
            return FullState();
        }

        Dict MonthCardGet(Dict b)
        {
            bool have = J.Bool(s, "monthCard", false);
            Dict r = J.Map();
            r["haveMonthCard"] = have; r["todayClaimed"] = true; r["leftDays"] = have ? 30 : 0;
            return r;
        }

        Dict RankingTop(Dict b)
        {
            Dict me = J.Map();
            me["userId"] = s["user"]; me["name"] = "Monsu"; me["highScore"] = N("bestScore"); me["group"] = 0; me["index"] = 1;
            Dict filler = J.Map();
            filler["userId"] = "end"; filler["name"] = ""; filler["highScore"] = 0; filler["group"] = 0; filler["index"] = 2;
            List<object> list = J.List(); list.Add(me); list.Add(filler);
            Dict top = J.Map(); top["userOrder"] = 1; top["list"] = list;
            return WithExtra("topPlayers", top);
        }
    }
}
