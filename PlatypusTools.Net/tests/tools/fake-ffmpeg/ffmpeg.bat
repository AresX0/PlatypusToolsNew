@echo off
echo out_time_ms=0
timeout /t 1 >nul
echo out_time_ms=2500
timeout /t 1 >nul
echo out_time_ms=5000
timeout /t 1 >nul
echo out_time_ms=7500
timeout /t 1 >nul
echo out_time_ms=10000
exit /b 0
