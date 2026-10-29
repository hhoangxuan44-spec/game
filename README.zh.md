# 卡牌萌兽（Monsu）离线版

把已停服的《卡牌萌兽》改成可以完全离线玩：修改过的客户端 + 一个 Python 假服务器。仅供个人离线游玩，仓库内不含游戏文件。

## 内容

| 目录 | 作用 |
|---|---|
| `patcher/` | Mono.Cecil 补丁程序，修改 `Assembly-CSharp.dll`（含把离线服务器合并进游戏） |
| `embedded/` | **内置服务器**（C#，编译进游戏）+ 差分测试 |
| `build/build.sh` | 一键：原版 APK → 编译内置服务器 → 打补丁 → 重新打包 → 签名 → `out/kapai-offline.apk` |
| `server/` | 外部版假服务器（Python，可选；和内置版行为一致） |
| `docs/protocol.md` | 协议说明 |
| `analysis/` | 前期元数据分析 |

**默认是内置服务器：装上 APK 就能玩，手机上不用跑任何别的东西。** 存档在应用私有目录的 `monsu_save.json`（清除应用数据即重置）。

## 补丁改了什么（`patcher/Program.cs`）

| 位置 | 改动 |
|---|---|
| `NetworkingProperties.get_SERVER_URL` | 固定返回 `http://127.0.0.1:8083/api/`（`build.sh` 可传别的地址） |
| `NetworkingProperties.SetServerUrlBy*` | 空操作（原本会覆盖地址） |
| `VkSdk.Initialize` | 不再调用 Java，`Channel=0` |
| `VkSdk.InitSDK` / `Login` / `Pay` | 不调用 Java，直接用假 JSON 走 `onFinish`：初始化成功、登录成功（固定假用户）、支付成功 |
| `VkSdk.Submit` / `TryQuit` | 空操作 |

内置服务器（`patcher/Embed.cs`）：

| 位置 | 改动 |
|---|---|
| `MonsuOffline.*`（新增类型） | `embedded/` 里的服务器逻辑，编译后克隆进 `Assembly-CSharp.dll` |
| `HttpRequestDispatcher.DispatchRequest` | 不再发 WWW 请求，改为调用 `MonsuOffline.Embedded.Dispatch`，下一帧用游戏自己的 `JSONObject.Parse` 解析后回调 |
| `NetworkHealthSensor.DoHealthCheck` | 直接报告“已联网”，不再请求 `<server>/up` |

服务器代码只能用游戏自带的（被裁剪过的）`mscorlib`里还存在的 API，所以编译时直接对着游戏的 DLL 编译，缺 API 会在编译期报错，而不是运行时崩。

Java 侧（`UnityPlayerNativeActivity`）是标准 Unity 启动器，没有 SDK 初始化，所以**没有改 Java/smali**。
补丁对已打过补丁的 DLL 再运行会检测到并跳过。

## 构建（电脑上）

需要：.NET SDK 8、Python 3、JDK（`keytool` `jarsigner`）。

```
# 把原版 APK 放到 original/kapai.apk
build/build.sh                              # 内置服务器（推荐）
build/build.sh --server                     # 外部 Python 服务器，在手机本机 127.0.0.1:8083
build/build.sh --server http://192.168.1.5:8083/api/   # 外部服务器在电脑上
```

签名密钥首次运行时生成在 `build/debug.keystore`（已 gitignore，不要提交）。

## 测试

```
python3 -m unittest discover -s server/tests                 # Python 服务器
MANAGED_DIR=build/work/managed MERGED_DLL=build/work/Assembly-CSharp.patched.dll \
  python3 embedded/tests/diff_test.py                          # 内置服务器 vs Python 服务器，69 步对比
```

差分测试把同一组请求分别交给 Python 服务器、C# 内置服务器（.NET）和合并进游戏 DLL 之后的版本（Mono 上运行），逐字段比较。

## 安装

```
adb install -r out/kapai-offline.apk
# Android 14+ 拒绝安装低 targetSdk 应用时：
adb install -r --bypass-low-target-sdk-block out/kapai-offline.apk
```

使用外部服务器时：`cd server && python3 server.py`（电脑或 Termux，无第三方依赖），`--reset` 清档。

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
- 内置版没有请求日志，只有 `adb logcat -s Unity`；需要日志请用 `build.sh --server`，然后发回 `server/logs/requests.log`（`UNHANDLED` 表示未实现的接口）
- `adb logcat -s Unity`
