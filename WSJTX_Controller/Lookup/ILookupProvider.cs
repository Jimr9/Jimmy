namespace WSJTX_Controller
{
    // Contract every lookup provider (QRZ, Club Log, LoTW, DX Spot Watch, and any
    // future service -- eQSL, HamQTH, DX Cluster, Club Log Most Wanted, ...)
    // implements so LookupManager can merge them into one LookupRecord without
    // knowing anything provider-specific.
    //
    // Contribute() runs on the per-decode hot path (WsjtxClient.BuildCallWaitingRow
    // and MatchedAwardRuleId are called once per decoded line, many times per
    // FT8/FT4 cycle) -- implementations must be synchronous, cache-only, and never
    // perform network I/O. Only set fields you actually know; leave the rest of
    // the record untouched so an earlier, more specific provider's answer isn't
    // clobbered. Any async/network operations (live queries, scheduled refresh)
    // are provider-specific and stay outside this interface.
    public interface ILookupProvider
    {
        string SourceName { get; }
        bool   IsEnabled  { get; }

        void Contribute(LookupRecord record, string call);
    }
}
