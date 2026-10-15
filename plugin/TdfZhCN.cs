// The Darkest Files — 简体中文补丁 (BepInEx 5 plugin)
// Replaces the French language slot with Simplified Chinese at runtime.
// No game files are modified.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Text;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using PixelCrushers.DialogueSystem;
using TMPro;
using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.Localization.Pseudo;
using UnityEngine.Localization.Settings;
using UnityEngine.Localization.Tables;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.SceneManagement;
using UnityEngine.TextCore.LowLevel;

namespace TdfZhCN
{
    [BepInPlugin(Guid, "The Darkest Files 简体中文", Version)]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "long.tdf.zhcn";
        public const string Version = "1.0.5";

        internal static ManualLogSource Log;
        internal static string Dir;
        internal static string Target = "fr";       // language slot we take over
        internal static string DisplayName = "简体中文";

        // translations
        internal static readonly Dictionary<string, Dictionary<long, string>> Tables = new Dictionary<string, Dictionary<long, string>>();
        internal static readonly Dictionary<string, string> DlgText = new Dictionary<string, string>();
        internal static readonly Dictionary<string, string> MenuText = new Dictionary<string, string>();
        internal static readonly Dictionary<string, string> Names = new Dictionary<string, string>(); // English name -> Chinese
        internal static readonly HashSet<string> Smart = new HashSet<string>();                          // "Table/KeyId" entries that use Smart Format
        internal static readonly Dictionary<string, string> Hardcoded = new Dictionary<string, string>(); // exact UI text baked into prefabs

        internal static ConfigEntry<bool> CfgEnglishImages;
        internal static ConfigEntry<bool> CfgLogMissing;
        internal static ConfigEntry<string> CfgFontFile;
        internal static ConfigEntry<int> CfgSamplingSize;
        internal static ConfigEntry<int> CfgAtlasSize;

        Harmony harmony;
        static float nextScan;
        static string tickSource;

        void Awake()
        {
            Log = Logger;
            Dir = Path.GetDirectoryName(Info.Location);

            CfgEnglishImages = Config.Bind("General", "EnglishDocumentImages", true,
                "中文模式下，文件/档案图片使用英文版（否则会显示法语版图片）。");
            CfgLogMissing = Config.Bind("Debug", "LogMissingStrings", true,
                "在日志中记录尚未翻译、仍显示原文的表格条目（每条只记一次）。");
            CfgFontFile = Config.Bind("Font", "FontFile", "TDFzhSansSC-Medium.otf",
                "插件目录下的中文字体文件名（.otf/.ttf）。");
            CfgSamplingSize = Config.Bind("Font_v2", "SamplingPointSize", 48, "动态字体采样字号。");
            CfgAtlasSize = Config.Bind("Font_v2", "AtlasSize", 4096, "动态字体图集尺寸（4096 可容纳全部约 2500 个汉字）。");

            LoadTranslations();

            harmony = new Harmony(Guid);
            TryPatch("StringTableEntry", () => TablePatches.Apply(harmony));
            if (CfgEnglishImages.Value) TryPatch("Asset tables -> English", () => AssetPatches.Apply(harmony));
            TryPatch("Language name", () => LanguageNamePatch.Apply(harmony));
            TryPatch("Font redirect", () => FontManager.ApplyPatches(harmony));
            TryPatch("DialogueManager.SetLanguage", () => DialoguePatcher.ApplyPatches(harmony));
            TryPatch("Character names", () => NamePatch.Apply(harmony));
            TryPatch("Archive index cards", () => ArchiveCardPatch.Apply(harmony));

            // The game destroys BepInEx's manager object, so this component's Update() never runs.
            // Drive the periodic work from sources that survive: a hidden runner object,
            // a per-frame static engine event, and scene loads.
            SceneManager.sceneLoaded += (s, m) => { nextScan = 0; Tick("sceneLoaded"); };
            Application.onBeforeRender += () => Tick("onBeforeRender");
            try
            {
                var go = new GameObject("TDF_zhCN_Runner");
                go.hideFlags = HideFlags.HideAndDontSave;
                UnityEngine.Object.DontDestroyOnLoad(go);
                go.AddComponent<Runner>();
            }
            catch (Exception e) { Log.LogWarning("[TDF-zh] runner: " + e.Message); }
            // create the CJK font as early as possible, before the game builds its UI
            try { FontManager.Ensure(); } catch (Exception e) { Log.LogError("[TDF-zh] early font init: " + e); }
            Log.LogInfo($"[TDF-zh] v{Version} loaded. tables={Tables.Sum(t => t.Value.Count)} dialogue={DlgText.Count} menu={MenuText.Count}");
        }

