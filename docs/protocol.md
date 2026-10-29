# 协议说明（从反编译的 Assembly-CSharp.dll 得出）

## 四个关键问题

1. **响应信封**：没有信封，响应就是一个 JSON 对象。**只要对象里有 `error` 键就算失败**
   （`NetworkRequestHandler.CheckForSuccess`），`{"error":{"code":"...","message":"..."}}`。
   特殊 code：`version`（强更）、`maintenance`（维护）、`sdkVerifyFailed`、`fbMismatch`/`fbTokenInvalid`。
   其他任何响应都算成功。响应为空串时客户端当作 `{}`。
2. **`MessageEncrypter`**：反编译结果里不存在这个类，请求没有签名，客户端也不校验响应。
3. **卡牌名称/描述**：全部在客户端（`CardDefinitions` 静态表 + `Boomlagoon.Localization.ChineseSimplified`），
   **服务器只下发数值**（价格、升级花费、奖励、任务）和玩家存档。卡牌的 54 个 ID 写死在客户端。
4. **启动流程**：`Initializer.Start` → `VkSdk.InitSDK` → `GET up`（联网检测）→ `VkSdk.Login`（成功后）→
   `newSessionSdk`（`UseOriginLogin=false` 时）→ 进入 `main_menu`。补丁后 `InitSDK`/`Login` 直接回调成功。

## 请求

`POST {SERVER_URL}{path}`，`Content-Type: application/vnd.boomlagoon.monsu+json`，正文为 JSON。
除 `newSession*` 外，正文都有 `sessionToken`。`SERVER_URL` 默认 `http://127.0.0.1:8083/api/`。
联网检测是 `GET up`，任意 2xx 即可。

## 通用“完整状态”

几乎所有修改存档的接口都返回下面这组字段，客户端会用它**整体替换**本地状态：

| 键 | 内容 |
|---|---|
| `user` | 用户 id |
| `gameState` | `GEMS` `COINS` `hearts` `heartTime`(UTC `yyyy-MM-dd HH:mm:ss.fff`) `score` `serverTime` `equippedCard` `lastUsedCards[]` `arms/head/body/feet`(当前穿戴) |
| `cards` | `[{id, amount(=等级/拥有数), unlocked(卡槽), upgradeProgress}]`，**必须列出全部卡**，否则缺的卡会被清零 |
| `customizationItems` | `{物品名: true}` 已拥有 |
| `ownedConsumables` | `[{consumableId, amount}]` |
| `achievements` | `[{achievement_name, progress(=剩余数量), claimed}]` |
| `missionStatus` | `currentMissions[]` `missionIndex` `packStartIndex` `buffs[]` `consecutiveBonusMissions` `reward[]` |

## 接口

| 路径 | 请求字段 | 响应 |
|---|---|---|
| `newSession` / `newSessionSdk` | `userData`, `accessToken?` / `channelId,userName,userId,token` | 完整状态 + `sessionToken` `accessToken` `newUser` `settings` `customIdentifiers{}` `pvpEnabled` `announcements[]` |
| `level/start` | `chosenCards[]` `usedConsumables[{consumableId,amount}]` `gameMode` | 完整状态 + `cardsUsedFirstTime[]` |
| `level/end` | `distance` `diamonds` `gems` `destruction` `scoreExtra` `coinExtra` `missionProgress` `usedCards[]` … | 完整状态 + `got{distance,gems,coins,diamonds,destruction,score,destructionScore,distanceScore,diamondScore,highscore,accumulatedDistance,accumulatedScore}` |
| `revive` | `reviveCount` `currencyUsed{COINS,GEMS}` | 完整状态 |
| `card/upgrade` `card/upgrade/full` | `cardId` `currencyUsed` | 完整状态 |
| `card/sacrifice` | `cardId` `sacrificedCardIds[]`（可重复） | 完整状态 |
| `card/evolve` | `cardId` | 完整状态 |
| `card/equip` | `cardId`（英雄 id，空串=默认 ogre） | 完整状态 |
| `card/getPack` | `cardPack`(`cardPackCommon/Rare/Epic`) | 完整状态 + `newCards[]` |
| `card/slot/unlock` `card/slot/buyUnlock` | `cardId` | 完整状态 |
| `card/slot/keys` | – | `{keys:[]}` |
| `purchaseCoins` | `coinPack` | 完整状态 |
| `purchaseconsumables` | `purchaseId` | 完整状态 |
| `customization/set` | `customizations[{part,skin}]` | 完整状态 |
| `customization/purchase` | `item` `currencyUsed` | 完整状态 |
| `mission/bonusReset` | `currencyUsed` | 完整状态 |
| `achievement/updateProgress` | `progressUpdateList[{achievementId,progressToSet(剩余)}]` | 任意 |
| `achievement/claim` | `achievementsToClaim[]` | 完整状态 |
| `tutorial/*` (5 个) | 见源码 | 完整状态（`luckyPack` 另带 `newCards`） |
| `customIds/add` | `customIdentifiers{}` | 完整状态 |
| `iap/query` | `name` `receipt` `transaction` `count` | 完整状态 + `count`（含 `gameState` 才算成功） |
| `iap/cardPack` | 同上 | 完整状态 + `newCards` |
| `iap/offerBundle` | 同上 | 完整状态 + `rewards[{rewardType,rewardAmount}]` |
| `opevent/get` | `userId` | `{events:[]}` |
| `opevent/getcheckin` `opevent/claimcheckin` | `userId` | `{chainCount,todayChecked}` / 完整状态 |
| `opevent/getmonthcard` | `userId` | `{haveMonthCard,todayClaimed,leftDays}` |
| `messages/get` | – | `{messages:[]}` |
| `ranking/top` | `rankMode` | 完整状态 + `topPlayers{userOrder,list[]}`（客户端会删掉 list 最后一项） |
| `userMetaProfile/get` | – | `{donePurchases}` |
| `gamestate` | 离线本地存档同步 | 任意 |
| `arena/*` | PvP | 返回 `error pvpUnavailable`；`pvpEnabled:false` 时入口不显示 |

未实现的路径一律返回 `{"success":true}`，并在日志里标 `UNHANDLED`。

## `settings` 对象

由 `GameConfigurationData.LoadDataFromJson` 解析，**缺任何一个必需键客户端都会空引用**：
`config`（13 个数值，见 `server/content.py`）、`prices`、`iap`、`purchaseDefs`、`bonusRestorePrice`、`customization`、
`cards`（`[{id,type}]`，type 为 `c r e u l t j h x`）、`revive`、`cardUpgrades`（每级 6 种稀有度花费，条数+1 = 等级上限）、
`cardTypeUpgradeValues`、`rewards`（成就奖励，`rewardName` = 成就枚举名）、`missionRewards`。
可选：`eventGroups`（group 5 = 签到）、`bundleOffers`、`honorGoods`、`rankTopRewards`、`rankGroups`。

## 任务

`missionStatus.currentMissions[i]`：`logic` ∈ `event|score|distance`，`localizationId` 是 `LocalizedString` 枚举名
（如 `MissionRunDistance`），`event` 是 `GameEvent` 枚举名。任务编号 = `packStartIndex + i`。
`level/end` 带的 `missionProgress` 是已完成任务数，服务器据此发奖励（`missionStatus.reward`）。

## 教程

`customIdentifiers` 里出现的键代表该教程序列已完成（键为 C# 类型名）。服务器默认全部标记完成。
