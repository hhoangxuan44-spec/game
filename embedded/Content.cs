// Static game content (port of server/content.py).  Numbers are invented -- the originals are lost.
using System;
using System.Collections.Generic;

namespace MonsuOffline
{
    public class CardDef
    {
        public string Id, Prefab, T, Kind;
        public string[] Requires;
    }

    public class CustDef
    {
        public string Name, Hero, Category, Currency;
        public int Price;
    }

    public class Price
    {
        public string Name, Currency;
        public int Cost, Amount;
        public Price(string name, int cost, string currency, int amount) { Name = name; Cost = cost; Currency = currency; Amount = amount; }
    }

    public static class Content
    {
        public static readonly List<CardDef> Cards = new List<CardDef>();
        public static readonly Dictionary<string, CardDef> CardsById = new Dictionary<string, CardDef>();
        public static readonly List<CustDef> Customization = new List<CustDef>();
        public static readonly Dictionary<string, CustDef> CustomizationByName = new Dictionary<string, CustDef>();
        public static readonly string[] Categories = "arms,head,body,feet".Split(',');
        public static readonly string[] Consumables = "cardBoostConsumable,missionBoostConsumable,magnetBoostConsumable,dashBoostConsumable,powerupBoostConsumable".Split(',');

        public const int MaxLevel = 10;
        public const int HeartsDefault = 99;
        public const int MissionPackSize = 10;

        // non-IAP purchases
        public static readonly List<Price> NonIap = new List<Price>();
        public static readonly Dictionary<string, Price> NonIapByName = new Dictionary<string, Price>();
        // IAP purchases: Cost holds nothing, Amount is what is granted; Currency holds the price string
        public static readonly List<Price> Iap = new List<Price>();
        public static readonly Dictionary<string, Price> IapByName = new Dictionary<string, Price>();

        public static readonly string[] AchievementNames = (
            "AchievementMoneyFlies,AchievementHalfMarathon,AchievementFullMarathon,AchievementAroundTheWorld," +
            "AchievementWinterIsComing,AchievementWardrobeMalfunction,AchievementCardmonger,AchievementGottaGetThemAll," +
            "AchievementHeartbreaker,AchievementGifted,AchievementRareSpecimen,AchievementPureEpic,AchievementThanks," +
            "AchievementPlayRounds").Split(',');

        public static readonly string[] TutorialSequences = (
            "AchievementTutorialSequence,CardPackReminderSequence,CustomizationItemsTutorial,FacebookPromptSequence," +
            "FirstRunDoneResultSequence,IngameTutorialSequence,IntroduceWeeklyTournamentSequence,NotEnoughGemsSequence," +
            "NotEnoughHeartsSequence,OpenWeeklyTournamentCardRewardsTutorialSequence,ShowAppiraterSequence," +
            "TutorialSequenceBuyCards,TutorialSequenceConsumables,TutorialSequenceEquipCard,TutorialSequenceEvolvePossible," +
            "TutorialSequenceFirstBonusMissionBegin,TutorialSequenceFirstBonusMissionCompleted,TutorialSequenceIntroduceEvolve," +
            "TutorialSequenceUpgradeCard,TutorialSequenceUpgradeEvolveCard,TutorialSequenceUpgradeResult,WeeklyMissionTutorialStart").Split(',');

        // logic|localizationId|event|baseAmount|step
        static readonly string[] MissionKinds = (
            "distance|MissionRunDistance||600|150;event|MissionDestroyBuildings|BuildingKilled|8|2;" +
            "score|MissionScorePoints||3000|1000;event|MissionStompEnemies|StompableKilled|6|2;" +
            "event|MissionCollectPowerups|PowerupCollected|3|1;distance|MissionRunDistance||900|200;" +
            "event|MissionDoubleJump|PlayerJumpedTwice|5|2;event|MissionEnterFever|FeverStartedEvent|1|1;" +
            "event|MissionMagnetPowerup|MagnetPowerupCollected|2|1;score|MissionScorePoints||5000|1500").Split(';');