        static void TryPatch(string what, Action a)
        {
            try { a(); Log.LogInfo($"[TDF-zh] patched: {what}"); }
            catch (Exception e) { Log.LogError($"[TDF-zh] FAILED to patch {what}: {e}"); }
        }

        void Update() { Tick("plugin.Update"); }

        internal static void Tick(string source)
        {
            if (Time.unscaledTime < nextScan) return;
            nextScan = Time.unscaledTime + 2f;
            if (tickSource == null) { tickSource = source; Log.LogInfo("[TDF-zh] periodic work running (via " + source + ")"); }
            try { FontManager.Ensure(); } catch (Exception e) { Log.LogError("[TDF-zh] font: " + e); FontManager.Failed = true; }
            try { DialoguePatcher.Scan(); } catch (Exception e) { Log.LogError("[TDF-zh] dialogue: " + e); }
            try { TablePatches.BulkApply(); } catch (Exception e) { Log.LogError("[TDF-zh] tables: " + e); }
            try { HardcodedText.Scan(); } catch (Exception e) { Log.LogError("[TDF-zh] labels: " + e); }
        }

        // ---------------- translation file ----------------
        // Format (UTF-8, tab separated, one entry per line):
        //   T <tab> TableName <tab> KeyId <tab> text
        //   D <tab> ArticyId  <tab> text        (dialogue line)
        //   M <tab> ArticyId  <tab> text        (choice / menu text)
        // Escapes inside text: \n newline, \t tab, \\ backslash. Lines starting with # are comments.
        static void LoadTranslations()
        {
            var files = Directory.GetFiles(Dir, "*.tsv").OrderBy(f => f, StringComparer.Ordinal).ToArray();
            foreach (var f in files)
            {
                int n = 0, lineNo = 0;
                foreach (var raw in File.ReadAllLines(f, Encoding.UTF8))
                {
                    lineNo++;
                    if (raw.Length == 0 || raw[0] == '#') continue;
                    var p = raw.Split('\t');
                    try
                    {
                        if (p[0] == "T" && p.Length >= 4)
                        {
                            Dictionary<long, string> d;
                            if (!Tables.TryGetValue(p[1], out d)) Tables[p[1]] = d = new Dictionary<long, string>();
                            d[long.Parse(p[2])] = Unescape(p[3]); n++;
                        }
                        else if (p[0] == "D" && p.Length >= 3) { DlgText[p[1]] = Unescape(p[2]); n++; }
                        else if (p[0] == "M" && p.Length >= 3) { MenuText[p[1]] = Unescape(p[2]); n++; }
                        else if (p[0] == "N" && p.Length >= 3) { Names[Unescape(p[1])] = Unescape(p[2]); n++; }
                        else if (p[0] == "S" && p.Length >= 3) { Smart.Add(p[1] + "/" + p[2]); n++; }
                        else if (p[0] == "H" && p.Length >= 3) { Hardcoded[Unescape(p[1])] = Unescape(p[2]); n++; }
                    }
                    catch (Exception e) { Log.LogWarning($"[TDF-zh] {Path.GetFileName(f)}:{lineNo} bad line: {e.Message}"); }
                }
                Log.LogInfo($"[TDF-zh] {Path.GetFileName(f)}: {n} entries");
            }
        }

