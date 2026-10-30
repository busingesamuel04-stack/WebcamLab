using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using WebcamLab.Core.Validation;

namespace WebcamLab;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AttachConsole(int dwProcessId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AllocConsole();

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GetStdHandle(int nStdHandle);

    private const int ATTACH_PARENT_PROCESS = -1;
    private const int STD_OUTPUT_HANDLE = -11;

    protected override void OnStartup(StartupEventArgs e)
    {
        if (e.Args.Any(a => a.Equals("--test", StringComparison.OrdinalIgnoreCase) || a.Equals("-t", StringComparison.OrdinalIgnoreCase)))
        {
            if (!AttachConsole(ATTACH_PARENT_PROCESS))
            {
                AllocConsole();
            }

            var stdHandle = GetStdHandle(STD_OUTPUT_HANDLE);
            var safeHandle = new Microsoft.Win32.SafeHandles.SafeFileHandle(stdHandle, ownsHandle: false);
            var fs = new System.IO.FileStream(safeHandle, System.IO.FileAccess.Write);
            var stdOut = new System.IO.StreamWriter(fs, System.Text.Encoding.UTF8) { AutoFlush = true };
            Console.SetOut(stdOut);
            Console.SetError(stdOut);

            Console.WriteLine();
            Console.WriteLine("==================================================================");
            Console.WriteLine("  WebcamLab Automated Mathematical & Software Look Test Suites");
            Console.WriteLine("==================================================================");

            var sb = new System.Text.StringBuilder();
            sb.AppendLine("==================================================================");
            sb.AppendLine("  WebcamLab Automated Mathematical & Software Look Test Suites");
            sb.AppendLine("==================================================================");

            bool allPassed = true;

            // Phase 5: Scopes & Telemetry
            Console.WriteLine("\n--- Part A: Phase 5 Scopes & Color Mathematics ---");
            sb.AppendLine("\n--- Part A: Phase 5 Scopes & Color Mathematics ---");
            var scopesResults = ScopesMathValidator.RunAllTests();
            foreach (var r in scopesResults)
            {
                string status = r.Passed ? "[PASS]" : "[FAIL]";
                Console.ForegroundColor = r.Passed ? ConsoleColor.Green : ConsoleColor.Red;
                Console.Write($"  {status,-7}");
                Console.ResetColor();
                Console.WriteLine($" {r.Name} ({r.ExecutionMs:F2} ms)");
                Console.WriteLine($"          {r.Details}");
                sb.AppendLine($"  {status,-7} {r.Name} ({r.ExecutionMs:F2} ms)");
                sb.AppendLine($"          {r.Details}");
                if (!r.Passed) allPassed = false;
            }

            // Phase 7: Software Look Pipeline
            Console.WriteLine("\n--- Part B: Phase 7 Non-Destructive Software Look Pipeline ---");
            sb.AppendLine("\n--- Part B: Phase 7 Non-Destructive Software Look Pipeline ---");
            var lookResults = SoftwareLookValidator.RunAllTests();
            foreach (var r in lookResults)
            {
                string status = r.Passed ? "[PASS]" : "[FAIL]";
                Console.ForegroundColor = r.Passed ? ConsoleColor.Green : ConsoleColor.Red;
                Console.Write($"  {status,-7}");
                Console.ResetColor();
                Console.WriteLine($" {r.Name} ({r.ExecutionMs:F2} ms)");
                Console.WriteLine($"          {r.Details}");
                sb.AppendLine($"  {status,-7} {r.Name} ({r.ExecutionMs:F2} ms)");
                sb.AppendLine($"          {r.Details}");
                if (!r.Passed) allPassed = false;
            }

            // Phase 8: False-Color Exposure Scopes
            Console.WriteLine("\n--- Part C: Phase 8 False-Color Exposure Scopes ---");
            sb.AppendLine("\n--- Part C: Phase 8 False-Color Exposure Scopes ---");
            bool fcPassed = FalseColorValidator.RunAllTests(out string fcReport);
            Console.WriteLine(fcReport);
            sb.AppendLine(fcReport);
            if (!fcPassed) allPassed = false;

            // Phase 9/13: Graded Video Recording & Cadence Policy (CFR vs Capture-Faithful VFR)
            Console.WriteLine("\n--- Part D: Phase 9/13 Video Recording & Cadence Policy ---");
            sb.AppendLine("\n--- Part D: Phase 9/13 Video Recording & Cadence Policy ---");
            bool recPassed = GradedRecordingValidator.RunAllTests(out string recReport);
            Console.WriteLine(recReport);
            sb.AppendLine(recReport);
            if (!recPassed) allPassed = false;

            // Phase 15: Experimental Realtek YUY2 Processing Control & Presets
            Console.WriteLine("\n--- Part E: Phase 15 Realtek Processing Control & Presets ---");
            sb.AppendLine("\n--- Part E: Phase 15 Realtek Processing Control & Presets ---");
            var rtResults = RealtekProcessingValidator.RunAllTests();
            foreach (var r in rtResults)
            {
                string status = r.Passed ? "[PASS]" : "[FAIL]";
                Console.ForegroundColor = r.Passed ? ConsoleColor.Green : ConsoleColor.Red;
                Console.Write($"  {status,-7}");
                Console.ResetColor();
                Console.WriteLine($" {r.Name} ({r.ExecutionMs:F2} ms)");
                Console.WriteLine($"          {r.Details}");
                sb.AppendLine($"  {status,-7} {r.Name} ({r.ExecutionMs:F2} ms)");
                sb.AppendLine($"          {r.Details}");
                if (!r.Passed) allPassed = false;
            }

            // Phase 16: Timestamp-Synchronized Audio/Video Recording & WASAPI Subsystem
            Console.WriteLine("\n--- Part F: Phase 16 Synchronized Audio/Video Recording & WASAPI ---");
            sb.AppendLine("\n--- Part F: Phase 16 Synchronized Audio/Video Recording & WASAPI ---");
            var audioResults = AudioSyncValidator.RunAllTests();
            foreach (var r in audioResults)
            {
                string status = r.Passed ? "[PASS]" : "[FAIL]";
                Console.ForegroundColor = r.Passed ? ConsoleColor.Green : ConsoleColor.Red;
                Console.Write($"  {status,-7}");
                Console.ResetColor();
                Console.WriteLine($" {r.Name} ({r.ExecutionMs:F2} ms)");
                Console.WriteLine($"          {r.Details}");
                sb.AppendLine($"  {status,-7} {r.Name} ({r.ExecutionMs:F2} ms)");
                sb.AppendLine($"          {r.Details}");
                if (!r.Passed) allPassed = false;
            }

            // Phase 22: Production Polish, Cinema Look Manager, Audio Monitoring & Telemetry
            Console.WriteLine("\n--- Part G: Phase 22 Production Polish & Cinema Look Manager ---");
            sb.AppendLine("\n--- Part G: Phase 22 Production Polish & Cinema Look Manager ---");
            var phase22Results = Phase22ProductionValidator.RunAllTests();
            foreach (var r in phase22Results)
            {
                string status = r.Passed ? "[PASS]" : "[FAIL]";
                Console.ForegroundColor = r.Passed ? ConsoleColor.Green : ConsoleColor.Red;
                Console.Write($"  {status,-7}");
                Console.ResetColor();
                Console.WriteLine($" {r.Name} ({r.ExecutionMs:F2} ms)");
                Console.WriteLine($"          {r.Details}");
                sb.AppendLine($"  {status,-7} {r.Name} ({r.ExecutionMs:F2} ms)");
                sb.AppendLine($"          {r.Details}");
                if (!r.Passed) allPassed = false;
            }

            // Phase 23: Multi-Frame Still Image Enhancement & Temporal Fusion
            Console.WriteLine("\n--- Part H: Phase 23 Multi-Frame Still Image Enhancement ---");
            sb.AppendLine("\n--- Part H: Phase 23 Multi-Frame Still Image Enhancement ---");
            var phase23Results = Phase23EnhancementValidator.RunAllTests();
            foreach (var r in phase23Results)
            {
                string status = r.Passed ? "[PASS]" : "[FAIL]";
                Console.ForegroundColor = r.Passed ? ConsoleColor.Green : ConsoleColor.Red;
                Console.Write($"  {status,-7}");
                Console.ResetColor();
                Console.WriteLine($" {r.Name} ({r.ExecutionMs:F2} ms)");
                Console.WriteLine($"          {r.Details}");
                sb.AppendLine($"  {status,-7} {r.Name} ({r.ExecutionMs:F2} ms)");
                sb.AppendLine($"          {r.Details}");
                if (!r.Passed) allPassed = false;
            }

            // Phase 26: Multi-Frame Super-Resolution Research & Subpixel Sampling
            Console.WriteLine("\n--- Part I: Phase 26 Multi-Frame Super-Resolution Research ---");
            sb.AppendLine("\n--- Part I: Phase 26 Multi-Frame Super-Resolution Research ---");
            var phase26Results = WebcamLab.Core.Validation.Phase26SuperResolutionValidator.RunTests();
            foreach (var r in phase26Results)
            {
                string status = r.Passed ? "[PASS]" : "[FAIL]";
                Console.ForegroundColor = r.Passed ? ConsoleColor.Green : ConsoleColor.Red;
                Console.Write($"  {status,-7}");
                Console.ResetColor();
                Console.WriteLine($" {r.Name} ({r.ElapsedMs:F2} ms)");
                Console.WriteLine($"          {r.Details}");
                sb.AppendLine($"  {status,-7} {r.Name} ({r.ElapsedMs:F2} ms)");
                sb.AppendLine($"          {r.Details}");
                if (!r.Passed) allPassed = false;
            }

            // Phase 33: Gate 2 Native Media Foundation Recording Backend Validation
            Console.WriteLine("\n--- Part J: Phase 33 Native Media Foundation Backend Validation ---");
            sb.AppendLine("\n--- Part J: Phase 33 Native Media Foundation Backend Validation ---");
            var nativeBackendResults = WebcamLab.Core.Validation.NativeBackendValidator.RunAllTests();
            foreach (var r in nativeBackendResults)
            {
                string status = r.Passed ? "[PASS]" : "[FAIL]";
                Console.ForegroundColor = r.Passed ? ConsoleColor.Green : ConsoleColor.Red;
                Console.Write($"  {status,-7}");
                Console.ResetColor();
                Console.WriteLine($" {r.Name} ({r.ExecutionMs:F2} ms)");
                Console.WriteLine($"          {r.Details}");
                sb.AppendLine($"  {status,-7} {r.Name} ({r.ExecutionMs:F2} ms)");
                sb.AppendLine($"          {r.Details}");
                if (!r.Passed) allPassed = false;
            }

            int totalTests = scopesResults.Count + lookResults.Count + 10 + 20 + rtResults.Count + audioResults.Count + phase22Results.Count + phase23Results.Count + phase26Results.Count + nativeBackendResults.Count;
            Console.WriteLine("------------------------------------------------------------------");
            sb.AppendLine("------------------------------------------------------------------");
            if (allPassed)
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"  ALL {totalTests} VALIDATION TESTS PASSED CLEANLY!");
                Console.ResetColor();
                sb.AppendLine($"  ALL {totalTests} VALIDATION TESTS PASSED CLEANLY!");
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("  TEST SUITE COMPLETED WITH FAILURES.");
                Console.ResetColor();
                sb.AppendLine("  TEST SUITE COMPLETED WITH FAILURES.");
            }
            Console.WriteLine("==================================================================");
            sb.AppendLine("==================================================================");

            try
            {
                System.IO.File.WriteAllText(System.IO.Path.Combine(AppContext.BaseDirectory, "TestResults.txt"), sb.ToString());
            }
            catch { }

            Shutdown(allPassed ? 0 : 1);
            return;
        }

        if (e.Args.Any(a => a.Equals("--regression", StringComparison.OrdinalIgnoreCase) || a.Equals("-r", StringComparison.OrdinalIgnoreCase)))
        {
            if (!AttachConsole(ATTACH_PARENT_PROCESS))
            {
                AllocConsole();
            }

            var stdHandle = GetStdHandle(STD_OUTPUT_HANDLE);
            var safeHandle = new Microsoft.Win32.SafeHandles.SafeFileHandle(stdHandle, ownsHandle: false);
            var fs = new System.IO.FileStream(safeHandle, System.IO.FileAccess.Write);
            var stdOut = new System.IO.StreamWriter(fs, System.Text.Encoding.UTF8) { AutoFlush = true };
            Console.SetOut(stdOut);
            Console.SetError(stdOut);

            // CRITICAL: Clear SynchronizationContext so async/await continuations run on ThreadPool threads,
            // avoiding deadlock with the WPF Dispatcher thread during synchronous CLI execution.
            SynchronizationContext.SetSynchronizationContext(null);

            string outDir = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "WebcamLab", "Regressions");
            bool passed = Task.Run(async () => await PhysicalCameraRegressionRunner.RunRegressionAsync(outDir)).GetAwaiter().GetResult();
            Shutdown(passed ? 0 : 1);
            return;
        }

        if (e.Args.Any(a => a.Equals("--burst-live", StringComparison.OrdinalIgnoreCase) ||
                            a.Equals("--burst-test", StringComparison.OrdinalIgnoreCase) ||
                            a.Equals("-bl", StringComparison.OrdinalIgnoreCase)))
        {
            if (!AttachConsole(ATTACH_PARENT_PROCESS))
            {
                AllocConsole();
            }

            var stdHandle = GetStdHandle(STD_OUTPUT_HANDLE);
            var safeHandle = new Microsoft.Win32.SafeHandles.SafeFileHandle(stdHandle, ownsHandle: false);
            var fs = new System.IO.FileStream(safeHandle, System.IO.FileAccess.Write);
            var stdOut = new System.IO.StreamWriter(fs, System.Text.Encoding.UTF8) { AutoFlush = true };
            Console.SetOut(stdOut);
            Console.SetError(stdOut);

            SynchronizationContext.SetSynchronizationContext(null);

            string outDir = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "WebcamLab", "Phase23_Bursts");
            bool passed = Task.Run(async () => await Phase23LiveBurstRunner.RunLiveBurstExperimentsAsync(outDir)).GetAwaiter().GetResult();
            Shutdown(passed ? 0 : 1);
            return;
        }

        if (e.Args.Any(a => a.Equals("--probe", StringComparison.OrdinalIgnoreCase) ||
                            a.Equals("--reverse-engineering", StringComparison.OrdinalIgnoreCase) ||
                            a.Equals("-re", StringComparison.OrdinalIgnoreCase)))
        {
            if (!AttachConsole(ATTACH_PARENT_PROCESS))
            {
                AllocConsole();
            }

            var stdHandle = GetStdHandle(STD_OUTPUT_HANDLE);
            var safeHandle = new Microsoft.Win32.SafeHandles.SafeFileHandle(stdHandle, ownsHandle: false);
            var fs = new System.IO.FileStream(safeHandle, System.IO.FileAccess.Write);
            var stdOut = new System.IO.StreamWriter(fs, System.Text.Encoding.UTF8) { AutoFlush = true };
            Console.SetOut(stdOut);
            Console.SetError(stdOut);

            SynchronizationContext.SetSynchronizationContext(null);

            bool ok = WebcamLab.Core.ReverseEngineering.ReverseEngineeringRunner.RunLaboratoryInvestigation(out _);
            Shutdown(ok ? 0 : 1);
            return;
        }

        if (e.Args.Any(a => a.Equals("--phase11", StringComparison.OrdinalIgnoreCase) ||
                            a.Equals("--isp", StringComparison.OrdinalIgnoreCase) ||
                            a.Equals("--investigate", StringComparison.OrdinalIgnoreCase)))
        {
            if (!AttachConsole(ATTACH_PARENT_PROCESS))
            {
                AllocConsole();
            }

            var stdHandle = GetStdHandle(STD_OUTPUT_HANDLE);
            var safeHandle = new Microsoft.Win32.SafeHandles.SafeFileHandle(stdHandle, ownsHandle: false);
            var fs = new System.IO.FileStream(safeHandle, System.IO.FileAccess.Write);
            var stdOut = new System.IO.StreamWriter(fs, System.Text.Encoding.UTF8) { AutoFlush = true };
            Console.SetOut(stdOut);
            Console.SetError(stdOut);

            SynchronizationContext.SetSynchronizationContext(null);

            bool ok = Task.Run(async () => await WebcamLab.Core.ReverseEngineering.Phase11InvestigationRunner.RunInvestigationAsync()).GetAwaiter().GetResult();
            Shutdown(ok ? 0 : 1);
            return;
        }

        if (e.Args.Any(a => a.Equals("--phase12", StringComparison.OrdinalIgnoreCase) ||
                            a.Equals("--linux", StringComparison.OrdinalIgnoreCase) ||
                            a.Equals("--wsl", StringComparison.OrdinalIgnoreCase)))
        {
            if (!AttachConsole(ATTACH_PARENT_PROCESS))
            {
                AllocConsole();
            }

            var stdHandle = GetStdHandle(STD_OUTPUT_HANDLE);
            var safeHandle = new Microsoft.Win32.SafeHandles.SafeFileHandle(stdHandle, ownsHandle: false);
            var fs = new System.IO.FileStream(safeHandle, System.IO.FileAccess.Write);
            var stdOut = new System.IO.StreamWriter(fs, System.Text.Encoding.UTF8) { AutoFlush = true };
            Console.SetOut(stdOut);
            Console.SetError(stdOut);

            SynchronizationContext.SetSynchronizationContext(null);

            bool ok = Task.Run(async () => await WebcamLab.Core.ReverseEngineering.Phase12InvestigationRunner.RunInvestigationAsync()).GetAwaiter().GetResult();
            Shutdown(ok ? 0 : 1);
            return;
        }

        if (e.Args.Any(a => a.Equals("--phase13", StringComparison.OrdinalIgnoreCase) ||
                            a.Equals("--cadence", StringComparison.OrdinalIgnoreCase) ||
                            a.Equals("--vfr", StringComparison.OrdinalIgnoreCase)))
        {
            if (!AttachConsole(ATTACH_PARENT_PROCESS))
            {
                AllocConsole();
            }

            var stdHandle = GetStdHandle(STD_OUTPUT_HANDLE);
            var safeHandle = new Microsoft.Win32.SafeHandles.SafeFileHandle(stdHandle, ownsHandle: false);
            var fs = new System.IO.FileStream(safeHandle, System.IO.FileAccess.Write);
            var stdOut = new System.IO.StreamWriter(fs, System.Text.Encoding.UTF8) { AutoFlush = true };
            Console.SetOut(stdOut);
            Console.SetError(stdOut);

            SynchronizationContext.SetSynchronizationContext(null);

            bool ok = Task.Run(async () => await WebcamLab.Core.ReverseEngineering.Phase13InvestigationRunner.RunInvestigationAsync()).GetAwaiter().GetResult();
            Shutdown(ok ? 0 : 1);
            return;
        }

        if (e.Args.Any(a => a.Equals("--phase13-2", StringComparison.OrdinalIgnoreCase) ||
                            a.Equals("--phase13_2", StringComparison.OrdinalIgnoreCase) ||
                            a.Equals("--true-timestamp", StringComparison.OrdinalIgnoreCase) ||
                            a.Equals("--true-pts", StringComparison.OrdinalIgnoreCase)))
        {
            if (!AttachConsole(ATTACH_PARENT_PROCESS))
            {
                AllocConsole();
            }

            var stdHandle = GetStdHandle(STD_OUTPUT_HANDLE);
            var safeHandle = new Microsoft.Win32.SafeHandles.SafeFileHandle(stdHandle, ownsHandle: false);
            var fs = new System.IO.FileStream(safeHandle, System.IO.FileAccess.Write);
            var stdOut = new System.IO.StreamWriter(fs, System.Text.Encoding.UTF8) { AutoFlush = true };
            Console.SetOut(stdOut);
            Console.SetError(stdOut);

            SynchronizationContext.SetSynchronizationContext(null);

            bool ok = Task.Run(async () => await WebcamLab.Core.ReverseEngineering.Phase13_2VerificationRunner.RunInvestigationAsync()).GetAwaiter().GetResult();
            Shutdown(ok ? 0 : 1);
            return;
        }

        if (e.Args.Any(a => a.Equals("--phase13-1", StringComparison.OrdinalIgnoreCase) ||
                            a.Equals("--phase13_1", StringComparison.OrdinalIgnoreCase) ||
                            a.Equals("--timestamp-audit", StringComparison.OrdinalIgnoreCase) ||
                            a.Equals("--ts-audit", StringComparison.OrdinalIgnoreCase)))
        {
            if (!AttachConsole(ATTACH_PARENT_PROCESS))
            {
                AllocConsole();
            }

            var stdHandle = GetStdHandle(STD_OUTPUT_HANDLE);
            var safeHandle = new Microsoft.Win32.SafeHandles.SafeFileHandle(stdHandle, ownsHandle: false);
            var fs = new System.IO.FileStream(safeHandle, System.IO.FileAccess.Write);
            var stdOut = new System.IO.StreamWriter(fs, System.Text.Encoding.UTF8) { AutoFlush = true };
            Console.SetOut(stdOut);
            Console.SetError(stdOut);

            SynchronizationContext.SetSynchronizationContext(null);

            bool ok = Task.Run(async () => await WebcamLab.Core.ReverseEngineering.Phase13_1VerificationRunner.RunInvestigationAsync()).GetAwaiter().GetResult();
            Shutdown(ok ? 0 : 1);
            return;
        }

        if (e.Args.Any(a => a.Equals("--sinkwriter", StringComparison.OrdinalIgnoreCase) ||
                            a.Equals("--test-sinkwriter", StringComparison.OrdinalIgnoreCase)))
        {
            if (!AttachConsole(ATTACH_PARENT_PROCESS))
            {
                AllocConsole();
            }

            var stdHandle = GetStdHandle(STD_OUTPUT_HANDLE);
            var safeHandle = new Microsoft.Win32.SafeHandles.SafeFileHandle(stdHandle, ownsHandle: false);
            var fs = new System.IO.FileStream(safeHandle, System.IO.FileAccess.Write);
            var stdOut = new System.IO.StreamWriter(fs, System.Text.Encoding.UTF8) { AutoFlush = true };
            Console.SetOut(stdOut);
            Console.SetError(stdOut);

            SynchronizationContext.SetSynchronizationContext(null);

            WebcamLab.Scratch.SinkWriterTest.Test();
            Shutdown(0);
            return;
        }

        if (e.Args.Any(a => a.Equals("--test-native-mf", StringComparison.OrdinalIgnoreCase) ||
                            a.Equals("--native-mf", StringComparison.OrdinalIgnoreCase) ||
                            a.Equals("--phase31", StringComparison.OrdinalIgnoreCase) ||
                            a.Equals("--phase32", StringComparison.OrdinalIgnoreCase) ||
                            a.Equals("--phase33", StringComparison.OrdinalIgnoreCase) ||
                            a.Equals("--native-promotion", StringComparison.OrdinalIgnoreCase) ||
                            a.Equals("--av-calibration", StringComparison.OrdinalIgnoreCase) ||
                            a.Equals("--dual-engine", StringComparison.OrdinalIgnoreCase) ||
                            a.Equals("-nmf", StringComparison.OrdinalIgnoreCase)))
        {
            if (!AttachConsole(ATTACH_PARENT_PROCESS))
            {
                AllocConsole();
            }

            var stdHandle = GetStdHandle(STD_OUTPUT_HANDLE);
            var safeHandle = new Microsoft.Win32.SafeHandles.SafeFileHandle(stdHandle, ownsHandle: false);
            var fs = new System.IO.FileStream(safeHandle, System.IO.FileAccess.Write);
            var stdOut = new System.IO.StreamWriter(fs, System.Text.Encoding.UTF8) { AutoFlush = true };
            Console.SetOut(stdOut);
            Console.SetError(stdOut);

            SynchronizationContext.SetSynchronizationContext(null);

            string outDir = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "WebcamLab", "NativeMF_Validation");
            bool passed = Task.Run(async () => await WebcamLab.Capture.NativeMediaFoundationValidationRunner.RunPhase31SuiteAsync(outDir)).GetAwaiter().GetResult();
            Shutdown(passed ? 0 : 1);
            return;
        }

        if (e.Args.Any(a => a.Equals("--phase27", StringComparison.OrdinalIgnoreCase) ||
                            a.Equals("-p27", StringComparison.OrdinalIgnoreCase) ||
                            a.Equals("--validate-sr", StringComparison.OrdinalIgnoreCase) ||
                            a.Equals("--phase27-validation", StringComparison.OrdinalIgnoreCase)))
        {
            if (!AttachConsole(ATTACH_PARENT_PROCESS))
            {
                AllocConsole();
            }

            var stdHandle = GetStdHandle(STD_OUTPUT_HANDLE);
            var safeHandle = new Microsoft.Win32.SafeHandles.SafeFileHandle(stdHandle, ownsHandle: false);
            var fs = new System.IO.FileStream(safeHandle, System.IO.FileAccess.Write);
            var stdOut = new System.IO.StreamWriter(fs, System.Text.Encoding.UTF8) { AutoFlush = true };
            Console.SetOut(stdOut);
            Console.SetError(stdOut);

            SynchronizationContext.SetSynchronizationContext(null);

            string outDir = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "WebcamLab", "Phase27_Validation");
            bool ok = Task.Run(async () => await WebcamLab.Core.SuperResolution.Phase27IndependentValidationRunner.RunValidationAsync(outDir)).GetAwaiter().GetResult();
            Shutdown(ok ? 0 : 1);
            return;
        }

        if (e.Args.Any(a => a.Equals("--phase26", StringComparison.OrdinalIgnoreCase) ||
                            a.Equals("-p26", StringComparison.OrdinalIgnoreCase) ||
                            a.Equals("--super-resolution", StringComparison.OrdinalIgnoreCase) ||
                            a.Equals("--sr", StringComparison.OrdinalIgnoreCase)))
        {
            if (!AttachConsole(ATTACH_PARENT_PROCESS))
            {
                AllocConsole();
            }

            var stdHandle = GetStdHandle(STD_OUTPUT_HANDLE);
            var safeHandle = new Microsoft.Win32.SafeHandles.SafeFileHandle(stdHandle, ownsHandle: false);
            var fs = new System.IO.FileStream(safeHandle, System.IO.FileAccess.Write);
            var stdOut = new System.IO.StreamWriter(fs, System.Text.Encoding.UTF8) { AutoFlush = true };
            Console.SetOut(stdOut);
            Console.SetError(stdOut);

            SynchronizationContext.SetSynchronizationContext(null);

            string outDir = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "WebcamLab", "Phase26_SuperResolution");
            bool ok = Task.Run(async () => await WebcamLab.Core.SuperResolution.Phase26InvestigationRunner.RunInvestigationAsync(outDir)).GetAwaiter().GetResult();
            Shutdown(ok ? 0 : 1);
            return;
        }

        if (e.Args.Any(a => a.Equals("--phase24", StringComparison.OrdinalIgnoreCase) ||
                            a.Equals("-p24", StringComparison.OrdinalIgnoreCase) ||
                            a.Equals("--unit4", StringComparison.OrdinalIgnoreCase) ||
                            a.Equals("--phase24-unit4", StringComparison.OrdinalIgnoreCase) ||
                            a.Equals("--unit4-investigation", StringComparison.OrdinalIgnoreCase)))
        {
            if (!AttachConsole(ATTACH_PARENT_PROCESS))
            {
                AllocConsole();
            }

            var stdHandle = GetStdHandle(STD_OUTPUT_HANDLE);
            var safeHandle = new Microsoft.Win32.SafeHandles.SafeFileHandle(stdHandle, ownsHandle: false);
            var fs = new System.IO.FileStream(safeHandle, System.IO.FileAccess.Write);
            var stdOut = new System.IO.StreamWriter(fs, System.Text.Encoding.UTF8) { AutoFlush = true };
            Console.SetOut(stdOut);
            Console.SetError(stdOut);

            SynchronizationContext.SetSynchronizationContext(null);

            bool ok = Task.Run(async () => await WebcamLab.Core.ReverseEngineering.Phase24InvestigationRunner.RunInvestigationAsync()).GetAwaiter().GetResult();
            Shutdown(ok ? 0 : 1);
            return;
        }

        if (e.Args.Any(a => a.Equals("--phase21", StringComparison.OrdinalIgnoreCase) ||
                            a.Equals("-p21", StringComparison.OrdinalIgnoreCase) ||
                            a.Equals("--unit4", StringComparison.OrdinalIgnoreCase) ||
                            a.Equals("--command", StringComparison.OrdinalIgnoreCase) ||
                            a.Equals("--unit4-command", StringComparison.OrdinalIgnoreCase)))
        {
            if (!AttachConsole(ATTACH_PARENT_PROCESS))
            {
                AllocConsole();
            }

            var stdHandle = GetStdHandle(STD_OUTPUT_HANDLE);
            var safeHandle = new Microsoft.Win32.SafeHandles.SafeFileHandle(stdHandle, ownsHandle: false);
            var fs = new System.IO.FileStream(safeHandle, System.IO.FileAccess.Write);
            var stdOut = new System.IO.StreamWriter(fs, System.Text.Encoding.UTF8) { AutoFlush = true };
            Console.SetOut(stdOut);
            Console.SetError(stdOut);

            SynchronizationContext.SetSynchronizationContext(null);

            bool ok = Task.Run(async () => await WebcamLab.Core.ReverseEngineering.Phase21InvestigationRunner.RunInvestigationAsync()).GetAwaiter().GetResult();
            Shutdown(ok ? 0 : 1);
            return;
        }

        if (e.Args.Any(a => a.Equals("--phase20", StringComparison.OrdinalIgnoreCase) ||
                            a.Equals("-p20", StringComparison.OrdinalIgnoreCase) ||
                            a.Equals("--control4", StringComparison.OrdinalIgnoreCase) ||
                            a.Equals("--unit7-control4", StringComparison.OrdinalIgnoreCase)))
        {
            if (!AttachConsole(ATTACH_PARENT_PROCESS))
            {
                AllocConsole();
            }

            var stdHandle = GetStdHandle(STD_OUTPUT_HANDLE);
            var safeHandle = new Microsoft.Win32.SafeHandles.SafeFileHandle(stdHandle, ownsHandle: false);
            var fs = new System.IO.FileStream(safeHandle, System.IO.FileAccess.Write);
            var stdOut = new System.IO.StreamWriter(fs, System.Text.Encoding.UTF8) { AutoFlush = true };
            Console.SetOut(stdOut);
            Console.SetError(stdOut);

            SynchronizationContext.SetSynchronizationContext(null);

            bool mutate = e.Args.Any(a => a.Equals("--mutate", StringComparison.OrdinalIgnoreCase) || a.Equals("-m", StringComparison.OrdinalIgnoreCase));
            bool ok = Task.Run(async () => await WebcamLab.Core.ReverseEngineering.Phase20InvestigationRunner.RunInvestigationAsync(mutate)).GetAwaiter().GetResult();
            Shutdown(ok ? 0 : 1);
            return;
        }

        if (e.Args.Any(a => a.Equals("--phase19", StringComparison.OrdinalIgnoreCase) ||
                            a.Equals("-p19", StringComparison.OrdinalIgnoreCase) ||
                            a.Equals("--control3", StringComparison.OrdinalIgnoreCase) ||
                            a.Equals("--unit7-control3", StringComparison.OrdinalIgnoreCase)))
        {
            if (!AttachConsole(ATTACH_PARENT_PROCESS))
            {
                AllocConsole();
            }

            var stdHandle = GetStdHandle(STD_OUTPUT_HANDLE);
            var safeHandle = new Microsoft.Win32.SafeHandles.SafeFileHandle(stdHandle, ownsHandle: false);
            var fs = new System.IO.FileStream(safeHandle, System.IO.FileAccess.Write);
            var stdOut = new System.IO.StreamWriter(fs, System.Text.Encoding.UTF8) { AutoFlush = true };
            Console.SetOut(stdOut);
            Console.SetError(stdOut);

            SynchronizationContext.SetSynchronizationContext(null);

            bool ok = Task.Run(async () => await WebcamLab.Core.ReverseEngineering.Phase19InvestigationRunner.RunInvestigationAsync()).GetAwaiter().GetResult();
            Shutdown(ok ? 0 : 1);
            return;
        }

        if (e.Args.Any(a => a.Equals("--phase18", StringComparison.OrdinalIgnoreCase) ||
                            a.Equals("-p18", StringComparison.OrdinalIgnoreCase) ||
                            a.Equals("--phase18-1", StringComparison.OrdinalIgnoreCase) ||
                            a.Equals("-p18-1", StringComparison.OrdinalIgnoreCase) ||
                            a.Equals("--forensic", StringComparison.OrdinalIgnoreCase)))
        {
            if (!AttachConsole(ATTACH_PARENT_PROCESS))
            {
                AllocConsole();
            }

            var stdHandle = GetStdHandle(STD_OUTPUT_HANDLE);
            var safeHandle = new Microsoft.Win32.SafeHandles.SafeFileHandle(stdHandle, ownsHandle: false);
            var fs = new System.IO.FileStream(safeHandle, System.IO.FileAccess.Write);
            var stdOut = new System.IO.StreamWriter(fs, System.Text.Encoding.UTF8) { AutoFlush = true };
            Console.SetOut(stdOut);
            Console.SetError(stdOut);

            SynchronizationContext.SetSynchronizationContext(null);

            bool ok = Task.Run(async () => await WebcamLab.Core.ReverseEngineering.Phase18ForensicRunner.RunAllAsync()).GetAwaiter().GetResult();
            Shutdown(ok ? 0 : 1);
            return;
        }

        if (e.Args.Any(a => a.Equals("--phase40d", StringComparison.OrdinalIgnoreCase) ||
                            a.Equals("-p40d", StringComparison.OrdinalIgnoreCase) ||
                            a.Equals("--interruption-test", StringComparison.OrdinalIgnoreCase)))
        {
            if (!AttachConsole(ATTACH_PARENT_PROCESS))
            {
                AllocConsole();
            }

            var stdHandle = GetStdHandle(STD_OUTPUT_HANDLE);
            var safeHandle = new Microsoft.Win32.SafeHandles.SafeFileHandle(stdHandle, ownsHandle: false);
            var fs = new System.IO.FileStream(safeHandle, System.IO.FileAccess.Write);
            var stdOut = new System.IO.StreamWriter(fs, System.Text.Encoding.UTF8) { AutoFlush = true };
            Console.SetOut(stdOut);
            Console.SetError(stdOut);

            SynchronizationContext.SetSynchronizationContext(null);

            string filter = "ALL";
            int idx = Array.FindIndex(e.Args, a => a.Equals("--phase40d", StringComparison.OrdinalIgnoreCase) || a.Equals("-p40d", StringComparison.OrdinalIgnoreCase));
            if (idx >= 0 && idx + 1 < e.Args.Length && !e.Args[idx + 1].StartsWith("-"))
            {
                filter = e.Args[idx + 1].ToUpperInvariant();
            }

            bool ok = Task.Run(async () => await WebcamLab.Core.ReverseEngineering.Phase40DInterruptionRunner.RunAllAsync(filter)).GetAwaiter().GetResult();
            Shutdown(ok ? 0 : 1);
            return;
        }

        if (e.Args.Any(a => a.Equals("--phase40e", StringComparison.OrdinalIgnoreCase) ||
                            a.Equals("-p40e", StringComparison.OrdinalIgnoreCase) ||
                            a.Equals("--release-audit", StringComparison.OrdinalIgnoreCase) ||
                            a.Equals("--final-audit", StringComparison.OrdinalIgnoreCase)))
        {
            if (!AttachConsole(ATTACH_PARENT_PROCESS))
            {
                AllocConsole();
            }

            var stdHandle = GetStdHandle(STD_OUTPUT_HANDLE);
            var safeHandle = new Microsoft.Win32.SafeHandles.SafeFileHandle(stdHandle, ownsHandle: false);
            var fs = new System.IO.FileStream(safeHandle, System.IO.FileAccess.Write);
            var stdOut = new System.IO.StreamWriter(fs, System.Text.Encoding.UTF8) { AutoFlush = true };
            Console.SetOut(stdOut);
            Console.SetError(stdOut);

            SynchronizationContext.SetSynchronizationContext(null);

            bool ok = Task.Run(async () => await WebcamLab.Core.ReverseEngineering.Phase40EReleaseAuditRunner.RunAllAsync()).GetAwaiter().GetResult();
            Shutdown(ok ? 0 : 1);
            return;
        }
        if (e.Args.Any(a => a.Equals("--phase41", StringComparison.OrdinalIgnoreCase) ||
                            a.Equals("-p41", StringComparison.OrdinalIgnoreCase) ||
                            a.Equals("--gui-test", StringComparison.OrdinalIgnoreCase) ||
                            a.Equals("--verify-gui", StringComparison.OrdinalIgnoreCase)))
        {
            if (!AttachConsole(ATTACH_PARENT_PROCESS))
            {
                AllocConsole();
            }

            var stdHandle = GetStdHandle(STD_OUTPUT_HANDLE);
            var safeHandle = new Microsoft.Win32.SafeHandles.SafeFileHandle(stdHandle, ownsHandle: false);
            var fs = new System.IO.FileStream(safeHandle, System.IO.FileAccess.Write);
            var stdOut = new System.IO.StreamWriter(fs, System.Text.Encoding.UTF8) { AutoFlush = true };
            Console.SetOut(stdOut);
            Console.SetError(stdOut);

            bool ok = Task.Run(async () => await WebcamLab.Core.ReverseEngineering.Phase41GuiVerificationRunner.RunVerificationAsync()).GetAwaiter().GetResult();
            Shutdown(ok ? 0 : 1);
            return;
        }

        if (e.Args.Any(a => a.Equals("--phase17", StringComparison.OrdinalIgnoreCase) ||
                            a.Equals("-p17", StringComparison.OrdinalIgnoreCase) ||
                            a.Equals("--drift-profile", StringComparison.OrdinalIgnoreCase) ||
                            a.Equals("--longform", StringComparison.OrdinalIgnoreCase)))
        {
            if (!AttachConsole(ATTACH_PARENT_PROCESS))
            {
                AllocConsole();
            }

            var stdHandle = GetStdHandle(STD_OUTPUT_HANDLE);
            var safeHandle = new Microsoft.Win32.SafeHandles.SafeFileHandle(stdHandle, ownsHandle: false);
            var fs = new System.IO.FileStream(safeHandle, System.IO.FileAccess.Write);
            var stdOut = new System.IO.StreamWriter(fs, System.Text.Encoding.UTF8) { AutoFlush = true };
            Console.SetOut(stdOut);
            Console.SetError(stdOut);

            SynchronizationContext.SetSynchronizationContext(null);

            string filter = "ALL";
            int idx = Array.FindIndex(e.Args, a => a.Equals("--phase17", StringComparison.OrdinalIgnoreCase) || a.Equals("-p17", StringComparison.OrdinalIgnoreCase));
            if (idx >= 0 && idx + 1 < e.Args.Length && !e.Args[idx + 1].StartsWith("-"))
            {
                filter = e.Args[idx + 1].ToUpperInvariant();
            }

            bool ok = Task.Run(async () => await WebcamLab.Core.ReverseEngineering.Phase17LongFormRunner.RunAllAsync(filter)).GetAwaiter().GetResult();
            Shutdown(ok ? 0 : 1);
            return;
        }

        if (e.Args.Any(a => a.Equals("--phase16", StringComparison.OrdinalIgnoreCase) ||
                            a.Equals("-p16", StringComparison.OrdinalIgnoreCase) ||
                            a.Equals("--av-sync", StringComparison.OrdinalIgnoreCase) ||
                            a.Equals("--audio-video", StringComparison.OrdinalIgnoreCase)))
        {
            if (!AttachConsole(ATTACH_PARENT_PROCESS))
            {
                AllocConsole();
            }

            var stdHandle = GetStdHandle(STD_OUTPUT_HANDLE);
            var safeHandle = new Microsoft.Win32.SafeHandles.SafeFileHandle(stdHandle, ownsHandle: false);
            var fs = new System.IO.FileStream(safeHandle, System.IO.FileAccess.Write);
            var stdOut = new System.IO.StreamWriter(fs, System.Text.Encoding.UTF8) { AutoFlush = true };
            Console.SetOut(stdOut);
            Console.SetError(stdOut);

            SynchronizationContext.SetSynchronizationContext(null);

            bool ok = Task.Run(async () => await WebcamLab.Core.ReverseEngineering.Phase16VerificationRunner.RunVerificationAsync()).GetAwaiter().GetResult();
            Shutdown(ok ? 0 : 1);
            return;
        }

        if (e.Args.Any(a => a.Equals("--phase15", StringComparison.OrdinalIgnoreCase) ||
                            a.Equals("-p15", StringComparison.OrdinalIgnoreCase) ||
                            a.Equals("--aba-test", StringComparison.OrdinalIgnoreCase)))
        {
            if (!AttachConsole(ATTACH_PARENT_PROCESS))
            {
                AllocConsole();
            }

            var stdHandle = GetStdHandle(STD_OUTPUT_HANDLE);
            var safeHandle = new Microsoft.Win32.SafeHandles.SafeFileHandle(stdHandle, ownsHandle: false);
            var fs = new System.IO.FileStream(safeHandle, System.IO.FileAccess.Write);
            var stdOut = new System.IO.StreamWriter(fs, System.Text.Encoding.UTF8) { AutoFlush = true };
            Console.SetOut(stdOut);
            Console.SetError(stdOut);

            SynchronizationContext.SetSynchronizationContext(null);

            bool ok = Task.Run(async () => await WebcamLab.Core.ReverseEngineering.Phase15VerificationRunner.RunVerificationAsync()).GetAwaiter().GetResult();
            Shutdown(ok ? 0 : 1);
            return;
        }

        if (e.Args.Any(a => a.Equals("--phase14", StringComparison.OrdinalIgnoreCase) ||
                            a.Equals("-p14", StringComparison.OrdinalIgnoreCase) ||
                            a.Equals("--isp-discovery", StringComparison.OrdinalIgnoreCase)))
        {
            if (!AttachConsole(ATTACH_PARENT_PROCESS))
            {
                AllocConsole();
            }

            var stdHandle = GetStdHandle(STD_OUTPUT_HANDLE);
            var safeHandle = new Microsoft.Win32.SafeHandles.SafeFileHandle(stdHandle, ownsHandle: false);
            var fs = new System.IO.FileStream(safeHandle, System.IO.FileAccess.Write);
            var stdOut = new System.IO.StreamWriter(fs, System.Text.Encoding.UTF8) { AutoFlush = true };
            Console.SetOut(stdOut);
            Console.SetError(stdOut);

            SynchronizationContext.SetSynchronizationContext(null);

            bool ok = Task.Run(async () => await WebcamLab.Core.ReverseEngineering.Phase14InvestigationRunner.RunInvestigationAsync()).GetAwaiter().GetResult();
            Shutdown(ok ? 0 : 1);
            return;
        }

        if (e.Args.Any(a => a.Equals("--concurrent-test", StringComparison.OrdinalIgnoreCase)))
        {
            if (!AttachConsole(ATTACH_PARENT_PROCESS))
            {
                AllocConsole();
            }

            var stdHandle = GetStdHandle(STD_OUTPUT_HANDLE);
            var safeHandle = new Microsoft.Win32.SafeHandles.SafeFileHandle(stdHandle, ownsHandle: false);
            var fs = new System.IO.FileStream(safeHandle, System.IO.FileAccess.Write);
            var stdOut = new System.IO.StreamWriter(fs, System.Text.Encoding.UTF8) { AutoFlush = true };
            Console.SetOut(stdOut);
            Console.SetError(stdOut);

            SynchronizationContext.SetSynchronizationContext(null);

            Task.Run(async () => await WebcamLab.Scratch.ConcurrentTest.Run()).GetAwaiter().GetResult();
            Shutdown(0);
            return;
        }

        if (e.Args.Any(a => a.Equals("--audio-devices", StringComparison.OrdinalIgnoreCase) ||
                            a.Equals("--audio", StringComparison.OrdinalIgnoreCase)))
        {
            if (!AttachConsole(ATTACH_PARENT_PROCESS))
            {
                AllocConsole();
            }

            var stdHandle = GetStdHandle(STD_OUTPUT_HANDLE);
            var safeHandle = new Microsoft.Win32.SafeHandles.SafeFileHandle(stdHandle, ownsHandle: false);
            var fs = new System.IO.FileStream(safeHandle, System.IO.FileAccess.Write);
            var stdOut = new System.IO.StreamWriter(fs, System.Text.Encoding.UTF8) { AutoFlush = true };
            Console.SetOut(stdOut);
            Console.SetError(stdOut);

            SynchronizationContext.SetSynchronizationContext(null);

            var devs = Task.Run(async () => await WebcamLab.Core.Audio.AudioDeviceService.EnumerateCaptureDevicesAsync()).GetAwaiter().GetResult();
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"Discovered {devs.Count} Active Audio Capture Endpoint(s):");
            Console.WriteLine($"Discovered {devs.Count} Active Audio Capture Endpoint(s):");
            for (int i = 0; i < devs.Count; i++)
            {
                var d = devs[i];
                sb.AppendLine($"  [{i + 1}] {d.FriendlyName}");
                sb.AppendLine($"      ID:          {d.Id}");
                sb.AppendLine($"      Default:     {d.IsDefault}");
                sb.AppendLine($"      Format:      {d.FormatSummary}");
                sb.AppendLine($"      Dell Webcam: {d.IsDellUZ2315H}");
                Console.WriteLine($"  [{i + 1}] {d.FriendlyName}");
                Console.WriteLine($"      ID:          {d.Id}");
                Console.WriteLine($"      Default:     {d.IsDefault}");
                Console.WriteLine($"      Format:      {d.FormatSummary}");
                Console.WriteLine($"      Dell Webcam: {d.IsDellUZ2315H}");
            }
            try
            {
                System.IO.File.WriteAllText(System.IO.Path.Combine(AppContext.BaseDirectory, "AudioDevices.txt"), sb.ToString());
            }
            catch { }
            Shutdown(0);
            return;
        }

        if (e.Args.Any(a => a.Equals("--test-audio-capture", StringComparison.OrdinalIgnoreCase)))
        {
            if (!AttachConsole(ATTACH_PARENT_PROCESS))
            {
                AllocConsole();
            }

            var stdHandle = GetStdHandle(STD_OUTPUT_HANDLE);
            var safeHandle = new Microsoft.Win32.SafeHandles.SafeFileHandle(stdHandle, ownsHandle: false);
            var fs = new System.IO.FileStream(safeHandle, System.IO.FileAccess.Write);
            var stdOut = new System.IO.StreamWriter(fs, System.Text.Encoding.UTF8) { AutoFlush = true };
            Console.SetOut(stdOut);
            Console.SetError(stdOut);

            SynchronizationContext.SetSynchronizationContext(null);

            try
            {
                string wavPath = System.IO.Path.Combine(AppContext.BaseDirectory, "test_capture.wav");
                var svc = new WebcamLab.Capture.Audio.WasapiCaptureService();
                Console.WriteLine("Starting 2-second audio capture test...");
                svc.Start(outputWavPath: wavPath);
                Thread.Sleep(2000);
                svc.Stop();
                var stats = svc.GetStats();
                var fi = new System.IO.FileInfo(wavPath);
                var sb = new System.Text.StringBuilder();
                sb.AppendLine("Audio Capture Test Results:");
                sb.AppendLine($"  WAV Path:         {wavPath}");
                sb.AppendLine($"  File Size:        {fi.Length} bytes");
                sb.AppendLine($"  Total Frames:     {stats.TotalFramesRead}");
                sb.AppendLine($"  Duration:         {stats.DurationSeconds:F3} s");
                sb.AppendLine($"  Sample Rate:      {stats.EstimatedSampleRate} Hz");
                sb.AppendLine($"  Channels:         {svc.Channels}");
                sb.AppendLine($"  Discontinuities:  {stats.DiscontinuitiesCount}");
                sb.AppendLine($"  Initial QPC:      {stats.InitialQpcPosition}");
                sb.AppendLine($"  Final QPC:        {stats.FinalQpcPosition}");
                sb.AppendLine($"  Initial DevPos:   {stats.InitialDevicePosition}");
                sb.AppendLine($"  Final DevPos:     {stats.FinalDevicePosition}");
                Console.WriteLine(sb.ToString());
                System.IO.File.WriteAllText(System.IO.Path.Combine(AppContext.BaseDirectory, "AudioCaptureTest.txt"), sb.ToString());
            }
            catch (Exception ex)
            {
                System.IO.File.WriteAllText(System.IO.Path.Combine(AppContext.BaseDirectory, "AudioCaptureError.txt"), ex.ToString());
            }
            Shutdown(0);
            return;
        }

        new MainWindow().Show();
        base.OnStartup(e);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try
        {
            WebcamLab.Core.Session.WorkstationSessionManager.ClearActiveExecutionState(out _);
        }
        catch { }
        base.OnExit(e);
    }
}
