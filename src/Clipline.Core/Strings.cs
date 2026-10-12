using System.Globalization;

namespace Clipline.Core;

/// <summary>
/// Every word the app shows. Each key is the English text exactly as written
/// in L("...") in the code; every language has the same keys, which the tests
/// check.
/// </summary>
public static class Strings
{
    public static string Language { get; set; } = Pick(CultureInfo.CurrentUICulture);

    public static string L(string key) =>
        Tables.TryGetValue(Language, out var table) && table.TryGetValue(key, out var text) ? text : key;

    public static string L(string key, params object[] args) => string.Format(CultureInfo.CurrentCulture, L(key), args);

    public static string Pick(CultureInfo culture)
    {
        for (var c = culture; !string.IsNullOrEmpty(c.Name); c = c.Parent)
        {
            switch (c.Name)
            {
                case "zh-Hant" or "zh-HK" or "zh-TW" or "zh-MO": return "zh-Hant";
                case "zh-Hans" or "zh-CN" or "zh-SG": return "zh-Hans";
                case "zh": return "zh-Hans";
            }
        }
        return "en";
    }

    static Dictionary<string, string> Traditional { get; } = new()
    {
        // Tray
        ["Clipline"] = "Clipline",
        ["Clipline, hanging copied images"] = "Clipline,正在掛起複製的圖片",
        ["Show line"] = "放低條繩",
        ["Hide line"] = "收起條繩",
        ["Take everything down"] = "全部收落嚟",
        ["Hang copied images"] = "掛起複製的圖片",
        ["Bring the line down"] = "幾時放低條繩",
        ["When the pointer rests at the top edge"] = "滑鼠停喺螢幕頂邊時",
        ["When something new hangs"] = "有新嘢掛上去時",
        ["Over full screen apps"] = "全螢幕 App 上面都放低",
        ["Watch folders"] = "監察資料夾",
        ["Add folder…"] = "加入資料夾…",
        ["Stop watching"] = "停止監察",
        ["Choose a folder to watch"] = "揀一個要監察的資料夾",
        ["Keep on line"] = "繩上保留",
        ["As many as fit"] = "放得落幾多就幾多",
        ["{0} photos"] = "{0} 張",
        ["Size"] = "大小",
        ["Small"] = "細",
        ["Medium"] = "中",
        ["Large"] = "大",
        ["Shortcut"] = "快捷鍵",
        ["None"] = "無",
        ["Change shortcut…"] = "更改快捷鍵…",
        ["Send discarded screenshots to the Recycle Bin"] = "丟走截圖時一併放入資源回收筒",
        ["Open inbox folder"] = "打開收件資料夾",
        ["Sounds"] = "音效",
        ["Start with Windows"] = "開機自動啟動",
        ["Quit Clipline"] = "結束 Clipline",
        ["Clipline is on the line"] = "Clipline 已經掛好",
        ["Rest the pointer at the top edge of the screen to bring your screenshots down. Settings are in this tray icon."] =
            "將滑鼠停喺螢幕頂邊,截圖就會跌落嚟。設定喺呢個系統匣圖示入面。",

        // A photo
        ["Copy"] = "複製",
        ["Open"] = "開啟",
        ["Preview"] = "預覽",
        ["Edit"] = "編輯",
        ["Show in File Explorer"] = "喺檔案總管顯示",
        ["Save to Desktop"] = "儲存到桌面",
        ["Save as…"] = "另存新檔…",
        ["Discard"] = "丟走",
        ["Take down"] = "收落嚟",
        ["Move to Recycle Bin"] = "移到資源回收筒",
        ["Take down everything to the left"] = "收起左邊全部",
        ["Take down everything to the right"] = "收起右邊全部",
        ["Copied"] = "已複製",
        ["Take a screenshot with Win + Shift + S and it will hang here"] = "用 Win + Shift + S 截圖,佢就會掛喺度",

        // Shortcut
        ["Change shortcut"] = "更改快捷鍵",
        ["Press the new shortcut"] = "按新的快捷鍵",
        ["Esc cancels. Backspace removes the shortcut."] = "Esc 取消,Backspace 移除快捷鍵。",
        ["That shortcut is taken by another app."] = "呢個快捷鍵已經俾第二個 App 用咗。",

        // Preview
        ["{0} of {1}"] = "第 {0} 張,共 {1} 張",
        ["Arrow keys to browse · Enter to open · Esc to close"] = "方向鍵切換 · Enter 開啟 · Esc 關閉",

        // Problems
        ["Could not move this file to the Recycle Bin."] = "無法將呢個檔案移到資源回收筒。",
        ["Could not copy this file."] = "無法複製呢個檔案。",
    };

