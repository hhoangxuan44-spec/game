#!/usr/bin/env python3
"""Differential test: replay one scenario through server/server.py and through the C# embedded server, compare.

usage: python3 embedded/tests/diff_test.py      (needs the dotnet SDK)
"""
import json
import os
import subprocess
import sys
import tempfile

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.join(HERE, "..", "..", "server"))
import server  # noqa: E402

VOLATILE = {"user", "accessToken", "sessionToken", "heartTime", "serverTime", "userId"}

SCENARIO = [
    ("newSessionSdk", {"userData": {}}),
    ("newSession", {}),
    ("up", {}),
    ("level/start", {"chosenCards": ["1", "2", "nope"], "gameMode": 1}),
    ("level/end", {"distance": 1234.5, "diamonds": 50, "gems": 2, "destruction": 5, "scoreExtra": 1.1, "coinExtra": 1, "missionProgress": 0}),
    ("level/end", {"distance": 10, "diamonds": 0, "gems": 0, "destruction": 0, "missionProgress": 3}),
    ("level/end", {"distance": 10, "missionProgress": 10}),
    ("level/end", {"distance": 10, "missionProgress": 12}),
    ("revive", {"reviveCount": 0, "currencyUsed": {"GEMS": 1}}),
    ("revive", {"reviveCount": 0, "currencyUsed": {"GEMS": 100000}}),
    ("card/upgrade", {"cardId": "1", "currencyUsed": {"COINS": 100}}),
    ("card/upgrade", {"cardId": "1", "currencyUsed": {"COINS": 100}}),
    ("card/upgrade", {"cardId": "41", "currencyUsed": {"COINS": 100}}),
    ("card/upgrade/full", {"cardId": "2", "currencyUsed": {"COINS": 1000}}),
    ("card/evolve", {"cardId": "41"}),
    ("card/evolve", {"cardId": "41"}),
    ("card/evolve", {"cardId": "1"}),
    ("card/sacrifice", {"cardId": "41", "sacrificedCardIds": ["1", "1", "3", "3", "4", "4", "13"]}),
    ("card/sacrifice", {"cardId": "41", "sacrificedCardIds": ["50"]}),
    ("card/equip", {"cardId": "1003"}),
    ("card/equip", {"cardId": "zzz"}),
    ("card/slot/unlock", {"cardId": "41"}),
    ("card/slot/buyUnlock", {"cardId": "41"}),
    ("card/slot/keys", {}),
    ("purchaseCoins", {"coinPack": "coinsSmall"}),
    ("purchaseCoins", {"coinPack": "cardPackEpic"}),
    ("purchaseconsumables", {"purchaseId": "cardBoostConsumable1"}),
    ("purchaseconsumables", {"purchaseId": "cardPackEpic"}),
    ("tutorial/consumable", {"consumableId": "magnetBoostConsumable"}),
    ("level/start", {"chosenCards": [], "usedConsumables": [{"consumableId": "cardBoostConsumable", "amount": 2}]}),
    ("customization/set", {"customizations": [{"part": "head", "skin": "hat_knight"}, {"part": "tail", "skin": "x"}]}),
    ("customization/purchase", {"item": "hat_knight", "currencyUsed": {"COINS": 10}}),
    ("customization/purchase", {"item": "nope"}),
    ("mission/bonusReset", {"currencyUsed": {"GEMS": 5}}),
    ("achievement/updateProgress", {"progressUpdateList": [{"achievementId": "AchievementPlayRounds", "progressToSet": 3}, {"achievementId": "bogus", "progressToSet": 0}]}),
    ("achievement/claim", {"achievementsToClaim": ["AchievementPlayRounds"]}),
    ("achievement/updateProgress", {"progressUpdateList": [{"achievementId": "AchievementPlayRounds", "progressToSet": 0}]}),
    ("achievement/claim", {"achievementsToClaim": ["AchievementPlayRounds"]}),
    ("achievement/claim", {"achievementsToClaim": ["AchievementPlayRounds"]}),
    ("customIds/add", {"customIdentifiers": {"x": 1, "TutorialSequenceBuyCards": 2}}),
    ("tutorial/heartRefill", {}),
    ("tutorial/upgradeCard", {"cardId": "1"}),
    ("tutorial/legendarySlot", {}),
    ("iap/query", {"name": "gemSmall", "receipt": "r", "transaction": "t", "count": 2}),
    ("iap/query", {"name": "energyBig", "count": 0}),
    ("iap/query", {"name": "MonthCard", "count": 0}),
    ("iap/query", {"name": "offerBundle1", "count": 0}),
    ("iap/offerBundle", {"name": "offerBundle2"}),
    ("iap/offerBundle", {"name": "zzz"}),
    ("messages/get", {}),
    ("messages/claim", {"messagesToClaim": []}),
    ("opevent/get", {"userId": "u"}),
    ("opevent/getcheckin", {"userId": "u"}),
    ("opevent/claimcheckin", {"userId": "u"}),
    ("opevent/claimcheckin", {"userId": "u"}),
    ("opevent/getcheckin", {"userId": "u"}),
    ("opevent/getmonthcard", {"userId": "u"}),
    ("opevent/claimmonthcard", {"userId": "u"}),
    ("ranking/top", {"rankMode": "x"}),
    ("userMetaProfile/get", {}),
    ("gamestate", {}),
    ("bundleshown", {"bundleId": "offerBundle1"}),
    ("arena/get", {}),
    ("arena/entry", {}),
    ("totally/unknown", {"a": 1}),
]
# random pack contents differ between the two implementations, so only the structure is compared for these
RANDOM = [
    ("card/getPack", {"cardPack": "cardPackCommon"}),
    ("card/getPack", {"cardPack": "bogus"}),
    ("tutorial/luckyPack", {}),
    ("iap/cardPack", {"name": "cardPackRare"}),
]


