using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEngine;

#if TMP_3_2_0_PRE_2 || UGUI_2_0
using GlyphAdjustmentRecord = UnityEngine.TextCore.LowLevel.GlyphAdjustmentRecord;
using GlyphPairAdjustmentRecord = UnityEngine.TextCore.LowLevel.GlyphPairAdjustmentRecord;
using GlyphValueRecord = UnityEngine.TextCore.LowLevel.GlyphValueRecord;
#else
using GlyphAdjustmentRecord = TMPro.TMP_GlyphAdjustmentRecord;
using GlyphPairAdjustmentRecord = TMPro.TMP_GlyphPairAdjustmentRecord;
using GlyphValueRecord = TMPro.TMP_GlyphValueRecord;
#endif

public class EasyThaiFontAdjustment : EditorWindow
{
    private TMP_FontAsset fontAsset;
    private string sampleText = @"ปิ่น อื้อ จี๊ด อื้ม ปรื๋อ ผื่น ลิ้น 
ติ๋ม ปริ่ม หั่น ปั้น ตั๊ก ป้า ม๊า 
ฝ่า ป่า ฟ้า ผ่า ผ้า จ๋า สิทธิ์  
ย่ำ ถ้ำ ฎุ ฎูำ";

    private Vector2 scrollPosition;
    private List<AdjustmentRule> adjustmentRules = new List<AdjustmentRule>();
    private int selectedTab = 0;
    private string[] tabNames = new string[] { "Templates", "Custom", "Rules" };

    private Dictionary<string, bool> categoryFoldouts = new Dictionary<string, bool>();
    private Dictionary<string, PresetConfig> presetConfigs = new Dictionary<string, PresetConfig>();

    // One-Click System
    private bool showAdvancedOptions = false;
    private bool isProcessing = false;
    private float progress = 0f;
    private string progressMessage = "";

    // Undo System
    private List<GlyphPairAdjustmentRecord> backupRecords = null;
    private TMP_FontAsset lastModifiedFont = null;

    [System.Serializable]
    private class AdjustmentRule
    {
        public string ruleName;
        public char firstChar;
        public char secondChar;
        public float xPlacement;
        public float yPlacement;
        public bool selected = true;
        public string category;

        public AdjustmentRule(string name, char first, char second, float x, float y, string cat = "")
        {
            ruleName = name;
            firstChar = first;
            secondChar = second;
            xPlacement = x;
            yPlacement = y;
            category = cat;
        }

        public string DisplayName => $"{firstChar} + {secondChar}";
        public string Key => $"{firstChar}_{secondChar}";
    }

    [System.Serializable]
    private class PresetConfig
    {
        public string name;
        public string description;
        public float defaultX;
        public float defaultY;
        public float offsetX = 0f; // Offset เพิ่มเติมจากค่าที่คำนวณ
        public float offsetY = 0f; // Offset เพิ่มเติมจากค่าที่คำนวณ
        public Color color;
        public bool expanded = false;

        public PresetConfig(string n, string desc, float x, float y, Color c)
        {
            name = n;
            description = desc;
            defaultX = x;
            defaultY = y;
            color = c;
        }

        // ค่าจริงที่จะใช้ (Default + Offset)
        public float FinalX => defaultX + offsetX;
        public float FinalY => defaultY + offsetY;
    }

    // Thai Character Sets
    private static readonly char[] allConsonants = new char[]
    {
        'ก', 'ข', 'ฃ', 'ค', 'ฅ', 'ฆ', 'ง', 'จ', 'ฉ', 'ช',
        'ซ', 'ฌ', 'ญ', 'ฎ', 'ฏ', 'ฐ', 'ฑ', 'ฒ', 'ณ', 'ด',
        'ต', 'ถ', 'ท', 'ธ', 'น', 'บ', 'ป', 'ผ', 'ฝ', 'พ',
        'ฟ', 'ภ', 'ม', 'ย', 'ร', 'ล', 'ว', 'ศ', 'ษ', 'ส',
        'ห', 'ฬ', 'อ', 'ฮ'
    };

    private static readonly char[] ascenderConsonants = new char[] { 'ป', 'ฝ', 'ฟ', 'ฬ' };
    private static readonly char[] descenderConsonants = new char[] { 'ฎ', 'ฏ' };

    private static readonly char[] upperVowels = new[] { 'ิ', 'ี', 'ึ', 'ื', '็', 'ั' };
    private static readonly char[] lowerVowels = new[] { 'ุ', 'ู' };
    private static readonly char saraAm = 'ำ'; // สระอำ
    private static readonly char[] toneMarks = new[] { '่', '้', '๊', '๋' };
    private static readonly char thanThaKhaat = '์';

    [MenuItem("Tools/Easy Thai Font Adjustment")]
    public static void ShowWindow()
    {
        var window = GetWindow<EasyThaiFontAdjustment>("Easy Thai Font Adjustment");
        window.minSize = new Vector2(600, 700);
    }

    private void OnEnable()
    {
        InitializePresets();
    }

