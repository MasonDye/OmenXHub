/* OmenXHubDrv.c — Self-contained x64 kernel driver for MSR and PCI access.
 * No WDK headers needed — all types and imports defined inline.
 *
 * Build:
 *   CL="D:/VS/VC/Tools/MSVC/<ver>/bin/Hostx64/x64/cl.exe"
 *   WDK_LIB="$HOME/.nuget/packages/microsoft.windows.wdk.x64/<ver>/c/Lib/<ver>/km/x64"
 *   "$CL" -c OmenXHubDrv.c -FoOmenXHubDrv.obj -GS- -O2 -D_AMD64_ -kernel -Zp8
 *   link.exe -OUT:OmenXHubDrv.sys -NOLOGO -DRIVER -SUBSYSTEM:NATIVE -MACHINE:X64
 *            -ENTRY:DriverEntry OmenXHubDrv.obj ntoskrnl.lib hal.lib -LIBPATH:"$WDK_LIB"
 */

/* ── MSVC intrinsics (built-in, no header needed) ───────────── */
#pragma intrinsic(__readmsr, __writemsr)
#pragma intrinsic(__outdword, __indword)
#pragma intrinsic(memcpy)

/* ── Standalone NT types ─────────────────────────────────────── */
typedef unsigned long       ULONG;
typedef unsigned long long  ULONGLONG;
typedef unsigned short      USHORT;
typedef unsigned char       UCHAR;
typedef long                NTSTATUS;
typedef unsigned short      WCHAR;
typedef const WCHAR        *PCWSTR;
typedef WCHAR              *PWSTR;
typedef char               *PCHAR;
typedef void               *PVOID;
typedef ULONG              *PULONG;
typedef ULONG               ULONG_PTR;

/* ── Struct forward decls ────────────────────────────────────── */
typedef struct _DRIVER_OBJECT *PDRIVER_OBJECT;
typedef struct _UNICODE_STRING *PUNICODE_STRING;
typedef struct _DRIVER_OBJECT *PDRIVER_OBJECT;

/* Minimal DRIVER_OBJECT layout (x64):
   +0 Type(2) +2 Size(2) +4 Unused(4) +8 DriverStart(8) +16 DeviceObject(8)
   +24 DriverSection(8) +32 DriverExtension(8) +40 DriverName(12)
   +52 HardwareDatabase(8) +60 FastIoDispatch(8) +68 DriverInit(8) +76 DriverStartIo(8)
   +84 DriverUnload(8) +92 ...padding... +104 MajorFunction[28] (28*8) */
typedef struct _DRIVER_OBJECT { char _[312]; } DRIVER_OBJECT;

#define STATUS_SUCCESS                  ((NTSTATUS)0x00000000L)
#define STATUS_INVALID_PARAMETER        ((NTSTATUS)0xC000000DL)
#define STATUS_BUFFER_TOO_SMALL         ((NTSTATUS)0xC0000023L)
#define STATUS_INVALID_DEVICE_REQUEST   ((NTSTATUS)0xC0000010L)
#define STATUS_PRIVILEGED_INSTRUCTION   ((NTSTATUS)0xC0000096L)

/* ── ntoskrnl imports ─────────────────────────────────────────── */
__declspec(dllimport) NTSTATUS IoCreateDevice(PVOID, ULONG, void*, ULONG, ULONG, UCHAR, PVOID*);
__declspec(dllimport) void IoDeleteDevice(PVOID);
__declspec(dllimport) NTSTATUS IoCreateSymbolicLink(void*, void*);
__declspec(dllimport) NTSTATUS IoDeleteSymbolicLink(void*);
__declspec(dllimport) void IoCompleteRequest(PVOID, UCHAR);
__declspec(dllimport) void RtlInitUnicodeString(void*, PCWSTR);
__declspec(dllimport) void DbgPrint(PCSTR, ...);

/* ── IoGetCurrentIrpStackLocation is a wdm.h inline macro, not importable.
   Use this instead: Irp → IrpSp is at Irp + 0x80 (Tail.Overlay.CurrentStackLocation) on x64 */
