import json
import os
import sys
import tempfile
import threading
import unittest
import urllib.request

sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), ".."))

import content  # noqa: E402
import server  # noqa: E402

SETTINGS_KEYS = ["config", "prices", "iap", "purchaseDefs", "bonusRestorePrice", "customization", "cards",
                 "revive", "cardUpgrades", "cardTypeUpgradeValues", "rewards", "missionRewards"]
CONFIG_KEYS = ["heartRegenerationTimeInSeconds", "diamondsPerCoin", "diamondScoreMultiplier",
               "distanceScoreMultiplier", "coinsPerGem", "giftRequestTimeoutMinutes", "maxHeartsGenerated",
               "coinsPerMatch", "gemsPerMatch", "honorWinCoinMatch", "honorLosCoinMatch",
               "honorWinGemMatch", "honorLosGemMatch"]


class Base(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.store = server.Store(os.path.join(self.tmp.name, "save.json"))

    def tearDown(self):
        self.tmp.cleanup()

    def call(self, path, body=None):
        resp, _ = server.dispatch(self.store, path, body or {})
        return resp

    def ok(self, path, body=None):
        r = self.call(path, body)
        self.assertNotIn("error", r, r)
        return r


class P0(Base):
    def test_new_session_shape(self):
        r = self.ok("newSessionSdk", {"userData": {}, "channelId": 0})
        for k in ("sessionToken", "user", "gameState", "cards", "settings", "customIdentifiers", "missionStatus"):
            self.assertIn(k, r)
        for k in SETTINGS_KEYS:
            self.assertIn(k, r["settings"])
        for k in CONFIG_KEYS:
            self.assertIn(k, r["settings"]["config"])
        gs = r["gameState"]
        for k in ("GEMS", "COINS", "hearts", "heartTime", "serverTime"):
            self.assertIn(k, gs)
        self.assertEqual(len(r["cards"]), len(content.CARDS))
        self.assertEqual(len(r["settings"]["cards"]), len(content.CARDS))
        self.assertIs(r["newUser"], True)
        self.assertIs(self.ok("newSession")["newUser"], False)

    def test_tutorials_marked_done(self):
        r = self.ok("newSession")
        self.assertIn("TutorialSequenceBuyCards", r["customIdentifiers"])

    def test_mission_status(self):
        ms = self.ok("newSession")["missionStatus"]
        self.assertEqual(len(ms["currentMissions"]), content.MISSION_PACK_SIZE)
        for k in ("missionIndex", "packStartIndex", "buffs", "consecutiveBonusMissions"):
            self.assertIn(k, ms)
        for m in ms["currentMissions"]:
            self.assertIn(m["logic"], ("event", "score", "distance"))

    def test_unhandled_returns_success(self):
        resp, unhandled = server.dispatch(self.store, "no/such/path", {})
        self.assertTrue(unhandled)
        self.assertNotIn("error", resp)

    def test_misc_p0(self):
        self.assertIn("donePurchases", self.ok("userMetaProfile/get"))
        self.ok("time")
        self.ok("gamestate")
        self.ok("up")


class P1(Base):
    def test_run_flow(self):
        self.ok("newSession")
        r = self.ok("level/start", {"chosenCards": ["1", "2"], "gameMode": 1})
        self.assertEqual(r["gameState"]["lastUsedCards"], ["1", "2"])
        before = r["gameState"]["COINS"]
        r = self.ok("level/end", {"distance": 1000, "diamonds": 50, "gems": 2, "destruction": 5,
                                  "scoreExtra": 1, "coinExtra": 1, "missionProgress": 0})
        got = r["got"]
        self.assertEqual(got["coins"], 50)
        self.assertEqual(got["score"], 1000 + 500 + 5)
        self.assertTrue(got["highscore"])
        self.assertEqual(r["gameState"]["COINS"], before + 50)
        self.assertEqual(r["gameState"]["score"], got["score"])
        r = self.ok("level/end", {"distance": 10, "diamonds": 0, "gems": 0, "destruction": 0, "missionProgress": 0})
        self.assertFalse(r["got"]["highscore"])

    def test_mission_progress_rewards_and_pack_roll(self):
        self.ok("newSession")
        r = self.ok("level/end", {"distance": 1, "missionProgress": 3})
        self.assertEqual(len(r["missionStatus"]["reward"]), 3)
        self.assertEqual(r["missionStatus"]["missionIndex"], 3)
        r = self.ok("level/end", {"distance": 1, "missionProgress": 10})
        self.assertEqual(r["missionStatus"]["packStartIndex"], 10)
        self.assertEqual(r["missionStatus"]["missionIndex"], 10)

    def test_upgrade_and_currency(self):
        self.ok("newSession")
        c0 = self.ok("tutorial/upgradeCard")
        coins = c0["gameState"]["COINS"]
        level = next(c for c in c0["cards"] if c["id"] == "1")["amount"]
        r = self.ok("card/upgrade", {"cardId": "1", "currencyUsed": {"COINS": 100}})
        self.assertEqual(r["gameState"]["COINS"], coins - 100)
        self.assertEqual(next(c for c in r["cards"] if c["id"] == "1")["amount"], level + 1)
        r = self.call("card/upgrade", {"cardId": "1", "currencyUsed": {"COINS": 10 ** 9}})
        self.assertIn("error", r)

    def test_upgrade_full(self):
        self.ok("newSession")
        r = self.ok("card/upgrade/full", {"cardId": "2", "currencyUsed": {"COINS": 1000}})
        self.assertEqual(next(c for c in r["cards"] if c["id"] == "2")["amount"], content.MAX_LEVEL)

    def test_get_pack(self):
        self.ok("newSession")
        r = self.ok("card/getPack", {"cardPack": "cardPackCommon"})
        self.assertEqual(len(r["newCards"]), 3)
        self.assertEqual(r["gameState"]["COINS"], 200000 - 1000)
        self.assertIn("error", self.call("card/getPack", {"cardPack": "bogus"}))

    def test_evolve_and_sacrifice(self):
        self.ok("newSession")
        r = self.ok("card/evolve", {"cardId": "41"})
        self.assertEqual(next(c for c in r["cards"] if c["id"] == "41")["amount"], 1)
        self.assertIn("error", self.call("card/evolve", {"cardId": "41"}))
        r = self.ok("card/sacrifice", {"cardId": "41", "sacrificedCardIds": ["1"] * 3 + ["2"] * 3})
        e = next(c for c in r["cards"] if c["id"] == "41")
        self.assertGreaterEqual(e["amount"], 1)

    def test_equip_slots_custom(self):
        self.ok("newSession")
        self.assertEqual(self.ok("card/equip", {"cardId": "1003"})["gameState"]["equippedCard"], "1003")
        self.ok("card/slot/unlock", {"cardId": "41"})
        self.ok("card/slot/buyUnlock", {"cardId": "41"})
        r = self.ok("customization/set", {"customizations": [{"part": "head", "skin": "hat_knight"}]})
        self.assertEqual(r["gameState"]["head"], "hat_knight")
        r = self.ok("customization/purchase", {"item": "hat_knight", "currencyUsed": {"COINS": 10}})
        self.assertIn("hat_knight", r["customizationItems"])
        self.assertIn("error", self.call("customization/purchase", {"item": "nope"}))

    def test_shop_and_consumables(self):
        self.ok("newSession")
        g0 = self.ok("tutorial/upgradeCard")["gameState"]
        r = self.ok("purchaseCoins", {"coinPack": "coinsSmall"})
        self.assertEqual(r["gameState"]["COINS"], g0["COINS"] + 2000)
        self.assertEqual(r["gameState"]["GEMS"], g0["GEMS"] - 20)
        r = self.ok("purchaseconsumables", {"purchaseId": "cardBoostConsumable1"})
        n = next(c for c in r["ownedConsumables"] if c["consumableId"] == "cardBoostConsumable")["amount"]
        self.assertEqual(n, 6)
        r = self.ok("tutorial/consumable", {"consumableId": "cardBoostConsumable"})
        r = self.ok("level/start", {"chosenCards": [], "usedConsumables": [{"consumableId": "cardBoostConsumable", "amount": 1}]})
        self.assertEqual(next(c for c in r["ownedConsumables"] if c["consumableId"] == "cardBoostConsumable")["amount"], 6)

    def test_achievements(self):
        self.ok("newSession")
        self.ok("achievement/updateProgress", {"progressUpdateList": [{"achievementId": "AchievementPlayRounds", "progressToSet": 0}]})
        r = self.ok("tutorial/upgradeCard")
        self.assertEqual(r["achievements"][0]["achievement_name"], "AchievementPlayRounds")
        gems = r["gameState"]["GEMS"]
        r = self.ok("achievement/claim", {"achievementsToClaim": ["AchievementPlayRounds"]})
        self.assertGreater(r["gameState"]["GEMS"], gems)
        self.assertIn("error", self.call("achievement/claim", {"achievementsToClaim": ["AchievementPlayRounds"]}))

    def test_iap(self):
        self.ok("newSession")
        g0 = self.ok("tutorial/upgradeCard")["gameState"]["GEMS"]
        r = self.ok("iap/query", {"name": "gemSmall", "receipt": "x", "transaction": "t", "count": 0})
        self.assertEqual(r["gameState"]["GEMS"], g0 + 60)
        self.assertIn("count", r)
        self.assertEqual(len(self.ok("iap/cardPack", {"name": "cardPackEpic"})["newCards"]), 3)
        self.assertTrue(self.ok("iap/offerBundle", {"name": "offerBundle1"})["rewards"])

    def test_revive_and_bonus(self):
        self.ok("newSession")
        g = self.ok("revive", {"reviveCount": 0, "currencyUsed": {"GEMS": 1}})["gameState"]["GEMS"]
        self.assertEqual(g, 4999)
        self.ok("mission/bonusReset", {"currencyUsed": {"GEMS": 5}})

    def test_checkin(self):
        self.ok("newSession")
        self.assertFalse(self.ok("opevent/getcheckin")["todayChecked"])
        self.ok("opevent/claimcheckin")
        self.assertTrue(self.ok("opevent/getcheckin")["todayChecked"])
        self.assertIn("error", self.call("opevent/claimcheckin"))

    def test_custom_ids(self):
        r = self.ok("customIds/add", {"customIdentifiers": {"x": 1}})
        self.assertEqual(r["customIdentifiers"]["x"], 1)

    def test_persistence(self):
        self.ok("newSession")
        self.ok("card/equip", {"cardId": "1002"})
        again = server.Store(self.store.path)
        self.assertEqual(again.data["equippedCard"], "1002")


class Http(Base):
    def test_http_roundtrip(self):
        srv = server.make_server("127.0.0.1", 0, self.store)
        t = threading.Thread(target=srv.serve_forever, daemon=True)
        t.start()
        try:
            url = "http://127.0.0.1:%d/api/newSessionSdk" % srv.server_address[1]
            req = urllib.request.Request(url, data=b'{"userData":{}}',
                                         headers={"Content-Type": "application/vnd.boomlagoon.monsu+json"})
            with urllib.request.urlopen(req) as resp:
                self.assertEqual(resp.headers["Content-Type"], "application/vnd.boomlagoon.monsu+json")
                data = json.loads(resp.read())
            self.assertIn("settings", data)
        finally:
            srv.shutdown()
            srv.server_close()


if __name__ == "__main__":
    unittest.main()