    private void InitializePresets()
    {
        presetConfigs.Clear();

        // คำนวณค่า Dynamic จาก Font Asset
        float baseAdjustment = CalculateBaseAdjustment();
        float upperToneAdjustment = CalculateUpperToneAdjustment();
        float ascenderAdjustment = CalculateAscenderAdjustment();

        presetConfigs["consonant_upper"] = new PresetConfig(
            "พยัญชนะ + สระบน",
            "ทุกพยัญชนะ (ก-ฮ) + สระบน (ิ ี ึ ื ั ็)",
            0f, baseAdjustment,
            new Color(0.2f, 0.6f, 1f, 0.3f)
        );

        presetConfigs["consonant_tone"] = new PresetConfig(
            "พยัญชนะ + วรรณยุกต์",
            "ทุกพยัญชนะ (ก-ฮ) + วรรณยุกต์ (่ ้ ๊ ๋)",
            0f, baseAdjustment,
            new Color(1f, 0.6f, 0.2f, 0.3f)
        );

        presetConfigs["consonant_thanthakhaat"] = new PresetConfig(
            "พยัญชนะ + ทัณฑฆาต (์)",
            "ทุกพยัญชนะ (ก-ฮ) + ์",
            0f, baseAdjustment,
            new Color(0.6f, 0.2f, 1f, 0.3f)
        );

        presetConfigs["upper_tone"] = new PresetConfig(
            "สระบน + วรรณยุกต์",
            "สระบน (ิ ี ึ ื ั ็) + วรรณยุกต์ (่ ้ ๊ ๋)",
            0f, upperToneAdjustment, // 👈 ค่าพิเศษสำหรับสระบน+วรรณยุกต์
            new Color(1f, 0.3f, 0.5f, 0.3f)
        );

        presetConfigs["upper_thanthakhaat"] = new PresetConfig(
            "สระบน + ทัณฑฆาต (์)",
            "สระบน (ิ ี ึ ื ั ็) + ์",
            0f, upperToneAdjustment,
            new Color(0.5f, 1f, 0.3f, 0.3f)
        );

        presetConfigs["sara_am_tone"] = new PresetConfig(
            "สระอำ + วรรณยุกต์",
            "สระอำ (ำ) + วรรณยุกต์ (่ ้ ๊ ๋)",
            0f, upperToneAdjustment, // ใช้ค่าเดียวกับสระบน + วรรณยุกต์
            new Color(1f, 0.5f, 1f, 0.3f)
        );

        presetConfigs["ascender_upper"] = new PresetConfig(
            "พยัญชนะหางบน + สระบน/วรรณยุกต์",
            "พยัญชนะหางบน (ป ฝ ฟ ฬ) + สระบน/วรรณยุกต์",
            0f, ascenderAdjustment, // 👈 ค่าพิเศษสำหรับพยัญชนะหางบน
            new Color(1f, 0.2f, 0.2f, 0.3f)
        );

        presetConfigs["descender_lower"] = new PresetConfig(
            "พยัญชนะหางล่าง + สระล่าง",
            "พยัญชนะหางล่าง (ฎ ฏ) + สระล่าง (ุ ู)",
            0f, 2f,
            new Color(0.2f, 1f, 0.8f, 0.3f)
        );

        Debug.Log($"[EasyThaiFontAdjustment] Initialized with dynamic values - Base: {baseAdjustment}, Upper+Tone: {upperToneAdjustment}, Ascender: {ascenderAdjustment}");
    }

    private float CalculateBaseAdjustment()
    {
        if (fontAsset == null) return -2f; // Default สำหรับ Point Size 100

        float pointSize = fontAsset.faceInfo.pointSize;
        float scale = fontAsset.faceInfo.scale;

        // สูตร: -2% ของ Point Size (100 = -2, 247 = -4.94)
        float adjustment = -(pointSize * 0.02f * scale);
        return Mathf.Round(adjustment * 100f) / 100f;
    }

    private float CalculateUpperToneAdjustment()
    {
        if (fontAsset == null) return 19.4f; // Default สำหรับ Point Size 100

        float pointSize = fontAsset.faceInfo.pointSize;
        float scale = fontAsset.faceInfo.scale;

        // สูตร: 19.4% ของ Point Size (100 = 19.4, 247 = 48)
        // มาจาก: 48/247 ≈ 0.194
        float adjustment = (pointSize * 0.194f * scale);
        return Mathf.Round(adjustment * 100f) / 100f;
    }

    private float CalculateAscenderAdjustment()
    {
        if (fontAsset == null) return -3.5f; // Default สำหรับ Point Size 100

        float pointSize = fontAsset.faceInfo.pointSize;
        float scale = fontAsset.faceInfo.scale;

        // สูตร: -3.5% ของ Point Size (100 = -3.5, 247 = -8.645)
        float adjustment = -(pointSize * 0.035f * scale);
        return Mathf.Round(adjustment * 100f) / 100f;
    }

    private void OnGUI()
    {
        EditorGUILayout.Space(10);

        // Header
        EditorGUILayout.BeginHorizontal();
        GUILayout.FlexibleSpace();
        var headerStyle = new GUIStyle(EditorStyles.boldLabel);
        headerStyle.fontSize = 18;
        headerStyle.alignment = TextAnchor.MiddleCenter;
        EditorGUILayout.LabelField("Easy Thai Font Adjustment", headerStyle, GUILayout.Height(30));
        GUILayout.FlexibleSpace();
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.HelpBox("เครื่องมือปรับแต่งฟอนต์ภาษาไทยสำหรับ TextMeshPro อย่างง่ายดาย", MessageType.Info);

        EditorGUILayout.Space(5);
        DrawLine();
        EditorGUILayout.Space(5);

        // Font Asset Selection
        TMP_FontAsset newFontAsset = EditorGUILayout.ObjectField("Font Asset", fontAsset, typeof(TMP_FontAsset), false) as TMP_FontAsset;

        if (newFontAsset != fontAsset)
        {
            fontAsset = newFontAsset;
            adjustmentRules.Clear();

            // Re-initialize presets with new font metrics
            if (fontAsset != null)
            {
                InitializePresets();
            }
        }

        if (fontAsset == null)
        {
            EditorGUILayout.Space(10);
            EditorGUILayout.HelpBox("กรุณาเลือก Font Asset ก่อน", MessageType.Warning);
            return;
        }

        // แสดงข้อมูล Font Metrics แบบกระชับ
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField($"Point Size: {fontAsset.faceInfo.pointSize}", GUILayout.Width(150));
        EditorGUILayout.LabelField($"Scale: {fontAsset.faceInfo.scale:F2}", GUILayout.Width(100));
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space(5);
        DrawLine();
        EditorGUILayout.Space(10);

        // ONE-CLICK AUTO FIX SECTION
        DrawOneClickSection();

        EditorGUILayout.Space(10);
        DrawLine();
        EditorGUILayout.Space(5);

        // Advanced Options (Foldout)
        showAdvancedOptions = EditorGUILayout.Foldout(showAdvancedOptions, "Advanced Options", true, EditorStyles.foldoutHeader);

        if (showAdvancedOptions)
        {
            EditorGUILayout.Space(5);

            // Tabs
            selectedTab = GUILayout.Toolbar(selectedTab, tabNames, GUILayout.Height(30));

            EditorGUILayout.Space(10);

            switch (selectedTab)
            {
                case 0:
                    DrawPresetsTab();
                    break;
                case 1:
                    DrawCustomTextTab();
                    break;
                case 2:
                    DrawRulesTab();
                    break;
            }
        }
    }