#define IoGetCurrentIrpStackLocation(Irp) (*(PVOID*)((char*)(Irp) + 0x80))

/* ─── IOCTL ──────────────────────────────────────────────────── */
#define METHOD_BUFFERED    0
#define FILE_ANY_ACCESS    0
#define CTL_CODE(t,f) ((t) << 16 | (f) << 2 | FILE_ANY_ACCESS)

#define IOCTL_READ_MSR   CTL_CODE(0x8000, 0x800)
#define IOCTL_WRITE_MSR  CTL_CODE(0x8000, 0x801)
#define IOCTL_READ_PCI   CTL_CODE(0x8000, 0x802)
#define IOCTL_WRITE_PCI  CTL_CODE(0x8000, 0x803)

#pragma pack(push, 1)
typedef struct { ULONG Index; ULONGLONG Value; } MSR_RW;
typedef struct { ULONG Bus, Device, Function, Offset, Value; } PCI_RW;
#pragma pack(pop)

#define DEVICE_NAME  L"\\Device\\OmenXHub"
#define SYMLINK_NAME L"\\DosDevices\\OmenXHub"

/* ── IOCTL dispatch ───────────────────────────────────────────── */
NTSTATUS DriverIoctl(PVOID Irp) {
    NTSTATUS status = STATUS_INVALID_PARAMETER;
    PVOID stack = IoGetCurrentIrpStackLocation(Irp);
    /* stack layout (x64): +0 MajorFunction (1b), +4 IoControlCode, +8 InLen, +12 OutLen */
    ULONG code  = *(ULONG*)((char*)stack + 4);
    ULONG inLen = *(ULONG*)((char*)stack + 8);
    ULONG outLen = *(ULONG*)((char*)stack + 12);
    /* Irp layout (x64): +24 SystemBuffer, +56 IoStatus.Status, +64 IoStatus.Information */
    PVOID buf = *(PVOID*)((char*)Irp + 24);
    ULONG_PTR info = 0;

    __try {
        switch (code) {

        case IOCTL_READ_MSR:
            if (inLen < sizeof(MSR_RW) || outLen < sizeof(MSR_RW))
                { status = STATUS_BUFFER_TOO_SMALL; break; }
            { MSR_RW *r = (MSR_RW*)buf;
              ULONGLONG v = __readmsr(r->Index);
              r->Value = v;
              info = sizeof(MSR_RW); status = STATUS_SUCCESS; }
            break;

        case IOCTL_WRITE_MSR:
            if (inLen < sizeof(MSR_RW))
                { status = STATUS_BUFFER_TOO_SMALL; break; }
            { MSR_RW *r = (MSR_RW*)buf;
              __writemsr(r->Index, r->Value);
              status = STATUS_SUCCESS; }
            break;

        case IOCTL_READ_PCI:
            if (inLen < sizeof(PCI_RW) || outLen < sizeof(PCI_RW))
                { status = STATUS_BUFFER_TOO_SMALL; break; }
            { PCI_RW *r = (PCI_RW*)buf;
              if (r->Offset > 0xFF || (r->Offset & 3))
                  { status = STATUS_INVALID_PARAMETER; break; }
              ULONG addr = 0x80000000 | (r->Bus<<16) | (r->Device<<11)
                         | (r->Function<<8) | (r->Offset & 0xFC);
              __outdword(0xCF8, addr);
              r->Value = __indword(0xCFC);
              info = sizeof(PCI_RW); status = STATUS_SUCCESS; }
            break;

        case IOCTL_WRITE_PCI:
            if (inLen < sizeof(PCI_RW))
                { status = STATUS_BUFFER_TOO_SMALL; break; }
            { PCI_RW *r = (PCI_RW*)buf;
              if (r->Offset > 0xFF || (r->Offset & 3))
                  { status = STATUS_INVALID_PARAMETER; break; }
              ULONG addr = 0x80000000 | (r->Bus<<16) | (r->Device<<11)
                         | (r->Function<<8) | (r->Offset & 0xFC);
              __outdword(0xCF8, addr);
              __outdword(0xCFC, r->Value);
              status = STATUS_SUCCESS; }
            break;

        default:
            status = STATUS_INVALID_DEVICE_REQUEST;
            break;
        }
    } __except(1) {
        status = STATUS_PRIVILEGED_INSTRUCTION;
    }

    *(NTSTATUS*)((char*)Irp + 56) = status;
    *(ULONG_PTR*)((char*)Irp + 64) = info;
    IoCompleteRequest(Irp, 0);
    return status;
}