        static void AddNonIap(string n, int cost, string cur, int amount)
        {
            Price p = new Price(n, cost, cur, amount);
            NonIap.Add(p);
            NonIapByName[n] = p;
        }

        static void AddIap(string n, string price, int amount)
        {
            Price p = new Price(n, 0, price, amount);
            Iap.Add(p);
            IapByName[n] = p;
        }

        static Content()
        {
            foreach (string line in Data.Cards.Split(';'))
            {
                string[] f = line.Split('|');
                CardDef c = new CardDef();
                c.Id = f[0]; c.Prefab = f[1]; c.T = f[2]; c.Kind = f[3];
                c.Requires = f[4].Length == 0 ? null : f[4].Split(',');
                Cards.Add(c);
                CardsById[c.Id] = c;
            }
            foreach (string line in Data.Customization.Split(';'))
            {
                string[] f = line.Split('|');
                CustDef c = new CustDef();
                c.Name = f[0]; c.Hero = f[1]; c.Category = f[2]; c.Currency = f[3]; c.Price = int.Parse(f[4]);
                Customization.Add(c);
                CustomizationByName[c.Name] = c;
            }

            AddNonIap("coinsSmall", 20, "GEMS", 2000);
            AddNonIap("coinsMedium", 50, "GEMS", 6000);
            AddNonIap("coinsBig", 100, "GEMS", 14000);
            AddNonIap("coinsHuge", 200, "GEMS", 32000);
            AddNonIap("cardPackCommon", 1000, "COINS", 3);
            AddNonIap("cardPackRare", 30, "GEMS", 3);
            AddNonIap("cardPackEpic", 80, "GEMS", 3);
            AddNonIap("cardSlotUnlock", 20, "GEMS", 1);
            AddNonIap("cardBoostConsumable1", 500, "COINS", 1);
            AddNonIap("missionBoostConsumable1", 500, "COINS", 1);
            AddNonIap("magnetBoostConsumable1", 500, "COINS", 1);
            AddNonIap("dashBoostConsumable1", 500, "COINS", 1);
            AddNonIap("powerupBoostConsumable1", 500, "COINS", 1);

            AddIap("gemSmall", "6", 60);
            AddIap("gemMedium", "30", 330);
            AddIap("gemBig", "98", 1180);
            AddIap("gemHuge", "198", 2500);
            AddIap("gemTremen", "328", 4400);
            AddIap("gemMax", "648", 9000);
            AddIap("energySmall", "6", 20);
            AddIap("energyMedium", "12", 50);
            AddIap("energyBig", "30", 150);
            AddIap("energyHuge", "60", 400);
            AddIap("offerBundle1", "6", 1);
            AddIap("offerBundle2", "30", 1);
            AddIap("MonthCard", "30", 1);
        }

        /// <summary>consumable resource name for a purchase id like "cardBoostConsumable1"; null when not a consumable.</summary>
        public static string ConsumableOfPurchase(string id)
        {
            if (id != null && id.EndsWith("Consumable1") && NonIapByName.ContainsKey(id)) return id.Substring(0, id.Length - 1);
            return null;
        }

        public static string TypeName(string t)
        {
            switch (t)
            {
                case "c": return "common";
                case "r": return "rare";
                case "e": return "epic";
                case "l": return "legendary";
                case "t": return "ultimate";
                case "x": return "exclusive";
            }
            return null;
        }

        public static int SacrificeValue(string t)
        {
            switch (t)
            {
                case "c": return 1;
                case "r": return 2;
                case "e": return 3;
                case "u": return 1;
                case "l": return 5;
                case "t": return 8;
            }
            return 1;
        }

        static int UpgradeBase(int row) { return 100 << row; }

        public static int UpgradeCost(int level, string t)
        {
            string name = TypeName(t);
            if (level < 1 || level > 9 || name == null) return -1;
            int b = UpgradeBase(level - 1);
            switch (name)
            {
                case "common": return b;
                case "rare": return b * 2;
                case "epic": return b * 3;
                case "legendary": return b * 4;
            }
            return b * 5;
        }