    private void DrawOneClickSection()
    {
        // One-Click Auto Fix Box
        var boxStyle = new GUIStyle(EditorStyles.helpBox);
        boxStyle.padding = new RectOffset(15, 15, 15, 15);

        EditorGUILayout.BeginVertical(boxStyle);

        // Title
        var titleStyle = new GUIStyle(EditorStyles.boldLabel);
        titleStyle.fontSize = 16;
        titleStyle.alignment = TextAnchor.MiddleCenter;
        EditorGUILayout.LabelField("ONE-CLICK AUTO FIX", titleStyle);

        EditorGUILayout.Space(5);
        DrawLine();
        EditorGUILayout.Space(10);

        // Main Button - สีเขียว ขนาดใหญ่
        GUI.backgroundColor = new Color(0.3f, 0.8f, 0.3f); // สีเขียว
        var buttonStyle = new GUIStyle(GUI.skin.button);
        buttonStyle.fontSize = 16;
        buttonStyle.fontStyle = FontStyle.Bold;
        buttonStyle.padding = new RectOffset(20, 20, 15, 15);

        GUI.enabled = !isProcessing;
        if (GUILayout.Button("แก้ทุกอย่างอัตโนมัติ", buttonStyle, GUILayout.Height(60)))
        {
            OneClickAutoFix();
        }
        GUI.enabled = true;
        GUI.backgroundColor = Color.white;

        EditorGUILayout.Space(10);

        // Progress Bar
        if (isProcessing)
        {
            EditorGUI.ProgressBar(EditorGUILayout.GetControlRect(GUILayout.Height(25)), progress, progressMessage);
            EditorGUILayout.Space(5);
        }
        else
        {
            // Info Box
            var infoStyle = new GUIStyle(EditorStyles.label);
            infoStyle.wordWrap = true;
            infoStyle.fontSize = 11;

            EditorGUILayout.LabelField("จะทำงาน:", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("  - สร้าง Rules ทุกเคส (562 คู่)", infoStyle);
            EditorGUILayout.LabelField("  - ปรับค่าอัตโนมัติตาม Font Size", infoStyle);
            EditorGUILayout.LabelField("  - Apply เข้า Font Asset", infoStyle);
            EditorGUILayout.LabelField("  - Refresh TextMeshPro ในScene", infoStyle);
        }

        EditorGUILayout.EndVertical();

        // Undo Button
        if (backupRecords != null && lastModifiedFont == fontAsset)
        {
            EditorGUILayout.Space(5);
            GUI.backgroundColor = new Color(1f, 0.7f, 0.3f); // สีส้ม
            if (GUILayout.Button("Undo Last Changes", GUILayout.Height(35)))
            {
                UndoLastChanges();
            }
            GUI.backgroundColor = Color.white;
        }
    }

    private async void OneClickAutoFix()
    {
        if (fontAsset == null) return;

        isProcessing = true;
        progress = 0f;

        try
        {
            // Backup current state
            BackupFontAsset();

            // Step 1: Generate All Rules
            progressMessage = "กำลังสร้าง Rules...";
            Repaint();
            await System.Threading.Tasks.Task.Delay(100);

            adjustmentRules.Clear();
            GenerateAllPresetsInternal();
            progress = 0.33f;
            Repaint();

            // Step 2: Apply to Font
            progressMessage = "กำลัง Apply เข้า Font Asset...";
            await System.Threading.Tasks.Task.Delay(100);

            ApplyAdjustmentsInternal();
            progress = 0.66f;
            Repaint();

            // Step 3: Refresh
            progressMessage = "กำลัง Refresh TextMeshPro...";
            await System.Threading.Tasks.Task.Delay(100);

            RefreshTextMeshProComponents(fontAsset);
            progress = 1f;
            progressMessage = "เสร็จสิ้น!";
            Repaint();

            await System.Threading.Tasks.Task.Delay(500);

            // Success Dialog
            int totalRules = fontAsset.fontFeatureTable.glyphPairAdjustmentRecords.Count;
            EditorUtility.DisplayDialog("สำเร็จ!",
                $"ปรับแต่งฟอนต์เสร็จสมบูรณ์!\n\n" +
                $"Rules ทั้งหมด: {totalRules} คู่\n" +
                $"พร้อมใช้งานแล้ว",
                "OK");
        }
        catch (System.Exception e)
        {
            EditorUtility.DisplayDialog("เกิดข้อผิดพลาด",
                $"ไม่สามารถดำเนินการได้\n\nError: {e.Message}",
                "OK");
            Debug.LogError($"[EasyThaiFontAdjustment] Error: {e}");
        }
        finally
        {
            isProcessing = false;
            progress = 0f;
            progressMessage = "";
            Repaint();
        }
    }

    private void BackupFontAsset()
    {
        if (fontAsset == null) return;

        backupRecords = new List<GlyphPairAdjustmentRecord>(
            fontAsset.fontFeatureTable.glyphPairAdjustmentRecords
        );
        lastModifiedFont = fontAsset;

        Debug.Log($"[EasyThaiFontAdjustment] Backup created: {backupRecords.Count} records");
    }

    private void UndoLastChanges()
    {
        if (fontAsset == null || backupRecords == null) return;

        if (EditorUtility.DisplayDialog("ยืนยันการ Undo",
            "คุณต้องการยกเลิกการเปลี่ยนแปลงล่าสุดใช่หรือไม่?",
            "Yes", "Cancel"))
        {
            Undo.RecordObject(fontAsset, "Undo Thai Font Adjustments");

            fontAsset.fontFeatureTable.glyphPairAdjustmentRecords.Clear();
            fontAsset.fontFeatureTable.glyphPairAdjustmentRecords.AddRange(backupRecords);

            EditorUtility.SetDirty(fontAsset);
            fontAsset.ReadFontAssetDefinition();
            TMPro_EventManager.ON_FONT_PROPERTY_CHANGED(true, fontAsset);

            RefreshTextMeshProComponents(fontAsset);

            backupRecords = null;
            lastModifiedFont = null;

            EditorUtility.DisplayDialog("สำเร็จ", "ยกเลิกการเปลี่ยนแปลงแล้ว", "OK");

            Debug.Log("[EasyThaiFontAdjustment] Undo completed");
        }
    }

    private void GenerateAllPresetsInternal()
    {
        // Same as GenerateAllPresets but without UI dialogs
        var config1 = presetConfigs["consonant_upper"];
        foreach (var consonant in allConsonants)
            foreach (var vowel in upperVowels)
                AddRule($"{consonant}+{vowel}", consonant, vowel, config1.FinalX, config1.FinalY, config1.name);

        var config2 = presetConfigs["consonant_tone"];
        foreach (var consonant in allConsonants)
            foreach (var tone in toneMarks)
                AddRule($"{consonant}+{tone}", consonant, tone, config2.FinalX, config2.FinalY, config2.name);

        var config3 = presetConfigs["consonant_thanthakhaat"];
        foreach (var consonant in allConsonants)
            AddRule($"{consonant}+์", consonant, thanThaKhaat, config3.FinalX, config3.FinalY, config3.name);

        var config4 = presetConfigs["upper_tone"];
        foreach (var vowel in upperVowels)
            foreach (var tone in toneMarks)
                AddRule($"{vowel}+{tone}", vowel, tone, config4.FinalX, config4.FinalY, config4.name);

        var config5 = presetConfigs["upper_thanthakhaat"];
        foreach (var vowel in upperVowels)
            AddRule($"{vowel}+์", vowel, thanThaKhaat, config5.FinalX, config5.FinalY, config5.name);

        var config6 = presetConfigs["sara_am_tone"];
        foreach (var tone in toneMarks)
            AddRule($"ำ+{tone}", saraAm, tone, config6.FinalX, config6.FinalY, config6.name);

        var config7 = presetConfigs["ascender_upper"];
        foreach (var consonant in ascenderConsonants)
        {
            foreach (var vowel in upperVowels)
                AddRule($"{consonant}+{vowel}", consonant, vowel, config7.FinalX, config7.FinalY, config7.name);
            foreach (var tone in toneMarks)
                AddRule($"{consonant}+{tone}", consonant, tone, config7.FinalX, config7.FinalY, config7.name);
            AddRule($"{consonant}+์", consonant, thanThaKhaat, config7.FinalX, config7.FinalY, config7.name);
        }

        var config8 = presetConfigs["descender_lower"];
        foreach (var consonant in descenderConsonants)
            foreach (var vowel in lowerVowels)
                AddRule($"{consonant}+{vowel}", consonant, vowel, config8.FinalX, config8.FinalY, config8.name);
    }

    private void ApplyAdjustmentsInternal()
    {
        // Same as ApplyAdjustments but without UI dialogs
        if (fontAsset == null) return;

        Undo.RecordObject(fontAsset, "Apply Thai Vowel Adjustments");

        foreach (var rule in adjustmentRules.Where(r => r.selected))
        {
            if (TryGetGlyphIndex(fontAsset, rule.firstChar, out uint firstGlyphIndex) &&
                TryGetGlyphIndex(fontAsset, rule.secondChar, out uint secondGlyphIndex))
            {
                AddOrUpdatePairAdjustment(firstGlyphIndex, secondGlyphIndex, rule.xPlacement, rule.yPlacement);
            }
        }

        EditorUtility.SetDirty(fontAsset);
        fontAsset.ReadFontAssetDefinition();
        TMPro_EventManager.ON_FONT_PROPERTY_CHANGED(true, fontAsset);
        UnityEditor.AssetDatabase.SaveAssets();
        UnityEditor.AssetDatabase.Refresh();
    }

    private void DrawPresetsTab()
    {
        EditorGUILayout.LabelField("เลือก Template ที่ต้องการ", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox("แต่ละ Template จะสร้างคู่อักขระทั้งหมดที่เป็นไปได้\nค่า Default คำนวณอัตโนมัติจาก Font Metrics", MessageType.Info);

        if (GUILayout.Button("Recalculate Default Values from Font", GUILayout.Height(30)))
        {
            InitializePresets();
            EditorUtility.DisplayDialog("Recalculated",
                $"คำนวณค่าใหม่จาก Font Metrics แล้ว!\n\n" +
                $"Base Adjustment: {CalculateBaseAdjustment()}\n" +
                $"Upper+Tone Adjustment: {CalculateUpperToneAdjustment()}\n" +
                $"Ascender Adjustment: {CalculateAscenderAdjustment()}",
                "OK");
        }

        EditorGUILayout.Space(10);

        scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);

        DrawPresetButton("consonant_upper",
            () => GenerateConsonantUpperVowelPairs(),
            allConsonants.Length * upperVowels.Length);

        DrawPresetButton("consonant_tone",
            () => GenerateConsonantToneMarkPairs(),
            allConsonants.Length * toneMarks.Length);

        DrawPresetButton("consonant_thanthakhaat",
            () => GenerateConsonantThanThaKhaatPairs(),
            allConsonants.Length);

        DrawPresetButton("upper_tone",
            () => GenerateUpperVowelToneMarkPairs(),
            upperVowels.Length * toneMarks.Length);

        DrawPresetButton("upper_thanthakhaat",
            () => GenerateUpperVowelThanThaKhaatPairs(),
            upperVowels.Length);

        DrawPresetButton("sara_am_tone",
            () => GenerateSaraAmToneMarkPairs(),
            toneMarks.Length);

        DrawPresetButton("ascender_upper",
            () => GenerateAscenderUpperGlyphPairs(),
            ascenderConsonants.Length * (upperVowels.Length + toneMarks.Length + 1));

        DrawPresetButton("descender_lower",
            () => GenerateDescenderLowerVowelPairs(),
            descenderConsonants.Length * lowerVowels.Length);

        EditorGUILayout.Space(10);
        DrawLine();
        EditorGUILayout.Space(10);

        // Generate All Button
        var allCount = CalculateTotalPossiblePairs();
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        EditorGUILayout.LabelField("สร้างทุกเคสที่เป็นไปได้", EditorStyles.boldLabel);
        EditorGUILayout.LabelField($"จะสร้าง {allCount:N0} คู่อักขระทั้งหมด", EditorStyles.miniLabel);

        if (GUILayout.Button("Generate All Templates", GUILayout.Height(40)))
        {
            if (EditorUtility.DisplayDialog("Confirmation",
                $"คุณต้องการสร้างทุก Template ({allCount:N0} คู่) ใช่หรือไม่?\n\nอาจใช้เวลาสักครู่",
                "Yes", "Cancel"))
            {
                GenerateAllPresets();
            }
        }
        EditorGUILayout.EndVertical();

        EditorGUILayout.EndScrollView();
    }

    private void DrawPresetButton(string key, System.Action generateAction, int pairCount)
    {
        if (!presetConfigs.ContainsKey(key)) return;

        var config = presetConfigs[key];

        EditorGUILayout.BeginVertical(EditorStyles.helpBox);

        // Header with color indicator
        EditorGUILayout.BeginHorizontal();
        var colorRect = EditorGUILayout.GetControlRect(GUILayout.Width(20), GUILayout.Height(20));
        EditorGUI.DrawRect(colorRect, config.color);

        config.expanded = EditorGUILayout.Foldout(config.expanded, config.name, true, EditorStyles.boldLabel);
        EditorGUILayout.EndHorizontal();

        if (config.expanded)
        {
            EditorGUI.indentLevel++;
            EditorGUILayout.LabelField(config.description, EditorStyles.wordWrappedLabel);
            EditorGUILayout.LabelField($"จำนวนคู่: {pairCount:N0} คู่", EditorStyles.miniLabel);

            // แสดงว่าคำนวณมาจากไหน
            if (key == "upper_tone" || key == "upper_thanthakhaat")
            {
                EditorGUILayout.HelpBox($"ค่านี้คำนวณจาก Point Size × 45% = {config.defaultY}", MessageType.None);
            }
            else if (key == "ascender_upper")
            {
                EditorGUILayout.HelpBox($"ค่านี้คำนวณจาก Ascender Height", MessageType.None);
            }
            else if (key.StartsWith("consonant") || key == "consonant_upper")
            {
                EditorGUILayout.HelpBox($"ค่านี้คำนวณจาก Point Size × 2%", MessageType.None);
            }

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Default X:", GUILayout.Width(70));
            EditorGUI.BeginChangeCheck();
            float newX = EditorGUILayout.FloatField(config.defaultX, GUILayout.Width(60));
            if (EditorGUI.EndChangeCheck())
            {
                config.defaultX = newX;
                Repaint();
            }

            EditorGUILayout.LabelField("Y:", GUILayout.Width(20));
            EditorGUI.BeginChangeCheck();
            float newY = EditorGUILayout.FloatField(config.defaultY, GUILayout.Width(60));
            if (EditorGUI.EndChangeCheck())
            {
                config.defaultY = newY;
                Repaint();
            }
            EditorGUILayout.EndHorizontal();

            // Offset fields
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Offset:", GUILayout.Width(80));
            EditorGUILayout.LabelField("X:", GUILayout.Width(20));
            EditorGUI.BeginChangeCheck();
            float newOffsetX = EditorGUILayout.FloatField(config.offsetX, GUILayout.Width(60));
            if (EditorGUI.EndChangeCheck())
            {
                config.offsetX = newOffsetX;
                Repaint();
            }

            EditorGUILayout.LabelField("Y:", GUILayout.Width(20));
            EditorGUI.BeginChangeCheck();
            float newOffsetY = EditorGUILayout.FloatField(config.offsetY, GUILayout.Width(60));
            if (EditorGUI.EndChangeCheck())
            {
                config.offsetY = newOffsetY;
                Repaint();
            }
            EditorGUILayout.EndHorizontal();

            // แสดงค่าสุดท้าย (Default + Offset)
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField($"Final: X={config.FinalX:F2}, Y={config.FinalY:F2}", EditorStyles.miniLabel);
            EditorGUILayout.EndHorizontal();

            if (GUILayout.Button($"Add {pairCount:N0} Rules", GUILayout.Height(30)))
            {
                generateAction();
            }

            EditorGUI.indentLevel--;
        }
        else
        {
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(25);
            EditorGUILayout.LabelField($"{pairCount:N0} คู่", EditorStyles.miniLabel, GUILayout.Width(80));
            if (GUILayout.Button($"Add", GUILayout.Height(25)))
            {
                generateAction();
            }
            EditorGUILayout.EndHorizontal();
        }

        EditorGUILayout.EndVertical();
        EditorGUILayout.Space(5);
    }

    private void DrawCustomTextTab()
    {
        EditorGUILayout.LabelField("วิเคราะห์จากข้อความ", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox("ใส่ข้อความที่มีสระลอย เครื่องมือจะวิเคราะห์และหาคู่อักขระให้", MessageType.Info);

        EditorGUILayout.Space(10);

        EditorGUILayout.LabelField("ข้อความตัวอย่าง:", EditorStyles.boldLabel);
        sampleText = EditorGUILayout.TextArea(sampleText, GUILayout.Height(150));

        EditorGUILayout.Space(10);

        if (GUILayout.Button("🔍 วิเคราะห์และเพิ่ม Rules", GUILayout.Height(40)))
        {
            ScanAndDetectPairs();
        }
    }

    private void DrawRulesTab()
    {
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField($"Rules ทั้งหมด: {adjustmentRules.Count} คู่", EditorStyles.boldLabel);

        if (adjustmentRules.Count > 0)
        {
            if (GUILayout.Button("Clear All", GUILayout.Width(80)))
            {
                if (EditorUtility.DisplayDialog("Confirmation", "ลบ Rules ทั้งหมด?", "Yes", "Cancel"))
                {
                    adjustmentRules.Clear();
                    Repaint();
                }
            }
        }
        EditorGUILayout.EndHorizontal();

        if (adjustmentRules.Count == 0)
        {
            EditorGUILayout.HelpBox("ยังไม่มี Rules\nกรุณาไปที่ Tab 'Templates' หรือ 'Custom' เพื่อเพิ่ม Rules", MessageType.Info);
            return;
        }

        EditorGUILayout.Space(5);

        // Group by category
        var categories = adjustmentRules.GroupBy(r => r.category).OrderBy(g => g.Key);

        // Toolbar for selection
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Select All", GUILayout.Width(80)))
        {
            foreach (var rule in adjustmentRules)
                rule.selected = true;
            Repaint();
        }
        if (GUILayout.Button("Deselect All", GUILayout.Width(100)))
        {
            foreach (var rule in adjustmentRules)
                rule.selected = false;
            Repaint();
        }
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space(5);

        scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition, GUILayout.ExpandHeight(true));

        foreach (var category in categories)
        {
            var categoryKey = string.IsNullOrEmpty(category.Key) ? "อื่นๆ" : category.Key;

            if (!categoryFoldouts.ContainsKey(categoryKey))
                categoryFoldouts[categoryKey] = true;

            var count = category.Count();
            var selectedCount = category.Count(r => r.selected);

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            EditorGUILayout.BeginHorizontal();
            categoryFoldouts[categoryKey] = EditorGUILayout.Foldout(
                categoryFoldouts[categoryKey],
                $"{categoryKey} ({selectedCount}/{count})",
                true,
                EditorStyles.boldLabel
            );
            EditorGUILayout.EndHorizontal();

            if (categoryFoldouts[categoryKey])
            {
                EditorGUI.indentLevel++;

                // Show ALL rules (แสดงทั้งหมดเพื่อให้แก้ไขได้ทุกคู่)
                foreach (var rule in category)
                {
                    DrawRuleItem(rule);
                }

                EditorGUI.indentLevel--;
            }

            EditorGUILayout.EndVertical();
            EditorGUILayout.Space(3);
        }

        EditorGUILayout.EndScrollView();

        // Apply Button
        EditorGUILayout.Space(5);
        DrawLine();
        EditorGUILayout.Space(5);

        var totalSelected = adjustmentRules.Count(r => r.selected);

        if (totalSelected > 0)
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField($"เลือกแล้ว: {totalSelected} คู่", EditorStyles.boldLabel);

            if (GUILayout.Button($"Apply to Font Asset ({totalSelected} คู่)", GUILayout.Height(50)))
            {
                ApplyAdjustments();
            }

            EditorGUILayout.Space(5);

            if (GUILayout.Button("Force Refresh TextMeshPro in Scene", GUILayout.Height(30)))
            {
                RefreshTextMeshProComponents(fontAsset);
                EditorUtility.DisplayDialog("Refreshed", "Force refresh TextMeshPro components ใน Scene แล้ว", "OK");
            }

            EditorGUILayout.EndVertical();
        }
        else
        {
            EditorGUILayout.HelpBox("กรุณาเลือก Rules ที่ต้องการ Apply", MessageType.Warning);
        }
    }

