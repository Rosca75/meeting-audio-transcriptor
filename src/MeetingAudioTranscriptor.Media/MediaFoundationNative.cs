// Every Media Foundation and COM declaration the Windows decoder uses: GUIDs, constants, vtable slot
// indices, struct layouts, the exported functions and one typed call per interface method. Each value
// cites the Windows SDK header it comes from, so a reviewer on Windows can check it line by line.
using System;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace MeetingAudioTranscriptor.Media;

/// <summary>The native surface of Media Foundation, called through function pointers read from each object's vtable.</summary>
/// <remarks>
/// <para>
/// The published application is trimmed and self-contained, so neither built-in COM marshalling nor
/// reflection is used: an interface method is called by reading the function pointer at its slot in
/// the object's vtable and calling it as <c>delegate* unmanaged[Stdcall]</c> (on x64 every convention
/// is the one Microsoft x64 convention, so Stdcall is the same as the default). The first argument of
/// every method is the object itself (<c>this</c>). Exported functions use <c>LibraryImport</c>, whose
/// marshalling is generated at compile time.
/// </para>
/// <para>
/// A vtable lists the base interface's methods first, then each derived interface's methods in
/// declaration order. The complete ordered lists below are those of the SDK headers (mfobjects.h,
/// mfreadwrite.h, Unknwn.h); a slot index is the position in that list counted from zero.
/// </para>
/// <code>
/// IUnknown (Unknwn.h)
///   0 QueryInterface   1 AddRef   2 Release
///
/// IMFAttributes : IUnknown (mfobjects.h)
///   3 GetItem          4 GetItemType      5 CompareItem      6 Compare
///   7 GetUINT32        8 GetUINT64        9 GetDouble       10 GetGUID
///  11 GetStringLength 12 GetString       13 GetAllocatedString
///  14 GetBlobSize     15 GetBlob         16 GetAllocatedBlob 17 GetUnknown
///  18 SetItem         19 DeleteItem      20 DeleteAllItems
///  21 SetUINT32       22 SetUINT64       23 SetDouble       24 SetGUID
///  25 SetString       26 SetBlob         27 SetUnknown
///  28 LockStore       29 UnlockStore     30 GetCount        31 GetItemByIndex
///  32 CopyAllItems
///
/// IMFMediaType : IMFAttributes (mfobjects.h)
///  33 GetMajorType    34 IsCompressedFormat 35 IsEqual      36 GetRepresentation
///  37 FreeRepresentation
///
/// IMFSample : IMFAttributes (mfobjects.h)
///  33 GetSampleFlags  34 SetSampleFlags  35 GetSampleTime   36 SetSampleTime
///  37 GetSampleDuration 38 SetSampleDuration 39 GetBufferCount 40 GetBufferByIndex
///  41 ConvertToContiguousBuffer 42 AddBuffer 43 RemoveBufferByIndex 44 RemoveAllBuffers
///  45 GetTotalLength  46 CopyToBuffer
///
/// IMFMediaBuffer : IUnknown (mfobjects.h)
///   3 Lock             4 Unlock           5 GetCurrentLength 6 SetCurrentLength
///   7 GetMaxLength
///
/// IMFSourceReader : IUnknown (mfreadwrite.h)
///   3 GetStreamSelection  4 SetStreamSelection  5 GetNativeMediaType
///   6 GetCurrentMediaType 7 SetCurrentMediaType 8 SetCurrentPosition
///   9 ReadSample         10 Flush              11 GetServiceForStream
///  12 GetPresentationAttribute
/// </code>
/// </remarks>
[SupportedOSPlatform("windows")]
internal static unsafe partial class MediaFoundationNative
{
    // ---- Vtable slots (see the ordered lists above) ----

    /// <summary><c>IUnknown::Release</c>: <c>ULONG Release()</c>.</summary>
    private const int SlotRelease = 2;

    /// <summary><c>IMFAttributes::GetUINT32</c>: <c>HRESULT GetUINT32(REFGUID guidKey, UINT32 *punValue)</c>.</summary>
    private const int SlotAttributesGetUInt32 = 7;

    /// <summary><c>IMFAttributes::GetGUID</c>: <c>HRESULT GetGUID(REFGUID guidKey, GUID *pguidValue)</c>.</summary>
    private const int SlotAttributesGetGuid = 10;

