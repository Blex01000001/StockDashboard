using System.Diagnostics;

namespace StockDashboard.Services
{
    public interface ILocalPathOpener
    {
        // 設定檔 appsettings.json 的完整路徑 (網站內容根目錄，也就是程式實際讀取的位置)
        string SettingsFilePath { get; }

        bool SettingsFileExists { get; }

        // 用本機的文字編輯器開啟設定檔；失敗丟出例外 (訊息可直接顯示)
        void OpenSettingsFile();

        // 用檔案總管 (Finder) 開啟設定檔所在資料夾
        void OpenSettingsFolder();
    }

    /// <summary>
    /// 在「執行網站的這台電腦」上開啟設定檔或資料夾。這是個人用的本機工具：只會開固定的 appsettings.json 與其資料夾，
    /// 不接受任何使用者輸入的路徑。若網站不是跑在使用者自己的電腦上 (例如雲端)，按鈕開的是伺服器那台，不會有效果。
    /// </summary>
    public class LocalPathOpener : ILocalPathOpener
    {
        public string SettingsFilePath { get; }
        public bool SettingsFileExists => File.Exists(SettingsFilePath);

        public LocalPathOpener(IWebHostEnvironment environment)
        {
            SettingsFilePath = Path.Combine(environment.ContentRootPath, "appsettings.json");
        }

        public void OpenSettingsFile()
        {
            if (!SettingsFileExists)
                throw new FileNotFoundException("找不到 appsettings.json，請先把 appsettings.Example.json 複製一份並改名為 appsettings.json");
            Start(BuildStartInfo(SettingsFilePath, isFolder: false, OperatingSystem.IsWindows(), OperatingSystem.IsMacOS()));
        }

        public void OpenSettingsFolder() =>
            Start(BuildStartInfo(Path.GetDirectoryName(SettingsFilePath)!, isFolder: true, OperatingSystem.IsWindows(), OperatingSystem.IsMacOS()));

        /// <summary>
        /// 各作業系統的開啟指令。設定檔固定用文字編輯器 (.json 在 Windows 可能沒有關聯程式，直接 ShellExecute 會跳出「選擇應用程式」)。
        /// </summary>
        public static ProcessStartInfo BuildStartInfo(string path, bool isFolder, bool isWindows, bool isMac)
        {
            if (isWindows)
                return isFolder
                    ? new ProcessStartInfo("explorer.exe") { ArgumentList = { path } }
                    : new ProcessStartInfo("notepad.exe") { ArgumentList = { path } };
            if (isMac)
                return isFolder
                    ? new ProcessStartInfo("open") { ArgumentList = { path } }
                    : new ProcessStartInfo("open") { ArgumentList = { "-t", path } };
            return new ProcessStartInfo("xdg-open") { ArgumentList = { path } };
        }

        private static void Start(ProcessStartInfo info)
        {
            try
            {
                info.UseShellExecute = false;
                using var process = Process.Start(info);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"無法開啟（{ex.Message}）。請自行到上方顯示的路徑開啟", ex);
            }
        }
    }
}
