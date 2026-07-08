using System;
using System.Linq;
using AndroidTreeView.App.ViewModels;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform.Storage;

namespace AndroidTreeView.App.Views;

public partial class ScreenMirrorWindow : Window
{
    // Below this movement (in device px) a press+release is treated as a tap, otherwise a swipe.
    private const int SwipeThreshold = 12;
    private const int SwipeDurationMs = 200;

    private (int X, int Y)? _pressPoint;

    public ScreenMirrorWindow()
    {
        InitializeComponent();
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DropEvent, OnDrop);
        Closed += OnClosed;
    }

    // ---- Remote control: map clicks on the mirrored image to device taps / swipes ----------------

    private void OnScreenPointerPressed(object? sender, PointerPressedEventArgs e) =>
        _pressPoint = MapToDevice(sender, e);

    private async void OnScreenPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        var start = _pressPoint;
        _pressPoint = null;

        if (DataContext is not ScreenMirrorViewModel vm || start is null)
        {
            return;
        }

        var end = MapToDevice(sender, e) ?? start;
        var distance = Math.Abs(end.Value.X - start.Value.X) + Math.Abs(end.Value.Y - start.Value.Y);

        if (distance < SwipeThreshold)
        {
            await vm.TapAsync(start.Value.X, start.Value.Y);
        }
        else
        {
            await vm.SwipeAsync(start.Value.X, start.Value.Y, end.Value.X, end.Value.Y, SwipeDurationMs);
        }
    }

    // Translate a pointer position within the Uniform-stretched Image into device pixel coordinates.
    private (int X, int Y)? MapToDevice(object? sender, PointerEventArgs e)
    {
        if (sender is not Image image || DataContext is not ScreenMirrorViewModel vm || vm.Frame is not { } frame)
        {
            return null;
        }

        var pos = e.GetPosition(image);
        double controlW = image.Bounds.Width, controlH = image.Bounds.Height;
        double imageW = frame.PixelSize.Width, imageH = frame.PixelSize.Height;
        if (controlW <= 0 || controlH <= 0 || imageW <= 0 || imageH <= 0)
        {
            return null;
        }

        // Uniform scale keeps aspect ratio and letterboxes; undo the scale + centering offset.
        var scale = Math.Min(controlW / imageW, controlH / imageH);
        var offsetX = (controlW - (imageW * scale)) / 2;
        var offsetY = (controlH - (imageH * scale)) / 2;
        var deviceX = (pos.X - offsetX) / scale;
        var deviceY = (pos.Y - offsetY) / scale;

        if (deviceX < 0 || deviceY < 0 || deviceX > imageW || deviceY > imageH)
        {
            return null; // clicked the letterbox area
        }

        return ((int)deviceX, (int)deviceY);
    }

    // ---- Drag-drop APK install -------------------------------------------------------------------

    private static void OnDragOver(object? sender, DragEventArgs e) =>
        e.DragEffects = e.DataTransfer.Contains(DataFormat.File) ? DragDropEffects.Copy : DragDropEffects.None;

    private async void OnDrop(object? sender, DragEventArgs e)
    {
        if (DataContext is not ScreenMirrorViewModel vm)
        {
            return;
        }

        var files = e.DataTransfer.TryGetFiles();
        if (files is null)
        {
            return;
        }

        foreach (var item in files.OfType<IStorageFile>())
        {
            var path = item.TryGetLocalPath();
            if (!string.IsNullOrEmpty(path) && path.EndsWith(".apk", StringComparison.OrdinalIgnoreCase))
            {
                await vm.InstallApkAsync(path);
            }
        }
    }

    private void OnClosed(object? sender, EventArgs e) =>
        (DataContext as ScreenMirrorViewModel)?.Dispose();
}
