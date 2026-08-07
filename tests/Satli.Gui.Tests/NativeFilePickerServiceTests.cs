using System.Reflection;
using System.Runtime.InteropServices;
using Satli_Gui.Services;
using Xunit;

namespace Satli_Gui.Tests;

public sealed class NativeFilePickerServiceTests
{
    [Fact]
    public void OpenFileNameHasAStableNativeLayout()
    {
        var type = typeof(NativeFilePickerService).GetNestedType(
            "OpenFileName",
            BindingFlags.NonPublic);

        Assert.NotNull(type);
        Assert.True(Marshal.SizeOf(type) > 0);
    }
}
