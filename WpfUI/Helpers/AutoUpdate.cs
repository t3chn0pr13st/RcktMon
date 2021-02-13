using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace RcktMon.Helpers
{
    public class ReleaseInfo
    {
        public string Title { get; set; }
        public Version Version { get; set; }
        public string Url { get; set; }
        public string Changelog { get; set; }
        public DateTime Date { get; set; }
        public string DetailsUrl { get; set; }
        public bool IsNewer { get; set; }

        public ReleaseInfo(string title, string version, string url, string description, string date)
        {
            Title = title;
            if (version != null && Version.TryParse(version, out var ver))
                Version = ver;
            if (date != null && DateTime.TryParse(date, out var parsedDate))
                Date = parsedDate;
            Url = url;
            Changelog = description;
        }

        public bool InvalidData => String.IsNullOrWhiteSpace(Url);
    }

    public class AutoUpdate
    {
        private string _checkUrl;
        private string _checkUrlBase;
        private string _checkUrlPathRegex;
        private readonly WebClient _webClient = new WebClient();

        public Version CurrentVersion { get; }

        public AutoUpdate(string checkUrl = "https://github.com/t3chn0pr13st/RcktMon/releases", string currentVersion = null)
        {
            if (Version.TryParse(currentVersion, out var ver))
                CurrentVersion = ver;
            SetCheckUrl(checkUrl);
        }

        public string CheckUrl
        {
            get => _checkUrl;
            set
            {
                SetCheckUrl(value);
            }
        }

        private void SetCheckUrl(string checkUrl)
        {
            _checkUrl = checkUrl;
            try
            {
                _checkUrlBase = String.Join('/', _checkUrl.Split(new char[] {'/'}).Take(3));
                _checkUrlPathRegex = _checkUrl.Replace(_checkUrlBase, "").Replace(@"/", @"\/");
            }
            catch
            {

            }
        }

        public async Task<ReleaseInfo> GetLastRelease()
        {
            var pageText = await _webClient.DownloadStringTaskAsync(_checkUrl);
            
            // <a href="/t3chn0pr13st/RcktMon/releases/tag/1.4.6">RcktMon v1.4.6</a>
            // <a href="\/t3chn0pr13st\/RcktMon\/releases\/tag\/(.*?)">(.*?)<\/a>
            string version = null, title = null, url = null, description = null, date = null;
            var m = Regex.Match(pageText, @$"<a href=""{_checkUrlPathRegex}\/tag\/(.*?)"">(.*?)<\/a>");
            if (m.Success)
            {
                version = m.Groups[1].Value;
                title = m.Groups[2].Value;

                m = Regex.Match(pageText, @"<div class=""markdown-body"">(.*?)<\/div>", RegexOptions.Singleline);
                if (m.Success)
                    description = m.Groups[1].Value
                        .Replace("<p>", "")
                        .Replace("</p>", "")
                        .Replace("\n", "")
                        .Replace("<br>", "\r\n")
                        .Trim();
                // <a href="/t3chn0pr13st/RcktMon/releases/download/1.4.6/RcktMon-1.4.6.zip" rel="nofollow" class="d-flex flex-items-center min-width-0">
                // <a href="(\/t3chn0pr13st\/RcktMon\/releases\/download\/.*?)" rel="nofollow"
                m = Regex.Match(pageText, @$"<a href=""({_checkUrlPathRegex}\/download\/.*?)"" rel=""nofollow""");
                if (m.Success)
                    url = _checkUrlBase + m.Groups[1].Value;
                m = Regex.Match(pageText, @"<relative-time datetime=""(.*?)""");
                if (m.Success)
                    date = m.Groups[1].Value;
            }

            var relInfo = new ReleaseInfo(title, version, url, description, date)
            {
                DetailsUrl = _checkUrl
            };

            if (!relInfo.InvalidData)
            {
                if (relInfo.Version != null && CurrentVersion != null && relInfo.Version > CurrentVersion)
                    relInfo.IsNewer = true;
            }

            return relInfo;
        }
    }
}
