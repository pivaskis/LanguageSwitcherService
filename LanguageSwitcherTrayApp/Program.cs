using System.Diagnostics;
using System.Runtime.InteropServices;

namespace LanguageSwitcherTrayApp
{
	static class Program
	{
		private static string _lastProcessName = string.Empty;
		private static IntPtr _lastKeyboardLayout = IntPtr.Zero;

		private static Dictionary<string, IntPtr> _programLanguages = new Dictionary<string, IntPtr>();
		// {
		// 	{"chrome",-257424350},
		// 	{"rider64",68748313},
		// };

		// P/Invoke для работы с Windows API
		[DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
		private static extern IntPtr GetForegroundWindow();

		[DllImport("user32.dll")]
		private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

		[DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
		private static extern bool PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

		[DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
		private static extern IntPtr GetKeyboardLayout(uint idThread);

		[DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
		private static extern IntPtr ActivateKeyboardLayout(IntPtr hkl, UInt32 flags);

		// Константа для смены языка через PostMessage
		private const uint WM_INPUTLANGCHANGEREQUEST = 0x0050;
		private const int WM_INPUTLANGCHANGE = 0x0051; // Смена языка
		

		[DllImport("user32.dll")]
		private static extern IntPtr SetWinEventHook(
			uint eventMin, uint eventMax, IntPtr hmodWinEventProc, WinEventDelegate lpfnWinEventProc,
			uint idProcess, uint idThread, uint dwFlags);

		[DllImport("user32.dll")]
		private static extern bool UnhookWinEvent(IntPtr hWinEventHook);

		private const uint EVENT_SYSTEM_FOREGROUND = 0x0003;
		private const uint WINEVENT_OUTOFCONTEXT = 0x0000;

		private delegate void WinEventDelegate(IntPtr hWinEventHook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime);

		private static IntPtr _winEventHook;
		private static WinEventDelegate _winEventProc;

		private static IntPtr _langEventHook;
		private static WinEventDelegate _langEventProc;

		[STAThread]
		static void Main()
		{
			Application.EnableVisualStyles();
			Application.SetCompatibleTextRenderingDefault(false);

			// Создаем Tray-иконку
			NotifyIcon trayIcon = new NotifyIcon
			{
				Text = "Language Switcher",
				Icon = SystemIcons.Application,
				Visible = true
			};

			// Контекстное меню для Tray-иконки
			ContextMenuStrip contextMenu = new ContextMenuStrip();
			contextMenu.Items.Add("Exit", null, (s, e) =>
			{
				UnhookWinEvent(_winEventHook);
				UnhookWinEvent(_langEventHook);
				Application.Exit();
			});

			trayIcon.ContextMenuStrip = contextMenu;

			// Инициализация хуков для отслеживания смены активного окна
			_winEventProc = new WinEventDelegate(WinEventProc);
			_winEventHook = SetWinEventHook(EVENT_SYSTEM_FOREGROUND, 40, IntPtr.Zero, _winEventProc, 0, 0, WINEVENT_OUTOFCONTEXT);

			// Запускаем приложение с невидимой формой для перехвата сообщений
			Application.Run(new HiddenForm());
		}

		// Обработчик события смены активного окна

		private static void WinEventProc(IntPtr hWinEventHook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime)
		{
			Console.WriteLine(eventType);

			if (eventType == EVENT_SYSTEM_FOREGROUND)
			{
				HandleWindowChange();
			}
			if (eventType == 40)
			{
				HandleLanguageChange();
			}
		}

		// Обработка смены активного окна
		private static void HandleWindowChange()
		{
			IntPtr currentWindowHandle = GetForegroundWindow();
			string currentProcessName = GetProcessName(currentWindowHandle);
			uint threadId = GetWindowThread(currentWindowHandle);
			IntPtr currentKeyboardLayout = GetKeyboardLayout(threadId);
			
			if (currentProcessName == _lastProcessName) return;

			// Console.WriteLine("Current process name = " + currentProcessName + " lang = " + currentKeyboardLayout);
			//
			// if (_programLanguages.ContainsKey(_lastProcessName) == false)
			// {
			// 	_programLanguages.Add(_lastProcessName, currentKeyboardLayout);
			// 	Console.WriteLine("Add " + _lastProcessName + "  " + currentKeyboardLayout);
			// }
			// else
			// {
			// 	_programLanguages[_lastProcessName] = currentKeyboardLayout;
			// 	Console.WriteLine("Save " + _lastProcessName + "  " + currentKeyboardLayout);
			// }
			
			
			_lastProcessName = currentProcessName;
			
			if (!_programLanguages.ContainsKey(currentProcessName)) return;

			// Применяем сохранённую раскладку для нового окна
			IntPtr savedKeyboardLayout = _programLanguages[currentProcessName];
			PostMessage(currentWindowHandle, WM_INPUTLANGCHANGEREQUEST, IntPtr.Zero, savedKeyboardLayout);
			Console.WriteLine("Change language to " + savedKeyboardLayout);
		}
		
		private static void HandleLanguageChange()
		{
			IntPtr currentWindowHandle = GetForegroundWindow();
			string currentProcessName = GetProcessName(currentWindowHandle);
			uint threadId = GetWindowThread(currentWindowHandle);
			IntPtr currentKeyboardLayout = GetKeyboardLayout(threadId);

			Console.WriteLine("SaveCurrentLanguage for " + currentProcessName + " to " + currentKeyboardLayout);
			if (_programLanguages.ContainsKey(currentProcessName))
			{
				_programLanguages[currentProcessName] = currentKeyboardLayout;
			}
			else
			{
				_programLanguages.Add(currentProcessName, currentKeyboardLayout);
			}
		}

		// Получаем имя процесса для окна
		private static string GetProcessName(IntPtr hwnd)
		{
			uint processId;
			GetWindowThreadProcessId(hwnd, out processId);
			Process process = Process.GetProcessById((int)processId);
			return process.ProcessName;
		}

		// Получаем ID потока для текущего окна
		private static uint GetWindowThread(IntPtr hwnd)
		{
			uint processId;
			return GetWindowThreadProcessId(hwnd, out processId);
		}

		// Невидимая форма для перехвата сообщений Windows
		private class HiddenForm : Form
		{
			public HiddenForm()
			{
				// Делаем форму невидимой
				this.ShowInTaskbar = false;
				this.WindowState = FormWindowState.Minimized;
				this.Visible = false;
			}

			protected override void WndProc(ref Message m)
			{
				if (m.Msg == WM_INPUTLANGCHANGE)
				{
					// Язык ввода изменен - обрабатываем
					IntPtr hwnd = GetForegroundWindow();
					HandleLanguageChange(hwnd);
				}

				base.WndProc(ref m);
			}

			// Обработка смены языка
			private void HandleLanguageChange(IntPtr hwnd)
			{
				string currentProcessName = GetProcessName(hwnd);
				uint threadId = GetWindowThread(hwnd);
				IntPtr currentKeyboardLayout = GetKeyboardLayout(threadId);

				Console.WriteLine("SaveCurrentLanguage for " + currentProcessName + " to " + currentKeyboardLayout);
				if (_programLanguages.ContainsKey(currentProcessName))
				{
					_programLanguages[currentProcessName] = currentKeyboardLayout;
				}
				else
				{
					_programLanguages.Add(currentProcessName, currentKeyboardLayout);
				}
			}
		}
	}
}