    /// <summary><c>IMFAttributes::SetUINT32</c>: <c>HRESULT SetUINT32(REFGUID guidKey, UINT32 unValue)</c>.</summary>
    private const int SlotAttributesSetUInt32 = 21;

    /// <summary><c>IMFAttributes::SetGUID</c>: <c>HRESULT SetGUID(REFGUID guidKey, REFGUID guidValue)</c>.</summary>
    private const int SlotAttributesSetGuid = 24;

    /// <summary><c>IMFSample::ConvertToContiguousBuffer</c>: <c>HRESULT ConvertToContiguousBuffer(IMFMediaBuffer **ppBuffer)</c>.</summary>
    private const int SlotSampleConvertToContiguousBuffer = 41;

    /// <summary><c>IMFMediaBuffer::Lock</c>: <c>HRESULT Lock(BYTE **ppbBuffer, DWORD *pcbMaxLength, DWORD *pcbCurrentLength)</c>.</summary>
    private const int SlotBufferLock = 3;

    /// <summary><c>IMFMediaBuffer::Unlock</c>: <c>HRESULT Unlock()</c>.</summary>
    private const int SlotBufferUnlock = 4;

    /// <summary><c>IMFSourceReader::SetStreamSelection</c>: <c>HRESULT SetStreamSelection(DWORD dwStreamIndex, BOOL fSelected)</c>.</summary>
    private const int SlotReaderSetStreamSelection = 4;

    /// <summary><c>IMFSourceReader::GetCurrentMediaType</c>: <c>HRESULT GetCurrentMediaType(DWORD dwStreamIndex, IMFMediaType **ppMediaType)</c>.</summary>
    private const int SlotReaderGetCurrentMediaType = 6;

    /// <summary><c>IMFSourceReader::SetCurrentMediaType</c>: <c>HRESULT SetCurrentMediaType(DWORD dwStreamIndex, DWORD *pdwReserved, IMFMediaType *pMediaType)</c>.</summary>
    private const int SlotReaderSetCurrentMediaType = 7;

    /// <summary>
    /// <c>IMFSourceReader::ReadSample</c>: <c>HRESULT ReadSample(DWORD dwStreamIndex, DWORD dwControlFlags,
    /// DWORD *pdwActualStreamIndex, DWORD *pdwStreamFlags, LONGLONG *pllTimestamp, IMFSample **ppSample)</c>.
    /// </summary>
    private const int SlotReaderReadSample = 9;

    /// <summary>
    /// <c>IMFSourceReader::GetPresentationAttribute</c>: <c>HRESULT GetPresentationAttribute(DWORD dwStreamIndex,
    /// REFGUID guidAttribute, PROPVARIANT *pvarAttribute)</c>.
    /// </summary>
    private const int SlotReaderGetPresentationAttribute = 12;

    // ---- Constants ----

    /// <summary><c>MF_VERSION</c> (mfapi.h): <c>(MF_SDK_VERSION &lt;&lt; 16) | MF_API_VERSION</c> = <c>(0x0002 &lt;&lt; 16) | 0x0070</c>.</summary>
    public const uint MfVersion = 0x00020070;

    /// <summary><c>MFSTARTUP_LITE</c> (mfapi.h): starts Media Foundation without the sockets library, which a local file never needs.</summary>
    public const uint MfStartupLite = 0x1;

    /// <summary><c>COINIT_MULTITHREADED</c> (objbase.h).</summary>
    public const uint CoInitMultithreaded = 0x0;

    /// <summary><c>S_FALSE</c> (winerror.h): COM was already initialised on this thread; the call still needs a CoUninitialize.</summary>
    public const int SFalse = 1;

    /// <summary><c>RPC_E_CHANGED_MODE</c> (winerror.h): the thread is already single-threaded; it is used as it is and not uninitialised.</summary>
    public const int RpcEChangedMode = unchecked((int)0x80010106);

    /// <summary><c>MF_SOURCE_READER_FIRST_AUDIO_STREAM</c> (mfreadwrite.h).</summary>
    public const uint FirstAudioStream = 0xFFFFFFFD;

    /// <summary><c>MF_SOURCE_READER_ALL_STREAMS</c> (mfreadwrite.h).</summary>
    public const uint AllStreams = 0xFFFFFFFE;

