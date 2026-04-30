using System.Diagnostics;
using System.Text;

namespace NuGetUpdater;

/// <summary>Thin wrapper around <see cref="System.Diagnostics.Process"/> for running external commands and capturing their output.</summary>
internal static class Shell
{
	internal static void AssertPrerequisite(string command, string installHint)
	{
		var psi = new ProcessStartInfo
		{
			FileName = OperatingSystem.IsWindows() ? "where" : "which",
			Arguments = command,
			UseShellExecute = false,
			RedirectStandardOutput = true,
			RedirectStandardError = true,
		};
		using var p = Process.Start(psi)!;
		p.WaitForExit();
		if (p.ExitCode != 0)
		{
			Log.Fail($"'{command}' not found. {installHint}");
			Environment.Exit(1);
		}
	}

	internal static bool Run(string description, string executable, string[] args, string? workingDir = null)
	{
		Log.Step(description);
		int exitCode = Execute(executable, args, workingDir);
		if (exitCode != 0)
		{
			Log.Fail($"{description} failed (exit {exitCode}).");
			return false;
		}
		Log.Ok(description);
		return true;
	}

	internal static (int ExitCode, string Output) Capture(string executable, string[] args, string? workingDir = null)
	{
		var output = new StringBuilder();
		using var process = new Process { StartInfo = BuildPsi(executable, args, workingDir) };

		process.OutputDataReceived += (_, e) =>
		{
			if (e.Data is null)
				return;
			Console.WriteLine(e.Data);
			output.AppendLine(e.Data);
		};
		process.ErrorDataReceived += (_, e) =>
		{
			if (e.Data is null)
				return;
			Console.WriteLine(e.Data);
			output.AppendLine(e.Data);
		};

		process.Start();
		process.BeginOutputReadLine();
		process.BeginErrorReadLine();
		process.WaitForExit();
		return (process.ExitCode, output.ToString().Trim());
	}

	private static int Execute(string executable, string[] args, string? workingDir)
	{
		using var process = new Process { StartInfo = BuildPsi(executable, args, workingDir) };
		process.OutputDataReceived += (_, e) => { if (e.Data is not null) Console.WriteLine(e.Data); };
		process.ErrorDataReceived += (_, e) => { if (e.Data is not null) Console.WriteLine(e.Data); };
		process.Start();
		process.BeginOutputReadLine();
		process.BeginErrorReadLine();
		process.WaitForExit();
		return process.ExitCode;
	}

	private static ProcessStartInfo BuildPsi(string executable, string[] args, string? workingDir)
	{
		var psi = new ProcessStartInfo(executable)
		{
			UseShellExecute = false,
			RedirectStandardOutput = true,
			RedirectStandardError = true,
			WorkingDirectory = workingDir ?? Directory.GetCurrentDirectory(),
		};
		foreach (string arg in args)
		{
			psi.ArgumentList.Add(arg);
		}
		return psi;
	}
}