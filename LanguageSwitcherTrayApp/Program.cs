using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace LanguageSwitcherTrayApp
{
	internal static class Program
	{
		private static string LastProcessName = string.Empty;

		private static readonly Dictionary<string, IntPtr> ProgramLanguages = new();

		[DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
		private static extern IntPtr GetForegroundWindow();

		[DllImport("user32.dll")]
		private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

		[DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
		private static extern bool PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

		[DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
		private static extern IntPtr GetKeyboardLayout(uint idThread);

		[DllImport("user32.dll")]
		private static extern IntPtr SetWinEventHook(
			uint eventMin, uint eventMax, IntPtr hmodWinEventProc, WinEventDelegate lpfnWinEventProc,
			uint idProcess, uint idThread, uint dwFlags);

		[DllImport("user32.dll")]
		private static extern bool UnhookWinEvent(IntPtr hWinEventHook);

		private const uint WM_INPUTLANGCHANGEREQUEST = 0x0050;
		private const uint EVENT_SYSTEM_FOREGROUND = 0x0003;
		private const uint EVENT_SYSTEM_LANGUAGECHANGE = 0x0028;
		private const uint WINEVENT_OUTOFCONTEXT = 0x0000;
		
		private const string RegistryKeyPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";

		private delegate void WinEventDelegate(IntPtr hWinEventHook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime);

		private static IntPtr WinEventHook;

		[STAThread]
		private static void Main()
		{
			Application.EnableVisualStyles();
			Application.SetCompatibleTextRenderingDefault(false);

			// Создаем Tray-иконку
			NotifyIcon trayIcon = new NotifyIcon
			{
				Text = "Language Switcher",
				Icon = SystemIcons.Application,
				//Icon = new System.Drawing.Icon(),
				Visible = true
			};

			string appPath = Application.ExecutablePath;
			string appName = Path.GetFileName(appPath);
			
			ContextMenuStrip contextMenu = new ContextMenuStrip();
			
			contextMenu.Items.Add("Exit", null, (s, e) =>
			{
				UnhookWinEvent(WinEventHook);
				Application.Exit();
			});
			
			contextMenu.Items.Add("Remove from startup", null, (s, e) =>
			{
				RemoveFromStartup(appName);
			});
			
			contextMenu.Items.Add("Add to startup", null, (s, e) =>
			{
				AddToStartup(appName, appPath);
			});

			trayIcon.ContextMenuStrip = contextMenu;

			WinEventHook = SetWinEventHook(EVENT_SYSTEM_FOREGROUND, EVENT_SYSTEM_LANGUAGECHANGE, IntPtr.Zero, WinEventProc, 0, 0, WINEVENT_OUTOFCONTEXT);

			Application.Run(new HiddenForm());
		}

		private static void WinEventProc(IntPtr hWinEventHook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime)
		{
			Console.WriteLine(eventType);

			switch (eventType)
			{
				case EVENT_SYSTEM_FOREGROUND:
					HandleWindowChange();
					break;
				case EVENT_SYSTEM_LANGUAGECHANGE:
					HandleLanguageChange();
					break;
			}
		}

		private static void HandleWindowChange()
		{
			IntPtr currentWindowHandle = GetForegroundWindow();
			string currentProcessName = GetProcessName(currentWindowHandle);
			uint threadId = GetWindowThread(currentWindowHandle);
			IntPtr currentKeyboardLayout = GetKeyboardLayout(threadId);

			if (currentProcessName == LastProcessName) return;

			LastProcessName = currentProcessName;

			if (ProgramLanguages.ContainsKey(currentProcessName) == false)
			{
				ProgramLanguages.Add(currentProcessName, currentKeyboardLayout);
				return;
			}

			IntPtr savedKeyboardLayout = ProgramLanguages[currentProcessName];
			PostMessage(currentWindowHandle, WM_INPUTLANGCHANGEREQUEST, IntPtr.Zero, savedKeyboardLayout);
		}

		private static void HandleLanguageChange()
		{
			IntPtr currentWindowHandle = GetForegroundWindow();
			string currentProcessName = GetProcessName(currentWindowHandle);
			uint threadId = GetWindowThread(currentWindowHandle);
			IntPtr currentKeyboardLayout = GetKeyboardLayout(threadId);

			Console.WriteLine("SaveCurrentLanguage for " + currentProcessName + " to " + currentKeyboardLayout);

			if (ProgramLanguages.ContainsKey(currentProcessName))
			{
				ProgramLanguages[currentProcessName] = currentKeyboardLayout;
			}
			else
			{
				ProgramLanguages.Add(currentProcessName, currentKeyboardLayout);
			}
		}

		private static string GetProcessName(IntPtr hwnd)
		{
			GetWindowThreadProcessId(hwnd, out uint processId);
			var process = Process.GetProcessById((int)processId);
			return process.ProcessName;
		}

		private static uint GetWindowThread(IntPtr hwnd) =>
			GetWindowThreadProcessId(hwnd, out uint _);

		private static void AddToStartup(string appName, string appPath)
		{
			using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RegistryKeyPath, true);
			if (key == null)
			{
				MessageBox.Show("Ошибка доступа к реестру", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
				return;
			}

			// Проверяем, есть ли уже запись
			var existingPath = key.GetValue(appName) as string;
			if (existingPath == $"\"{appPath}\"")
			{
				MessageBox.Show("Приложение уже в автозагрузке", "Информация", MessageBoxButtons.OK, MessageBoxIcon.Information);
				return;
			}

			key.SetValue(appName, $"\"{appPath}\"");
			MessageBox.Show("Приложение добавлено в автозагрузку", "Успех", MessageBoxButtons.OK, MessageBoxIcon.Information);
		}

		private static void RemoveFromStartup(string appName)
		{
			using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RegistryKeyPath, true);
			if (key?.GetValue(appName) == null) return;
			key.DeleteValue(appName);
			MessageBox.Show("Приложение удалено из автозагрузки", "Информация", MessageBoxButtons.OK, MessageBoxIcon.Information);
		}

		private class HiddenForm : Form
		{
			public HiddenForm()
			{
				ShowInTaskbar = false;
				WindowState = FormWindowState.Minimized;
				Visible = false;
			}
		}
	}
}