@echo off
setlocal

echo Stenographer - Whisper Model Setup
echo ====================================

set MODELS_DIR=Models\whisper.cpp
set EXEC_URL=https://github.com/ggerganov/whisper.cpp/releases/download/v1.5.5/whisper-bin-x64.zip
set EXEC_ZIP=whisper-temp.zip
set MODEL_URL=https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-base.bin
set MODEL_FILE=%MODELS_DIR%\ggml-base.bin

if not exist "%MODELS_DIR%" (
    echo Creating %MODELS_DIR% ...
    mkdir "%MODELS_DIR%"
)

echo Downloading Whisper executable package...
powershell -Command "Invoke-WebRequest -Uri '%EXEC_URL%' -OutFile '%EXEC_ZIP%'" || goto :error

echo Extracting Whisper executable...
powershell -Command "Expand-Archive -Path '%EXEC_ZIP%' -DestinationPath '%MODELS_DIR%' -Force" || goto :error

del "%EXEC_ZIP%"

echo Downloading Whisper base model (ggml-base.bin)...
powershell -Command "Invoke-WebRequest -Uri '%MODEL_URL%' -OutFile '%MODEL_FILE%'" || goto :error

echo Whisper model setup completed successfully.
pause
exit /b 0

:error
echo.
echo Setup failed. Please check your internet connection and try again.
pause
exit /b 1
