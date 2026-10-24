// BannerBlock.cs — 字幕遮挡横幅
// 一个永远置顶、默认纯黑、无边框的色块，用来看剧时盖住中文字幕、只留英文。
// 拖动任意位置移动，拖边缘/四角缩放，双击临时半透明"偷看"，右键出菜单。
// 用 Windows 自带 .NET Framework 编译，不需要安装任何运行库。
//
// 编译：
//   C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe /nologo /target:winexe /codepage:65001
//     /r:System.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll
//     /out:BannerBlock.exe BannerBlock.cs

using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace BannerBlock
{
    internal static class Program
    {
        [DllImport("user32.dll")]
        private static extern bool SetProcessDPIAware();

        private static Mutex mutex;
        private static EventWaitHandle showSignal;

        [STAThread]
        private static void Main()
        {
            bool createdNew;
            mutex = new Mutex(true, "Local\\BannerBlock_SingleInstance", out createdNew);
            if (!createdNew)
            {
                // 已经有一个实例在跑：把现有横幅叫出来，自己退出
                try { EventWaitHandle.OpenExisting("Local\\BannerBlock_Show").Set(); }
                catch { }
                return;
            }
            showSignal = new EventWaitHandle(false, EventResetMode.AutoReset, "Local\\BannerBlock_Show");

            try { SetProcessDPIAware(); }
            catch { }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            BannerForm form = new BannerForm();
            ThreadPool.RegisterWaitForSingleObject(showSignal, delegate(object state, bool timedOut)
            {
                try { form.BeginInvoke((MethodInvoker)delegate { form.EnsureVisible(); }); }
                catch { }
            }, null, -1, true);

            Application.Run(form);
            GC.KeepAlive(mutex);
        }
    }

    internal class BannerForm : Form
    {
        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint flags);

        [DllImport("user32.dll")]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint modifiers, uint vk);

        [DllImport("user32.dll")]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        private static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        private const uint SWP_NOSIZE = 0x0001;
        private const uint SWP_NOMOVE = 0x0002;
        private const uint SWP_NOACTIVATE = 0x0010;

        private const int WM_NCHITTEST = 0x0084;
        private const int WM_NCLBUTTONDBLCLK = 0x00A3;
        private const int WM_NCRBUTTONUP = 0x00A5;
        private const int WM_HOTKEY = 0x0312;
        private const int WM_EXITSIZEMOVE = 0x0232;

        private const int HTCAPTION = 2;
        private const int HTLEFT = 10, HTRIGHT = 11, HTTOP = 12, HTTOPLEFT = 13, HTTOPRIGHT = 14,
                          HTBOTTOM = 15, HTBOTTOMLEFT = 16, HTBOTTOMRIGHT = 17;

        private const int ID_TOGGLE_VIS = 1;
        private const int ID_QUIT = 2;
        private const int ID_MOVE_LEFT = 3, ID_MOVE_UP = 4, ID_MOVE_RIGHT = 5, ID_MOVE_DOWN = 6;
        private const int ID_SIZE_LEFT = 7, ID_SIZE_UP = 8, ID_SIZE_RIGHT = 9, ID_SIZE_DOWN = 10;

        private const uint MOD_ALT = 0x1, MOD_CONTROL = 0x2, MOD_SHIFT = 0x4;
        private const uint VK_LEFT = 0x25, VK_UP = 0x26, VK_RIGHT = 0x27, VK_DOWN = 0x28;

        private NotifyIcon tray;
        private System.Windows.Forms.Timer zTimer;
        private ContextMenuStrip bannerMenu;
        private bool firstRunTipShown;
        private bool peeking;
        private double mainOpacity = 1.0;
        private Color bannerColor = Color.Black;

        public BannerForm()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            TopMost = true;
            AutoScaleMode = AutoScaleMode.None;
            MinimumSize = new Size(32, 12);
            LoadConfig();

            // 每 250ms 重新断言一次置顶：就算播放器全屏后把自己抬进置顶组，
            // 横幅也会被立刻推回最上面 —— 这是"全屏也能盖住"的关键。
            zTimer = new System.Windows.Forms.Timer();
            zTimer.Interval = 250;
            zTimer.Tick += delegate
            {
                SetWindowPos(Handle, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
            };
            zTimer.Start();

            BuildTray();
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= 0x00000080; // WS_EX_TOOLWINDOW  不进任务栏 / Alt+Tab
                cp.ExStyle |= 0x08000000; // WS_EX_NOACTIVATE  点横幅不抢播放器的键盘焦点
                cp.ExStyle |= 0x00000008; // WS_EX_TOPMOST
                return cp;
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            uint m = MOD_CONTROL | MOD_ALT;
            uint ms = MOD_CONTROL | MOD_ALT | MOD_SHIFT;
            int failed = 0;
            if (!RegisterHotKey(Handle, ID_TOGGLE_VIS, m, 0x42)) failed++;          // Ctrl+Alt+B
            if (!RegisterHotKey(Handle, ID_QUIT, m, 0x51)) failed++;                // Ctrl+Alt+Q
            if (!RegisterHotKey(Handle, ID_MOVE_LEFT, m, VK_LEFT)) failed++;        // Ctrl+Alt+方向
            if (!RegisterHotKey(Handle, ID_MOVE_UP, m, VK_UP)) failed++;
            if (!RegisterHotKey(Handle, ID_MOVE_RIGHT, m, VK_RIGHT)) failed++;
            if (!RegisterHotKey(Handle, ID_MOVE_DOWN, m, VK_DOWN)) failed++;
            if (!RegisterHotKey(Handle, ID_SIZE_LEFT, ms, VK_LEFT)) failed++;       // Ctrl+Alt+Shift+方向
            if (!RegisterHotKey(Handle, ID_SIZE_UP, ms, VK_UP)) failed++;
            if (!RegisterHotKey(Handle, ID_SIZE_RIGHT, ms, VK_RIGHT)) failed++;
            if (!RegisterHotKey(Handle, ID_SIZE_DOWN, ms, VK_DOWN)) failed++;
            if (failed > 0)
            {
                tray.ShowBalloonTip(4000, "字幕遮挡器",
                    "有 " + failed + " 个快捷键注册失败（可能被其他软件占用，比如显卡驱动的屏幕旋转快捷键）。鼠标操作不受影响。",
                    ToolTipIcon.Warning);
            }
            if (!firstRunTipShown && !File.Exists(ConfigPath))
            {
                firstRunTipShown = true;
                tray.ShowBalloonTip(5000, "字幕遮挡器已启动",
                    "拖动=移动，拖边缘=缩放，双击=偷看，右键=菜单，Ctrl+Alt+B=隐藏",
                    ToolTipIcon.Info);
            }
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            if (zTimer != null) zTimer.Stop();
            SaveConfig();
            for (int id = 1; id <= 10; id++) UnregisterHotKey(Handle, id);
            if (tray != null) { tray.Visible = false; tray.Dispose(); }
            base.OnFormClosed(e);
        }

        protected override void WndProc(ref Message m)
        {
            switch (m.Msg)
            {
                case WM_NCHITTEST:
                {
                    // 整个横幅当作标题栏（可拖动），靠边缘 8px 当作缩放手柄，
                    // 无边框窗口也能用系统原生的拖动/缩放和对应的鼠标指针。
                    long lp = m.LParam.ToInt64();
                    int sx = (short)(lp & 0xFFFF);
                    int sy = (short)((lp >> 16) & 0xFFFF);
                    Point p = PointToClient(new Point(sx, sy));
                    const int edge = 8;
                    bool l = p.X < edge, r = p.X >= Width - edge, t = p.Y < edge, b = p.Y >= Height - edge;
                    int hit;
                    if (l && t) hit = HTTOPLEFT;
                    else if (r && t) hit = HTTOPRIGHT;
                    else if (l && b) hit = HTBOTTOMLEFT;
                    else if (r && b) hit = HTBOTTOMRIGHT;
                    else if (l) hit = HTLEFT;
                    else if (r) hit = HTRIGHT;
                    else if (t) hit = HTTOP;
                    else if (b) hit = HTBOTTOM;
                    else hit = HTCAPTION;
                    m.Result = (IntPtr)hit;
                    return;
                }
                case WM_NCLBUTTONDBLCLK:
                    TogglePeek(); // 拦下双击，顺便防止标题栏默认的"最大化"
                    return;
                case WM_NCRBUTTONUP:
                    ShowBannerMenu();
                    return;
                case WM_HOTKEY:
                    HandleHotkey(m.WParam.ToInt32());
                    return;
                case WM_EXITSIZEMOVE:
                    SaveConfig(); // 拖动/缩放一结束就记住位置
                    break;
            }
            base.WndProc(ref m);
        }

        public void EnsureVisible()
        {
            if (!Visible) Show();
            SetWindowPos(Handle, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
        }

        public void ToggleVisible()
        {
            if (Visible) Hide();
            else EnsureVisible();
        }

        private void SafeClose()
        {
            if (IsDisposed) return;
            try { Close(); }
            catch (ObjectDisposedException) { }
            catch (InvalidOperationException) { }
        }

        private void TogglePeek()
        {
            peeking = !peeking;
            Opacity = peeking ? Math.Min(0.30, mainOpacity) : mainOpacity;
        }

        private void HandleHotkey(int id)
        {
            const int step = 8;
            switch (id)
            {
                case ID_TOGGLE_VIS: ToggleVisible(); return;
                case ID_QUIT: SafeClose(); return;
                case ID_MOVE_LEFT: SetBounds(Location.X - step, Location.Y, Width, Height); break;
                case ID_MOVE_UP: SetBounds(Location.X, Location.Y - step, Width, Height); break;
                case ID_MOVE_RIGHT: SetBounds(Location.X + step, Location.Y, Width, Height); break;
                case ID_MOVE_DOWN: SetBounds(Location.X, Location.Y + step, Width, Height); break;
                case ID_SIZE_LEFT: SetBounds(Location.X, Location.Y, Math.Max(32, Width - step), Height); break;
                case ID_SIZE_UP: SetBounds(Location.X, Location.Y, Width, Math.Max(12, Height - step)); break;
                case ID_SIZE_RIGHT: SetBounds(Location.X, Location.Y, Width + step, Height); break;
                case ID_SIZE_DOWN: SetBounds(Location.X, Location.Y, Width, Height + step); break;
            }
            if (id >= ID_MOVE_LEFT) SaveConfig();
        }

        private void ShowBannerMenu()
        {
            // 上一个菜单此刻早已结束关闭流程，这时销毁才安全。
            // 绝不能在菜单自己的 Closed 事件里 Dispose：WinForms 在 Closed 之后
            // 还会继续使用它（曾导致点"退出"时 ObjectDisposedException 崩溃）。
            if (bannerMenu != null)
            {
                try { bannerMenu.Dispose(); }
                catch { }
                bannerMenu = null;
            }

            ContextMenuStrip menu = new ContextMenuStrip();
            try
            {
                ToolStripMenuItem peekItem = new ToolStripMenuItem("窥视 / 恢复不透明（双击横幅也行）");
                peekItem.Checked = peeking;
                peekItem.Click += delegate { TogglePeek(); };
                menu.Items.Add(peekItem);

                ToolStripMenuItem colorMenu = new ToolStripMenuItem("颜色");
                AddColorItem(colorMenu, "黑色", Color.Black);
                AddColorItem(colorMenu, "深灰", Color.FromArgb(45, 45, 45));
                AddColorItem(colorMenu, "白色", Color.White);
                AddColorItem(colorMenu, "藏青", Color.FromArgb(16, 32, 80));
                ToolStripMenuItem customColor = new ToolStripMenuItem("自定义…");
                customColor.Click += delegate
                {
                    using (ColorDialog dlg = new ColorDialog())
                    {
                        dlg.Color = bannerColor;
                        dlg.FullOpen = true;
                        if (dlg.ShowDialog(this) == DialogResult.OK)
                        {
                            bannerColor = dlg.Color;
                            BackColor = bannerColor;
                            SaveConfig();
                        }
                    }
                };
                colorMenu.DropDownItems.Add(customColor);
                menu.Items.Add(colorMenu);

                ToolStripMenuItem opMenu = new ToolStripMenuItem("不透明度");
                AddOpacityItem(opMenu, 1.0);
                AddOpacityItem(opMenu, 0.8);
                AddOpacityItem(opMenu, 0.6);
                AddOpacityItem(opMenu, 0.4);
                menu.Items.Add(opMenu);

                menu.Items.Add(new ToolStripSeparator());

                ToolStripMenuItem recenter = new ToolStripMenuItem("回到屏幕底部居中");
                recenter.Click += delegate { BottomCenter(); SaveConfig(); };
                menu.Items.Add(recenter);

                ToolStripMenuItem helpItem = new ToolStripMenuItem("使用说明");
                helpItem.Click += delegate { ShowHelp(); };
                menu.Items.Add(helpItem);

                ToolStripMenuItem exitItem = new ToolStripMenuItem("退出（Ctrl+Alt+Q）");
                exitItem.Click += delegate
                {
                    // 排队到下一条消息再关窗口：等菜单自己的点击/收起流程完整跑完，
                    // 不要在它收尾代码的半路上拆掉主窗体
                    try { BeginInvoke((MethodInvoker)SafeClose); }
                    catch (ObjectDisposedException) { }
                    catch (InvalidOperationException) { }
                };
                menu.Items.Add(exitItem);

                bannerMenu = menu;
                menu.Show(Cursor.Position);
            }
            catch
            {
                bannerMenu = null;
                menu.Dispose();
            }
        }

        private void AddColorItem(ToolStripMenuItem parent, string label, Color c)
        {
            ToolStripMenuItem item = new ToolStripMenuItem(label);
            item.Checked = (c.ToArgb() == bannerColor.ToArgb());
            item.Click += delegate { bannerColor = c; BackColor = c; peeking = false; Opacity = mainOpacity; SaveConfig(); };
            parent.DropDownItems.Add(item);
        }

        private void AddOpacityItem(ToolStripMenuItem parent, double op)
        {
            ToolStripMenuItem item = new ToolStripMenuItem(((int)(op * 100)) + "%");
            item.Checked = !peeking && Math.Abs(mainOpacity - op) < 0.01;
            item.Click += delegate { mainOpacity = op; peeking = false; Opacity = op; SaveConfig(); };
            parent.DropDownItems.Add(item);
        }

        private void BuildTray()
        {
            tray = new NotifyIcon();
            tray.Icon = MakeIcon();
            tray.Text = "字幕遮挡器 Ctrl+Alt+B 显隐";
            tray.Visible = true;
            tray.DoubleClick += delegate { ToggleVisible(); };

            ContextMenuStrip tm = new ContextMenuStrip();
            tm.Items.Add(MakeMenuItem("显示 / 隐藏（Ctrl+Alt+B）", delegate { ToggleVisible(); }));
            tm.Items.Add(MakeMenuItem("使用说明", delegate { ShowHelp(); }));
            tm.Items.Add(new ToolStripSeparator());
            tm.Items.Add(MakeMenuItem("退出（Ctrl+Alt+Q）", delegate { SafeClose(); }));
            tray.ContextMenuStrip = tm;
        }

        private static ToolStripMenuItem MakeMenuItem(string text, EventHandler onClick)
        {
            ToolStripMenuItem item = new ToolStripMenuItem(text);
            if (onClick != null) item.Click += onClick;
            return item;
        }

        private static Icon MakeIcon()
        {
            using (Bitmap bmp = new Bitmap(32, 32))
            {
                using (Graphics g = Graphics.FromImage(bmp))
                {
                    g.Clear(Color.Transparent);
                    using (SolidBrush brush = new SolidBrush(Color.Black))
                        g.FillRectangle(brush, 3, 12, 26, 9);
                    using (Pen pen = new Pen(Color.DimGray, 1.5f))
                        g.DrawRectangle(pen, 3, 12, 26, 9);
                }
                return Icon.FromHandle(bmp.GetHicon());
            }
        }

        private void BottomCenter()
        {
            Rectangle wa = Screen.PrimaryScreen.WorkingArea;
            Location = new Point(wa.Left + (wa.Width - Width) / 2, wa.Bottom - Height - 56);
        }

        private void ShowHelp()
        {
            MessageBox.Show(this,
                "字幕遮挡器 — 用法\n\n" +
                "· 按住横幅任意位置拖动 → 移动\n" +
                "· 拖动横幅边缘 / 四角 → 水平、垂直缩放\n" +
                "· 双击横幅 → 临时变半透明，偷看一眼中文\n" +
                "· 右键横幅 → 颜色 / 不透明度 / 退出\n\n" +
                "快捷键（全局有效）：\n" +
                "· Ctrl+Alt+B            显示 / 隐藏\n" +
                "· Ctrl+Alt+方向键        微调位置（每次 8 像素）\n" +
                "· Ctrl+Alt+Shift+方向键  微调大小\n" +
                "· Ctrl+Alt+Q            退出\n\n" +
                "横幅永远置顶，播放器全屏时也照样盖住。\n" +
                "位置、大小、颜色会自动记住，下次打开自动恢复。\n" +
                "隐藏后找不到？双击右下角托盘里的黑色小图标，或再运行一次程序。",
                "使用说明", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private static string ConfigDir
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "BannerBlock"); }
        }

        private static string ConfigPath
        {
            get { return Path.Combine(ConfigDir, "config.ini"); }
        }

        private static string Get(Dictionary<string, string> kv, string key)
        {
            string v;
            return kv.TryGetValue(key, out v) ? v : null;
        }

        private void LoadConfig()
        {
            try
            {
                if (File.Exists(ConfigPath))
                {
                    Dictionary<string, string> kv = new Dictionary<string, string>();
                    foreach (string line in File.ReadAllLines(ConfigPath))
                    {
                        int i = line.IndexOf('=');
                        if (i > 0) kv[line.Substring(0, i).Trim()] = line.Substring(i + 1).Trim();
                    }

                    int x = 0, y = 0, w = 0, h = 0, argb = 0, opPct = 0;
                    bool rectOk = int.TryParse(Get(kv, "x"), out x)
                               && int.TryParse(Get(kv, "y"), out y)
                               && int.TryParse(Get(kv, "w"), out w)
                               && int.TryParse(Get(kv, "h"), out h)
                               && w >= 32 && h >= 12;
                    if (rectOk)
                    {
                        Rectangle r = new Rectangle(x, y, w, h);
                        rectOk = false;
                        foreach (Screen s in Screen.AllScreens)
                            if (s.Bounds.IntersectsWith(r)) { rectOk = true; break; }
                        if (rectOk)
                        {
                            Size = new Size(w, h);
                            Location = new Point(x, y);
                        }
                    }
                    if (!rectOk) DefaultRect();

                    if (int.TryParse(Get(kv, "color"), out argb)) bannerColor = Color.FromArgb(argb);
                    if (int.TryParse(Get(kv, "opacity"), out opPct) && opPct >= 15 && opPct <= 100)
                        mainOpacity = opPct / 100.0;

                    BackColor = bannerColor;
                    Opacity = mainOpacity;
                    return;
                }
            }
            catch { }
            DefaultPlacement();
        }

        private void DefaultRect()
        {
            Rectangle wa = Screen.PrimaryScreen.WorkingArea;
            int w = Math.Min(900, wa.Width * 3 / 4);
            int h = 100;
            Size = new Size(w, h);
            Location = new Point(wa.Left + (wa.Width - w) / 2, wa.Bottom - h - 56);
        }

        private void DefaultPlacement()
        {
            bannerColor = Color.Black;
            mainOpacity = 1.0;
            BackColor = bannerColor;
            Opacity = mainOpacity;
            DefaultRect();
        }

        private void SaveConfig()
        {
            try
            {
                Directory.CreateDirectory(ConfigDir);
                File.WriteAllText(ConfigPath,
                    "x=" + Location.X + "\r\n" +
                    "y=" + Location.Y + "\r\n" +
                    "w=" + Width + "\r\n" +
                    "h=" + Height + "\r\n" +
                    "color=" + bannerColor.ToArgb() + "\r\n" +
                    "opacity=" + (int)Math.Round(mainOpacity * 100) + "\r\n");
            }
            catch { }
        }
    }
}