    private void DrawRuleItem(AdjustmentRule rule)
    {
        EditorGUILayout.BeginHorizontal();

        EditorGUI.BeginChangeCheck();
        bool newSelected = EditorGUILayout.Toggle(rule.selected, GUILayout.Width(20));
        if (EditorGUI.EndChangeCheck())
        {
            rule.selected = newSelected;
            Repaint();
        }

        var style = new GUIStyle(EditorStyles.label);
        style.fontSize = 14;
        EditorGUILayout.LabelField($"{rule.firstChar} + {rule.secondChar}", style, GUILayout.Width(60));

        GUILayout.FlexibleSpace();

        EditorGUILayout.LabelField("X:", GUILayout.Width(15));
        EditorGUI.BeginChangeCheck();
        float newX = EditorGUILayout.FloatField(rule.xPlacement, GUILayout.Width(50));
        if (EditorGUI.EndChangeCheck())
        {
            rule.xPlacement = newX;
            Repaint();
        }

        EditorGUILayout.LabelField("Y:", GUILayout.Width(15));
        EditorGUI.BeginChangeCheck();
        float newY = EditorGUILayout.FloatField(rule.yPlacement, GUILayout.Width(50));
        if (EditorGUI.EndChangeCheck())
        {
            rule.yPlacement = newY;
            Repaint();
        }

        EditorGUILayout.EndHorizontal();
    }

