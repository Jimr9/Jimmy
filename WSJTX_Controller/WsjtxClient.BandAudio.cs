using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading;
using WsjtxUdpLib.Messages.Out;

namespace WSJTX_Controller
{
    public partial class WsjtxClient
    {
        private void ClearAudioOffsets()
        {
            oddOffset = 0;
            evenOffset = 0;
            cachedOddOffset = 0;
            cachedEvenOffset = 0;
            period = Periods.UNK;
            DisableAutoFreqPause();
            skipFirstDecodeSeries = true;
            timeOffset = 0;
            analysisCompleted = false;
            pendingCqAfterAnalysis = false;
            DebugOutput($"{Time()} [BAND-AUDIT] ClearAudioOffsets: bandIdx:{bandIdx} skipFirstDecodeSeries:{skipFirstDecodeSeries} mode:'{mode}'");
        }

        private int? FreqToBandIdx(double? freq)            //null if unknown
        {
            if (freq == null) return null;
            if (freq >= 1.8 && freq <= 2.0) return 0;
            if (freq >= 3.5 && freq <= 4.0) return 1;
            if (freq >= 5.35 && freq <= 5.37) return 2;
            if (freq >= 7.0 && freq <= 7.3) return 3;
            if (freq >= 10.1 && freq <= 10.15) return 4;
            if (freq >= 14.0 && freq <= 14.35) return 5;
            if (freq >= 18.068 && freq <= 18.168) return 6;
            if (freq >= 21.0 && freq <= 21.45) return 7;
            if (freq >= 24.89 && freq <= 24.99) return 8;
            if (freq >= 28.0 && freq <= 29.7) return 9;
            if (freq >= 50.0 && freq <= 54.0) return 10;
            return null;
        }

        private string FreqToBandStr(double? freq)           //null if unknown
        {
            if (freq == null) return null;
            int? idx = FreqToBandIdx(freq);
            if (idx == null || (int)idx < 0 || !freqsDict.Keys.Contains(mode) || (int)idx >= freqsDict[mode].Count) return null;
            return $"{bands[(int)idx]}m";
        }

        private int? bandToFreq(int? idx)
        {
            if (idx == null || (int)idx < 0 || !freqsDict.Keys.Contains(mode) || (int)idx >= freqsDict[mode].Count) return null;
            return freqsDict[mode][(int)idx];
        }

        public bool BandUp()
        {
            if (!freqsDict.Keys.Contains(mode)) return false;
            if (bandIdx == null || (int)bandIdx >= freqsDict[mode].Count - 1) return false;
            int targetIdx = (int)bandIdx + 1;
            if (bandToFreq(targetIdx) == null) return false;

            ClearAudioOffsets();
            if (ctrl.freqCheckBox.Checked) _requireOffsetForActive = true;
            AutoFreqChanged(ctrl.freqCheckBox.Checked, true);
            Pause(true, false);
            CancelQso();

            DebugOutput($"{Time()} [BAND-AUDIT] BandUp: currentBandIdx:{bandIdx} targetIdx:{targetIdx} newFreq:{(uint)(bandToFreq(targetIdx) * 1000)} txFirst:{txFirst}");
            SetBandTxFirst((uint)(bandToFreq(targetIdx) * 1000), txFirst, "BandUp");
            ShowBandChangePending(targetIdx);
            return true;
        }

        public bool BandDown()
        {
            if (!freqsDict.Keys.Contains(mode)) return false;
            if (bandIdx == null || (int)bandIdx <= 0) return false;
            int targetIdx = (int)bandIdx - 1;
            if (bandToFreq(targetIdx) == null) return false;

            ClearAudioOffsets();
            if (ctrl.freqCheckBox.Checked) _requireOffsetForActive = true;
            AutoFreqChanged(ctrl.freqCheckBox.Checked, true);
            Pause(true, false);
            CancelQso();

            DebugOutput($"{Time()} [BAND-AUDIT] BandDown: currentBandIdx:{bandIdx} targetIdx:{targetIdx} newFreq:{(uint)(bandToFreq(targetIdx) * 1000)} txFirst:{txFirst}");
            SetBandTxFirst((uint)(bandToFreq(targetIdx) * 1000), txFirst, "BandDown");
            ShowBandChangePending(targetIdx);
            return true;
        }

        public bool SelectBand(int targetIdx)
        {
            if (!freqsDict.Keys.Contains(mode)) return false;
            if (targetIdx < 0 || targetIdx >= freqsDict[mode].Count) return false;
            if (bandToFreq(targetIdx) == null) return false;
            if (bandIdx != null && (int)bandIdx == targetIdx) return false;

            ClearAudioOffsets();
            if (ctrl.freqCheckBox.Checked) _requireOffsetForActive = true;
            AutoFreqChanged(ctrl.freqCheckBox.Checked, true);
            Pause(true, false);
            CancelQso();

            DebugOutput($"{Time()} [BAND-AUDIT] SelectBand: currentBandIdx:{bandIdx} targetIdx:{targetIdx} newFreq:{(uint)(bandToFreq(targetIdx) * 1000)} txFirst:{txFirst}");
            SetBandTxFirst((uint)(bandToFreq(targetIdx) * 1000), txFirst, "SelectBand");
            ShowBandChangePending(targetIdx);
            return true;
        }

