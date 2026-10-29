#!/usr/bin/env python3
"""Offline stand-in for the Monsu game server.

Pure standard library (runs in Termux).  Start with:  python3 server.py [--port 8083]

Protocol summary (see docs/protocol.md):
  * POST <base>/api/<path>, body = JSON, response = JSON object (no envelope).
  * A response containing the key "error" is a failure; anything else is success.
  * Almost every mutating call answers with the full player state ("gameState",
    "cards", "customizationItems", "ownedConsumables", "achievements",
    "missionStatus") because the client replaces its state with whatever it gets.
"""
import argparse
import copy
import json
import math
import os
import random
import threading
import time
import uuid
from datetime import datetime, timezone
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer

import content

HERE = os.path.dirname(os.path.abspath(__file__))
SAVE_PATH = os.path.join(HERE, "data", "save.json")
LOG_PATH = os.path.join(HERE, "logs", "requests.log")
TIME_FMT = "%Y-%m-%d %H:%M:%S.%f"

LOCK = threading.RLock()


def now_str(ts=None):
    d = datetime.fromtimestamp(ts if ts is not None else time.time(), timezone.utc)
    return d.strftime(TIME_FMT)[:-3]


def today():
    return datetime.now(timezone.utc).strftime("%Y-%m-%d")


class ApiError(Exception):
    def __init__(self, code, message=""):
        Exception.__init__(self, code)
        self.code = code
        self.message = message


# --------------------------------------------------------------------------- save


def new_save():
    cards = {}
    for c in content.CARDS:
        if c["t"] in ("c", "r", "e", "u"):
            amount = 5 if c["t"] != "u" else 1
        elif c["t"] in ("j", "h"):
            amount = 1
        else:
            amount = 0
        cards[c["id"]] = {"amount": amount, "progress": 0, "unlocked": True}
    return {
        "user": "local-" + uuid.uuid4().hex[:8],
        "accessToken": uuid.uuid4().hex,
        "newUser": True,
        "coins": 200000,
        "gems": 5000,
        "hearts": content.HEARTS_DEFAULT,
        "heartTime": now_str(),
        "bestScore": 0,
        "bestDistance": 0,
        "totalDistance": 0,
        "totalScore": 0,
        "cards": cards,
        "owned_customization": [c["name"] for c in content.CUSTOMIZATION],
        "selected": {},
        "equippedCard": "",
        "lastUsedCards": [],
        "consumables": {n: 5 for n in content.CONSUMABLES},
        "achievements": {},
        "mission": {"index": 0, "packStart": 0},
        "customIds": {n: 0 for n in content.TUTORIAL_SEQUENCES},
        "checkin": {"chain": 0, "last": ""},
        "monthCard": {"have": False},
        "offered": {},
    }


class Store:
    def __init__(self, path=SAVE_PATH):
        self.path = path
        self.data = None
        self.load()

    def load(self):
        try:
            with open(self.path, encoding="utf-8") as f:
                self.data = json.load(f)
        except (IOError, ValueError):
            self.data = new_save()
            self.save()

    def save(self):
        os.makedirs(os.path.dirname(self.path), exist_ok=True)
        tmp = self.path + ".tmp"
        with open(tmp, "w", encoding="utf-8") as f:
            json.dump(self.data, f, indent=1)
        os.replace(tmp, self.path)

    def reset(self):
        self.data = new_save()
        self.save()


# --------------------------------------------------------------------------- state builders


def card_entries(s):
    out = []
    for c in content.CARDS:
        e = s["cards"].get(c["id"], {"amount": 0, "progress": 0, "unlocked": True})
        out.append({"id": c["id"], "amount": e["amount"], "unlocked": bool(e.get("unlocked", True)),
                    "upgradeProgress": e.get("progress", 0)})
    return out


def mission_status(s, rewards=None):
    m = s["mission"]
    start = m["packStart"]
    cur = [content.mission(start + i) for i in range(content.MISSION_PACK_SIZE)]
    st = {
        "currentMissions": cur,
        "missionIndex": m["index"],
        "packStartIndex": start,
        "buffs": [],
        "consecutiveBonusMissions": 0,
    }
    if rewards:
        st["reward"] = rewards
    return st


