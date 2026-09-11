using System.Buffers.Binary;
using System.Runtime.InteropServices;

namespace Steamworks;

/// <summary>
/// The Steam Workshop calls the CS2 workshop manager makes, on ISteamUGC.
/// </summary>
public sealed class SteamUGC
{
    /// <summary>ISteamUGC version whose layout is used below, the client keeps serving old versions.</summary>
    public const string InterfaceVersion = "STEAMUGC_INTERFACE_VERSION020";

    /// <summary>MAX_PATH, the folder buffer the workshop manager hands to GetItemInstallInfo.</summary>
    private const int InstallFolderBufferSize = 260;

    /// <summary>ISteamUGC vtable slots, in the order of isteamugc.h.</summary>
    private enum Slot
    {
        CreateItem = 44,
        StartItemUpdate = 45,
        SetItemTitle = 46,
        SetItemDescription = 47,
        SetItemVisibility = 50,
        SetItemTags = 51,
        SetItemContent = 52,
        SetItemPreview = 53,
        SubmitItemUpdate = 66,
        GetItemUpdateProgress = 67,
        GetItemInstallInfo = 77,
    }

    private readonly nint instance;

    internal SteamUGC(nint instance)
    {
        this.instance = instance;
    }

    private unsafe void** VTable => SteamClient.VTable(instance);

    /// <returns>Call handle for <see cref="SteamClient.WaitForCallResultAsync{T}"/> with <see cref="CreateItemResult"/>.</returns>
    public unsafe ulong CreateItem(uint appId, EWorkshopFileType fileType)
    {
        return ((delegate* unmanaged<void*, uint, int, ulong>)VTable[(int)Slot.CreateItem])((void*)instance, appId, (int)fileType);
    }

    public unsafe ulong StartItemUpdate(uint appId, ulong publishedFileId)
    {
        return ((delegate* unmanaged<void*, uint, ulong, ulong>)VTable[(int)Slot.StartItemUpdate])((void*)instance, appId, publishedFileId);
    }

    public bool SetItemTitle(ulong handle, string title)
    {
        return CallWithString(Slot.SetItemTitle, handle, title);
    }

    public bool SetItemDescription(ulong handle, string description)
    {
        return CallWithString(Slot.SetItemDescription, handle, description);
    }

    public unsafe bool SetItemVisibility(ulong handle, ERemoteStoragePublishedFileVisibility visibility)
    {
        return ((delegate* unmanaged<void*, ulong, int, byte>)VTable[(int)Slot.SetItemVisibility])((void*)instance, handle, (int)visibility) != 0;
    }

    public unsafe bool SetItemTags(ulong handle, IReadOnlyList<string> tags, bool allowAdminTags = false)
    {
        var strings = new nint[tags.Count];

        try
        {
            for (var i = 0; i < strings.Length; i++)
            {
                strings[i] = Marshal.StringToCoTaskMemUTF8(tags[i]);
            }

            fixed (nint* pointers = strings)
            {
                var array = new StringArray
                {
                    Strings = (byte**)pointers,
                    Count = strings.Length,
                };

                return ((delegate* unmanaged<void*, ulong, StringArray*, byte, byte>)VTable[(int)Slot.SetItemTags])((void*)instance, handle, &array, allowAdminTags ? (byte)1 : (byte)0) != 0;
            }
        }
        finally
        {
            foreach (var value in strings)
            {
                Marshal.FreeCoTaskMem(value);
            }
        }
    }

    public bool SetItemContent(ulong handle, string contentFolder)
    {
        return CallWithString(Slot.SetItemContent, handle, contentFolder);
    }

    public bool SetItemPreview(ulong handle, string previewFile)
    {
        return CallWithString(Slot.SetItemPreview, handle, previewFile);
    }

    /// <returns>Call handle for <see cref="SteamClient.WaitForCallResultAsync{T}"/> with <see cref="SubmitItemUpdateResult"/>.</returns>
    public unsafe ulong SubmitItemUpdate(ulong handle, string? changeNote)
    {
        fixed (byte* text = changeNote != null ? SteamClient.NullTerminated(changeNote) : null)
        {
            return ((delegate* unmanaged<void*, ulong, byte*, ulong>)VTable[(int)Slot.SubmitItemUpdate])((void*)instance, handle, text);
        }
    }

