using System.Runtime.CompilerServices;
using AniSprinkles.Utilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AniSprinkles.Views;

public partial class MediaDetailsLoadedContentView : ContentView
{
    private readonly ILogger<MediaDetailsLoadedContentView>? _logger;
    private readonly int _viewId;

    public MediaDetailsLoadedContentView()
    {
        InitializeComponent();

        _viewId = RuntimeHelpers.GetHashCode(this);
        try
        {
            _logger = ServiceProviderHelper.GetServiceProvider()
                .GetService<ILoggerFactory>()?.CreateLogger<MediaDetailsLoadedContentView>();
        }
        catch (InvalidOperationException)
        {
        }

        _logger?.LogInformation("LOADEDVIEW MediaDetails[#{ViewId:X}] constructed", _viewId);
    }

    protected override void OnHandlerChanged()
    {
        base.OnHandlerChanged();
        _logger?.LogInformation(
            "LOADEDVIEW MediaDetails[#{ViewId:X}] OnHandlerChanged (handler={HasHandler})",
            _viewId, Handler is not null);
    }
}
