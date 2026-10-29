# 卡牌萌兽（Monsu）离线复活：任务说明

> 给 AI 编码代理（Jules 等）读的任务文档。人类读者请先看本文第 1 节和第 10 节。
> 本项目仅供仓库所有者个人离线游玩。**仓库保持私有，不发布、不分发任何 APK 或游戏资源。**

---

## 1. 目标

《卡牌萌兽》（包名 `com.jianyou.mengshou.uc`）已停服，启动后因连不上服务器而无法进入游戏。目标是：

1. **改客户端**：把服务器地址指向本地，并跳过已失效的九游（UC / 方寸）SDK 登录。
2. **写假服务器**：用 Python 实现客户端需要的 HTTP 接口，让游戏能进入主菜单、跑酷、拿卡、升级、换装。
3. **最终形态**：假服务器运行在**同一台安卓手机的 Termux 里**（`127.0.0.1:8083`），游戏完全离线可玩。

**明确不做**：PvP / 竞技场、排行榜、好友 / Facebook / GameCenter 社交、真实支付。这些接口只需返回"成功"或空数据，保证客户端不报错。

**数值不重要**：仓库所有者明确表示卡牌属性、爆率无所谓。卡牌数值随便填、开局直接送全部卡都可以。优先级是"能玩"，不是"还原"。

---

## 2. 已知事实（前期分析结果，可直接采信）

| 项目 | 结论 |
|---|---|
| 原版游戏 | 芬兰 Boomlagoon 的跑酷 + 卡牌游戏 **Monsu**，国内由 17waa 代理，走 UC 九游渠道 |
| 引擎 | **Unity 4.6.7f1**，脚本后端 **Mono**（不是 IL2CPP） |
| 游戏代码 | `assets/bin/Data/Managed/Assembly-CSharp.dll`（约 1.7MB，**未混淆**）：1469 个类型，约 13900 个方法 |
| 原生库 | **只有 `armeabi-v7a` 和 `x86`**，没有 arm64，见第 9 节风险 1 |
| 战斗逻辑 | 在客户端：跑酷、碰撞、加成卡效果（`*Booster` 类）都在 C# 里 |
| 服务器 | `NetworkingProperties.get_SERVER_URL` 按地区或渠道选择，国服为 `http://monsuserver.chinacloudapp.cn:8083/api/`（另有 `monsuandroid.chinacloudapp.cn`、KR、TW 等），**明文 HTTP** |
| 请求格式 | POST JSON，`Content-Type: application/vnd.boomlagoon.monsu+json`，由 `HttpRequestDispatcher` 发送，请求体由 `AbstractServiceRequest.CreateRequestBase` / `CreateUserData` 构造 |
| 签名 | 存在 `MessageEncrypter`（字段 `secretKey`，方法 `GetHash`），**需确认**是只给请求签名，还是也会校验响应 |
| 游戏配置 | **完全由服务器下发，客户端内没有副本**（卡牌定义、升级花费、复活价格、商店、任务、活动全部在 `GameConfigurationData.LoadDataFromJson` 里从 JSON 解析） |
| 存档 | 由服务器下发 `gameState`，由 `GameState.LoadDataFromJson` 解析；本地也有 `localGameState` / `localMetaData` 缓存 |
| 登录 SDK | C# 侧 `VkSdk`（方法 `InitSDK` `Login` `Pay` `Submit` `TryQuit` `onFinish`，常量 `FC_LOGIN_SUCCESS` 等）→ Java 侧 `com.fangcun.FCUnityBridge`（方寸 SDK）→ `cn.uc.gamesdk`（UC 九游）。启动 Activity 为 `com.jianyou.mengshou.uc.UnityPlayerNativeActivity` |
| 第三方统计 | TalkingData、GameAnalytics、Parse、Boomlytics（`EventServerDispatcher`）、百度定位、支付宝、易联支付，全部应当无害失败或被屏蔽 |
| 卡牌美术 | 资源里有 **71 张** `card_illustration_*` 插画（列表见 `analysis/card_illustrations.txt`） |
| 中文文本 | UI 中文少量在 `sharedassets2.assets` 里；**卡牌名称和描述的中文未在客户端找到**，可能也是服务器下发（见第 6.3 节） |

### 2.1 卡牌定义的数据结构（从元数据还原）

