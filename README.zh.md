# 卡牌萌兽（Monsu）离线版

把已停服的《卡牌萌兽》改成可以完全离线玩：修改过的客户端 + 一个 Python 假服务器。仅供个人离线游玩，仓库内不含游戏文件。

## 内容

| 目录 | 作用 |
|---|---|
| `patcher/` | Mono.Cecil 补丁程序，修改 `Assembly-CSharp.dll` |
| `build/build.sh` | 一键：原版 APK → 打补丁 → 重新打包 → 签名 → `out/kapai-offline.apk` |
| `server/` | 假服务器（纯标准库，Termux 可跑）+ `tests/` |
| `docs/protocol.md` | 协议说明 |
| `analysis/` | 前期元数据分析 |

## 补丁改了什么（`patcher/Program.cs`）

| 位置 | 改动 |
|---|---|
| `NetworkingProperties.get_SERVER_URL` | 固定返回 `http://127.0.0.1:8083/api/`（`build.sh` 可传别的地址） |
| `NetworkingProperties.SetServerUrlBy*` | 空操作（原本会覆盖地址） |
| `VkSdk.Initialize` | 不再调用 Java，`Channel=0` |
| `VkSdk.InitSDK` / `Login` / `Pay` | 不调用 Java，直接用假 JSON 走 `onFinish`：初始化成功、登录成功（固定假用户）、支付成功 |
| `VkSdk.Submit` / `TryQuit` | 空操作 |

Java 侧（`UnityPlayerNativeActivity`）是标准 Unity 启动器，没有 SDK 初始化，所以**没有改 Java/smali**。
补丁对已打过补丁的 DLL 再运行会检测到并跳过。

## 构建（电脑上）

需要：.NET SDK 8、Python 3、JDK（`keytool` `jarsigner`）。

```
# 把原版 APK 放到 original/kapai.apk
build/build.sh                       # 服务器在手机本机
build/build.sh http://192.168.1.5:8083/api/   # 服务器在电脑上（局域网）
```

签名密钥首次运行时生成在 `build/debug.keystore`（已 gitignore，不要提交）。

## 运行

服务器（电脑或 Termux）：

```
cd server
python3 server.py          # 监听 0.0.0.0:8083；存档 server/data/save.json
python3 server.py --reset  # 清档重来
python3 -m unittest discover -s tests
```

Termux：`pkg install python`，把 `server/` 拷进去运行即可，无第三方依赖。

安装：

```
adb install -r out/kapai-offline.apk
# Android 14+ 拒绝安装低 targetSdk 应用时：
adb install -r --bypass-low-target-sdk-block out/kapai-offline.apk
```

## 初始存档（`server/server.py: new_save`）

200000 金币、5000 钻石、99 体力；普通/稀有/史诗卡全部 5 级，5 位英雄全部拥有，全部卡槽已解锁，全部服装已拥有，
消耗品各 5 个，教程全部跳过。传奇/终极/火焰英雄卡需要用「进化」合成（材料卡 5 级，已满足）。
所有数值（价格、升级花费、奖励）都是编的，见 `server/content.py`，随便改。

## 已知风险

1. **只有 32 位原生库（armeabi-v7a / x86）**。新手机可能不能运行：`adb shell getprop ro.product.cpu.abilist` 里要有 `armeabi-v7a`。
2. **Android 14+** 对 targetSdk=21 的应用有安装限制，需要上面的 `--bypass-low-target-sdk-block`。
3. Unity 4.6 在新系统上可能黑屏/闪退，需要实机测试。作者没有设备，**从未在真机上运行过**。
4. 不做：PvP/竞技场、排行榜、好友/社交、真实支付（`iap/*` 直接发放，不校验）。

## 反馈问题

发回：
- `server/logs/requests.log`（每个请求和响应都有记录；`UNHANDLED` 标记表示未实现的接口）
- `adb logcat -s Unity`
