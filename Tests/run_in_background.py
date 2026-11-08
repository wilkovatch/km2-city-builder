"""Runs Godot without taking the focus, on the second monitor when there is one (for windowed tests and renders).

Usage: python Tests/run_in_background.py <godot executable> <godot arguments...>
e.g.   python Tests/run_in_background.py "<console executable>" --path . -- selftest undo <folder> --mode drag

Godot activates its main window when it creates it, before any script runs. On Windows this script
blocks that (LockSetForegroundWindow) until the window appears. It also adds --screen with the first
monitor that isn't the primary one, unless the arguments already have --screen or --position.
The self-tests keep the window unfocusable and ignore the real mouse after that (GameEngine.Input.SimulateMouse).
Other platforms run Godot unchanged. The exit code is Godot's.
"""
import subprocess
import sys
import time

LSFW_LOCK, LSFW_UNLOCK = 1, 2
MONITORINFOF_PRIMARY = 1
WINDOW_TIMEOUT = 120  # seconds to wait for the window (a cold C# build can take a while)


def main():
    if len(sys.argv) < 2:
        print(__doc__, file=sys.stderr)
        return 2
    command = sys.argv[1:]
    if sys.platform != 'win32':
        return subprocess.call(command)
    import ctypes
    from ctypes import wintypes
    user32 = ctypes.WinDLL('user32', use_last_error=True)
    godot_args = command[1:command.index('--')] if '--' in command else command[1:]
    if '--screen' not in godot_args and '--position' not in godot_args:
        screen = secondary_screen(ctypes, wintypes, user32)
        if screen is not None:
            command[1:1] = ['--screen', str(screen)]
    before = engine_windows(ctypes, wintypes, user32)
    locked = user32.LockSetForegroundWindow(LSFW_LOCK)
    try:
        process = subprocess.Popen(command)
        start = time.time()
        while locked and process.poll() is None and time.time() - start < WINDOW_TIMEOUT:
            if engine_windows(ctypes, wintypes, user32) - before:
                time.sleep(0.5)  # the window is shown right after it is created
                break
            time.sleep(0.05)
    finally:
        if locked:
            user32.LockSetForegroundWindow(LSFW_UNLOCK)
    return process.wait()


def secondary_screen(ctypes, wintypes, user32):
    """Index of the first non-primary monitor, in the order of Godot's screens (EnumDisplayMonitors)."""
    class MONITORINFO(ctypes.Structure):
        _fields_ = [('cbSize', wintypes.DWORD), ('rcMonitor', wintypes.RECT), ('rcWork', wintypes.RECT), ('dwFlags', wintypes.DWORD)]
    flags = []

    @ctypes.WINFUNCTYPE(wintypes.BOOL, wintypes.HMONITOR, wintypes.HDC, ctypes.POINTER(wintypes.RECT), wintypes.LPARAM)
    def callback(monitor, dc, rect, data):
        info = MONITORINFO(cbSize=ctypes.sizeof(MONITORINFO))
        user32.GetMonitorInfoW(monitor, ctypes.byref(info))
        flags.append(info.dwFlags)
        return True
    user32.EnumDisplayMonitors(None, None, callback, 0)
    return next((i for i, f in enumerate(flags) if not f & MONITORINFOF_PRIMARY), None)


def engine_windows(ctypes, wintypes, user32):
    """The visible top-level windows of class Engine (Godot's)."""
    found = set()
    name = ctypes.create_unicode_buffer(64)

    @ctypes.WINFUNCTYPE(wintypes.BOOL, wintypes.HWND, wintypes.LPARAM)
    def callback(window, data):
        if user32.IsWindowVisible(window) and user32.GetClassNameW(window, name, 64) and name.value == 'Engine':
            found.add(window)
        return True
    user32.EnumWindows(callback, 0)
    return found


if __name__ == '__main__':
    sys.exit(main())
