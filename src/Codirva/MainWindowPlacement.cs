using System.Diagnostics;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;

namespace Codirva;

internal static class MainWindowPlacement
{
    private sealed record Placement(int X, int Y, double Width, double Height, bool Maximized);

    private static string FilePath =>
        Path.Combine(Path.GetDirectoryName(ConnectionConfig.PathName)!, "window-placement.json");

    public static void Remember(Window window)
    {
        Placement? normalBounds = Restore(window);
        var maximized = window.WindowState == WindowState.Maximized;

        void CaptureNormalBounds()
        {
            if (window.WindowState == WindowState.Normal && window.Bounds.Width > 0 && window.Bounds.Height > 0)
                normalBounds = new Placement(window.Position.X, window.Position.Y,
                    window.Bounds.Width, window.Bounds.Height, false);
        }

        window.Opened += (_, _) => CaptureNormalBounds();
        window.PositionChanged += (_, _) => CaptureNormalBounds();
        window.SizeChanged += (_, _) => CaptureNormalBounds();
        window.PropertyChanged += (_, change) =>
        {
            if (change.Property == Window.WindowStateProperty && window.WindowState != WindowState.Minimized)
                maximized = window.WindowState == WindowState.Maximized;
        };
        window.Closing += (_, _) =>
        {
            CaptureNormalBounds();
            if (normalBounds is null)
                return;

            Save(normalBounds with { Maximized = maximized });
        };
    }

    private static Placement? Restore(Window window)
    {
        try
        {
            if (!File.Exists(FilePath))
                return null;

            var saved = JsonSerializer.Deserialize<Placement>(File.ReadAllText(FilePath));
            if (saved is null || !double.IsFinite(saved.Width) || !double.IsFinite(saved.Height)
                || saved.Width <= 0 || saved.Height <= 0)
                return null;

            var screen = window.Screens.ScreenFromPoint(new PixelPoint(saved.X, saved.Y))
                ?? window.Screens.Primary;
            if (screen is null)
                return null;

            var area = screen.WorkingArea;
            if (area.Width <= 0 || area.Height <= 0 || screen.Scaling <= 0)
                return null;

            window.Width = Math.Min(Math.Max(window.MinWidth, saved.Width), area.Width / screen.Scaling);
            window.Height = Math.Min(Math.Max(window.MinHeight, saved.Height), area.Height / screen.Scaling);
            var width = (int)Math.Ceiling(window.Width * screen.Scaling);
            var height = (int)Math.Ceiling(window.Height * screen.Scaling);
            var x = Math.Clamp(saved.X, area.X, area.X + Math.Max(0, area.Width - width));
            var y = Math.Clamp(saved.Y, area.Y, area.Y + Math.Max(0, area.Height - height));

            window.WindowStartupLocation = WindowStartupLocation.Manual;
            window.Position = new PixelPoint(x, y);
            if (saved.Maximized)
                window.WindowState = WindowState.Maximized;

            return new Placement(x, y, window.Width, window.Height, saved.Maximized);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
        {
            Trace.TraceWarning("Could not restore Codirva window placement: {0}", error.Message);
            return null;
        }
    }

    private static void Save(Placement placement)
    {
        var temporaryPath = FilePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(placement));
            File.Move(temporaryPath, FilePath, overwrite: true);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            Trace.TraceWarning("Could not save Codirva window placement: {0}", error.Message);
        }
        finally
        {
            try { File.Delete(temporaryPath); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                Trace.TraceWarning("Could not clean up temporary window placement: {0}", error.Message);
            }
        }
    }
}
