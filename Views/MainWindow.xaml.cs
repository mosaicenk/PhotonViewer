using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using PhotonViewer.Models;
using PhotonViewer.ViewModels;

namespace PhotonViewer.Views;

public partial class MainWindow : Window
{
    private MainViewModel ViewModel => (MainViewModel)DataContext;

    public MainWindow()
    {
        InitializeComponent();
        
        Loaded += OnLoaded;
        Closing += OnClosing;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // Focus the image viewer for keyboard input
        ImageViewer.Focus();
        
        // Handle command line arguments
        var args = Environment.GetCommandLineArgs();
        if (args.Length > 1 && !string.IsNullOrWhiteSpace(args[1]))
        {
            _ = ViewModel.LoadImageAsync(args[1]);
        }
    }

    private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        ViewModel.Dispose();
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        // Forward keyboard events to viewer if not handled
        if (!e.Handled && ImageViewer.IsFocused == false)
        {
            ImageViewer.Focus();
        }
    }

    #region Drag and Drop

    private void OnDragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            var files = (string[])e.Data.GetData(DataFormats.FileDrop)!;
            
            if (files.Any(ImageInfo.IsSupportedFormat))
            {
                e.Effects = DragDropEffects.Copy;
                DropHint.Visibility = Visibility.Visible;
            }
            else
            {
                e.Effects = DragDropEffects.None;
            }
        }
        else
        {
            e.Effects = DragDropEffects.None;
        }
        
        e.Handled = true;
    }

    private async void OnFileDrop(object sender, DragEventArgs e)
    {
        DropHint.Visibility = Visibility.Collapsed;
        
        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
        
        var files = (string[])e.Data.GetData(DataFormats.FileDrop)!;
        var imageFile = files.FirstOrDefault(ImageInfo.IsSupportedFormat);
        
        if (imageFile != null)
        {
            await ViewModel.LoadImageAsync(imageFile);
        }
    }

    #endregion
}

#region Value Converters

/// <summary>
/// Converts boolean to Visibility (true = Visible, false = Collapsed).
/// </summary>
public class BooleanToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value is true ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value is Visibility.Visible;
    }
}

/// <summary>
/// Converts boolean to inverse Visibility (false = Visible, true = Collapsed).
/// </summary>
public class InverseBooleanToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value is false ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value is Visibility.Collapsed;
    }
}

#endregion