def achievements_list(s):
    return [{"achievement_name": n, "progress": a["left"], "claimed": a["claimed"]}
            for n, a in s["achievements"].items()]


def game_state(s):
    gs = {
        "GEMS": s["gems"],
        "COINS": s["coins"],
        "hearts": s["hearts"],
        "heartTime": s["heartTime"],
        "score": s["bestScore"],
        "serverTime": now_str(),
        "equippedCard": s["equippedCard"],
        "lastUsedCards": s["lastUsedCards"],
    }
    for cat in content.CATEGORIES:
        gs[cat] = s["selected"].get(cat, "")
    return gs


def full_state(s, **extra):
    r = {
        "user": s["user"],
        "gameState": game_state(s),
        "cards": card_entries(s),
        "customizationItems": {n: True for n in s["owned_customization"]},
        "ownedConsumables": [{"consumableId": n, "amount": a} for n, a in s["consumables"].items()],
        "achievements": achievements_list(s),
        "missionStatus": mission_status(s),
        "messages": [],
    }
    r.update(extra)
    return r


# --------------------------------------------------------------------------- helpers


def spend(s, currency_used):
    """Deduct the currency the client says it used.  Raises when the player can't pay."""
    if not currency_used:
        return
    coins = int(currency_used.get("COINS", 0) or 0)
    gems = int(currency_used.get("GEMS", 0) or 0)
    if coins < 0 or gems < 0 or coins > s["coins"] or gems > s["gems"]:
        raise ApiError("notEnoughFunds", "not enough currency")
    s["coins"] -= coins
    s["gems"] -= gems


def charge(s, price, currency):
    key = "coins" if currency == "COINS" else "gems"
    if s[key] < price:
        raise ApiError("notEnoughFunds", "not enough " + currency)
    s[key] -= price


def card_level(s, cid):
    return min(s["cards"][cid]["amount"], content.MAX_LEVEL)


def draw_cards(pack, n=3):
    pool = [c["id"] for c in content.CARDS if c["t"] in content.PACKABLE_TYPES]
    weights = {
        "cardPackCommon": {"c": 75, "r": 22, "e": 3},
        "cardPackRare": {"c": 20, "r": 65, "e": 15},
        "cardPackEpic": {"c": 5, "r": 35, "e": 60},
    }.get(pack, {"c": 60, "r": 30, "e": 10})
    w = [weights[content.CARDS_BY_ID[i]["t"]] for i in pool]
    return random.choices(pool, weights=w, k=n)


def add_cards(s, ids):
    for cid in ids:
        e = s["cards"].setdefault(cid, {"amount": 0, "progress": 0, "unlocked": True})
        e["amount"] += 1


def grant(s, rtype, amount, ids=None):
    if rtype == "COINS":
        s["coins"] += amount
    elif rtype == "GEMS":
        s["gems"] += amount
    elif rtype == "HEARTS":
        s["hearts"] += amount
    elif rtype in s["consumables"]:
        s["consumables"][rtype] += amount or 1


# --------------------------------------------------------------------------- handlers
# Each handler: fn(store, body) -> dict.  Raise ApiError for failures.


def h_new_session(st, b):
    s = st.data
    r = full_state(s)
    r.update({
        "sessionToken": uuid.uuid4().hex,
        "accessToken": s["accessToken"],
        "newUser": bool(s.pop("newUser", False)),
        "settings": content.settings(),
        "customIdentifiers": s["customIds"],
        "pvpEnabled": False,
        "announcements": [],
        "testingGroup": "default",
    })
    st.save()
    return r


def h_level_start(st, b):
    s = st.data
    cards = [c for c in (b.get("chosenCards") or []) if c in s["cards"]]
    s["lastUsedCards"] = cards
    for u in b.get("usedConsumables") or []:
        n = u.get("consumableId")
        if n in s["consumables"]:
            s["consumables"][n] = max(0, s["consumables"][n] - int(u.get("amount", 1)))
    st.save()
    return full_state(s, cardsUsedFirstTime=[])


