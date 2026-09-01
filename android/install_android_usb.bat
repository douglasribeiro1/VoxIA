@echo off
chcp 65001 >nul
title Instalador VoxIA Android (USB/ADB)
echo ==========================================================
echo    VoxIA Android - Instalador via Depuracao USB
echo ==========================================================
echo.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0install_android_usb.ps1"
echo.
pause