    // Generation Methods
    private void GenerateConsonantUpperVowelPairs()
    {
        var config = presetConfigs["consonant_upper"];
        int added = 0;

        foreach (var consonant in allConsonants)
        {
            foreach (var vowel in upperVowels)
            {
                if (AddRule($"{consonant}+{vowel}", consonant, vowel, config.FinalX, config.FinalY, config.name))
                    added++;
            }
        }

        EditorUtility.DisplayDialog("Success", $"เพิ่ม {added} Rules ใหม่", "OK");
        selectedTab = 2; // Switch to Rules tab
        Repaint();
    }

    private void GenerateConsonantToneMarkPairs()
    {
        var config = presetConfigs["consonant_tone"];
        int added = 0;

        foreach (var consonant in allConsonants)
        {
            foreach (var tone in toneMarks)
            {
                if (AddRule($"{consonant}+{tone}", consonant, tone, config.FinalX, config.FinalY, config.name))
                    added++;
            }
        }

        EditorUtility.DisplayDialog("Success", $"เพิ่ม {added} Rules ใหม่", "OK");
        selectedTab = 2;
        Repaint();
    }

    private void GenerateConsonantThanThaKhaatPairs()
    {
        var config = presetConfigs["consonant_thanthakhaat"];
        int added = 0;

        foreach (var consonant in allConsonants)
        {
            if (AddRule($"{consonant}+์", consonant, thanThaKhaat, config.FinalX, config.FinalY, config.name))
                added++;
        }

        EditorUtility.DisplayDialog("Success", $"เพิ่ม {added} Rules ใหม่", "OK");
        selectedTab = 2;
        Repaint();
    }

