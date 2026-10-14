# 《The Darkest Files》非官方简体中文补丁

为 Steam 版 [The Darkest Files](https://store.steampowered.com/app/2058730/The_Darkest_Files/)（中文暂译《至暗卷宗》）制作的玩家自制简体中文补丁。

**[⬇ 下载 v1.0.3（Windows / Steam Deck）](https://github.com/longying217/TheDarkestFiles-zhCN/raw/main/download/TDF_zhCN_v1.0.3.zip)**

| | |
|---|---|
| 适用平台 | Windows、Steam Deck（Mac 版暂未支持） |
| 适用游戏版本 | 1.0.8.2 |
| 汉化范围 | 全部文本，约 11,500 条 |
| 是否修改游戏文件 | 否，删除即可恢复原版 |

## 汉化内容

- 菜单、设置、教程、成就
- 案卷、证词、文件、报纸、档案中的文字
- 两个案件（叛乱者、劫掠者）和尾声的全部对话与选项

图片形式的文档（扫描件、照片上的字）无法修改，中文模式下显示英文版图片。

## 安装

1. 下载 [TDF_zhCN_v1.0.3.zip](https://github.com/longying217/TheDarkestFiles-zhCN/raw/main/download/TDF_zhCN_v1.0.3.zip)。
2. Steam 库中右键游戏 → 管理 → 浏览本地文件，打开游戏目录。
3. 把压缩包里的全部内容解压到游戏目录，与 `The Darkest Files.exe` 放在同一层。
4. **仅 Steam Deck**：游戏属性 → 启动选项，填入
   ```
   WINEDLLOVERRIDES="winhttp=n,b" %command%
   ```
5. 启动游戏 → 设置 → 语言，选择「简体中文」（占用原来 Français 的位置）。

完整说明（含卸载、常见问题）见压缩包内的「安装说明.txt」，或 [docs/安装说明.txt](docs/安装说明.txt)。

## 反馈

遇到漏译、错译、显示问题，请在 [Issues](../../issues) 提出，并尽量附上：

- 截图
- 游戏目录下的 `BepInEx/LogOutput.log`（其中 `[TDF-zh] MISSING` 开头的行是未覆盖到的文字）

## 实现原理

补丁基于 [BepInEx 5](https://github.com/BepInEx/BepInEx) 和 Harmony，在运行时完成以下替换，不改动任何游戏文件：

- 接管游戏原有的法语语言槽位，显示名改为「简体中文」
- 替换 Unity Localization 字符串表和 Dialogue System 对话数据库中的法语文本
- 动态生成 TextMeshPro 中文字体（思源黑体子集），作为游戏所有字体的后备字体
- 中文模式下，图片类文档改用英文版资源

```
plugin/TdfZhCN.cs        插件源代码
plugin/build.sh          编译脚本（需要游戏的 Managed 目录和 BepInEx/core）
translation/zh_CN.tsv    译文（按字符串表 ID / Articy ID 索引）
docs/                    安装说明、字体许可证
```

## 许可

- 插件源代码：MIT
- 译文：非商业用途可自由分享和修改，需注明出处
- 游戏及原文版权归 Paintbucket Games 所有。本补丁与开发商无关，如果喜欢这个游戏，请支持正版。

详见 [LICENSE](LICENSE)。

---

*Unofficial fan-made Simplified Chinese translation patch for The Darkest Files (Steam, Windows / Steam Deck). Not affiliated with Paintbucket Games.*
