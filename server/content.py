"""Static game content: everything the real server used to send in the `settings` object.

The client already contains the card definitions, names and descriptions
(CardDefinitions.cs / Localization), so the server only has to provide
numbers: prices, upgrade costs, rewards, missions.  Values are made up --
the original numbers are lost -- and are deliberately generous.
"""
import json
import os

DATA_DIR = os.path.join(os.path.dirname(os.path.abspath(__file__)), "data")


def _load(name):
    with open(os.path.join(DATA_DIR, name), encoding="utf-8") as f:
        return json.load(f)


CARDS = _load("cards.json")
CARDS_BY_ID = {c["id"]: c for c in CARDS}
CUSTOMIZATION = _load("customization.json")
CUSTOMIZATION_BY_NAME = {c["name"]: c for c in CUSTOMIZATION}
CATEGORIES = ["arms", "head", "body", "feet"]

# Card types: c common, r rare, e epic, u useless, l legendary, t ultimate,
# j jugg hero, h hero, x fire hero.
PACKABLE_TYPES = ("c", "r", "e")
# Cards that can only be created by evolving (merge) other cards.
EVOLVE_TARGETS = [c["id"] for c in CARDS if c["kind"] != "CardDefinition" and c["t"] in ("l", "t", "x")]

CONSUMABLES = [
    "cardBoostConsumable",
    "missionBoostConsumable",
    "magnetBoostConsumable",
    "dashBoostConsumable",
    "powerupBoostConsumable",
]

MAX_LEVEL = 10
HEARTS_DEFAULT = 99

# --- prices -----------------------------------------------------------------
# name -> (price, currency, amount)   (non-IAP purchases)
NON_IAP = {
    "coinsSmall": (20, "GEMS", 2000),
    "coinsMedium": (50, "GEMS", 6000),
    "coinsBig": (100, "GEMS", 14000),
    "coinsHuge": (200, "GEMS", 32000),
    "cardPackCommon": (1000, "COINS", 3),
    "cardPackRare": (30, "GEMS", 3),
    "cardPackEpic": (80, "GEMS", 3),
    "cardSlotUnlock": (20, "GEMS", 1),
    "cardBoostConsumable1": (500, "COINS", 1),
    "missionBoostConsumable1": (500, "COINS", 1),
    "magnetBoostConsumable1": (500, "COINS", 1),
    "dashBoostConsumable1": (500, "COINS", 1),
    "powerupBoostConsumable1": (500, "COINS", 1),
}

# name -> (price string, amount)   (IAP purchases; granted for free by the local server)
IAP = {
    "gemSmall": ("6", 60),
    "gemMedium": ("30", 330),
    "gemBig": ("98", 1180),
    "gemHuge": ("198", 2500),
    "gemTremen": ("328", 4400),
    "gemMax": ("648", 9000),
    "energySmall": ("6", 20),
    "energyMedium": ("12", 50),
    "energyBig": ("30", 150),
    "energyHuge": ("60", 400),
    "offerBundle1": ("6", 1),
    "offerBundle2": ("30", 1),
    "MonthCard": ("30", 1),
}

CONSUMABLE_OF_PURCHASE = {n: n[:-1] for n in NON_IAP if n.endswith("Consumable1")}

# upgrade cost to go from level N to N+1 (index N-1), per card type
_BASE = [100, 200, 400, 800, 1600, 3200, 6400, 12800, 25600]


def card_upgrades():
    rows = []
    for base in _BASE:
        rows.append({
            "common": base,
            "rare": base * 2,
            "epic": base * 3,
            "legendary": base * 4,
            "ultimate": base * 5,
            "exclusive": base * 5,
        })
    return rows


TYPE_NAME = {"c": "common", "r": "rare", "e": "epic", "l": "legendary", "t": "ultimate", "x": "exclusive"}
SACRIFICE_VALUE = {"c": 1, "r": 2, "e": 3, "u": 1, "l": 5, "t": 8}


def upgrade_cost(level, ctype):
    """Cost (in coins) to raise a card from `level` to `level+1`; -1 when impossible."""
    rows = card_upgrades()
    if level < 1 or level > len(rows) or ctype not in TYPE_NAME:
        return -1
    return rows[level - 1][TYPE_NAME[ctype]]


def achievement_names():
    return [
        "AchievementMoneyFlies", "AchievementHalfMarathon", "AchievementFullMarathon",
        "AchievementAroundTheWorld", "AchievementWinterIsComing", "AchievementWardrobeMalfunction",
        "AchievementCardmonger", "AchievementGottaGetThemAll", "AchievementHeartbreaker",
        "AchievementGifted", "AchievementRareSpecimen", "AchievementPureEpic",
        "AchievementThanks", "AchievementPlayRounds",
    ]


ACHIEVEMENT_REWARD = {n: ("GEMS", 10 + 5 * i) for i, n in enumerate(achievement_names())}