def h_level_end(st, b):
    s = st.data
    cfg = content.settings()["config"]
    distance = float(b.get("distance", 0) or 0)
    diamonds = int(b.get("diamonds", 0) or 0)
    gems = int(b.get("gems", 0) or 0)
    destruction = int(b.get("destruction", 0) or 0)
    score_extra = float(b.get("scoreExtra", 1) or 1)
    coin_extra = float(b.get("coinExtra", 1) or 1)
    distance_score = math.ceil(distance * cfg["distanceScoreMultiplier"])
    diamond_score = math.ceil(cfg["diamondScoreMultiplier"] * diamonds)
    score = int((distance_score + diamond_score + destruction) * score_extra)
    coins = math.ceil(diamonds / cfg["diamondsPerCoin"] * coin_extra)
    highscore = score > s["bestScore"]
    if highscore:
        s["bestScore"] = score
    s["bestDistance"] = max(s["bestDistance"], int(distance))
    s["totalDistance"] += int(distance)
    s["totalScore"] += score
    s["coins"] += coins
    s["gems"] += gems

    rewards = []
    m = s["mission"]
    progress = b.get("missionProgress")
    if isinstance(progress, (int, float)) and progress > m["index"]:
        for i in range(m["index"], int(progress)):
            t, a = content.mission_reward(i)
            grant(s, t, a)
            rewards.append({"rewardType": t, "rewardAmount": a})
        m["index"] = int(progress)
        if m["index"] >= m["packStart"] + content.MISSION_PACK_SIZE:
            m["packStart"] = m["index"]
    st.save()
    r = full_state(s)
    r["missionStatus"] = mission_status(s, rewards)
    r["got"] = {
        "distance": math.ceil(distance), "gems": gems, "coins": coins, "diamonds": diamonds,
        "destruction": destruction, "score": score, "destructionScore": destruction,
        "distanceScore": distance_score, "diamondScore": diamond_score, "highscore": highscore,
        "accumulatedDistance": s["totalDistance"], "accumulatedScore": s["totalScore"],
    }
    return r


def h_revive(st, b):
    spend(st.data, b.get("currencyUsed"))
    st.save()
    return full_state(st.data)


def h_card_upgrade(st, b):
    s = st.data
    cid = b.get("cardId")
    if cid not in s["cards"] or s["cards"][cid]["amount"] < 1:
        raise ApiError("noCard", "card not owned")
    if card_level(s, cid) >= content.MAX_LEVEL:
        raise ApiError("maxLevel", "card already at max level")
    spend(s, b.get("currencyUsed"))
    s["cards"][cid]["amount"] += 1
    st.save()
    return full_state(s)


def h_card_upgrade_full(st, b):
    s = st.data
    cid = b.get("cardId")
    if cid not in s["cards"] or s["cards"][cid]["amount"] < 1:
        raise ApiError("noCard", "card not owned")
    spend(s, b.get("currencyUsed"))
    s["cards"][cid]["amount"] = max(s["cards"][cid]["amount"], content.MAX_LEVEL)
    st.save()
    return full_state(s)


def h_card_sacrifice(st, b):
    s = st.data
    cid = b.get("cardId")
    if cid not in s["cards"]:
        raise ApiError("noCard", "unknown card")
    entry = s["cards"][cid]
    points = entry.get("progress", 0)
    for sid in b.get("sacrificedCardIds") or []:
        victim = s["cards"].get(sid)
        if not victim or victim["amount"] < 1:
            raise ApiError("noCard", "sacrificed card not owned")
        victim["amount"] -= 1
        points += content.SACRIFICE_VALUE.get(content.CARDS_BY_ID[sid]["t"], 1)
    ctype = content.CARDS_BY_ID[cid]["t"]
    while card_level(s, cid) < content.MAX_LEVEL:
        cost = content.upgrade_cost(card_level(s, cid), ctype)
        if cost < 0 or points < cost:
            break
        points -= cost
        entry["amount"] += 1
    entry["progress"] = points
    st.save()
    return full_state(s)