        public static string[] RewardOfAchievement(string name)
        {
            for (int i = 0; i < AchievementNames.Length; i++)
                if (AchievementNames[i] == name) return new string[] { "GEMS", (10 + 5 * i).ToString() };
            return null;
        }

        public static Dictionary<string, object> Mission(int index)
        {
            string[] k = MissionKinds[index % MissionKinds.Length].Split('|');
            int tier = index / MissionKinds.Length;
            Dictionary<string, object> m = J.Map();
            m["amount"] = int.Parse(k[3]) + int.Parse(k[4]) * tier;
            m["localizationId"] = k[1];
            m["logic"] = k[0];
            m["isBonus"] = false;
            m["difficulty"] = Math.Min(1 + index / 5, 10);
            if (k[2].Length > 0) m["event"] = k[2];
            return m;
        }

        public static string[] MissionReward(int index)
        {
            if (index % 5 != 0) return new string[] { "COINS", (200 + 50 * index).ToString() };
            return new string[] { "GEMS", (5 + index / 5).ToString() };
        }

        public static List<object> SignRewards()
        {
            List<object> l = J.List();
            for (int i = 0; i < 6; i++) l.Add(Rw("COINS", 500 * (i + 1)));
            l.Add(Rw("GEMS", 30));
            return l;
        }

        static Dictionary<string, object> Rw(string type, int amount)
        {
            Dictionary<string, object> m = J.Map();
            m["Type"] = type; m["Id"] = ""; m["Amount"] = amount;
            return m;
        }

        static Dictionary<string, object> BundleOffer(string name, int gems, int coins)
        {
            Dictionary<string, object> o = J.Map();
            o["name"] = name;
            List<object> r = J.List();
            Dictionary<string, object> a = J.Map(); a["Type"] = "GEMS"; a["Amount"] = gems; r.Add(a);
            Dictionary<string, object> b = J.Map(); b["Type"] = "COINS"; b["Amount"] = coins; r.Add(b);
            o["rewards"] = r;
            return o;
        }

        public static List<object> BundleOffers()
        {
            List<object> l = J.List();
            l.Add(BundleOffer("offerBundle1", 100, 5000));
            l.Add(BundleOffer("offerBundle2", 500, 30000));
            return l;
        }

