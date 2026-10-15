# 《The Darkest Files》非官方简体中文补丁

为 Steam 版 [The Darkest Files](https://store.steampowered.com/app/2058730/The_Darkest_Files/)（中文暂译《至暗卷宗》）制作的玩家自制简体中文补丁。

**[⬇ 下载最新版本（Windows / Steam Deck）](https://github.com/longying217/TheDarkestFiles-zhCN/releases/latest)**

| | |
|---|---|
| 适用平台 | Windows、Steam Deck（Mac 版暂未支持） |
| 适用游戏版本 | 1.0.8.2 |
| 汉化范围 | 全部文本，约 11,500 条 |
| 是否修改游戏文件 | 否，删除即可恢复原版 |
| 翻译方式 | AI 翻译，未经逐条人工校对 |

## 汉化内容

- 菜单、设置、教程、成就
- 案卷、证词、文件、报纸、档案中的文字
- 两个案件（叛乱者、劫掠者）和尾声的全部对话与选项

图片形式的文档（扫描件、照片上的字）无法修改，中文模式下显示英文版图片。

> 译文由 AI 翻译完成，未经逐条人工校对，难免有不准确或不自然之处。发现问题欢迎在 [Issues](../../issues) 指出。

## 安装

1. 从 [Releases](https://github.com/longying217/TheDarkestFiles-zhCN/releases/latest) 页面的 Assets 下载 `TDF_zhCN_v*.zip`。
2. Steam 库中右键游戏 → 管理 → 浏览本地文件，打开游戏目录。
3. 把压缩包里的全部内容解压到游戏目录，与 `The Darkest Files.exe` 放在同一层。
4. **仅 Steam Deck**：游戏属性 → 启动选项，填入
   ```
   WINEDLLOVERRIDES="winhttp=n,b" %command%
   ```
5. 启动游戏 → 设置 → 语言，选择「简体中文」（占用原来 Français 的位置）。

完整说明（含卸载、常见问题）见压缩包内的「安装说明.txt」，或 [docs/安装说明.txt](docs/安装说明.txt)。

## 更新日志

**v1.0.5**
- 修复约 490 条文本显示为英文或「No translation found」报错的问题（游戏的法语表缺少这些条目，补丁现会自动补上）
- 修复「分配"{action}"」等提示中占位符未被替换的问题
- 补全施瓦茨等角色名；过场漫画上的人名标签、「陈述」标题改为中文

**v1.0.4**
- 档案柜抽屉标签和索引卡上的人名改为中文（这部分人名存放在游戏数据里，不在翻译表中，此前漏译）
- 安装说明中注明译文为 AI 翻译

**v1.0.3**
- 首个公开版本

## 报纸等图片文档

报纸、原始档案等是图片，图片上的字无法替换。查看时按画面下方提示的「显示文字记录」按键，即可看到中文译文。

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
