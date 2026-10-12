<picture>
  <source media="(prefers-color-scheme: dark)" srcset="images/line-dark.png">
  <img src="images/line-light.png" alt="五張玻璃相框嘅截圖,夾喺 Windows 桌面頂部一條幼繩上面。">
</picture>

<h3 align="center">Clipline</h3>

<p align="center">
  將你嘅截圖,晾喺螢幕頂部一條繩上。
  <br>
  適用於 Windows 10 同 11。免費、開源。
  <br><br>
  <a href="../README.md">English</a>&nbsp;·&nbsp;繁體中文
</p>

<br>

## 睇唔到,但隨手拎到。

每張截圖都會掛喺你工作範圍上面嘅一條繩。
將滑鼠停喺螢幕頂邊,條繩就會滑落嚟;移開,佢就收返上去。

用 <kbd>Win</kbd>&thinsp;<kbd>Shift</kbd>&thinsp;<kbd>S</kbd> 截圖,張圖會由你截嘅位置飛上條繩。

<br>

## 每個動作都有用。

| | |
|:--|:--|
| 單擊 | 複製圖片。 |
| 長按 | 用小畫家標註。儲存之後,繩上會顯示新版本。 |
| 雙擊 | 用預設 App 開啟。 |
| 中鍵 | 快速預覽。方向鍵喺條繩上前後切換。 |
| 拖去 App | 傳一份副本過去,張相留喺繩上。 |
| 拖去資料夾 | 存喺嗰度,張相離開條繩。 |
| 拖去資源回收筒,或者撳角落 | 丟走。 |
| 右鍵 | 喺檔案總管顯示、儲存到桌面、收起一邊全部。 |
| 滑鼠停喺螢幕頂邊 | 喺嗰個螢幕放低條繩。 |
| 喺頂邊撳任何嘢 | 收起條繩。 |
| <kbd>Ctrl</kbd>&thinsp;<kbd>Alt</kbd>&thinsp;<kbd>T</kbd> | 顯示/收起條繩。可以喺系統匣選單「快捷鍵」更改。 |
| 滑鼠滾輪 | 「繩上保留」設為 25/50/100 張時,喺長繩上面移動。 |

角落個掣會話你知佢會做咩:**×** 會將檔案放入資源回收筒;**眼睛** 只係將張相攞落嚟,檔案留喺原位。

<br>

## 圖片從哪裏來

Clipline 自己唔截圖。佢監察 Windows 本身儲存截圖嘅資料夾,一寫完新檔案就即刻掛上去:

| 資料夾 | 內容 |
|:--|:--|
| 圖片\Screenshots | <kbd>Win</kbd>&thinsp;<kbd>Print Screen</kbd>,同埋開咗「自動儲存螢幕擷取畫面」(預設開)嘅剪取工具截圖。 |
| 影片\Screen Recordings | 剪取工具錄影,會顯示播放掣。 |
| 影片\Captures | Xbox Game Bar 擷取。 |

喺「監察資料夾」最多可以加三個你自己嘅資料夾。資料夾入面原有嘅檔案唔會理,只會掛新檔案。

**掛起複製的圖片**(預設關)會將複製到剪貼簿嘅圖片都掛上去。只限純圖片:連同文字或檔案一齊複製嘅、或者 App(例如密碼管理器)標記為私人嘅,都唔會掛。同一張截圖由兩條路到達,只會掛一次。

<br>

## 截圖資料夾,終於乾淨。

開咗 **丟走截圖時一併放入資源回收筒**,丟走一張相就會連檔案放入資源回收筒,資料夾只留你拖出去想保留嘅。預設關;任何情況都唔會直接永久刪除。

<br>

## 私隱優先。

冇帳戶、冇網絡、冇數據分析。Clipline 完全喺你部電腦運行,截圖永遠唔會離開。

為咗知道一張截圖喺邊度截,Clipline 會喺記憶體記住最近幾個滑鼠拖出嚟嘅長方形嘅角落,只保留幾秒。唔會記錄撳咗咩,亦唔會記錄鍵盤。

<br>

## 系統匣

其他設定全部喺系統匣圖示嘅選單入面:幾時放低條繩、保留幾多張、大小、快捷鍵、音效、開機啟動。撳個圖示就可以顯示/收起條繩。掛起複製圖片期間,圖示入面張相會變實心。

全螢幕 App(影片、遊戲)開住時 Clipline 唔會出現,除非你開咗「全螢幕 App 上面都放低」。

<br>

## 安裝

由 [最新 Release](https://github.com/xxxJay123/Clipline/releases/latest) 下載 **Clipline Setup**。
安裝程式已包 .NET,只為目前使用者安裝,唔需要管理員權限;會建立開始選單捷徑同解除安裝項目。
安裝時可以選擇「Start Clipline with Windows」(預設勾選)。

想用免安裝版,下載 zip,解壓後開 `Clipline.exe`。
Setup 版可以喺 Windows「已安裝的應用程式」移除;免安裝版就喺系統匣結束再刪除資料夾。
條繩同設定喺 `%LOCALAPPDATA%\Clipline`;互動式解除安裝會先問你要唔要刪除呢啲資料。

| 下載 | |
|:--|:--|
| `Clipline-Setup-0.1.0-x64.exe` | Intel/AMD 64 位元 Windows 安裝程式,已包 .NET。 |
| `Clipline-win-x64.zip` | 任何 64 位元 Windows 10/11 都用得,已包 .NET。 |
| `Clipline-win-arm64.zip` | 同上,俾 Arm 電腦。 |
| `Clipline-win-x64-small.zip` | 少過 1MB,需要 [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0)。 |

App 暫時未有程式碼簽章,第一次開 Windows SmartScreen 會問一次。

<br>

---

<sub>
Clipline 係 Alejandro Buján 嘅 <a href="https://github.com/alejandrobujan/tendedero">Tendedero</a> 嘅 Windows 版本,移植咗佢嘅設計同大部分行為,但冇用 Tendedero 嘅名同 icon。詳見 <a href="../NOTICE.md">NOTICE.md</a>。MIT 授權。
</sub>
