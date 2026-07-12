using System;
using System.Numerics;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Effects;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Hosting;
using Windows.UI;

namespace Kanban_WinUI.Services
{
    /// <summary>
    /// Liquid glass in the style of the Sedela dashboard reference: calm, static frosted
    /// panels. Each panel hosts a compositor SpriteVisual with a Win2D effect graph —
    /// backdrop capture → soft gaussian blur → gentle saturation lift → warm dark tint —
    /// clipped to rounded corners. The colorful ambient glow layer behind the board
    /// (MainWindow) bleeds through the frost, which is what gives the glass its life.
    /// Compositor-thread only; adapts to light/dark; XAML brushes remain as fallback.
    /// </summary>
    public static class LiquidGlass
    {
        public static void Attach(FrameworkElement host, float cornerRadius = 8f)
        {
            Apply(host, cornerRadius);
            host.ActualThemeChanged += (_, _) => Apply(host, cornerRadius);
            host.SizeChanged += (_, _) => Apply(host, cornerRadius);
        }

        private static void Apply(FrameworkElement host, float cornerRadius)
        {
            try
            {
                if (host.ActualWidth <= 0 || host.ActualHeight <= 0)
                    return;

                var compositor = ElementCompositionPreview.GetElementVisual(host).Compositor;
                bool dark = host.ActualTheme == ElementTheme.Dark;
                var tint = dark
                    ? Color.FromArgb(0xB6, 0x1C, 0x18, 0x16)   // deep warm frost (reference-like)
                    : Color.FromArgb(0xA0, 0xFB, 0xFA, 0xF9);  // warm light frost

                var graph = new CompositeEffect
                {
                    Mode = CanvasComposite.SourceOver,
                    Sources =
                    {
                        new SaturationEffect
                        {
                            Saturation = 1.2f,
                            Source = new GaussianBlurEffect
                            {
                                BlurAmount = 30f,
                                BorderMode = EffectBorderMode.Hard,
                                Source = new CompositionEffectSourceParameter("backdrop")
                            }
                        },
                        new ColorSourceEffect { Color = tint }
                    }
                };

                var brush = compositor.CreateEffectFactory(graph).CreateBrush();
                brush.SetSourceParameter("backdrop", compositor.CreateBackdropBrush());

                var sprite = compositor.CreateSpriteVisual();
                sprite.Brush = brush;
                sprite.RelativeSizeAdjustment = Vector2.One;

                var rounded = compositor.CreateRoundedRectangleGeometry();
                rounded.CornerRadius = new Vector2(cornerRadius);
                rounded.Size = new Vector2((float)host.ActualWidth, (float)host.ActualHeight);
                sprite.Clip = compositor.CreateGeometricClip(rounded);

                ElementCompositionPreview.SetElementChildVisual(host, sprite);
            }
            catch
            {
                // Effect graph unavailable (very old GPU/driver): XAML brushes remain as fallback
            }
        }
    }
}
