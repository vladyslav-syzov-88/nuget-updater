namespace NuGetUpdater;

/// <summary>Coloured console output helpers for the different message severity levels.</summary>
internal static class Log
{
	internal static void Step(string msg) => Write(ConsoleColor.Cyan, $"  --> {msg}");
	internal static void Ok(string msg) => Write(ConsoleColor.Green, $"  [OK] {msg}");
	internal static void Fail(string msg) => Write(ConsoleColor.Red, $"  [FAIL] {msg}");
	internal static void Warn(string msg) => Write(ConsoleColor.Yellow, $"  [WARN] {msg}");
	internal static void Header(string msg) => Write(ConsoleColor.Magenta, $"\n==============================\n  {msg}\n==============================");

	private static void Write(ConsoleColor color, string msg)
	{
		Console.ForegroundColor = color;
		Console.WriteLine(msg);
		Console.ResetColor();
	}
}