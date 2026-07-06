using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace WSJTX_Controller
{
    // Note: List<string>, not IReadOnlyList<string> -- matches exactly what WsjtxClient
    // already builds in memory before touching the UI, avoiding a pointless conversion.
    // View seams (Phase 2.3 of the modernization plan) letting WsjtxClient stop holding a raw
    // `public Controller ctrl` reference. Controller implements these; WsjtxClient is being
    // migrated call-site-by-call-site onto them (Phase 2.4), one bounded, well-understood
    // render method at a time. The Advanced Call Layout TX1/TX2 snapshot lists (distinct from
    // Raw Decodes) are deliberately left on the old direct-ctrl path for now -- they carry
    // extra period/snapshot bookkeeping that deserves its own dedicated wave.

    public interface IJimmyStatusView
    {
        // Mirrors WsjtxClient.ShowStatus()'s former `finally` block exactly, including the
        // SendKeys.Send("{UP}") screen-reader re-announce guard -- an accessibility-load-bearing
        // detail, not a cosmetic one, so it is preserved verbatim rather than "improved" here.
        void RenderStatus(string headingText, string statusText, Color foreColor, Color backColor);

        // Wraps the existing (currently no-op) Controller.ShowMsg.
        void ShowMessage(string text, bool sound);
    }

    public interface IJimmyQueueView
    {
        // Mirrors WsjtxClient.ShowQueue()'s list-rendering tail: change-detection, focus/selection
        // preservation, and BeginUpdate/EndUpdate batching. Queue-index bookkeeping
        // (_callListBoxQueueIndices) stays in WsjtxClient -- it's queue state, not view state.
        // `keys` is parallel to `items` (same order/count) and identifies each row's station so
        // selection can be preserved by identity across a rebuild instead of by raw position --
        // see Controller.FindPreservedSelectionIndex().
        void RenderCallQueue(string headerText, List<string> items, List<string> keys, SelectionMode selectionMode);

        // Mirrors WsjtxClient.ShowRawDecodes()'s list-rendering tail (advRawListBox). No header
        // label update here -- the raw decodes panel has none, unlike the call queue. `keys` is
        // parallel to `items`; since the same callsign can appear in several distinct rows (CQ,
        // reply, report, ...), each key must disambiguate the specific decode, not just the call.
        void RenderRawDecodes(List<string> items, List<string> keys);

        // Mirrors WsjtxClient.ShowAdvancedQueue()'s per-side tail (advTx1ListBox/advTx2ListBox):
        // AccessibleName update (only when the call count actually changed), then the same
        // change-detection + BeginUpdate/EndUpdate + focus/selection-preservation shape as the
        // other Render* methods here. `keys` is parallel to `items` (the callsign for each row).
        void RenderAdvancedList(bool isTx1Side, string accessibleName, List<string> items, List<string> keys);
    }

    public interface IJimmyLogView
    {
        // Mirrors WsjtxClient.ShowLogged()'s list-rendering tail (same shape as RenderCallQueue,
        // without the queue-index bookkeeping since the logged list has no queue positions).
        // `keys` is parallel to `items` (the callsign for each row).
        void RenderLoggedList(string headerText, List<string> items, List<string> keys);
    }
}
