using System.Runtime.CompilerServices;
using HorizonRadioOverlay;

namespace HorizonRadioOverlay.Services;

public static class OverlayWindowUiExtensions
{
    private sealed class OverlayWindowCoverState
    {
        public byte[]? LastCoverBytes;
    }

    private static readonly ConditionalWeakTable<OverlayWindow, OverlayWindowCoverState> State = new();

    public static void UpdateCoverIfChanged(this OverlayWindow window, byte[]? coverBytes)
    {
        OverlayWindowCoverState state = State.GetOrCreateValue(window);
        if (SameBytes(state.LastCoverBytes, coverBytes))
        {
            return;
        }

        state.LastCoverBytes = coverBytes;
        window.UpdateCover(coverBytes);
    }

    private static bool SameBytes(byte[]? left, byte[]? right)
    {
        if (ReferenceEquals(left, right))
        {
            return true;
        }

        if (left is null || right is null || left.Length != right.Length)
        {
            return false;
        }

        return left.AsSpan().SequenceEqual(right);
    }
}