    private void GenerateUpperVowelToneMarkPairs()
    {
        var config = presetConfigs["upper_tone"];
        int added = 0;

        foreach (var vowel in upperVowels)
        {
            foreach (var tone in toneMarks)
            {
                if (AddRule($"{vowel}+{tone}", vowel, tone, config.FinalX, config.FinalY, config.name))
                    added++;
            }
        }

        EditorUtility.DisplayDialog("Success", $"เพิ่ม {added} Rules ใหม่", "OK");
        selectedTab = 2;
        Repaint();
    }

    private void GenerateUpperVowelThanThaKhaatPairs()
    {
        var config = presetConfigs["upper_thanthakhaat"];
        int added = 0;

        foreach (var vowel in upperVowels)
        {
            if (AddRule($"{vowel}+์", vowel, thanThaKhaat, config.FinalX, config.FinalY, config.name))
                added++;
        }

        EditorUtility.DisplayDialog("Success", $"เพิ่ม {added} Rules ใหม่", "OK");
        selectedTab = 2;
        Repaint();
    }

    private void GenerateSaraAmToneMarkPairs()
    {
        var config = presetConfigs["sara_am_tone"];
        int added = 0;

        foreach (var tone in toneMarks)
        {
            if (AddRule($"{saraAm}+{tone}", saraAm, tone, config.FinalX, config.FinalY, config.name))
                added++;
        }

        EditorUtility.DisplayDialog("Success", $"เพิ่ม {added} Rules สระอำ+วรรณยุกต์", "OK");
        selectedTab = 2;
        Repaint();
    }

    private void GenerateAscenderUpperGlyphPairs()
    {
        var config = presetConfigs["ascender_upper"];
        int added = 0;

        foreach (var consonant in ascenderConsonants)
        {
            foreach (var vowel in upperVowels)
            {
                if (AddRule($"{consonant}+{vowel}", consonant, vowel, config.FinalX, config.FinalY, config.name))
                    added++;
            }

            foreach (var tone in toneMarks)
            {
                if (AddRule($"{consonant}+{tone}", consonant, tone, config.FinalX, config.FinalY, config.name))
                    added++;
            }

            if (AddRule($"{consonant}+์", consonant, thanThaKhaat, config.FinalX, config.FinalY, config.name))
                added++;
        }

        EditorUtility.DisplayDialog("Success", $"เพิ่ม {added} Rules ใหม่", "OK");
        selectedTab = 2;
        Repaint();
    }

