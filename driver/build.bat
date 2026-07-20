@echo off
call "D:\VS\Common7\Tools\VsDevCmd.bat" -arch=x64 -host_arch=x64 >nul 2>&1
cd /d "E:\Desktop\OmenXHub-main - 副本 (3) - 副本 - 副本\driver"
echo === Compiling OmenXHubDrv.c ===
cl.exe /c OmenXHubDrv.c /FoOmenXHubDrv.obj /GS- /W4 /WX /O2 /D_AMD64_ /D_WIN32_WINNT=0x0A00 /DDBG=0 /kernel /Zp8
if %ERRORLEVEL% NEQ 0 (
    echo === COMPILE FAILED ===
    exit /b 1
)
echo === Linking OmenXHubDrv.sys ===
link.exe /OUT:OmenXHubDrv.sys /NOLOGO /DRIVER /SUBSYSTEM:NATIVE /MACHINE:X64 /ENTRY:DriverEntry OmenXHubDrv.obj ntoskrnl.lib hal.lib
if %ERRORLEVEL% NEQ 0 (
    echo === LINK FAILED ===
    exit /b 1
)
echo === SUCCESS: OmenXHubDrv.sys built ===
dir OmenXHubDrv.sys