    static Dictionary<string, string> Simplified { get; } = new()
    {
        ["Clipline"] = "Clipline",
        ["Clipline, hanging copied images"] = "Clipline,正在挂起复制的图片",
        ["Show line"] = "放下晾衣绳",
        ["Hide line"] = "收起晾衣绳",
        ["Take everything down"] = "全部取下",
        ["Hang copied images"] = "挂起复制的图片",
        ["Bring the line down"] = "何时放下晾衣绳",
        ["When the pointer rests at the top edge"] = "指针停在屏幕顶边时",
        ["When something new hangs"] = "有新内容挂上时",
        ["Over full screen apps"] = "在全屏应用上也放下",
        ["Watch folders"] = "监视文件夹",
        ["Add folder…"] = "添加文件夹…",
        ["Stop watching"] = "停止监视",
        ["Choose a folder to watch"] = "选择要监视的文件夹",
        ["Keep on line"] = "绳上保留",
        ["As many as fit"] = "放得下多少就多少",
        ["{0} photos"] = "{0} 张",
        ["Size"] = "大小",
        ["Small"] = "小",
        ["Medium"] = "中",
        ["Large"] = "大",
        ["Shortcut"] = "快捷键",
        ["None"] = "无",
        ["Change shortcut…"] = "更改快捷键…",
        ["Send discarded screenshots to the Recycle Bin"] = "丢弃截图时一并移到回收站",
        ["Open inbox folder"] = "打开收件文件夹",
        ["Sounds"] = "音效",
        ["Start with Windows"] = "开机自动启动",
        ["Quit Clipline"] = "退出 Clipline",
        ["Clipline is on the line"] = "Clipline 已挂好",
        ["Rest the pointer at the top edge of the screen to bring your screenshots down. Settings are in this tray icon."] =
            "将指针停在屏幕顶边,截图就会落下来。设置在这个托盘图标里。",
        ["Copy"] = "复制",
        ["Open"] = "打开",
        ["Preview"] = "预览",
        ["Edit"] = "编辑",
        ["Show in File Explorer"] = "在文件资源管理器中显示",
        ["Save to Desktop"] = "保存到桌面",
        ["Save as…"] = "另存为…",
        ["Discard"] = "丢弃",
        ["Take down"] = "取下",
        ["Move to Recycle Bin"] = "移到回收站",
        ["Take down everything to the left"] = "取下左边全部",
        ["Take down everything to the right"] = "取下右边全部",
        ["Copied"] = "已复制",
        ["Take a screenshot with Win + Shift + S and it will hang here"] = "用 Win + Shift + S 截图,它就会挂在这里",
        ["Change shortcut"] = "更改快捷键",
        ["Press the new shortcut"] = "按下新的快捷键",
        ["Esc cancels. Backspace removes the shortcut."] = "Esc 取消,Backspace 移除快捷键。",
        ["That shortcut is taken by another app."] = "这个快捷键已被其他应用占用。",
        ["{0} of {1}"] = "第 {0} 张,共 {1} 张",
        ["Arrow keys to browse · Enter to open · Esc to close"] = "方向键切换 · Enter 打开 · Esc 关闭",
        ["Could not move this file to the Recycle Bin."] = "无法将此文件移到回收站。",
        ["Could not copy this file."] = "无法复制此文件。",
    };

    // Declared after the tables they read: static members initialise in order.
    public static IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> Tables { get; } =
        new Dictionary<string, IReadOnlyDictionary<string, string>>
        {
            ["en"] = Traditional.Keys.ToDictionary(k => k, k => k),
            ["zh-Hant"] = Traditional,
            ["zh-Hans"] = Simplified,
        };

    public static IReadOnlyCollection<string> Keys => Traditional.Keys;
}
