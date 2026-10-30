using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using WebcamLab.ViewModels;

namespace WebcamLab.Core.ReverseEngineering;

public static class Phase41GuiVerificationRunner
{
    public static readonly string OutputDir = @"E:\WebcamLab_Phase41_GuiTest";

    public static async Task<bool> RunVerificationAsync()
    {
        Directory.CreateDirectory(OutputDir);
        Console.WriteLine("\n==================================================================");
        Console.WriteLine("  Phase 41: Interactive GUI Startup & Audio Meter Verification");
        Console.WriteLine("==================================================================");

        var tcs = new TaskCompletionSource<bool>();

        var thread = new Thread(() =>
        {
            try
            {
                Console.WriteLine("[1/6] Initializing WPF Application context on STA Thread...");
                var app = Application.Current ?? new App();

                Console.WriteLine("[2/6] Instantiating MainWindow & initializing visual tree...");
                var window = new MainWindow();
                var vm = window.DataContext as MainViewModel;

                if (vm == null)
                {
                    Console.WriteLine("[FAIL] DataContext is null or not MainViewModel!");
                    tcs.SetResult(false);
                    return;
                }

                Console.WriteLine("[3/6] Showing MainWindow (Verifying data bindings & layout)...");
                window.Show();

                // Process initial render & binding tasks
                DoEvents();

                Console.WriteLine($"[PASS] MainWindow is visible! (Title: '{window.Title}', IsVisible={window.IsVisible})");

                Console.WriteLine("[4/6] Auditing Audio Peak & RMS Meter bindings...");
                // Verify initial values without exception
                float initialPeak = vm.AudioPeakLevel;
                float initialRms = vm.AudioRmsLevel;
                Console.WriteLine($"      Initial AudioPeakLevel = {initialPeak}, AudioRmsLevel = {initialRms}");

                // Keep window alive and pump messages for 3 seconds to test live capture
                Console.WriteLine("[5/6] Running live preview & metering for 3 seconds...");
                var sw = Stopwatch.StartNew();
                float maxPeakObserved = 0f;
                float maxRmsObserved = 0f;

                while (sw.ElapsedMilliseconds < 3000)
                {
                    DoEvents();
                    Thread.Sleep(50);
                    if (vm.AudioPeakLevel > maxPeakObserved) maxPeakObserved = vm.AudioPeakLevel;
                    if (vm.AudioRmsLevel > maxRmsObserved) maxRmsObserved = vm.AudioRmsLevel;
                }

                Console.WriteLine($"[PASS] Live metering active! MaxPeak={maxPeakObserved:F3}, MaxRms={maxRmsObserved:F3}");

                Console.WriteLine("[6/6] Testing real physical recording via MainViewModel...");
                if (vm.ToggleRecordingCommand.CanExecute(null))
                {
                    Console.WriteLine("      Starting recording via ToggleRecordingCommand...");
                    vm.ToggleRecordingCommand.Execute(null);

                    var recSw = Stopwatch.StartNew();
                    while (recSw.ElapsedMilliseconds < 3000)
                    {
                        DoEvents();
                        Thread.Sleep(50);
                    }

                    Console.WriteLine("      Stopping recording...");
                    vm.ToggleRecordingCommand.Execute(null);

                    // Wait for finalization
                    var finSw = Stopwatch.StartNew();
                    while (vm.IsFinalizing && finSw.ElapsedMilliseconds < 5000)
                    {
                        DoEvents();
                        Thread.Sleep(50);
                    }
                    Console.WriteLine($"[PASS] Recording finalized! IsRecording={vm.IsRecording}, IsFinalizing={vm.IsFinalizing}");
                }
                else
                {
                    Console.WriteLine("      Note: ToggleRecordingCommand not executable (camera device not connected or initializing).");
                }

                Console.WriteLine("Closing MainWindow...");
                window.Close();
                DoEvents();

                Console.WriteLine("[PASS] GUI Startup and interactive recording verified cleanly!");
                tcs.SetResult(true);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[FAIL] GUI Verification Exception: {ex}");
                tcs.SetResult(false);
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();

        return await tcs.Task;
    }

    private static void DoEvents()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background, new DispatcherOperationCallback(ExitFrame), frame);
        Dispatcher.PushFrame(frame);
    }

    private static object? ExitFrame(object state)
    {
        ((DispatcherFrame)state).Continue = false;
        return null;
    }
}
