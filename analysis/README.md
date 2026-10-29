# analysis/

从 `original/kapai.apk` 中 `assets/bin/Data/Managed/Assembly-CSharp.dll` 提取的元数据，用 `tools/dnmeta.py` 生成：

```
python3 tools/dnmeta.py <Assembly-CSharp.dll> out.json
```

- `types.json`：每个类型的命名空间、类名、字段名、方法名（没有方法体，方法体请用 ilspycmd 反编译）
- `strings.txt`：#US 用户字符串表，格式为 `序号<TAB>JSON字符串`；接口路径、JSON 字段名、服务器地址都在这里
- `endpoints.txt`：接口路径列表
- `card_illustrations.txt`：资源中 71 张卡牌插画的名字
- `asset_names_card_prefix.txt`：资源中所有以 `card_` 开头的名字