        public static Dictionary<string, object> Settings()
        {
            Dictionary<string, object> s = J.Map();
            Dictionary<string, object> cfg = J.Map();
            cfg["heartRegenerationTimeInSeconds"] = 300;
            cfg["diamondsPerCoin"] = 1;
            cfg["diamondScoreMultiplier"] = 10;
            cfg["distanceScoreMultiplier"] = 1;
            cfg["coinsPerGem"] = 10;
            cfg["giftRequestTimeoutMinutes"] = 60;
            cfg["maxHeartsGenerated"] = 5;
            cfg["coinsPerMatch"] = 100;
            cfg["gemsPerMatch"] = 5;
            cfg["honorWinCoinMatch"] = 50;
            cfg["honorLosCoinMatch"] = 15;
            cfg["honorWinGemMatch"] = 50;
            cfg["honorLosGemMatch"] = 15;
            s["config"] = cfg;

            List<object> prices = J.List(), purchaseDefs = J.List(), iap = J.List();
            foreach (Price p in NonIap)
            {
                Dictionary<string, object> m = J.Map();
                m["name"] = p.Name; m["price"] = p.Cost; m["currency"] = p.Currency;
                prices.Add(m);
                Dictionary<string, object> d = J.Map();
                d["name"] = p.Name; d["amount"] = p.Amount;
                purchaseDefs.Add(d);
            }
            foreach (Price p in Iap)
            {
                Dictionary<string, object> m = J.Map();
                m["name"] = p.Name; m["apple"] = p.Name; m["google"] = p.Name; m["ch"] = p.Name; m["price"] = "CNY " + p.Currency;
                iap.Add(m);
                Dictionary<string, object> d = J.Map();
                d["name"] = p.Name; d["amount"] = p.Amount;
                purchaseDefs.Add(d);
            }
            s["prices"] = prices;
            s["iap"] = iap;
            s["purchaseDefs"] = purchaseDefs;

            List<object> restore = J.List();
            Dictionary<string, object> r0 = J.Map(); r0["BonusMissions"] = 0; r0["Price"] = 5; r0["Currency"] = "GEMS";
            restore.Add(r0);
            s["bonusRestorePrice"] = restore;

            List<object> cust = J.List();
            foreach (CustDef c in Customization)
            {
                Dictionary<string, object> m = J.Map();
                m["name"] = c.Name; m["price"] = c.Price; m["currency"] = c.Currency;
                cust.Add(m);
            }
            s["customization"] = cust;

            List<object> cards = J.List();
            foreach (CardDef c in Cards)
            {
                Dictionary<string, object> m = J.Map();
                m["id"] = c.Id; m["type"] = c.T;
                cards.Add(m);
            }
            s["cards"] = cards;

            List<object> revive = J.List();
            string[] costs = "1,2,3,5,8".Split(',');
            for (int i = 0; i < costs.Length; i++)
            {
                Dictionary<string, object> m = J.Map();
                m["currency"] = "GEMS"; m["amount"] = int.Parse(costs[i]);
                revive.Add(m);
            }
            s["revive"] = revive;

            List<object> ups = J.List();
            for (int row = 0; row < 9; row++)
            {
                int b = UpgradeBase(row);
                Dictionary<string, object> m = J.Map();
                m["common"] = b; m["rare"] = b * 2; m["epic"] = b * 3; m["legendary"] = b * 4; m["ultimate"] = b * 5; m["exclusive"] = b * 5;
                ups.Add(m);
            }
            s["cardUpgrades"] = ups;

            List<object> vals = J.List();
            string[] types = "c,r,e,u,l,t".Split(',');
            for (int i = 0; i < types.Length; i++)
            {
                Dictionary<string, object> m = J.Map();
                m["type"] = types[i]; m["value"] = SacrificeValue(types[i]);
                vals.Add(m);
            }
            s["cardTypeUpgradeValues"] = vals;

            List<object> rewards = J.List();
            for (int i = 0; i < AchievementNames.Length; i++)
            {
                Dictionary<string, object> m = J.Map();
                m["rewardName"] = AchievementNames[i]; m["rewardType"] = "GEMS"; m["rewardAmount"] = 10 + 5 * i;
                rewards.Add(m);
            }
            for (int i = 1; i <= 3; i++)
            {
                Dictionary<string, object> m = J.Map();
                m["rewardName"] = "weeklyTournament" + i; m["rewardType"] = "COINS"; m["rewardAmount"] = 1000 * (4 - i);
                rewards.Add(m);
            }
            s["rewards"] = rewards;

            Dictionary<string, object> mr = J.Map();
            List<object> bonus = J.List();
            for (int i = 0; i < 10; i++) { Dictionary<string, object> b = J.Map(); b["Type"] = "GEMS"; bonus.Add(b); }
            mr["bonus"] = bonus;
            s["missionRewards"] = mr;

            s["rankTopRewards"] = J.List();
            s["rankGroups"] = J.List();
            s["honorGoods"] = J.List();
            s["bundleOffers"] = BundleOffers();

            Dictionary<string, object> ev = J.Map();
            ev["eventId"] = 5001; ev["type"] = "checkin"; ev["value"] = 0; ev["eventRewards"] = SignRewards();
            List<object> evs = J.List(); evs.Add(ev);
            Dictionary<string, object> g = J.Map();
            g["group"] = 5; g["active"] = true; g["startTime"] = "2000-01-01 00:00:00"; g["endTime"] = "2099-01-01 00:00:00";
            g["refreshTime"] = "00:00:00"; g["icon"] = ""; g["name"] = "checkin"; g["t_localizationId"] = ""; g["d_localizationId"] = "";
            g["stamp"] = ""; g["show"] = true; g["events"] = evs;
            List<object> groups = J.List(); groups.Add(g);
            s["eventGroups"] = groups;
            return s;
        }
    }
}