    private void GenerateDescenderLowerVowelPairs()
    {
        var config = presetConfigs["descender_lower"];
        int added = 0;

        foreach (var consonant in descenderConsonants)
        {
            foreach (var vowel in lowerVowels)
            {
                if (AddRule($"{consonant}+{vowel}", consonant, vowel, config.FinalX, config.FinalY, config.name))
                    added++;
            }
        }

        EditorUtility.DisplayDialog("Success", $"เพิ่ม {added} Rules ใหม่", "OK");
        selectedTab = 2;
        Repaint();
    }

    private void GenerateAllPresets()
    {
        int beforeCount = adjustmentRules.Count;

        var config1 = presetConfigs["consonant_upper"];
        foreach (var consonant in allConsonants)
            foreach (var vowel in upperVowels)
                AddRule($"{consonant}+{vowel}", consonant, vowel, config1.FinalX, config1.FinalY, config1.name);

        var config2 = presetConfigs["consonant_tone"];
        foreach (var consonant in allConsonants)
            foreach (var tone in toneMarks)
                AddRule($"{consonant}+{tone}", consonant, tone, config2.FinalX, config2.FinalY, config2.name);

        var config3 = presetConfigs["consonant_thanthakhaat"];
        foreach (var consonant in allConsonants)
            AddRule($"{consonant}+์", consonant, thanThaKhaat, config3.FinalX, config3.FinalY, config3.name);

        var config4 = presetConfigs["upper_tone"];
        foreach (var vowel in upperVowels)
            foreach (var tone in toneMarks)
                AddRule($"{vowel}+{tone}", vowel, tone, config4.FinalX, config4.FinalY, config4.name);

        var config5 = presetConfigs["upper_thanthakhaat"];
        foreach (var vowel in upperVowels)
            AddRule($"{vowel}+์", vowel, thanThaKhaat, config5.FinalX, config5.FinalY, config5.name);

        var config55 = presetConfigs["sara_am_tone"];
        foreach (var tone in toneMarks)
            AddRule($"{saraAm}+{tone}", saraAm, tone, config55.FinalX, config55.FinalY, config55.name);

        var config6 = presetConfigs["ascender_upper"];
        foreach (var consonant in ascenderConsonants)
        {
            foreach (var vowel in upperVowels)
                AddRule($"{consonant}+{vowel}", consonant, vowel, config6.FinalX, config6.FinalY, config6.name);
            foreach (var tone in toneMarks)
                AddRule($"{consonant}+{tone}", consonant, tone, config6.FinalX, config6.FinalY, config6.name);
            AddRule($"{consonant}+์", consonant, thanThaKhaat, config6.FinalX, config6.FinalY, config6.name);
        }

        var config7 = presetConfigs["descender_lower"];
        foreach (var consonant in descenderConsonants)
            foreach (var vowel in lowerVowels)
                AddRule($"{consonant}+{vowel}", consonant, vowel, config7.FinalX, config7.FinalY, config7.name);

        int addedCount = adjustmentRules.Count - beforeCount;

        EditorUtility.DisplayDialog("Success",
            $"สร้างทุก Templates สำเร็จ!\n\nเพิ่ม: {addedCount} Rules ใหม่\nรวมทั้งหมด: {adjustmentRules.Count} Rules",
            "OK");

        selectedTab = 2;
        Repaint();
    }

    private void ScanAndDetectPairs()
    {
        if (string.IsNullOrEmpty(sampleText))
        {
            EditorUtility.DisplayDialog("Error", "กรุณาใส่ข้อความตัวอย่าง", "OK");
            return;
        }

        var words = sampleText.Split(new[] { ' ', '\n', '\r', '\t' }, System.StringSplitOptions.RemoveEmptyEntries);
        int added = 0;

        foreach (var word in words)
        {
            for (int i = 0; i < word.Length - 1; i++)
            {
                char firstChar = word[i];
                char secondChar = word[i + 1];

                // Check if it's a valid pair
                if (IsValidThaiPair(firstChar, secondChar))
                {
                    if (AddRule($"{word}", firstChar, secondChar, 0f, -2f, "จากข้อความ"))
                        added++;
                }
            }
        }

        if (added == 0)
        {
            EditorUtility.DisplayDialog("No Pairs Found", "ไม่พบคู่อักขระที่ต้องปรับในข้อความที่ระบุ", "OK");
        }
        else
        {
            EditorUtility.DisplayDialog("Success", $"เพิ่ม {added} Rules ใหม่จากข้อความ", "OK");
            selectedTab = 2;
            Repaint();
        }
    }

    private bool IsValidThaiPair(char first, char second)
    {
        // Consonant + Upper Glyph
        if (IsThaiConsonant(first) && IsUpperGlyph(second))
            return true;

        // Upper Vowel + Tone Mark
        if (upperVowels.Contains(first) && (toneMarks.Contains(second) || second == thanThaKhaat))
            return true;

        // Consonant + Lower Vowel
        if (IsThaiConsonant(first) && lowerVowels.Contains(second))
            return true;

        return false;
    }

    private bool AddRule(string name, char first, char second, float x, float y, string category)
    {
        var key = $"{first}_{second}";

        // Check if rule already exists
        if (adjustmentRules.Any(r => r.Key == key))
            return false;

        adjustmentRules.Add(new AdjustmentRule(name, first, second, x, y, category));
        return true;
    }

    private int CalculateTotalPossiblePairs()
    {
        int total = 0;
        total += allConsonants.Length * upperVowels.Length;
        total += allConsonants.Length * toneMarks.Length;
        total += allConsonants.Length; // + ์
        total += upperVowels.Length * toneMarks.Length;
        total += upperVowels.Length; // + ์
        total += toneMarks.Length; // สระอำ + วรรณยุกต์
        total += ascenderConsonants.Length * (upperVowels.Length + toneMarks.Length + 1);
        total += descenderConsonants.Length * lowerVowels.Length;
        return total;
    }

