using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace WSJTX_Controller
{
    // Tracks which calls have already been logged for POTA today, backed by a simple
    // "call,date,band,mode" text file (pota.txt) so repeat-POTA-QSO detection survives
    // a Jimmy restart within the same day. Extracted from WsjtxClient (2026-07-09,
    // technique A of the WsjtxClient.cs modularization) -- its own state (the dict and
    // the file writer) has a small enough footprint outside these methods that it made
    // sense to actually own it here, unlike CallQueueStore/AwardTagger which read back
    // through WsjtxClient instead of owning their own state.
    public class PotaLogTracker
    {
        private readonly WsjtxClient _wc;
        private Dictionary<string, List<string>> potaLogDict = new Dictionary<string, List<string>>();      //calls logged for any mode/band for this day: "call: date,band,mode"
        private StreamWriter potaSw = null;

        public PotaLogTracker(WsjtxClient wc)
        {
            _wc = wc;
        }

        public string DictString()
        {
            StringBuilder sb = new StringBuilder();
            sb.Append($"{WsjtxClient.spacer}potaLogDict");
            if (potaLogDict.Count == 0)
            {
                sb.Append(" []");
            }
            else
            {
                sb.Append(":");
            }
            foreach (var entry in potaLogDict)
            {
                string delim = "";
                sb.Append($"{_wc.nl}{WsjtxClient.spacer}{entry.Key} [");
                foreach (var info in entry.Value)
                {
                    sb.Append($"{delim}{info}");
                    delim = "  ";
                }
                sb.Append("]");
            }

            return sb.ToString();
        }

        public bool TryGetValue(string call, out List<string> list) => potaLogDict.TryGetValue(call, out list);

        public void Read()
        {
            List<string> updList = new List<string>();
            string pathFileNameExt = $"{_wc.path}\\pota.txt";
            StreamReader potaSr = null;
            potaSw = null;
            potaLogDict.Clear();

            try
            {
                if (File.Exists(pathFileNameExt))
                {
                    string line = null;
                    string today = DateTime.Now.ToShortDateString();        //local time
                    potaSr = File.OpenText(pathFileNameExt);
                    _wc.DebugOutput($"{WsjtxClient.spacer}POTA log opened for read");

                    while ((line = potaSr.ReadLine()) != null)
                    {
                        string[] parts = line.Split(new char[] { ',' });   //call,date,band,mode
                        if (parts.Length == 4 && parts[1] == today)
                        {                       //date     band       mode
                            string potaInfo = $"{parts[1]},{parts[2]},{parts[3]}";
                            List<string> curList;
                            //                          call
                            if (potaLogDict.TryGetValue(parts[0], out curList))
                            {
                                if (!curList.Contains(potaInfo)) curList.Add(potaInfo);
                            }
                            else
                            {
                                List<string> newList = new List<string>();
                                newList.Add(potaInfo);
                                //              call
                                potaLogDict.Add(parts[0], newList);
                            }

                            updList.Add(line);
                        }
                    }
                    potaSr.Close();
                }
            }
            catch (Exception err)
            {
                _wc.DebugOutput($"{WsjtxClient.spacer}POTA log open/read failed: {err.ToString()}");
                if (potaSr != null) potaSr.Close();
                return;
            }

            //open, re-write updated file; leave file open if no error
            try
            {
                if (File.Exists(pathFileNameExt)) File.Delete(pathFileNameExt);
                if (!Directory.Exists(_wc.path)) Directory.CreateDirectory(_wc.path);
                potaSw = File.AppendText(pathFileNameExt);
                potaSw.AutoFlush = true;
                _wc.DebugOutput($"{WsjtxClient.spacer}POTA log opened for write");

                foreach (string line in updList)
                {
                    potaSw.WriteLine(line);
                }
            }
            catch (Exception err)
            {
                _wc.DebugOutput($"{WsjtxClient.spacer}POTA log open/rewrite failed: {err.ToString()}");
                potaSw = null;
            }
            _wc.DebugOutput($"{DictString()}");
        }

        public void Add(string potaCall, DateTime potaDtLocal, string potaBand, string potaMode)     //UTC
        {
            bool updateLog = false;

            string potaInfo = $"{potaDtLocal.Date.ToShortDateString()},{potaBand},{potaMode}";
            _wc.DebugOutput($"{WsjtxClient.spacer}AddPotaLogDict, potaInfo:{potaInfo}");
            _wc.DebugOutput($"{DictString()}");
            List<string> curList;
            if (potaLogDict.TryGetValue(potaCall, out curList))
            {
                if (!curList.Contains(potaInfo))
                {
                    curList.Add(potaInfo);
                    updateLog = true;
                }
            }
            else
            {
                List<string> newList = new List<string>();
                newList.Add(potaInfo);
                potaLogDict.Add(potaCall, newList);
                updateLog = true;
            }

            if (potaSw != null && updateLog)
            {
                potaSw.WriteLine($"{potaCall},{potaInfo}");
                _wc.DebugOutput($"{DictString()}");
            }
        }

        public void Close()
        {
            if (potaSw != null)
            {
                potaSw.Flush();
                potaSw.Close();
                potaSw = null;
            }
        }
    }
}