def h_card_evolve(st, b):
    s = st.data
    cid = b.get("cardId")
    cdef = content.CARDS_BY_ID.get(cid)
    if not cdef or not cdef.get("requires"):
        raise ApiError("badCard", "card cannot be evolved")
    if s["cards"][cid]["amount"] > 0:
        raise ApiError("owned", "already owned")
    for r in cdef["requires"]:
        if s["cards"][r]["amount"] < 5:
            raise ApiError("requirements", "requirements not met")
    s["cards"][cid]["amount"] = 1
    st.save()
    return full_state(s)


def h_card_equip(st, b):
    s = st.data
    cid = b.get("cardId", "")
    if cid and cid not in s["cards"]:
        raise ApiError("noCard", "unknown hero")
    s["equippedCard"] = cid
    st.save()
    return full_state(s)


def h_slot_unlock(st, b):
    s = st.data
    cid = b.get("cardId")
    if cid in s["cards"]:
        s["cards"][cid]["unlocked"] = True
    st.save()
    return full_state(s)


def h_slot_buy_unlock(st, b):
    s = st.data
    price, cur, _ = content.NON_IAP["cardSlotUnlock"]
    charge(s, price, cur)
    return h_slot_unlock(st, b)


def h_slot_keys(st, b):
    return {"keys": []}


def h_get_pack(st, b):
    s = st.data
    pack = b.get("cardPack")
    if pack not in content.NON_IAP or not pack.startswith("cardPack"):
        raise ApiError("badPack", "unknown pack " + str(pack))
    price, cur, n = content.NON_IAP[pack]
    charge(s, price, cur)
    new = draw_cards(pack, n)
    add_cards(s, new)
    st.save()
    return full_state(s, newCards=new)


def h_purchase_coins(st, b):
    s = st.data
    pack = b.get("coinPack")
    if pack not in content.NON_IAP or not pack.startswith("coins"):
        raise ApiError("badPack", "unknown coin pack")
    price, cur, amount = content.NON_IAP[pack]
    charge(s, price, cur)
    s["coins"] += amount
    st.save()
    return full_state(s)


def h_purchase_consumables(st, b):
    s = st.data
    pid = b.get("purchaseId")
    if pid not in content.CONSUMABLE_OF_PURCHASE:
        raise ApiError("badItem", "unknown consumable")
    price, cur, amount = content.NON_IAP[pid]
    charge(s, price, cur)
    s["consumables"][content.CONSUMABLE_OF_PURCHASE[pid]] += amount
    st.save()
    return full_state(s)


def h_customization_set(st, b):
    s = st.data
    for c in b.get("customizations") or []:
        part, skin = c.get("part"), c.get("skin")
        if part in content.CATEGORIES:
            s["selected"][part] = skin or ""
    st.save()
    return full_state(s)


def h_customization_purchase(st, b):
    s = st.data
    item = b.get("item")
    if item not in content.CUSTOMIZATION_BY_NAME:
        raise ApiError("badItem", "unknown item")
    if item not in s["owned_customization"]:
        spend(s, b.get("currencyUsed"))
        s["owned_customization"].append(item)
    st.save()
    return full_state(s)


def h_bonus_reset(st, b):
    spend(st.data, b.get("currencyUsed"))
    st.save()
    return full_state(st.data)


def h_ach_update(st, b):
    s = st.data
    for u in b.get("progressUpdateList") or []:
        n = u.get("achievementId")
        if n in content.ACHIEVEMENT_REWARD:
            a = s["achievements"].setdefault(n, {"left": 1, "claimed": False})
            if not a["claimed"]:
                a["left"] = int(u.get("progressToSet", a["left"]))
    st.save()
    return {"success": True}


def h_ach_claim(st, b):
    s = st.data
    for n in b.get("achievementsToClaim") or []:
        a = s["achievements"].get(n)
        if not a or a["left"] > 0 or a["claimed"]:
            raise ApiError("notClaimable", "achievement not claimable")
        t, amt = content.ACHIEVEMENT_REWARD[n]
        grant(s, t, amt)
        a["claimed"] = True
    st.save()
    return full_state(s)


def h_custom_ids(st, b):
    s = st.data
    for k, v in (b.get("customIdentifiers") or {}).items():
        s["customIds"][k] = int(v)
    st.save()
    return full_state(s, customIdentifiers=s["customIds"])


