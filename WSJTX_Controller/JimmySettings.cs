using System.Drawing;

namespace WSJTX_Controller
{
    // First slice of settings/INI extraction (Phase 2 of the modernization plan):
    // the Advanced Call Layout display flags, modeled on HotkeyConfig.cs's
    // LoadFromIni/SaveToIni pattern. Deliberately scoped small -- Controller.Form_Load
    // has ~270 more interleaved settings reads (plus a legacy Properties.Settings.Default
    // migration path) that stay inline for now rather than risk a single large,
    // hard-to-review diff. See the modernization plan doc for the full rationale.
    //
    // LoadFromIni intentionally preserves an existing quirk rather than "fixing" it:
    // AdvancedCallLayout reads as `== "True"` (missing/empty key -> false) while the
    // other three read as `!= "False"` (missing/empty key -> true) -- this mismatch
    // already existed in Controller.Form_Load and is preserved here byte-for-byte
    // rather than silently changed as part of a refactor.
    public class JimmySettings
    {
        public bool AdvancedCallLayout { get; set; } = true;
        public bool AdvShowTx1 { get; set; } = true;
        public bool AdvShowTx2 { get; set; } = true;
        public bool AdvShowRaw { get; set; } = true;

        // Appearance (list font size + colors) -- defaults match the app's original
        // hardcoded look exactly, so nothing changes for anyone who never opens the
        // new Appearance tab. Colors are stored as ARGB ints (unambiguous, no named-
        // color parsing needed) rather than as strings.
        public int ListFontSize { get; set; } = 10;
        public Color ListBackColor { get; set; } = SystemColors.Window;
        public Color ListForeColor { get; set; } = SystemColors.WindowText;
        public Color ListAltRowColor { get; set; } = Color.FromArgb(233, 233, 233);

        public void LoadFromIni(IniFile ini)
        {
            AdvancedCallLayout = ini.Read("advCallLayout") == "True";
            AdvShowTx1 = ini.Read("advShowTx1") != "False";
            AdvShowTx2 = ini.Read("advShowTx2") != "False";
            AdvShowRaw = ini.Read("advShowRaw") != "False";

            if (int.TryParse(ini.Read("listFontSize"), out int fontSize) && fontSize >= 8 && fontSize <= 18)
                ListFontSize = fontSize;
            ListBackColor = ReadColor(ini, "listBackColor", ListBackColor);
            ListForeColor = ReadColor(ini, "listForeColor", ListForeColor);
            ListAltRowColor = ReadColor(ini, "listAltRowColor", ListAltRowColor);
        }

        public void SaveToIni(IniFile ini)
        {
            ini.Write("advCallLayout", AdvancedCallLayout.ToString());
            ini.Write("advShowTx1", AdvShowTx1.ToString());
            ini.Write("advShowTx2", AdvShowTx2.ToString());
            ini.Write("advShowRaw", AdvShowRaw.ToString());

            ini.Write("listFontSize", ListFontSize.ToString());
            ini.Write("listBackColor", ListBackColor.ToArgb().ToString());
            ini.Write("listForeColor", ListForeColor.ToArgb().ToString());
            ini.Write("listAltRowColor", ListAltRowColor.ToArgb().ToString());
        }

        private static Color ReadColor(IniFile ini, string key, Color fallback)
        {
            string raw = ini.Read(key);
            return int.TryParse(raw, out int argb) ? Color.FromArgb(argb) : fallback;
        }
    }
}
