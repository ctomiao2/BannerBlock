# 字幕遮挡器 BannerBlock

一个永远置顶的不透明色块，看剧时盖住中文字幕、只留英文。

## 使用

双击 exe 运行，横幅默认出现在主屏幕底部中间（正好压在字幕区域上方，拖到位即可）。

- **移动**：按住横幅任意位置拖动
- **缩放**：拖动横幅的边缘 / 四角（水平、垂直都能调）
- **偷看**：双击横幅 → 临时半透明，再双击恢复不透明
- **菜单**：右键横幅 → 颜色 / 不透明度 / 回到底部居中 / 退出
- 托盘（右下角）也有菜单；程序不在任务栏和 Alt+Tab 里出现

## 快捷键（全局有效，播放器全屏时也能用）

| 按键 | 作用 |
|---|---|
| Ctrl+Alt+B | 显示 / 隐藏 |
| Ctrl+Alt+方向键 | 微调位置（每次 8 像素） |
| Ctrl+Alt+Shift+方向键 | 微调大小 |
| Ctrl+Alt+Q | 退出 |

位置、大小、颜色、透明度自动保存在 `%APPDATA%\BannerBlock\config.ini`，下次打开自动恢复。
重复运行 exe 不会开第二个，只会把已运行的横幅叫出来。

## 关于全屏

横幅每 250ms 把自己重新压回系统置顶窗口组的最上层，所以 VLC / PotPlayer / mpv /
浏览器等"无边框全屏"播放时都能盖住，且点击横幅不会抢播放器的键盘焦点（空格暂停不受影响）。

极少数播放器开了"独占全屏"（exclusive fullscreen / D3D fullscreen）时，Windows 不允许
任何普通窗口覆盖——把播放器设置里的独占全屏关掉即可。

## 重新编译

源码就一个 `BannerBlock.cs`，用 Windows 自带编译器即可，无需安装任何东西：

```
C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe /nologo /target:winexe /codepage:65001 ^
  /r:System.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll ^
  /out:BannerBlock.exe BannerBlock.cs
```

注意：系统自带 csc 只支持 C# 5 语法（不能用字符串插值、`using var` 等）。