def h_tutorial_hearts(st, b):
    st.data["hearts"] = max(st.data["hearts"], content.HEARTS_DEFAULT)
    st.save()
    return full_state(st.data)


def h_tutorial_pack(st, b):
    new = draw_cards("cardPackCommon", 3)
    add_cards(st.data, new)
    st.save()
    return full_state(st.data, newCards=new)


def h_tutorial_consumable(st, b):
    n = b.get("consumableId")
    if n in st.data["consumables"]:
        st.data["consumables"][n] += 1
    st.save()
    return full_state(st.data)


def h_state_only(st, b):
    return full_state(st.data)


def h_ok(st, b):
    return {"success": True}


def h_iap_query(st, b):
    s = st.data
    name = b.get("name")
    if name in ("MonthCard",):
        s["monthCard"] = {"have": True}
    elif name in content.IAP and name.startswith("gem"):
        s["gems"] += content.IAP[name][1]
    elif name in content.IAP and name.startswith("energy"):
        s["hearts"] += content.IAP[name][1]
    else:
        raise ApiError("badItem", "unknown iap " + str(name))
    st.save()
    return full_state(s, count=int(b.get("count", 0) or 0))


def h_iap_card_pack(st, b):
    new = draw_cards(b.get("name", ""), 3)
    add_cards(st.data, new)
    st.save()
    return full_state(st.data, newCards=new)


def h_iap_offer_bundle(st, b):
    s = st.data
    defs = {o["name"]: o["rewards"] for o in content.settings()["bundleOffers"]}
    rewards = defs.get(b.get("name"), [])
    out = []
    for r in rewards:
        grant(s, r["Type"], r["Amount"])
        out.append({"rewardType": r["Type"], "rewardAmount": r["Amount"]})
    st.save()
    return full_state(s, rewards=out)


def h_messages_get(st, b):
    return {"messages": []}


def h_op_events(st, b):
    return {"events": []}


def h_checkin_get(st, b):
    c = st.data["checkin"]
    return {"chainCount": c["chain"], "todayChecked": c["last"] == today()}


def h_checkin_claim(st, b):
    s = st.data
    c = s["checkin"]
    if c["last"] == today():
        raise ApiError("alreadyChecked", "already checked in today")
    rewards = [r for g in content.settings()["eventGroups"] for r in g["events"][0]["eventRewards"]]
    r = rewards[c["chain"] % len(rewards)]
    grant(s, r["Type"], r["Amount"])
    c["chain"] += 1
    c["last"] = today()
    st.save()
    return full_state(s)


def h_month_card_get(st, b):
    return {"haveMonthCard": st.data["monthCard"]["have"], "todayClaimed": True, "leftDays": 30 if st.data["monthCard"]["have"] else 0}


def h_ranking_top(st, b):
    s = st.data
    me = {"userId": s["user"], "name": "Monsu", "highScore": s["bestScore"], "group": 0, "index": 1}
    filler = {"userId": "end", "name": "", "highScore": 0, "group": 0, "index": 2}
    return full_state(s, topPlayers={"userOrder": 1, "list": [me, filler]})


def h_user_meta(st, b):
    return {"donePurchases": False}


def h_time(st, b):
    return {"time": now_str()}


def h_pvp_unavailable(st, b):
    raise ApiError("pvpUnavailable", "PvP is not available offline")


