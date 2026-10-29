# 文明6（com.aspyr.civvi）内置模组

把模组打进 APK，游戏每次启动时自动复制到它自己的 `Mods` 目录：
`Android/data/com.aspyr.civvi/files/Sid_Meiers_Civilization_VI/Mods/`。
这个目录是游戏自己的私有目录，游戏写它不受 Android 限制，所以不需要文件管理器、MT 或 adb。

## 构建
```
civ6/build.sh <原版 base.apk> <模组文件夹> [apktool.jar]
```
需要 JDK、apktool 2.9+、python3。输出 `civ6/out/civ6-mod.apk`。

改动：
- `assets/mods/AdvancedCheatMenu/`：模组文件（`.modinfo` 里 `UI/POPUPS/` 改成实际的 `UI/Popups/`，Android 文件系统区分大小写）
- 新增 `com.aspyr.civvi.ModInstaller`（`ModInstaller.smali`），在 `Civ6Application.onCreate` 开头调用一次，捕获所有异常，不影响游戏启动

## 额外功能：生成任意单位
`patch_mod.py` 给作弊菜单加了一行"生成单位"按钮，点开后：
- 列出游戏里所有单位，分 全部/陆军/海军/空军/其他 五个标签，每页 8 个，按造价从低到高排，早期单位在前。
- 点一个单位，就在**选中的单位旁边**生成；没有选中单位就用选中的城市；都没有就用首都。
- 右下角的 "x1 / x5 / x10" 按钮切换一次生成的数量。
- 只对人类玩家生效，AI 拿不到。

## 注意
- 重新签名后**不能覆盖安装**。卸载前务必备份 `Android/obb/com.aspyr.civvi/`（两个共约 4GB 的 OBB）和 `Android/data/com.aspyr.civvi/files/`（存档），安装新版后、启动前再放回去。
- 渠道（ccplay）或游戏有没有签名校验，没有在真机上验证过。
- 进游戏后在"附加内容/模组"里启用 Advanced Cheat Menu。
