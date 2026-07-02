using System.Collections.Generic;

namespace WSJTX_Controller
{
    // Holds the Rule Definitions loaded at startup. Load() is safe to call again
    // later (e.g. a future "Reload Rules" action) to pick up newly-added or
    // edited files without restarting Jimmy.
    public static class RuleLibrary
    {
        public static List<RuleDefinition> Definitions { get; private set; } = new List<RuleDefinition>();
        public static List<string>         LoadErrors  { get; private set; } = new List<string>();
        public static bool                 Loaded      { get; private set; }

        // Set once at startup (Controller.cs) so RuleEngine can resolve
        // Club Log-backed universes (DXCC_CURRENT etc.) without every caller
        // having to thread a provider reference through. May be null (e.g. in
        // unit tests) -- Club Log-backed universes just report "unavailable".
        public static ClubLogProvider ClubLog { get; set; }

        public static void Load()
        {
            var result = RuleLoader.LoadAll();
            Definitions = result.Definitions;
            LoadErrors  = result.Errors;
            Loaded      = true;
        }
    }
}