    // Apply to Font Asset
    private void ApplyAdjustments()
    {
        if (fontAsset == null)
        {
            EditorUtility.DisplayDialog("Error", "กรุณาเลือก Font Asset", "OK");
            return;
        }

        Undo.RecordObject(fontAsset, "Apply Thai Vowel Adjustments");

        int addedCount = 0;
        int updatedCount = 0;
        int skippedCount = 0;

        foreach (var rule in adjustmentRules.Where(r => r.selected))
        {
            if (TryGetGlyphIndex(fontAsset, rule.firstChar, out uint firstGlyphIndex) &&
                TryGetGlyphIndex(fontAsset, rule.secondChar, out uint secondGlyphIndex))
            {
                bool isNew = AddOrUpdatePairAdjustment(firstGlyphIndex, secondGlyphIndex, rule.xPlacement, rule.yPlacement);

                Debug.Log($"[EasyThaiFontAdjustment] {(isNew ? "Added" : "Updated")} pair: {rule.firstChar} (#{firstGlyphIndex}) + {rule.secondChar} (#{secondGlyphIndex}) → X:{rule.xPlacement}, Y:{rule.yPlacement}");

                if (isNew)
                    addedCount++;
                else
                    updatedCount++;
            }
            else
            {
                skippedCount++;
                Debug.LogWarning($"[EasyThaiFontAdjustment] Skipped: {rule.firstChar} + {rule.secondChar} (glyph not found in font)");
            }
        }

        // Apply changes
        EditorUtility.SetDirty(fontAsset);

        // Force update the font asset
        fontAsset.ReadFontAssetDefinition();

        // Notify TextMeshPro that font changed
        TMPro_EventManager.ON_FONT_PROPERTY_CHANGED(true, fontAsset);

        // Force save assets
        UnityEditor.AssetDatabase.SaveAssets();
        UnityEditor.AssetDatabase.Refresh();

        // Force refresh all TextMeshPro components in scene
        RefreshTextMeshProComponents(fontAsset);

        // Verify a few pairs to make sure they were set correctly
        VerifyPairAdjustments();

        string message = $"สำเร็จ!\n\n";
        if (addedCount > 0)
            message += $"เพิ่มใหม่: {addedCount} คู่\n";
        if (updatedCount > 0)
            message += $"อัพเดท: {updatedCount} คู่\n";
        if (skippedCount > 0)
            message += $"ข้าม (ไม่มีใน Font): {skippedCount} คู่\n";
        message += $"\nรวมทั้งหมดใน Font Asset: {fontAsset.fontFeatureTable.glyphPairAdjustmentRecords.Count} คู่";

        EditorUtility.DisplayDialog("Success", message, "OK");

        Debug.Log($"Applied adjustments to {fontAsset.name}: {addedCount} new, {updatedCount} updated, {skippedCount} skipped");

        Repaint();
    }

    private void VerifyPairAdjustments()
    {
        var records = fontAsset.fontFeatureTable.glyphPairAdjustmentRecords;
        Debug.Log($"[EasyThaiFontAdjustment] Verifying {records.Count} pairs in font asset...");

        // แสดง 5 pairs แรก เพื่อตรวจสอบ
        for (int i = 0; i < Mathf.Min(5, records.Count); i++)
        {
            var record = records[i];
            var first = record.firstAdjustmentRecord;
            var second = record.secondAdjustmentRecord;

            Debug.Log($"  Pair #{i}: Glyph {first.glyphIndex} + Glyph {second.glyphIndex} → " +
                     $"Second placement: X={second.glyphValueRecord.xPlacement}, Y={second.glyphValueRecord.yPlacement}");
        }
    }

    private bool AddOrUpdatePairAdjustment(uint firstGlyphIndex, uint secondGlyphIndex, float xPlacement, float yPlacement)
    {
        var adjustmentRecords = fontAsset.fontFeatureTable.glyphPairAdjustmentRecords;

        // Check if pair already exists
        for (int i = 0; i < adjustmentRecords.Count; i++)
        {
            var record = adjustmentRecords[i];
            if (record.firstAdjustmentRecord.glyphIndex == firstGlyphIndex &&
                record.secondAdjustmentRecord.glyphIndex == secondGlyphIndex)
            {
                // Update existing - ต้องสร้าง struct ใหม่ทั้งหมด
                var firstValueRecord = new GlyphValueRecord
                {
                    xPlacement = 0,
                    yPlacement = 0,
                    xAdvance = 0,
                    yAdvance = 0
                };

                var secondValueRecord = new GlyphValueRecord
                {
                    xPlacement = xPlacement,
                    yPlacement = yPlacement,
                    xAdvance = 0,
                    yAdvance = 0
                };

                var newFirstRecord = new GlyphAdjustmentRecord(firstGlyphIndex, firstValueRecord);
                var newSecondRecord = new GlyphAdjustmentRecord(secondGlyphIndex, secondValueRecord);
                var newPairRecord = new GlyphPairAdjustmentRecord(newFirstRecord, newSecondRecord);

                adjustmentRecords[i] = newPairRecord;
                return false;
            }
        }

        // Add new - สร้างใหม่ด้วยค่าที่ถูกต้อง
        var firstValueRec = new GlyphValueRecord
        {
            xPlacement = 0,
            yPlacement = 0,
            xAdvance = 0,
            yAdvance = 0
        };

        var secondValueRec = new GlyphValueRecord
        {
            xPlacement = xPlacement,
            yPlacement = yPlacement,
            xAdvance = 0,
            yAdvance = 0
        };

        var firstAdjustment = new GlyphAdjustmentRecord(firstGlyphIndex, firstValueRec);
        var secondAdjustment = new GlyphAdjustmentRecord(secondGlyphIndex, secondValueRec);
        var newPair = new GlyphPairAdjustmentRecord(firstAdjustment, secondAdjustment);

        adjustmentRecords.Add(newPair);
        return true;
    }

    private bool TryGetGlyphIndex(TMP_FontAsset fontAsset, char character, out uint glyphIndex)
    {
        glyphIndex = 0;
        var characterData = fontAsset.characterTable.FirstOrDefault(c => c.unicode == character);

        if (characterData != null)
        {
            glyphIndex = characterData.glyphIndex;
            return true;
        }

        return false;
    }

    private bool IsThaiConsonant(char c)
    {
        return (c >= 'ก' && c <= 'ฮ');
    }

    private bool IsUpperGlyph(char c)
    {
        return upperVowels.Contains(c) || toneMarks.Contains(c) || c == thanThaKhaat;
    }

    private void RefreshTextMeshProComponents(TMP_FontAsset fontAsset)
    {
        // Refresh all TMP_Text components in scene that use this font
        var tmpTexts = GameObject.FindObjectsOfType<TMPro.TMP_Text>();
        int refreshedCount = 0;

        foreach (var tmpText in tmpTexts)
        {
            if (tmpText.font == fontAsset)
            {
                tmpText.ForceMeshUpdate(true, true);
                UnityEditor.EditorUtility.SetDirty(tmpText);
                refreshedCount++;
            }
        }

        if (refreshedCount > 0)
        {
            Debug.Log($"Refreshed {refreshedCount} TextMeshPro components in scene");
        }
    }

    private void DrawLine()
    {
        var rect = EditorGUILayout.GetControlRect(false, 1);
        EditorGUI.DrawRect(rect, new Color(0.5f, 0.5f, 0.5f, 0.5f));
    }
}