    /// <summary><c>MF_SOURCE_READER_MEDIASOURCE</c> (mfreadwrite.h): the stream index that addresses the source itself.</summary>
    public const uint MediaSource = 0xFFFFFFFF;

    /// <summary><c>MF_SOURCE_READERF_ERROR</c> (mfreadwrite.h, MF_SOURCE_READER_FLAG).</summary>
    public const uint ReaderFlagError = 0x00000001;

    /// <summary><c>MF_SOURCE_READERF_ENDOFSTREAM</c> (mfreadwrite.h).</summary>
    public const uint ReaderFlagEndOfStream = 0x00000002;

    /// <summary><c>MF_SOURCE_READERF_CURRENTMEDIATYPECHANGED</c> (mfreadwrite.h).</summary>
    public const uint ReaderFlagCurrentMediaTypeChanged = 0x00000020;

    /// <summary><c>VT_UI8</c> (wtypes.h): the PROPVARIANT type of <c>MF_PD_DURATION</c>.</summary>
    public const ushort VtUI8 = 21;

    // ---- GUIDs (DEFINE_GUID values) ----

    /// <summary><c>MFMediaType_Audio</c> (mfapi.h): FCC('auds') as a FOURCC base GUID, {73647561-0000-0010-8000-00AA00389B71}.</summary>
    public static readonly Guid MediaTypeAudio = new(0x73647561, 0x0000, 0x0010, 0x80, 0x00, 0x00, 0xAA, 0x00, 0x38, 0x9B, 0x71);

    /// <summary><c>MFAudioFormat_Float</c> (mfapi.h): WAVE_FORMAT_IEEE_FLOAT (3) as a base GUID.</summary>
    public static readonly Guid AudioFormatFloat = new(0x00000003, 0x0000, 0x0010, 0x80, 0x00, 0x00, 0xAA, 0x00, 0x38, 0x9B, 0x71);

    /// <summary><c>MFAudioFormat_PCM</c> (mfapi.h): WAVE_FORMAT_PCM (1) as a base GUID.</summary>
    public static readonly Guid AudioFormatPcm = new(0x00000001, 0x0000, 0x0010, 0x80, 0x00, 0x00, 0xAA, 0x00, 0x38, 0x9B, 0x71);

    /// <summary><c>MF_MT_MAJOR_TYPE</c> (mfapi.h): {48eba18e-f8c9-4687-bf11-0a74c9f96a8f}.</summary>
    public static readonly Guid MtMajorType = new(0x48eba18e, 0xf8c9, 0x4687, 0xbf, 0x11, 0x0a, 0x74, 0xc9, 0xf9, 0x6a, 0x8f);

    /// <summary><c>MF_MT_SUBTYPE</c> (mfapi.h): {f7e34c9a-42e8-4714-b74b-cb29d72c35e5}.</summary>
    public static readonly Guid MtSubtype = new(0xf7e34c9a, 0x42e8, 0x4714, 0xb7, 0x4b, 0xcb, 0x29, 0xd7, 0x2c, 0x35, 0xe5);

    /// <summary><c>MF_MT_AUDIO_NUM_CHANNELS</c> (mfapi.h): {37e48bf5-645e-4c5b-89de-ada9e29b696a}.</summary>
    public static readonly Guid MtAudioNumChannels = new(0x37e48bf5, 0x645e, 0x4c5b, 0x89, 0xde, 0xad, 0xa9, 0xe2, 0x9b, 0x69, 0x6a);

    /// <summary><c>MF_MT_AUDIO_SAMPLES_PER_SECOND</c> (mfapi.h): {5faeeae7-0290-4c31-9e8a-c534f68d9dba}.</summary>
    public static readonly Guid MtAudioSamplesPerSecond = new(0x5faeeae7, 0x0290, 0x4c31, 0x9e, 0x8a, 0xc5, 0x34, 0xf6, 0x8d, 0x9d, 0xba);

    /// <summary><c>MF_MT_AUDIO_BITS_PER_SAMPLE</c> (mfapi.h): {f2deb57f-40fa-4764-aa33-ed4f2d1ff669}.</summary>
    public static readonly Guid MtAudioBitsPerSample = new(0xf2deb57f, 0x40fa, 0x4764, 0xaa, 0x33, 0xed, 0x4f, 0x2d, 0x1f, 0xf6, 0x69);