```
CardDefinition:
  ID, PrefabID, Name, DescriptionID, UsageDescriptionID, AttributeDescriptionID,
  Type, Index, MaterialName, Slot,
  PrimaryAttributeBase, PrimaryAttributeAddition,
  SecondaryAttributeBase, SecondaryAttributeAddition
EvolvableCardDefinition : CardDefinition   + CardsRequiredForMerge, AdditionalCardSlotGraphics
FireHeroCardDefinition  : CardDefinition   + EquipedHero, LockDescId
CardDefinitions (static): CardDefinitionsList, CardDefinitionsDict, EvolvableCardDefinitionsList
                          TranslateCardTypeStrToEnum(), GetDefinitionForCardId()
```

**JSON 字段名、类型枚举字符串必须以反编译出的 `GameConfigurationData.LoadCardDefinitions` / `LoadEvolveDefinitions` 和 `CardDefinitions.TranslateCardTypeStrToEnum` 为准。** 上表只是 C# 属性名。

### 2.2 客户端调用的接口

完整列表见 `analysis/endpoints.txt`。按优先级分组：

- **P0 启动必需**：`newSession` / `newSessionSdk`（建立会话，`AppStartServiceRequest` / `AppStartServiceWithSdkRequest`）、`settings`（`GameSettingsRequest`，大概率下发 `GameConfigurationData`）、`time`（`TimeServiceRequest`）、`gamestate`（`TemporarySaveServiceRequest` 存档同步）、`userMetaProfile/get`
- **P1 核心玩法**：`level/start`、`level/end`、`card/getPack`、`card/upgrade`、`card/upgrade/full`、`card/equip`、`card/evolve`、`card/sacrifice`、`card/slot/unlock`、`card/slot/buyUnlock`、`card/slot/keys`、`customization/set`、`customization/purchase`、`mission/bonusReset`、`achievement/updateProgress`、`achievement/claim`、`tutorial/*`（5 个）
- **P2 外围（返回空或成功即可）**：`messages/*`、`opevent/*`（签到、月卡、活动）、`ranking/*`、`arena/*`、`iap/*`、`gamecenter/name`、`customIds/add`、`translate`、`tournament*`

`NetworkRequestHandler` 的方法名与各接口一一对应，是反编译时的主要入口：`StartRunning` `HandleLevelStartResponse` `RunningDone` `HandleLevelEndResponse` `Revive` `UpgradeCard` `SetActiveCustomizationItems` `UnlockCustomizationItem` `BuyCoinsPack` `BuyConsumable` `BuyCardsPack` `ParseReceivedCards` `GetUserMetaProfileData` `UpdateAchievements` `ClaimAchievements` `DoIap*` `SyncLocalGameState` `UpdateSlotUnlockStatus` `ClaimMessages` `UpdateMessages` `UpdateActivityProgress` `UpdateSigninProgress` `UpdateMonthCardProgress` 等。

---

## 3. 仓库结构（目标）

```
original/kapai.apk          # 原始 APK（仓库所有者放入，只读，禁止修改）
analysis/                   # 前期分析产物（已提供）
  types.json                #   所有类型及其字段、方法名
  strings.txt               #   #US 字符串表（序号 \t JSON 字符串）
  endpoints.txt             #   接口列表
  card_illustrations.txt    #   71 张卡牌插画资源名
  asset_names_card_prefix.txt
tools/dnmeta.py             # 最小 .NET 元数据解析器（无依赖）
docs/
  protocol.md               # 【你要写】每个接口的请求字段、响应字段、解析代码位置
  config-schema.md          # 【你要写】settings / gameState 的完整 JSON 结构
  decompiled/               # 【你生成】ilspycmd 反编译出的 C#（只放需要的类，或整份）
patcher/                    # 【你要写】自动修改 Assembly-CSharp.dll 的程序
build/build.sh              # 【你要写】一键：解包 → 打补丁 → 重新打包 → 签名
server/                     # 【你要写】Python 假服务器
  server.py
  data/config.json          #   GameConfigurationData（卡牌定义、价格等）
  data/default_state.json   #   新玩家初始存档
  tests/
README.zh.md                # 【你要写】给仓库所有者的中文使用说明
```

---

## 4. 环境与工具

工作环境为 Linux。需要的工具：

- **.NET SDK 8** + `dotnet tool install -g ilspycmd`：反编译 `Assembly-CSharp.dll`
- **Mono.Cecil** 或 **dnlib**（NuGet）：写 C# 补丁程序，修改 IL 后保存。Unity 4.6 的 Mono 运行时对应 .NET 2.0/3.5 程序集，**保存时保持原目标框架和引用不变**
- **apktool**（需要 Java 17+）：解包和重新打包 APK。如果需要改 smali，也用它
- **apksigner** 或 **uber-apk-signer**：用自己生成的 debug key 签名
- **Python 3.11+**：服务器只用标准库，或者只依赖 Flask（Termux 里 `pip install flask` 即可装上）
- 可选：**UnityPy**，用来列出资源里的 GameObject / Prefab 名称，辅助建立卡牌 PrefabID 对照表

