using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Satli_Gui.Services;

public static class NativeFilePickerService
{
    private const uint Explorer = 0x00080000;
    private const uint HideReadOnly = 0x00000004;
    private const uint PathMustExist = 0x00000800;
    private const uint FileMustExist = 0x00001000;
    private const uint OverwritePrompt = 0x00000002;
    private const int MaximumPathLength = 32768;

    public static string? PickSaveFile(
        nint owner,
        string title,
        string suggestedFileName,
        string filterLabel,
        string extension)
    {
        var normalizedExtension = NormalizeExtension(extension);
        return Show(
            owner,
            title,
            suggestedFileName,
            BuildFilter((filterLabel, $"*.{normalizedExtension}")),
            normalizedExtension,
            OverwritePrompt,
            save: true);
    }

    public static string? PickOpenFile(
        nint owner,
        string title,
        params (string Label, string Pattern)[] filters)
    {
        return Show(
            owner,
            title,
            string.Empty,
            BuildFilter(filters),
            defaultExtension: null,
            additionalFlags: FileMustExist,
            save: false);
    }

    private static string? Show(
        nint owner,
        string title,
        string suggestedFileName,
        string filter,
        string? defaultExtension,
        uint additionalFlags,
        bool save)
    {
        using var memory = new UnmanagedMemory();
        var downloads = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "Downloads");
        var options = new OpenFileName
        {
            lStructSize = checked((uint)Marshal.SizeOf<OpenFileName>()),
            hwndOwner = owner,
            lpstrFilter = memory.String(filter),
            nFilterIndex = 1,
            lpstrFile = memory.FileBuffer(suggestedFileName),
            nMaxFile = MaximumPathLength,
            lpstrInitialDir = memory.String(Directory.Exists(downloads) ? downloads : null),
            lpstrTitle = memory.String(title),
            lpstrDefExt = memory.String(defaultExtension),
            Flags = Explorer | HideReadOnly | PathMustExist | additionalFlags,
        };
        var succeeded = save
            ? GetSaveFileName(ref options)
            : GetOpenFileName(ref options);
        if (succeeded)
        {
            return Marshal.PtrToStringUni(options.lpstrFile);
        }
        var error = CommDlgExtendedError();
        if (error == 0)
        {
            return null;
        }
        throw new Win32Exception(
            unchecked((int)error),
            $"系统文件选择器失败（0x{error:X4}）。");
    }

    private static string BuildFilter(params (string Label, string Pattern)[] filters)
    {
        if (filters.Length == 0)
        {
            throw new ArgumentException("至少需要一个文件类型筛选器。", nameof(filters));
        }
        return string.Join(
            '\0',
            filters.SelectMany(filter => new[] { filter.Label, filter.Pattern })) + "\0\0";
    }

    private static string NormalizeExtension(string extension)
    {
        var normalized = extension.Trim().TrimStart('.');
        if (string.IsNullOrWhiteSpace(normalized)
            || normalized.Any(character => !char.IsAsciiLetterOrDigit(character)))
        {
            throw new ArgumentException("文件扩展名无效。", nameof(extension));
        }
        return normalized;
    }

    [DllImport("comdlg32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetOpenFileNameW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetOpenFileName(ref OpenFileName options);

    [DllImport("comdlg32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetSaveFileNameW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSaveFileName(ref OpenFileName options);

    [DllImport("comdlg32.dll")]
    private static extern uint CommDlgExtendedError();

    [StructLayout(LayoutKind.Sequential)]
    private struct OpenFileName
    {
        public uint lStructSize;
        public nint hwndOwner;
        public nint hInstance;
        public nint lpstrFilter;
        public nint lpstrCustomFilter;
        public uint nMaxCustFilter;
        public uint nFilterIndex;
        public nint lpstrFile;
        public uint nMaxFile;
        public nint lpstrFileTitle;
        public uint nMaxFileTitle;
        public nint lpstrInitialDir;
        public nint lpstrTitle;
        public uint Flags;
        public ushort nFileOffset;
        public ushort nFileExtension;
        public nint lpstrDefExt;
        public nint lCustData;
        public nint lpfnHook;
        public nint lpTemplateName;
        public nint pvReserved;
        public uint dwReserved;
        public uint FlagsEx;
    }

    private sealed class UnmanagedMemory : IDisposable
    {
        private readonly List<nint> _allocations = [];

        public nint String(string? value)
        {
            if (value is null)
            {
                return nint.Zero;
            }
            var pointer = Marshal.StringToCoTaskMemUni(value);
            _allocations.Add(pointer);
            return pointer;
        }

        public nint FileBuffer(string value)
        {
            if (value.Length >= MaximumPathLength)
            {
                throw new ArgumentException("建议文件名过长。", nameof(value));
            }
            var pointer = Marshal.AllocCoTaskMem(MaximumPathLength * sizeof(char));
            _allocations.Add(pointer);
            var characters = value.ToCharArray();
            Marshal.Copy(characters, 0, pointer, characters.Length);
            Marshal.WriteInt16(pointer, characters.Length * sizeof(char), 0);
            return pointer;
        }

        public void Dispose()
        {
            foreach (var pointer in _allocations)
            {
                Marshal.FreeCoTaskMem(pointer);
            }
            _allocations.Clear();
        }
    }
}