    public unsafe ItemUpdateProgress GetItemUpdateProgress(ulong handle)
    {
        ulong bytesProcessed;
        ulong bytesTotal;

        var status = ((delegate* unmanaged<void*, ulong, ulong*, ulong*, int>)VTable[(int)Slot.GetItemUpdateProgress])((void*)instance, handle, &bytesProcessed, &bytesTotal);

        return new ItemUpdateProgress((EItemUpdateStatus)status, bytesProcessed, bytesTotal);
    }

    /// <summary>
    /// The folder an item is installed to, GetItemInstallInfo, or null when it is not installed.
    /// </summary>
    public unsafe string? GetItemInstallFolder(ulong publishedFileId)
    {
        var folder = stackalloc byte[InstallFolderBufferSize];
        ulong sizeOnDisk;
        uint timestamp;

        var installed = ((delegate* unmanaged<void*, ulong, ulong*, byte*, uint, uint*, byte>)VTable[(int)Slot.GetItemInstallInfo])((void*)instance, publishedFileId, &sizeOnDisk, folder, InstallFolderBufferSize, &timestamp) != 0;

        return installed ? Marshal.PtrToStringUTF8((nint)folder) : null;
    }

    private unsafe bool CallWithString(Slot slot, ulong handle, string value)
    {
        fixed (byte* text = SteamClient.NullTerminated(value))
        {
            return ((delegate* unmanaged<void*, ulong, byte*, byte>)VTable[(int)slot])((void*)instance, handle, text) != 0;
        }
    }

    /// <summary>SteamParamStringArray_t</summary>
    [StructLayout(LayoutKind.Sequential)]
    private unsafe struct StringArray
    {
        public byte** Strings;
        public int Count;
    }
}

public readonly record struct ItemUpdateProgress(EItemUpdateStatus Status, ulong BytesProcessed, ulong BytesTotal);

/// <summary>CreateItemResult_t</summary>
public readonly record struct CreateItemResult(EResult Result, ulong PublishedFileId, bool UserNeedsToAcceptWorkshopLegalAgreement) : ICallResult<CreateItemResult>
{
    public static CreateItemResult Read(ReadOnlySpan<byte> data)
    {
        // EResult m_eResult; PublishedFileId_t m_nPublishedFileId; bool m_bUserNeedsToAcceptWorkshopLegalAgreement;
        var publishedFileIdOffset = SteamClient.StructPack;

        return new CreateItemResult(
            (EResult)BinaryPrimitives.ReadInt32LittleEndian(data),
            BinaryPrimitives.ReadUInt64LittleEndian(data[publishedFileIdOffset..]),
            data[publishedFileIdOffset + sizeof(ulong)] != 0);
    }
}

/// <summary>SubmitItemUpdateResult_t</summary>
public readonly record struct SubmitItemUpdateResult(EResult Result, bool UserNeedsToAcceptWorkshopLegalAgreement, ulong PublishedFileId) : ICallResult<SubmitItemUpdateResult>
{
    public static SubmitItemUpdateResult Read(ReadOnlySpan<byte> data)
    {
        // EResult m_eResult; bool m_bUserNeedsToAcceptWorkshopLegalAgreement; PublishedFileId_t m_nPublishedFileId;
        return new SubmitItemUpdateResult(
            (EResult)BinaryPrimitives.ReadInt32LittleEndian(data),
            data[sizeof(int)] != 0,
            BinaryPrimitives.ReadUInt64LittleEndian(data[(2 * sizeof(int))..]));
    }
}

public enum EWorkshopFileType
{
    Community = 0,
    Microtransaction = 1,
    Collection = 2,
    Art = 3,
    Video = 4,
    Screenshot = 5,
    Game = 6,
    Software = 7,
    Concept = 8,
    WebGuide = 9,
    IntegratedGuide = 10,
    Merch = 11,
    ControllerBinding = 12,
    SteamworksAccessInvite = 13,
    SteamVideo = 14,
    GameManagedItem = 15,
    Clip = 16,
}

public enum ERemoteStoragePublishedFileVisibility
{
    k_ERemoteStoragePublishedFileVisibilityPublic = 0,
    k_ERemoteStoragePublishedFileVisibilityFriendsOnly = 1,
    k_ERemoteStoragePublishedFileVisibilityPrivate = 2,
    k_ERemoteStoragePublishedFileVisibilityUnlisted = 3,
}

public enum EItemUpdateStatus
{
    Invalid = 0,
    PreparingConfig = 1,
    PreparingContent = 2,
    UploadingContent = 3,
    UploadingPreviewFile = 4,
    CommittingChanges = 5,
}
