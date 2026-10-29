using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using WebcamLab.ViewModels;

namespace WebcamLab;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        DataContext = new MainViewModel();
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);

        // Do not intercept shortcuts when typing in a text editing control
        if (e.OriginalSource is TextBox || e.OriginalSource is PasswordBox)
        {
            return;
        }

        if (DataContext is MainViewModel vm)
        {
            bool isCtrl = (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control;
            bool isShift = (Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift;
            bool isAlt = (Keyboard.Modifiers & ModifierKeys.Alt) == ModifierKeys.Alt;

            if (isCtrl && !isShift && !isAlt)
            {
                // Ctrl+0: Reset to neutral look
                if (e.Key == Key.D0 || e.Key == Key.NumPad0)
                {
                    vm.ResetToNeutralLook();
                    e.Handled = true;
                    return;
                }

                // Ctrl+1 through Ctrl+9: Recall look by shortcut
                if (e.Key >= Key.D1 && e.Key <= Key.D9)
                {
                    int num = e.Key - Key.D0;
                    if (vm.RecallLookByShortcut($"Ctrl+{num}"))
                    {
                        e.Handled = true;
                        return;
                    }
                }

                if (e.Key >= Key.NumPad1 && e.Key <= Key.NumPad9)
                {
                    int num = e.Key - Key.NumPad1 + 1;
                    if (vm.RecallLookByShortcut($"Ctrl+{num}"))
                    {
                        e.Handled = true;
                        return;
                    }
                }
            }
        }
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (DataContext is IDisposable disposable)
        {
            disposable.Dispose();
        }
        base.OnClosing(e);
    }
}