        private void ShowBandChangePending(int targetIdx)
        {
            ctrl.statusText.ForeColor = Color.Black;
            ctrl.statusText.BackColor = Color.Yellow;
            ctrl.statusText.Text = $"Changing to {bands[targetIdx]} meter band...";
            ctrl.statusText.SelectionStart = 0;
        }

        public bool ReportPowerSwr()
        {
            GetPowerSwr();
            StartStatusTimer2(false);
            return true;
        }

        public bool ToggleTuningProcess()
        {
            if (!tuning && transmitting)
            {
                HaltTx();
                Thread.Sleep(500);
            }

            ToggleTuning();
            tuning = !tuning;

            if (!tuning) StartStatusTimer2(false);

            return true;
        }

        public bool AudioLevel(bool up)
        {
            if (!transmitting) return false;

            if (!tuning) StartStatusTimer2(false);

            AdjAudioLevel(up);
            return true;
        }

        private bool CalcBestOffset(List<int> offsetList, Periods decodePeriod, bool clearList)
        {
            DebugOutput($"{Time()} CalcBestOffset, decodePeriod:{decodePeriod} clearList:{clearList} offsetList.Count:{offsetList.Count()} skipFirstDecodeSeries:{skipFirstDecodeSeries}");

            if (period == Periods.UNK)
            {
                oddOffset = 0;
                evenOffset = 0;
                offsetList.Clear();
                timeOffset = 0;
                return false;
            }

            int bestOffset = 0;
            int maxInterval = 0;

            //set limits
            offsetList.Add(offsetLoLimit);
            offsetList.Add(offsetHiLimit);

            offsetList.Sort();
            int[] offsets = offsetList.ToArray();

            for (int i = 0; i < offsets.Length - 1; i++)
            {
                if (offsets[i + 1] - offsets[i] > maxInterval)
                {
                    maxInterval = offsets[i + 1] - offsets[i];
                    bestOffset = (offsets[i + 1] + offsets[i]) / 2;
                }
            }

            if (decodePeriod == Periods.EVEN)
            {
                evenOffset = bestOffset;
                if (bestOffset > 0) cachedEvenOffset = bestOffset;
            }
            else
            {
                oddOffset = bestOffset;
                if (bestOffset > 0) cachedOddOffset = bestOffset;
            }

            if (clearList) offsetList.Clear();

            DebugOutput($"{spacer}evenOffset:{evenOffset} oddOffset:{oddOffset}");

            bool bothKnown = oddOffset > 0 && evenOffset > 0;
            if (bothKnown) analysisCompleted = true;
            return bothKnown;
        }

        private UInt32 AudioOffsetFromMsg(EnqueueDecodeMessage msg)        //msg is a reply msg, so tx msg will be opposite time period
        {
            if (msg == null || !ctrl.freqCheckBox.Checked) return 0;

            if (IsEvenCall(msg))
            {
                return (UInt32)oddOffset;
            }
            else
            {
                return (UInt32)evenOffset;
            }
        }

        private UInt32 AudioOffsetFromTxPeriod()
        {
            if ((period == Periods.UNK || !ctrl.freqCheckBox.Checked))
                return 0;

            if (txFirst)
            {
                return (UInt32)evenOffset;
            }
            else
            {
                return (UInt32)oddOffset;
            }
        }

        private int CalcTimerAdj()
        {
            return (mode == "FT8" ? 150 /*300*/ : (mode == "FT4" ? 150 /*300*/ : (mode == "FST4" ? 750 : 300)));      //msec
        }

        private void UpdateBandComboBox()
        {
            int idx = ctrl.bandComboBox.SelectedIndex;
            ctrl.bandComboBox.Items.Clear();
            if (opMode == OpModes.ACTIVE)
            {
                string b = FreqToBandStr(dialFrequency / 1e6);
                if (b == null) b = "this band";
                ctrl.bandComboBox.Items.AddRange(new string[] { "for 1 band", $"for {b}" });
            }
            else
            {
                ctrl.bandComboBox.Items.AddRange(new string[] { "for 1 band", "this band" });
            }
            ctrl.bandComboBox.SelectedIndex = idx;
        }

        private void CalcAvgTimeOffset(bool clear)
        {
            timeOffset = 0;

            if (timeOffsets.Count == 0) return;

            foreach (double offset in timeOffsets)
            {
                timeOffset += offset;
            }
            timeOffset /= timeOffsets.Count;

            DebugOutput($"{Time()} CalcAvgTimeOffset, timeOffset:{timeOffset:F2} clear:{clear}");
            if (clear) timeOffsets.Clear();
        }
    }
}
