using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using PhotonViewer.Models;

namespace PhotonViewer.Views.Controls;

/// <summary>
/// Virtualized thumbnail strip for efficient gallery navigation.
/// Only loads thumbnails that are currently visible.
/// </summary>
public class VirtualizedThumbnailStrip : Control
{
    private ScrollViewer? _scrollViewer;
    private ItemsControl? _itemsControl;
    
    #region Dependency Properties
    
    public static readonly DependencyProperty ImagesProperty =
        DependencyProperty.Register(
            nameof(Images),
            typeof(IList<ImageInfo>),
            typeof(VirtualizedThumbnailStrip),
            new PropertyMetadata(null, OnImagesChanged));
    
    public static readonly DependencyProperty SelectedIndexProperty =
        DependencyProperty.Register(
            nameof(SelectedIndex),
            typeof(int),
            typeof(VirtualizedThumbnailStrip),
            new FrameworkPropertyMetadata(
                -1,
                FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
                OnSelectedIndexChanged));
    
    public static readonly DependencyProperty ThumbnailSizeProperty =
        DependencyProperty.Register(
            nameof(ThumbnailSize),
            typeof(double),
            typeof(VirtualizedThumbnailStrip),
            new PropertyMetadata(80.0));
    
    public static readonly DependencyProperty ThumbnailSelectedCommandProperty =
        DependencyProperty.Register(
            nameof(ThumbnailSelectedCommand),
            typeof(ICommand),
            typeof(VirtualizedThumbnailStrip));
    
    public IList<ImageInfo>? Images
    {
        get => (IList<ImageInfo>?)GetValue(ImagesProperty);
        set => SetValue(ImagesProperty, value);
    }
    
    public int SelectedIndex
    {
        get => (int)GetValue(SelectedIndexProperty);
        set => SetValue(SelectedIndexProperty, value);
    }
    
    public double ThumbnailSize
    {
        get => (double)GetValue(ThumbnailSizeProperty);
        set => SetValue(ThumbnailSizeProperty, value);
    }
    
    public ICommand? ThumbnailSelectedCommand
    {
        get => (ICommand?)GetValue(ThumbnailSelectedCommandProperty);
        set => SetValue(ThumbnailSelectedCommandProperty, value);
    }
    
    #endregion
    
    static VirtualizedThumbnailStrip()
    {
        DefaultStyleKeyProperty.OverrideMetadata(
            typeof(VirtualizedThumbnailStrip),
            new FrameworkPropertyMetadata(typeof(VirtualizedThumbnailStrip)));
    }
    
    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        
        _scrollViewer = GetTemplateChild("PART_ScrollViewer") as ScrollViewer;
        _itemsControl = GetTemplateChild("PART_ItemsControl") as ItemsControl;
        
        if (_scrollViewer != null)
        {
            _scrollViewer.ScrollChanged += OnScrollChanged;
        }
    }
    
    private static void OnImagesChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is VirtualizedThumbnailStrip strip)
        {
            strip.RefreshThumbnails();
        }
    }
    
    private static void OnSelectedIndexChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is VirtualizedThumbnailStrip strip)
        {
            strip.ScrollToSelected();
        }
    }
    
    private void OnScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        // Load visible thumbnails on scroll
        LoadVisibleThumbnails();
    }
    
    private void RefreshThumbnails()
    {
        // Trigger re-virtualization
        if (_itemsControl != null)
        {
            _itemsControl.ItemsSource = Images;
        }
    }
    
    private void ScrollToSelected()
    {
        if (_scrollViewer == null || Images == null || SelectedIndex < 0) return;
        
        var itemWidth = ThumbnailSize + 8; // Include margin
        var targetOffset = SelectedIndex * itemWidth - (_scrollViewer.ViewportWidth / 2) + (itemWidth / 2);
        
        _scrollViewer.ScrollToHorizontalOffset(Math.Max(0, targetOffset));
    }
    
    private void LoadVisibleThumbnails()
    {
        // In a full implementation, this would trigger async thumbnail loading
        // for items that become visible during scrolling
    }
}

/// <summary>
/// Template for the VirtualizedThumbnailStrip control.
/// Place this in a ResourceDictionary for themes.
/// </summary>
public static class ThumbnailStripTemplates
{
    public static readonly string Template = """
        <ControlTemplate TargetType="{x:Type local:VirtualizedThumbnailStrip}">
            <Border Background="#252526" 
                    BorderBrush="#3F3F46" 
                    BorderThickness="0,1,0,0"
                    Height="100">
                <ScrollViewer x:Name="PART_ScrollViewer"
                              HorizontalScrollBarVisibility="Auto"
                              VerticalScrollBarVisibility="Disabled">
                    <ItemsControl x:Name="PART_ItemsControl"
                                  VirtualizingStackPanel.IsVirtualizing="True"
                                  VirtualizingStackPanel.VirtualizationMode="Recycling">
                        <ItemsControl.ItemsPanel>
                            <ItemsPanelTemplate>
                                <VirtualizingStackPanel Orientation="Horizontal" />
                            </ItemsPanelTemplate>
                        </ItemsControl.ItemsPanel>
                        <ItemsControl.ItemTemplate>
                            <DataTemplate>
                                <Border Width="{Binding ThumbnailSize, RelativeSource={RelativeSource AncestorType=local:VirtualizedThumbnailStrip}}"
                                        Height="{Binding ThumbnailSize, RelativeSource={RelativeSource AncestorType=local:VirtualizedThumbnailStrip}}"
                                        Margin="4"
                                        CornerRadius="4"
                                        Background="#3F3F46"
                                        Cursor="Hand">
                                    <TextBlock Text="{Binding FileName}"
                                               Foreground="#9D9D9D"
                                               FontSize="9"
                                               TextTrimming="CharacterEllipsis"
                                               VerticalAlignment="Bottom"
                                               Margin="4" />
                                </Border>
                            </DataTemplate>
                        </ItemsControl.ItemTemplate>
                    </ItemsControl>
                </ScrollViewer>
            </Border>
        </ControlTemplate>
        """;
}
