using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace CoreNgine.Shared
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

        public ReleaseInfo()
        {

        }

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

        public override string ToString()
        {
            return $"Доступна новая версия {Title} от {Date.ToShortDateString()}\r\nСписок изменений:\r\n{Changelog}";
        }

        public bool InvalidData => string.IsNullOrWhiteSpace(Url);
    }

    public class AutoUpdate
    {
        private string _checkUrl;
        private string _checkUrlBase;
        private string _checkUrlPathRegex;
        private readonly HttpClient _httpClient = new HttpClient();

        public Version CurrentVersion { get; set; }

        public ReleaseInfo LastRelease { get; private set; }

        public AutoUpdate(string checkUrl = "https://github.com/t3chn0pr13st/RcktMon/releases", string currentVersion = null)
        {
            if (Version.TryParse(currentVersion, out var ver))
                CurrentVersion = ver;
            else
                DetectCurrentVersion();

            SetCheckUrl(checkUrl);
        }

        private void DetectCurrentVersion()
        {
            Version currentVersion = Version.Parse("1.1.1");
            try
            {
                var currentVersionString = Process.GetCurrentProcess().MainModule.FileVersionInfo.ProductVersion;
                currentVersion = Version.Parse(currentVersionString);
            }
            catch
            {

            }

            CurrentVersion = currentVersion;
        }

        public bool InProgress { get; set; }

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
                _checkUrlBase = string.Join("/", _checkUrl.Split(new char[] { '/' }).Take(3));
                _checkUrlPathRegex = _checkUrl.Replace(_checkUrlBase, "").Replace(@"/", @"\/");
            }
            catch
            {

            }
        }

        public async Task InstallUpdate(ReleaseInfo relInfo = null, Action<string> statusCallback = null)
        {
            if (relInfo == null)
                relInfo = LastRelease;
            if (relInfo == null)
                throw new ArgumentException("ReleaseInfo is not set", "relInfo");

            await Task.Run(async () =>
            {
                var tempDir = $"{AppContext.BaseDirectory}Update";
                if (Directory.Exists(tempDir))
                    ClearFolder(tempDir);
                else
                    Directory.CreateDirectory(tempDir);

                var localZipPath = $"{tempDir}\\{Path.GetFileName(relInfo.Url)}";

                statusCallback?.Invoke("Загрузка архива...");

                using (var stream = await _httpClient.GetStreamAsync(relInfo.Url))
                {
                    using (var outStream = File.OpenWrite(localZipPath))
                    {
                        int read = 0;
                        byte[] buffer = new byte[8192];
                        while ((read = await stream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                        {
                            await outStream.WriteAsync(buffer, 0, read);
                        }
                    }
                }

                //await _httpClient.DownloadFileTaskAsync(relInfo.Url, localZipPath);

                statusCallback?.Invoke("Установка обновления...");

                ZipFile.ExtractToDirectory(localZipPath, tempDir);
                File.Delete(localZipPath);

                statusCallback?.Invoke("Ща будет перезапуск, но вы этого сообщения уже не увидите...");

                string args = "";
                if (Environment.CommandLine.Contains('/'))
                    args = "/" + String.Join(" /", Environment.CommandLine.Split('/').Skip(1));

                Process.Start(new ProcessStartInfo("cmd", @$"/c ping -n 2 127.0.0.1 && move /y * .. && cd .. && RcktMon.exe {args}")
                {
                    WorkingDirectory = tempDir,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden
                });

                Process.GetCurrentProcess().Kill();
            });
        }

        private void ClearFolder(string path)
        {
            DirectoryInfo dir = new DirectoryInfo(path);

            foreach (FileInfo fi in dir.GetFiles())
            {
                fi.Delete();
            }

            foreach (DirectoryInfo di in dir.GetDirectories())
            {
                ClearFolder(di.FullName);
                di.Delete();
            }
        }

        public async Task<ReleaseInfo> GetLastRelease()
        {
            var pageText = await _httpClient.GetStringAsync(_checkUrl);

            // <a href="/t3chn0pr13st/RcktMon/releases/tag/1.4.6">RcktMon v1.4.6</a>
            // <a href="\/t3chn0pr13st\/RcktMon\/releases\/tag\/(.*?)">(.*?)<\/a>
            string version = null, title = null, url = null, description = null, date = null;
            var m = Regex.Match(pageText, @$"<a href=""{_checkUrlPathRegex}\/tag\/(.*?)"".*?>(.*?)<\/a>");
            if (m.Success)
            {
                version = m.Groups[1].Value;
                title = m.Groups[2].Value;

                m = Regex.Match(pageText, @"markdown-body.*?>(.*?)<\/div>", RegexOptions.Singleline);
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

            LastRelease = relInfo;

            return relInfo;
        }
    }
}
