using System;

namespace WSJTX_Controller
{
    public class CallsignLookupResult
    {
        public string    Callsign         { get; set; }
        public string    Country          { get; set; }
        public string    State            { get; set; }
        public string    Grid             { get; set; }
        public string    Continent        { get; set; }
        public string    Name             { get; set; }
        public string    County           { get; set; }
        public int       CqZone           { get; set; }
        public int       ItuZone          { get; set; }
        public string    QslManager       { get; set; }
        public string    Email            { get; set; }
        public bool      IsLoTWUser       { get; set; }
        public DateTime? LoTWLastActivity { get; set; }
    }
}
