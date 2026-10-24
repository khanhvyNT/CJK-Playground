@echo off
setlocal
cd /d "%~dp0"
echo ========================================================
echo   Dang bien dich CJK Playground Win32 Native Application
echo   Ngon ngu: C# Win32 / .NET Framework (Khong Web/React)
echo ========================================================

set CSC="C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist %CSC% (
    set CSC="C:\Windows\Microsoft.NET\Framework\v4.0.30319\csc.exe"
)

if not exist %CSC% (
    echo [LOI] Khong tim thay trinh bien dich csc.exe tren he thong Windows!
    exit /b 1
)

if not exist "bin" mkdir "bin"

echo Dang bien dich ma nguon va dong goi Single-File Resource...
%CSC% /nologo /target:winexe /optimize+ /unsafe /out:"bin\CJKPlayground.exe" ^
    /win32icon:app.ico ^
    /win32manifest:app.manifest ^
    /resource:app.ico,app.ico ^
    /resource:data\unihan-full.json,unihan-full.json ^
    /resource:data\HVDic.wrd,HVDic.wrd ^
    /resource:"data\All HAN IDS.TXT",All_HAN_IDS.TXT ^
    /r:System.dll ^
    /r:System.Drawing.dll ^
    /r:System.Windows.Forms.dll ^
    /r:System.Core.dll ^
    /r:System.Net.Http.dll ^
    /r:"C:\Windows\Microsoft.NET\Framework64\v4.0.30319\System.Web.Extensions.dll" ^
    /r:"C:\Windows\Microsoft.NET\Framework64\v4.0.30319\System.IO.Compression.dll" ^
    /r:"C:\Windows\Microsoft.NET\Framework64\v4.0.30319\System.IO.Compression.FileSystem.dll" ^
    "src\Models\*.cs" ^
    "src\Services\*.cs" ^
    "src\Forms\*.cs" ^
    "src\Tests\*.cs" ^
    "src\Program.cs"

if %ERRORLEVEL% EQU 0 (
    if not exist "bin\data" mkdir "bin\data"
    if exist "data\unihan-full.json" copy /y "data\unihan-full.json" "bin\data\unihan-full.json" >nul 2>&1
    if exist "data\HVDic.wrd" copy /y "data\HVDic.wrd" "bin\data\HVDic.wrd" >nul 2>&1
    if exist "data\All HAN IDS.TXT" copy /y "data\All HAN IDS.TXT" "bin\data\All HAN IDS.TXT" >nul 2>&1
    if exist "app.ico" copy /y "app.ico" "bin\app.ico" >nul 2>&1
    if exist "app.png" copy /y "app.png" "bin\app.png" >nul 2>&1

    echo.
    echo ========================================================
    echo   BIEN DICH THANH CONG!
    echo   File thuc thi duy nhat: bin\CJKPlayground.exe
    echo ========================================================
    echo.
    exit /b 0
) else (
    echo.
    echo [LOI] Qua trinh bien dich that bai voi ma loi %ERRORLEVEL%.
    exit /b %ERRORLEVEL%
)
