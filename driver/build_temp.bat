@echo off
call "D:\VS\Common7\Tools\VsDevCmd.bat" -arch=x64 -host_arch=x64 >nul 2>&1

REM Create temp drive mapping to avoid Chinese path issues
subst Z: "E:\Desktop\OmenXHub-main - 副本 (3) - 副本 - 副本"

Z:
cd \driver
echo === Compiling ===
cl.exe /c OmenXHubDrv.c /FoOmenXHubDrv.obj /GS- /O2 /D_AMD64_ /D_WIN32_WINNT=0x0A00 /DDBG=0 /kernel /Zp8
if %ERRORLEVEL% NEQ 0 (
    echo === COMPILE FAILED ===
    subst Z: /d
    exit /b 1
)
echo === Linking ===
link.exe /OUT:OmenXHubDrv.sys /NOLOGO /DRIVER /SUBSYSTEM:NATIVE /MACHINE:X64 /ENTRY:DriverEntry OmenXHubDrv.obj ntoskrnl.lib hal.lib
if %ERRORLEVEL% NEQ 0 (
    echo === LINK FAILED ===
    subst Z: /d
    exit /b 1
)
echo === SUCCESS ===
dir OmenXHubDrv.sys
subst Z: /d