        static string Unescape(string s)
        {
            if (s.IndexOf('\\') < 0) return s;
            var sb = new StringBuilder(s.Length);
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c == '\\' && i + 1 < s.Length)
                {
                    char n = s[++i];
                    if (n == 'n') sb.Append('\n');
                    else if (n == 't') sb.Append('\t');
                    else if (n == 'r') sb.Append('\r');
                    else if (n == '\\') sb.Append('\\');
                    else { sb.Append('\\').Append(n); }
                }
                else sb.Append(c);
            }
            return sb.ToString();
        }

        internal static bool IsTarget(Locale l) => l != null && l.Identifier.Code == Target;
    }

    // =====================================================================
    // Unity Localization string tables
    // =====================================================================
    static class TablePatches
    {
        static readonly HashSet<string> missingLogged = new HashSet<string>();
        static readonly HashSet<int> bulkDone = new HashSet<int>();
        static readonly Dictionary<string, AsyncOperationHandle<StringTable>> pending = new Dictionary<string, AsyncOperationHandle<StringTable>>();
        static string bulkLocale;

        public static void Apply(Harmony h)
        {
            var m = AccessTools.Method(typeof(StringTableEntry), "GetLocalizedString",
                new[] { typeof(IFormatProvider), typeof(IList<object>), typeof(PseudoLocale) });
            h.Patch(m, prefix: new HarmonyMethod(typeof(TablePatches), nameof(Prefix)));
        }

        static void Prefix(StringTableEntry __instance)
        {
            try { ApplyEntry(__instance, true); } catch { }
        }

        internal static void ApplyEntry(StringTableEntry e, bool fromDisplay)
        {
            var t = e.Table;
            if (t == null || t.LocaleIdentifier.Code != Plugin.Target) return;
            var name = t.TableCollectionName;
            Dictionary<long, string> d;
            string z;
            if (Plugin.Tables.TryGetValue(name, out d) && d.TryGetValue(e.KeyId, out z))
            {
                if (!string.Equals(e.Value, z, StringComparison.Ordinal)) e.Value = z;
                // some French entries lost the Smart Format flag ({action} etc. stayed literal)
                if (!e.IsSmart && Plugin.Smart.Contains(name + "/" + e.KeyId)) e.IsSmart = true;
            }
            else if (fromDisplay && Plugin.CfgLogMissing.Value)
            {
                var k = name + "/" + e.KeyId;
                if (missingLogged.Add(k)) Plugin.Log.LogWarning($"[TDF-zh] MISSING T\t{name}\t{e.KeyId}\t{e.Key}\t{e.Value}");
            }
        }

        // Pre-apply whole tables once they are loaded for the target locale,
        // so code that reads entry.Value directly also gets Chinese.
        public static void BulkApply()
        {
            if (!LocalizationSettings.InitializationOperation.IsDone) return;
            var loc = LocalizationSettings.SelectedLocale;
            if (!Plugin.IsTarget(loc)) { bulkLocale = null; return; }
            if (bulkLocale != Plugin.Target) { bulkLocale = Plugin.Target; pending.Clear(); }

            foreach (var name in Plugin.Tables.Keys)
            {
                AsyncOperationHandle<StringTable> h;
                if (!pending.TryGetValue(name, out h))
                {
                    h = LocalizationSettings.StringDatabase.GetTableAsync(name, loc);
                    pending[name] = h;
                }
                if (!h.IsValid() || !h.IsDone) continue;
                var table = h.Result;
                if (table == null) continue;
                if (!bulkDone.Add(table.GetInstanceID())) continue;
                int n = 0, added = 0;
                // ~490 entries exist in English but not in the game's French tables
                // ("No translation found for ..."); add them so the Chinese text has a home.
                Dictionary<long, string> d;
                if (Plugin.Tables.TryGetValue(name, out d))
                    foreach (var kv in d)
                        if (table.GetEntry(kv.Key) == null)
                        {
                            try { table.AddEntry(kv.Key, kv.Value); added++; } catch { }
                        }
                foreach (var e in table.Values) { ApplyEntry(e, false); n++; }
                if (added > 0) Plugin.Log.LogInfo($"[TDF-zh] table {name}: added {added} entries missing from the French table");
                Plugin.Log.LogInfo($"[TDF-zh] table {name}: applied ({n} entries)");
            }
        }
    }

    // =====================================================================
    // Localized images (documents etc.): use English assets in the target slot
    // =====================================================================
    static class AssetPatches
    {
        static Locale en;

        public static void Apply(Harmony h)
        {
            var t = typeof(LocalizedDatabase<AssetTable, AssetTableEntry>);
            foreach (var m in t.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            {
                if ((m.Name == "GetTableAsync" || m.Name == "GetTable" || m.Name == "GetTableEntryAsync" || m.Name == "GetTableEntry")
                    && !m.IsGenericMethodDefinition && m.GetParameters().Any(p => p.ParameterType == typeof(Locale)))
                {
                    h.Patch(m, prefix: new HarmonyMethod(typeof(AssetPatches), nameof(Prefix)));
                }
            }
        }

        static void Prefix(object __instance, ref Locale locale)
        {
            if (!Plugin.CfgEnglishImages.Value) return;
            if (!(__instance is LocalizedAssetDatabase)) return;
            var l = locale ?? LocalizationSettings.SelectedLocale;
            if (!Plugin.IsTarget(l)) return;
            if (en == null)
            {
                try { en = LocalizationSettings.AvailableLocales.GetLocale(new LocaleIdentifier("en")); } catch { }
            }
            if (en != null) locale = en;
        }
    }

    // =====================================================================
    // Settings menu: show "简体中文" instead of "Français"
    // =====================================================================
    static class LanguageNamePatch
    {
        public static void Apply(Harmony h)
        {
            var t = AccessTools.TypeByName("TDF.Localization.LocalizationManager");
            var m = AccessTools.Method(t, "GetAvailableLocals");
            h.Patch(m, postfix: new HarmonyMethod(typeof(LanguageNamePatch), nameof(Postfix)));
        }

        static void Postfix(List<string> __result)
        {
            try
            {
                var locales = LocalizationSettings.AvailableLocales.Locales;
                for (int i = 0; i < locales.Count && i < __result.Count; i++)
                    if (Plugin.IsTarget(locales[i])) __result[i] = Plugin.DisplayName;
            }
            catch { }
        }
    }

    // =====================================================================
    // Character names (dialogue log, notebook): CharacterData.FullName etc.
    // =====================================================================
    // =====================================================================
    // Archive file cabinet / index cards: names are stored on ArchiveIndexCard assets,
    // not in string tables. Only the display is changed; the getters stay English because
    // the cabinet drawers filter cards by the first letter of the last name.
    // =====================================================================
    static class ArchiveCardPatch
    {
        static MethodInfo getFirst, getLast, fmt2;

        public static void Apply(Harmony h)
        {
            var card = AccessTools.TypeByName("TDF.Plugins.PaintbucketArticyConverter.Archives.ArchiveIndexCard");
            getFirst = AccessTools.PropertyGetter(card, "FirstName");
            getLast = AccessTools.PropertyGetter(card, "LastName");
            fmt2 = AccessTools.Method(typeof(string), "Format", new[] { typeof(string), typeof(object), typeof(object) });
            var targets = new List<MethodBase>();
            var doc = AccessTools.TypeByName("TDF.DocumentMode.Pages.IndexCardDocumentObject");
            targets.Add(AccessTools.Method(doc, "Initialize"));
            var folder = AccessTools.TypeByName("TDF.GUI.Archives.FileCabinetFolder");
            foreach (var nt in folder.GetNestedTypes(BindingFlags.NonPublic | BindingFlags.Public))
                foreach (var m in nt.GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                    if (m.Name.Contains("InitData") && m.GetMethodBody() != null) targets.Add(m);
            int n = 0;
            foreach (var m in targets)
            {
                if (m == null) continue;
                h.Patch(m, transpiler: new HarmonyMethod(typeof(ArchiveCardPatch), nameof(Transpiler)));
                n++;
            }
            Plugin.Log.LogInfo($"[TDF-zh] archive card display patched in {n} methods");
        }

        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> ins)
        {
            foreach (var i in ins)
            {
                var mi = i.operand as MethodInfo;
                if (mi != null && (i.opcode == OpCodes.Call || i.opcode == OpCodes.Callvirt))
                {
                    if (mi == getFirst || mi == getLast)
                    {
                        yield return new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(ArchiveCardPatch), mi == getFirst ? nameof(First) : nameof(Last))) { labels = i.labels, blocks = i.blocks };
                        continue;
                    }
                    if (mi == fmt2)
                    {
                        yield return new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(ArchiveCardPatch), nameof(Format))) { labels = i.labels, blocks = i.blocks };
                        continue;
                    }
                }
                yield return i;
            }
        }

        static bool Active()
        {
            try { return Plugin.IsTarget(LocalizationSettings.SelectedLocale); } catch { return false; }
        }

        static string Zh(string en)
        {
            string z;
            return (en != null && Active() && Plugin.Names.TryGetValue(en, out z)) ? z : en;
        }

        public static string First(object card) => Zh((string)getFirst.Invoke(card, null));
        public static string Last(object card) => Zh((string)getLast.Invoke(card, null));

        public static string Format(string format, object a, object b)
        {
            if (Active() && format != null && format.Replace(" ", "") == "{0},{1}") return a + "，" + b;
            return string.Format(format, a, b);
        }
    }

    // =====================================================================
    // Text baked into prefabs (never localized by the game, e.g. full names on
    // comic panels, the "Statements" header). Exact whole-text matches only.
    // =====================================================================
    static class HardcodedText
    {
        // instance id -> (original English, the Chinese we put there)
        static readonly Dictionary<int, KeyValuePair<string, string>> swapped = new Dictionary<int, KeyValuePair<string, string>>();

        public static void Scan()
        {
            bool zh = Plugin.IsTarget(LocalizationSettings.SelectedLocale);
            foreach (var t in Resources.FindObjectsOfTypeAll<TMP_Text>())
            {
                if (t == null) continue;
                var cur = t.text;
                if (string.IsNullOrEmpty(cur)) continue;
                int id = t.GetInstanceID();
                KeyValuePair<string, string> sw;
                bool had = swapped.TryGetValue(id, out sw);
                if (had && !string.Equals(cur, sw.Value, StringComparison.Ordinal)) { swapped.Remove(id); had = false; } // game changed it since
                if (zh)
                {
                    if (had) continue;
                    var key = cur.Trim();
                    string z;
                    if ((key.IndexOf(' ') > 0 && Plugin.Names.TryGetValue(key, out z)) || Plugin.Hardcoded.TryGetValue(key, out z))
                    {
                        swapped[id] = new KeyValuePair<string, string>(cur, z);
                        t.text = z;
                    }
                }
                else if (had)
                {
                    t.text = sw.Key;
                    swapped.Remove(id);
                }
            }
        }
    }

    static class NamePatch
    {
        public static void Apply(Harmony h)
        {
            var t = AccessTools.TypeByName("TDF.Plugins.PaintbucketArticyConverter.Character.CharacterData");
            foreach (var n in new[] { "get_FullName", "get_FirstName", "get_LastName" })
                h.Patch(AccessTools.Method(t, n), postfix: new HarmonyMethod(typeof(NamePatch), nameof(Postfix)));
        }

        static void Postfix(ref string __result)
        {
            if (string.IsNullOrEmpty(__result) || Plugin.Names.Count == 0) return;
            try
            {
                if (!Plugin.IsTarget(LocalizationSettings.SelectedLocale)) return;
                string z;
                if (Plugin.Names.TryGetValue(__result, out z)) __result = z;
            }
            catch { }
        }
    }

    // =====================================================================
    // Dialogue System (articy) database
    // =====================================================================
    static class DialoguePatcher
    {
        sealed class RefEq : IEqualityComparer<object>
        {
            public new bool Equals(object a, object b) => ReferenceEquals(a, b);
            public int GetHashCode(object o) => RuntimeHelpers.GetHashCode(o);
        }
        static readonly HashSet<object> done = new HashSet<object>(new RefEq());

        public static void ApplyPatches(Harmony h)
        {
            var m = AccessTools.Method(typeof(DialogueManager), "SetLanguage", new[] { typeof(string) });
            h.Patch(m, prefix: new HarmonyMethod(typeof(DialoguePatcher), nameof(SetLanguagePrefix)));
        }

        static void SetLanguagePrefix() { try { Scan(); } catch (Exception e) { Plugin.Log.LogError("[TDF-zh] " + e); } }

        public static void Scan()
        {
            var dbs = new List<DialogueDatabase>(Resources.FindObjectsOfTypeAll<DialogueDatabase>());
            try { if (DialogueManager.hasInstance && DialogueManager.masterDatabase != null) dbs.Add(DialogueManager.masterDatabase); } catch { }
            foreach (var db in dbs)
            {
                if (db == null || db.conversations == null) continue;
                int convs = 0, lines = 0, menus = 0;
                foreach (var c in db.conversations)
                {
                    if (c == null || !done.Add(c)) continue;
                    convs++;
                    foreach (var e in c.dialogueEntries)
                    {
                        var aid = Field.LookupValue(e.fields, "Articy Id");
                        if (string.IsNullOrEmpty(aid)) continue;
                        string z;
                        if (Plugin.DlgText.TryGetValue(aid, out z)) { Set(e.fields, Plugin.Target, z); lines++; }
                        if (Plugin.MenuText.TryGetValue(aid, out z)) { Set(e.fields, "Menu Text " + Plugin.Target, z); menus++; }
                    }
                }
                if (convs > 0) Plugin.Log.LogInfo($"[TDF-zh] dialogue db '{db.name}': {convs} conversations, {lines} lines, {menus} choices patched");
            }
        }

        static void Set(List<Field> fields, string title, string value)
        {
            var f = Field.Lookup(fields, title);
            if (f != null) f.value = value;
            else fields.Add(new Field(title, value, FieldType.Localization));
        }
    }

    // =====================================================================
    // Chinese font: dynamic TMP font asset whose face is loaded from a file.
    // TMP calls FontEngine.LoadFontFace(Font, int) with the asset's sourceFontFile;
    // we rewrite those call sites (IL transpiler, immune to JIT inlining) so that
    // our placeholder Font loads the .otf from disk instead.
    // =====================================================================
    public class Runner : MonoBehaviour
    {
        void Update() { Plugin.Tick("runner"); }
    }

    static class FontManager
    {
        internal static Font Placeholder;
        internal static string FontPath;
        internal static TMP_FontAsset Cjk;
        internal static bool Failed;
        static int lastFontCount = -1;
        static bool warnedShader;

        static readonly MethodInfo Original = AccessTools.Method(typeof(FontEngine), "LoadFontFace", new[] { typeof(Font), typeof(int) });
        static readonly MethodInfo Replacement = AccessTools.Method(typeof(FontManager), nameof(LoadFontFace));

        public static void ApplyPatches(Harmony h)
        {
            var t = typeof(TMP_FontAsset);
            var names = new HashSet<string> { "CreateFontAsset", "AddSynthesizedCharactersAndFaceMetrics", "GetGlyphIndex",
                "TryAddCharacters", "TryAddCharacterInternal", "TryGetCharacter_and_QueueRenderToTexture" };
            int n = 0;
            foreach (var m in t.GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            {
                if (!names.Contains(m.Name) || m.IsGenericMethodDefinition || m.GetMethodBody() == null) continue;
                h.Patch(m, transpiler: new HarmonyMethod(typeof(FontManager), nameof(Transpiler)));
                n++;
            }
            // belt and braces: direct prefix as well
            h.Patch(Original, prefix: new HarmonyMethod(typeof(FontManager), nameof(Prefix)));
            Plugin.Log.LogInfo($"[TDF-zh] font redirect on {n} TMP methods");
        }

        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> ins)
        {
            foreach (var i in ins)
            {
                if ((i.opcode == OpCodes.Call || i.opcode == OpCodes.Callvirt) && i.operand is MethodInfo mi && mi == Original)
                    yield return new CodeInstruction(OpCodes.Call, Replacement) { labels = i.labels, blocks = i.blocks };
                else yield return i;
            }
        }

        static bool Prefix(Font font, int pointSize, ref FontEngineError __result)
        {
            if (Placeholder != null && ReferenceEquals(font, Placeholder))
            {
                __result = FontEngine.LoadFontFace(FontPath, pointSize);
                return false;
            }
            return true;
        }

        public static FontEngineError LoadFontFace(Font font, int pointSize)
        {
            if (Placeholder != null && ReferenceEquals(font, Placeholder))
                return FontEngine.LoadFontFace(FontPath, pointSize);
            return FontEngine.LoadFontFace(font, pointSize);
        }

        public static void Ensure()
        {
            if (Failed) return;
            if (Cjk == null)
            {
                if (TMP_Settings.instance == null) return; // TMP not ready yet
                FontPath = Path.Combine(Plugin.Dir, Plugin.CfgFontFile.Value);
                if (!File.Exists(FontPath))
                {
                    var any = Directory.GetFiles(Plugin.Dir, "*.otf").Concat(Directory.GetFiles(Plugin.Dir, "*.ttf")).FirstOrDefault();
                    if (any == null) { Plugin.Log.LogError("[TDF-zh] font file not found: " + FontPath); Failed = true; return; }
                    Plugin.Log.LogInfo("[TDF-zh] configured font missing, using " + Path.GetFileName(any));
                    FontPath = any;
                }

                if (!EnsureShader()) return; // try again later, once TMP shaders are loaded
                FontEngine.InitializeFontEngine();
                var err = FontEngine.LoadFontFace(FontPath, Plugin.CfgSamplingSize.Value);
                if (err != FontEngineError.Success) { Plugin.Log.LogError("[TDF-zh] FontEngine cannot read font: " + err); Failed = true; return; }

                Placeholder = new Font("TDF_zhCN_Placeholder");
                int size = Plugin.CfgSamplingSize.Value;
                int pad = Math.Max(4, size / 10);
                Cjk = TMP_FontAsset.CreateFontAsset(Placeholder, size, pad, GlyphRenderMode.SDFAA,
                    Plugin.CfgAtlasSize.Value, Plugin.CfgAtlasSize.Value, AtlasPopulationMode.Dynamic, true);
                if (Cjk == null) { Plugin.Log.LogError("[TDF-zh] CreateFontAsset returned null"); Failed = true; return; }
                Cjk.name = "TDF_zhCN_SourceHanSansSC";
                Placeholder.hideFlags |= HideFlags.DontUnloadUnusedAsset;
                Cjk.hideFlags |= HideFlags.DontUnloadUnusedAsset;
                if (Cjk.material != null) Cjk.material.hideFlags |= HideFlags.DontUnloadUnusedAsset;
                Protect();

                string missing;
                bool ok = Cjk.TryAddCharacters("中文测试", out missing, false);
                Plugin.Log.LogInfo($"[TDF-zh] CJK font asset created (test glyphs ok={ok}, missing='{missing}')");

            }

            bool changed = false;
            var global = TMP_Settings.fallbackFontAssets;
            if (global == null)
            {
                global = new List<TMP_FontAsset>();
                AccessTools.Field(typeof(TMP_Settings), "m_fallbackFontAssets").SetValue(TMP_Settings.instance, global);
            }
            if (!global.Contains(Cjk)) { global.Add(Cjk); changed = true; }
            Protect();

            // attach as fallback to every font asset the game has loaded (re-checked every time:
            // font assets can be unloaded and reloaded with a fresh fallback list)
            var all = Resources.FindObjectsOfTypeAll<TMP_FontAsset>();
            int added = 0;
            foreach (var fa in all)
            {
                if (fa == null || fa == Cjk) continue;
                var list = fa.fallbackFontAssetTable;
                if (list == null) { list = new List<TMP_FontAsset>(); fa.fallbackFontAssetTable = list; }
                if (!list.Contains(Cjk)) { list.Add(Cjk); added++; }
            }
            if (added > 0) { changed = true; Plugin.Log.LogInfo($"[TDF-zh] CJK fallback attached to {added} font assets (total {all.Length})"); }
            if (all.Length != lastFontCount) { lastFontCount = all.Length; changed = true; }
            if (changed) RefreshTexts();
        }

        // texts that were laid out before the fallback existed keep their boxes until re-parsed
        static void RefreshTexts()
        {
            int n = 0;
            foreach (var t in Resources.FindObjectsOfTypeAll<TMP_Text>())
            {
                if (t == null) continue;
                try { t.havePropertiesChanged = true; if (t.isActiveAndEnabled) t.ForceMeshUpdate(false, true); n++; } catch { }
            }
            Plugin.Log.LogInfo($"[TDF-zh] refreshed {n} text objects");
        }

        static void Protect()
        {
            if (Cjk == null) return;
            var tex = Cjk.atlasTextures;
            if (tex != null) foreach (var t in tex) if (t != null) t.hideFlags |= HideFlags.DontUnloadUnusedAsset;
        }

        static bool EnsureShader()
        {
            var prop = AccessTools.Property(typeof(ShaderUtilities), "ShaderRef_MobileSDF");
            Shader cur = null;
            try { cur = prop?.GetValue(null, null) as Shader; } catch { }
            if (cur != null) { Plugin.Log.LogInfo("[TDF-zh] TMP mobile SDF shader present: " + cur.name); return true; }
            Shader s =Shader.Find("TextMeshPro/Mobile/Distance Field") ?? Shader.Find("TextMeshPro/Distance Field");
            if (s == null)
            {
                foreach (var fa in Resources.FindObjectsOfTypeAll<TMP_FontAsset>())
                    if (fa != null && fa.material != null && fa.material.shader != null && fa.material.HasProperty("_GradientScale")) { s = fa.material.shader; break; }
            }
            if (s != null)
            {
                AccessTools.Field(typeof(ShaderUtilities), "k_ShaderRef_MobileSDF").SetValue(null, s);
                Plugin.Log.LogInfo("[TDF-zh] using shader for CJK font: " + s.name);
                return true;
            }
            if (!warnedShader) { warnedShader = true; Plugin.Log.LogWarning("[TDF-zh] no TMP SDF shader found yet, will retry"); }
            return false;
        }
    }
}
