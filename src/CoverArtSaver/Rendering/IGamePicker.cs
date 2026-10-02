using System.Windows;
using CoverArtSaver.Core;

namespace CoverArtSaver.Rendering;

/// <summary>A view whose covers can be clicked to open their game.</summary>
internal interface IGamePicker
{
    /// <summary>The game whose cover is at <paramref name="point"/> (relative to the view), or null for empty space.</summary>
    GameEntry? GameAt(Point point);
}
