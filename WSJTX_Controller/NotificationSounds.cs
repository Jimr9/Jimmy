using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace WSJTX_Controller
{
    // Sound playback subsystem, extracted from WsjtxClient (Phase 2.5 of the modernization
    // plan). Bodies moved verbatim -- this was already almost fully self-contained; the only
    // external dependency was Controller.soundsEnabled (the master mute flag), now passed in
    // as a Func<bool> so this class has no reference to Controller/WinForms at all.
    public class NotificationSounds
    {
        private readonly Func<bool> _soundsEnabled;
        private readonly Queue<string> _soundQueue = new Queue<string>();

        [DllImport("winmm.dll", SetLastError = true)]
        static extern bool PlaySound(string pszSound, UIntPtr hmod, uint fdwSound);

        [Flags]
        private enum SoundFlags
        {
            /// <summary>play synchronously (default)</summary>
            SND_SYNC = 0x0000,
            /// <summary>play asynchronously</summary>
            SND_ASYNC = 0x0001,
            /// <summary>silence (!default) if sound not found</summary>
            SND_NODEFAULT = 0x0002,
            /// <summary>pszSound points to a memory file</summary>
            SND_MEMORY = 0x0004,
            /// <summary>loop the sound until next sndPlaySound</summary>
            SND_LOOP = 0x0008,
            /// <summary>don't stop any currently playing sound</summary>
            SND_NOSTOP = 0x0010,
            /// <summary>Stop Playing Wave</summary>
            SND_PURGE = 0x40,
            /// <summary>don't wait if the driver is busy</summary>
            SND_NOWAIT = 0x00002000,
            /// <summary>name is a registry alias</summary>
            SND_ALIAS = 0x00010000,
            /// <summary>alias is a predefined id</summary>
            SND_ALIAS_ID = 0x00110000,
            /// <summary>name is file name</summary>
            SND_FILENAME = 0x00020000,
            /// <summary>name is resource name or atom</summary>
            SND_RESOURCE = 0x00040004
        }

        // Filenames present in Resources/, refreshed at startup and whenever Options
        // closes -- kept in memory so every sound lookup is a HashSet check, not a disk
        // hit, even when trying several drop-in-file candidates per alert.
        private HashSet<string> _resourceFileNames;
        private string _resourceDir;

        public NotificationSounds(Func<bool> soundsEnabled)
        {
            _soundsEnabled = soundsEnabled;
            RefreshResourceFileCache();
            Task task = new Task(new Action(ProcSoundQueue));
            task.Start();
        }

        public void RefreshResourceFileCache()
        {
            try
            {
                _resourceDir = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), "Resources");
                _resourceFileNames = Directory.Exists(_resourceDir)
                    ? new HashSet<string>(Directory.GetFiles(_resourceDir).Select(Path.GetFileName), StringComparer.OrdinalIgnoreCase)
                    : new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            }
            catch
            {
                _resourceDir = null;
                _resourceFileNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            }
        }

        // Tries, in order: a callsign-specific file (e.g. KG4CCG.wav), a rule/category-key
        // -specific file (e.g. WAS.wav, NEW_COUNTRY.wav), then the file configured in
        // Options -- the only behavior that existed before this. All three live in the same
        // Resources folder already used for configured sounds; no new folder, no new
        // Options UI needed for the first two.
        private string ResolveSoundPath(string name, string callsign, string key)
        {
            if (_resourceFileNames == null) RefreshResourceFileCache();

            if (!string.IsNullOrEmpty(callsign))
            {
                string candidate = SanitizeSoundFileName(callsign) + ".wav";
                if (_resourceFileNames.Contains(candidate)) return Path.Combine(_resourceDir, candidate);
            }
            if (!string.IsNullOrEmpty(key))
            {
                string candidate = SanitizeSoundFileName(key) + ".wav";
                if (_resourceFileNames.Contains(candidate)) return Path.Combine(_resourceDir, candidate);
            }

            if (string.IsNullOrEmpty(name)) return null;
            if (Path.IsPathRooted(name))
                return File.Exists(name) ? name : null;
            return _resourceFileNames.Contains(name) ? Path.Combine(_resourceDir, name) : null;
        }

        private static string SanitizeSoundFileName(string s)
        {
            foreach (char c in Path.GetInvalidFileNameChars())
                s = s.Replace(c, '_');
            return s;
        }

        public void Play(string strFileName)
        {
            Play(strFileName, null, null);
        }

        public void Play(string strFileName, string callsign, string key)
        {
            string resolved = ResolveSoundPath(strFileName, callsign, key);
            if (resolved != null) _soundQueue.Enqueue(resolved);
        }

        public bool PlaySoundEvent(bool enabled, string file)
        {
            return PlaySoundEvent(enabled, file, null, null);
        }

        // callsign/key let a more specific drop-in sound file win over the configured
        // default -- see ResolveSoundPath. Returns whether a sound actually resolved and
        // was queued (not merely whether one was nominally configured), so callers that
        // fall back to a generic sound when this returns false never go silent just
        // because a configured file went missing.
        public bool PlaySoundEvent(bool enabled, string file, string callsign, string key)
        {
            if (!_soundsEnabled() || !enabled) return false;
            string resolved = ResolveSoundPath(file, callsign, key);
            if (resolved == null) return false;
            _soundQueue.Enqueue(resolved);
            return true;
        }

        public void TestPlaySound(string file)
        {
            if (string.IsNullOrEmpty(file)) return;
            Play(file);
        }

        private void ProcSoundQueue()
        {
            while (true)
            {
                if (_soundQueue.Count > 0)
                {
                    string waveFileName = _soundQueue.Peek();
                    _soundQueue.Dequeue();
                    if (!string.IsNullOrEmpty(waveFileName))
                    {
                        PlaySound(waveFileName, UIntPtr.Zero,
                            (uint)(SoundFlags.SND_FILENAME | SoundFlags.SND_ASYNC | SoundFlags.SND_NODEFAULT));
                        string baseName = Path.GetFileNameWithoutExtension(waveFileName).ToLower();
                        if (baseName == "beepbeep" || baseName == "blip")
                            Thread.Sleep(200);
                        else
                            Thread.Sleep(650);
                    }
                }

                Thread.Sleep(100);
            }
        }
    }
}