# Tutorial / one-time sequences; the id is the C# type name.  Marking them done skips the tutorials.
TUTORIAL_SEQUENCES = [
    "AchievementTutorialSequence", "CardPackReminderSequence", "CustomizationItemsTutorial",
    "FacebookPromptSequence", "FirstRunDoneResultSequence", "IngameTutorialSequence",
    "IntroduceWeeklyTournamentSequence", "NotEnoughGemsSequence", "NotEnoughHeartsSequence",
    "OpenWeeklyTournamentCardRewardsTutorialSequence", "ShowAppiraterSequence",
    "TutorialSequenceBuyCards", "TutorialSequenceConsumables", "TutorialSequenceEquipCard",
    "TutorialSequenceEvolvePossible", "TutorialSequenceFirstBonusMissionBegin",
    "TutorialSequenceFirstBonusMissionCompleted", "TutorialSequenceIntroduceEvolve",
    "TutorialSequenceUpgradeCard", "TutorialSequenceUpgradeEvolveCard",
    "TutorialSequenceUpgradeResult", "WeeklyMissionTutorialStart",
]

# --- missions -----------------------------------------------------------------
MISSION_PACK_SIZE = 10

# (logic, localizationId, event name or None, base amount, step)
_MISSION_KINDS = [
    ("distance", "MissionRunDistance", None, 600, 150),
    ("event", "MissionDestroyBuildings", "BuildingKilled", 8, 2),
    ("score", "MissionScorePoints", None, 3000, 1000),
    ("event", "MissionStompEnemies", "StompableKilled", 6, 2),
    ("event", "MissionCollectPowerups", "PowerupCollected", 3, 1),
    ("distance", "MissionRunDistance", None, 900, 200),
    ("event", "MissionDoubleJump", "PlayerJumpedTwice", 5, 2),
    ("event", "MissionEnterFever", "FeverStartedEvent", 1, 1),
    ("event", "MissionMagnetPowerup", "MagnetPowerupCollected", 2, 1),
    ("score", "MissionScorePoints", None, 5000, 1500),
]


def mission(index):
    logic, loc, event, base, step = _MISSION_KINDS[index % len(_MISSION_KINDS)]
    tier = index // len(_MISSION_KINDS)
    m = {
        "amount": base + step * tier,
        "localizationId": loc,
        "logic": logic,
        "isBonus": False,
        "difficulty": min(1 + index // 5, 10),
    }
    if event:
        m["event"] = event
    return m


def mission_reward(index):
    return ("COINS", 200 + 50 * index) if index % 5 else ("GEMS", 5 + index // 5)


def settings():
    prices = [{"name": n, "price": p, "currency": c} for n, (p, c, _a) in NON_IAP.items()]
    iap = [{"name": n, "apple": n, "google": n, "ch": n, "price": p} for n, (p, _a) in IAP.items()]
    purchase_defs = [{"name": n, "amount": a} for n, (_p, _c, a) in NON_IAP.items()]
    purchase_defs += [{"name": n, "amount": a} for n, (_p, a) in IAP.items()]
    rewards = [{"rewardName": n, "rewardType": t, "rewardAmount": a} for n, (t, a) in ACHIEVEMENT_REWARD.items()]
    rewards += [{"rewardName": "weeklyTournament%d" % i, "rewardType": "COINS", "rewardAmount": 1000 * (4 - i)}
                for i in (1, 2, 3)]
    sign_rewards = [{"Type": "COINS", "Id": "", "Amount": 500 * (i + 1)} for i in range(6)]
    sign_rewards.append({"Type": "GEMS", "Id": "", "Amount": 30})
    return {
        "config": {
            "heartRegenerationTimeInSeconds": 300,
            "diamondsPerCoin": 1,
            "diamondScoreMultiplier": 10,
            "distanceScoreMultiplier": 1,
            "coinsPerGem": 10,
            "giftRequestTimeoutMinutes": 60,
            "maxHeartsGenerated": 5,
            "coinsPerMatch": 100,
            "gemsPerMatch": 5,
            "honorWinCoinMatch": 50,
            "honorLosCoinMatch": 15,
            "honorWinGemMatch": 50,
            "honorLosGemMatch": 15,
        },
        "prices": prices,
        "iap": iap,
        "purchaseDefs": purchase_defs,
        "bonusRestorePrice": [{"BonusMissions": 0, "Price": 5, "Currency": "GEMS"}],
        "customization": [{"name": c["name"], "price": c["price"], "currency": c["currency"]} for c in CUSTOMIZATION],
        "cards": [{"id": c["id"], "type": c["t"]} for c in CARDS],
        "revive": [{"currency": "GEMS", "amount": a} for a in (1, 2, 3, 5, 8)],
        "cardUpgrades": card_upgrades(),
        "cardTypeUpgradeValues": [{"type": t, "value": v} for t, v in SACRIFICE_VALUE.items()],
        "rewards": rewards,
        "missionRewards": {"bonus": [{"Type": "GEMS"} for _ in range(10)]},
        "rankTopRewards": [],
        "rankGroups": [],
        "honorGoods": [],
        "bundleOffers": [
            {"name": "offerBundle1", "rewards": [{"Type": "GEMS", "Amount": 100}, {"Type": "COINS", "Amount": 5000}]},
            {"name": "offerBundle2", "rewards": [{"Type": "GEMS", "Amount": 500}, {"Type": "COINS", "Amount": 30000}]},
        ],
        "eventGroups": [
            {
                "group": 5, "active": True, "startTime": "2000-01-01 00:00:00", "endTime": "2099-01-01 00:00:00",
                "refreshTime": "00:00:00", "icon": "", "name": "checkin", "t_localizationId": "", "d_localizationId": "",
                "stamp": "", "show": True,
                "events": [{"eventId": 5001, "type": "checkin", "value": 0, "eventRewards": sign_rewards}],
            }
        ],
    }