/* ── Create/Close dispatch ────────────────────────────────────── */
NTSTATUS DriverDispatch(PVOID Irp) {
    *(NTSTATUS*)((char*)Irp + 56) = STATUS_SUCCESS;
    *(ULONG_PTR*)((char*)Irp + 64) = 0;
    IoCompleteRequest(Irp, 0);
    return STATUS_SUCCESS;
}

/* ── Unload ───────────────────────────────────────────────────── */
void DriverUnload(PDRIVER_OBJECT drv) {
    /* DRIVER_OBJECT layout (x64): +0 Type(2) +2 Size(2) +8 DriverStart(8) +16 DeviceObject(8) */
    PVOID dev = *(PVOID*)((char*)drv + 16);
    if (dev) {
        /* DEVICE_OBJECT layout: +0 Type(2) +2 Size(2) +8 DriverObject(8) +16 NextDevice(8)
           +24 CurrentIrp(8) +32 Timer(8) +40 DeviceExtension(8) +48 Flags(4) */
        PVOID ext = *(PVOID*)((char*)dev + 40);
        /* DEVICE_EXTENSION: +0 DeviceObject(8) +8 SymLink.Length(2)+MaxLength(2)+Buffer(8) */
        PVOID symBuf = *(PVOID*)((char*)ext + 8 + 4);
        if (symBuf) IoDeleteSymbolicLink((char*)ext + 8);
        IoDeleteDevice(dev);
    }
    DbgPrint("[OmenXHubDrv] Unloaded\n");
}

/* ── DriverEntry ──────────────────────────────────────────────── */
NTSTATUS DriverEntry(void *drv, void *reg) {
    PVOID dev = 0;
    WCHAR devName[] = DEVICE_NAME;
    WCHAR symName[] = SYMLINK_NAME;
    char nameStr[12]; /* UNICODE_STRING = { Length(2), MaxLen(2), Buffer(8) } */
    char symStr[12];

    RtlInitUnicodeString(nameStr, devName);
    NTSTATUS s = IoCreateDevice(drv, 40, nameStr, 0x22, 0, 0, &dev);
    if (s < 0) { DbgPrint("[OmenXHubDrv] IoCreateDevice fail\n"); return s; }

    /* Store device extension (DEVICE_EXTENSION at DevObj+40) */
    PVOID ext = *(PVOID*)((char*)dev + 40);
    *(PVOID*)ext = dev;                             /* DeviceObject */
    RtlInitUnicodeString((char*)ext + 8, symName);  /* SymLink */
    s = IoCreateSymbolicLink((char*)ext + 8, nameStr);
    if (s < 0) { IoDeleteDevice(dev); return s; }

    /* Flag: DO_BUFFERED_IO (bit 2) */
    *(ULONG*)((char*)dev + 48) |= 0x00000004;

    /* MajorFunction table starts at DRIVER_OBJECT+104 (x64).
       Indices: 0=Create, 2=Close, 14=DeviceControl */
    PVOID *mf = (PVOID*)((char*)drv + 104);
    mf[0]  = (PVOID)DriverDispatch;
    mf[2]  = (PVOID)DriverDispatch;
    mf[14] = (PVOID)DriverIoctl;

    /* DriverUnload at DRIVER_OBJECT+96 (x64) */
    *(PVOID*)((char*)drv + 96) = (PVOID)DriverUnload;

    /* Clear DO_DEVICE_INITIALIZING */
    *(ULONG*)((char*)dev + 48) &= ~0x00000080;

    DbgPrint("[OmenXHubDrv] Loaded OK\n");
    return STATUS_SUCCESS;
}