ROUTES = {
    "newSession": h_new_session,
    "newSessionSdk": h_new_session,
    "level/start": h_level_start,
    "level/end": h_level_end,
    "revive": h_revive,
    "card/upgrade": h_card_upgrade,
    "card/upgrade/full": h_card_upgrade_full,
    "card/sacrifice": h_card_sacrifice,
    "card/evolve": h_card_evolve,
    "card/equip": h_card_equip,
    "card/getPack": h_get_pack,
    "card/slot/unlock": h_slot_unlock,
    "card/slot/buyUnlock": h_slot_buy_unlock,
    "card/slot/keys": h_slot_keys,
    "purchaseCoins": h_purchase_coins,
    "purchaseconsumables": h_purchase_consumables,
    "customization/set": h_customization_set,
    "customization/purchase": h_customization_purchase,
    "mission/bonusReset": h_bonus_reset,
    "achievement/updateProgress": h_ach_update,
    "achievement/claim": h_ach_claim,
    "customIds/add": h_custom_ids,
    "tutorial/heartRefill": h_tutorial_hearts,
    "tutorial/luckyPack": h_tutorial_pack,
    "tutorial/consumable": h_tutorial_consumable,
    "tutorial/upgradeCard": h_state_only,
    "tutorial/legendarySlot": h_state_only,
    "iap/query": h_iap_query,
    "iap/cardPack": h_iap_card_pack,
    "iap/offerBundle": h_iap_offer_bundle,
    "messages/get": h_messages_get,
    "messages/claim": h_state_only,
    "opevent/get": h_op_events,
    "opevent/getcheckin": h_checkin_get,
    "opevent/claimcheckin": h_checkin_claim,
    "opevent/getmonthcard": h_month_card_get,
    "opevent/claimmonthcard": h_state_only,
    "opevent/claim": h_state_only,
    "ranking/top": h_ranking_top,
    "userMetaProfile/get": h_user_meta,
    "up": h_ok,
    "gamestate": h_ok,
    "bundleshown": h_ok,
    "gamecenter/name": h_ok,
    "GetTime.ashx": h_time,
    "time": h_time,
    "arena/get": h_pvp_unavailable,
    "arena/status": h_pvp_unavailable,
    "arena/entry": h_pvp_unavailable,
    "arena/match": h_pvp_unavailable,
    "arena/breathe": h_pvp_unavailable,
    "arena/checkResult": h_pvp_unavailable,
    "arena/claim": h_pvp_unavailable,
}


def dispatch(store, path, body):
    """Run one API call.  Returns the response dict (errors turned into the client's error shape)."""
    with LOCK:
        fn = ROUTES.get(path)
        if fn is None:
            return {"success": True}, True
        try:
            return fn(store, body), False
        except ApiError as e:
            return {"error": {"code": e.code, "message": e.message}}, False


# --------------------------------------------------------------------------- http


def log(line):
    os.makedirs(os.path.dirname(LOG_PATH), exist_ok=True)
    with open(LOG_PATH, "a", encoding="utf-8") as f:
        f.write(line + "\n")


class Handler(BaseHTTPRequestHandler):
    store = None
    protocol_version = "HTTP/1.1"

    def log_message(self, fmt, *args):  # silence default stderr logging
        pass

    def _handle(self):
        length = int(self.headers.get("Content-Length") or 0)
        raw = self.rfile.read(length) if length else b""
        path = self.path.split("?", 1)[0]
        if path.startswith("/api/"):
            path = path[5:]
        path = path.strip("/")
        try:
            body = json.loads(raw.decode("utf-8")) if raw else {}
            if not isinstance(body, dict):
                body = {}
        except ValueError:
            body = {}
        resp, unhandled = dispatch(self.store, path, body)
        out = json.dumps(resp).encode("utf-8")
        stamp = datetime.now().strftime("%H:%M:%S")
        log("%s %s%s\n  REQ  %s\n  RESP %s" % (stamp, "UNHANDLED " if unhandled else "", path,
                                              raw.decode("utf-8", "replace"), out.decode("utf-8")))
        self.send_response(200)
        self.send_header("Content-Type", "application/vnd.boomlagoon.monsu+json")
        self.send_header("Content-Length", str(len(out)))
        self.end_headers()
        self.wfile.write(out)

    do_POST = _handle
    do_GET = _handle


def make_server(host, port, store):
    Handler.store = store
    return ThreadingHTTPServer((host, port), Handler)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--host", default="0.0.0.0")
    ap.add_argument("--port", type=int, default=8083)
    ap.add_argument("--reset", action="store_true", help="throw away the save file and start fresh")
    args = ap.parse_args()
    store = Store()
    if args.reset:
        store.reset()
    srv = make_server(args.host, args.port, store)
    print("Monsu offline server on %s:%d  (log: %s)" % (args.host, args.port, LOG_PATH))
    try:
        srv.serve_forever()
    except KeyboardInterrupt:
        pass


if __name__ == "__main__":
    main()