**不要**在构建流程里依赖 Windows 专用工具（dnSpy、AssetStudio GUI）。

---

## 5. 阶段一：反编译与协议文档

1. 用 `ilspycmd` 反编译 `Assembly-CSharp.dll`，输出到 `docs/decompiled/`。
2. 通读以下类，写出 `docs/protocol.md`：
   - `NetworkingProperties`、`HttpRequestDispatcher`、`AbstractServiceRequest` 及其所有子类、`GenericRequest`、`MessageEncrypter`
   - `NetworkRequestHandler`（每个方法：发什么、怎么解析响应、失败时怎么处理）
   - `GameConfigurationData.LoadDataFromJson` 及全部 `Load*` 方法
   - `GameState.LoadDataFromJson` 及全部 `Load*` 方法、`InitializeNewState`
   - `VkSdk`、`MainMenuController.Start`，以及启动场景里决定"何时发起登录 / 新会话"的代码
   - `FatalServerErrorDialog`、`NetworkingProperties.ServerError*`：服务器返回什么样的错误码会弹窗、强制更新或进入维护
3. `protocol.md` 里每个接口写清楚：**URL 路径、请求 JSON 示例、客户端读取的响应字段（含类型、是否必需）、对应的解析代码位置**。
4. 写 `docs/config-schema.md`：`settings` 响应和 `gameState` 的完整 JSON 结构（含所有枚举字符串，例如卡牌 Type、货币类型 `COINS` / `GEMS` / `HEARTS`、奖励类型）。
5. **回答这几个问题，写进文档开头：**
   - 响应外层的信封格式是什么（例如 `{"code":0,"data":{...}}`，还是直接返回对象）？成功和失败分别靠什么判断？
   - `MessageEncrypter.GetHash` 用在哪里？客户端会不会校验响应？
   - 卡牌的 `Name`、`DescriptionID` 是本地化键，还是直接显示的文本？本地化表从哪里来？
   - 启动流程里，`newSession` 和 `newSessionSdk` 哪个会被调用？前置条件是什么？

**验收**：`protocol.md` 覆盖 2.2 节里的 P0、P1 全部接口，并回答上面 4 个问题。

---

## 6. 阶段二：客户端补丁（`patcher/` + `build/build.sh`）

用 Mono.Cecil 或 dnlib 写一个补丁程序，**每处修改都要在代码注释和 `README.zh.md` 里说明改了什么**：

1. **服务器地址**：让 `NetworkingProperties.get_SERVER_URL` 固定返回 `http://127.0.0.1:8083/api/`。地址做成补丁程序的命令行参数，方便以后改成局域网 IP。`EventServerDispatcher.EVENT_SERVER_URL` 等统计地址也改到本地，或者让发送方法直接 return。
2. **跳过 SDK 登录**：修改 `VkSdk`：
   - `InitSDK` / `Initialize`：不调用 Java，直接按"初始化成功"回调；
   - `Login`：不调用 Java，直接以 `FC_LOGIN_SUCCESS` 触发 `CurrentLoginEvent`，给一个固定的假 `userId` / `userName` / `token` / `channel`；
   - `Pay`：直接按支付成功回调，由服务器的 `iap/*` 接口发放物品；
   - `Submit`、`TryQuit` 等：空实现或直接回调成功。
   - 要先读懂 `onFinish` 怎么解析 Java 传回来的字符串，再决定是伪造这个字符串，还是直接触发事件。
3. **Java 侧**：检查 `UnityPlayerNativeActivity` 和 `com.fangcun.*` 在 `onCreate` 里会不会主动初始化 SDK、弹窗或因联网失败退出。如果会，就用 apktool 改 smali 把它跳过。**能只改 C# 就不要改 Java。**
4. 如果 `MessageEncrypter` 会校验响应，就让校验恒为通过。
5. `build/build.sh`：`original/kapai.apk` → apktool 解包 → 替换打过补丁的 `Assembly-CSharp.dll` → 重新打包 → zipalign → 签名 → 输出 `out/kapai-offline.apk`。签名用的 keystore 由脚本首次运行时自动生成，**不要提交进仓库**（写进 `.gitignore`）。

**验收**：`build.sh` 在干净环境里一条命令跑通；补丁程序对补丁后的 DLL 再运行一次时，能检测到"已打过补丁"，不会重复修改。

---

