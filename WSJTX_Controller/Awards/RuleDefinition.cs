using System.Collections.Generic;

namespace WSJTX_Controller
{
    // What a QSO gets grouped by when checking off units of an award (e.g. "one
    // entry per DXCC entity"). None means the award is a simple QSO count with no
    // grouping at all.
    public enum RuleGroupBy
    {
        None, Dxcc, Country, State, CqZone, ItuZone, Continent, County,
        Grid, Grid4, Iota, Prefix, Callsign, SigInfo, DarcDok
    }

    // Which QSL source(s) count as "confirmed". None means the award doesn't
    // require confirmation at all -- completion is judged on worked QSOs.
    public enum RuleConfirmation { Any, Lotw, Qrz, Both, None }

    public enum RuleTargetType { All, Count, Levels }

    public class RuleLevel
    {
        public string Name;
        public int    Threshold;
    }

    public class RuleEndorsements
    {
        public List<string> Bands = new List<string>();
        public List<string> Modes = new List<string>();
    }

    public class RuleDefinition
    {
        public string Id;
        public string Name;
        public string Sponsor;
        public string Category;
        public int    FormatVersion;
        public bool   Enabled = true;
        public string Description;
        public string Website;

        public RuleGroupBy  GroupBy;
        public string       Universe;   // e.g. "US_50_STATES", "File:na_dxcc.txt"; raw as written
        public string       LimitTo;    // optional: restrict counted values to this universe (e.g. "only NA entities")
        public List<string> Bands = new List<string>();
        public List<string> Modes = new List<string>();
        public string        CallsignPattern;
        public string        Sig;        // optional exact filter on the SIG column (for GroupBy=SigInfo)
        public string        DateFrom;   // yyyy-MM-dd
        public string        DateTo;

        public RuleConfirmation Confirmation = RuleConfirmation.Any;

        public RuleTargetType  Target;
        public int              Threshold;              // Target=Count
        public List<RuleLevel>  Levels = new List<RuleLevel>();  // Target=Levels, ascending by Threshold

        public RuleEndorsements Endorsements;    // null if the file has no [Endorsements] section

        // Set by the loader; not read from the file.
        public string SourceFile;
    }
}