    /// <summary><c>MF_PD_DURATION</c> (mfidl.h): {6c990d33-bb8e-477a-8598-0d5d96fcd88a}, a UINT64 in 100 ns units.</summary>
    public static readonly Guid PdDuration = new(0x6c990d33, 0xbb8e, 0x477a, 0x85, 0x98, 0x0d, 0x5d, 0x96, 0xfc, 0xd8, 0x8a);

    // ---- Structs ----

    /// <summary>
    /// <c>PROPVARIANT</c> (propidlbase.h) on 64-bit Windows: <c>VARTYPE vt</c> (2 bytes), three reserved
    /// WORDs, then an 8-byte-aligned union whose largest member (a BLOB or a DECIMAL overlay) is 16 bytes,
    /// for 24 bytes in all. Only <c>uhVal</c> is read; PropVariantClear frees whatever the union holds.
    /// </summary>
    [StructLayout(LayoutKind.Explicit, Size = 24)]
    public struct PropVariant
    {
        /// <summary>The VARTYPE that says which union member is set.</summary>
        [FieldOffset(0)]
        public ushort VariantType;

        /// <summary>The union read as <c>ULARGE_INTEGER uhVal</c>, valid when the type is VT_UI8.</summary>
        [FieldOffset(8)]
        public ulong UnsignedLong;
    }

    // ---- Exported functions ----

    /// <summary><c>HRESULT CoInitializeEx(LPVOID pvReserved, DWORD dwCoInit)</c> (combaseapi.h, ole32.dll).</summary>
    [LibraryImport("ole32.dll")]
    public static partial int CoInitializeEx(void* reserved, uint coInit);

    /// <summary><c>void CoUninitialize()</c> (combaseapi.h, ole32.dll).</summary>
    [LibraryImport("ole32.dll")]
    public static partial void CoUninitialize();

    /// <summary><c>HRESULT PropVariantClear(PROPVARIANT *pvar)</c> (propidl.h, ole32.dll).</summary>
    [LibraryImport("ole32.dll")]
    public static partial int PropVariantClear(PropVariant* value);

    /// <summary><c>HRESULT MFStartup(ULONG Version, DWORD dwFlags)</c> (mfapi.h, mfplat.dll).</summary>
    [LibraryImport("mfplat.dll")]
    public static partial int MFStartup(uint version, uint flags);

    /// <summary><c>HRESULT MFShutdown()</c> (mfapi.h, mfplat.dll).</summary>
    [LibraryImport("mfplat.dll")]
    public static partial int MFShutdown();

    /// <summary><c>HRESULT MFCreateMediaType(IMFMediaType **ppMFType)</c> (mfapi.h, mfplat.dll).</summary>
    [LibraryImport("mfplat.dll")]
    public static partial int MFCreateMediaType(void** mediaType);

    /// <summary>
    /// <c>HRESULT MFCreateSourceReaderFromURL(LPCWSTR pwszURL, IMFAttributes *pAttributes, IMFSourceReader **ppSourceReader)</c>
    /// (mfreadwrite.h, mfreadwrite.dll). A local file path is accepted as the URL.
    /// </summary>
    [LibraryImport("mfreadwrite.dll", StringMarshalling = StringMarshalling.Utf16)]
    public static partial int MFCreateSourceReaderFromURL(string url, void* attributes, void** sourceReader);

    // ---- Interface methods ----

    /// <summary>Calls <c>IUnknown::Release</c> when the pointer is set, and clears it.</summary>
    public static void Release(ref void* unknown)
    {
        void* instance = unknown;
        unknown = null;
        if (instance != null)
        {
            _ = ((delegate* unmanaged[Stdcall]<void*, uint>)Slot(instance, SlotRelease))(instance);
        }
    }

    /// <summary><c>IMFAttributes::GetUINT32</c> (also on IMFMediaType).</summary>
    public static int GetUInt32(void* attributes, Guid key, out uint value)
    {
        uint result = 0;
        int hr = ((delegate* unmanaged[Stdcall]<void*, Guid*, uint*, int>)Slot(attributes, SlotAttributesGetUInt32))(attributes, &key, &result);
        value = result;
        return hr;
    }