## 7. 阶段三：假服务器（`server/`）

1. **结构**：一个 `server.py`，用一张"路径 → 处理函数"的路由表；监听 `0.0.0.0:8083`，路径前缀 `/api/`。
2. **日志**：**每个请求的路径、请求体、响应体都完整记录到 `server/logs/requests.log`**。仓库所有者会把这份日志发回来排错，这是最重要的调试手段。
3. **兜底**：未实现的路径一律按约定的信封格式返回"成功 + 空数据"，同时在日志里标记 `UNHANDLED`。
4. **存档**：单个玩家，存成 `server/data/save.json`。每次修改先写临时文件再改名，防止写坏。
5. **P0 接口**：让客户端能走完启动流程、进入主菜单。
6. **P1 接口**：跑酷开始和结算（结算时把金币、钻石、最高分写进存档）、抽卡包（随机从全部卡里抽）、升级、进化、装备、卡槽、换装、成就、新手引导。
7. **P2 接口**：全部返回空列表或成功。`iap/*` 直接发放对应物品，不校验收据。
8. **测试**：`server/tests/` 里用 Python 标准库 `unittest`，覆盖每个 P0 / P1 接口至少一个用例，响应格式要符合 `protocol.md`。

**验收**：`python -m unittest` 全部通过；服务器在 Termux（Python 3.11、无 C 扩展依赖）里能直接运行。

---

## 8. 阶段四：游戏内容数据（`server/data/config.json`）

1. **卡牌定义**：为 71 张插画各生成一条 `CardDefinition`。
   - **ID / PrefabID 要对得上**：PrefabID 决定战斗中的实际效果，必须对应资源里真实存在的 Prefab 名称。用 UnityPy 列出资源名，结合代码里引用 PrefabID 的地方（以及 `*Booster` 类挂在哪些 Prefab 上）建立对照表，写进 `docs/card-mapping.md`。对不上的卡宁可先不放，也不要放进会导致崩溃的配置。
   - **Type**：按插画名或资源名里的 common / rare / epic / legendary 等字样推断；推断不出来就统一填 common。
   - **数值**：随便填一个合理值即可，例如 `PrimaryAttributeBase = 10`、`Addition = 1`。
   - **名字**：如果第 5 节的结论是"Name 直接显示文本"，就根据插画名起中文名；如果是本地化键，而本地化表也需要服务器下发，就一起生成。
2. **其他配置**（升级花费、复活价格、商店、任务、成就、换装物品、活动）：先填满足客户端解析所需的最小数据，**不追求平衡**。
3. **初始存档**：新玩家直接拥有**全部卡牌**、大量金币和钻石、全部卡槽已解锁；新手引导标记为已完成（如果能这样做的话）。

---

## 9. 已知风险（请在 README.zh.md 里如实告知仓库所有者）

1. **只有 32 位原生库。** 很多 2023 年以后的新手机已经不能运行 32 位应用。用 `adb shell getprop ro.product.cpu.abilist` 检查，输出里没有 `armeabi-v7a` 就装不了。备选方案：电脑上的安卓模拟器（MuMu、雷电等支持 32 位和 x86 的），或者一台老手机。
2. **Unity 4.6 在新版 Android 上**可能出现闪退、黑屏或权限问题，需要实机测试后再处理。
3. **原版数值已经丢失**，内容只能做到"能玩"，无法完全还原。
4. **代理不能在真机上运行游戏。** 每一轮修改后，需要仓库所有者在手机上测试，并提供 `server/logs/requests.log` 和 `adb logcat -s Unity` 的输出。请把每轮需要对方执行的步骤写清楚，**一次只验证一个目标**（例如"这轮只看能不能进主菜单"）。

---

## 10. 给仓库所有者的工作方式

- 本仓库**设为私有**；`original/kapai.apk` 由你自己放进去（约 46MB，GitHub 单文件限制为 100MB，能放下）。
- 按阶段让 Jules 一个一个做：**先只做阶段一**，看完 `protocol.md` 再继续。阶段一的结论会决定后面的做法。
- 每次测试后，把日志和截图交回来。测试命令会写在 `README.zh.md` 里。

## 11. 代理工作约束

- 不修改 `original/` 下的任何文件。
- 不在仓库里提交签名密钥、构建产物（`out/`、`*.apk`，原始 APK 除外）或解包出来的游戏资源。
- 不把游戏或资源上传到任何外部服务。
- 不实现任何真实支付或连接任何真实的第三方账号服务。
- 遇到客户端里写死的、无法确定的行为，先在 `docs/` 里记录假设和依据，再编码。
