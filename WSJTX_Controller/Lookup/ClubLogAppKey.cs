using System;
using System.IO;

namespace WSJTX_Controller
{
    // Club Log issues one API key per registered application, not per user --
    // every Jimmy installation shares the same key, and end users never see or
    // enter it (there is no field for it in Options). The key must never be
    // stored in source code or committed to the repo, so it is resolved at
    // runtime, outside version control, using the first of these that's set:
    //
    //   1. Environment variable JIMMY_CLUBLOG_APIKEY -- the key value itself.
    //      Simplest option; no file paths involved at all.
    //   2. Environment variable JIMMY_CLUBLOG_KEYFILE -- full path to a local
    //      text file whose entire (trimmed) contents are the key. Useful if
    //      you'd rather keep the key in a file than an environment variable.
    //   3. %LocalAppData%\Jimmy\clublog_key.txt -- a per-user fallback file
    //      location so a maintainer can drop the key in place with no
    //      environment configuration at all. This path is computed at
    //      runtime (Environment.SpecialFolder.LocalApplicationData), so no
    //      machine-specific directory ever appears in source.
    //
    // If none of these resolve (any machine other than a maintainer's own,
    // including every end user's install), Resolve() returns "" and Club Log
    // country lookup simply stays unavailable -- nothing else in Jimmy
    // depends on it.
    internal static class ClubLogAppKey
    {
        private const string EnvKeyVar     = "JIMMY_CLUBLOG_APIKEY";
        private const string EnvKeyFileVar = "JIMMY_CLUBLOG_KEYFILE";
        private const string FallbackFileName = "clublog_key.txt";

        public static string Resolve()
        {
            try
            {
                string direct = Environment.GetEnvironmentVariable(EnvKeyVar);
                if (!string.IsNullOrWhiteSpace(direct)) return direct.Trim();

                string keyFile = Environment.GetEnvironmentVariable(EnvKeyFileVar);
                if (!string.IsNullOrWhiteSpace(keyFile) && File.Exists(keyFile))
                    return File.ReadAllText(keyFile).Trim();

                string fallback = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Jimmy", FallbackFileName);
                if (File.Exists(fallback))
                    return File.ReadAllText(fallback).Trim();

                return "";
            }
            catch
            {
                return "";
            }
        }
    }
}