    /// <summary><c>IMFAttributes::GetGUID</c> (also on IMFMediaType).</summary>
    public static int GetGuid(void* attributes, Guid key, out Guid value)
    {
        Guid result = default;
        int hr = ((delegate* unmanaged[Stdcall]<void*, Guid*, Guid*, int>)Slot(attributes, SlotAttributesGetGuid))(attributes, &key, &result);
        value = result;
        return hr;
    }

    /// <summary><c>IMFAttributes::SetUINT32</c> (also on IMFMediaType).</summary>
    public static int SetUInt32(void* attributes, Guid key, uint value) =>
        ((delegate* unmanaged[Stdcall]<void*, Guid*, uint, int>)Slot(attributes, SlotAttributesSetUInt32))(attributes, &key, value);

    /// <summary><c>IMFAttributes::SetGUID</c> (also on IMFMediaType).</summary>
    public static int SetGuid(void* attributes, Guid key, Guid value) =>
        ((delegate* unmanaged[Stdcall]<void*, Guid*, Guid*, int>)Slot(attributes, SlotAttributesSetGuid))(attributes, &key, &value);

    /// <summary><c>IMFSample::ConvertToContiguousBuffer</c>.</summary>
    public static int ConvertToContiguousBuffer(void* sample, void** buffer) =>
        ((delegate* unmanaged[Stdcall]<void*, void**, int>)Slot(sample, SlotSampleConvertToContiguousBuffer))(sample, buffer);

    /// <summary><c>IMFMediaBuffer::Lock</c>; the max-length pointer may be null.</summary>
    public static int Lock(void* buffer, byte** data, uint* maxLength, uint* currentLength) =>
        ((delegate* unmanaged[Stdcall]<void*, byte**, uint*, uint*, int>)Slot(buffer, SlotBufferLock))(buffer, data, maxLength, currentLength);

    /// <summary><c>IMFMediaBuffer::Unlock</c>.</summary>
    public static int Unlock(void* buffer) =>
        ((delegate* unmanaged[Stdcall]<void*, int>)Slot(buffer, SlotBufferUnlock))(buffer);

    /// <summary><c>IMFSourceReader::SetStreamSelection</c>; BOOL is a 32-bit int.</summary>
    public static int SetStreamSelection(void* reader, uint streamIndex, bool selected) =>
        ((delegate* unmanaged[Stdcall]<void*, uint, int, int>)Slot(reader, SlotReaderSetStreamSelection))(reader, streamIndex, selected ? 1 : 0);

    /// <summary><c>IMFSourceReader::GetCurrentMediaType</c>; the caller releases the returned type.</summary>
    public static int GetCurrentMediaType(void* reader, uint streamIndex, void** mediaType) =>
        ((delegate* unmanaged[Stdcall]<void*, uint, void**, int>)Slot(reader, SlotReaderGetCurrentMediaType))(reader, streamIndex, mediaType);

    /// <summary><c>IMFSourceReader::SetCurrentMediaType</c>; the reserved pointer must be null.</summary>
    public static int SetCurrentMediaType(void* reader, uint streamIndex, void* mediaType) =>
        ((delegate* unmanaged[Stdcall]<void*, uint, uint*, void*, int>)Slot(reader, SlotReaderSetCurrentMediaType))(reader, streamIndex, null, mediaType);

    /// <summary><c>IMFSourceReader::ReadSample</c> in synchronous mode; the sample may come back null with only flags set.</summary>
    public static int ReadSample(void* reader, uint streamIndex, uint* actualStreamIndex, uint* streamFlags, long* timestamp, void** sample) =>
        ((delegate* unmanaged[Stdcall]<void*, uint, uint, uint*, uint*, long*, void**, int>)Slot(reader, SlotReaderReadSample))(
            reader, streamIndex, 0, actualStreamIndex, streamFlags, timestamp, sample);

    /// <summary><c>IMFSourceReader::GetPresentationAttribute</c>; the caller clears the PROPVARIANT.</summary>
    public static int GetPresentationAttribute(void* reader, uint streamIndex, Guid attribute, PropVariant* value) =>
        ((delegate* unmanaged[Stdcall]<void*, uint, Guid*, PropVariant*, int>)Slot(reader, SlotReaderGetPresentationAttribute))(reader, streamIndex, &attribute, value);

    /// <summary>Reads the function pointer at a slot of a COM object's vtable: the object's first field points at the table.</summary>
    private static void* Slot(void* instance, int slot) => (*(void***)instance)[slot];
}