def norm(v):
    if isinstance(v, dict):
        return {k: ("<v>" if k in VOLATILE else norm(x)) for k, x in sorted(v.items())}
    if isinstance(v, list):
        return [norm(x) for x in v]
    if isinstance(v, float) and v == int(v):
        return int(v)
    return v


def compare(label, py, cs):
    steps = SCENARIO + RANDOM
    if len(cs) != len(py):
        print("line count differs", len(cs), len(py))
        return 1
    bad = 0
    for i, (a, b) in enumerate(zip(py, cs)):
        ua, ub = a.startswith("UNHANDLED "), b.startswith("UNHANDLED ")
        ja, jb = norm(json.loads(a.replace("UNHANDLED ", "", 1))), norm(json.loads(b.replace("UNHANDLED ", "", 1)))
        if i >= len(SCENARIO):
            # structure only: same keys / same error-ness
            same = ua == ub and set(ja) == set(jb)
        else:
            same = ua == ub and ja == jb
        if not same:
            bad += 1
            print("MISMATCH at step %d %s" % (i, steps[i][0]))
            if isinstance(ja, dict) and isinstance(jb, dict):
                for k in sorted(set(ja) | set(jb)):
                    if ja.get(k) != jb.get(k):
                        print("  key", k, "\n   py:", json.dumps(ja.get(k))[:300], "\n   cs:", json.dumps(jb.get(k))[:300])
    print("%s: steps %d, mismatches %d" % (label, len(py), bad))
    return 1 if bad else 0


def main():
    tmp = tempfile.mkdtemp()
    scen = os.path.join(tmp, "scenario.json")
    with open(scen, "w") as f:
        json.dump([{"path": p, "body": b} for p, b in SCENARIO + RANDOM], f)

    store = server.Store(os.path.join(tmp, "py_save.json"))
    py = []
    for p, b in SCENARIO + RANDOM:
        r, un = server.dispatch(store, p, b)
        py.append(("UNHANDLED " if un else "") + json.dumps(r))

    env = dict(os.environ, DOTNET_ROLL_FORWARD="Major", DOTNET_CLI_TELEMETRY_OPTOUT="1")
    out = subprocess.run(["dotnet", "run", "-c", "Release", "--project", HERE, "--", scen],
                         capture_output=True, text=True, env=env)
    if out.returncode != 0:
        print(out.stdout, out.stderr)
        return 2
    cs = [l for l in out.stdout.splitlines() if l.strip()]
    merged = os.environ.get("MERGED_DLL")        # optional: also run the merged Assembly-CSharp.dll on Mono
    if merged:
        d = os.path.join(tmp, "merged")
        os.makedirs(d)
        import shutil
        shutil.copy(merged, os.path.join(d, "Assembly-CSharp.dll"))
        for extra in os.environ.get("MANAGED_DIR", "").split(os.pathsep):
            if extra:
                for f in os.listdir(extra):
                    if f.endswith(".dll") and not f.startswith(("Assembly-CSharp", "mscorlib", "System")):
                        shutil.copy(os.path.join(extra, f), d)
        exe = os.path.join(d, "MonoReplay.exe")
        subprocess.run(["mcs", "-out:" + exe, "-r:" + os.path.join(d, "Assembly-CSharp.dll"), os.path.join(HERE, "MonoReplay.cs")], check=True)
        mono = subprocess.run(["mono", exe, scen], capture_output=True, text=True,
                              env=dict(os.environ, MONO_PATH=d))
        if mono.returncode != 0:
            print(mono.stdout, mono.stderr)
            return 2
        ml = [l for l in mono.stdout.splitlines() if l.strip().startswith(("{", "UNHANDLED"))]
        rc = compare("mono(merged dll)", py, ml)
        if rc:
            return rc
    rc = compare("dotnet", py, cs)
    return rc


if __name__ == "__main__":
    sys.exit(main())
